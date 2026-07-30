using Android.App;
using Android.Runtime;
using Avalonia.Android;
using HelloV;
using HelloV.Services;

namespace HelloV.Android;

[Application]
public sealed class AndroidApp : AvaloniaAndroidApplication<App>
{
    // .NET for Android creates the Application instance through JNI. A sealed type cannot
    // introduce a protected member, so this constructor must be public rather than protected.
    public AndroidApp(nint javaReference, JniHandleOwnership transfer)
        : base(javaReference, transfer)
    {
        AppServices.PlatformKind = AppPlatformKind.Mobile;
    }
}
