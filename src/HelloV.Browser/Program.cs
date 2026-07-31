using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Browser;
using HelloV;
using HelloV.Services;

[assembly: SupportedOSPlatform("browser")]

namespace HelloV.Browser;

internal sealed partial class Program
{
    private static async Task Main(string[] args)
    {
        await JSHost.ImportAsync("HelloVBrowser", "../js/hellov-browser.js");

        AppServices.PlatformKind = AppPlatformKind.Browser;
        AppServices.CameraFactory = static () => new BrowserCameraService();
        AppServices.EmojiImageProvider ??= new BrowserEmojiImageProvider();
        AppServices.ToggleFullscreenAsync = BrowserInterop.ToggleFullscreenAsync;

        await BuildAvaloniaApp()
            .WithFont_SourceHanSansCN()
            .StartBrowserAppAsync("out");
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>();
}
