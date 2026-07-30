using Android.App;
using Android.Content.Res;
using Android.Util;
using HelloV.Services;

namespace HelloV.Android;

/// <summary>
/// Android APK assets are not ordinary filesystem files. ONNX Runtime's path constructor needs a
/// real file, so the packaged model is copied to the app-private files directory on a worker thread
/// before the inference session is created.
/// </summary>
internal static class AndroidModelLoader
{
    public static IGestureRecognizer CreateRecognizer(Activity activity)
    {
        var assets = activity.Assets
                     ?? throw new InvalidOperationException("Android AssetManager 不可用");
        var assetName = GestureRecognizerFactory.SupportedModelFileNames
            .FirstOrDefault(name => ContainsAsset(assets, name));

        if (assetName is null)
        {
            Log.Warn(
                "HelloV",
                $"Android asset model is missing. Supported names: {GestureRecognizerFactory.SupportedModelNamesText}");
            return GestureRecognizerFactory.CreateDefault();
        }

        var filesDirectory = activity.FilesDir?.AbsolutePath
                             ?? throw new InvalidOperationException("Android 应用文件目录不可用");
        Directory.CreateDirectory(filesDirectory);

        var modelPath = Path.Combine(filesDirectory, assetName);
        var temporaryPath = modelPath + ".tmp";

        try
        {
            using var source = assets.Open(assetName, Access.Streaming);
            using (var destination = new FileStream(
                       temporaryPath,
                       FileMode.Create,
                       FileAccess.Write,
                       FileShare.None,
                       bufferSize: 1024 * 1024,
                       useAsync: false))
            {
                source.CopyTo(destination, 1024 * 1024);
                destination.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, modelPath, overwrite: true);
            Log.Info("HelloV", $"Android ONNX model path: {modelPath}");
            return new YoloV10GestureRecognizer(modelPath);
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
            catch
            {
            }
        }
    }

    private static bool ContainsAsset(AssetManager assets, string assetName)
    {
        var names = assets.List(string.Empty) ?? [];
        return names.Any(x => string.Equals(x, assetName, StringComparison.Ordinal));
    }
}
