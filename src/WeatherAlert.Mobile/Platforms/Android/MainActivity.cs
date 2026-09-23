using Android.App;
using Android.Content.PM;
using Android.Runtime;

namespace WeatherAlert.Mobile.Platforms.Android;

[Activity(
    Label = "天气预警",
    Theme = "@style/Maui.SplashTheme",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode |
                           ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
}

[Application]
public class MainApplication : MauiApplication
{
    public MainApplication(IntPtr handle, JniHandleOwnership ownership)
        : base(handle, ownership)
    {
    }

    public override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
