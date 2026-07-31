using System.Collections.Concurrent;
using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using HelloV.Services;

namespace HelloV.Browser;

/// <summary>
/// Renders emoji with the browser's native emoji font on an off-screen HTML canvas and copies the
/// resulting RGBA bitmap into Avalonia. This avoids missing-glyph boxes in CanvasKit/Skia where
/// operating-system color emoji fonts aren't available to the WebAssembly renderer.
/// </summary>
[SupportedOSPlatform("browser")]
internal sealed class BrowserEmojiImageProvider : IEmojiImageProvider
{
    private readonly ConcurrentDictionary<string, IImage> _cache = new(StringComparer.Ordinal);
    private int _disposed;

    public IImage? GetEmojiImage(string emoji, int pixelSize)
    {
        if (Volatile.Read(ref _disposed) != 0 || string.IsNullOrWhiteSpace(emoji))
            return null;

        pixelSize = Math.Clamp(pixelSize, 16, 512);
        var key = $"{emoji}\u001f{pixelSize}";
        if (_cache.TryGetValue(key, out var cached))
            return cached;

        var created = CreateImage(emoji, pixelSize);
        if (created is null)
            return null;

        var result = _cache.GetOrAdd(key, created);
        if (!ReferenceEquals(result, created) && created is IDisposable disposable)
            disposable.Dispose();
        return result;
    }

    private static unsafe IImage? CreateImage(string emoji, int pixelSize)
    {
        try
        {
            var packet = BrowserInterop.RenderEmoji(emoji, pixelSize);
            if (packet is null || packet.Length < 8)
                return null;

            var width = ReadInt32LittleEndian(packet, 0);
            var height = ReadInt32LittleEndian(packet, 4);
            if (width <= 0 || height <= 0)
                return null;

            var pixelLength = checked(width * height * 4);
            if (packet.Length != pixelLength + 8)
                return null;

            var bitmap = new WriteableBitmap(
                new PixelSize(width, height),
                new Vector(96, 96),
                PixelFormat.Rgba8888,
                AlphaFormat.Unpremul);

            using var framebuffer = bitmap.Lock();
            fixed (byte* source = &packet[8])
            {
                var sourceRowBytes = width * 4;
                if (framebuffer.RowBytes == sourceRowBytes)
                {
                    Buffer.MemoryCopy(source, (void*)framebuffer.Address, pixelLength, pixelLength);
                }
                else
                {
                    for (var y = 0; y < height; y++)
                    {
                        Buffer.MemoryCopy(
                            source + y * sourceRowBytes,
                            (byte*)framebuffer.Address + y * framebuffer.RowBytes,
                            framebuffer.RowBytes,
                            sourceRowBytes);
                    }
                }
            }

            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private static int ReadInt32LittleEndian(byte[] data, int offset) =>
        data[offset] |
        data[offset + 1] << 8 |
        data[offset + 2] << 16 |
        data[offset + 3] << 24;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        foreach (var image in _cache.Values)
        {
            if (image is IDisposable disposable)
                disposable.Dispose();
        }

        _cache.Clear();
    }
}
