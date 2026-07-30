using Avalonia.Media;

namespace HelloV.Services;

/// <summary>
/// Supplies platform-native emoji images for renderers where color emoji glyphs are not exposed
/// through Avalonia/Skia. Implementations should cache images because animations request the same
/// emoji repeatedly.
/// </summary>
public interface IEmojiImageProvider : IDisposable
{
    IImage? GetEmojiImage(string emoji, int pixelSize);
}
