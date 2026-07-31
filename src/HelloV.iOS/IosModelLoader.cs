using Foundation;
using HelloV.Services;

namespace HelloV.iOS;

/// <summary>
/// Resolves the ONNX model that MSBuild linked from the repository-root Models directory into
/// the iOS application bundle. iOS bundle resources are real read-only files, so ONNX Runtime can
/// open the resolved path directly without making a second private copy.
/// </summary>
internal static class IosModelLoader
{
    public static IGestureRecognizer CreateRecognizer()
    {
        foreach (var fileName in GestureRecognizerFactory.SupportedModelFileNames)
        {
            var resourceName = Path.GetFileNameWithoutExtension(fileName);
            var extension = Path.GetExtension(fileName).TrimStart('.');
            var bundlePath = NSBundle.MainBundle.PathForResource(resourceName, extension);

            if (!string.IsNullOrWhiteSpace(bundlePath) && File.Exists(bundlePath))
                return new YoloV10GestureRecognizer(bundlePath);
        }

        // Retain the common missing-model state text and desktop-compatible fallback behavior.
        return GestureRecognizerFactory.CreateDefault();
    }
}
