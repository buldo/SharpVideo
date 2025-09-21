using System.Runtime.Versioning;

namespace SharpVideo.Linux.Native;

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
    public static unsafe readonly uint VIDIOC_QUERYCTRL = IoctlConstants.IOWR(V4L2_IOCTL_MAGIC, 36, (uint)sizeof(V4L2QueryCtrl));
    public static unsafe readonly uint VIDIOC_QUERYMENU = IoctlConstants.IOWR(V4L2_IOCTL_MAGIC, 37, (uint)sizeof(V4L2QueryMenuItem));
    public static unsafe readonly uint VIDIOC_QUERY_EXT_CTRL = IoctlConstants.IOWR(V4L2_IOCTL_MAGIC, 103, (uint)sizeof(V4L2QueryExtCtrl));
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

    // Memory types
    public const uint V4L2_MEMORY_MMAP = 1;
    public const uint V4L2_MEMORY_USERPTR = 2;
    public const uint V4L2_MEMORY_OVERLAY = 3;
    public const uint V4L2_MEMORY_DMABUF = 4;

    // Other useful constants
    public const uint VIDEO_MAX_FRAME = 32;
    public const uint VIDEO_MAX_PLANES = 8;
}