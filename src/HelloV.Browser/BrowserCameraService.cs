using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia.Platform;
using HelloV.Models;
using HelloV.Services;

namespace HelloV.Browser;

/// <summary>
/// Browser camera and gesture bridge. JavaScript owns getUserMedia and ONNX Runtime Web. A bounded
/// 640-pixel preview frame is copied into the normal Avalonia camera pipeline so the browser build
/// does not depend on unsupported transparent-canvas compositing.
/// </summary>
[SupportedOSPlatform("browser")]
internal sealed class BrowserCameraService :
    ICameraService,
    IExternalGestureSource,
    IPreviewMirroringController
{
    private const int PreviewMaximumWidth = 640;
    private const int PreviewMaximumHeight = 640;
    private static readonly TimeSpan RuntimePollInterval = TimeSpan.FromMilliseconds(33);
    private const int PreviewEveryRuntimePolls = 2;

    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private CancellationTokenSource? _pollCancellation;
    private Task? _pollTask;
    private Task? _modelTask;
    private long _lastDetectionSequence = -1;
    private CameraFrameStatistics _lastStatistics;
    private ExternalModelState? _lastModelState;
    private bool _mirrorHorizontally = true;
    private int _disposed;

    public event Action<GestureFrameResult>? GestureFrameReady;
    public event Action<CameraFrameStatistics>? FrameStatisticsChanged;
    public event Action<ExternalModelState>? ModelStateChanged;

    public async Task<IReadOnlyList<CameraDeviceInfo>> GetCamerasAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        var json = await BrowserInterop.GetCamerasJsonAsync().WaitAsync(cancellationToken);
        var devices = JsonSerializer.Deserialize(json, BrowserJsonContext.Default.CameraList) ?? [];

        return devices.Select((device, index) => new CameraDeviceInfo(
                device.Id ?? string.Empty,
                string.IsNullOrWhiteSpace(device.Label) ? $"摄像头 {index + 1}" : device.Label,
                ParseFacing(device.Facing),
                index))
            .ToArray();
    }

    public async Task StartAsync(
        CameraDeviceInfo camera,
        Func<VideoFrame, ValueTask> onFrame,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(camera);
        ArgumentNullException.ThrowIfNull(onFrame);
        ThrowIfDisposed();

        await _lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            await StopCoreAsync();
            cancellationToken.ThrowIfCancellationRequested();

            RaiseModelState(new ExternalModelState(
                ExternalModelStatus.Loading,
                "ONNX Runtime Web"));

            _modelTask ??= InitializeModelAsync();
            await BrowserInterop.StartCameraAsync(camera.Id, _mirrorHorizontally)
                .WaitAsync(cancellationToken);

            _lastDetectionSequence = -1;
            _lastStatistics = default;
            var polling = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _pollCancellation = polling;
            _pollTask = PollRuntimeAsync(onFrame, polling.Token);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _disposed) != 0)
            return;

        await _lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            await StopCoreAsync();
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public void SetPreviewMirroring(bool flipHorizontally)
    {
        _mirrorHorizontally = flipHorizontally;
        if (Volatile.Read(ref _disposed) != 0)
            return;

        try
        {
            BrowserInterop.SetMirror(flipHorizontally);
        }
        catch
        {
            // The JS module can be unavailable during early app startup or final teardown.
        }
    }

    private async Task InitializeModelAsync()
    {
        var started = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            string json;
            var embedded = await TryLoadEmbeddedModelAsync();
            if (embedded is not null)
            {
                json = await BrowserInterop.InitializeModelBytesAsync(
                    embedded.Value.Bytes,
                    embedded.Value.Name);
            }
            else
            {
                // Static-file fallback supports models placed directly in the Browser project's
                // wwwroot/models folder. The JavaScript side also probes rooted and case variants.
                json = await BrowserInterop.InitializeModelAsync(
                    "./models/YOLOv10n_gestures.onnx",
                    "./models/YOLOv10x_gestures.onnx");
            }

            var state = JsonSerializer.Deserialize(json, BrowserJsonContext.Default.ModelState);
            if (state is null)
                throw new InvalidOperationException("浏览器模型加载器没有返回状态");

            RaiseModelState(ToExternalModelState(state, started.Elapsed.TotalSeconds));
        }
        catch (Exception ex)
        {
            RaiseModelState(new ExternalModelState(
                ExternalModelStatus.Failed,
                "ONNX Runtime Web",
                started.Elapsed.TotalSeconds,
                ex.Message));
        }
    }

    private static async Task<(byte[] Bytes, string Name)?> TryLoadEmbeddedModelAsync()
    {
        var candidates = new[]
        {
            (Uri: new Uri("avares://HelloV.Browser/Models/YOLOv10n_gestures.onnx"),
                Name: "YOLOv10n_gestures.onnx"),
            (Uri: new Uri("avares://HelloV.Browser/Models/YOLOv10x_gestures.onnx"),
                Name: "YOLOv10x_gestures.onnx")
        };

        foreach (var candidate in candidates)
        {
            if (!AssetLoader.Exists(candidate.Uri))
                continue;

            await using var source = AssetLoader.Open(candidate.Uri);
            using var memory = source.CanSeek && source.Length > 0 && source.Length <= int.MaxValue
                ? new MemoryStream((int)source.Length)
                : new MemoryStream();
            await source.CopyToAsync(memory);
            var bytes = memory.ToArray();
            if (bytes.Length >= 1024)
                return (bytes, candidate.Name);
        }

        return null;
    }

    private async Task PollRuntimeAsync(
        Func<VideoFrame, ValueTask> onFrame,
        CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(RuntimePollInterval);
        var pollsUntilPreview = PreviewEveryRuntimePolls;
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                // Gesture/model state is lightweight and is polled at about 30 Hz. Preview RGBA
                // transfer remains about 15 Hz so a large JS-to-.NET frame copy cannot hold a newly
                // recognized gesture in the queue for several seconds.
                PublishRuntimeState();

                if (--pollsUntilPreview > 0)
                    continue;

                pollsUntilPreview = PreviewEveryRuntimePolls;
                await PublishPreviewFrameAsync(onFrame, cancellationToken);

                // Inference may have completed while the preview frame was copied and rendered.
                // Publish once more immediately instead of waiting for another timer period.
                PublishRuntimeState();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            RaiseModelState(new ExternalModelState(
                ExternalModelStatus.Failed,
                "浏览器摄像头",
                ErrorMessage: ex.Message));
        }
    }

    private void PublishRuntimeState()
    {
        var json = BrowserInterop.GetRuntimeStateJson();
        if (string.IsNullOrWhiteSpace(json))
            return;

        var state = JsonSerializer.Deserialize(json, BrowserJsonContext.Default.RuntimeState);
        if (state is null)
            return;

        PublishStatistics(state.Camera);
        PublishModelState(state.Model);
        PublishDetections(state);
    }

    private static async ValueTask PublishPreviewFrameAsync(
        Func<VideoFrame, ValueTask> onFrame,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var packet = BrowserInterop.CapturePreviewFrame(
            PreviewMaximumWidth,
            PreviewMaximumHeight);
        if (packet is null || packet.Length < 8)
            return;

        var width = ReadInt32LittleEndian(packet, 0);
        var height = ReadInt32LittleEndian(packet, 4);
        if (width <= 0 || height <= 0)
            return;

        int pixelLength;
        try
        {
            pixelLength = checked(width * height * 4);
        }
        catch (OverflowException)
        {
            return;
        }

        if (packet.Length != pixelLength + 8)
            return;

        VideoFrame? frame = VideoFrame.Rent(
            width,
            height,
            DateTime.UtcNow.Ticks,
            pixelFormat: VideoPixelFormat.Rgba8888);
        try
        {
            Buffer.BlockCopy(packet, 8, frame.Pixels, 0, pixelLength);
            await onFrame(frame);
            frame = null; // Ownership was consumed by the camera callback.
        }
        finally
        {
            frame?.Dispose();
        }
    }

    private static int ReadInt32LittleEndian(byte[] bytes, int offset) =>
        bytes[offset] |
        bytes[offset + 1] << 8 |
        bytes[offset + 2] << 16 |
        bytes[offset + 3] << 24;

    private void PublishStatistics(BrowserCameraStateDto? camera)
    {
        if (camera is null || camera.Width <= 0 || camera.Height <= 0)
            return;

        var statistics = new CameraFrameStatistics(
            camera.Width,
            camera.Height,
            Math.Max(0, camera.Fps));
        if (statistics.Equals(_lastStatistics))
            return;

        _lastStatistics = statistics;
        FrameStatisticsChanged?.Invoke(statistics);
    }

    private void PublishModelState(BrowserModelDto? model)
    {
        if (model is null)
            return;

        var state = ToExternalModelState(model, model.LoadSeconds);
        RaiseModelState(state);
    }

    private void PublishDetections(BrowserRuntimeDto state)
    {
        if (state.DetectionSequence == _lastDetectionSequence)
            return;

        _lastDetectionSequence = state.DetectionSequence;
        var detections = new List<GestureDetection>(8);
        foreach (var item in state.Detections ?? [])
        {
            if (!Enum.IsDefined(typeof(GestureKind), item.Kind) || item.Kind <= 0)
                continue;

            var kind = (GestureKind)item.Kind;
            var bounds = NormalizedRect.FromCorners(
                item.X,
                item.Y,
                item.X + item.Width,
                item.Y + item.Height);
            detections.Add(new GestureDetection(
                kind,
                Math.Clamp(item.Confidence, 0f, 1f),
                bounds));
        }

        GestureFrameReady?.Invoke(new GestureFrameResult(detections));
    }

    private void RaiseModelState(ExternalModelState state)
    {
        if (_lastModelState is { } previous && previous.Equals(state))
            return;

        _lastModelState = state;
        ModelStateChanged?.Invoke(state);
    }

    private static ExternalModelState ToExternalModelState(
        BrowserModelDto model,
        double fallbackLoadSeconds)
    {
        var status = model.State?.ToLowerInvariant() switch
        {
            "ready" => ExternalModelStatus.Ready,
            "missing" => ExternalModelStatus.Missing,
            "error" or "failed" => ExternalModelStatus.Failed,
            _ => ExternalModelStatus.Loading
        };
        var backend = string.IsNullOrWhiteSpace(model.Backend)
            ? string.Empty
            : $" · {model.Backend}";
        var text = string.IsNullOrWhiteSpace(model.Name)
            ? $"ONNX Runtime Web{backend}"
            : $"{model.Name}{backend}";

        return new ExternalModelState(
            status,
            text,
            model.LoadSeconds > 0 ? model.LoadSeconds : fallbackLoadSeconds,
            model.Error);
    }

    private async Task StopCoreAsync()
    {
        var cancellation = _pollCancellation;
        _pollCancellation = null;
        cancellation?.Cancel();

        var polling = _pollTask;
        _pollTask = null;
        if (polling is not null)
        {
            try
            {
                await polling;
            }
            catch (OperationCanceledException)
            {
            }
        }

        cancellation?.Dispose();
        try
        {
            await BrowserInterop.StopCameraAsync();
        }
        catch
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        await _lifecycleGate.WaitAsync();
        try
        {
            await StopCoreAsync();
        }
        finally
        {
            _lifecycleGate.Release();
        }

        if (_modelTask is not null)
        {
            try
            {
                await _modelTask;
            }
            catch
            {
            }
        }

        _lifecycleGate.Dispose();
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
            throw new ObjectDisposedException(nameof(BrowserCameraService));
    }

    private static CameraFacing ParseFacing(string? facing) => facing?.ToLowerInvariant() switch
    {
        "front" => CameraFacing.Front,
        "back" => CameraFacing.Back,
        "external" => CameraFacing.External,
        _ => CameraFacing.Unknown
    };

}

internal sealed class BrowserCameraDto
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("label")]
    public string? Label { get; init; }

    [JsonPropertyName("facing")]
    public string? Facing { get; init; }
}

internal sealed class BrowserRuntimeDto
{
    [JsonPropertyName("camera")]
    public BrowserCameraStateDto? Camera { get; init; }

    [JsonPropertyName("model")]
    public BrowserModelDto? Model { get; init; }

    [JsonPropertyName("detectionSequence")]
    public long DetectionSequence { get; init; }

    [JsonPropertyName("detections")]
    public List<BrowserDetectionDto>? Detections { get; init; }
}

internal sealed class BrowserCameraStateDto
{
    [JsonPropertyName("width")]
    public int Width { get; init; }

    [JsonPropertyName("height")]
    public int Height { get; init; }

    [JsonPropertyName("fps")]
    public double Fps { get; init; }
}

internal sealed class BrowserModelDto
{
    [JsonPropertyName("state")]
    public string? State { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("backend")]
    public string? Backend { get; init; }

    [JsonPropertyName("error")]
    public string? Error { get; init; }

    [JsonPropertyName("loadSeconds")]
    public double LoadSeconds { get; init; }
}

internal sealed class BrowserDetectionDto
{
    [JsonPropertyName("kind")]
    public int Kind { get; init; }

    [JsonPropertyName("confidence")]
    public float Confidence { get; init; }

    [JsonPropertyName("x")]
    public float X { get; init; }

    [JsonPropertyName("y")]
    public float Y { get; init; }

    [JsonPropertyName("width")]
    public float Width { get; init; }

    [JsonPropertyName("height")]
    public float Height { get; init; }
}

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(List<BrowserCameraDto>), TypeInfoPropertyName = "CameraList")]
[JsonSerializable(typeof(BrowserRuntimeDto), TypeInfoPropertyName = "RuntimeState")]
[JsonSerializable(typeof(BrowserModelDto), TypeInfoPropertyName = "ModelState")]
internal partial class BrowserJsonContext : JsonSerializerContext
{
}
