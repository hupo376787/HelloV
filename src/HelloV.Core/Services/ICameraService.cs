using HelloV.Models;

namespace HelloV.Services;

public interface ICameraService : IAsyncDisposable
{
    Task<IReadOnlyList<CameraDeviceInfo>> GetCamerasAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts frame delivery. Ownership of every frame is transferred to <paramref name="onFrame"/>;
    /// the callback must dispose it after retaining any asynchronous preview/inference references.
    /// </summary>
    Task StartAsync(
        CameraDeviceInfo camera,
        Func<VideoFrame, ValueTask> onFrame,
        CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);
}
