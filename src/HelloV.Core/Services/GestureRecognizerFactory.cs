using HelloV.Models;

namespace HelloV.Services;

public static class GestureRecognizerFactory
{
    public const string ModelFileName = "YOLOv10n_gestures.onnx";
    public const string LargeModelFileName = "YOLOv10x_gestures.onnx";

    // Preserve the current lightweight model as the first choice. If it is absent, the
    // YOLOv10x model is loaded with the same end-to-end YOLOv10 parser and gesture labels.
    public static IReadOnlyList<string> SupportedModelFileNames { get; } =
        [ModelFileName, LargeModelFileName];

    public static string SupportedModelNamesText =>
        string.Join(" / ", SupportedModelFileNames);

    public static IGestureRecognizer CreateDefault()
    {
#if HELLOV_BROWSER
        // Browser recognition is supplied by HelloV.Browser through ONNX Runtime Web. Keeping a
        // no-op fallback here prevents the native ONNX Runtime package from entering the WASM app.
        return new MissingModelGestureRecognizer();
#else
        var modelPath = FindModel();
        if (modelPath is not null)
            return new YoloV10GestureRecognizer(modelPath);

        return new MissingModelGestureRecognizer();
#endif
    }

    /// <summary>
    /// Only searches beside the executable and in the Models folder beside the executable.
    /// The executable directory has priority; within one directory YOLOv10n is preferred over
    /// YOLOv10x so an existing installation does not unexpectedly switch to the heavier model.
    /// </summary>
    private static string? FindModel() =>
        GetCandidatePaths().FirstOrDefault(File.Exists);

    private static IEnumerable<string> GetCandidatePaths()
    {
        var baseDirectory = AppContext.BaseDirectory;
        var directories = new[]
        {
            baseDirectory,
            Path.Combine(baseDirectory, "Models")
        };

        foreach (var directory in directories)
        {
            foreach (var fileName in SupportedModelFileNames)
                yield return Path.Combine(directory, fileName);
        }
    }

    private sealed class MissingModelGestureRecognizer : IGestureRecognizer
    {
        public bool IsReady => false;

        public string StateText => SupportedModelNamesText;

        public GestureFrameResult Recognize(VideoFrame frame) => GestureFrameResult.Empty;
        public void Dispose() { }
    }
}
