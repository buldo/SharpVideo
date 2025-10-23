using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using SharpVideo.Drm;
using SharpVideo.Linux.Native;
using SharpVideo.V4L2;
using SharpVideo.V4L2Decoding.Models;

namespace SharpVideo.V4L2Decoding.Services;

/// <summary>
/// H.264 V4L2 Stateful Decoder - hardware decoder manages frame references and state internally
/// </summary>
[SupportedOSPlatform("linux")]
public class H264V4L2StatefulDecoder : H264V4L2DecoderBase
{
    private readonly MediaDevice? _mediaDevice;

    public H264V4L2StatefulDecoder(
        V4L2Device device,
        MediaDevice? mediaDevice,
        ILogger<H264V4L2StatefulDecoder> logger,
        DecoderConfiguration configuration,
        Action<ReadOnlySpan<byte>>? processDecodedAction,
        DrmBufferManager? drmBufferManager)
        : base(device, logger, configuration, processDecodedAction, drmBufferManager)
    {
        _mediaDevice = mediaDevice;
    }

    /// <summary>
    /// Decodes H.264 stream using V4L2 stateful hardware decoder
    /// </summary>
    public async Task DecodeStreamAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        if (stream == null)
        {
            throw new ArgumentNullException(nameof(stream));
        }

        if (!stream.CanRead)
        {
            throw new ArgumentException("Stream must be readable", nameof(stream));
        }

        Logger.LogInformation("Starting H.264 stateful stream decode");
        var decodingStopwatch = Stopwatch.StartNew();

        await FeedStreamToDecoderAsync(stream, cancellationToken);

        // Drain the pipeline
        await DrainDecoderAsync(cancellationToken);

        decodingStopwatch.Stop();
        Statistics.DecodeElapsed = decodingStopwatch.Elapsed;
        var fps = Statistics.DecodeElapsed.TotalSeconds > 0
            ? FramesDecoded / Statistics.DecodeElapsed.TotalSeconds
            : 0;

        Logger.LogInformation(
            "Stateful decoding completed successfully. {FrameCount} frames in {ElapsedTime:F2}s ({FPS:F2} fps)",
            FramesDecoded,
            Statistics.DecodeElapsed.TotalSeconds,
            fps);
    }

    /// <summary>
    /// Feed stream data directly to the decoder (stateful decoders accept raw H.264 stream)
    /// </summary>
    private async Task FeedStreamToDecoderAsync(Stream stream, CancellationToken cancellationToken)
    {
        const int bufferSize = 64 * 1024; // 64KB chunks
        var buffer = new byte[bufferSize];
        long totalBytesRead = 0;

        try
        {
            int readIterations = 0;
            int bytesRead;
            while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
            {
                // Ensure there's a free buffer available
                Device.OutputMPlaneQueue.EnsureFreeBuffer();

                // Write buffer directly to output queue (stateful decoder handles parsing)
                Device.OutputMPlaneQueue.WriteBufferAndEnqueue(buffer.AsSpan(0, bytesRead), null);

                totalBytesRead += bytesRead;
                readIterations++;

                if (readIterations <= 5 || readIterations % 500 == 0)
                {
                    Logger.LogInformation(
                        "Fed chunk #{Chunk} ({Bytes} bytes); total {Total} bytes",
                        readIterations,
                        bytesRead,
                        totalBytesRead);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error while feeding stream to decoder");
            throw;
        }
        finally
        {
            Logger.LogInformation("Completed feeding bitstream: {BytesRead} bytes", totalBytesRead);
        }
    }

    /// <summary>
    /// Drain the decoder pipeline by sending V4L2_DEC_CMD_STOP and waiting for completion
    /// </summary>
    private async Task DrainDecoderAsync(CancellationToken cancellationToken)
    {
        Logger.LogInformation("Draining decoder pipeline...");

        // Send stop command to trigger decoder drain
        try
        {
            Device.DecoderCommandStop();
            Logger.LogDebug("Sent V4L2_DEC_CMD_STOP command");
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to send decoder stop command, using fallback drain");
        }

        int drainAttempts = 0;
        int lastFrameCount = FramesDecoded;

        // Wait for all frames to be processed
        while (drainAttempts < 100)
        {
            Device.OutputMPlaneQueue.ReclaimProcessed();

// Check for progress
            if (FramesDecoded != lastFrameCount)
            {
                Logger.LogDebug("Decoded {NewFrames} more frames during drain", FramesDecoded - lastFrameCount);
                lastFrameCount = FramesDecoded;
                drainAttempts = 0;
            }

            await Task.Delay(1, cancellationToken);
            drainAttempts++;
        }

        Logger.LogInformation("Decoder pipeline drained");
    }

    protected override void ConfigureFormats()
    {
        Logger.LogInformation("Configuring stateful decoder formats...");

        // For stateful decoders, we use V4L2_PIX_FMT_H264 (not H264_SLICE)
        var outputFormat = new V4L2PixFormatMplane
        {
            Width = Configuration.InitialWidth,
            Height = Configuration.InitialHeight,
            PixelFormat = V4L2PixelFormats.V4L2_PIX_FMT_H264, // Full H.264 stream for stateful
            NumPlanes = 1,
            Field = (uint)V4L2Field.NONE,
            Colorspace = 5, // V4L2_COLORSPACE_REC709
            YcbcrEncoding = 1, // V4L2_YCBCR_ENC_DEFAULT
            Quantization = 1, // V4L2_QUANTIZATION_DEFAULT
            XferFunc = 1 // V4L2_XFER_FUNC_DEFAULT
        };
        Device.SetOutputFormatMPlane(outputFormat);

        var confirmedOutputFormat = Device.GetOutputFormatMPlane();
        Logger.LogInformation(
            "Set output format: {Width}x{Height} H264 ({Planes} plane(s))",
            confirmedOutputFormat.Width,
            confirmedOutputFormat.Height,
            confirmedOutputFormat.NumPlanes);

        // Configure capture format (decoded output)
        var captureFormat = new V4L2PixFormatMplane
        {
            Width = Configuration.InitialWidth,
            Height = Configuration.InitialHeight,
            PixelFormat = Configuration.PreferredPixelFormat, // Usually NV12
            NumPlanes = 2, // NV12 typically has 2 planes
            Field = (uint)V4L2Field.NONE,
            Colorspace = 5,
            YcbcrEncoding = 1,
            Quantization = 1,
            XferFunc = 1
        };

        Device.SetCaptureFormatMPlane(captureFormat);
    }

    protected override void ConfigureDecoderControls()
    {
        // Stateful decoders typically don't need explicit control configuration
        // The hardware manages SPS/PPS/DPB internally
        Logger.LogInformation("Stateful decoder: no explicit controls needed");
    }

    protected override void SetupDecoderBuffers()
    {
        Logger.LogInformation("Setting up and mapping buffers...");

        // Setup OUTPUT buffers for encoded stream data
        SetupMMapBufferQueue(Device.OutputMPlaneQueue, Configuration.OutputBufferCount);

        // Media requests are not typically used with stateful decoders
        if (_mediaDevice != null)
        {
            Logger.LogWarning("Media device provided but stateful decoders typically don't use media requests");
        }

        // Setup CAPTURE buffers for decoded frames
        if (Configuration.UseDrmPrimeBuffers)
        {
            SetupDmaBufCaptureQueue();
        }
        else
        {
            SetupMMapBufferQueue(Device.CaptureMPlaneQueue, Configuration.CaptureBufferCount);
        }
    }
}
