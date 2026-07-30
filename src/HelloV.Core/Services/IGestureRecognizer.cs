using HelloV.Models;

namespace HelloV.Services;

public interface IGestureRecognizer : IDisposable
{
    bool IsReady { get; }
    string StateText { get; }
    GestureFrameResult Recognize(VideoFrame frame);
}
