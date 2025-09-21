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

    public V4L2StatelessControlManager(
        ILogger<V4L2StatelessControlManager> logger,
        IH264ParameterSetParser parameterSetParser,
        int deviceFd)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _parameterSetParser = parameterSetParser ?? throw new ArgumentNullException(nameof(parameterSetParser));
        _deviceFd = deviceFd;
    }

    /// <inheritdoc />
    public async Task SetParameterSetControlsAsync(byte[] spsData, byte[] ppsData, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Setting SPS/PPS controls for stateless decoder");

        try
        {
            // Parse SPS and PPS to control structures
            var spsControl = _parameterSetParser.ParseSpsToControl(spsData);
            var ppsControl = _parameterSetParser.ParsePpsToControl(ppsData);

            // Create extended controls array
            var extControls = new V4L2ExtControl[2];

            // Allocate unmanaged memory for SPS data
            var spsPtr = Marshal.AllocHGlobal(Marshal.SizeOf<V4L2CtrlH264Sps>());
            Marshal.StructureToPtr(spsControl, spsPtr, false);

            // Allocate unmanaged memory for PPS data
            var ppsPtr = Marshal.AllocHGlobal(Marshal.SizeOf<V4L2CtrlH264Pps>());
            Marshal.StructureToPtr(ppsControl, ppsPtr, false);

            try
            {
                // Set up SPS control
                extControls[0] = new V4L2ExtControl
                {
                    Id = V4L2Constants.V4L2_CID_STATELESS_H264_SPS,
                    Size = (uint)Marshal.SizeOf<V4L2CtrlH264Sps>(),
                    Ptr = spsPtr
                };

                // Set up PPS control
                extControls[1] = new V4L2ExtControl
                {
                    Id = V4L2Constants.V4L2_CID_STATELESS_H264_PPS,
                    Size = (uint)Marshal.SizeOf<V4L2CtrlH264Pps>(),
                    Ptr = ppsPtr
                };

                // Allocate memory for controls array
                var controlsPtr = Marshal.AllocHGlobal(Marshal.SizeOf<V4L2ExtControl>() * 2);
                try
                {
                    // Copy controls to unmanaged memory
                    Marshal.StructureToPtr(extControls[0], controlsPtr, false);
                    Marshal.StructureToPtr(extControls[1],
                        IntPtr.Add(controlsPtr, Marshal.SizeOf<V4L2ExtControl>()), false);

                    // Set up extended controls structure
                    var extCtrlsStruct = new V4L2ExtControls
                    {
                        Which = V4L2Constants.V4L2_CTRL_CLASS_CODEC,
                        Count = 2,
                        Controls = controlsPtr
                    };

                    // Set the controls
                    var result = LibV4L2.SetExtendedControls(_deviceFd, ref extCtrlsStruct);
                    if (!result.Success)
                    {
                        _logger.LogError("Failed to set SPS/PPS controls: {Error}", result.ErrorMessage);
                        throw new InvalidOperationException($"Failed to set parameter set controls: {result.ErrorMessage}");
                    }

                    _logger.LogInformation("Successfully set SPS and PPS controls for stateless decoder");
                }
                finally
                {
                    Marshal.FreeHGlobal(controlsPtr);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(spsPtr);
                Marshal.FreeHGlobal(ppsPtr);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting parameter set controls");
            throw;
        }

        await Task.CompletedTask;
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

            // Create extended controls array
            var extControls = new V4L2ExtControl[2];

            // Allocate unmanaged memory for slice params
            var slicePtr = Marshal.AllocHGlobal(Marshal.SizeOf<V4L2CtrlH264SliceParams>());
            Marshal.StructureToPtr(sliceParams, slicePtr, false);

            // Allocate unmanaged memory for decode params
            var decodePtr = Marshal.AllocHGlobal(Marshal.SizeOf<V4L2CtrlH264DecodeParams>());
            Marshal.StructureToPtr(decodeParams, decodePtr, false);

            try
            {
                // Set up slice params control
                extControls[0] = new V4L2ExtControl
                {
                    Id = V4L2Constants.V4L2_CID_STATELESS_H264_SLICE_PARAMS,
                    Size = (uint)Marshal.SizeOf<V4L2CtrlH264SliceParams>(),
                    Ptr = slicePtr
                };

                // Set up decode params control
                extControls[1] = new V4L2ExtControl
                {
                    Id = V4L2Constants.V4L2_CID_STATELESS_H264_DECODE_PARAMS,
                    Size = (uint)Marshal.SizeOf<V4L2CtrlH264DecodeParams>(),
                    Ptr = decodePtr
                };

                // Allocate memory for controls array
                var controlsPtr = Marshal.AllocHGlobal(Marshal.SizeOf<V4L2ExtControl>() * 2);
                try
                {
                    // Copy controls to unmanaged memory
                    Marshal.StructureToPtr(extControls[0], controlsPtr, false);
                    Marshal.StructureToPtr(extControls[1],
                        IntPtr.Add(controlsPtr, Marshal.SizeOf<V4L2ExtControl>()), false);

                    // Set up extended controls structure
                    var extCtrlsStruct = new V4L2ExtControls
                    {
                        Which = V4L2Constants.V4L2_CTRL_CLASS_CODEC,
                        Count = 2,
                        Controls = controlsPtr
                    };

                    // Set the controls
                    var result = LibV4L2.SetExtendedControls(_deviceFd, ref extCtrlsStruct);
                    if (!result.Success)
                    {
                        _logger.LogWarning("Failed to set slice/decode params controls: {Error}", result.ErrorMessage);
                        // Don't throw - some drivers may not support all controls
                    }
                    else
                    {
                        _logger.LogDebug("Successfully set slice and decode parameters controls");
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(controlsPtr);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(slicePtr);
                Marshal.FreeHGlobal(decodePtr);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error setting slice parameters controls - continuing without them");
            // Don't throw - slice parameter controls may not be fully supported by all drivers
        }

        await Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<bool> ConfigureStatelessControlsAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Configuring stateless decoder controls...");

        try
        {
            // First, try to set decode mode to frame-based (preferred)
            var decodeModeControl = new V4L2Control
            {
                Id = V4L2Constants.V4L2_CID_STATELESS_H264_DECODE_MODE,
                Value = (int)V4L2Constants.V4L2_STATELESS_H264_DECODE_MODE_FRAME_BASED
            };

            var result = LibV4L2.SetControl(_deviceFd, ref decodeModeControl);
            if (result.Success)
            {
                _logger.LogInformation("Set stateless decoder to frame-based mode");
            }
            else
            {
                _logger.LogWarning("Failed to set frame-based mode, trying slice-based: {Error}", result.ErrorMessage);

                // Try slice-based mode as fallback
                decodeModeControl.Value = (int)V4L2Constants.V4L2_STATELESS_H264_DECODE_MODE_SLICE_BASED;
                result = LibV4L2.SetControl(_deviceFd, ref decodeModeControl);

                if (result.Success)
                {
                    _logger.LogInformation("Set stateless decoder to slice-based mode");
                }
                else
                {
                    _logger.LogWarning("Failed to set decode mode: {Error}", result.ErrorMessage);
                }
            }

            // Configure start code format - try Annex-B first, then none
            var startCodeControl = new V4L2Control
            {
                Id = V4L2Constants.V4L2_CID_STATELESS_H264_START_CODE,
                Value = (int)V4L2Constants.V4L2_STATELESS_H264_START_CODE_ANNEX_B
            };

            result = LibV4L2.SetControl(_deviceFd, ref startCodeControl);
            if (result.Success)
            {
                _logger.LogInformation("Successfully configured stateless decoder for Annex-B format (with start codes)");
                return true; // Use start codes
            }
            else
            {
                _logger.LogWarning("Failed to set Annex-B format: {Error}. Trying without start codes.", result.ErrorMessage);

                // Try setting to no start codes
                startCodeControl.Value = (int)V4L2Constants.V4L2_STATELESS_H264_START_CODE_NONE;
                result = LibV4L2.SetControl(_deviceFd, ref startCodeControl);

                if (result.Success)
                {
                    _logger.LogInformation("Successfully configured stateless decoder for raw NALUs (without start codes)");
                    return false; // Don't use start codes
                }
                else
                {
                    _logger.LogWarning("Failed to configure start code control. Using default Annex-B format: {Error}", result.ErrorMessage);
                    return true; // Default fallback to start codes
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error configuring stateless decoder controls. Using default settings");
            return true; // Default fallback to start codes
        }
        finally
        {
            await Task.CompletedTask;
        }
    }

    /// <summary>
    /// Set SPS and PPS parameter sets via V4L2 extended controls
    /// </summary>
    public async Task SetParameterSetsAsync(int deviceFd, V4L2CtrlH264Sps sps, V4L2CtrlH264Pps pps)
    {
        try
        {
            _logger.LogInformation("Setting SPS/PPS controls for stateless decoder");

            // Validate SPS parameters
            if (sps.ProfileIdc == 0 || sps.LevelIdc == 0)
            {
                throw new ArgumentException("Invalid SPS: ProfileIdc and LevelIdc must be non-zero");
            }

            if (sps.PicWidthInMbsMinus1 == 0xFFFF || sps.PicHeightInMapUnitsMinus1 == 0xFFFF)
            {
                throw new ArgumentException("Invalid SPS: Picture dimensions are invalid");
            }

            // Log SPS details for debugging
            _logger.LogInformation("SPS Details: Profile=0x{Profile:X2}, Level=0x{Level:X2}, Constraints=0x{Constraints:X2}, " +
                                   "ChromaFormat={Chroma}, BitDepthLuma={LumaBits}, BitDepthChroma={ChromaBits}, " +
                                   "Width={Width}MB, Height={Height}MB, MaxRefFrames={MaxRef}",
                sps.ProfileIdc, sps.LevelIdc, sps.ConstraintSetFlags, sps.ChromaFormatIdc,
                sps.BitDepthLumaMinus8 + 8, sps.BitDepthChromaMinus8 + 8,
                sps.PicWidthInMbsMinus1 + 1, sps.PicHeightInMapUnitsMinus1 + 1, sps.MaxNumRefFrames);

            // Log PPS details for debugging
            _logger.LogInformation("PPS Details: ID={PpsId}, SpsId={SpsId}, SliceGroups={SliceGroups}, " +
                                   "RefIdxL0={RefL0}, RefIdxL1={RefL1}, WeightedBipred={WeightedBipred}, " +
                                   "InitQP={InitQP}, ChromaQpOffset={ChromaOffset}",
                pps.PicParameterSetId, pps.SeqParameterSetId, pps.NumSliceGroupsMinus1 + 1,
                pps.NumRefIdxL0DefaultActiveMinus1 + 1, pps.NumRefIdxL1DefaultActiveMinus1 + 1,
                pps.WeightedBipredIdc, pps.PicInitQpMinus26 + 26, pps.ChromaQpIndexOffset);

            // Create extended controls array
            var extControls = new V4L2ExtControl[2];

            // Allocate unmanaged memory for SPS data
            var spsPtr = Marshal.AllocHGlobal(Marshal.SizeOf<V4L2CtrlH264Sps>());
            Marshal.StructureToPtr(sps, spsPtr, false);

            // Allocate unmanaged memory for PPS data
            var ppsPtr = Marshal.AllocHGlobal(Marshal.SizeOf<V4L2CtrlH264Pps>());
            Marshal.StructureToPtr(pps, ppsPtr, false);

            try
            {
                // Setup SPS control
                extControls[0] = new V4L2ExtControl
                {
                    Id = V4L2Constants.V4L2_CID_STATELESS_H264_SPS,
                    Size = (uint)Marshal.SizeOf<V4L2CtrlH264Sps>(),
                    Ptr = spsPtr
                };

                // Setup PPS control
                extControls[1] = new V4L2ExtControl
                {
                    Id = V4L2Constants.V4L2_CID_STATELESS_H264_PPS,
                    Size = (uint)Marshal.SizeOf<V4L2CtrlH264Pps>(),
                    Ptr = ppsPtr
                };

                // Allocate memory for controls array
                var controlsPtr = Marshal.AllocHGlobal(Marshal.SizeOf<V4L2ExtControl>() * 2);
                try
                {
                    // Copy controls to unmanaged memory
                    Marshal.StructureToPtr(extControls[0], controlsPtr, false);
                    Marshal.StructureToPtr(extControls[1],
                        IntPtr.Add(controlsPtr, Marshal.SizeOf<V4L2ExtControl>()), false);

                    // Set extended controls
                    var extControlsWrapper = new V4L2ExtControls
                    {
                        Which = V4L2Constants.V4L2_CTRL_CLASS_CODEC,
                        Count = 2,
                        Controls = controlsPtr
                    };

                    var result = LibV4L2.SetExtendedControls(deviceFd, ref extControlsWrapper);
                    if (!result.Success)
                    {
                        _logger.LogError("Failed to set SPS/PPS controls: {Error}", result.ErrorMessage);

                        // Try alternative approach: set controls individually
                        _logger.LogInformation("Attempting to set SPS and PPS controls individually...");

                        var spsControlsWrapper = new V4L2ExtControls
                        {
                            Which = V4L2Constants.V4L2_CTRL_CLASS_CODEC,
                            Count = 1,
                            Controls = controlsPtr // Points to SPS control
                        };

                        var spsResult = LibV4L2.SetExtendedControls(deviceFd, ref spsControlsWrapper);
                        if (!spsResult.Success)
                        {
                            _logger.LogError("Failed to set SPS control individually: {Error}", spsResult.ErrorMessage);
                        }
                        else
                        {
                            _logger.LogInformation("Successfully set SPS control individually");
                        }

                        var ppsControlsWrapper = new V4L2ExtControls
                        {
                            Which = V4L2Constants.V4L2_CTRL_CLASS_CODEC,
                            Count = 1,
                            Controls = IntPtr.Add(controlsPtr, Marshal.SizeOf<V4L2ExtControl>()) // Points to PPS control
                        };

                        var ppsResult = LibV4L2.SetExtendedControls(deviceFd, ref ppsControlsWrapper);
                        if (!ppsResult.Success)
                        {
                            _logger.LogError("Failed to set PPS control individually: {Error}", ppsResult.ErrorMessage);
                            throw new InvalidOperationException($"Failed to set parameter sets: SPS={spsResult.ErrorMessage}, PPS={ppsResult.ErrorMessage}");
                        }
                        else
                        {
                            _logger.LogInformation("Successfully set PPS control individually");
                        }

                        if (spsResult.Success && ppsResult.Success)
                        {
                            _logger.LogInformation("Successfully set SPS and PPS controls individually");
                        }
                        else
                        {
                            throw new InvalidOperationException($"Failed to set parameter sets: {result.ErrorMessage}");
                        }
                    }
                    else
                    {
                        _logger.LogInformation("Successfully set SPS and PPS controls together");
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(controlsPtr);
                }
            }
            finally
            {
                // Free allocated memory
                Marshal.FreeHGlobal(spsPtr);
                Marshal.FreeHGlobal(ppsPtr);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set parameter sets");
            throw;
        }

        await Task.CompletedTask;
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