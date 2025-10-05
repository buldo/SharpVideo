using System.Runtime.Versioning;

namespace SharpVideo.Linux.Native;

/// <summary>
/// P/Invoke wrapper for V4L2 (Video4Linux2) operations.
/// Provides high-level helper methods for common V4L2 operations using ioctl.
/// </summary>
[SupportedOSPlatform("linux")]
public static unsafe class LibV4L2
{
    /// <summary>
    /// Query device capabilities.
    /// </summary>
    /// <param name="fd">Open V4L2 device file descriptor</param>
    /// <param name="capability">Structure to receive capability information</param>
    /// <returns>Result of the operation</returns>
    public static IoctlResult QueryCapabilities(int fd, out V4L2Capability capability)
    {
        capability = new V4L2Capability();
        return IoctlHelper.Ioctl(fd, V4L2Constants.VIDIOC_QUERYCAP, ref capability);
    }

    /// <summary>
    /// Get current format for the specified buffer type.
    /// </summary>
    /// <param name="fd">Open V4L2 device file descriptor</param>
    /// <param name="format">Format structure with buffer type set, receives current format</param>
    /// <returns>Result of the operation</returns>
    public static IoctlResult GetFormat(int fd, ref V4L2Format format)
    {
        return IoctlHelper.Ioctl(fd, V4L2Constants.VIDIOC_G_FMT, ref format);
    }

    /// <summary>
    /// Set format for the specified buffer type.
    /// </summary>
    /// <param name="fd">Open V4L2 device file descriptor</param>
    /// <param name="format">Format structure with desired format</param>
    /// <returns>Result of the operation</returns>
    public static IoctlResult SetFormat(int fd, ref V4L2Format format)
    {
        return IoctlHelper.Ioctl(fd, V4L2Constants.VIDIOC_S_FMT, ref format);
    }

    /// <summary>
    /// Request buffer allocation.
    /// </summary>
    /// <param name="fd">Open V4L2 device file descriptor</param>
    /// <param name="requestBuffers">Buffer request structure</param>
    /// <returns>Result of the operation</returns>
    public static IoctlResult RequestBuffers(int fd, ref V4L2RequestBuffers requestBuffers)
    {
        return IoctlHelper.Ioctl(fd, V4L2Constants.VIDIOC_REQBUFS, ref requestBuffers);
    }

    /// <summary>
    /// Queue buffer for capture or output.
    /// </summary>
    /// <param name="fd">Open V4L2 device file descriptor</param>
    /// <param name="buffer">Buffer structure to queue</param>
    /// <returns>Result of the operation</returns>
    public static IoctlResult QueueBuffer(int fd, ref V4L2Buffer buffer)
    {
        return IoctlHelper.Ioctl(fd, V4L2Constants.VIDIOC_QBUF, ref buffer);
    }

    /// <summary>
    /// Query buffer properties (memory offsets, lengths, etc.).
    /// </summary>
    /// <param name="fd">Open V4L2 device file descriptor</param>
    /// <param name="buffer">Buffer structure with index/type set</param>
    /// <returns>Result of the operation</returns>
    public static IoctlResult QueryBuffer(int fd, ref V4L2Buffer buffer)
    {
        return IoctlHelper.Ioctl(fd, V4L2Constants.VIDIOC_QUERYBUF, ref buffer);
    }

    /// <summary>
    /// Dequeue buffer after capture or output.
    /// </summary>
    /// <param name="fd">Open V4L2 device file descriptor</param>
    /// <param name="buffer">Buffer structure to receive dequeued buffer info</param>
    /// <returns>Result of the operation</returns>
    public static IoctlResult DequeueBuffer(int fd, ref V4L2Buffer buffer)
    {
        return IoctlHelper.Ioctl(fd, V4L2Constants.VIDIOC_DQBUF, ref buffer);
    }


    /// <summary>
    /// Start streaming.
    /// </summary>
    /// <param name="fd">Open V4L2 device file descriptor</param>
    /// <param name="bufferType">Buffer type to start streaming</param>
    /// <returns>Result of the operation</returns>
    public static IoctlResult StreamOn(int fd, V4L2BufferType bufferType)
    {
        var type = (uint)bufferType;
        return IoctlHelper.Ioctl(fd, V4L2Constants.VIDIOC_STREAMON, ref type);
    }

    /// <summary>
    /// Stop streaming.
    /// </summary>
    /// <param name="fd">Open V4L2 device file descriptor</param>
    /// <param name="bufferType">Buffer type to stop streaming</param>
    /// <returns>Result of the operation</returns>
    public static IoctlResult StreamOff(int fd, V4L2BufferType bufferType)
    {
        var type = (uint)bufferType;
        return IoctlHelper.Ioctl(fd, V4L2Constants.VIDIOC_STREAMOFF, ref type);
    }


    /// <summary>
    /// Send decoder command.
    /// </summary>
    /// <param name="fd">Open V4L2 device file descriptor</param>
    /// <param name="decoderCmd">Decoder command structure</param>
    /// <returns>Result of the operation</returns>
    public static IoctlResult DecoderCommand(int fd, ref V4L2DecoderCmd decoderCmd)
    {
        return IoctlHelper.Ioctl(fd, V4L2Constants.VIDIOC_DECODER_CMD, ref decoderCmd);
    }

    /// <summary>
    /// Enumerate supported formats for a given buffer type.
    /// </summary>
    /// <param name="fd">Open V4L2 device file descriptor</param>
    /// <param name="fmtDesc">Format description structure with type and index set</param>
    /// <returns>Result of the operation</returns>
    public static IoctlResult EnumerateFormat(int fd, ref V4L2FmtDesc fmtDesc)
    {
        return IoctlHelper.Ioctl(fd, V4L2Constants.VIDIOC_ENUM_FMT, ref fmtDesc);
    }

    /// <summary>
    /// Helper method to set multiplanar capture format.
    /// </summary>
    /// <param name="fd">Open V4L2 device file descriptor</param>
    /// <param name="width">Frame width</param>
    /// <param name="height">Frame height</param>
    /// <param name="pixelFormat">Pixel format (FOURCC)</param>
    /// <param name="numPlanes">Number of planes</param>
    /// <returns>Result of the operation and the actual format set</returns>
    public static (IoctlResult Result, V4L2Format Format) SetMultiplanarCaptureFormat(
        int fd, uint width, uint height, uint pixelFormat, byte numPlanes)
    {
        var format = new V4L2Format
        {
            Type = V4L2BufferType.VIDEO_CAPTURE_MPLANE,
            Pix_mp = new V4L2PixFormatMplane
            {
                Width = width,
                Height = height,
                PixelFormat = pixelFormat,
                NumPlanes = numPlanes,
                Field = (uint)V4L2Field.NONE
            }
        };

        var result = SetFormat(fd, ref format);
        return (result, format);
    }

    /// <summary>
    /// Helper method to start decoder.
    /// </summary>
    /// <param name="fd">Open V4L2 device file descriptor</param>
    /// <returns>Result of the operation</returns>
    public static IoctlResult StartDecoder(int fd)
    {
        var cmd = new V4L2DecoderCmd
        {
            Cmd = (uint)V4L2DecoderCommand.START,
            Flags = 0
        };

        return DecoderCommand(fd, ref cmd);
    }

    /// <summary>
    /// Helper method to stop decoder.
    /// </summary>
    /// <param name="fd">Open V4L2 device file descriptor</param>
    /// <param name="immediately">Stop immediately or drain buffers first</param>
    /// <returns>Result of the operation</returns>
    public static IoctlResult StopDecoder(int fd, bool immediately = false)
    {
        var cmd = new V4L2DecoderCmd
        {
            Cmd = (uint)V4L2DecoderCommand.STOP,
            Flags = immediately ? 1u : 0u // V4L2_DEC_CMD_STOP_TO_BLACK = 1
        };

        return DecoderCommand(fd, ref cmd);
    }

    /// <summary>
    /// Helper method to flush decoder buffers.
    /// </summary>
    /// <param name="fd">Open V4L2 device file descriptor</param>
    /// <returns>Result of the operation</returns>
    public static IoctlResult FlushDecoder(int fd)
    {
        var cmd = new V4L2DecoderCmd
        {
            Cmd = (uint)V4L2DecoderCommand.FLUSH,
            Flags = 0
        };

        return DecoderCommand(fd, ref cmd);
    }

    /// <summary>
    /// Set a single control value.
    /// </summary>
    /// <param name="fd">Open V4L2 device file descriptor</param>
    /// <param name="control">Control structure with ID and value to set</param>
    /// <returns>Result of the operation</returns>
    public static IoctlResult SetControl(int fd, ref V4L2Control control)
    {
        return IoctlHelper.Ioctl(fd, V4L2Constants.VIDIOC_S_CTRL, ref control);
    }

    /// <summary>
    /// Set extended V4L2 controls (for complex data structures).
    /// </summary>
    /// <param name="fd">Open V4L2 device file descriptor</param>
    /// <param name="extControls">Extended controls structure</param>
    /// <returns>Result of the operation</returns>
    public static IoctlResult SetExtendedControls(int fd, ref V4L2ExtControls extControls)
    {
        return IoctlHelper.Ioctl(fd, V4L2Constants.VIDIOC_S_EXT_CTRLS, ref extControls);
    }

    /// <summary>
    /// Query control information.
    /// </summary>
    /// <param name="fd">Open V4L2 device file descriptor</param>
    /// <param name="queryCtrl">Control query structure</param>
    /// <returns>Result of the operation</returns>
    public static IoctlResult QueryControl(int fd, ref V4L2QueryCtrl queryCtrl)
    {
        return IoctlHelper.Ioctl(fd, V4L2Constants.VIDIOC_QUERYCTRL, ref queryCtrl);
    }

    /// <summary>
    /// Query extended control information (including compound controls).
    /// </summary>
    /// <param name="fd">Open V4L2 device file descriptor</param>
    /// <param name="queryExtCtrl">Extended control query structure</param>
    /// <returns>Result of the operation</returns>
    public static IoctlResult QueryExtendedControl(int fd, ref V4L2QueryExtCtrl queryExtCtrl)
    {
        return IoctlHelper.Ioctl(fd, V4L2Constants.VIDIOC_QUERY_EXT_CTRL, ref queryExtCtrl);
    }
}