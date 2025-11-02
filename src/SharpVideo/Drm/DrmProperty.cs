using SharpVideo.Linux.Native.Drm;

namespace SharpVideo.Drm;

public class DrmProperty
{
    public required uint Id { get; init; }

    public required string Name { get; init; }

    public required PropertyType Type { get; init; }

    public required ulong Value { get; init; }

    public uint Flags { get; init; }

    /// <summary>
    /// For range properties, contains [min, max] values.
    /// For enum/bitmask properties, contains valid values.
    /// </summary>
    public IReadOnlyList<ulong>? Values { get; init; }

    /// <summary>
    /// For enum properties, contains the enum names corresponding to Values.
    /// </summary>
    public IReadOnlyList<string>? EnumNames { get; init; }

    /// <summary>
    /// For blob properties, contains the blob IDs.
    /// </summary>
    public IReadOnlyList<uint>? BlobIds { get; init; }
}