using HelloV.Models;

namespace HelloV.Services;

internal readonly record struct LetterboxInfo(
    float Scale,
    float PadX,
    float PadY,
    int InputWidth,
    int InputHeight,
    int SourceWidth,
    int SourceHeight)
{
    public NormalizedRect ToSourceRect(float x1, float y1, float x2, float y2)
    {
        // Some exports use normalized coordinates while others return input-image pixels.
        if (Math.Max(Math.Max(Math.Abs(x1), Math.Abs(y1)), Math.Max(Math.Abs(x2), Math.Abs(y2))) <= 2f)
        {
            x1 *= InputWidth;
            x2 *= InputWidth;
            y1 *= InputHeight;
            y2 *= InputHeight;
        }

        x1 = (x1 - PadX) / Scale / SourceWidth;
        x2 = (x2 - PadX) / Scale / SourceWidth;
        y1 = (y1 - PadY) / Scale / SourceHeight;
        y2 = (y2 - PadY) / Scale / SourceHeight;
        return NormalizedRect.FromCorners(x1, y1, x2, y2);
    }
}

internal static class OnnxImagePreprocessor
{
    private const float PaddingValue = 114f / 255f;

    public static LetterboxInfo LetterboxBgraToNchwRgb(
        VideoFrame frame,
        float[] destination,
        int inputWidth,
        int inputHeight)
    {
        var plane = inputWidth * inputHeight;
        if (destination.Length < plane * 3)
            throw new ArgumentException("输入张量缓冲区太小。", nameof(destination));

        Array.Fill(destination, PaddingValue);

        var rotation = NormalizeRotation(frame.RotationDegrees);
        var orientedWidth = rotation is 90 or 270 ? frame.Height : frame.Width;
        var orientedHeight = rotation is 90 or 270 ? frame.Width : frame.Height;
        var scale = Math.Min(
            inputWidth / (float)orientedWidth,
            inputHeight / (float)orientedHeight);
        var resizedWidth = Math.Max(1, (int)MathF.Round(orientedWidth * scale));
        var resizedHeight = Math.Max(1, (int)MathF.Round(orientedHeight * scale));
        var padX = (inputWidth - resizedWidth) / 2f;
        var padY = (inputHeight - resizedHeight) / 2f;
        var startX = (int)MathF.Floor(padX);
        var startY = (int)MathF.Floor(padY);
        var source = frame.Pixels;
        var redOffset = frame.PixelFormat == VideoPixelFormat.Rgba8888 ? 0 : 2;
        const int greenOffset = 1;
        var blueOffset = frame.PixelFormat == VideoPixelFormat.Rgba8888 ? 2 : 0;

        // Mobile frames carry rotation and front-camera mirroring as metadata. Sample the source
        // through that transform while resizing directly to 640x640, instead of allocating and
        // copying an oriented 1080p BGRA frame before every inference.
        if (OperatingSystem.IsAndroid() || rotation != 0 || frame.MirrorHorizontally)
        {
            for (var y = 0; y < resizedHeight; y++)
            {
                var orientedY = Math.Min(
                    orientedHeight - 1,
                    y * orientedHeight / resizedHeight);
                var destinationRow = (startY + y) * inputWidth + startX;

                for (var x = 0; x < resizedWidth; x++)
                {
                    var orientedX = Math.Min(
                        orientedWidth - 1,
                        x * orientedWidth / resizedWidth);
                    var sourceOffset = GetRawSourceOffset(
                        frame,
                        orientedX,
                        orientedY,
                        orientedWidth,
                        rotation);
                    var destinationOffset = destinationRow + x;
                    destination[destinationOffset] = source[sourceOffset + redOffset] / 255f;
                    destination[plane + destinationOffset] = source[sourceOffset + greenOffset] / 255f;
                    destination[plane * 2 + destinationOffset] = source[sourceOffset + blueOffset] / 255f;
                }
            }

            return new LetterboxInfo(
                scale,
                startX,
                startY,
                inputWidth,
                inputHeight,
                orientedWidth,
                orientedHeight);
        }

        for (var y = 0; y < resizedHeight; y++)
        {
            var sourceY = (y + 0.5f) / scale - 0.5f;
            var y0 = Math.Clamp((int)MathF.Floor(sourceY), 0, frame.Height - 1);
            var y1 = Math.Min(frame.Height - 1, y0 + 1);
            var fy = Math.Clamp(sourceY - y0, 0f, 1f);

            for (var x = 0; x < resizedWidth; x++)
            {
                var sourceX = (x + 0.5f) / scale - 0.5f;
                var x0 = Math.Clamp((int)MathF.Floor(sourceX), 0, frame.Width - 1);
                var x1 = Math.Min(frame.Width - 1, x0 + 1);
                var fx = Math.Clamp(sourceX - x0, 0f, 1f);

                var p00 = (y0 * frame.Width + x0) * 4;
                var p10 = (y0 * frame.Width + x1) * 4;
                var p01 = (y1 * frame.Width + x0) * 4;
                var p11 = (y1 * frame.Width + x1) * 4;
                var destinationOffset = (startY + y) * inputWidth + startX + x;

                destination[destinationOffset] = Interpolate(
                    source[p00 + redOffset], source[p10 + redOffset],
                    source[p01 + redOffset], source[p11 + redOffset], fx, fy) / 255f;
                destination[plane + destinationOffset] = Interpolate(
                    source[p00 + greenOffset], source[p10 + greenOffset],
                    source[p01 + greenOffset], source[p11 + greenOffset], fx, fy) / 255f;
                destination[plane * 2 + destinationOffset] = Interpolate(
                    source[p00 + blueOffset], source[p10 + blueOffset],
                    source[p01 + blueOffset], source[p11 + blueOffset], fx, fy) / 255f;
            }
        }

        return new LetterboxInfo(
            scale,
            startX,
            startY,
            inputWidth,
            inputHeight,
            frame.Width,
            frame.Height);
    }

    private static int GetRawSourceOffset(
        VideoFrame frame,
        int orientedX,
        int orientedY,
        int orientedWidth,
        int rotation)
    {
        var unmirroredX = frame.MirrorHorizontally
            ? orientedWidth - 1 - orientedX
            : orientedX;

        int rawX;
        int rawY;
        switch (rotation)
        {
            case 90:
                rawX = orientedY;
                rawY = frame.Height - 1 - unmirroredX;
                break;
            case 180:
                rawX = frame.Width - 1 - unmirroredX;
                rawY = frame.Height - 1 - orientedY;
                break;
            case 270:
                rawX = frame.Width - 1 - orientedY;
                rawY = unmirroredX;
                break;
            default:
                rawX = unmirroredX;
                rawY = orientedY;
                break;
        }

        rawX = Math.Clamp(rawX, 0, frame.Width - 1);
        rawY = Math.Clamp(rawY, 0, frame.Height - 1);
        return (rawY * frame.Width + rawX) * 4;
    }

    private static int NormalizeRotation(int rotation)
    {
        rotation = ((rotation % 360) + 360) % 360;
        return rotation is 0 or 90 or 180 or 270 ? rotation : 0;
    }

    private static float Interpolate(
        byte p00,
        byte p10,
        byte p01,
        byte p11,
        float fx,
        float fy)
    {
        var top = p00 + (p10 - p00) * fx;
        var bottom = p01 + (p11 - p01) * fx;
        return top + (bottom - top) * fy;
    }
}
