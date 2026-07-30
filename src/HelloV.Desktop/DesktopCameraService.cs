using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using HelloV.Models;
using HelloV.Services;
using OpenCvSharp;

namespace HelloV.Desktop;

public sealed class DesktopCameraService : ICameraService
{
    private static readonly (int Width, int Height)[] CandidateModes =
    [
        (7680, 4320), (4096, 2160), (3840, 2160), (2560, 1440),
        (1920, 1080), (1600, 1200), (1280, 1024), (1280, 720),
        (1024, 768), (800, 600), (640, 480)
    ];

    private VideoCapture? _capture;
    private CancellationTokenSource? _captureCts;
    private Task? _captureTask;

    public Task<IReadOnlyList<CameraDeviceInfo>> GetCamerasAsync(
        CancellationToken cancellationToken = default)
    {
        return Task.Run<IReadOnlyList<CameraDeviceInfo>>(() =>
        {
            if (OperatingSystem.IsWindows())
            {
                var directShowDevices = WindowsDirectShowCameraEnumerator.Enumerate();
                if (directShowDevices.Count > 0)
                {
                    return directShowDevices
                        .Select(device => new CameraDeviceInfo(
                            device.Id,
                            device.DisplayName,
                            CameraFacing.External,
                            device.Index))
                        .ToArray();
                }
            }

            // Fallback still probes only the platform-specific backend. It deliberately does not
            // fall back to CAP_ANY for each index, because CAP_ANY can reopen index 0 under a
            // different backend and make one physical camera appear twice.
            return ProbeIndexedCameras(cancellationToken);
        }, cancellationToken);
    }

    private static IReadOnlyList<CameraDeviceInfo> ProbeIndexedCameras(
        CancellationToken cancellationToken)
    {
        var result = new List<CameraDeviceInfo>();
        for (var index = 0; index < 10; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var probe = OpenCapture(index, allowAnyFallback: false);
            if (!probe.IsOpened())
                continue;

            result.Add(new CameraDeviceInfo(
                $"camera:{index}",
                $"摄像头 {index + 1}",
                CameraFacing.External,
                index));
        }

        return result;
    }

    public async Task StartAsync(
        CameraDeviceInfo camera,
        Func<VideoFrame, ValueTask> onFrame,
        CancellationToken cancellationToken = default)
    {
        await StopAsync(cancellationToken).ConfigureAwait(false);

        // Opening a USB camera and negotiating several resolutions can block inside the native
        // driver. Keep all of that work away from Avalonia's UI thread.
        var capture = await Task.Run(
            () => CreateConfiguredCapture(camera, cancellationToken),
            cancellationToken).ConfigureAwait(false);

        if (cancellationToken.IsCancellationRequested)
        {
            capture.Dispose();
            cancellationToken.ThrowIfCancellationRequested();
        }

        _capture = capture;
        _captureCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _captureTask = Task.Run(
            () => CaptureLoop(capture, onFrame, _captureCts.Token),
            _captureCts.Token);
    }

    private static VideoCapture CreateConfiguredCapture(
        CameraDeviceInfo camera,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var capture = OpenCapture(camera.Index, allowAnyFallback: true);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!capture.IsOpened())
                throw new InvalidOperationException($"无法打开 {camera.DisplayName}");

            // MJPG avoids uncompressed USB bandwidth becoming the bottleneck at 720p/1080p.
            capture.Set(VideoCaptureProperties.FourCC, VideoWriter.FourCC('M', 'J', 'P', 'G'));

            if (!TryApplyCachedMode(capture, camera.Id, out var selectedMode))
            {
                selectedMode = SelectHighestSupportedResolution(capture, cancellationToken);
                CameraModeCache.Save(camera.Id, selectedMode);
            }

            capture.Set(VideoCaptureProperties.Fps, selectedMode.Fps);

            // Supported backends retain only the newest decoded frame instead of accumulating latency.
            capture.Set(VideoCaptureProperties.BufferSize, 1);

            Debug.WriteLine(
                $"摄像头初始化完成：{camera.DisplayName}，" +
                $"{selectedMode.Width}x{selectedMode.Height}@{selectedMode.Fps}，" +
                $"{stopwatch.ElapsedMilliseconds} ms");
            return capture;
        }
        catch
        {
            capture.Release();
            capture.Dispose();
            throw;
        }
    }

    private static bool TryApplyCachedMode(
        VideoCapture capture,
        string cameraId,
        out CameraMode selectedMode)
    {
        selectedMode = default;
        if (!CameraModeCache.TryGet(cameraId, out var cached))
            return false;

        capture.Set(VideoCaptureProperties.FrameWidth, cached.Width);
        capture.Set(VideoCaptureProperties.FrameHeight, cached.Height);
        capture.Set(VideoCaptureProperties.Fps, cached.Fps);

        var actualWidth = Math.Max(1, (int)Math.Round((double)capture.FrameWidth));
        var actualHeight = Math.Max(1, (int)Math.Round((double)capture.FrameHeight));
        if (Math.Abs(actualWidth - cached.Width) > 8 ||
            Math.Abs(actualHeight - cached.Height) > 8)
        {
            return false;
        }

        selectedMode = new CameraMode(actualWidth, actualHeight, cached.Fps);
        return true;
    }

    private static VideoCapture OpenCapture(int index, bool allowAnyFallback)
    {
        if (OperatingSystem.IsWindows())
        {
            // Media Foundation opens substantially faster on modern Windows cameras.
            // Keep DirectShow as a compatibility fallback for older devices and drivers.
            var capture = new VideoCapture(index, VideoCaptureAPIs.MSMF);
            if (capture.IsOpened())
                return capture;

            capture.Dispose();
            capture = new VideoCapture(index, VideoCaptureAPIs.DSHOW);
            if (capture.IsOpened() || !allowAnyFallback)
                return capture;

            capture.Dispose();
            return new VideoCapture(index, VideoCaptureAPIs.ANY);
        }

        var preferred = OperatingSystem.IsLinux()
            ? VideoCaptureAPIs.V4L2
            : OperatingSystem.IsMacOS()
                ? VideoCaptureAPIs.AVFOUNDATION
                : VideoCaptureAPIs.ANY;

        var fallback = new VideoCapture(index, preferred);
        if (fallback.IsOpened() || preferred == VideoCaptureAPIs.ANY || !allowAnyFallback)
            return fallback;

        fallback.Dispose();
        return new VideoCapture(index, VideoCaptureAPIs.ANY);
    }

    private static CameraMode SelectHighestSupportedResolution(
        VideoCapture capture,
        CancellationToken cancellationToken)
    {
        var bestWidth = Math.Max(1, (int)Math.Round((double)capture.FrameWidth));
        var bestHeight = Math.Max(1, (int)Math.Round((double)capture.FrameHeight));
        var bestArea = (long)bestWidth * bestHeight;

        foreach (var mode in CandidateModes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            capture.Set(VideoCaptureProperties.FrameWidth, mode.Width);
            capture.Set(VideoCaptureProperties.FrameHeight, mode.Height);

            var actualWidth = Math.Max(1, (int)Math.Round((double)capture.FrameWidth));
            var actualHeight = Math.Max(1, (int)Math.Round((double)capture.FrameHeight));
            var area = (long)actualWidth * actualHeight;

            if (area > bestArea)
            {
                bestWidth = actualWidth;
                bestHeight = actualHeight;
                bestArea = area;
            }

            // CandidateModes is ordered from high to low. The first exact match is therefore the
            // highest mode accepted by the driver; no need to probe every smaller resolution.
            if (Math.Abs(actualWidth - mode.Width) <= 8 &&
                Math.Abs(actualHeight - mode.Height) <= 8)
            {
                bestWidth = actualWidth;
                bestHeight = actualHeight;
                break;
            }
        }

        capture.Set(VideoCaptureProperties.FrameWidth, bestWidth);
        capture.Set(VideoCaptureProperties.FrameHeight, bestHeight);
        capture.Set(VideoCaptureProperties.Fps, 30);
        return new CameraMode(bestWidth, bestHeight, 30);
    }

    private static async Task CaptureLoop(
        VideoCapture capture,
        Func<VideoFrame, ValueTask> onFrame,
        CancellationToken cancellationToken)
    {
        using var bgr = new Mat();
        using var bgra = new Mat();

        while (!cancellationToken.IsCancellationRequested)
        {
            if (!capture.Read(bgr) || bgr.Empty())
            {
                await Task.Delay(5, cancellationToken);
                continue;
            }

            Cv2.CvtColor(bgr, bgra, ColorConversionCodes.BGR2BGRA);
            var frame = VideoFrame.Rent(bgra.Width, bgra.Height, DateTime.UtcNow.Ticks);
            Marshal.Copy(bgra.Data, frame.Pixels, 0, frame.DataLength);

            try
            {
                var callback = onFrame(frame);
                if (!callback.IsCompletedSuccessfully)
                    await callback;
                // Ownership was transferred to onFrame.
            }
            catch
            {
                frame.Dispose();
                throw;
            }
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        var cts = Interlocked.Exchange(ref _captureCts, null);
        var task = Interlocked.Exchange(ref _captureTask, null);
        var capture = Interlocked.Exchange(ref _capture, null);

        cts?.Cancel();
        if (task is not null)
        {
            try
            {
                await task.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);
            }
            catch (OperationCanceledException)
            {
            }
            catch (TimeoutException)
            {
            }
        }

        capture?.Release();
        capture?.Dispose();
        cts?.Dispose();
    }

    public async ValueTask DisposeAsync() => await StopAsync();

    private readonly record struct CameraMode(int Width, int Height, int Fps);

    private static class CameraModeCache
    {
        private static readonly object SyncRoot = new();
        private static readonly string CachePath = BuildCachePath();
        private static Dictionary<string, CameraMode>? _modes;

        public static bool TryGet(string cameraId, out CameraMode mode)
        {
            lock (SyncRoot)
            {
                EnsureLoaded();
                return _modes!.TryGetValue(cameraId, out mode);
            }
        }

        public static void Save(string cameraId, CameraMode mode)
        {
            lock (SyncRoot)
            {
                EnsureLoaded();
                _modes![cameraId] = mode;

                try
                {
                    var directory = Path.GetDirectoryName(CachePath);
                    if (!string.IsNullOrEmpty(directory))
                        Directory.CreateDirectory(directory);

                    File.WriteAllText(
                        CachePath,
                        JsonSerializer.Serialize(_modes, new JsonSerializerOptions
                        {
                            WriteIndented = true
                        }));
                }
                catch (Exception ex)
                {
                    // A cache write failure must never prevent the camera from opening.
                    Debug.WriteLine($"保存摄像头分辨率缓存失败：{ex.Message}");
                }
            }
        }

        private static void EnsureLoaded()
        {
            if (_modes is not null)
                return;

            try
            {
                _modes = File.Exists(CachePath)
                    ? JsonSerializer.Deserialize<Dictionary<string, CameraMode>>(
                          File.ReadAllText(CachePath))
                      ?? new Dictionary<string, CameraMode>(StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, CameraMode>(StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"读取摄像头分辨率缓存失败：{ex.Message}");
                _modes = new Dictionary<string, CameraMode>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private static string BuildCachePath()
        {
            var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(root))
                root = Path.GetTempPath();

            return Path.Combine(root, "HelloV", "camera-modes.json");
        }
    }
}
