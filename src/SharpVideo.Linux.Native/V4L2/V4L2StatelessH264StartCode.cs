namespace SharpVideo.Linux.Native.V4L2;

/// <summary>
/// Start code
/// </summary>
public enum V4L2StatelessH264StartCode
{
    /// <summary>
    /// Slices are passed to the driver without any start code
    /// </summary>
    NONE,

    /// <summary>
    /// Slices are passed to the driver with an Annex B start code prefix (legal start codes can be 3-bytes 0x000001 or 4-bytes 0x00000001).
    /// This mode is typically supported by device drivers that parse the start code in hardware.
    /// </summary>
    ANNEX_B,
};