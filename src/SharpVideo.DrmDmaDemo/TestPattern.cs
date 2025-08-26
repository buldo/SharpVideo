using System;

namespace SharpVideo.DrmDmaDemo;

public static class TestPattern
{
    // Standard ITU-R BT.601 color bar values for YUV
    private static readonly (byte Y, byte U, byte V)[] ColorBars =
    {
        (235, 128, 128), // White
        (210, 16, 146),  // Yellow
        (170, 166, 16),   // Cyan
        (145, 54, 34),   // Green
        (107, 202, 222), // Magenta
        (81, 90, 240),   // Red
        (41, 240, 110),  // Blue
        (16, 128, 128)   // Black
    };

    public static unsafe void FillYuv422(byte* buffer, int width, int height)
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

    public static unsafe void FillXR24(byte* buffer, int width, int height)
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
                buffer[pos] = b;     // Blue
                buffer[pos + 1] = g; // Green
                buffer[pos + 2] = r; // Red
                buffer[pos + 3] = 0; // X (unused)
            }
        }
    }
}
