namespace SharpVideo.Utils;

public static class TestPattern
{
    // 5 vertical color bars: Red, Green, Blue, Yellow, Cyan
    // Using standard ITU-R BT.601 color values for YUV
    private static readonly (byte Y, byte U, byte V)[] ColorBars =
    {
        (81, 90, 240), // Red
        (145, 54, 34), // Green
        (41, 240, 110), // Blue
        (210, 16, 146), // Yellow
        (170, 166, 16) // Cyan
    };

    public static void FillYuv422(Span<byte> buffer, int width, int height)
    {
        var barWidth = width / ColorBars.Length;

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x += 2)
            {
                var barIndex = Math.Min(x / barWidth, ColorBars.Length - 1);
                var color = ColorBars[barIndex];

                var baseIndex = (y * width + x) * 2;

                // YUYV format
                buffer[baseIndex + 0] = color.Y;
                buffer[baseIndex + 1] = color.U;
                buffer[baseIndex + 2] = color.Y;
                buffer[baseIndex + 3] = color.V;
            }
        }
    }

    public static void FillXR24(Span<byte> buffer, int width, int height)
    {
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                // Simple color bar pattern
                byte r = 0, g = 0, b = 0;
                if (x < width / 3)
                {
                    r = 255;
                }
                else if (x < width * 2 / 3)
                {
                    g = 255;
                }
                else
                {
                    b = 255;
                }

                int pos = (y * width + x) * 4;
                buffer[pos] = b; // Blue
                buffer[pos + 1] = g; // Green
                buffer[pos + 2] = r; // Red
                buffer[pos + 3] = 0; // X (unused)
            }
        }
    }

    public static void FillNV12(Span<byte> buffer, int width, int height)
    {
        // NV12 format: Y plane followed by interleaved UV plane
        // Y plane is full resolution (width * height)
        // UV plane is half resolution (width * height / 2)
        int yPlaneSize = width * height;
        int uvPlaneSize = width * height / 2;

        var yPlane = buffer.Slice(0, yPlaneSize);
        var uvPlane = buffer.Slice(yPlaneSize, uvPlaneSize);

        // Fill with color bar pattern using standard ITU-R BT.601 values
        var barWidth = width / ColorBars.Length;

        // Fill Y plane
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                var barIndex = Math.Min(x / barWidth, ColorBars.Length - 1);
                var color = ColorBars[barIndex];
                yPlane[y * width + x] = color.Y;
            }
        }

        // Fill UV plane (interleaved, half resolution)
        for (int y = 0; y < height / 2; y++)
        {
            for (int x = 0; x < width / 2; x++)
            {
                var barIndex = Math.Min((x * 2) / barWidth, ColorBars.Length - 1);
                var color = ColorBars[barIndex];
                int uvIndex = (y * width) + (x * 2);
                uvPlane[uvIndex] = color.U;
                uvPlane[uvIndex + 1] = color.V;
            }
        }
    }

    /// <summary>
    /// Fills buffer with ARGB8888 test pattern featuring semi-transparent geometric shapes.
    /// This pattern is designed to be overlaid on top of other content to demonstrate compositing.
    /// </summary>
    public static void FillARGB8888(Span<byte> buffer, int width, int height)
    {
        // Create a pattern with transparent background and semi-transparent/opaque shapes
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int pos = (y * width + x) * 4;
                byte r = 0, g = 0, b = 0, a = 0;

                // Draw a semi-transparent red rectangle in top-left quarter
                if (x < width / 4 && y < height / 4)
                {
                    r = 255;
                    g = 0;
                    b = 0;
                    a = 192; // 75% opacity
                }
                // Draw a semi-transparent green rectangle in top-right quarter
                else if (x >= width * 3 / 4 && y < height / 4)
                {
                    r = 0;
                    g = 255;
                    b = 0;
                    a = 192; // 75% opacity
                }
                // Draw a semi-transparent blue rectangle in bottom-left quarter
                else if (x < width / 4 && y >= height * 3 / 4)
                {
                    r = 0;
                    g = 0;
                    b = 255;
                    a = 192; // 75% opacity
                }
                // Draw a semi-transparent yellow rectangle in bottom-right quarter
                else if (x >= width * 3 / 4 && y >= height * 3 / 4)
                {
                    r = 255;
                    g = 255;
                    b = 0;
                    a = 192; // 75% opacity
                }
                // Draw a white border (opaque) around the edges
                else if (x < 10 || x >= width - 10 || y < 10 || y >= height - 10)
                {
                    r = 255;
                    g = 255;
                    b = 255;
                    a = 255; // 100% opacity
                }
                // Center cross pattern (semi-transparent white)
                else if ((x >= width / 2 - 5 && x <= width / 2 + 5) ||
                         (y >= height / 2 - 5 && y <= height / 2 + 5))
                {
                    r = 255;
                    g = 255;
                    b = 255;
                    a = 128; // 50% opacity
                }
                // Rest is fully transparent
                else
                {
                    r = 0;
                    g = 0;
                    b = 0;
                    a = 0; // Fully transparent
                }

                // ARGB8888 format: B, G, R, A
                buffer[pos] = b;
                buffer[pos + 1] = g;
                buffer[pos + 2] = r;
                buffer[pos + 3] = a;
            }
        }
    }
}
