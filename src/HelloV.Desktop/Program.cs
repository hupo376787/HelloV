using Avalonia;
using HelloV;
using HelloV.Services;

namespace HelloV.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        AppServices.PlatformKind = AppPlatformKind.Desktop;
        AppServices.CameraFactory = static () => new DesktopCameraService();
        AppServices.ToggleFullscreenAsync = null;
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
