using HelloV.Services;
using UIKit;

namespace HelloV.iOS;

public static class Application
{
    public static void Main(string[] args)
    {
        AppServices.PlatformKind = AppPlatformKind.Mobile;
        AppServices.CameraFactory = static () => new IosCameraService();
        UIApplication.Main(args, null, typeof(AppDelegate));
    }
}
