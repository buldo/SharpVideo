using System.Runtime.InteropServices;

namespace SharpVideo.Linux.Native.Tests;

/// <summary>
/// P/Invoke declarations for the native test library
/// </summary>
public static unsafe partial class NativeTestLibrary
{
    private const string LibraryName = "libtest_structures.so";

    // Function to fill drmModeRes structure with test data
    [LibraryImport(LibraryName, EntryPoint = "fill_native_drm_mode_res")]
    public static partial void FillNativeDrmModeRes(DrmModeRes* structure);

    // Function to get drmModeRes structure size for verification
    [LibraryImport(LibraryName, EntryPoint = "get_native_drm_mode_res_size")]
    public static partial int GetNativeDrmModeResSize();

    // Function to fill drmModeEncoder structure with test data
    [LibraryImport(LibraryName, EntryPoint = "fill_native_drm_mode_encoder")]
    public static partial void FillNativeDrmModeEncoder(DrmModeEncoder* structure);

    // Function to get drmModeEncoder structure size for verification
    [LibraryImport(LibraryName, EntryPoint = "get_native_drm_mode_encoder_size")]
    public static partial int GetNativeDrmModeEncoderSize();

    // Function to fill drmModeConnector structure with test data
    [LibraryImport(LibraryName, EntryPoint = "fill_native_drm_mode_connector")]
    public static partial void FillNativeDrmModeConnector(DrmModeConnector* structure);

    // Function to get drmModeConnector structure size for verification
    [LibraryImport(LibraryName, EntryPoint = "get_native_drm_mode_connector_size")]
    public static partial int GetNativeDrmModeConnectorSize();

    // Function to fill drmModeCrtc structure with test data
    [LibraryImport(LibraryName, EntryPoint = "fill_native_drm_mode_crtc")]
    public static partial void FillNativeDrmModeCrtc(DrmModeCrtc* structure);

    // Function to get drmModeCrtc structure size for verification
    [LibraryImport(LibraryName, EntryPoint = "get_native_drm_mode_crtc_size")]
    public static partial int GetNativeDrmModeCrtcSize();

    // Function to fill drmModeModeInfo structure with test data
    [LibraryImport(LibraryName, EntryPoint = "fill_native_drm_mode_mode_info")]
    public static partial void FillNativeDrmModeModeInfo(DrmModeModeInfo* structure);

    // Function to get drmModeModeInfo structure size for verification
    [LibraryImport(LibraryName, EntryPoint = "get_native_drm_mode_mode_info_size")]
    public static partial int GetNativeDrmModeModeInfoSize();

    // Function to fill drmModePlane structure with test data
    [LibraryImport(LibraryName, EntryPoint = "fill_native_drm_mode_plane")]
    public static partial void FillNativeDrmModePlane(DrmModePlane* structure);

    // Function to get drmModePlane structure size for verification
    [LibraryImport(LibraryName, EntryPoint = "get_native_drm_mode_plane_size")]
    public static partial int GetNativeDrmModePlaneSize();

    // Function to fill drmModeFB structure with test data
    [LibraryImport(LibraryName, EntryPoint = "fill_native_drm_mode_fb")]
    public static partial void FillNativeDrmModeFB(DrmModeFB* structure);

    // Function to get drmModeFB structure size for verification
    [LibraryImport(LibraryName, EntryPoint = "get_native_drm_mode_fb_size")]
    public static partial int GetNativeDrmModeFBSize();

    // Function to fill dma_heap_allocation_data structure with test data
    [LibraryImport(LibraryName, EntryPoint = "fill_native_dma_heap_allocation_data")]
    public static partial void FillNativeDmaHeapAllocationData(DmaHeapAllocationData* structure);

    // Function to get dma_heap_allocation_data structure size for verification
    [LibraryImport(LibraryName, EntryPoint = "get_native_dma_heap_allocation_data_size")]
    public static partial int GetNativeDmaHeapAllocationDataSize();

    // Function to get the real DMA_HEAP_IOCTL_ALLOC constant value from Linux headers
    [LibraryImport(LibraryName, EntryPoint = "get_native_dma_heap_ioctl_alloc")]
    public static partial uint GetNativeDmaHeapIoctlAlloc();

    // Function to get __off_t size for verification
    [LibraryImport(LibraryName, EntryPoint = "get_off_t_size")]
    public static partial int GetOffTSize();

    // V4L2 structure testing functions

    // Function to fill v4l2_capability structure with test data
    [LibraryImport(LibraryName, EntryPoint = "fill_native_v4l2_capability")]
    public static partial void FillNativeV4L2Capability(V4L2Capability* structure);

    // Function to get v4l2_capability structure size for verification
    [LibraryImport(LibraryName, EntryPoint = "get_native_v4l2_capability_size")]
    public static partial int GetNativeV4L2CapabilitySize();

    // Function to fill v4l2_pix_format_mplane structure with test data
    [LibraryImport(LibraryName, EntryPoint = "fill_native_v4l2_pix_format_mplane")]
    public static partial void FillNativeV4L2PixFormatMplane(V4L2PixFormatMplane* structure);

    // Function to get v4l2_pix_format_mplane structure size for verification
    [LibraryImport(LibraryName, EntryPoint = "get_native_v4l2_pix_format_mplane_size")]
    public static partial int GetNativeV4L2PixFormatMplaneSize();

    // Function to fill v4l2_format structure with test data
    [LibraryImport(LibraryName, EntryPoint = "fill_native_v4l2_format")]
    public static partial void FillNativeV4L2Format(V4L2Format* structure);

    // Function to get v4l2_format structure size for verification
    [LibraryImport(LibraryName, EntryPoint = "get_native_v4l2_format_size")]
    public static partial int GetNativeV4L2FormatSize();

    // Function to fill v4l2_requestbuffers structure with test data
    [LibraryImport(LibraryName, EntryPoint = "fill_native_v4l2_requestbuffers")]
    public static partial void FillNativeV4L2RequestBuffers(V4L2RequestBuffers* structure);

    // Function to get v4l2_requestbuffers structure size for verification
    [LibraryImport(LibraryName, EntryPoint = "get_native_v4l2_requestbuffers_size")]
    public static partial int GetNativeV4L2RequestBuffersSize();

    // Function to fill v4l2_buffer structure with test data
    [LibraryImport(LibraryName, EntryPoint = "fill_native_v4l2_buffer")]
    public static partial void FillNativeV4L2Buffer(V4L2Buffer* structure);

    // Function to get v4l2_buffer structure size for verification
    [LibraryImport(LibraryName, EntryPoint = "get_native_v4l2_buffer_size")]
    public static partial int GetNativeV4L2BufferSize();

    // Function to fill v4l2_exportbuffer structure with test data
    [LibraryImport(LibraryName, EntryPoint = "fill_native_v4l2_exportbuffer")]
    public static partial void FillNativeV4L2ExportBuffer(V4L2ExportBuffer* structure);

    // Function to get v4l2_exportbuffer structure size for verification
    [LibraryImport(LibraryName, EntryPoint = "get_native_v4l2_exportbuffer_size")]
    public static partial int GetNativeV4L2ExportBufferSize();

    // Function to fill v4l2_decoder_cmd structure with test data
    [LibraryImport(LibraryName, EntryPoint = "fill_native_v4l2_decoder_cmd")]
    public static partial void FillNativeV4L2DecoderCmd(V4L2DecoderCmd* structure);

    // Function to get v4l2_decoder_cmd structure size for verification
    [LibraryImport(LibraryName, EntryPoint = "get_native_v4l2_decoder_cmd_size")]
    public static partial int GetNativeV4L2DecoderCmdSize();

    // Function to fill v4l2_ext_control structure with test data
    [LibraryImport(LibraryName, EntryPoint = "fill_native_v4l2_ext_control")]
    public static partial void FillNativeV4L2ExtControl(V4L2ExtControl* structure);

    // Function to get v4l2_ext_control structure size for verification
    [LibraryImport(LibraryName, EntryPoint = "get_native_v4l2_ext_control_size")]
    public static partial int GetNativeV4L2ExtControlSize();

    // Function to fill v4l2_ctrl_h264_sps structure with test data
    [LibraryImport(LibraryName, EntryPoint = "fill_native_v4l2_ctrl_h264_sps")]
    public static partial void FillNativeV4L2CtrlH264Sps(IntPtr structure);

    // Function to get v4l2_ctrl_h264_sps structure size for verification
    [LibraryImport(LibraryName, EntryPoint = "get_native_v4l2_ctrl_h264_sps_size")]
    public static partial int GetNativeV4L2CtrlH264SpsSize();

    // New native V4L2 control constant accessors

    [LibraryImport(LibraryName, EntryPoint = "get_native_v4l2_ctrl_class_user")]
    public static partial uint GetNativeV4L2CtrlClassUser();

    [LibraryImport(LibraryName, EntryPoint = "get_native_v4l2_ctrl_class_codec")]
    public static partial uint GetNativeV4L2CtrlClassCodec();

    [LibraryImport(LibraryName, EntryPoint = "get_native_v4l2_ctrl_class_camera")]
    public static partial uint GetNativeV4L2CtrlClassCamera();

    [LibraryImport(LibraryName, EntryPoint = "get_native_v4l2_ctrl_class_fm_tx")]
    public static partial uint GetNativeV4L2CtrlClassFmTx();

    [LibraryImport(LibraryName, EntryPoint = "get_native_v4l2_ctrl_class_flash")]
    public static partial uint GetNativeV4L2CtrlClassFlash();

    [LibraryImport(LibraryName, EntryPoint = "get_native_v4l2_ctrl_class_jpeg")]
    public static partial uint GetNativeV4L2CtrlClassJpeg();

    [LibraryImport(LibraryName, EntryPoint = "get_native_v4l2_ctrl_class_image_source")]
    public static partial uint GetNativeV4L2CtrlClassImageSource();

    [LibraryImport(LibraryName, EntryPoint = "get_native_v4l2_ctrl_class_image_proc")]
    public static partial uint GetNativeV4L2CtrlClassImageProc();

    [LibraryImport(LibraryName, EntryPoint = "get_native_v4l2_ctrl_class_dv")]
    public static partial uint GetNativeV4L2CtrlClassDv();

    [LibraryImport(LibraryName, EntryPoint = "get_native_v4l2_ctrl_class_fm_rx")]
    public static partial uint GetNativeV4L2CtrlClassFmRx();

    [LibraryImport(LibraryName, EntryPoint = "get_native_v4l2_ctrl_class_rf_tuner")]
    public static partial uint GetNativeV4L2CtrlClassRfTuner();

    [LibraryImport(LibraryName, EntryPoint = "get_native_v4l2_ctrl_class_detect")]
    public static partial uint GetNativeV4L2CtrlClassDetect();

    [LibraryImport(LibraryName, EntryPoint = "get_native_v4l2_ctrl_class_codec_stateless")]
    public static partial uint GetNativeV4L2CtrlClassCodecStateless();

    [LibraryImport(LibraryName, EntryPoint = "get_native_v4l2_ctrl_class_colorimetry")]
    public static partial uint GetNativeV4L2CtrlClassColorimetry();

    [LibraryImport(LibraryName, EntryPoint = "get_native_v4l2_cid_codec_stateless_base")]
    public static partial uint GetNativeV4L2CidCodecStatelessBase();

    [LibraryImport(LibraryName, EntryPoint = "get_native_v4l2_cid_codec_stateless_class")]
    public static partial uint GetNativeV4L2CidCodecStatelessClass();

    [LibraryImport(LibraryName, EntryPoint = "get_native_v4l2_cid_stateless_h264_decode_mode")]
    public static partial uint GetNativeV4L2CidStatelessH264DecodeMode();

    [LibraryImport(LibraryName, EntryPoint = "get_native_v4l2_cid_stateless_h264_start_code")]
    public static partial uint GetNativeV4L2CidStatelessH264StartCode();

    [LibraryImport(LibraryName, EntryPoint = "get_native_v4l2_cid_stateless_h264_sps")]
    public static partial uint GetNativeV4L2CidStatelessH264Sps();

    [LibraryImport(LibraryName, EntryPoint = "get_native_v4l2_cid_stateless_h264_pps")]
    public static partial uint GetNativeV4L2CidStatelessH264Pps();

    [LibraryImport(LibraryName, EntryPoint = "get_native_v4l2_cid_stateless_h264_scaling_matrix")]
    public static partial uint GetNativeV4L2CidStatelessH264ScalingMatrix();

    [LibraryImport(LibraryName, EntryPoint = "get_native_v4l2_cid_stateless_h264_pred_weights")]
    public static partial uint GetNativeV4L2CidStatelessH264PredWeights();

    [LibraryImport(LibraryName, EntryPoint = "get_native_v4l2_cid_stateless_h264_slice_params")]
    public static partial uint GetNativeV4L2CidStatelessH264SliceParams();

    [LibraryImport(LibraryName, EntryPoint = "get_native_v4l2_cid_stateless_h264_decode_params")]
    public static partial uint GetNativeV4L2CidStatelessH264DecodeParams();
}
