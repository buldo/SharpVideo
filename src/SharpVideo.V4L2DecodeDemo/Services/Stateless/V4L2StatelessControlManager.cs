using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using SharpVideo.H264;
using SharpVideo.Linux.Native;
using SharpVideo.V4L2;

namespace SharpVideo.V4L2DecodeDemo.Services.Stateless;

/// <summary>
/// Manages V4L2 extended controls for stateless H.264 decoders
/// </summary>
/// [SupportedOSPlatform("linux")]
public class V4L2StatelessControlManager
{
    private readonly ILogger<V4L2StatelessControlManager> _logger;
    private readonly H264ParameterSetParser _parameterSetParser;
    private readonly V4L2Device _device;

    public V4L2StatelessControlManager(
        ILogger<V4L2StatelessControlManager> logger,
        H264ParameterSetParser parameterSetParser,
        V4L2Device device)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _parameterSetParser = parameterSetParser ?? throw new ArgumentNullException(nameof(parameterSetParser));
        _device = device;
    }

    /// <inheritdoc />
    public void SetSliceParamsControls(ReadOnlySpan<byte> sliceData, H264NaluType sliceType)
    {
        _logger.LogDebug("Setting slice parameters controls for stateless decoder");

        try
        {
            // Parse slice header to control structure
            var sliceParams = _parameterSetParser.ParseSliceHeaderToControl(sliceData);

            // Create decode parameters (simplified for basic operation)
            var decodeParams = new V4L2CtrlH264DecodeParams
            {
                FrameNum = 0, // Would need proper frame numbering
                IdrPicId = (ushort)(sliceType == H264NaluType.CodedSliceIdr ? 1 : 0), // IDR picture ID
                PicOrderCntLsb = 0, // Picture order count
                DeltaPicOrderCntBottom = 0,
                DeltaPicOrderCnt0 = 0,
                DeltaPicOrderCnt1 = 0,
                DecRefPicMarkingBitSize = 0,
                PicOrderCntBitSize = 0,
                SliceGroupChangeCycle = 0,
                Flags = 0
            };

            // Set slice parameters control
            _logger.LogDebug("Setting slice parameters control...");
            _device.SetSingleExtendedControl(V4l2ControlsConstants.V4L2_CID_STATELESS_H264_SLICE_PARAMS, sliceParams);

            // Set decode parameters control
            _logger.LogDebug("Setting decode parameters control...");
            _device.SetSingleExtendedControl(V4l2ControlsConstants.V4L2_CID_STATELESS_H264_DECODE_PARAMS, decodeParams);

            _logger.LogDebug("Successfully set slice and decode parameters controls");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error setting slice parameters controls - continuing without them");
            // Don't throw - slice parameter controls may not be fully supported by all drivers
        }
    }

    public void ConfigureStatelessControls(V4L2StatelessH264DecodeMode decodeMode, V4L2StatelessH264StartCode startCode)
    {
        _logger.LogInformation("Configuring stateless decoder controls: {DecodeMode}, {StartCode}", decodeMode, startCode);

        if (!TrySetSimpleControl(
                V4l2ControlsConstants.V4L2_CID_STATELESS_H264_DECODE_MODE,
                (int)decodeMode,
                "stateless decoder to frame-based mode"))
        {
            throw new Exception($"Failed to set decode mode to {decodeMode}");
        }

        if (!TrySetSimpleControl(
                V4l2ControlsConstants.V4L2_CID_STATELESS_H264_START_CODE,
                (int)startCode,
                "start code format to Annex-B"))
        {
            throw new Exception($"Failed to set start code to {startCode}");
        }
    }


    /// <summary>
    /// Try to set a simple V4L2 control
    /// </summary>
    private bool TrySetSimpleControl(uint controlId, int value, string description)
    {
        try
        {
            var control = new V4L2Control
            {
                Id = controlId,
                Value = value
            };

            var result = LibV4L2.SetControl(_device.fd, ref control);
            if (result.Success)
            {
                _logger.LogInformation("Set {Description}", description);
                return true;
            }
            else
            {
                _logger.LogWarning("Failed to set {Description}: {Error}", description, result.ErrorMessage);
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Exception setting {Description}", description);
            return false;
        }
    }

}