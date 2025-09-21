using System.Runtime.InteropServices;
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
    /// Try format for the specified buffer type without applying it.
    /// </summary>
    /// <param name="fd">Open V4L2 device file descriptor</param>
    /// <param name="format">Format structure with desired format</param>
    /// <returns>Result of the operation</returns>
    public static IoctlResult TryFormat(int fd, ref V4L2Format format)
    {
        return IoctlHelper.Ioctl(fd, V4L2Constants.VIDIOC_TRY_FMT, ref format);
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
    /// Query buffer information.
    /// </summary>
    /// <param name="fd">Open V4L2 device file descriptor</param>
    /// <param name="buffer">Buffer structure with index and type set, receives buffer info</param>
    /// <returns>Result of the operation</returns>
    public static IoctlResult QueryBuffer(int fd, ref V4L2Buffer buffer)
    {
        return IoctlHelper.Ioctl(fd, V4L2Constants.VIDIOC_QUERYBUF, ref buffer);
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
    /// Export buffer as DMABUF file descriptor.
    /// </summary>
    /// <param name="fd">Open V4L2 device file descriptor</param>
    /// <param name="exportBuffer">Export buffer structure</param>
    /// <returns>Result of the operation</returns>
    public static IoctlResult ExportBuffer(int fd, ref V4L2ExportBuffer exportBuffer)
    {
        return IoctlHelper.Ioctl(fd, V4L2Constants.VIDIOC_EXPBUF, ref exportBuffer);
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
    /// Get stream parameters.
    /// </summary>
    /// <param name="fd">Open V4L2 device file descriptor</param>
    /// <param name="streamParm">Stream parameters structure</param>
    /// <returns>Result of the operation</returns>
    public static IoctlResult GetStreamParameters(int fd, ref V4L2StreamParm streamParm)
    {
        return IoctlHelper.Ioctl(fd, V4L2Constants.VIDIOC_G_PARM, ref streamParm);
    }

    /// <summary>
    /// Set stream parameters.
    /// </summary>
    /// <param name="fd">Open V4L2 device file descriptor</param>
    /// <param name="streamParm">Stream parameters structure</param>
    /// <returns>Result of the operation</returns>
    public static IoctlResult SetStreamParameters(int fd, ref V4L2StreamParm streamParm)
    {
        return IoctlHelper.Ioctl(fd, V4L2Constants.VIDIOC_S_PARM, ref streamParm);
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
    /// Try decoder command without executing it.
    /// </summary>
    /// <param name="fd">Open V4L2 device file descriptor</param>
    /// <param name="decoderCmd">Decoder command structure</param>
    /// <returns>Result of the operation</returns>
    public static IoctlResult TryDecoderCommand(int fd, ref V4L2DecoderCmd decoderCmd)
    {
        return IoctlHelper.Ioctl(fd, V4L2Constants.VIDIOC_TRY_DECODER_CMD, ref decoderCmd);
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
    /// Send encoder command.
    /// </summary>
    /// <param name="fd">Open V4L2 device file descriptor</param>
    /// <param name="encoderCmd">Encoder command structure</param>
    /// <returns>Result of the operation</returns>
    public static IoctlResult EncoderCommand(int fd, ref V4L2EncoderCmd encoderCmd)
    {
        return IoctlHelper.Ioctl(fd, V4L2Constants.VIDIOC_ENCODER_CMD, ref encoderCmd);
    }

    /// <summary>
    /// Try encoder command without executing it.
    /// </summary>
    /// <param name="fd">Open V4L2 device file descriptor</param>
    /// <param name="encoderCmd">Encoder command structure</param>
    /// <returns>Result of the operation</returns>
    public static IoctlResult TryEncoderCommand(int fd, ref V4L2EncoderCmd encoderCmd)
    {
        return IoctlHelper.Ioctl(fd, V4L2Constants.VIDIOC_TRY_ENCODER_CMD, ref encoderCmd);
    }

    // High-level helper methods for common operations

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
            Type = V4L2Constants.V4L2_BUF_TYPE_VIDEO_CAPTURE_MPLANE,
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
    /// Helper method to request DMABUF buffers for multiplanar capture.
    /// </summary>
    /// <param name="fd">Open V4L2 device file descriptor</param>
    /// <param name="count">Number of buffers to request</param>
    /// <returns>Result of the operation and the actual count allocated</returns>
    public static (IoctlResult Result, uint Count) RequestMultiplanarDmaBufCapture(int fd, uint count)
    {
        var reqbufs = new V4L2RequestBuffers
        {
            Count = count,
            Type = V4L2Constants.V4L2_BUF_TYPE_VIDEO_CAPTURE_MPLANE,
            Memory = V4L2Constants.V4L2_MEMORY_DMABUF
        };

        var result = RequestBuffers(fd, ref reqbufs);
        return (result, reqbufs.Count);
    }

    /// <summary>
    /// Helper method to export a buffer plane as DMABUF file descriptor.
    /// </summary>
    /// <param name="fd">Open V4L2 device file descriptor</param>
    /// <param name="index">Buffer index</param>
    /// <param name="plane">Plane index</param>
    /// <returns>Result of the operation and the DMABUF file descriptor</returns>
    public static (IoctlResult Result, int DmaBufFd) ExportMultiplanarBuffer(int fd, uint index, uint plane)
    {
        var expbuf = new V4L2ExportBuffer
        {
            Type = V4L2Constants.V4L2_BUF_TYPE_VIDEO_CAPTURE_MPLANE,
            Index = index,
            Plane = plane,
            Flags = 0
        };

        var result = ExportBuffer(fd, ref expbuf);
        return (result, expbuf.Fd);
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
    /// Get a single control value.
    /// </summary>
    /// <param name="fd">Open V4L2 device file descriptor</param>
    /// <param name="control">Control structure with ID set, receives current value</param>
    /// <returns>Result of the operation</returns>
    public static IoctlResult GetControl(int fd, ref V4L2Control control)
    {
        return IoctlHelper.Ioctl(fd, V4L2Constants.VIDIOC_G_CTRL, ref control);
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
    /// Get extended V4L2 controls (for complex data structures).
    /// </summary>
    /// <param name="fd">Open V4L2 device file descriptor</param>
    /// <param name="extControls">Extended controls structure</param>
    /// <returns>Result of the operation</returns>
    public static IoctlResult GetExtendedControls(int fd, ref V4L2ExtControls extControls)
    {
        return IoctlHelper.Ioctl(fd, V4L2Constants.VIDIOC_G_EXT_CTRLS, ref extControls);
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
    /// Query menu item information for a menu control.
    /// </summary>
    /// <param name="fd">Open V4L2 device file descriptor</param>
    /// <param name="queryMenuItem">Menu item query structure</param>
    /// <returns>Result of the operation</returns>
    public static IoctlResult QueryMenuItem(int fd, ref V4L2QueryMenuItem queryMenuItem)
    {
        return IoctlHelper.Ioctl(fd, V4L2Constants.VIDIOC_QUERYMENU, ref queryMenuItem);
    }
}