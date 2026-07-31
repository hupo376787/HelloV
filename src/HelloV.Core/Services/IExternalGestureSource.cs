using HelloV.Models;

namespace HelloV.Services;

public readonly record struct CameraFrameStatistics(
    int Width,
    int Height,
    double FramesPerSecond);

public enum ExternalModelStatus
{
    Loading,
    Ready,
    Missing,
    Failed
}

public readonly record struct ExternalModelState(
    ExternalModelStatus Status,
    string StateText,
    double LoadSeconds = 0,
    string? ErrorMessage = null);

/// <summary>
/// Allows a platform camera pipeline to perform recognition outside managed ONNX Runtime.
/// The browser implementation uses ONNX Runtime Web so the model can use WebGPU/WASM without
/// copying every camera frame through JavaScript interop into .NET WebAssembly.
/// </summary>
public interface IExternalGestureSource
{
    event Action<GestureFrameResult>? GestureFrameReady;
    event Action<CameraFrameStatistics>? FrameStatisticsChanged;
    event Action<ExternalModelState>? ModelStateChanged;
}

/// <summary>
/// Updates a platform-native preview when the shared mirror setting changes.
/// </summary>
public interface IPreviewMirroringController
{
    void SetPreviewMirroring(bool flipHorizontally);
}
