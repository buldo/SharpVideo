namespace SharpVideo.Linux.Native;

/// <summary>
/// V4L2 buffer types
/// </summary>
public enum V4L2BufferType : uint
{
    VIDEO_CAPTURE = 1,
    VIDEO_OUTPUT = 2,
    VIDEO_OVERLAY = 3,
    VBI_CAPTURE = 4,
    VBI_OUTPUT = 5,
    SLICED_VBI_CAPTURE = 6,
    SLICED_VBI_OUTPUT = 7,
    VIDEO_OUTPUT_OVERLAY = 8,
    VIDEO_CAPTURE_MPLANE = 9,
    VIDEO_OUTPUT_MPLANE = 10,
    SDR_CAPTURE = 11,
    SDR_OUTPUT = 12,
    META_CAPTURE = 13,
    META_OUTPUT = 14,
}