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
        AppServices.LoadInterruptMode = LoadInterruptMode;
        AppServices.SaveInterruptMode = SaveInterruptMode;
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    private static string InterruptModeSettingPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "HelloV",
        "interrupt-mode.txt");

    private static bool? LoadInterruptMode()
    {
        try
        {
            if (!File.Exists(InterruptModeSettingPath))
                return null;

            return bool.TryParse(File.ReadAllText(InterruptModeSettingPath), out var enabled)
                ? enabled
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static void SaveInterruptMode(bool enabled)
    {
        try
        {
            var directory = Path.GetDirectoryName(InterruptModeSettingPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(InterruptModeSettingPath, enabled.ToString());
        }
        catch
        {
            // A read-only application environment must not prevent the camera app from running.
        }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
