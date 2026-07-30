using HelloV.Models;

namespace HelloV.Imaging;

public static class FrameTransforms
{
    /// <summary>
    /// Consumes <paramref name="frame"/> and returns an owned, orientation-normalized frame.
    /// Rotation and mirroring are combined into a single pass to avoid two full-frame copies.
    /// </summary>
    public static VideoFrame NormalizeOwned(VideoFrame frame)
    {
        var rotation = ((frame.RotationDegrees % 360) + 360) % 360;
        if (rotation == 0 && !frame.MirrorHorizontally)
            return frame;

        if (rotation is not (0 or 90 or 180 or 270))
            rotation = 0;

        var dstWidth = rotation is 90 or 270 ? frame.Height : frame.Width;
        var dstHeight = rotation is 90 or 270 ? frame.Width : frame.Height;
        var result = VideoFrame.Rent(
            dstWidth,
            dstHeight,
            frame.TimestampTicks,
            pixelFormat: frame.PixelFormat);

        try
        {
            var src = frame.Pixels;
            var dst = result.Pixels;

            for (var y = 0; y < frame.Height; y++)
            {
                for (var x = 0; x < frame.Width; x++)
                {
                    int dx;
                    int dy;
                    switch (rotation)
                    {
                        case 90:
                            dx = frame.Height - 1 - y;
                            dy = x;
                            break;
                        case 180:
                            dx = frame.Width - 1 - x;
                            dy = frame.Height - 1 - y;
                            break;
                        case 270:
                            dx = y;
                            dy = frame.Width - 1 - x;
                            break;
                        default:
                            dx = x;
                            dy = y;
                            break;
                    }

                    if (frame.MirrorHorizontally)
                        dx = dstWidth - 1 - dx;

                    var sourceOffset = (y * frame.Width + x) * 4;
                    var destinationOffset = (dy * dstWidth + dx) * 4;
                    dst[destinationOffset] = src[sourceOffset];
                    dst[destinationOffset + 1] = src[sourceOffset + 1];
                    dst[destinationOffset + 2] = src[sourceOffset + 2];
                    dst[destinationOffset + 3] = src[sourceOffset + 3];
                }
            }

            return result;
        }
        catch
        {
            result.Dispose();
            throw;
        }
        finally
        {
            frame.Dispose();
        }
    }
}
