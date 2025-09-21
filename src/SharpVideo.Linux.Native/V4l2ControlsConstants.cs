namespace SharpVideo.Linux.Native;

public static class V4l2ControlsConstants
{
    /* Control classes */
    /// <summary>
    /// Old-style 'user' controls
    /// </summary>
    public const uint V4L2_CTRL_CLASS_USER = 0x00980000;

    /// <summary>
    /// Stateful codec controls
    /// </summary>
    public const uint V4L2_CTRL_CLASS_CODEC = 0x00990000;

    /// <summary>
    /// Camera class controls
    /// </summary>
    public const uint V4L2_CTRL_CLASS_CAMERA = 0x009a0000;

    /// <summary>
    /// FM Modulator controls
    /// </summary>
    public const uint V4L2_CTRL_CLASS_FM_TX = 0x009b0000;

    /// <summary>
    /// Camera flash controls
    /// </summary>
    public const uint V4L2_CTRL_CLASS_FLASH = 0x009c0000;

    /// <summary>
    /// JPEG-compression controls
    /// </summary>
    public const uint V4L2_CTRL_CLASS_JPEG = 0x009d0000;

    /// <summary>
    /// Image source controls
    /// </summary>
    public const uint V4L2_CTRL_CLASS_IMAGE_SOURCE = 0x009e0000;

    /// <summary>
    /// Image processing controls
    /// </summary>
    public const uint V4L2_CTRL_CLASS_IMAGE_PROC = 0x009f0000;

    /// <summary>
    /// Digital Video controls
    /// </summary>
    public const uint V4L2_CTRL_CLASS_DV = 0x00a00000;

    /// <summary>
    /// FM Receiver controls
    /// </summary>
    public const uint V4L2_CTRL_CLASS_FM_RX = 0x00a10000;

    /// <summary>
    /// RF tuner controls
    /// </summary>
    public const uint V4L2_CTRL_CLASS_RF_TUNER = 0x00a20000;

    /// <summary>
    /// Detection controls
    /// </summary>
    public const uint V4L2_CTRL_CLASS_DETECT = 0x00a30000;

    /// <summary>
    /// Stateless codecs controls
    /// </summary>
    public const uint V4L2_CTRL_CLASS_CODEC_STATELESS = 0x00a40000;

    /// <summary>
    /// Colorimetry controls
    /// </summary>
    public const uint V4L2_CTRL_CLASS_COLORIMETRY = 0x00a50000;



    public const uint V4L2_CID_CODEC_STATELESS_BASE = (V4L2_CTRL_CLASS_CODEC_STATELESS | 0x900);
    public const uint V4L2_CID_CODEC_STATELESS_CLASS = (V4L2_CTRL_CLASS_CODEC_STATELESS | 1);

    public const uint V4L2_CID_STATELESS_H264_DECODE_MODE = (V4L2_CID_CODEC_STATELESS_BASE + 0);
    public const uint V4L2_CID_STATELESS_H264_START_CODE = (V4L2_CID_CODEC_STATELESS_BASE + 1);
    public const uint V4L2_CID_STATELESS_H264_SPS = (V4L2_CID_CODEC_STATELESS_BASE + 2);
    public const uint V4L2_CID_STATELESS_H264_PPS = (V4L2_CID_CODEC_STATELESS_BASE + 3);
    public const uint V4L2_CID_STATELESS_H264_SCALING_MATRIX = (V4L2_CID_CODEC_STATELESS_BASE + 4);
    public const uint V4L2_CID_STATELESS_H264_PRED_WEIGHTS = (V4L2_CID_CODEC_STATELESS_BASE + 5);
    public const uint V4L2_CID_STATELESS_H264_SLICE_PARAMS = (V4L2_CID_CODEC_STATELESS_BASE + 6);
    public const uint V4L2_CID_STATELESS_H264_DECODE_PARAMS = (V4L2_CID_CODEC_STATELESS_BASE + 7);
}