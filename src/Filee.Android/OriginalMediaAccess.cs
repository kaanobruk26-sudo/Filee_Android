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


    /// <summary>
    /// Read the name and MIME type from the native URI. Avalonia's file picker
    /// bridge cannot resolve all Android provider URI types.
    /// </summary>
    public static string GetGalleryJpegName(global::Android.Net.Uri selected)
    {
        if (selected.Scheme != "content" && selected.Scheme != "file")
            throw new NotSupportedException("Galeri geçerli bir dosya adresi vermedi.");

        var resolver = global::Android.App.Application.Context.ContentResolver
            ?? throw new IOException("Android içerik sağlayıcısı kullanılamıyor.");
        var mime = resolver.GetType(selected);
        if (!string.IsNullOrWhiteSpace(mime) &&
            !mime.Equals("image/jpeg", StringComparison.OrdinalIgnoreCase) &&
            !mime.Equals("image/jpg", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("Seçilen dosya JPEG değil: " + mime);

        string? displayName = null;
        try
        {
            using var cursor = resolver.Query(
                selected, new[] { global::Android.Provider.OpenableColumns.DisplayName }, null, null, null);
            if (cursor is not null && cursor.MoveToFirst())
                displayName = cursor.GetString(0);
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Warn("FileeGalleryPicker", "Filename query: " + ex.Message);
        }

        if (string.IsNullOrWhiteSpace(displayName))
            displayName = selected.LastPathSegment ?? "fotoğraf.jpg";
        displayName = System.IO.Path.GetFileName(displayName);
        if (!MobileConversion.IsJpeg(displayName))
        {
            if (!string.IsNullOrWhiteSpace(mime) &&
                (mime.Equals("image/jpeg", StringComparison.OrdinalIgnoreCase) ||
                 mime.Equals("image/jpg", StringComparison.OrdinalIgnoreCase)))
                displayName += ".jpg";
            else
                throw new NotSupportedException(
                    "Galeri dosyayı JPEG olarak tanımlamıyor. JPG fotoğraf seçin.");
        }

        return displayName;
    }

    // The result distinguishes a MediaStore unredacted original from bytes
    // supplied by a third-party content provider (whose EXIF may be masked).
    public static async Task<(long bytes, string sha256, bool confirmedOriginal)> CopyGalleryJpegExactAsync(
        global::Android.Net.Uri selected, IStorageFile output)
    {
        await EnsureOriginalAccessAsync(selected);
        var original = ResolveMediaUri(selected);
        bool confirmedOriginal = original is not null;
        // Non-MediaStore providers (e.g., Xiaomi File Explorer) are legitimate
        // sources. We can copy their selected stream exactly, but cannot prove
        // Android did not redact location EXIF before it reached us.

        var cacheDir = global::Android.App.Application.Context.CacheDir?.AbsolutePath
            ?? throw new IOException("Uygulama önbelleğine ulaşılamıyor.");
        var temp = System.IO.Path.Combine(cacheDir,
            "filee_gallery_" + Guid.NewGuid().ToString("N") + ".jpg");
        try
        {
            if (original is null)
            {
                await CopyProviderStreamAsync(selected, temp);
            }
            else if (original.Scheme == "file")
            {
                var filePath = original.Path
                    ?? throw new IOException("Dosya konumu bulunamadı.");
                System.IO.File.Copy(filePath, temp);
            }
            else
            {
                await Task.Run(() => CopyRequireOriginal(original, temp));
            }

            await using (var test = System.IO.File.OpenRead(temp))
            {
                if (test.Length < 4 || test.ReadByte() != 0xFF || test.ReadByte() != 0xD8)
                    throw new InvalidDataException("Özgün dosya geçerli bir JPEG değil.");
            }
            var bytes = new System.IO.FileInfo(temp).Length;
            byte[] sourceHash;
            await using (var src = System.IO.File.OpenRead(temp))
                sourceHash = await SHA256.HashDataAsync(src);

            await using (var target = await output.OpenWriteAsync())
            await using (var src = System.IO.File.OpenRead(temp))
            {
                await src.CopyToAsync(target);
                await target.FlushAsync();
            }

            // Verify what the user actually saved. Do not report "byte exact"
            // merely because the cached source and internal copy match.
            byte[] savedHash;
            await using (var saved = await output.OpenReadAsync())
                savedHash = await SHA256.HashDataAsync(saved);
            if (!sourceHash.AsSpan().SequenceEqual(savedHash))
                throw new InvalidDataException(
                    "Kaydedilen JPG ile özgün kaynak uyuşmadı. Birebir kopya doğrulanamadı.");

            global::Android.Util.Log.Info("FileeOriginal",
                $"Saved JPEG verified: bytes={bytes}, SHA256={Convert.ToHexString(sourceHash)}, originalGuaranteed={confirmedOriginal}, provider={selected.Authority}");
            return (bytes, Convert.ToHexString(sourceHash), confirmedOriginal);
        }
        finally
        {
            if (System.IO.File.Exists(temp)) System.IO.File.Delete(temp);
        }
    }


    /// <summary>
    /// Read the bytes that a third-party provider exposes via the picker URI.
    /// This never transcodes/recompresses, but the provider could have
    /// redacted metadata before handing the stream to the application.
    /// </summary>
    private static async Task CopyProviderStreamAsync(global::Android.Net.Uri selected, string path)
    {
        var resolver = global::Android.App.Application.Context.ContentResolver
            ?? throw new IOException("Dosya sağlayıcısına erişilemiyor.");
        using var source = resolver.OpenInputStream(selected)
            ?? throw new IOException("Seçilen JPG için okuma akışı açılamadı. Dosya sağlayıcısı erişim vermiyor.");
        await using var destination = new FileStream(
            path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await source.CopyToAsync(destination);
        await destination.FlushAsync();
        global::Android.Util.Log.Info("FileeOriginal",
            $"Provider stream copied: {selected.Authority}, {destination.Length} bytes");
    }

    public static async Task EnsureOriginalAccessAsync(global::Android.Net.Uri selected)
    {
        var uri = ResolveMediaUri(selected);
        if (uri is null)
        {
            // A picker-granted URI is readable through ContentResolver, even
            // if it doesn't belong to MediaStore. There is no meaningful way
            // to apply MediaStore.SetRequireOriginal to this authority.
            if (selected.Scheme != "content")
                throw new NotSupportedException("Fotoğrafın sağlayıcı adresi desteklenmiyor.");
            global::Android.Util.Log.Warn("FileeOriginal",
                "Third-party JPEG provider: copying accessible bytes only, original EXIF not guaranteed: "
                + selected.Authority);
            return;
        }

        if (uri.Scheme == "file") return;
        var activity = MainActivity.Current
            ?? throw new InvalidOperationException("Android ekranı bulunamadı.");
        if (!await activity.RequestMediaLocationAccessAsync())
            throw new UnauthorizedAccessException(
                "Özgün EXIF için medya konumu izni verilmedi. İzin vermeden birebir kopyalama yapılamaz.");
    }

    private static global::Android.Net.Uri? ResolveMediaUri(global::Android.Net.Uri source)
    {
        if (source.Scheme == "file") return source;
        if (source.Scheme != "content") return null;
        if (source.Authority == "media") return source;
        if (source.Authority == "com.android.providers.media.documents")
        {
            var id = DocumentsContract.GetDocumentId(source);
            if (id is not null && id.StartsWith("image:", StringComparison.OrdinalIgnoreCase) &&
                long.TryParse(id["image:".Length..], out var imageId))
                return ContentUris.WithAppendedId(
                    MediaStore.Images.Media.ExternalContentUri!, imageId);
        }
        return null;
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

            var galleryName = GetGalleryJpegName(uri);
            if (!galleryName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Galeri medya URI dosya adını çözemedi.");
            CopyRequireOriginal(uri, copied);
            // Exercise the generic provider stream path too; the real Xiaomi
            // picker uses com.android.fileexplorer.myprovider instead of media.
            var generic = Path.Combine(context.CacheDir!.AbsolutePath,
                "filee_provider_" + Guid.NewGuid().ToString("N") + ".jpg");
            try
            {
                await CopyProviderStreamAsync(uri, generic);
                if (!SHA256.HashData(File.ReadAllBytes(local)).AsSpan().SequenceEqual(
                    SHA256.HashData(File.ReadAllBytes(generic))))
                    throw new InvalidDataException("Generic provider byte-copy did not match.");
            }
            finally
            {
                if (File.Exists(generic)) File.Delete(generic);
            }
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
