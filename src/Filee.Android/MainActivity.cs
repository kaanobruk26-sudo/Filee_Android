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
    }
}