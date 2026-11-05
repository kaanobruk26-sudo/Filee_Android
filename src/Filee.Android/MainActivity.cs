using Android.App;
using Android.Content.PM;
using Avalonia.Android;

namespace Filee.Mobile;

[Activity(Label = "Filee", MainLauncher = true, Exported = true,
    Theme = "@style/FileeTheme",
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity
{
}