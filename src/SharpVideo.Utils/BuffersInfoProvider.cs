using System.Collections.Frozen;
using SharpVideo.Drm;

namespace SharpVideo.Utils;

public static class BuffersInfoProvider
{
    private static readonly FrozenDictionary<PixelFormat, FormatInfo> _db = new Dictionary<PixelFormat, FormatInfo>
        {
            {
                KnownPixelFormats.DRM_FORMAT_NV12,
                new FormatInfo
                {
                    PlanesCount = 2,
                    BitsPerPixel = [8, 4]
                }
            },
            {
                KnownPixelFormats.DRM_FORMAT_XRGB8888,
                new FormatInfo
                {
                    PlanesCount = 1,
                    BitsPerPixel = [4 * 8]
                }
            },
            {
                KnownPixelFormats.DRM_FORMAT_ARGB8888,
                new FormatInfo
                {
                    PlanesCount = 1,
                    BitsPerPixel = [4 * 8]
                }
            },
            {
                KnownPixelFormats.DRM_FORMAT_RGB888,
                new FormatInfo
                {
                    PlanesCount = 1,
                    BitsPerPixel = [3 * 8]
                }
            },
        }
        .ToFrozenDictionary();

    public static BufferParams GetBufferParams(uint width, uint height, PixelFormat format)
    {
        var info = _db[format];
        ulong fullSize = 0;
        var offsets = new List<ulong>();
        for (int i = 0; i < info.PlanesCount; i++)
        {
            offsets.Add(fullSize);
            var planeSize = width * height * (info.BitsPerPixel[i]/(float)8); // TODO: Rework. Very bad code
            fullSize += (ulong)planeSize;
        }

        return new()
        {
            Width = width,
            Height = height,
            FullSize = fullSize,
            PlanesCount = info.PlanesCount,
            PlaneOffsets = offsets,
            Stride = (uint)(width * info.BitsPerPixel[0] / 8)
        };
    }

    private class FormatInfo
    {
        public required int PlanesCount { get; init; }

        public required List<int> BitsPerPixel { get; init; }
    }
}

public class BufferParams
{
    public required uint Width { get; init; }
    public required uint Height { get; init; }
    public required ulong FullSize { get; init; }

    public required int PlanesCount { get; init; }

    public required IReadOnlyList<ulong> PlaneOffsets { get; init; }

    public required uint Stride { get; init; }
}