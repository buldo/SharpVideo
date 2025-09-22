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
    public async Task SetSliceParamsControlsAsync(byte[] sliceData, H264NaluType sliceType)
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
            SetSingleExtendedControl(V4l2ControlsConstants.V4L2_CID_STATELESS_H264_SLICE_PARAMS, sliceParams);

            // Set decode parameters control
            _logger.LogDebug("Setting decode parameters control...");
            SetSingleExtendedControl(V4l2ControlsConstants.V4L2_CID_STATELESS_H264_DECODE_PARAMS, decodeParams);

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
    /// Set SPS and PPS parameter sets via V4L2 extended controls (simplified approach)
    /// </summary>
    public async Task SetParameterSetsAsync(V4L2CtrlH264Sps sps, V4L2CtrlH264Pps pps)
    {
        _logger.LogInformation("Setting SPS/PPS controls for stateless decoder");

        // Validate SPS and PPS parameters
        ValidateSpsParameters(sps);
        ValidatePpsParameters(pps);

        // Log structure details for debugging
        LogParameterDetails(sps, pps);

        try
        {
            // Set SPS control first
            _logger.LogDebug("Setting SPS control...");
            SetSingleExtendedControl(V4l2ControlsConstants.V4L2_CID_STATELESS_H264_SPS, sps);

            // Set PPS control second
            _logger.LogDebug("Setting PPS control...");
            SetSingleExtendedControl(V4l2ControlsConstants.V4L2_CID_STATELESS_H264_PPS, pps);

            _logger.LogInformation("Successfully set SPS and PPS controls");
        }
        catch (ArgumentException ex)
        {
            _logger.LogError(ex, "Parameter validation failed");
            throw new InvalidOperationException($"Invalid parameter sets: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set parameter sets");
            throw new InvalidOperationException($"Failed to set parameter sets: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Set a single extended control - much simpler and more predictable
    /// </summary>
    private void SetSingleExtendedControl<T>(uint controlId, T data) where T : struct
    {
        var size = (uint)Marshal.SizeOf<T>();
        var dataPtr = Marshal.AllocHGlobal((int)size);

        try
        {
            // Clear the allocated memory
            unsafe
            {
                byte* ptr = (byte*)dataPtr.ToPointer();
                for (int i = 0; i < size; i++)
                {
                    ptr[i] = 0;
                }
            }

            // Marshal the structure to unmanaged memory
            Marshal.StructureToPtr(data, dataPtr, false);

            // Create the control structure
            var control = new V4L2ExtControl
            {
                Id = controlId,
                Size = size,
                Ptr = dataPtr
            };

            // Allocate memory for single control
            var controlPtr = Marshal.AllocHGlobal(Marshal.SizeOf<V4L2ExtControl>());

            try
            {
                Marshal.StructureToPtr(control, controlPtr, false);

                // Set up extended controls wrapper for single control
                var extControlsWrapper = new V4L2ExtControls
                {
                    Which = V4l2ControlsConstants.V4L2_CTRL_CLASS_CODEC,
                    Count = 1,
                    Controls = controlPtr
                };

                _logger.LogDebug("Setting control 0x{ControlId:X8} with {Size} bytes", controlId, size);

                // Set the control
                var result = LibV4L2.SetExtendedControls(_device.fd, ref extControlsWrapper);
                if (!result.Success)
                {
                    var errorCode = Marshal.GetLastWin32Error();
                    _logger.LogError("Failed to set control 0x{ControlId:X8}: {Error} (errno: {ErrorCode})",
                        controlId, result.ErrorMessage, errorCode);

                    throw new InvalidOperationException($"Failed to set control 0x{controlId:X8}: {result.ErrorMessage} (errno: {errorCode})");
                }

                _logger.LogDebug("Successfully set control 0x{ControlId:X8}", controlId);
            }
            finally
            {
                Marshal.FreeHGlobal(controlPtr);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(dataPtr);
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

    /// <summary>
    /// Validate SPS parameters before sending to V4L2
    /// </summary>
    private static void ValidateSpsParameters(V4L2CtrlH264Sps sps)
    {
        if (sps.ProfileIdc == 0 || sps.LevelIdc == 0)
        {
            throw new ArgumentException("Invalid SPS: ProfileIdc and LevelIdc must be non-zero");
        }

        if (sps.PicWidthInMbsMinus1 == 0xFFFF || sps.PicHeightInMapUnitsMinus1 == 0xFFFF)
        {
            throw new ArgumentException("Invalid SPS: Picture dimensions are invalid");
        }

        // Validate array is properly initialized
        if (sps.OffsetForRefFrame == null)
        {
            throw new ArgumentException("Invalid SPS: OffsetForRefFrame array is null");
        }

        if (sps.OffsetForRefFrame.Length != 255)
        {
            throw new ArgumentException($"Invalid SPS: OffsetForRefFrame array must be 255 elements, got {sps.OffsetForRefFrame.Length}");
        }

        // Validate reasonable values
        if (sps.MaxNumRefFrames > 16)
        {
            throw new ArgumentException($"Invalid SPS: MaxNumRefFrames ({sps.MaxNumRefFrames}) exceeds reasonable limit");
        }

        if (sps.ChromaFormatIdc > 3)
        {
            throw new ArgumentException($"Invalid SPS: ChromaFormatIdc ({sps.ChromaFormatIdc}) is invalid");
        }

        if (sps.BitDepthLumaMinus8 > 8 || sps.BitDepthChromaMinus8 > 8)
        {
            throw new ArgumentException($"Invalid SPS: Bit depth values too high (Luma: {sps.BitDepthLumaMinus8 + 8}, Chroma: {sps.BitDepthChromaMinus8 + 8})");
        }
    }

    /// <summary>
    /// Validate PPS parameters before sending to V4L2
    /// </summary>
    private static void ValidatePpsParameters(V4L2CtrlH264Pps pps)
    {
        if (pps.NumSliceGroupsMinus1 > 7)
        {
            throw new ArgumentException($"Invalid PPS: NumSliceGroupsMinus1 ({pps.NumSliceGroupsMinus1}) exceeds maximum");
        }

        if (pps.NumRefIdxL0DefaultActiveMinus1 > 31)
        {
            throw new ArgumentException($"Invalid PPS: NumRefIdxL0DefaultActiveMinus1 ({pps.NumRefIdxL0DefaultActiveMinus1}) exceeds maximum");
        }

        if (pps.NumRefIdxL1DefaultActiveMinus1 > 31)
        {
            throw new ArgumentException($"Invalid PPS: NumRefIdxL1DefaultActiveMinus1 ({pps.NumRefIdxL1DefaultActiveMinus1}) exceeds maximum");
        }

        if (pps.WeightedBipredIdc > 2)
        {
            throw new ArgumentException($"Invalid PPS: WeightedBipredIdc ({pps.WeightedBipredIdc}) is invalid");
        }

        if (pps.PicInitQpMinus26 < -26 || pps.PicInitQpMinus26 > 25)
        {
            throw new ArgumentException($"Invalid PPS: PicInitQpMinus26 ({pps.PicInitQpMinus26}) is out of range");
        }
    }

    /// <summary>
    /// Log parameter details for debugging
    /// </summary>
    private void LogParameterDetails(V4L2CtrlH264Sps sps, V4L2CtrlH264Pps pps)
    {
        // Log SPS details for debugging
        _logger.LogInformation("SPS Details: Profile=0x{Profile:X2}, Level=0x{Level:X2}, Constraints=0x{Constraints:X2}, " +
                               "ChromaFormat={Chroma}, BitDepthLuma={LumaBits}, BitDepthChroma={ChromaBits}, " +
                               "Width={Width}MB, Height={Height}MB, MaxRefFrames={MaxRef}",
            sps.ProfileIdc, sps.LevelIdc, sps.ConstraintSetFlags, sps.ChromaFormatIdc,
            sps.BitDepthLumaMinus8 + 8, sps.BitDepthChromaMinus8 + 8,
            sps.PicWidthInMbsMinus1 + 1, sps.PicHeightInMapUnitsMinus1 + 1, sps.MaxNumRefFrames);

        // Log structure sizes for debugging
        _logger.LogInformation("Structure sizes: SPS={SpsSize} bytes, PPS={PpsSize} bytes",
            Marshal.SizeOf<V4L2CtrlH264Sps>(), Marshal.SizeOf<V4L2CtrlH264Pps>());

        // Log PPS details for debugging
        _logger.LogInformation("PPS Details: ID={PpsId}, SpsId={SpsId}, SliceGroups={SliceGroups}, " +
                               "RefIdxL0={RefL0}, RefIdxL1={RefL1}, WeightedBipred={WeightedBipred}, " +
                               "InitQP={InitQP}, ChromaQpOffset={ChromaOffset}",
            pps.PicParameterSetId, pps.SeqParameterSetId, pps.NumSliceGroupsMinus1 + 1,
            pps.NumRefIdxL0DefaultActiveMinus1 + 1, pps.NumRefIdxL1DefaultActiveMinus1 + 1,
            pps.WeightedBipredIdc, pps.PicInitQpMinus26 + 26, pps.ChromaQpIndexOffset);
    }
}