using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.VisualTree;
using HelloV.Models;

namespace HelloV.Controls;

/// <summary>
/// Realtime camera preview surface.
///
/// Camera pixels are copied directly from the pooled RGBA/BGRA byte array into one persistent
/// WriteableBitmap. Rotation and mirroring are applied by the renderer instead of rewriting the
/// full pixel buffer, which is especially important for 1080p Android preview at 30 FPS.
/// </summary>
public sealed class CameraPreviewControl : Control
{
    public static readonly StyledProperty<bool> FlipHorizontallyProperty =
        AvaloniaProperty.Register<CameraPreviewControl, bool>(nameof(FlipHorizontally));

    public static readonly StyledProperty<double> ViewportWidthProperty =
        AvaloniaProperty.Register<CameraPreviewControl, double>(nameof(ViewportWidth));

    public static readonly StyledProperty<bool> AlignViewportRightProperty =
        AvaloniaProperty.Register<CameraPreviewControl, bool>(nameof(AlignViewportRight));

    private WriteableBitmap? _bitmap;
    private VideoPixelFormat _bitmapPixelFormat;
    private int _frameRotationDegrees;
    private bool _frameMirrorHorizontally;

    /// <summary>
    /// Applies an additional horizontal flip after the camera-provided orientation transform.
    /// </summary>
    public bool FlipHorizontally
    {
        get => GetValue(FlipHorizontallyProperty);
        set => SetValue(FlipHorizontallyProperty, value);
    }

    /// <summary>
    /// Optional width of the complete camera viewport represented by this control. A smaller
    /// control can use this value together with <see cref="AlignViewportRight"/> to render the
    /// exact right-hand crop of the full preview, which is used by the desktop glass panel.
    /// </summary>
    public double ViewportWidth
    {
        get => GetValue(ViewportWidthProperty);
        set => SetValue(ViewportWidthProperty, value);
    }

    /// <summary>
    /// When <see cref="ViewportWidth"/> is wider than this control, display the right-hand slice
    /// instead of fitting the complete camera image into this control's own width.
    /// </summary>
    public bool AlignViewportRight
    {
        get => GetValue(AlignViewportRightProperty);
        set => SetValue(AlignViewportRightProperty, value);
    }

    /// <summary>
    /// Copies one packed RGBA8888/BGRA8888 camera frame into the persistent WriteableBitmap.
    /// Must be called on Avalonia's UI thread.
    /// </summary>
    public void Present(VideoFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        EnsureBitmap(frame.Width, frame.Height, frame.PixelFormat);
        _frameRotationDegrees = NormalizeRotation(frame.RotationDegrees);
        _frameMirrorHorizontally = frame.MirrorHorizontally;

        using (var framebuffer = _bitmap!.Lock())
        {
            CopyPixels(frame, framebuffer);
        }

        InvalidateVisual();
    }


    private static unsafe void CopyPixels(VideoFrame frame, ILockedFramebuffer framebuffer)
    {
        var sourceRowBytes = frame.RowBytes;
        fixed (byte* source = frame.Pixels)
        {
            if (framebuffer.RowBytes == sourceRowBytes)
            {
                Buffer.MemoryCopy(
                    source,
                    (void*)framebuffer.Address,
                    frame.DataLength,
                    frame.DataLength);
                return;
            }

            for (var y = 0; y < frame.Height; y++)
            {
                Buffer.MemoryCopy(
                    source + y * sourceRowBytes,
                    (byte*)framebuffer.Address + y * framebuffer.RowBytes,
                    framebuffer.RowBytes,
                    sourceRowBytes);
            }
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var bitmap = _bitmap;
        var viewWidth = Bounds.Width;
        var viewHeight = Bounds.Height;

        if (bitmap is null || viewWidth <= 0 || viewHeight <= 0)
            return;

        var imageWidth = bitmap.PixelSize.Width;
        var imageHeight = bitmap.PixelSize.Height;
        if (imageWidth <= 0 || imageHeight <= 0)
            return;

        var rotation = _frameRotationDegrees;
        var orientedWidth = rotation is 90 or 270 ? imageHeight : imageWidth;
        var orientedHeight = rotation is 90 or 270 ? imageWidth : imageHeight;

        var layoutWidth = ViewportWidth > 0 ? Math.Max(ViewportWidth, viewWidth) : viewWidth;
        var scale = Math.Max(layoutWidth / orientedWidth, viewHeight / orientedHeight);
        var offsetX = (layoutWidth - orientedWidth * scale) / 2;
        if (AlignViewportRight && layoutWidth > viewWidth)
            offsetX -= layoutWidth - viewWidth;

        var offsetY = (viewHeight - orientedHeight * scale) / 2;
        var mirror = _frameMirrorHorizontally ^ FlipHorizontally;
        var transform = CreateFrameTransform(
            imageWidth,
            imageHeight,
            rotation,
            mirror,
            scale,
            offsetX,
            offsetY);

        var source = new Rect(0, 0, imageWidth, imageHeight);
        var destination = source;

        using (context.PushClip(new Rect(0, 0, viewWidth, viewHeight)))
        using (context.PushTransform(transform))
            context.DrawImage(bitmap, source, destination);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == FlipHorizontallyProperty ||
            change.Property == ViewportWidthProperty ||
            change.Property == AlignViewportRightProperty)
        {
            InvalidateVisual();
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        DisposeBitmap();
    }

    private static Matrix CreateFrameTransform(
        int imageWidth,
        int imageHeight,
        int rotation,
        bool mirrorHorizontally,
        double scale,
        double offsetX,
        double offsetY)
    {
        double ax;
        double bx;
        double cx;
        double ay;
        double by;
        double cy;
        double orientedWidth;

        switch (rotation)
        {
            case 90:
                // (x, y) -> (height - y, x)
                ax = 0;
                bx = -1;
                cx = imageHeight;
                ay = 1;
                by = 0;
                cy = 0;
                orientedWidth = imageHeight;
                break;
            case 180:
                // (x, y) -> (width - x, height - y)
                ax = -1;
                bx = 0;
                cx = imageWidth;
                ay = 0;
                by = -1;
                cy = imageHeight;
                orientedWidth = imageWidth;
                break;
            case 270:
                // (x, y) -> (y, width - x)
                ax = 0;
                bx = 1;
                cx = 0;
                ay = -1;
                by = 0;
                cy = imageWidth;
                orientedWidth = imageHeight;
                break;
            default:
                ax = 1;
                bx = 0;
                cx = 0;
                ay = 0;
                by = 1;
                cy = 0;
                orientedWidth = imageWidth;
                break;
        }

        if (mirrorHorizontally)
        {
            ax = -ax;
            bx = -bx;
            cx = orientedWidth - cx;
        }

        return new Matrix(
            ax * scale,
            ay * scale,
            bx * scale,
            by * scale,
            cx * scale + offsetX,
            cy * scale + offsetY);
    }

    private static int NormalizeRotation(int rotation)
    {
        rotation = ((rotation % 360) + 360) % 360;
        return rotation is 0 or 90 or 180 or 270 ? rotation : 0;
    }

    private void EnsureBitmap(int width, int height, VideoPixelFormat pixelFormat)
    {
        if (_bitmap is not null &&
            _bitmap.PixelSize.Width == width &&
            _bitmap.PixelSize.Height == height &&
            _bitmapPixelFormat == pixelFormat)
        {
            return;
        }

        DisposeBitmap();
        var avaloniaPixelFormat = pixelFormat == VideoPixelFormat.Rgba8888
            ? PixelFormat.Rgba8888
            : PixelFormat.Bgra8888;
        _bitmap = new WriteableBitmap(
            new PixelSize(width, height),
            new Vector(96, 96),
            avaloniaPixelFormat,
            AlphaFormat.Unpremul);
        _bitmapPixelFormat = pixelFormat;
    }

    private void DisposeBitmap()
    {
        _bitmap?.Dispose();
        _bitmap = null;
    }
}
