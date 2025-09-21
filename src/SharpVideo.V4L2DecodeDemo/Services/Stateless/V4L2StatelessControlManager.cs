using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using SharpVideo.Linux.Native;
using SharpVideo.V4L2DecodeDemo.Interfaces;

namespace SharpVideo.V4L2DecodeDemo.Services.Stateless;

/// <summary>
/// Manages V4L2 extended controls for stateless H.264 decoders
/// </summary>
public class V4L2StatelessControlManager : IV4L2StatelessControlManager
{
    private readonly ILogger<V4L2StatelessControlManager> _logger;
    private readonly IH264ParameterSetParser _parameterSetParser;
    private readonly int _deviceFd;
    private readonly bool _useRkvdecControls;

    public V4L2StatelessControlManager(
        ILogger<V4L2StatelessControlManager> logger,
        IH264ParameterSetParser parameterSetParser,
        int deviceFd)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _parameterSetParser = parameterSetParser ?? throw new ArgumentNullException(nameof(parameterSetParser));
        _deviceFd = deviceFd;

        // Detect if this is a rkvdec device that needs special control IDs
        _useRkvdecControls = DetectRkvdecDevice(deviceFd);
        if (_useRkvdecControls)
        {
            _logger.LogInformation("Detected rkvdec device - using device-specific control IDs");
        }
    }

    /// <summary>
    /// Detect if this is a rkvdec device that requires special control IDs
    /// </summary>
    private bool DetectRkvdecDevice(int deviceFd)
    {
        try
        {
            // Try to query rkvdec-specific control to detect the device type
            var control = new V4L2Control
            {
                Id = V4L2Constants.RKVDEC_CID_H264_DECODE_MODE
            };

            var result = LibV4L2.GetControl(deviceFd, ref control);
            return result.Success; // If we can read this control, it's likely rkvdec
        }
        catch
        {
            return false;
        }
    }

    /// <inheritdoc />
    public async Task SetParameterSetControlsAsync(byte[] spsData, byte[] ppsData, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Setting SPS/PPS controls for stateless decoder");

        // Parse SPS and PPS to control structures
        var spsControl = _parameterSetParser.ParseSpsToControl(spsData);
        var ppsControl = _parameterSetParser.ParsePpsToControl(ppsData);

        // Use the main parameter setting method
        await SetParameterSetsAsync(_deviceFd, spsControl, ppsControl);
    }

    /// <inheritdoc />
    public async Task SetSliceParamsControlsAsync(byte[] sliceData, byte sliceType, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Setting slice parameters controls for stateless decoder");

        try
        {
            // Parse slice header to control structure
            var sliceParams = _parameterSetParser.ParseSliceHeaderToControl(sliceData, sliceType);

            // Create decode parameters (simplified for basic operation)
            var decodeParams = new V4L2CtrlH264DecodeParams
            {
                FrameNum = 0, // Would need proper frame numbering
                IdrPicId = (ushort)(sliceType == 5 ? 1 : 0), // IDR picture ID
                PicOrderCntLsb = 0, // Picture order count
                DeltaPicOrderCntBottom = 0,
                DeltaPicOrderCnt0 = 0,
                DeltaPicOrderCnt1 = 0,
                DecRefPicMarkingBitSize = 0,
                PicOrderCntBitSize = 0,
                SliceGroupChangeCycle = 0,
                Flags = 0
            };

            // Set slice parameters and decode parameters
            await SetExtendedControlsAsync(new[] {
                CreateExtendedControl(V4L2Constants.V4L2_CID_STATELESS_H264_SLICE_PARAMS, sliceParams),
                CreateExtendedControl(V4L2Constants.V4L2_CID_STATELESS_H264_DECODE_PARAMS, decodeParams)
            });

            _logger.LogDebug("Successfully set slice and decode parameters controls");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error setting slice parameters controls - continuing without them");
            // Don't throw - slice parameter controls may not be fully supported by all drivers
        }
    }

    /// <inheritdoc />
    public Task<bool> ConfigureStatelessControlsAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Configuring stateless decoder controls...");

        try
        {
            // For rkvdec, the decode mode is fixed to frame-based and start code to Annex-B
            if (_useRkvdecControls)
            {
                _logger.LogInformation("Using rkvdec-specific controls - decode mode and start codes are fixed");

                // Try to set rkvdec controls to their expected values (they may be read-only)
                TrySetSimpleControl(V4L2Constants.RKVDEC_CID_H264_DECODE_MODE, 1, "rkvdec decode mode to frame-based");
                TrySetSimpleControl(V4L2Constants.RKVDEC_CID_H264_START_CODE, 1, "rkvdec start code format to Annex-B");

                return Task.FromResult(true); // rkvdec uses Annex-B format
            }

            // Try standard V4L2 stateless controls
            // First, try to set decode mode to frame-based (preferred)
            if (TrySetSimpleControl(V4L2Constants.V4L2_CID_STATELESS_H264_DECODE_MODE,
                    (int)V4L2Constants.V4L2_STATELESS_H264_DECODE_MODE_FRAME_BASED,
                    "stateless decoder to frame-based mode"))
            {
                _logger.LogInformation("Set stateless decoder to frame-based mode");
            }
            else
            {
                // Try slice-based mode as fallback
                TrySetSimpleControl(V4L2Constants.V4L2_CID_STATELESS_H264_DECODE_MODE,
                    (int)V4L2Constants.V4L2_STATELESS_H264_DECODE_MODE_SLICE_BASED,
                    "stateless decoder to slice-based mode");
            }

            // Configure start code format - try Annex-B first, then none
            if (TrySetSimpleControl(V4L2Constants.V4L2_CID_STATELESS_H264_START_CODE,
                    (int)V4L2Constants.V4L2_STATELESS_H264_START_CODE_ANNEX_B,
                    "start code format to Annex-B"))
            {
                _logger.LogInformation("Successfully configured stateless decoder for Annex-B format (with start codes)");
                return Task.FromResult(true); // Use start codes
            }
            else
            {
                // Try setting to no start codes
                if (TrySetSimpleControl(V4L2Constants.V4L2_CID_STATELESS_H264_START_CODE,
                        (int)V4L2Constants.V4L2_STATELESS_H264_START_CODE_NONE,
                        "start code format to none"))
                {
                    _logger.LogInformation("Successfully configured stateless decoder for raw NALUs (without start codes)");
                    return Task.FromResult(false); // Don't use start codes
                }
                else
                {
                    _logger.LogWarning("Failed to configure start code control. Using default Annex-B format");
                    return Task.FromResult(true); // Default fallback to start codes
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error configuring stateless decoder controls. Using default settings");
            return Task.FromResult(true); // Default fallback to start codes
        }
    }

    /// <summary>
    /// Set SPS and PPS parameter sets via V4L2 extended controls
    /// </summary>
    public async Task SetParameterSetsAsync(int deviceFd, V4L2CtrlH264Sps sps, V4L2CtrlH264Pps pps)
    {
        _logger.LogInformation("Setting SPS/PPS controls for stateless decoder");

        // Validate SPS parameters
        ValidateSpsParameters(sps);

        // Log structure details for debugging
        LogParameterDetails(sps, pps);

        try
        {
            // Set SPS and PPS controls together
            await SetExtendedControlsAsync(new[] {
                CreateExtendedControl(_useRkvdecControls ? V4L2Constants.RKVDEC_CID_H264_SPS : V4L2Constants.V4L2_CID_STATELESS_H264_SPS, sps),
                CreateExtendedControl(_useRkvdecControls ? V4L2Constants.RKVDEC_CID_H264_PPS : V4L2Constants.V4L2_CID_STATELESS_H264_PPS, pps)
            });

            _logger.LogInformation("Successfully set SPS and PPS controls");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set parameter sets");
            throw new InvalidOperationException($"Failed to set parameter sets: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Helper method to create an extended control structure for any data type
    /// </summary>
    private static (V4L2ExtControl control, IntPtr dataPtr) CreateExtendedControl<T>(uint controlId, T data) where T : struct
    {
        var size = (uint)Marshal.SizeOf<T>();
        var dataPtr = Marshal.AllocHGlobal((int)size);
        Marshal.StructureToPtr(data, dataPtr, false);

        var control = new V4L2ExtControl
        {
            Id = controlId,
            Size = size,
            Ptr = dataPtr
        };

        return (control, dataPtr);
    }

    /// <summary>
    /// Set multiple extended controls with proper memory management
    /// </summary>
    private async Task SetExtendedControlsAsync(IEnumerable<(V4L2ExtControl control, IntPtr dataPtr)> controls)
    {
        var controlList = controls.ToList();
        var controlArray = controlList.Select(c => c.control).ToArray();
        var dataPtrs = controlList.Select(c => c.dataPtr).ToList();

        // Allocate memory for controls array
        var controlsPtr = Marshal.AllocHGlobal(Marshal.SizeOf<V4L2ExtControl>() * controlArray.Length);

        try
        {
            // Copy controls to unmanaged memory
            for (int i = 0; i < controlArray.Length; i++)
            {
                var controlOffset = IntPtr.Add(controlsPtr, i * Marshal.SizeOf<V4L2ExtControl>());
                Marshal.StructureToPtr(controlArray[i], controlOffset, false);
            }

            // Set up extended controls structure
            var extControlsWrapper = new V4L2ExtControls
            {
                Which = V4L2Constants.V4L2_CTRL_CLASS_CODEC,
                Count = (uint)controlArray.Length,
                Controls = controlsPtr
            };

            // Set the controls
            var result = LibV4L2.SetExtendedControls(_deviceFd, ref extControlsWrapper);
            if (!result.Success)
            {
                throw new InvalidOperationException($"Failed to set extended controls: {result.ErrorMessage}");
            }
        }
        finally
        {
            // Free all allocated memory
            Marshal.FreeHGlobal(controlsPtr);
            foreach (var dataPtr in dataPtrs)
            {
                Marshal.FreeHGlobal(dataPtr);
            }
        }

        await Task.CompletedTask;
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

            var result = LibV4L2.SetControl(_deviceFd, ref control);
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
    /// Validate SPS parameters
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

    /// <summary>
    /// Set decode parameters for a frame
    /// </summary>
    public async Task SetDecodeParametersAsync(int deviceFd, V4L2CtrlH264DecodeParams decodeParams, V4L2CtrlH264SliceParams[] sliceParams)
    {
        try
        {
            _logger.LogInformation("Setting decode parameters for frame");

            // For now, use simplified approach
            // In a full implementation, we'd set all slice parameters
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set decode parameters");
            throw;
        }
    }

    /// <summary>
    /// Configure stateless decoder mode and settings
    /// </summary>
    public async Task ConfigureStatelessModeAsync(int deviceFd)
    {
        try
        {
            _logger.LogInformation("Configuring stateless decoder mode");

            // Use existing method
            await ConfigureStatelessControlsAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to configure stateless mode");
            throw;
        }
    }
}