using System.Collections.Frozen;

using SharpVideo.Drm;

namespace SharpVideo.Utils.Buffers;

/// <summary>
/// Provides buffer layout information for DRM pixel formats.
/// Calculates plane sizes, pitches, and offsets with optional memory alignment.
/// </summary>
public static class BuffersInfoProvider
{
    private static readonly FrozenDictionary<PixelFormat, FormatDescriptor> FormatDatabase =
        new Dictionary<PixelFormat, FormatDescriptor>
        {
            // NV12: Y plane (8 bits per pixel) + UV plane (8 bits per 2x2 pixel block = 4 bits per pixel)
            {
                KnownPixelFormats.DRM_FORMAT_NV12,
                new FormatDescriptor([
                    new PlaneDescriptor(BytesPerPixelNumerator: 1, BytesPerPixelDenominator: 1, WidthDivisor: 1, HeightDivisor: 1),
                    new PlaneDescriptor(BytesPerPixelNumerator: 2, BytesPerPixelDenominator: 1, WidthDivisor: 2, HeightDivisor: 2)
                ])
            },
            // XRGB8888: 4 bytes per pixel (32-bit, no alpha)
            {
                KnownPixelFormats.DRM_FORMAT_XRGB8888,
                new FormatDescriptor([
                    new PlaneDescriptor(BytesPerPixelNumerator: 4, BytesPerPixelDenominator: 1, WidthDivisor: 1, HeightDivisor: 1)
                ])
            },
            // ARGB8888: 4 bytes per pixel (32-bit with alpha)
            {
                KnownPixelFormats.DRM_FORMAT_ARGB8888,
                new FormatDescriptor([
                    new PlaneDescriptor(BytesPerPixelNumerator: 4, BytesPerPixelDenominator: 1, WidthDivisor: 1, HeightDivisor: 1)
                ])
            },
            // RGB888: 3 bytes per pixel (24-bit)
            {
                KnownPixelFormats.DRM_FORMAT_RGB888,
                new FormatDescriptor([
                    new PlaneDescriptor(BytesPerPixelNumerator: 3, BytesPerPixelDenominator: 1, WidthDivisor: 1, HeightDivisor: 1)
                ])
            },
        }.ToFrozenDictionary();

    /// <summary>
    /// Calculates buffer parameters for the specified dimensions and format.
    /// </summary>
    /// <param name="width">Image width in pixels.</param>
    /// <param name="height">Image height in pixels.</param>
    /// <param name="format">Pixel format.</param>
    /// <param name="alignment">Memory alignment in bytes (must be power of 2). Use 1 for no alignment.</param>
    /// <returns>Buffer parameters with plane layouts suitable for drmModeAddFB2.</returns>
    /// <exception cref="ArgumentException">Thrown when alignment is not a power of 2.</exception>
    /// <exception cref="KeyNotFoundException">Thrown when format is not supported.</exception>
    public static BufferParams GetBufferParams(uint width, uint height, PixelFormat format, uint alignment = 64)
    {
        if (alignment == 0 || (alignment & (alignment - 1)) != 0)
        {
            throw new ArgumentException("Alignment must be a power of 2", nameof(alignment));
        }

        var descriptor = FormatDatabase[format];
        var planes = new List<PlaneInfo>(descriptor.Planes.Count);
        uint currentOffset = 0;

        foreach (var planeDesc in descriptor.Planes)
        {
            uint planeWidth = width / planeDesc.WidthDivisor;
            uint planeHeight = height / planeDesc.HeightDivisor;

            // Calculate unaligned pitch (bytes per row)
            uint pitch = planeWidth * planeDesc.BytesPerPixelNumerator / planeDesc.BytesPerPixelDenominator;

            // Align pitch to specified boundary
            pitch = AlignUp(pitch, alignment);

            // Calculate plane size
            uint planeSize = pitch * planeHeight;

            planes.Add(new PlaneInfo(pitch, currentOffset, planeSize));

            // Align next plane offset
            currentOffset += AlignUp(planeSize, alignment);
        }

        return new BufferParams(width, height, currentOffset, planes);
    }

    private static uint AlignUp(uint value, uint alignment)
    {
        return (value + alignment - 1) & ~(alignment - 1);
    }

    /// <summary>
    /// Describes a pixel format's plane layout.
    /// </summary>
    private readonly record struct FormatDescriptor(IReadOnlyList<PlaneDescriptor> Planes);

    /// <summary>
    /// Describes how to calculate a single plane's dimensions.
    /// </summary>
    /// <param name="BytesPerPixelNumerator">Bytes per pixel numerator for fractional values.</param>
    /// <param name="BytesPerPixelDenominator">Bytes per pixel denominator for fractional values.</param>
    /// <param name="WidthDivisor">Divide image width by this for plane width.</param>
    /// <param name="HeightDivisor">Divide image height by this for plane height.</param>
    private readonly record struct PlaneDescriptor(
        uint BytesPerPixelNumerator,
        uint BytesPerPixelDenominator,
        uint WidthDivisor,
        uint HeightDivisor);
}