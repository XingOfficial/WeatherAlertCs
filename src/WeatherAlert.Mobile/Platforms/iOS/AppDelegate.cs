using Foundation;
using UIKit;

namespace WeatherAlert.Mobile.Platforms.iOS;

[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate
{
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}

[Register("Main")]
public class Program
{
    static void Main(string[] args) => UIApplication.Main(args, null, typeof(AppDelegate));
}
