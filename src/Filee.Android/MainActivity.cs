using Android.App;
using Android.Content.PM;
using Avalonia.Android;

namespace Filee.Mobile;

[Activity(Label = "Filee", MainLauncher = true, Exported = true,
    Theme = "@style/FileeTheme",
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity
{
    // Native ACTION_CREATE_DOCUMENT is used so Android saves the output as
    // application/pdf instead of the generic */* MIME from Avalonia's picker.
    private const int PdfSaveRequest = 16072;
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
    }
}