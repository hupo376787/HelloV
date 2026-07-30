using System.Buffers;

namespace HelloV.Models;

public enum VideoPixelFormat
{
    Bgra8888,
    Rgba8888
}

/// <summary>
/// Packed four-channel frame backed by a reference-counted ArrayPool buffer.
/// The callback receiving a frame owns it and must dispose it. Call <see cref="Retain"/>
/// before handing the same pixels to another asynchronous consumer.
/// </summary>
public sealed class VideoFrame : IDisposable
{
    // The configurable pool explicitly keeps common 720p/1080p four-channel arrays. Some mobile
    // runtimes are conservative about caching multi-megabyte arrays in ArrayPool.Shared, which
    // otherwise creates continuous large-object GC pressure during realtime preview.
    private const int RealtimePoolMaximumLength = 16 * 1024 * 1024;
    private static readonly ArrayPool<byte> RealtimeFramePool =
        ArrayPool<byte>.Create(RealtimePoolMaximumLength, maxArraysPerBucket: 4);

    private readonly SharedBuffer _buffer;
    private int _disposed;

    private VideoFrame(
        SharedBuffer buffer,
        int width,
        int height,
        long timestampTicks,
        int rotationDegrees,
        bool mirrorHorizontally,
        VideoPixelFormat pixelFormat)
    {
        _buffer = buffer;
        Width = width;
        Height = height;
        TimestampTicks = timestampTicks;
        RotationDegrees = rotationDegrees;
        MirrorHorizontally = mirrorHorizontally;
        PixelFormat = pixelFormat;
    }

    public byte[] Pixels => _buffer.Array;

    /// <summary>
    /// Compatibility alias for older platform camera services. New code should use
    /// <see cref="Pixels"/> together with <see cref="PixelFormat"/>.
    /// </summary>
    public byte[] Bgra => Pixels;

    public int Width { get; }
    public int Height { get; }
    public long TimestampTicks { get; }
    public int RotationDegrees { get; }
    public bool MirrorHorizontally { get; }
    public VideoPixelFormat PixelFormat { get; }
    public int RowBytes => checked(Width * 4);
    public int DataLength => checked(RowBytes * Height);

    public static VideoFrame Rent(
        int width,
        int height,
        long timestampTicks,
        int rotationDegrees = 0,
        bool mirrorHorizontally = false,
        VideoPixelFormat pixelFormat = VideoPixelFormat.Bgra8888)
    {
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height));

        var length = checked(width * height * 4);
        var pool = length <= RealtimePoolMaximumLength
            ? RealtimeFramePool
            : ArrayPool<byte>.Shared;
        var array = pool.Rent(length);
        return new VideoFrame(
            new SharedBuffer(array, pool),
            width,
            height,
            timestampTicks,
            rotationDegrees,
            mirrorHorizontally,
            pixelFormat);
    }

    public VideoFrame Retain()
    {
        if (Volatile.Read(ref _disposed) != 0)
            throw new ObjectDisposedException(nameof(VideoFrame));

        _buffer.Retain();
        return new VideoFrame(
            _buffer,
            Width,
            Height,
            TimestampTicks,
            RotationDegrees,
            MirrorHorizontally,
            PixelFormat);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
            _buffer.Release();
    }

    private sealed class SharedBuffer(byte[] array, ArrayPool<byte> pool)
    {
        private readonly ArrayPool<byte> _pool = pool;
        private int _referenceCount = 1;
        private byte[]? _array = array;

        public byte[] Array => Volatile.Read(ref _array)
                               ?? throw new ObjectDisposedException(nameof(VideoFrame));

        public void Retain()
        {
            while (true)
            {
                var current = Volatile.Read(ref _referenceCount);
                if (current <= 0)
                    throw new ObjectDisposedException(nameof(VideoFrame));

                if (Interlocked.CompareExchange(ref _referenceCount, current + 1, current) == current)
                    return;
            }
        }

        public void Release()
        {
            if (Interlocked.Decrement(ref _referenceCount) != 0)
                return;

            var returned = Interlocked.Exchange(ref _array, null);
            if (returned is not null)
                _pool.Return(returned, clearArray: false);
        }
    }
}
