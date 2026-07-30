using System.Collections.Concurrent;
using Avalonia.Media;
using AvaloniaBitmap = Avalonia.Media.Imaging.Bitmap;
using AndroidBitmap = global::Android.Graphics.Bitmap;
using AndroidCanvas = global::Android.Graphics.Canvas;
using AndroidColor = global::Android.Graphics.Color;
using AndroidPaint = global::Android.Graphics.Paint;
using AndroidPaintFlags = global::Android.Graphics.PaintFlags;
using HelloV.Services;

namespace HelloV.Android;

/// <summary>
/// Renders Unicode emoji through Android's native text stack. This uses the device's color emoji
/// font and then exposes the result as an Avalonia image, avoiding missing-glyph squares in Skia.
/// </summary>
public sealed class AndroidEmojiImageProvider : IEmojiImageProvider
{
    private readonly ConcurrentDictionary<EmojiCacheKey, Lazy<IImage?>> _cache = new();
    private int _disposed;

    public IImage? GetEmojiImage(string emoji, int pixelSize)
    {
        if (string.IsNullOrWhiteSpace(emoji) || Volatile.Read(ref _disposed) != 0)
            return null;

        // Quantization prevents the pop animation from creating a new bitmap for every frame.
        var quantizedSize = Math.Clamp((int)Math.Round(pixelSize / 8d) * 8, 24, 384);
        var key = new EmojiCacheKey(emoji, quantizedSize);
        return _cache.GetOrAdd(
            key,
            static value => new Lazy<IImage?>(
                () => Render(value),
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    }

    private static IImage? Render(EmojiCacheKey key)
    {
        try
        {
            var canvasSize = Math.Max(48, (int)Math.Ceiling(key.PixelSize * 1.45));
            using var androidBitmap = AndroidBitmap.CreateBitmap(
                canvasSize,
                canvasSize,
                AndroidBitmap.Config.Argb8888!);
            if (androidBitmap is null)
                return null;

            androidBitmap.EraseColor(AndroidColor.Transparent);
            using var canvas = new AndroidCanvas(androidBitmap);
            using var paint = new AndroidPaint(
                AndroidPaintFlags.AntiAlias |
                AndroidPaintFlags.SubpixelText |
                AndroidPaintFlags.LinearText |
                AndroidPaintFlags.EmbeddedBitmapText)
            {
                Color = AndroidColor.White,
                TextAlign = AndroidPaint.Align.Center,
                TextSize = key.PixelSize
            };

            // Keep Paint's platform default typeface. Android's native text stack
            // performs fallback to the installed system color Emoji font.

            using var metrics = paint.GetFontMetrics();
            var ascent = metrics?.Ascent ?? -key.PixelSize * 0.8f;
            var descent = metrics?.Descent ?? key.PixelSize * 0.2f;
            var baseline = canvasSize / 2f - (ascent + descent) / 2f;
            canvas.DrawText(key.Emoji, canvasSize / 2f, baseline, paint);

            using var stream = new MemoryStream();
            if (!androidBitmap.Compress(AndroidBitmap.CompressFormat.Png!, 100, stream))
                return null;

            stream.Position = 0;
            return new AvaloniaBitmap(stream);
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        foreach (var item in _cache.Values)
        {
            if (item.IsValueCreated && item.Value is IDisposable disposable)
                disposable.Dispose();
        }

        _cache.Clear();
    }

    private readonly record struct EmojiCacheKey(string Emoji, int PixelSize);
}
