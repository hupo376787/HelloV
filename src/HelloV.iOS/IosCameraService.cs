using System.Runtime.InteropServices;
using AVFoundation;
using CoreFoundation;
using CoreMedia;
using CoreVideo;
using Foundation;
using HelloV.Models;
using HelloV.Services;

namespace HelloV.iOS;

public sealed class IosCameraService : ICameraService
{
    private AVCaptureSession? _session;
    private AVCaptureVideoDataOutput? _output;
    private SampleBufferDelegate? _delegate;
    private DispatchQueue? _queue;

    public Task<IReadOnlyList<CameraDeviceInfo>> GetCamerasAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<CameraDeviceInfo> cameras = AVCaptureDevice.Devices
            .Where(x => x.HasMediaType(AVMediaTypes.Video))
            .Select((device, index) => new CameraDeviceInfo(
                device.UniqueID,
                device.Position switch
                {
                    AVCaptureDevicePosition.Front => "前置摄像头",
                    AVCaptureDevicePosition.Back => "后置摄像头",
                    _ => device.LocalizedName
                },
                device.Position switch
                {
                    AVCaptureDevicePosition.Front => CameraFacing.Front,
                    AVCaptureDevicePosition.Back => CameraFacing.Back,
                    _ => CameraFacing.Unknown
                },
                index))
            .OrderBy(x => x.Facing == CameraFacing.Front ? 0 : x.Facing == CameraFacing.Back ? 1 : 2)
            .ToArray();
        return Task.FromResult(cameras);
    }

    public async Task StartAsync(
        CameraDeviceInfo camera,
        Func<VideoFrame, ValueTask> onFrame,
        CancellationToken cancellationToken = default)
    {
        await EnsurePermissionAsync(cancellationToken);
        await StopAsync(cancellationToken);

        var device = AVCaptureDevice.Devices.FirstOrDefault(x => x.UniqueID == camera.Id)
                     ?? throw new InvalidOperationException("找不到所选摄像头");

        SelectHighestResolution(device);
        var input = AVCaptureDeviceInput.FromDevice(device)
                    ?? throw new InvalidOperationException("无法创建摄像头输入");

        _session = new AVCaptureSession();
        _session.BeginConfiguration();
        if (!_session.CanAddInput(input))
            throw new InvalidOperationException("无法向会话添加摄像头输入");
        _session.AddInput(input);

        _output = new AVCaptureVideoDataOutput
        {
            AlwaysDiscardsLateVideoFrames = true,
            WeakVideoSettings = NSDictionary.FromObjectAndKey(
                NSNumber.FromUInt32((uint)CVPixelFormatType.CV32BGRA),
                CVPixelBuffer.PixelFormatTypeKey)
        };

        if (!_session.CanAddOutput(_output))
            throw new InvalidOperationException("无法向会话添加视频输出");
        _session.AddOutput(_output);

        _queue = new DispatchQueue("gesture-camera-ios");
        _delegate = new SampleBufferDelegate(onFrame);
        _output.SetSampleBufferDelegate(_delegate, _queue);

        // AVCaptureVideoDataOutput has only the video connection configured above. Reading the
        // generated Connections collection avoids passing the legacy AVMediaTypes smart enum to
        // ConnectionFromMediaType, whose .NET 10 binding now requires an NSString.
        var connection = _output.Connections.FirstOrDefault();
        if (connection is not null)
        {
            if (connection.SupportsVideoOrientation)
                connection.VideoOrientation = AVCaptureVideoOrientation.Portrait;
            if (connection.SupportsVideoMirroring)
            {
                connection.AutomaticallyAdjustsVideoMirroring = false;
                connection.VideoMirrored = camera.Facing == CameraFacing.Front;
            }
        }

        _session.CommitConfiguration();
        _session.StartRunning();
    }

    private static void SelectHighestResolution(AVCaptureDevice device)
    {
        var best = device.Formats
            .Select(format => new
            {
                Format = format,
                Dimensions = (format.FormatDescription as CMVideoFormatDescription)?.Dimensions
            })
            .Where(x => x.Dimensions.HasValue)
            .OrderByDescending(x => (long)x.Dimensions!.Value.Width * x.Dimensions.Value.Height)
            .FirstOrDefault();

        if (best is null || !device.LockForConfiguration(out _))
            return;
        try
        {
            device.ActiveFormat = best.Format;
        }
        finally
        {
            device.UnlockForConfiguration();
        }
    }

    private static async Task EnsurePermissionAsync(CancellationToken cancellationToken)
    {
        var status = AVCaptureDevice.GetAuthorizationStatus(AVAuthorizationMediaType.Video);
        if (status == AVAuthorizationStatus.Authorized)
            return;
        if (status == AVAuthorizationStatus.Denied || status == AVAuthorizationStatus.Restricted)
            throw new UnauthorizedAccessException("未获得相机权限");

        var granted = await AVCaptureDevice.RequestAccessForMediaTypeAsync(AVAuthorizationMediaType.Video)
            .WaitAsync(cancellationToken);
        if (!granted)
            throw new UnauthorizedAccessException("未获得相机权限");
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_session?.Running == true)
            _session.StopRunning();

        _delegate?.Dispose();
        _delegate = null;
        _output?.Dispose();
        _output = null;
        _session?.Dispose();
        _session = null;
        _queue?.Dispose();
        _queue = null;
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await StopAsync();

    private sealed class SampleBufferDelegate(Func<VideoFrame, ValueTask> onFrame)
        : AVCaptureVideoDataOutputSampleBufferDelegate
    {
        private long _lastFrameTicks;

        public override void DidOutputSampleBuffer(
            AVCaptureOutput captureOutput,
            CMSampleBuffer sampleBuffer,
            AVCaptureConnection connection)
        {
            var now = DateTime.UtcNow.Ticks;
            if (now - Interlocked.Read(ref _lastFrameTicks) < TimeSpan.TicksPerSecond / 30)
                return;
            Interlocked.Exchange(ref _lastFrameTicks, now);

            var pixelBuffer = sampleBuffer.GetImageBuffer() as CVPixelBuffer;
            if (pixelBuffer is null)
                return;

            pixelBuffer.Lock(CVPixelBufferLock.ReadOnly);
            try
            {
                var width = checked((int)pixelBuffer.Width);
                var height = checked((int)pixelBuffer.Height);
                var srcStride = checked((int)pixelBuffer.BytesPerRow);
                var frame = VideoFrame.Rent(width, height, now);
                try
                {
                    var rowBytes = frame.RowBytes;
                    for (var y = 0; y < height; y++)
                    {
                        Marshal.Copy(
                            IntPtr.Add(pixelBuffer.BaseAddress, y * srcStride),
                            frame.Pixels,
                            y * rowBytes,
                            rowBytes);
                    }

                    var callback = onFrame(frame);
                    if (!callback.IsCompletedSuccessfully)
                        _ = ObserveCallbackAsync(callback, frame);
                    // Ownership was transferred to onFrame.
                }
                catch
                {
                    frame.Dispose();
                }
            }
            finally
            {
                pixelBuffer.Unlock(CVPixelBufferLock.ReadOnly);
            }
        }

        private static async Task ObserveCallbackAsync(ValueTask callback, VideoFrame frame)
        {
            try
            {
                await callback;
            }
            catch
            {
                frame.Dispose();
            }
        }
    }
}
