using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using SharpVideo.Drm;
using SharpVideo.Linux.Native;
using SharpVideo.V4L2;
using SharpVideo.V4L2Decoding.Models;

namespace SharpVideo.V4L2Decoding.Services;

/// <summary>
/// Factory for creating appropriate H.264 V4L2 decoder based on hardware capabilities
/// </summary>
[SupportedOSPlatform("linux")]
public static class H264V4L2DecoderFactory
{
    /// <summary>
    /// Detects decoder type supported by the device
    /// </summary>
    public static DecoderType DetectDecoderType(V4L2DeviceInfo deviceInfo)
    {
        // Check if device supports stateless H.264 slice format
     var supportsStateless = deviceInfo.SupportedFormats.Any(f => 
            f.PixelFormat == V4L2PixelFormats.V4L2_PIX_FMT_H264_SLICE);

        // Check if device supports stateful H.264 stream format
        var supportsStateful = deviceInfo.SupportedFormats.Any(f => 
      f.PixelFormat == V4L2PixelFormats.V4L2_PIX_FMT_H264);

 // Prefer stateless for better control if both are supported
        if (supportsStateless)
        {
    return DecoderType.Stateless;
        }

if (supportsStateful)
        {
            return DecoderType.Stateful;
        }

        throw new NotSupportedException(
         $"Device does not support H.264 decoding. Supported formats: {string.Join(", ", deviceInfo.SupportedFormats.Select(f => f.Description))}");
    }

    /// <summary>
    /// Creates appropriate decoder based on device capabilities
    /// </summary>
    public static H264V4L2DecoderBase CreateDecoder(
        V4L2Device device,
  V4L2DeviceInfo deviceInfo,
        MediaDevice? mediaDevice,
 ILogger logger,
     DecoderConfiguration configuration,
        Action<ReadOnlySpan<byte>>? processDecodedAction,
        DrmBufferManager? drmBufferManager)
    {
        var decoderType = DetectDecoderType(deviceInfo);
        return CreateDecoder(
            decoderType,
      device,
            mediaDevice,
       logger,
       configuration,
          processDecodedAction,
            drmBufferManager);
    }

    /// <summary>
    /// Creates decoder of specified type
    /// </summary>
    public static H264V4L2DecoderBase CreateDecoder(
        DecoderType decoderType,
        V4L2Device device,
        MediaDevice? mediaDevice,
        ILogger logger,
        DecoderConfiguration configuration,
    Action<ReadOnlySpan<byte>>? processDecodedAction,
   DrmBufferManager? drmBufferManager)
    {
        return decoderType switch
   {
      DecoderType.Stateless => new H264V4L2StatelessDecoder(
       device,
      mediaDevice,
          LoggerFactory.CreateStatelessLogger(logger),
           configuration,
         processDecodedAction,
      drmBufferManager),

  DecoderType.Stateful => new H264V4L2StatefulDecoder(
             device,
  mediaDevice,
      LoggerFactory.CreateStatefulLogger(logger),
          configuration,
  processDecodedAction,
    drmBufferManager),

       _ => throw new ArgumentOutOfRangeException(nameof(decoderType), decoderType, "Unknown decoder type")
     };
    }

    private static class LoggerFactory
    {
        public static ILogger<H264V4L2StatelessDecoder> CreateStatelessLogger(ILogger baseLogger)
        {
            return new DelegatingLogger<H264V4L2StatelessDecoder>(baseLogger);
        }

        public static ILogger<H264V4L2StatefulDecoder> CreateStatefulLogger(ILogger baseLogger)
        {
            return new DelegatingLogger<H264V4L2StatefulDecoder>(baseLogger);
        }

        private class DelegatingLogger<T> : ILogger<T>
{
            private readonly ILogger _logger;

         public DelegatingLogger(ILogger logger)
            {
  _logger = logger;
   }

  public IDisposable? BeginScope<TState>(TState state) where TState : notnull
      => _logger.BeginScope(state);

   public bool IsEnabled(LogLevel logLevel)
            => _logger.IsEnabled(logLevel);

  public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
                => _logger.Log(logLevel, eventId, state, exception, formatter);
        }
    }
}
