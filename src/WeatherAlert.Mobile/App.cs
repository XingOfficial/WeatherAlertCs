namespace WeatherAlert.Mobile;

public class App : Microsoft.Maui.Controls.Application
{
    public App() => MainPage = new Microsoft.Maui.Controls.NavigationPage(new MainPage());

    protected override Microsoft.Maui.Controls.Window CreateWindow(IActivationState activationState)
    {
        var w = base.CreateWindow(activationState);
        w.Title = "天气预警";
        return w;
    }
}
