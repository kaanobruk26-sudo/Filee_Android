using Android.App;
using Android.Content.PM;
using Avalonia.Android;

[assembly: global::Android.App.UsesPermission(global::Android.Manifest.Permission.AccessMediaLocation)]

namespace Filee.Mobile;

[Activity(Label = "Filee", MainLauncher = true, Exported = true,
    Theme = "@style/FileeTheme",
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity
{
    // Native ACTION_CREATE_DOCUMENT is used so Android saves the output as
    // application/pdf instead of the generic */* MIME from Avalonia's picker.
    private const int PdfSaveRequest = 16072;
    private const int OriginalJpegPickerRequest = 16074;
    private TaskCompletionSource<global::Android.Net.Uri?>? _pendingOriginalJpegPick;

    /// <summary>
    /// Pick a JPEG via the system's media picker, retaining the content URI
    /// grant rather than using the generic document picker.
    /// </summary>
    public Task<global::Android.Net.Uri?> PickOriginalJpegAsync()
    {
        if (_pendingOriginalJpegPick is not null)
            throw new InvalidOperationException("Galeri seçimi zaten açık.");
        var pending = new TaskCompletionSource<global::Android.Net.Uri?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingOriginalJpegPick = pending;
        try
        {
            var intent = new global::Android.Content.Intent(
                global::Android.Content.Intent.ActionPick);
            intent.SetDataAndType(
                global::Android.Provider.MediaStore.Images.Media.ExternalContentUri, "image/jpeg");
            intent.AddFlags(global::Android.Content.ActivityFlags.GrantReadUriPermission);
            StartActivityForResult(intent, OriginalJpegPickerRequest);
        }
        catch (Exception e)
        {
            _pendingOriginalJpegPick = null;
            pending.TrySetException(e);
        }
        return pending.Task;
    }

    private const string InitialUriExtra = "android.provider.extra.INITIAL_URI";

    /// <summary>
    /// Best-effort browsing of a saved SAF location with Android DocumentsUI.
    /// Some document providers ignore EXTRA_INITIAL_URI; the UI says so.
    /// </summary>
    public void OpenSavedFolderBrowser(string contentUri)
    {
        var uri = global::Android.Net.Uri.Parse(contentUri)
            ?? throw new ArgumentException("Kaydedilen konum geçersiz.", nameof(contentUri));
        if (!string.Equals(uri.Scheme, "content", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Bu dosya konumu Android belge seçicisiyle açılamıyor.");

        StartActivity(CreateSavedFolderBrowseIntent(uri));
    }

    private static global::Android.Net.Uri NormalizeDocumentInitialUri(
        global::Android.Net.Uri uri)
    {
        // ACTION_OPEN_DOCUMENT requires a document URI (or a tree URI with
        // document ID). Xiaomi's picker can fall back to Download when given
        // only content://.../tree/<id>, even if <id> points to a subfolder.
        var segments = uri.PathSegments;
        if (segments is not null && segments.Contains("tree") &&
            !segments.Contains("document"))
        {
            var id = global::Android.Provider.DocumentsContract.GetTreeDocumentId(uri);
            return global::Android.Provider.DocumentsContract.BuildDocumentUriUsingTree(uri, id)
                ?? uri;
        }
        return uri;
    }

    private static global::Android.Content.Intent CreateSavedFolderBrowseIntent(
        global::Android.Net.Uri initialUri)
    {
        var normalized = NormalizeDocumentInitialUri(initialUri);
        return new global::Android.Content.Intent(global::Android.Content.Intent.ActionOpenDocument)
            .AddCategory(global::Android.Content.Intent.CategoryOpenable)
            .SetType("*/*")
            .PutExtra(InitialUriExtra, normalized)
            .AddFlags(global::Android.Content.ActivityFlags.GrantReadUriPermission);
    }

    private void RunFolderBrowserSelfTest()
    {
        try
        {
            using var uri = global::Android.Net.Uri.Parse(
                "content://com.android.externalstorage.documents/tree/" +
                "primary%3ADownload%2FFilee%20Test")
                ?? throw new InvalidOperationException("Test URI missing");
            using var intent = CreateSavedFolderBrowseIntent(uri);
            var normalized = NormalizeDocumentInitialUri(uri);
            var id = global::Android.Provider.DocumentsContract.GetDocumentId(normalized);
            if (intent.Action != global::Android.Content.Intent.ActionOpenDocument ||
                intent.Type != "*/*" || !intent.HasExtra(InitialUriExtra) ||
                !normalized.ToString().Contains("/document/") ||
                id != "primary:Download/Filee Test")
                throw new InvalidOperationException(
                    "Selected subfolder was not preserved in document URI.");
            using var image = global::Android.Net.Uri.Parse(
                "content://com.android.externalstorage.documents/tree/" +
                "primary%3AFilee%20Test/document/primary%3AFilee%20Test%2Fphoto.jpg")
                ?? throw new InvalidOperationException("Test image URI missing");
            using var view = CreateSavedFileViewIntent(image, "photo.jpg");
            if (view.Action != global::Android.Content.Intent.ActionView ||
                view.Type != "image/jpeg" ||
                view.Data?.ToString() != image.ToString() ||
                (view.Flags & global::Android.Content.ActivityFlags.GrantReadUriPermission) == 0 ||
                MainView.FriendlyFolderName("primary:Filee Test") != "Filee Test" ||
                MainView.FriendlyFolderName("primary:Download/Filee Test") != "Filee Test")
                throw new InvalidOperationException("File URI/MIME/read grant or friendly folder name failed.");
            global::Android.Util.Log.Info("FileeFolderTest",
                "PASS: exact subfolder URI, clean folder name, JPEG ACTION_VIEW with read grant");
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Error("FileeFolderTest", "FAIL: " + ex);
        }
    }

    /// <summary>
    /// Opens the exact file returned by the saved folder's Android document
    /// provider. The content URI is passed to the matching viewer with a
    /// temporary READ grant. No copying to a public or external directory.
    /// </summary>
    public void OpenSavedFileInAnotherApp(string uriText, string fileName)
    {
        var uri = global::Android.Net.Uri.Parse(uriText)
            ?? throw new ArgumentException("Dosya adresi geçersiz.", nameof(uriText));
        if (!string.Equals(uri.Scheme, "content", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException(
                "Bu dosyanın içerik adresi Android uygulamalarında açılamıyor.");
        var intent = CreateSavedFileViewIntent(uri, fileName);
        StartActivity(intent);
    }

    private static string FileMimeType(string name)
    {
        var ext = System.IO.Path.GetExtension(name).ToLowerInvariant();
        return ext switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".heic" or ".heif" or ".hif" => "image/heic",
            ".bmp" => "image/bmp",
            ".pdf" => "application/pdf",
            ".txt" or ".log" => "text/plain",
            ".zip" => "application/zip",
            ".mp4" => "video/mp4",
            ".mp3" => "audio/mpeg",
            _ => "*/*"
        };
    }

    private static global::Android.Content.Intent CreateSavedFileViewIntent(
        global::Android.Net.Uri uri, string fileName)
    {
        return new global::Android.Content.Intent(global::Android.Content.Intent.ActionView)
            .SetDataAndType(uri, FileMimeType(fileName))
            .AddFlags(global::Android.Content.ActivityFlags.GrantReadUriPermission);
    }

    private const int MediaLocationRequest = 16073;
    private TaskCompletionSource<bool>? _pendingMediaLocation;

    public Task<bool> RequestMediaLocationAccessAsync()
    {
        if ((int)global::Android.OS.Build.VERSION.SdkInt < 29)
            return Task.FromResult(true);
        var permission = global::Android.Manifest.Permission.AccessMediaLocation;
        if (CheckSelfPermission(permission) == Permission.Granted)
            return Task.FromResult(true);
        if (_pendingMediaLocation is not null)
            return _pendingMediaLocation.Task;
        var pending = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingMediaLocation = pending;
        try { RequestPermissions(new[] { permission }, MediaLocationRequest); }
        catch (Exception ex)
        {
            _pendingMediaLocation = null;
            pending.TrySetException(ex);
        }
        return pending.Task;
    }

    public override void OnRequestPermissionsResult(
        int requestCode, string[] permissions, Permission[] grantResults)
    {
        if (requestCode == MediaLocationRequest)
        {
            var pending = _pendingMediaLocation;
            _pendingMediaLocation = null;
            pending?.TrySetResult(grantResults.Length > 0 && grantResults[0] == Permission.Granted);
        }
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
    }

    private TaskCompletionSource<global::Android.Net.Uri?>? _pendingPdfSave;
    public static MainActivity? Current { get; private set; }

    public Task<global::Android.Net.Uri?> PickPdfSaveUriAsync(string fileName)
    {
        if (_pendingPdfSave is not null)
            throw new InvalidOperationException("Başka bir PDF kaydetme penceresi açık.");
        var pending = new TaskCompletionSource<global::Android.Net.Uri?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingPdfSave = pending;
        try
        {
            var intent = new global::Android.Content.Intent(global::Android.Content.Intent.ActionCreateDocument)
                .AddCategory(global::Android.Content.Intent.CategoryOpenable)
                .SetType("application/pdf")
                .PutExtra(global::Android.Content.Intent.ExtraTitle, fileName);
            StartActivityForResult(intent, PdfSaveRequest);
        }
        catch (Exception exception)
        {
            _pendingPdfSave = null;
            pending.TrySetException(exception);
        }
        return pending.Task;
    }

    protected override void OnActivityResult(int requestCode, global::Android.App.Result resultCode,
        global::Android.Content.Intent? data)
    {
        if (requestCode == OriginalJpegPickerRequest)
        {
            var pending = _pendingOriginalJpegPick;
            _pendingOriginalJpegPick = null;
            pending?.TrySetResult(resultCode == global::Android.App.Result.Ok ? data?.Data : null);
        }
        if (requestCode == PdfSaveRequest)
        {
            var pending = _pendingPdfSave;
            _pendingPdfSave = null;
            pending?.TrySetResult(resultCode == global::Android.App.Result.Ok ? data?.Data : null);
        }
        base.OnActivityResult(requestCode, resultCode, data);
    }

    protected override void OnDestroy()
    {
        _pendingOriginalJpegPick?.TrySetCanceled();
        _pendingOriginalJpegPick = null;
        _pendingMediaLocation?.TrySetCanceled();
        _pendingMediaLocation = null;
        _pendingPdfSave?.TrySetCanceled();
        _pendingPdfSave = null;
        if (ReferenceEquals(Current, this)) Current = null;
        base.OnDestroy();
    }

    protected override void OnCreate(global::Android.OS.Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        Current = this;
        if (Intent?.GetBooleanExtra("filee_pdf_selftest", false) == true)
            _ = Task.Run(MobileConversion.RunPdfEngineSelfTest);
        if (Intent?.GetBooleanExtra("filee_image_selftest", false) == true)
            _ = Task.Run(MobileConversion.RunImageEngineSelfTestAsync);
        if (Intent?.GetBooleanExtra("filee_original_selftest", false) == true)
            _ = Task.Run(OriginalMediaAccess.RunOriginalMediaSelfTestAsync);
        if (Intent?.GetBooleanExtra("filee_heif_selftest", false) == true)
            _ = Task.Run(MobileConversion.RunHeifEngineSelfTestAsync);
        if (Intent?.GetBooleanExtra("filee_batch_selftest", false) == true)
            _ = Task.Run(BatchConversionQueue.RunSelfTestAsync);
        if (Intent?.GetBooleanExtra("filee_folder_selftest", false) == true)
            RunFolderBrowserSelfTest();
        if (Intent?.GetBooleanExtra("filee_filter_selftest", false) == true)
            FolderListFilter.RunSelfTest();
    }
}