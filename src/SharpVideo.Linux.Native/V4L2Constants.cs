using System.Runtime.Versioning;

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

/// <summary>
/// V4L2 memory types
/// </summary>
public enum V4L2Memory : uint
{
    MMAP = 1,
    USERPTR = 2,
    OVERLAY = 3,
    DMABUF = 4,
}

/// <summary>
/// V4L2 field definitions
/// </summary>
public enum V4L2Field : uint
{
    ANY = 0,
    NONE = 1,
    TOP = 2,
    BOTTOM = 3,
    INTERLACED = 4,
    SEQ_TB = 5,
    SEQ_BT = 6,
    ALTERNATE = 7,
    INTERLACED_TB = 8,
    INTERLACED_BT = 9,
}

/// <summary>
/// V4L2 capability flags
/// </summary>
[Flags]
public enum V4L2Capabilities : uint
{
    VIDEO_CAPTURE = 0x00000001,
    VIDEO_OUTPUT = 0x00000002,
    VIDEO_OVERLAY = 0x00000004,
    VBI_CAPTURE = 0x00000010,
    VBI_OUTPUT = 0x00000020,
    SLICED_VBI_CAPTURE = 0x00000040,
    SLICED_VBI_OUTPUT = 0x00000080,
    RDS_CAPTURE = 0x00000100,
    VIDEO_OUTPUT_OVERLAY = 0x00000200,
    HW_FREQ_SEEK = 0x00000400,
    RDS_OUTPUT = 0x00000800,
    VIDEO_CAPTURE_MPLANE = 0x00001000,
    VIDEO_OUTPUT_MPLANE = 0x00002000,
    VIDEO_M2M_MPLANE = 0x00004000,
    VIDEO_M2M = 0x00008000,
    TUNER = 0x00010000,
    AUDIO = 0x00020000,
    RADIO = 0x00040000,
    MODULATOR = 0x00080000,
    SDR_CAPTURE = 0x00100000,
    EXT_PIX_FORMAT = 0x00200000,
    SDR_OUTPUT = 0x00400000,
    META_CAPTURE = 0x00800000,
    READWRITE = 0x01000000,
    ASYNCIO = 0x02000000,
    STREAMING = 0x04000000,
    META_OUTPUT = 0x08000000,
    TOUCH = 0x10000000,
    IO_MC = 0x20000000,
    DEVICE_CAPS = 0x80000000,
}

/// <summary>
/// V4L2 buffer flags
/// </summary>
[Flags]
public enum V4L2BufferFlags : uint
{
    MAPPED = 0x00000001,
    QUEUED = 0x00000002,
    DONE = 0x00000004,
    KEYFRAME = 0x00000008,
    PFRAME = 0x00000010,
    BFRAME = 0x00000020,
    ERROR = 0x00000040,
    IN_REQUEST = 0x00000080,
    TIMECODE = 0x00000100,
    M2M_HOLD_CAPTURE_BUF = 0x00000200,
    PREPARED = 0x00000400,
    NO_CACHE_INVALIDATE = 0x00000800,
    NO_CACHE_CLEAN = 0x00001000,
    TIMESTAMP_MASK = 0x0000e000,
    TIMESTAMP_UNKNOWN = 0x00000000,
    TIMESTAMP_MONOTONIC = 0x00002000,
    TIMESTAMP_COPY = 0x00004000,
    TSTAMP_SRC_MASK = 0x00070000,
    TSTAMP_SRC_EOF = 0x00000000,
    TSTAMP_SRC_SOE = 0x00010000,
    LAST = 0x00100000,
    REQUEST_FD = 0x00800000,
}

/// <summary>
/// V4L2 decoder commands
/// </summary>
public enum V4L2DecoderCommand : uint
{
    START = 0,
    STOP = 1,
    PAUSE = 2,
    RESUME = 3,
    FLUSH = 4,
}

/// <summary>
/// V4L2 encoder commands
/// </summary>
public enum V4L2EncoderCommand : uint
{
    START = 0,
    STOP = 1,
    PAUSE = 2,
    RESUME = 3,
}

/// <summary>
/// Common V4L2 pixel formats (FOURCC codes)
/// </summary>
public static class V4L2PixelFormats
{
    private static uint v4l2_fourcc(char a, char b, char c, char d)
    {
        return (uint)((byte)a | ((byte)b << 8) | ((byte)c << 16) | ((byte)d << 24));
    }

    // Multiplanar formats (most commonly used)
    public static readonly uint NV12M = v4l2_fourcc('N', 'M', '1', '2');
    public static readonly uint NV21M = v4l2_fourcc('N', 'M', '2', '1');
    public static readonly uint NV16M = v4l2_fourcc('N', 'M', '1', '6');
    public static readonly uint NV61M = v4l2_fourcc('N', 'M', '6', '1');

    // RGB formats
    public static readonly uint RGB332 = v4l2_fourcc('R', 'G', 'B', '1');
    public static readonly uint RGB444 = v4l2_fourcc('R', '4', '4', '4');
    public static readonly uint ARGB444 = v4l2_fourcc('A', 'R', '1', '2');
    public static readonly uint XRGB444 = v4l2_fourcc('X', 'R', '1', '2');
    public static readonly uint RGB555 = v4l2_fourcc('R', 'G', 'B', 'O');
    public static readonly uint ARGB555 = v4l2_fourcc('A', 'R', '1', '5');
    public static readonly uint XRGB555 = v4l2_fourcc('X', 'R', '1', '5');
    public static readonly uint RGB565 = v4l2_fourcc('R', 'G', 'B', 'P');
    public static readonly uint BGR24 = v4l2_fourcc('B', 'G', 'R', '3');
    public static readonly uint RGB24 = v4l2_fourcc('R', 'G', 'B', '3');
    public static readonly uint BGR32 = v4l2_fourcc('B', 'G', 'R', '4');
    public static readonly uint ABGR32 = v4l2_fourcc('A', 'R', '2', '4');
    public static readonly uint XBGR32 = v4l2_fourcc('X', 'R', '2', '4');
    public static readonly uint RGB32 = v4l2_fourcc('R', 'G', 'B', '4');
    public static readonly uint ARGB32 = v4l2_fourcc('B', 'A', '2', '4');
    public static readonly uint XRGB32 = v4l2_fourcc('B', 'X', '2', '4');

    // YUV formats
    public static readonly uint GREY = v4l2_fourcc('G', 'R', 'E', 'Y');
    public static readonly uint Y10 = v4l2_fourcc('Y', '1', '0', ' ');
    public static readonly uint Y12 = v4l2_fourcc('Y', '1', '2', ' ');
    public static readonly uint Y16 = v4l2_fourcc('Y', '1', '6', ' ');
    public static readonly uint YUYV = v4l2_fourcc('Y', 'U', 'Y', 'V');
    public static readonly uint YVYU = v4l2_fourcc('Y', 'V', 'Y', 'U');
    public static readonly uint UYVY = v4l2_fourcc('U', 'Y', 'V', 'Y');
    public static readonly uint VYUY = v4l2_fourcc('V', 'Y', 'U', 'Y');

    // Planar YUV formats
    public static readonly uint YUV420 = v4l2_fourcc('Y', 'U', '1', '2');
    public static readonly uint YVU420 = v4l2_fourcc('Y', 'V', '1', '2');
    public static readonly uint YUV422P = v4l2_fourcc('4', '2', '2', 'P');
    public static readonly uint YUV444P = v4l2_fourcc('Y', '4', '4', 'P');

    // More multiplanar formats
    public static readonly uint YUV420M = v4l2_fourcc('Y', 'M', '1', '2');
    public static readonly uint YVU420M = v4l2_fourcc('Y', 'M', '2', '1');
    public static readonly uint YUV422M = v4l2_fourcc('Y', 'M', '4', '2');
    public static readonly uint YVU422M = v4l2_fourcc('Y', 'M', '2', '4');
    public static readonly uint YUV444M = v4l2_fourcc('Y', 'M', '4', '4');
    public static readonly uint YVU444M = v4l2_fourcc('Y', 'M', '6', '4');

    // Compressed formats
    public static readonly uint MJPEG = v4l2_fourcc('M', 'J', 'P', 'G');
    public static readonly uint JPEG = v4l2_fourcc('J', 'P', 'E', 'G');
    public static readonly uint H264 = v4l2_fourcc('H', '2', '6', '4');
    public static readonly uint H263 = v4l2_fourcc('H', '2', '6', '3');
    public static readonly uint MPEG1 = v4l2_fourcc('M', 'P', 'G', '1');
    public static readonly uint MPEG2 = v4l2_fourcc('M', 'P', 'G', '2');
    public static readonly uint MPEG4 = v4l2_fourcc('M', 'P', 'G', '4');
    public static readonly uint VP8 = v4l2_fourcc('V', 'P', '8', '0');
    public static readonly uint VP9 = v4l2_fourcc('V', 'P', '9', '0');
    public static readonly uint HEVC = v4l2_fourcc('H', 'E', 'V', 'C');
}

/// <summary>
/// V4L2 constants and ioctl request codes
/// </summary>
[SupportedOSPlatform("linux")]
public static class V4L2Constants
{
    // V4L2 ioctl magic number
    public const uint V4L2_IOCTL_MAGIC = (uint)'V';

    // V4L2 ioctl request codes
    public static readonly uint VIDIOC_QUERYCAP = IoctlConstants.IOR(V4L2_IOCTL_MAGIC, 0, 104); // sizeof(V4L2Capability)
    public static readonly uint VIDIOC_RESERVED = IoctlConstants.IO(V4L2_IOCTL_MAGIC, 1);
    public static readonly uint VIDIOC_ENUM_FMT = IoctlConstants.IOWR(V4L2_IOCTL_MAGIC, 2, 64); // sizeof(v4l2_fmtdesc)
    public static readonly uint VIDIOC_G_FMT = IoctlConstants.IOWR(V4L2_IOCTL_MAGIC, 4, 208); // sizeof(V4L2Format)
    public static readonly uint VIDIOC_S_FMT = IoctlConstants.IOWR(V4L2_IOCTL_MAGIC, 5, 208); // sizeof(V4L2Format)
    public static readonly uint VIDIOC_REQBUFS = IoctlConstants.IOWR(V4L2_IOCTL_MAGIC, 8, 20); // sizeof(V4L2RequestBuffers)
    public static readonly uint VIDIOC_QUERYBUF = IoctlConstants.IOWR(V4L2_IOCTL_MAGIC, 9, 88); // sizeof(V4L2Buffer)
    public static readonly uint VIDIOC_G_FBUF = IoctlConstants.IOR(V4L2_IOCTL_MAGIC, 10, 48);
    public static readonly uint VIDIOC_S_FBUF = IoctlConstants.IOW(V4L2_IOCTL_MAGIC, 11, 48);
    public static readonly uint VIDIOC_OVERLAY = IoctlConstants.IOW(V4L2_IOCTL_MAGIC, 14, 4);
    public static readonly uint VIDIOC_QBUF = IoctlConstants.IOWR(V4L2_IOCTL_MAGIC, 15, 88); // sizeof(V4L2Buffer)
    public static readonly uint VIDIOC_EXPBUF = IoctlConstants.IOWR(V4L2_IOCTL_MAGIC, 16, 64); // sizeof(V4L2ExportBuffer)
    public static readonly uint VIDIOC_DQBUF = IoctlConstants.IOWR(V4L2_IOCTL_MAGIC, 17, 88); // sizeof(V4L2Buffer)
    public static readonly uint VIDIOC_STREAMON = IoctlConstants.IOW(V4L2_IOCTL_MAGIC, 18, 4);
    public static readonly uint VIDIOC_STREAMOFF = IoctlConstants.IOW(V4L2_IOCTL_MAGIC, 19, 4);
    public static readonly uint VIDIOC_G_PARM = IoctlConstants.IOWR(V4L2_IOCTL_MAGIC, 21, 204); // sizeof(V4L2StreamParm)
    public static readonly uint VIDIOC_S_PARM = IoctlConstants.IOWR(V4L2_IOCTL_MAGIC, 22, 204); // sizeof(V4L2StreamParm)
    public static readonly uint VIDIOC_G_STD = IoctlConstants.IOR(V4L2_IOCTL_MAGIC, 23, 8);
    public static readonly uint VIDIOC_S_STD = IoctlConstants.IOW(V4L2_IOCTL_MAGIC, 24, 8);
    public static readonly uint VIDIOC_ENUMSTD = IoctlConstants.IOWR(V4L2_IOCTL_MAGIC, 25, 168);
    public static readonly uint VIDIOC_ENUMINPUT = IoctlConstants.IOWR(V4L2_IOCTL_MAGIC, 26, 80);
    public static readonly uint VIDIOC_G_CTRL = IoctlConstants.IOWR(V4L2_IOCTL_MAGIC, 27, 8);
    public static readonly uint VIDIOC_S_CTRL = IoctlConstants.IOWR(V4L2_IOCTL_MAGIC, 28, 8);
    public static readonly uint VIDIOC_G_TUNER = IoctlConstants.IOWR(V4L2_IOCTL_MAGIC, 29, 84);
    public static readonly uint VIDIOC_S_TUNER = IoctlConstants.IOW(V4L2_IOCTL_MAGIC, 30, 84);
    public static readonly uint VIDIOC_G_AUDIO = IoctlConstants.IOR(V4L2_IOCTL_MAGIC, 33, 52);
    public static readonly uint VIDIOC_S_AUDIO = IoctlConstants.IOW(V4L2_IOCTL_MAGIC, 34, 52);
    public static readonly uint VIDIOC_QUERYCTRL = IoctlConstants.IOWR(V4L2_IOCTL_MAGIC, 36, 68);
    public static readonly uint VIDIOC_QUERYMENU = IoctlConstants.IOWR(V4L2_IOCTL_MAGIC, 37, 44);
    public static readonly uint VIDIOC_G_INPUT = IoctlConstants.IOR(V4L2_IOCTL_MAGIC, 38, 4);
    public static readonly uint VIDIOC_S_INPUT = IoctlConstants.IOWR(V4L2_IOCTL_MAGIC, 39, 4);
    public static readonly uint VIDIOC_G_EDID = IoctlConstants.IOWR(V4L2_IOCTL_MAGIC, 40, 32);
    public static readonly uint VIDIOC_S_EDID = IoctlConstants.IOWR(V4L2_IOCTL_MAGIC, 41, 32);
    public static readonly uint VIDIOC_G_OUTPUT = IoctlConstants.IOR(V4L2_IOCTL_MAGIC, 46, 4);
    public static readonly uint VIDIOC_S_OUTPUT = IoctlConstants.IOWR(V4L2_IOCTL_MAGIC, 47, 4);
    public static readonly uint VIDIOC_ENUMOUTPUT = IoctlConstants.IOWR(V4L2_IOCTL_MAGIC, 48, 72);
    public static readonly uint VIDIOC_G_AUDOUT = IoctlConstants.IOR(V4L2_IOCTL_MAGIC, 49, 52);
    public static readonly uint VIDIOC_S_AUDOUT = IoctlConstants.IOW(V4L2_IOCTL_MAGIC, 50, 52);
    public static readonly uint VIDIOC_G_MODULATOR = IoctlConstants.IOWR(V4L2_IOCTL_MAGIC, 54, 68);
    public static readonly uint VIDIOC_S_MODULATOR = IoctlConstants.IOW(V4L2_IOCTL_MAGIC, 55, 68);
    public static readonly uint VIDIOC_G_FREQUENCY = IoctlConstants.IOWR(V4L2_IOCTL_MAGIC, 56, 44);
    public static readonly uint VIDIOC_S_FREQUENCY = IoctlConstants.IOW(V4L2_IOCTL_MAGIC, 57, 44);
    public static readonly uint VIDIOC_CROPCAP = IoctlConstants.IOWR(V4L2_IOCTL_MAGIC, 58, 44);
    public static readonly uint VIDIOC_G_CROP = IoctlConstants.IOWR(V4L2_IOCTL_MAGIC, 59, 20);
    public static readonly uint VIDIOC_S_CROP = IoctlConstants.IOW(V4L2_IOCTL_MAGIC, 60, 20);
    public static readonly uint VIDIOC_G_JPEGCOMP = IoctlConstants.IOR(V4L2_IOCTL_MAGIC, 61, 140);
    public static readonly uint VIDIOC_S_JPEGCOMP = IoctlConstants.IOW(V4L2_IOCTL_MAGIC, 62, 140);
    public static readonly uint VIDIOC_QUERYSTD = IoctlConstants.IOR(V4L2_IOCTL_MAGIC, 63, 8);
    public static readonly uint VIDIOC_TRY_FMT = IoctlConstants.IOWR(V4L2_IOCTL_MAGIC, 64, 208); // sizeof(V4L2Format)
    public static readonly uint VIDIOC_ENUMAUDIO = IoctlConstants.IOWR(V4L2_IOCTL_MAGIC, 65, 52);
    public static readonly uint VIDIOC_ENUMAUDOUT = IoctlConstants.IOWR(V4L2_IOCTL_MAGIC, 66, 52);
    public static readonly uint VIDIOC_G_PRIORITY = IoctlConstants.IOR(V4L2_IOCTL_MAGIC, 67, 4);
    public static readonly uint VIDIOC_S_PRIORITY = IoctlConstants.IOW(V4L2_IOCTL_MAGIC, 68, 4);
    public static readonly uint VIDIOC_G_SLICED_VBI_CAP = IoctlConstants.IOWR(V4L2_IOCTL_MAGIC, 69, 208);
    public static readonly uint VIDIOC_LOG_STATUS = IoctlConstants.IO(V4L2_IOCTL_MAGIC, 70);
    public static readonly uint VIDIOC_G_EXT_CTRLS = IoctlConstants.IOWR(V4L2_IOCTL_MAGIC, 71, 32);
    public static readonly uint VIDIOC_S_EXT_CTRLS = IoctlConstants.IOWR(V4L2_IOCTL_MAGIC, 72, 32);
    public static readonly uint VIDIOC_TRY_EXT_CTRLS = IoctlConstants.IOWR(V4L2_IOCTL_MAGIC, 73, 32);
    public static readonly uint VIDIOC_ENUM_FRAMESIZES = IoctlConstants.IOWR(V4L2_IOCTL_MAGIC, 74, 44);
    public static readonly uint VIDIOC_ENUM_FRAMEINTERVALS = IoctlConstants.IOWR(V4L2_IOCTL_MAGIC, 75, 52);
    public static readonly uint VIDIOC_G_ENC_INDEX = IoctlConstants.IOR(V4L2_IOCTL_MAGIC, 76, 11020);
    public static readonly uint VIDIOC_ENCODER_CMD = IoctlConstants.IOWR(V4L2_IOCTL_MAGIC, 77, 40); // sizeof(V4L2EncoderCmd)
    public static readonly uint VIDIOC_TRY_ENCODER_CMD = IoctlConstants.IOWR(V4L2_IOCTL_MAGIC, 78, 40); // sizeof(V4L2EncoderCmd)
    public static readonly uint VIDIOC_DECODER_CMD = IoctlConstants.IOWR(V4L2_IOCTL_MAGIC, 96, 72); // sizeof(V4L2DecoderCmd)
    public static readonly uint VIDIOC_TRY_DECODER_CMD = IoctlConstants.IOWR(V4L2_IOCTL_MAGIC, 97, 72); // sizeof(V4L2DecoderCmd)

    // Stream type constants
    public const uint V4L2_BUF_TYPE_VIDEO_CAPTURE = 1;
    public const uint V4L2_BUF_TYPE_VIDEO_OUTPUT = 2;
    public const uint V4L2_BUF_TYPE_VIDEO_OVERLAY = 3;
    public const uint V4L2_BUF_TYPE_VBI_CAPTURE = 4;
    public const uint V4L2_BUF_TYPE_VBI_OUTPUT = 5;
    public const uint V4L2_BUF_TYPE_SLICED_VBI_CAPTURE = 6;
    public const uint V4L2_BUF_TYPE_SLICED_VBI_OUTPUT = 7;
    public const uint V4L2_BUF_TYPE_VIDEO_OUTPUT_OVERLAY = 8;
    public const uint V4L2_BUF_TYPE_VIDEO_CAPTURE_MPLANE = 9;
    public const uint V4L2_BUF_TYPE_VIDEO_OUTPUT_MPLANE = 10;
    public const uint V4L2_BUF_TYPE_SDR_CAPTURE = 11;
    public const uint V4L2_BUF_TYPE_SDR_OUTPUT = 12;
    public const uint V4L2_BUF_TYPE_META_CAPTURE = 13;
    public const uint V4L2_BUF_TYPE_META_OUTPUT = 14;

    // Memory types
    public const uint V4L2_MEMORY_MMAP = 1;
    public const uint V4L2_MEMORY_USERPTR = 2;
    public const uint V4L2_MEMORY_OVERLAY = 3;
    public const uint V4L2_MEMORY_DMABUF = 4;

    // Other useful constants
    public const uint VIDEO_MAX_FRAME = 32;
    public const uint VIDEO_MAX_PLANES = 8;
}