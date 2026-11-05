using System.IO.Compression;
using Android.Graphics;
using Android.Graphics.Pdf;
using Avalonia.Platform.Storage;
using Path = System.IO.Path;

namespace Filee.Mobile;

/// <summary>
/// İlk Android prototipinin yerel dönüştürücüsü.
/// Orijinal Filee.Engines / JobQueue henüz bağlanmadı.
/// </summary>
internal static class MobileConversion
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp", ".bmp", ".heic", ".heif"
    };

    public static bool IsImage(string name) => ImageExtensions.Contains(Path.GetExtension(name));

    public static string[] AvailableFormats(IReadOnlyList<IStorageFile> inputs)
    {
        if (inputs.Count == 0) return Array.Empty<string>();
        return inputs.All(x => IsImage(x.Name))
            ? ["JPG", "PNG", "WEBP", "PDF", "ZIP"] : ["ZIP"];
    }

    public static string OutputFileName(string original, string format, int index = -1)
    {
        var stem = Path.GetFileNameWithoutExtension(original);
        var safe = string.Concat(stem.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_'));
        if (string.IsNullOrWhiteSpace(safe)) safe = "file";
        return $"{safe}_filee{(index < 0 ? "" : $"_{index + 1}")}.{format.ToLowerInvariant()}";
    }

    public static async Task ConvertImageAsync(IStorageFile input, IStorageFile output, string target)
    {
        if (!IsImage(input.Name)) throw new NotSupportedException("Bu dosya görüntü olarak açılamıyor.");

        using var bitmap = await Task.Run(async () =>
        {
            await using var boundsStream = await input.OpenReadAsync();
            using var bounds = new BitmapFactory.Options { InJustDecodeBounds = true };
            BitmapFactory.DecodeStream(boundsStream, null, bounds);

            var sample = 1;
            while (bounds.OutWidth / sample > 4096 || bounds.OutHeight / sample > 4096)
                sample *= 2;

            await using var imageStream = await input.OpenReadAsync();
            using var options = new BitmapFactory.Options { InSampleSize = sample };
            return BitmapFactory.DecodeStream(imageStream, null, options)
                ?? throw new InvalidDataException("Android görüntüyü açamadı; codec desteği cihaza bağlı.");
        });

        await using var destination = await output.OpenWriteAsync();
        await Task.Run(() =>
        {
            if (target == "PDF")
            {
                using var pdf = new PdfDocument();
                var pageInfo = new PdfDocument.PageInfo.Builder(bitmap.Width, bitmap.Height, 1).Create();
                var page = pdf.StartPage(pageInfo);
                page.Canvas!.DrawBitmap(bitmap, 0, 0, null);
                pdf.FinishPage(page);
                pdf.WriteTo(destination);
                return;
            }

            var codec = target switch
            {
                "JPG" => Bitmap.CompressFormat.Jpeg!,
                "PNG" => Bitmap.CompressFormat.Png!,
                "WEBP" => Bitmap.CompressFormat.WebpLossy!,
                _ => throw new NotSupportedException($"Bilinmeyen hedef: {target}")
            };

            // JPEG saydamlık desteklemez. Saydam arka planı beyaza düzleştir.
            if (target == "JPG" && bitmap.HasAlpha)
            {
                using var flattened = Bitmap.CreateBitmap(bitmap.Width, bitmap.Height, Bitmap.Config.Argb8888!);
                using var canvas = new Canvas(flattened);
                canvas.DrawColor(Color.White);
                canvas.DrawBitmap(bitmap, 0, 0, null);
                if (!flattened.Compress(codec, 90, destination))
                    throw new IOException("JPEG kodlanamadı.");
            }
            else if (!bitmap.Compress(codec, 90, destination))
                throw new IOException($"{target} kodlanamadı.");
        });
    }

    public static async Task CreateZipAsync(IReadOnlyList<IStorageFile> inputs, IStorageFile output)
    {
        await using var destination = await output.OpenWriteAsync();
        using var zip = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var input in inputs)
        {
            var baseName = Path.GetFileName(input.Name);
            if (string.IsNullOrWhiteSpace(baseName)) baseName = "file";
            var candidate = baseName;
            for (var i = 1; !names.Add(candidate); i++)
                candidate = $"{Path.GetFileNameWithoutExtension(baseName)}_{i}{Path.GetExtension(baseName)}";
            var entry = zip.CreateEntry(candidate, CompressionLevel.Optimal);
            await using var src = await input.OpenReadAsync();
            await using var dst = entry.Open();
            await src.CopyToAsync(dst);
        }
    }
}