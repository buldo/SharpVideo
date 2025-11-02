namespace SharpVideo.Linux.Native.V4L2;

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

    /// <summary>
    /// Motion-JPEG
    /// </summary>
    public static readonly uint V4L2_PIX_FMT_MJPEG = v4l2_fourcc('M', 'J', 'P', 'G');

    /// <summary>
    /// JFIF JPEG
    /// </summary>
    public static readonly uint V4L2_PIX_FMT_JPEG = v4l2_fourcc('J', 'P', 'E', 'G');

    /// <summary>
    /// 1394
    /// </summary>
    public static readonly uint V4L2_PIX_FMT_DV = v4l2_fourcc('d', 'v', 's', 'd');

    /// <summary>
    /// MPEG-1/2/4 Multiplexed
    /// </summary>
    public static readonly uint V4L2_PIX_FMT_MPEG = v4l2_fourcc('M', 'P', 'E', 'G');

    /// <summary>
    /// H264 with start codes
    /// </summary>
    public static readonly uint V4L2_PIX_FMT_H264 = v4l2_fourcc('H', '2', '6', '4');

    /// <summary>
    /// H264 without start codes
    /// </summary>
    public static readonly uint V4L2_PIX_FMT_H264_NO_SC = v4l2_fourcc('A', 'V', 'C', '1');

    /// <summary>
    /// H264 MVC
    /// </summary>
    public static readonly uint V4L2_PIX_FMT_H264_MVC = v4l2_fourcc('M', '2', '6', '4');

    /// <summary>
    /// H263
    /// </summary>
    public static readonly uint V4L2_PIX_FMT_H263 = v4l2_fourcc('H', '2', '6', '3');

    /// <summary>
    /// MPEG-1 ES
    /// </summary>
    public static readonly uint V4L2_PIX_FMT_MPEG1 = v4l2_fourcc('M', 'P', 'G', '1');

    /// <summary>
    /// MPEG-2 ES
    /// </summary>
    public static readonly uint V4L2_PIX_FMT_MPEG2 = v4l2_fourcc('M', 'P', 'G', '2');

    /// <summary>
    /// MPEG-2 parsed slice data
    /// </summary>
    public static readonly uint V4L2_PIX_FMT_MPEG2_SLICE = v4l2_fourcc('M', 'G', '2', 'S');

    /// <summary>
    /// MPEG-4 part 2 ES
    /// </summary>
    public static readonly uint V4L2_PIX_FMT_MPEG4 = v4l2_fourcc('M', 'P', 'G', '4');

    /// <summary>
    /// Xvid
    /// </summary>
    public static readonly uint V4L2_PIX_FMT_XVID = v4l2_fourcc('X', 'V', 'I', 'D');

    /// <summary>
    /// SMPTE 421M Annex G compliant stream
    /// </summary>
    public static readonly uint V4L2_PIX_FMT_VC1_ANNEX_G = v4l2_fourcc('V', 'C', '1', 'G');

    /// <summary>
    /// SMPTE 421M Annex L compliant stream
    /// </summary>
    public static readonly uint V4L2_PIX_FMT_VC1_ANNEX_L = v4l2_fourcc('V', 'C', '1', 'L');

    /// <summary>
    /// VP8
    /// </summary>
    public static readonly uint V4L2_PIX_FMT_VP8 = v4l2_fourcc('V', 'P', '8', '0');

    /// <summary>
    /// VP8 parsed frame
    /// </summary>
    public static readonly uint V4L2_PIX_FMT_VP8_FRAME = v4l2_fourcc('V', 'P', '8', 'F');

    /// <summary>
    /// VP9
    /// </summary>
    public static readonly uint V4L2_PIX_FMT_VP9 = v4l2_fourcc('V', 'P', '9', '0');

    /// <summary>
    /// VP9 parsed frame
    /// </summary>
    public static readonly uint V4L2_PIX_FMT_VP9_FRAME = v4l2_fourcc('V', 'P', '9', 'F');

    /// <summary>
    /// HEVC aka H.265
    /// </summary>
    public static readonly uint V4L2_PIX_FMT_HEVC = v4l2_fourcc('H', 'E', 'V', 'C');

    /// <summary>
    /// Fast Walsh Hadamard Transform (vicodec)
    /// </summary>
    public static readonly uint V4L2_PIX_FMT_FWHT = v4l2_fourcc('F', 'W', 'H', 'T');

    /// <summary>
    /// Stateless FWHT (vicodec)
    /// </summary>
    public static readonly uint V4L2_PIX_FMT_FWHT_STATELESS = v4l2_fourcc('S', 'F', 'W', 'H');

    /// <summary>
    /// H264 parsed slices
    /// </summary>
    public static readonly uint V4L2_PIX_FMT_H264_SLICE = v4l2_fourcc('S', '2', '6', '4');

    /// <summary>
    /// HEVC parsed slices
    /// </summary>
    public static readonly uint V4L2_PIX_FMT_HEVC_SLICE = v4l2_fourcc('S', '2', '6', '5');

    /// <summary>
    /// AV1 parsed frame
    /// </summary>
    public static readonly uint V4L2_PIX_FMT_AV1_FRAME = v4l2_fourcc('A', 'V', '1', 'F');

    /// <summary>
    /// Sorenson Spark
    /// </summary>
    public static readonly uint V4L2_PIX_FMT_SPK = v4l2_fourcc('S', 'P', 'K', '0');

    /// <summary>
    /// RealVideo 8
    /// </summary>
    public static readonly uint V4L2_PIX_FMT_RV30 = v4l2_fourcc('R', 'V', '3', '0');

    /// <summary>
    /// RealVideo 9 & 10
    /// </summary>
    public static readonly uint V4L2_PIX_FMT_RV40 = v4l2_fourcc('R', 'V', '4', '0');
}