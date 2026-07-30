using HelloV.Models;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace HelloV.Services;

/// <summary>
/// Compatibility recognizer for the original 19-class HaGRID YOLOX model. It supports the
/// seven reactions that can be composed from like/dislike/peace/rock, but it has no heart class.
/// </summary>
public sealed class YoloXGestureRecognizer : IGestureRecognizer
{
    private const int InputWidth = 384;
    private const int InputHeight = 384;
    private const int ClassCount = 19;
    private const int ProposalLength = ClassCount + 5;
    private static readonly GridCell[] Grid = CreateGrid();

    private readonly InferenceSession _session;
    private readonly string _inputName;
    private readonly float[] _input = new float[3 * InputWidth * InputHeight];
    private readonly DenseTensor<float> _tensor;

    public YoloXGestureRecognizer(string modelPath)
    {
        var options = new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
            IntraOpNumThreads = Math.Clamp(Environment.ProcessorCount / 4, 1, 2),
            InterOpNumThreads = 1
        };

        _session = new InferenceSession(modelPath, options);
        _inputName = _session.InputMetadata.Keys.First();
        _tensor = new DenseTensor<float>(_input, [1, 3, InputHeight, InputWidth]);
        StateText = Path.GetFileName(modelPath);
    }

    public bool IsReady => true;
    public string StateText { get; }

    public GestureFrameResult Recognize(VideoFrame frame)
    {
        var letterbox = OnnxImagePreprocessor.LetterboxBgraToNchwRgb(
            frame, _input, InputWidth, InputHeight);

        // This particular YOLOX model was exported with mean=[0.5,0.5,0.5], std=[1,1,1].
        for (var i = 0; i < _input.Length; i++)
            _input[i] -= 0.5f;

        var input = NamedOnnxValue.CreateFromTensor(_inputName, _tensor);
        using var results = _session.Run([input]);
        var output = results.First().AsTensor<float>();
        var values = output.ToArray();
        var dimensions = output.Dimensions.ToArray();
        var (anchors, fields, transposed) = ResolveOutputShape(dimensions, values.Length);
        if (fields < ProposalLength)
            return GestureFrameResult.Empty;

        var count = Math.Min(anchors, Grid.Length);
        var candidates = new List<GestureDetection>();
        for (var anchor = 0; anchor < count; anchor++)
        {
            var objectness = Read(values, anchor, 4, anchors, fields, transposed);
            if (objectness <= 0.01f)
                continue;

            var (kind, classIndex, classThreshold) = BestTargetClass(values, anchor, anchors, fields, transposed);
            if (kind == GestureKind.None)
                continue;

            var confidence = objectness * Read(values, anchor, 5 + classIndex, anchors, fields, transposed);
            if (confidence < classThreshold)
                continue;

            var grid = Grid[anchor];
            var centerX = (Read(values, anchor, 0, anchors, fields, transposed) + grid.X) * grid.Stride;
            var centerY = (Read(values, anchor, 1, anchors, fields, transposed) + grid.Y) * grid.Stride;
            var width = MathF.Exp(Math.Clamp(Read(values, anchor, 2, anchors, fields, transposed), -10f, 10f)) * grid.Stride;
            var height = MathF.Exp(Math.Clamp(Read(values, anchor, 3, anchors, fields, transposed), -10f, 10f)) * grid.Stride;
            var bounds = letterbox.ToSourceRect(
                centerX - width / 2f,
                centerY - height / 2f,
                centerX + width / 2f,
                centerY + height / 2f);

            if (bounds.Area is < 0.006f or > 0.90f)
                continue;

            candidates.Add(new GestureDetection(kind, confidence, bounds));
        }

        return new GestureFrameResult(NonMaximumSuppression(candidates));
    }

    private static (GestureKind Kind, int ClassIndex, float Threshold) BestTargetClass(
        float[] values,
        int anchor,
        int anchors,
        int fields,
        bool transposed)
    {
        var bestKind = GestureKind.None;
        var bestClass = -1;
        var bestValue = 0f;
        var bestThreshold = 1f;

        Test(1, GestureKind.Dislike, 0.32f);
        Test(4, GestureKind.Like, 0.30f);
        Test(10, GestureKind.Peace, 0.32f);
        Test(11, GestureKind.Peace, 0.32f);
        Test(12, GestureKind.Rock, 0.34f);
        Test(17, GestureKind.Peace, 0.32f);
        Test(18, GestureKind.Peace, 0.32f);
        return (bestKind, bestClass, bestThreshold);

        void Test(int classIndex, GestureKind kind, float threshold)
        {
            var value = Read(values, anchor, 5 + classIndex, anchors, fields, transposed);
            if (value <= bestValue)
                return;
            bestValue = value;
            bestClass = classIndex;
            bestKind = kind;
            bestThreshold = threshold;
        }
    }

    private static IReadOnlyList<GestureDetection> NonMaximumSuppression(List<GestureDetection> candidates)
    {
        var kept = new List<GestureDetection>(8);
        foreach (var candidate in candidates.OrderByDescending(x => x.Confidence))
        {
            if (kept.Any(existing =>
                    existing.Kind == candidate.Kind &&
                    existing.Bounds.IntersectionOverUnion(candidate.Bounds) > 0.48f))
                continue;

            kept.Add(candidate);
            if (kept.Count == 8)
                break;
        }
        return kept;
    }

    private static (int Anchors, int Fields, bool Transposed) ResolveOutputShape(int[] dimensions, int length)
    {
        var nonBatch = dimensions.Where(x => x > 1).ToArray();
        if (nonBatch.Length >= 2)
        {
            var a = nonBatch[^2];
            var b = nonBatch[^1];
            if (b >= ProposalLength && b <= 128)
                return (a, b, false);
            if (a >= ProposalLength && a <= 128)
                return (b, a, true);
        }

        return (length / ProposalLength, ProposalLength, false);
    }

    private static float Read(float[] values, int anchor, int field, int anchors, int fields, bool transposed) =>
        transposed ? values[field * anchors + anchor] : values[anchor * fields + field];

    private static GridCell[] CreateGrid()
    {
        var result = new List<GridCell>();
        foreach (var stride in new[] { 8, 16, 32 })
        {
            for (var y = 0; y < InputHeight / stride; y++)
            for (var x = 0; x < InputWidth / stride; x++)
                result.Add(new GridCell(x, y, stride));
        }
        return result.ToArray();
    }

    public void Dispose() => _session.Dispose();

    private readonly record struct GridCell(int X, int Y, int Stride);
}
