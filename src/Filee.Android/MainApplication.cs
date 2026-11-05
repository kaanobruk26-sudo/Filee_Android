using Android.App;
using Android.Runtime;
using Avalonia.Android;

namespace Filee.Mobile;

[Application]
public class MainApplication : AvaloniaAndroidApplication<App>
{
    public MainApplication(IntPtr javaReference, JniHandleOwnership transfer)
        : base(javaReference, transfer)
    {
    }
}