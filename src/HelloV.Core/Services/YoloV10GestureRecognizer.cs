using HelloV.Models;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace HelloV.Services;

/// <summary>
/// HaGRIDv2 YOLOv10n/YOLOv10x end-to-end detector. The exported model returns rows in the form
/// [x1, y1, x2, y2, confidence, classId]. HaGRIDv2 includes the two-hand heart classes.
/// </summary>
public sealed class YoloV10GestureRecognizer : IGestureRecognizer
{
    private const int DefaultInputSize = 640;
    private const float MinimumUsefulBoxArea = 0.006f;
    private const float MaximumUsefulBoxArea = 0.90f;

    private readonly InferenceSession _session;
    private readonly string _inputName;
    private readonly int _inputWidth;
    private readonly int _inputHeight;
    private readonly float[] _input;
    private readonly DenseTensor<float> _tensor;

    public YoloV10GestureRecognizer(string modelPath)
    {
        var options = new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
            // One ORT worker on Android leaves CPU time for Camera2 conversion and Avalonia
            // rendering. Desktop keeps up to two workers.
            IntraOpNumThreads = OperatingSystem.IsAndroid()
                ? 1
                : Math.Clamp(Environment.ProcessorCount / 4, 1, 2),
            InterOpNumThreads = 1
        };

        _session = new InferenceSession(modelPath, options);
        _inputName = _session.InputMetadata.Keys.First();
        var metadata = _session.InputMetadata[_inputName];
        var dimensions = metadata.Dimensions.ToArray();
        _inputHeight = dimensions.Length >= 4 && dimensions[^2] > 0
            ? dimensions[^2]
            : DefaultInputSize;
        _inputWidth = dimensions.Length >= 4 && dimensions[^1] > 0
            ? dimensions[^1]
            : DefaultInputSize;

        _input = new float[3 * _inputWidth * _inputHeight];
        _tensor = new DenseTensor<float>(_input, [1, 3, _inputHeight, _inputWidth]);
        var relativePath = Path.GetRelativePath(AppContext.BaseDirectory, modelPath);
        StateText = relativePath;
    }

    public bool IsReady => true;
    public string StateText { get; }

    public GestureFrameResult Recognize(VideoFrame frame)
    {
        var letterbox = OnnxImagePreprocessor.LetterboxBgraToNchwRgb(
            frame, _input, _inputWidth, _inputHeight);

        var input = NamedOnnxValue.CreateFromTensor(_inputName, _tensor);
        using var results = _session.Run([input]);
        var output = results.First().AsTensor<float>();
        var values = output.ToArray();
        var dimensions = output.Dimensions.ToArray();

        if (!TryResolveEndToEndShape(dimensions, values.Length, out var rows, out var transposed))
        {
            throw new InvalidOperationException(
                $"不支持的 YOLOv10 输出形状：[{string.Join(", ", dimensions)}]。请使用项目脚本导出的 end-to-end ONNX 模型。");
        }

        var candidates = new List<GestureDetection>();
        for (var row = 0; row < rows; row++)
        {
            var confidence = Read(values, row, 4, rows, transposed);
            var classId = (int)MathF.Round(Read(values, row, 5, rows, transposed));
            var kind = GestureCatalog.FromClassId(classId);
            if (kind == GestureKind.None || confidence < GestureCatalog.ConfidenceThreshold(kind))
                continue;

            var bounds = letterbox.ToSourceRect(
                Read(values, row, 0, rows, transposed),
                Read(values, row, 1, rows, transposed),
                Read(values, row, 2, rows, transposed),
                Read(values, row, 3, rows, transposed));

            if (bounds.Area is < MinimumUsefulBoxArea or > MaximumUsefulBoxArea)
                continue;

            candidates.Add(new GestureDetection(kind, confidence, bounds));
        }

        return new GestureFrameResult(RemoveDuplicates(candidates));
    }

    private static bool TryResolveEndToEndShape(
        int[] dimensions,
        int valueCount,
        out int rows,
        out bool transposed)
    {
        rows = 0;
        transposed = false;
        if (dimensions.Length < 2 || valueCount < 6)
            return false;

        if (dimensions[^1] == 6)
        {
            rows = valueCount / 6;
            return true;
        }

        if (dimensions[^2] == 6)
        {
            rows = dimensions[^1];
            transposed = true;
            return rows * 6 <= valueCount;
        }

        return false;
    }

    private static float Read(float[] values, int row, int field, int rows, bool transposed) =>
        transposed ? values[field * rows + row] : values[row * 6 + field];

    private static IReadOnlyList<GestureDetection> RemoveDuplicates(List<GestureDetection> candidates)
    {
        var kept = new List<GestureDetection>(8);
        foreach (var candidate in candidates.OrderByDescending(x => x.Confidence))
        {
            if (kept.Any(existing =>
                    existing.Kind == candidate.Kind &&
                    existing.Bounds.IntersectionOverUnion(candidate.Bounds) > 0.55f))
            {
                continue;
            }

            kept.Add(candidate);
            if (kept.Count == 8)
                break;
        }

        return kept;
    }

    public void Dispose() => _session.Dispose();
}
