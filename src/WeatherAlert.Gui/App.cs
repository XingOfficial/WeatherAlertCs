using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;

namespace WeatherAlert.Gui;

public class App : Application
{
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow
            {
                Title = "天气预警查询",
                Width = 1000,
                Height = 700,
            };
        base.OnFrameworkInitializationCompleted();
    }
}
