using Android;
using Android.Content;
using Android.Content.PM;
using Android.Hardware.Camera2;
using Android.OS;
using AndroidX.Camera.Core;
using AndroidX.Camera.Lifecycle;
using CameraAspectRatioStrategy = AndroidX.Camera.Core.ResolutionSelector.AspectRatioStrategy;
using CameraResolutionSelector = AndroidX.Camera.Core.ResolutionSelector.ResolutionSelector;
using CameraResolutionStrategy = AndroidX.Camera.Core.ResolutionSelector.ResolutionStrategy;
using AndroidX.Core.Content;
using AndroidX.Lifecycle;
using HelloV.Models;
using HelloV.Services;
using AndroidActivity = global::Android.App.Activity;
using AndroidLog = global::Android.Util.Log;
using AndroidSize = global::Android.Util.Size;
using Stopwatch = System.Diagnostics.Stopwatch;
using JavaExecutors = Java.Util.Concurrent.Executors;
using JavaExecutorService = Java.Util.Concurrent.IExecutorService;

namespace HelloV.Android;

/// <summary>
/// Android camera implementation based on CameraX ImageAnalysis.
///
/// CameraX performs the camera YUV -> RGBA conversion in its native image-processing path.
/// The analyzer only copies the already packed RGBA plane into the reusable VideoFrame pool.
/// This avoids the former managed per-pixel 1080p conversion, which took roughly 140-150 ms
/// per frame on the test device and limited the preview to about 6-7 FPS.
/// </summary>
public sealed class AndroidCameraService : ICameraService
{
    private const int PermissionRequestCode = 4201;
    private const int MaxWidth = 1920;
    private const int MaxHeight = 1080;

    private readonly AndroidActivity _activity;
    private readonly CameraManager _cameraManager;
    private readonly JavaExecutorService _analysisExecutor;
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);

    private ProcessCameraProvider? _cameraProvider;
    private ImageAnalysis? _imageAnalysis;
    private RgbaAnalyzer? _analyzer;
    private int _disposed;

    public AndroidCameraService(AndroidActivity activity)
    {
        _activity = activity ?? throw new ArgumentNullException(nameof(activity));
        _cameraManager = (CameraManager?)activity.GetSystemService(Context.CameraService)
            ?? throw new InvalidOperationException("无法获取 Android 摄像头服务");
        _analysisExecutor = JavaExecutors.NewSingleThreadExecutor()
            ?? throw new InvalidOperationException("无法创建 CameraX 图像分析线程");
    }

    public Task<IReadOnlyList<CameraDeviceInfo>> GetCamerasAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        IReadOnlyList<CameraDeviceInfo> devices = _cameraManager.GetCameraIdList()
            .Select((id, index) =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                var characteristics = _cameraManager.GetCameraCharacteristics(id);
                var facingValue =
                    (characteristics.Get(CameraCharacteristics.LensFacing)
                     as Java.Lang.Integer)?.IntValue() ?? -1;

                var facing = facingValue switch
                {
                    (int)LensFacing.Front => CameraFacing.Front,
                    (int)LensFacing.Back => CameraFacing.Back,
                    (int)LensFacing.External => CameraFacing.External,
                    _ => CameraFacing.Unknown
                };

                var displayName = facing switch
                {
                    CameraFacing.Front => "前置摄像头",
                    CameraFacing.Back => "后置摄像头",
                    CameraFacing.External => "外接摄像头",
                    _ => $"摄像头 {index + 1}"
                };

                return new CameraDeviceInfo(id, displayName, facing, index);
            })
            .OrderBy(x => x.Facing == CameraFacing.Front ? 0 :
                          x.Facing == CameraFacing.Back ? 1 : 2)
            .ToArray();

        return Task.FromResult(devices);
    }

    public async Task StartAsync(
        CameraDeviceInfo camera,
        Func<VideoFrame, ValueTask> onFrame,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(camera);
        ArgumentNullException.ThrowIfNull(onFrame);
        ThrowIfDisposed();

        await EnsurePermissionAsync(cancellationToken).ConfigureAwait(false);
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            await StopCoreAsync().ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            var lifecycleOwner = _activity as ILifecycleOwner
                ?? throw new InvalidOperationException("当前 Android Activity 不支持 CameraX 生命周期");
            var provider = _cameraProvider ??=
                await GetCameraProviderAsync(cancellationToken).ConfigureAwait(false);

            var analyzer = new RgbaAnalyzer(
                onFrame,
                camera.Facing == CameraFacing.Front);
            var imageAnalysis = BuildImageAnalysis();
            imageAnalysis.SetAnalyzer(_analysisExecutor, analyzer);

            try
            {
                await RunOnMainThreadAsync(() =>
                {
                    provider.UnbindAll();

                    var lensFacing = camera.Facing == CameraFacing.Front
                        ? CameraSelector.LensFacingFront
                        : CameraSelector.LensFacingBack;
                    using var selectorBuilder = new CameraSelector.Builder();
                    selectorBuilder.RequireLensFacing(lensFacing);
                    using var selector = selectorBuilder.Build()
                        ?? throw new InvalidOperationException("CameraX 无法创建摄像头选择器");

                    // Bind only one RGBA ImageAnalysis use case. With no competing Preview or
                    // ImageCapture use case, CameraX can honor the requested 1080p analysis size
                    // on devices that expose it and can select an efficient Camera2 stream combo.
                    provider.BindToLifecycle(lifecycleOwner, selector, imageAnalysis);
                }).ConfigureAwait(false);
            }
            catch
            {
                analyzer.Stop();
                imageAnalysis.ClearAnalyzer();
                imageAnalysis.Dispose();
                throw;
            }

            _analyzer = analyzer;
            _imageAnalysis = imageAnalysis;

            AndroidLog.Info(
                "HelloV",
                $"CameraX started: facing={camera.Facing}, max={MaxWidth}x{MaxHeight}, " +
                "format=RGBA_8888, backpressure=KEEP_ONLY_LATEST");
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    private static ImageAnalysis BuildImageAnalysis()
    {
        // SetTargetResolution(1920x1080) is only a target/minimum hint: CameraX is allowed to
        // choose a larger stream when the exact size is unavailable. On phones that can mean a
        // 2K/4K RGBA analysis stream, which is unnecessarily expensive because every frame is
        // converted to RGBA and copied into the Avalonia preview pipeline.
        //
        // ResolutionSelector with CLOSEST_LOWER makes Full HD a real upper preference: use
        // 1920x1080 when available, otherwise fall back to the nearest LOWER 16:9 resolution.
        // PREFER_CAPTURE_RATE_OVER_HIGHER_RESOLUTION also keeps CameraX away from slow high-res
        // sensor modes.
        using var resolutionStrategy = new CameraResolutionStrategy(
            new AndroidSize(MaxWidth, MaxHeight),
            CameraResolutionStrategy.FallbackRuleClosestLower);
        using var resolutionSelectorBuilder = new CameraResolutionSelector.Builder();
        resolutionSelectorBuilder.SetAspectRatioStrategy(
            CameraAspectRatioStrategy.Ratio169FallbackAutoStrategy);
        resolutionSelectorBuilder.SetResolutionStrategy(resolutionStrategy);
        resolutionSelectorBuilder.SetAllowedResolutionMode(
            CameraResolutionSelector.PreferCaptureRateOverHigherResolution);
        using var resolutionSelector = resolutionSelectorBuilder.Build()
            ?? throw new InvalidOperationException("CameraX 无法创建分辨率选择器");

        using var builder = new ImageAnalysis.Builder();
        builder.SetResolutionSelector(resolutionSelector);
        builder.SetBackpressureStrategy(ImageAnalysis.StrategyKeepOnlyLatest);
        builder.SetOutputImageFormat(ImageAnalysis.OutputImageFormatRgba8888);
        return builder.Build()
            ?? throw new InvalidOperationException("CameraX 无法创建图像分析用例");
    }

    private async Task<ProcessCameraProvider> GetCameraProviderAsync(
        CancellationToken cancellationToken)
    {
        var future = ProcessCameraProvider.GetInstance(_activity)
            ?? throw new InvalidOperationException("CameraX 无法创建摄像头提供程序任务");
        var completion = new TaskCompletionSource<ProcessCameraProvider>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var runnable = new ActionRunnable(() =>
        {
            try
            {
                var provider = future.Get() as ProcessCameraProvider
                    ?? throw new InvalidOperationException("CameraX 未返回有效的摄像头提供程序");
                completion.TrySetResult(provider);
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        });

        future.AddListener(runnable, ContextCompat.GetMainExecutor(_activity));

        using var registration = cancellationToken.Register(
            static state =>
                ((TaskCompletionSource<ProcessCameraProvider>)state!).TrySetCanceled(),
            completion);

        return await completion.Task.ConfigureAwait(false);
    }

    private async Task EnsurePermissionAsync(CancellationToken cancellationToken)
    {
        if (_activity.CheckSelfPermission(Manifest.Permission.Camera) == Permission.Granted)
            return;

        _activity.RequestPermissions(
            [Manifest.Permission.Camera],
            PermissionRequestCode);

        for (var i = 0; i < 150; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_activity.CheckSelfPermission(Manifest.Permission.Camera) == Permission.Granted)
                return;

            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
        }

        throw new UnauthorizedAccessException("未获得相机权限");
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _disposed) != 0)
            return;

        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await StopCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    private async Task StopCoreAsync()
    {
        var analyzer = _analyzer;
        _analyzer = null;
        analyzer?.Stop();

        var imageAnalysis = _imageAnalysis;
        _imageAnalysis = null;
        if (imageAnalysis is not null)
        {
            try
            {
                imageAnalysis.ClearAnalyzer();
            }
            catch
            {
            }
        }

        var provider = _cameraProvider;
        if (provider is not null)
        {
            try
            {
                await RunOnMainThreadAsync(provider.UnbindAll).ConfigureAwait(false);
            }
            catch (Java.Lang.IllegalStateException)
            {
            }
        }

        if (analyzer is not null)
            await analyzer.WaitForIdleAsync().ConfigureAwait(false);

        imageAnalysis?.Dispose();
    }

    private Task RunOnMainThreadAsync(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (Looper.MyLooper() == Looper.MainLooper)
        {
            action();
            return Task.CompletedTask;
        }

        var completion = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _activity.RunOnUiThread(() =>
        {
            try
            {
                action();
                completion.TrySetResult(true);
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        });
        return completion.Task;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        await _lifecycleGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await StopCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            _lifecycleGate.Release();
        }

        try
        {
            _analysisExecutor.ShutdownNow();
        }
        catch
        {
        }

        _lifecycleGate.Dispose();
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
            throw new ObjectDisposedException(nameof(AndroidCameraService));
    }

    private sealed class ActionRunnable(Action action) : Java.Lang.Object, Java.Lang.IRunnable
    {
        public void Run() => action();
    }

    private sealed class RgbaAnalyzer(
        Func<VideoFrame, ValueTask> onFrame,
        bool mirrorHorizontally) : Java.Lang.Object, ImageAnalysis.IAnalyzer
    {
        private readonly TaskCompletionSource<bool> _idle = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private long _statsStarted = Stopwatch.GetTimestamp();
        private long _copyTicks;
        private long _dispatchTicks;
        private int _frames;
        private int _directFrames;
        private int _activeCallbacks;
        private int _active = 1;
        private byte[] _fallbackBuffer = [];

        AndroidSize ImageAnalysis.IAnalyzer.DefaultTargetResolution => null!;

        public void Stop()
        {
            Volatile.Write(ref _active, 0);
            if (Volatile.Read(ref _activeCallbacks) == 0)
                _idle.TrySetResult(true);
        }

        public async Task WaitForIdleAsync()
        {
            if (Volatile.Read(ref _activeCallbacks) == 0)
                return;

            try
            {
                await _idle.Task.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
            }
            catch (System.TimeoutException)
            {
            }
        }

        public void Analyze(IImageProxy? image)
        {
            if (image is null)
                return;

            Interlocked.Increment(ref _activeCallbacks);
            VideoFrame? frame = null;
            try
            {
                if (Volatile.Read(ref _active) == 0)
                    return;

                var copyStarted = Stopwatch.GetTimestamp();
                frame = CopyRgbaFrame(image, out var usedDirectBuffer);
                var copyElapsed = Stopwatch.GetTimestamp() - copyStarted;

                // Release CameraX's ImageProxy immediately after the owned frame copy. This is
                // critical for KEEP_ONLY_LATEST: the next newest image cannot be delivered until
                // the current ImageProxy is closed.
                image.Close();

                if (Volatile.Read(ref _active) == 0)
                {
                    frame.Dispose();
                    frame = null;
                    return;
                }

                var dispatchStarted = Stopwatch.GetTimestamp();
                var callback = onFrame(frame);
                var dispatchElapsed = Stopwatch.GetTimestamp() - dispatchStarted;
                RecordFrame(
                    frame.Width,
                    frame.Height,
                    copyElapsed,
                    dispatchElapsed,
                    usedDirectBuffer);

                // Ownership was transferred to onFrame. The callback is normally synchronous and
                // retains only the latest preview/inference references. Observe an asynchronous
                // failure without blocking the CameraX analyzer thread.
                frame = null;
                if (!callback.IsCompletedSuccessfully)
                    _ = ObserveCallbackAsync(callback);
            }
            catch (Exception ex)
            {
                frame?.Dispose();
                AndroidLog.Warn("HelloV", $"CameraX frame failed: {ex.Message}");
            }
            finally
            {
                try
                {
                    image.Close();
                }
                catch
                {
                }

                if (Interlocked.Decrement(ref _activeCallbacks) == 0 &&
                    Volatile.Read(ref _active) == 0)
                {
                    _idle.TrySetResult(true);
                }
            }
        }

        private unsafe VideoFrame CopyRgbaFrame(
            IImageProxy image,
            out bool usedDirectBuffer)
        {
            var planes = image.GetPlanes()
                ?? throw new InvalidOperationException("CameraX RGBA 图像没有像素平面");
            if (planes.Length == 0)
                throw new InvalidOperationException("CameraX RGBA 图像像素平面为空");

            var plane = planes[0];
            var buffer = plane.Buffer
                ?? throw new InvalidOperationException("CameraX RGBA 缓冲区为空");
            var width = image.Width;
            var height = image.Height;
            var sourceRowBytes = plane.RowStride;
            var pixelStride = plane.PixelStride;

            if (width <= 0 || height <= 0)
                throw new InvalidOperationException("CameraX 返回了无效图像尺寸");
            if (pixelStride != 4)
                throw new InvalidOperationException($"CameraX RGBA PixelStride 应为 4，实际为 {pixelStride}");
            if (sourceRowBytes < checked(width * 4))
                throw new InvalidOperationException("CameraX RGBA RowStride 小于有效像素行长度");

            var rotation = image.ImageInfo?.RotationDegrees ?? 0;
            var frame = VideoFrame.Rent(
                width,
                height,
                DateTime.UtcNow.Ticks,
                rotation,
                mirrorHorizontally,
                VideoPixelFormat.Rgba8888);

            try
            {
                buffer.Rewind();
                var sourceLength = buffer.Remaining();
                var requiredLength = checked((height - 1) * sourceRowBytes + width * 4);
                if (sourceLength < requiredLength)
                {
                    throw new InvalidOperationException(
                        $"CameraX RGBA 缓冲区不足：需要 {requiredLength}，实际 {sourceLength}");
                }

                usedDirectBuffer = buffer.IsDirect;
                fixed (byte* destination = frame.Pixels)
                {
                    if (buffer.IsDirect)
                    {
                        var sourceAddress = buffer.GetDirectBufferAddress();
                        if (sourceAddress == 0)
                            throw new InvalidOperationException("无法取得 CameraX RGBA 直接缓冲区地址");

                        CopyRows(
                            (byte*)sourceAddress,
                            destination,
                            width,
                            height,
                            sourceRowBytes,
                            frame.RowBytes);
                    }
                    else
                    {
                        if (_fallbackBuffer.Length < sourceLength)
                            _fallbackBuffer = new byte[sourceLength];

                        buffer.Get(_fallbackBuffer, 0, sourceLength);
                        fixed (byte* source = _fallbackBuffer)
                        {
                            CopyRows(
                                source,
                                destination,
                                width,
                                height,
                                sourceRowBytes,
                                frame.RowBytes);
                        }
                    }
                }

                return frame;
            }
            catch
            {
                frame.Dispose();
                throw;
            }
        }

        private static unsafe void CopyRows(
            byte* source,
            byte* destination,
            int width,
            int height,
            int sourceRowBytes,
            int destinationRowBytes)
        {
            var validRowBytes = checked(width * 4);
            if (sourceRowBytes == destinationRowBytes)
            {
                var totalBytes = checked(validRowBytes * height);
                Buffer.MemoryCopy(source, destination, totalBytes, totalBytes);
                return;
            }

            for (var y = 0; y < height; y++)
            {
                Buffer.MemoryCopy(
                    source + y * sourceRowBytes,
                    destination + y * destinationRowBytes,
                    destinationRowBytes,
                    validRowBytes);
            }
        }

        private void RecordFrame(
            int width,
            int height,
            long copyTicks,
            long dispatchTicks,
            bool direct)
        {
            _copyTicks += copyTicks;
            _dispatchTicks += dispatchTicks;
            _frames++;
            if (direct)
                _directFrames++;

            var elapsed = Stopwatch.GetElapsedTime(_statsStarted);
            if (elapsed < TimeSpan.FromSeconds(2))
                return;

            var copyMs = _copyTicks * 1000d / Stopwatch.Frequency / _frames;
            var dispatchMs = _dispatchTicks * 1000d / Stopwatch.Frequency / _frames;
            AndroidLog.Info(
                "HelloV",
                $"CameraX RGBA: {width}x{height}, {_frames / elapsed.TotalSeconds:F1} FPS, " +
                $"copy={copyMs:F2} ms, dispatch={dispatchMs:F2} ms, " +
                $"direct={_directFrames}/{_frames}");

            _statsStarted = Stopwatch.GetTimestamp();
            _copyTicks = 0;
            _dispatchTicks = 0;
            _frames = 0;
            _directFrames = 0;
        }

        private static async Task ObserveCallbackAsync(ValueTask callback)
        {
            try
            {
                await callback.ConfigureAwait(false);
            }
            catch
            {
            }
        }
    }
}
