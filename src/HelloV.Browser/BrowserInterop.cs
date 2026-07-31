using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

namespace HelloV.Browser;

[SupportedOSPlatform("browser")]
internal static partial class BrowserInterop
{
    [JSImport("getCamerasJson", "HelloVBrowser")]
    internal static partial Task<string> GetCamerasJsonAsync();

    [JSImport("startCamera", "HelloVBrowser")]
    internal static partial Task StartCameraAsync(string deviceId, bool mirrorHorizontally);

    [JSImport("stopCamera", "HelloVBrowser")]
    internal static partial Task StopCameraAsync();

    [JSImport("setMirror", "HelloVBrowser")]
    internal static partial void SetMirror(bool mirrorHorizontally);

    [JSImport("toggleFullscreen", "HelloVBrowser")]
    internal static partial Task ToggleFullscreenAsync();

    /// <summary>
    /// Returns a packet containing an eight-byte little-endian width/height header followed by
    /// packed RGBA8888 pixels. JavaScript returns a Uint8Array, which .NET marshals to byte[].
    /// </summary>
    [JSImport("capturePreviewFrame", "HelloVBrowser")]
    internal static partial byte[] CapturePreviewFrame(int maxWidth, int maxHeight);

    [JSImport("renderEmoji", "HelloVBrowser")]
    internal static partial byte[] RenderEmoji(string emoji, int pixelSize);

    [JSImport("initializeModel", "HelloVBrowser")]
    internal static partial Task<string> InitializeModelAsync(
        string preferredModelUrl,
        string fallbackModelUrl);

    [JSImport("initializeModelBytes", "HelloVBrowser")]
    internal static partial Task<string> InitializeModelBytesAsync(
        byte[] modelBytes,
        string modelName);

    [JSImport("getRuntimeStateJson", "HelloVBrowser")]
    internal static partial string GetRuntimeStateJson();
}
