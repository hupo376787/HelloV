using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using Avalonia.Android;
using HelloV.Services;

namespace HelloV.Android;

[Activity(
    Label = "HelloV",
    Theme = "@style/AppTheme",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public sealed class MainActivity : AvaloniaMainActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        // Avalonia 12 initializes Android through AvaloniaAndroidApplication<TApp>.
        // Register the activity-dependent camera service before AvaloniaMainActivity.OnCreate,
        // because the main-view factory can be invoked from the base implementation.
        AppServices.PlatformKind = AppPlatformKind.Mobile;
        AppServices.CameraFactory = () => new AndroidCameraService(this);

        // Android assets live inside the APK and are not normal files. The recognizer factory is
        // already invoked on a worker thread, where AndroidModelLoader copies the packaged ONNX
        // model to FilesDir and then creates the ONNX Runtime session.
        AppServices.GestureFactory = () => AndroidModelLoader.CreateRecognizer(this);
        AppServices.EmojiImageProvider ??= new AndroidEmojiImageProvider();

        base.OnCreate(savedInstanceState);

        // Keep the display awake while the camera experience is visible. Android clears the
        // effect automatically when this Activity is no longer in the foreground.
        Window?.AddFlags(WindowManagerFlags.KeepScreenOn);
    }
}
