using System.Runtime.Versioning;

using SharpVideo.Linux.Native.V4L2;

namespace SharpVideo.V4L2;

[SupportedOSPlatform("linux")]
public static class V4L2DeviceExtensions
{
    public static void SetCaptureFormatMPlane(
        this V4L2Device device,
        V4L2PixFormatMplane pixFormat)
    {
        var format = new V4L2Format
        {
            Type = V4L2BufferType.VIDEO_CAPTURE_MPLANE,
            Pix_mp = pixFormat
        };

        device.SetFormat(ref format);
    }

    public static V4L2PixFormatMplane GetCaptureFormatMPlane(
        this V4L2Device device)
    {
        var format = new V4L2Format
        {
            Type = V4L2BufferType.VIDEO_CAPTURE_MPLANE
        };

        device.GetFormat(ref format);

        return format.Pix_mp;
    }

    public static void SetOutputFormatMPlane(
        this V4L2Device device,
        V4L2PixFormatMplane pixFormat)
    {
        var format = new V4L2Format
        {
            Type = V4L2BufferType.VIDEO_OUTPUT_MPLANE,
            Pix_mp = pixFormat
        };

        device.SetFormat(ref format);
    }

    public static V4L2PixFormatMplane GetOutputFormatMPlane(
        this V4L2Device device)
    {
        var format = new V4L2Format
        {
            Type = V4L2BufferType.VIDEO_OUTPUT_MPLANE
        };

        device.GetFormat(ref format);

        return format.Pix_mp;
    }
}