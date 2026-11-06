using System.Security.Cryptography;
using Android.Content;
using Android.Provider;
using Avalonia.Platform.Storage;

namespace Filee.Mobile;

/// <summary>
/// For archival JPEG copies request unredacted bytes from Android's media provider.
/// A redacted stream must never silently be labeled "byte-exact original".
/// </summary>
internal static class OriginalMediaAccess
{
    private static readonly string PermissionName = global::Android.Manifest.Permission.AccessMediaLocation;

    private static global::Android.Net.Uri? ResolveMediaUri(IStorageFile file)
    {
        var source = global::Android.Net.Uri.Parse(file.Path.ToString())
            ?? throw new InvalidDataException("Kaynak dosya adresi belirlenemedi.");
        if (source.Scheme == "file") return source;
        if (source.Scheme != "content") return null;

        // MediaStore from media pickers.
        if (source.Authority == "media") return source;

        // SAF often returns content://com.android.providers.media.documents/document/image:ID.
        if (source.Authority == "com.android.providers.media.documents")
        {
            var id = DocumentsContract.GetDocumentId(source);
            if (id is not null && id.StartsWith("image:", StringComparison.OrdinalIgnoreCase) &&
                long.TryParse(id["image:".Length..], out var imageId))
            {
                return ContentUris.WithAppendedId(MediaStore.Images.Media.ExternalContentUri!, imageId);
            }
        }
        return null;
    }

    public static bool IsSupportedOriginalSource(IStorageFile file) =>
        ResolveMediaUri(file) is not null;

    public static string SourceAuthority(IStorageFile file)
    {
        var source = global::Android.Net.Uri.Parse(file.Path.ToString());
        return source?.Authority ?? (source?.Scheme ?? "bilinmiyor");
    }

    public static async Task EnsureOriginalAccessAsync(IStorageFile file)
    {
        var mediaUri = ResolveMediaUri(file);
        if (mediaUri is null)
            throw new NotSupportedException(
                "Bu dosya sağlayıcısı (" + SourceAuthority(file) +
                ") tam EXIF kopyalamayı desteklemiyor. " +
                "Ana ekrandaki 'Galeriden özgün JPG seç' düğmesini kullanın.");
        if (mediaUri.Scheme == "file") return; // Direct filesystem access is not MediaStore-redacted.
        var activity = MainActivity.Current ??
            throw new InvalidOperationException("Android etkinliği bulunamadı.");
        if (!await activity.RequestMediaLocationAccessAsync())
            throw new UnauthorizedAccessException(
                "Tam EXIF kopyası için 'Medya konumu' iznini onaylayın. " +
                "İzin verilmezse standart dönüştürmeyi kullanabilirsiniz.");
    }

    public static async Task CopyUnredactedAsync(IStorageFile file, string destinationPath)
    {
        await EnsureOriginalAccessAsync(file);
        var mediaUri = ResolveMediaUri(file)
            ?? throw new NotSupportedException("Kaynak medya dosyası bulunamadı.");
        if (mediaUri.Scheme == "file")
        {
            await using var src = await file.OpenReadAsync();
            await using var dst = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await src.CopyToAsync(dst);
            return;
        }
        await Task.Run(() => CopyRequireOriginal(mediaUri, destinationPath));
    }

    private static void CopyRequireOriginal(global::Android.Net.Uri mediaUri, string destination)
    {
        var resolver = global::Android.App.Application.Context.ContentResolver
            ?? throw new IOException("Android medya sağlayıcısına erişilemiyor.");
        var required = MediaStore.SetRequireOriginal(mediaUri);
        try
        {
            // The original flag is documented for OpenFileDescriptor, not for
            // generic streams. Do not fall back to a potentially redacted URI.
            var fd = resolver.OpenFileDescriptor(required, "r")
                ?? throw new IOException("Özgün dosya tanımlayıcısı açılamadı.");
            using var stream = new global::Android.OS.ParcelFileDescriptor.AutoCloseInputStream(fd);
            using var dst = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            var buffer = new byte[131072];
            int n;
            while ((n = stream.Read(buffer, 0, buffer.Length)) != -1)
            {
                if (n > 0) dst.Write(buffer, 0, n);
            }
            dst.Flush();
            global::Android.Util.Log.Info("FileeOriginal", "Original media read via MediaStore: " + dst.Length);
        }
        catch (Java.Lang.UnsupportedOperationException e)
        {
            throw new NotSupportedException("Android özgün fotoğraf verisini sağlamıyor: " + e.Message, e);
        }
        catch (Java.Lang.SecurityException e)
        {
            throw new UnauthorizedAccessException("Medya konumu izni veya fotoğraf erişimi engellendi.", e);
        }
    }

    public static async Task RunOriginalMediaSelfTestAsync()
    {
        var context = global::Android.App.Application.Context;
        var resolver = context.ContentResolver ??
            throw new InvalidOperationException("ContentResolver bulunamadı.");
        var local = Path.Combine(context.CacheDir!.AbsolutePath, "filee_original_" + Guid.NewGuid().ToString("N") + ".jpg");
        var copied = Path.Combine(context.CacheDir!.AbsolutePath, "filee_copied_" + Guid.NewGuid().ToString("N") + ".jpg");
        global::Android.Net.Uri? uri = null;
        try
        {
            using (var bitmap = global::Android.Graphics.Bitmap.CreateBitmap(
                128, 96, global::Android.Graphics.Bitmap.Config.Argb8888!))
            using (var stream = File.Create(local))
            {
                bitmap.EraseColor(global::Android.Graphics.Color.Green);
                if (!bitmap.Compress(global::Android.Graphics.Bitmap.CompressFormat.Jpeg!, 92, stream))
                    throw new IOException("Test JPEG üretilemedi.");
            }
            using (var exif = new global::Android.Media.ExifInterface(local))
            {
                exif.SetAttribute("Make", "FileeOriginalTest");
                exif.SetAttribute("GPSVersionID", "2.3.0.0");
                exif.SaveAttributes();
            }
            using var values = new ContentValues();
            values.Put(MediaStore.IMediaColumns.DisplayName, "filee_test_" + Guid.NewGuid().ToString("N") + ".jpg");
            values.Put(MediaStore.IMediaColumns.MimeType, "image/jpeg");
            values.Put(MediaStore.IMediaColumns.RelativePath, "Pictures/FileeTest");
            values.Put(MediaStore.IMediaColumns.IsPending, 1);
            uri = resolver.Insert(MediaStore.Images.Media.ExternalContentUri!, values)
                ?? throw new IOException("MediaStore test dosyası oluşturulamadı.");
            using (var dst = resolver.OpenOutputStream(uri, "wt")
                ?? throw new IOException("MediaStore test dosyasına yazılamadı."))
            using (var src = File.OpenRead(local))
                src.CopyTo(dst);
            values.Clear();
            values.Put(MediaStore.IMediaColumns.IsPending, 0);
            resolver.Update(uri, values, null, null);

            CopyRequireOriginal(uri, copied);
            var hashA = SHA256.HashData(File.ReadAllBytes(local));
            var hashB = SHA256.HashData(File.ReadAllBytes(copied));
            if (!hashA.AsSpan().SequenceEqual(hashB))
                throw new InvalidDataException("MediaStore özgün SHA-256 eşleşmedi.");
            global::Android.Util.Log.Info("FileeOriginalTest", "PASS: MediaStore original bytes SHA256 match");
        }
        catch (Exception e)
        {
            global::Android.Util.Log.Error("FileeOriginalTest", "FAIL: " + e);
        }
        finally
        {
            if (uri is not null)
            {
                try { resolver.Delete(uri, null, null); } catch (Exception e)
                { global::Android.Util.Log.Warn("FileeOriginalTest", "Cleanup: " + e.Message); }
            }
            if (File.Exists(local)) File.Delete(local);
            if (File.Exists(copied)) File.Delete(copied);
        }
    }
}
