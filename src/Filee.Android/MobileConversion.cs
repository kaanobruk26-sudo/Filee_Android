using System.IO.Compression;
using Android.Graphics;
using Android.Graphics.Pdf;
using Android.OS;
using Avalonia.Platform.Storage;
using PdfSharpDocument = PdfSharp.Pdf.PdfDocument;
using PdfSharp.Pdf.IO;
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
    public static bool IsJpeg(string name) =>
        Path.GetExtension(name).Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
        Path.GetExtension(name).Equals(".jpeg", StringComparison.OrdinalIgnoreCase);
    public static bool IsPdf(string name) => Path.GetExtension(name).Equals(".pdf", StringComparison.OrdinalIgnoreCase);

    public static string[] AvailableFormats(IReadOnlyList<IStorageFile> inputs)
    {
        if (inputs.Count == 0) return Array.Empty<string>();
        if (inputs.All(x => IsImage(x.Name)))
            return ["JPG", "PNG", "WEBP", "PDF", "ZIP"];
        if (inputs.All(x => IsPdf(x.Name)))
            return inputs.Count > 1
                ? ["JPG", "PNG", "BİRLEŞTİR", "AYIR", "ZIP"]
                : ["JPG", "PNG", "AYIR", "ZIP"];
        return ["ZIP"];
    }

    public static string OutputFileName(string original, string format, int index = -1)
    {
        var stem = Path.GetFileNameWithoutExtension(original);
        var safe = string.Concat(stem.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_'));
        if (string.IsNullOrWhiteSpace(safe)) safe = "file";
        return $"{safe}_filee{(index < 0 ? "" : $"_{index + 1}")}.{format.ToLowerInvariant()}";
    }

    public readonly record struct ImageOptions(
        int Quality, int MaxLongEdge, bool PreserveExif, bool ExactJpegCopy = false)
    {
        public static ImageOptions Default => new(92, 0, true, false);
    }

    // Safe, widely recognized EXIF fields only. GPS location, face regions,
    // maker notes and full proprietary metadata are not copied.
    private static readonly string[] ExifFields =
    [
        "Make", "Model", "DateTime", "DateTimeOriginal", "DateTimeDigitized",
        "ExposureTime", "FNumber", "ISOSpeedRatings", "PhotographicSensitivity",
        "FocalLength", "FocalLengthIn35mmFilm", "Flash", "WhiteBalance",
        "LensMake", "LensModel", "LensSpecification", "LensSerialNumber",
        "ExposureBiasValue", "MeteringMode", "ExposureProgram", "ExposureMode",
        "SubSecTime", "SubSecTimeOriginal", "LightSource",
        "Artist", "Copyright", "Software"
    ];

    private static string TempImagePath(string extension)
    {
        var dir = global::Android.App.Application.Context.CacheDir?.AbsolutePath
            ?? throw new IOException("Android önbelleğine ulaşılamıyor.");
        return Path.Combine(dir, "filee_img_" + Guid.NewGuid().ToString("N") + extension);
    }

    public static async Task ConvertImageAsync(
        IStorageFile input, IStorageFile output, string target, ImageOptions? settings = null)
    {
        if (!IsImage(input.Name))
            throw new NotSupportedException("Bu dosya bir görüntü değil.");
        var source = TempImagePath(Path.GetExtension(input.Name));
        var result = TempImagePath("." + target.ToLowerInvariant());
        try
        {
            await using (var inputStream = await input.OpenReadAsync())
            await using (var sourceStream = new FileStream(source, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                await inputStream.CopyToAsync(sourceStream);
            var options = settings ?? ImageOptions.Default;
            if (options.ExactJpegCopy && !IsExactJpegCopy(input.Name, target, options))
                throw new InvalidOperationException(
                    "Kayıpsız kopya yalnızca JPG → JPG, orijinal boyut ve EXIF koruma açıkken kullanılabilir.");
            if (IsExactJpegCopy(input.Name, target, options))
                await Task.Run(() => CopyJpegExactly(source, result));
            else
                await Task.Run(() => ConvertLocalImage(source, result, target, options));
            await using var outputStream = await output.OpenWriteAsync();
            await using var resultStream = File.OpenRead(result);
            await resultStream.CopyToAsync(outputStream);
            await outputStream.FlushAsync();
        }
        finally
        {
            if (File.Exists(source)) File.Delete(source);
            if (File.Exists(result)) File.Delete(result);
        }
    }

    public static bool IsExactJpegCopy(string name, string target, ImageOptions opts) =>
        opts.ExactJpegCopy && target == "JPG" && IsJpeg(name) &&
        opts.MaxLongEdge == 0 && opts.PreserveExif;

    private static void CopyJpegExactly(string input, string output)
    {
        // This is a byte-for-byte archival copy, not a JPEG transcoding step.
        using (var source = File.OpenRead(input))
        {
            if (source.Length < 4 || source.ReadByte() != 0xFF || source.ReadByte() != 0xD8)
                throw new InvalidDataException("Kaynak dosya geçerli bir JPEG değil.");
        }
        using var dimensions = new BitmapFactory.Options { InJustDecodeBounds = true };
        BitmapFactory.DecodeFile(input, dimensions);
        if (dimensions.OutWidth <= 0 || dimensions.OutHeight <= 0)
            throw new InvalidDataException("Kaynak JPEG okunamadı.");
        File.Copy(input, output, overwrite: false);
        global::Android.Util.Log.Info("FileeImage",
            $"Byte-exact JPEG copied; {new FileInfo(output).Length} bytes.");
    }

    private static int ReadOrientation(string source)
    {
        try
        {
            using var exif = new global::Android.Media.ExifInterface(source);
            return exif.GetAttributeInt("Orientation", 1);
        }
        catch (Exception e)
        {
            global::Android.Util.Log.Warn("FileeImage", "EXIF orientation unreadable: " + e.Message);
            return 1;
        }
    }

    private static Bitmap ApplyOrientation(Bitmap bitmap, int orientation)
    {
        if (orientation <= 1 || orientation > 8) return bitmap;
        using var matrix = new global::Android.Graphics.Matrix();
        switch (orientation)
        {
            case 2: matrix.SetScale(-1, 1); break;
            case 3: matrix.SetRotate(180); break;
            case 4: matrix.SetScale(1, -1); break;
            case 5: matrix.SetRotate(90); matrix.PostScale(-1, 1); break;
            case 6: matrix.SetRotate(90); break;
            case 7: matrix.SetRotate(270); matrix.PostScale(-1, 1); break;
            case 8: matrix.SetRotate(270); break;
        }
        return Bitmap.CreateBitmap(bitmap, 0, 0, bitmap.Width, bitmap.Height, matrix, true)
            ?? throw new InvalidDataException("Fotoğrafın yönü düzeltilemedi.");
    }

    private static void CopySafeExif(string source, string target)
    {
        try
        {
            using var src = new global::Android.Media.ExifInterface(source);
            using var dst = new global::Android.Media.ExifInterface(target);
            int copied = 0;
            foreach (var tag in ExifFields)
            {
                try
                {
                    var value = src.GetAttribute(tag);
                    if (string.IsNullOrWhiteSpace(value)) continue;
                    dst.SetAttribute(tag, value);
                    copied++;
                }
                catch (Exception e)
                {
                    // One unsupported maker-specific field should not prevent
                    // all other standard EXIF fields from being saved.
                    global::Android.Util.Log.Warn("FileeImage",
                        $"EXIF field {tag} not supported on this device: {e.Message}");
                }
            }
            // Pixels were physically rotated before encoding.
            dst.SetAttribute("Orientation", "1");
            dst.SaveAttributes();
            global::Android.Util.Log.Info("FileeImage", $"Preserved {copied} standard EXIF fields (no GPS).");
        }
        catch (Exception e)
        {
            // Invalid or unsupported metadata must not silently destroy an
            // otherwise successfully encoded photograph.
            global::Android.Util.Log.Warn("FileeImage", "Partial/unsupported EXIF: " + e);
        }
    }

    private static void ConvertLocalImage(string input, string output, string target, ImageOptions opts)
    {
        if (target is not ("JPG" or "PNG" or "WEBP"))
            throw new NotSupportedException("Görsel hedefi desteklenmiyor: " + target);
        if (opts.Quality is < 60 or > 100 || opts.MaxLongEdge < 0)
            throw new ArgumentOutOfRangeException(nameof(opts));

        using var bounds = new BitmapFactory.Options { InJustDecodeBounds = true, InScaled = false };
        BitmapFactory.DecodeFile(input, bounds);
        int width = bounds.OutWidth;
        int height = bounds.OutHeight;
        if (width <= 0 || height <= 0)
            throw new InvalidDataException("Android bu görüntü biçimini çözemedi.");
        if (opts.MaxLongEdge == 0 && (long)width * height > 60_000_000L)
            throw new InvalidDataException("Fotoğraf 60 MP üzerinde. Boyutu 8192, 4096 veya 2048 olarak seçin.");

        int maxSide = Math.Max(width, height);
        int requested = opts.MaxLongEdge <= 0 ? maxSide : Math.Min(maxSide, opts.MaxLongEdge);
        int sample = 1;
        while (maxSide / (sample * 2) >= requested && sample < 64)
            sample *= 2;

        using var decodeOptions = new BitmapFactory.Options { InSampleSize = sample, InScaled = false };
        Bitmap current;
        try
        {
            current = BitmapFactory.DecodeFile(input, decodeOptions)
                ?? throw new InvalidDataException("Görüntü çözümlenemedi.");
        }
        catch (OutOfMemoryException)
        {
            throw new InvalidDataException("Bellek yetersiz. Daha küçük çözünürlük seçin.");
        }

        try
        {
            int orientation = ReadOrientation(input);
            if (orientation != 1)
            {
                var corrected = ApplyOrientation(current, orientation);
                if (!ReferenceEquals(current, corrected))
                {
                    current.Dispose();
                    current = corrected;
                }
            }

            int currentLong = Math.Max(current.Width, current.Height);
            if (opts.MaxLongEdge > 0 && currentLong > opts.MaxLongEdge)
            {
                double ratio = (double)opts.MaxLongEdge / currentLong;
                var scaled = Bitmap.CreateScaledBitmap(current,
                    Math.Max(1, (int)Math.Round(current.Width * ratio)),
                    Math.Max(1, (int)Math.Round(current.Height * ratio)), true)
                    ?? throw new OutOfMemoryException("Görüntü ölçeklenemedi.");
                if (!ReferenceEquals(current, scaled))
                {
                    current.Dispose();
                    current = scaled;
                }
            }

            var codec = target switch
            {
                "JPG" => Bitmap.CompressFormat.Jpeg!,
                "PNG" => Bitmap.CompressFormat.Png!,
                _ => Bitmap.CompressFormat.WebpLossy!
            };

            using (var dest = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                if (target == "JPG" && current.HasAlpha)
                {
                    using var flattened = Bitmap.CreateBitmap(current.Width, current.Height, Bitmap.Config.Argb8888!)
                        ?? throw new OutOfMemoryException("Arka plan oluşturulamadı.");
                    using var canvas = new Canvas(flattened);
                    canvas.DrawColor(Color.White);
                    canvas.DrawBitmap(current, 0, 0, null);
                    if (!flattened.Compress(codec, opts.Quality, dest))
                        throw new IOException("JPEG kaydedilemedi.");
                }
                else if (!current.Compress(codec, opts.Quality, dest))
                    throw new IOException(target + " kaydedilemedi.");
                dest.Flush();
            }

            if (opts.PreserveExif)
                CopySafeExif(input, output);

            // Check the encoded image locally before it is copied to SAF.
            using var verified = new BitmapFactory.Options { InJustDecodeBounds = true, InScaled = false };
            BitmapFactory.DecodeFile(output, verified);
            if (verified.OutWidth != current.Width || verified.OutHeight != current.Height)
                throw new InvalidDataException("Oluşturulan görüntünün boyutları doğrulanamadı.");
            global::Android.Util.Log.Info("FileeImage",
                $"Converted {width}x{height} to {verified.OutWidth}x{verified.OutHeight}, {target}");
        }
        finally
        {
            current.Dispose();
        }
    }

    public static async Task RunImageEngineSelfTestAsync()
    {
        var input = TempImagePath(".jpg");
        var jpg = TempImagePath(".jpg");
        var png = TempImagePath(".png");
        var webp = TempImagePath(".webp");
        var originalCopy = TempImagePath(".jpg");
        try
        {
            using (var b = Bitmap.CreateBitmap(240, 120, Bitmap.Config.Argb8888!))
            using (var stream = new FileStream(input, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                b.EraseColor(Color.Green);
                if (!b.Compress(Bitmap.CompressFormat.Jpeg!, 90, stream))
                    throw new IOException("Örnek JPEG üretilemedi.");
            }
            using (var exif = new global::Android.Media.ExifInterface(input))
            {
                exif.SetAttribute("Orientation", "6");
                exif.SetAttribute("Make", "FileeTest");
                exif.SetAttribute("Model", "AndroidEmulator");
                exif.SaveAttributes();
            }

            await Task.Run(() =>
            {
                // A no-reencode JPEG must preserve every byte, including
                // MakerNotes, ICC, GPS when present, and 4:2:2 subsampling.
                CopyJpegExactly(input, originalCopy);
                using (var src = File.OpenRead(input))
                using (var dst = File.OpenRead(originalCopy))
                {
                    var a = System.Security.Cryptography.SHA256.HashData(src);
                    var b = System.Security.Cryptography.SHA256.HashData(dst);
                    if (!a.AsSpan().SequenceEqual(b))
                        throw new InvalidDataException("Tam JPG kopyasının SHA-256 değeri eşleşmedi.");
                }
                using (var originalExif = new global::Android.Media.ExifInterface(originalCopy))
                {
                    if (originalExif.GetAttributeInt("Orientation", 0) != 6 ||
                        originalExif.GetAttribute("Make") != "FileeTest")
                        throw new InvalidDataException("Tam JPEG kopyasında EXIF değişti.");
                }
                ConvertLocalImage(input, jpg, "JPG", new ImageOptions(95, 0, true));
                using (var e = new global::Android.Media.ExifInterface(jpg))
                {
                    if (e.GetAttribute("Make") != "FileeTest" || e.GetAttributeInt("Orientation", 0) != 1)
                        throw new InvalidDataException("JPEG EXIF korunamadı.");
                }
                AssertSize(jpg, 120, 240);
                ConvertLocalImage(input, png, "PNG", new ImageOptions(90, 160, false));
                AssertSize(png, 80, 160);
                ConvertLocalImage(input, webp, "WEBP", new ImageOptions(77, 120, false));
                AssertSize(webp, 60, 120);
            });
            global::Android.Util.Log.Info("FileeImageTest", "PASS: JPEG byte-exact SHA256; JPEG EXIF and rotation; PNG 80x160; WEBP 60x120");
        }
        catch (Exception e)
        {
            global::Android.Util.Log.Error("FileeImageTest", "FAIL: " + e);
        }
        finally
        {
            foreach (var f in new[] { input, jpg, png, webp, originalCopy })
                if (File.Exists(f)) File.Delete(f);
        }
    }

    private static void AssertSize(string image, int width, int height)
    {
        using var options = new BitmapFactory.Options { InJustDecodeBounds = true };
        BitmapFactory.DecodeFile(image, options);
        if (options.OutWidth != width || options.OutHeight != height)
            throw new InvalidDataException(
                $"Expected {width}x{height}, got {options.OutWidth}x{options.OutHeight}");
    }


    /// <summary>
    /// Renders each page of one or more PDFs into JPG/PNG files in a folder
    /// selected via Android's Storage Access Framework.
    /// This is a rasterization, not an editable document conversion.
    /// </summary>
    public static async Task<int> ConvertPdfPagesAsync(
        IReadOnlyList<IStorageFile> inputs,
        IStorageFolder destination,
        string target,
        IProgress<string>? progress = null)
    {
        if (target is not ("JPG" or "PNG"))
            throw new NotSupportedException("PDF sayfaları yalnızca JPG veya PNG olarak kaydedilebilir.");
        if (inputs.Count == 0 || !inputs.All(x => IsPdf(x.Name)))
            throw new InvalidDataException("Yalnızca PDF dosyaları seçin.");

        return await Task.Run(async () =>
        {
            int total = 0;
            string batchId = Guid.NewGuid().ToString("N")[..6];
            for (int fileIndex = 0; fileIndex < inputs.Count; fileIndex++)
            {
                var input = inputs[fileIndex];
                var cacheDir = global::Android.App.Application.Context.CacheDir?.AbsolutePath
                    ?? throw new IOException("Android önbellek klasörüne erişilemiyor.");
                var temporary = Path.Combine(cacheDir, $"filee_{Guid.NewGuid():N}.pdf");
                try
                {
                    // PdfRenderer needs a seekable descriptor. A SAF content URI
                    // may be a pipe, so copy it to a private cache file first.
                    await using (var inputStream = await input.OpenReadAsync())
                    await using (var cached = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        var buffer = new byte[131072];
                        long totalBytes = 0;
                        int read;
                        while ((read = await inputStream.ReadAsync(buffer)) > 0)
                        {
                            totalBytes += read;
                            if (totalBytes > 200L * 1024 * 1024)
                                throw new InvalidDataException("PDF 200 MB sınırını aşıyor.");
                            await cached.WriteAsync(buffer.AsMemory(0, read));
                        }
                    }

                    // The renderer takes ownership of the ParcelFileDescriptor.
                    using var fd = ParcelFileDescriptor.Open(
                        new Java.IO.File(temporary), ParcelFileMode.ReadOnly)
                        ?? throw new IOException("PDF okunamıyor.");
                    using var renderer = new PdfRenderer(fd);
                    if (renderer.PageCount > 300)
                        throw new InvalidDataException("Bu prototip bir PDF için en fazla 300 sayfayı destekler.");

                    var stem = Path.GetFileNameWithoutExtension(input.Name);
                    var safe = string.Concat(stem.Select(c =>
                        char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_'));
                    if (string.IsNullOrWhiteSpace(safe)) safe = "pdf";

                    for (int pageIndex = 0; pageIndex < renderer.PageCount; pageIndex++)
                    {
                        using var page = renderer.OpenPage(pageIndex);
                        var scale = Math.Min(2.0, 3200.0 / Math.Max(page.Width, page.Height));
                        var w = Math.Max(1, (int)Math.Round(page.Width * scale));
                        var h = Math.Max(1, (int)Math.Round(page.Height * scale));
                        using var bitmap = Bitmap.CreateBitmap(w, h, Bitmap.Config.Argb8888!)
                            ?? throw new OutOfMemoryException("PDF sayfa görüntüsü oluşturulamadı.");
                        bitmap.EraseColor(Color.White);
                        page.Render(bitmap, null, null, PdfRenderMode.ForDisplay);
                        var resultName = $"{safe}_{batchId}_sayfa_{pageIndex + 1:000}.{target.ToLowerInvariant()}";
                        progress?.Report($"{fileIndex + 1}/{inputs.Count} PDF • {pageIndex + 1}/{renderer.PageCount} sayfa");
                        var result = await destination.CreateFileAsync(resultName)
                            ?? throw new IOException("Dosya oluşturulamadı: " + resultName);
                        await using var outStream = await result.OpenWriteAsync();
                        var format = target == "JPG" ? Bitmap.CompressFormat.Jpeg! : Bitmap.CompressFormat.Png!;
                        if (!bitmap.Compress(format, 92, outStream))
                            throw new IOException("Sayfa kaydedilemedi: " + resultName);
                        await outStream.FlushAsync();
                        total++;
                    }
                }
                finally
                {
                    if (System.IO.File.Exists(temporary))
                        System.IO.File.Delete(temporary);
                }
            }
            return total;
        });
    }


    /// <summary>
    /// Export a SAF PDF to app-private seekable storage with a fixed 200 MiB limit.
    /// PDFsharp can read the resulting local file without relying on seekable SAF streams.
    /// </summary>
    private static async Task<string> CachePdfAsync(IStorageFile input)
    {
        var cacheDir = global::Android.App.Application.Context.CacheDir?.AbsolutePath
            ?? throw new IOException("Android önbellek klasörü bulunamadı.");
        var filePath = Path.Combine(cacheDir, "filee_" + Guid.NewGuid().ToString("N") + ".pdf");
        try
        {
            await using var source = await input.OpenReadAsync();
            await using var dest = new FileStream(filePath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            var buffer = new byte[131072];
            long total = 0;
            int read;
            while ((read = await source.ReadAsync(buffer.AsMemory())) > 0)
            {
                total += read;
                if (total > 200L * 1024 * 1024)
                    throw new InvalidDataException("PDF 200 MB sınırını aşıyor.");
                await dest.WriteAsync(buffer.AsMemory(0, read));
            }
            return filePath;
        }
        catch
        {
            if (File.Exists(filePath)) File.Delete(filePath);
            throw;
        }
    }

    /// <summary>
    /// PDFsharp's writer requires a seekable stream; Android SAF output streams
    /// may be non-seekable. Save to app-private cache then copy the result.
    /// </summary>
    private static async Task SavePdfSafAsync(PdfSharpDocument pdf, IStorageFile output)
    {
        var cacheDir = global::Android.App.Application.Context.CacheDir?.AbsolutePath
            ?? throw new IOException("Android önbellek klasörü bulunamadı.");
        var temporary = Path.Combine(cacheDir, "filee_out_" + Guid.NewGuid().ToString("N") + ".pdf");
        try
        {
            await Task.Run(() => pdf.Save(temporary));
            await using var source = new FileStream(temporary, FileMode.Open, FileAccess.Read, FileShare.Read);
            await using var destination = await output.OpenWriteAsync();
            await source.CopyToAsync(destination);
            await destination.FlushAsync();
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    /// <summary>
    /// Save to a correctly typed Android PDF document URI. Android's PdfRenderer
    /// confirms the resulting PDF is actually openable before reporting success.
    /// </summary>
    private static async Task SavePdfToNativeUriAsync(PdfSharpDocument pdf,
        global::Android.Net.Uri output, int expectedPages)
    {
        var context = global::Android.App.Application.Context;
        var cacheDir = context.CacheDir?.AbsolutePath
            ?? throw new IOException("Android önbellek klasörüne ulaşılamıyor.");
        var temp = Path.Combine(cacheDir, "filee_pdf_" + Guid.NewGuid().ToString("N") + ".pdf");
        var verifyTemp = Path.Combine(cacheDir, "filee_verify_" + Guid.NewGuid().ToString("N") + ".pdf");
        try
        {
            await Task.Run(() => pdf.Save(temp));

            // Validate the generated local PDF before writing to the user's file.
            VerifyPdfWithAndroidRenderer(temp, expectedPages);

            var resolver = context.ContentResolver
                ?? throw new IOException("Android dosya sağlayıcısına ulaşılamıyor.");
            using (var dest = resolver.OpenOutputStream(output, "wt")
                ?? throw new IOException("PDF yazma izni alınamadı."))
            await using (var source = File.OpenRead(temp))
            {
                await source.CopyToAsync(dest);
                await dest.FlushAsync();
            }

            // Read the user-visible output again, not merely the private temp
            // file, to catch empty, truncated or corrupted SAF documents.
            using (var written = resolver.OpenInputStream(output)
                ?? throw new IOException("Kaydedilen PDF okunamıyor."))
            await using (var verify = new FileStream(verifyTemp, FileMode.CreateNew,
                FileAccess.Write, FileShare.None))
            {
                await written.CopyToAsync(verify);
                await verify.FlushAsync();
            }
            VerifyPdfWithAndroidRenderer(verifyTemp, expectedPages);

            var mime = resolver.GetType(output);
            global::Android.Util.Log.Info("FileePdfSave",
                $"PDF verified: pages={expectedPages}, bytes={new FileInfo(verifyTemp).Length}, mime={mime}");
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
            if (File.Exists(verifyTemp)) File.Delete(verifyTemp);
        }
    }

    private static void VerifyPdfWithAndroidRenderer(string filePath, int expectedPages)
    {
        if (new FileInfo(filePath).Length < 100)
            throw new InvalidDataException("Kaydedilen PDF boş veya eksik.");
        using var fd = ParcelFileDescriptor.Open(new Java.IO.File(filePath), ParcelFileMode.ReadOnly)
            ?? throw new IOException("PDF doğrulama için açılamadı.");
        using var renderer = new PdfRenderer(fd);
        if (renderer.PageCount != expectedPages)
            throw new InvalidDataException(
                $"PDF doğrulanamadı: {expectedPages} sayfa bekleniyordu, {renderer.PageCount} bulundu.");
        if (expectedPages > 0)
        {
            using var page = renderer.OpenPage(0);
            using var bitmap = Bitmap.CreateBitmap(64, 64, Bitmap.Config.Argb8888!)
                ?? throw new OutOfMemoryException("PDF kontrol görüntüsü oluşturulamadı.");
            bitmap.EraseColor(Color.White);
            page.Render(bitmap, null, null, PdfRenderMode.ForDisplay);
        }
    }

    /// <summary>
    /// An Android-only PDFsharp regression probe with synthetic documents.
    /// It never accesses or modifies the user's documents.
    /// </summary>
    public static void RunPdfEngineSelfTest()
    {
        var cacheDir = global::Android.App.Application.Context.CacheDir?.AbsolutePath
            ?? throw new IOException("Android önbellek klasörü bulunamadı.");
        var tmp = Path.Combine(cacheDir, "filee_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var a = Path.Combine(tmp, "a.pdf");
            var b = Path.Combine(tmp, "b.pdf");
            var mergedPath = Path.Combine(tmp, "merged.pdf");
            var splitPath = Path.Combine(tmp, "split.pdf");
            using (var pdf = new PdfSharpDocument())
            {
                pdf.AddPage();
                pdf.Save(a);
            }
            using (var pdf = new PdfSharpDocument())
            {
                pdf.AddPage();
                pdf.AddPage();
                pdf.Save(b);
            }
            using (var first = PdfReader.Open(a, PdfDocumentOpenMode.Import))
            using (var second = PdfReader.Open(b, PdfDocumentOpenMode.Import))
            using (var merged = new PdfSharpDocument())
            {
                foreach (var page in first.Pages) merged.AddPage(page);
                foreach (var page in second.Pages) merged.AddPage(page);
                if (merged.PageCount != 3)
                    throw new InvalidOperationException("PDF birleştirme sayfa sayısı yanlış.");
                merged.Save(mergedPath);
            }
            using (var checkedMerge = PdfReader.Open(mergedPath, PdfDocumentOpenMode.Import))
            using (var split = new PdfSharpDocument())
            {
                if (checkedMerge.PageCount != 3)
                    throw new InvalidDataException("Kaydedilen birleşik PDF açılamadı.");
                split.AddPage(checkedMerge.Pages[1]);
                split.Save(splitPath);
            }
            VerifyPdfWithAndroidRenderer(mergedPath, 3);
            VerifyPdfWithAndroidRenderer(splitPath, 1);
            using var checkedSplit = PdfReader.Open(splitPath, PdfDocumentOpenMode.Import);
            if (checkedSplit.PageCount != 1)
                throw new InvalidDataException("Kaydedilen ayrılmış PDF doğrulanamadı.");
            global::Android.Util.Log.Info("FileePdfTest", "PASS: merge 1+2=3 pages; split page=1");
        }
        catch (Exception e)
        {
            global::Android.Util.Log.Error("FileePdfTest", "FAIL: " + e);
        }
        finally
        {
            if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
        }
    }

    /// <summary>Merge PDFs in the order selected, without rasterizing their pages.</summary>
    public static async Task<int> MergePdfsAsync(IReadOnlyList<IStorageFile> inputs, global::Android.Net.Uri output, IProgress<string>? progress = null)
    {
        if (inputs.Count < 2 || !inputs.All(x => IsPdf(x.Name)))
            throw new InvalidDataException("Birleştirmek için en az iki PDF seçilmelidir.");

        var cached = new List<string>();
        var opened = new List<PdfSharpDocument>();
        try
        {
            using var merged = new PdfSharpDocument();
            for (int i = 0; i < inputs.Count; i++)
            {
                progress?.Report($"PDF birleştiriliyor: {i + 1}/{inputs.Count}");
                var path = await CachePdfAsync(inputs[i]);
                cached.Add(path);
                var original = PdfReader.Open(path, PdfDocumentOpenMode.Import);
                opened.Add(original); // Keep imports alive until the output is written.
                if (original.PageCount > 300)
                    throw new InvalidDataException("PDF başına en fazla 300 sayfa destekleniyor.");
                foreach (var page in original.Pages)
                    merged.AddPage(page);
            }
            int pageCount = merged.PageCount;
            progress?.Report("PDF kaydediliyor…");
            await SavePdfToNativeUriAsync(merged, output, pageCount);
            return pageCount;
        }
        finally
        {
            foreach (var document in opened) document.Dispose();
            foreach (var path in cached)
                if (File.Exists(path)) File.Delete(path);
        }
    }

    /// <summary>Split a PDF into one lossless PDF file per page.</summary>
    public static async Task<int> SplitPdfsAsync(
        IReadOnlyList<IStorageFile> inputs,
        IStorageFolder destination,
        IProgress<string>? progress = null)
    {
        if (inputs.Count == 0 || !inputs.All(x => IsPdf(x.Name)))
            throw new InvalidDataException("Ayırmak için PDF dosyası seçilmelidir.");

        int total = 0;
        var batchId = Guid.NewGuid().ToString("N")[..6];
        for (int inputIndex = 0; inputIndex < inputs.Count; inputIndex++)
        {
            var path = await CachePdfAsync(inputs[inputIndex]);
            try
            {
                using var original = PdfReader.Open(path, PdfDocumentOpenMode.Import);
                if (original.PageCount > 300)
                    throw new InvalidDataException("PDF başına en fazla 300 sayfa destekleniyor.");
                var stem = Path.GetFileNameWithoutExtension(inputs[inputIndex].Name);
                var safe = string.Concat(stem.Select(c =>
                    char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_'));
                if (string.IsNullOrWhiteSpace(safe)) safe = "pdf";

                for (int pageIndex = 0; pageIndex < original.PageCount; pageIndex++)
                {
                    progress?.Report($"PDF ayrılıyor: {inputIndex + 1}/{inputs.Count} • {pageIndex + 1}/{original.PageCount}");
                    using var single = new PdfSharpDocument();
                    single.AddPage(original.Pages[pageIndex]);
                    var fileName = $"{safe}_{batchId}_sayfa_{pageIndex + 1:000}.pdf";
                    var output = await destination.CreateFileAsync(fileName)
                        ?? throw new IOException("Sayfa dosyası oluşturulamadı: " + fileName);
                    await SavePdfSafAsync(single, output);
                    total++;
                }
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }
        return total;
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