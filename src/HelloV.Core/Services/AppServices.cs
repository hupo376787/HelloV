namespace HelloV.Services;

public enum AppPlatformKind
{
    Desktop,
    Mobile,
    Browser
}

public static class AppServices
{
    public static AppPlatformKind PlatformKind { get; set; } = AppPlatformKind.Desktop;
    public static Func<ICameraService>? CameraFactory { get; set; }
    public static Func<IGestureRecognizer>? GestureFactory { get; set; }
    public static IEmojiImageProvider? EmojiImageProvider { get; set; }
    public static Func<Task>? ToggleFullscreenAsync { get; set; }
    public static Func<bool?>? LoadInterruptMode { get; set; }
    public static Action<bool>? SaveInterruptMode { get; set; }

    public static ICameraService CreateCameraService() =>
        CameraFactory?.Invoke() ?? throw new InvalidOperationException(
            "平台入口尚未注册 ICameraService。请从 Desktop、Android、iOS 或 Browser 项目启动。 ");

    public static IGestureRecognizer CreateGestureRecognizer() =>
        GestureFactory?.Invoke() ?? GestureRecognizerFactory.CreateDefault();
}
