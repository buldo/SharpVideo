# SharpVideo

This is experimenental project for working with video via C#.  
It focused on linux ARM environment with V4L2 decoding and DRM presenting. But some HW and OS independed code also provided.

# What is available
1. KMS/DRM abstractions with DMA buffers
2. Some V4L2 abstractions for decoding
3. H264 bitstream parsing

# Available examples
* `DrmDemo` - exploring DRM devices
* `DrmDmaDemo` - video output via DRM with DMA-BUF
* `FfmpegDemo` - using ffmpeg
* `ImGuiDemo` - using ImGui in DRM environment
* `MultiPlaneDemo` - multy-plane DRM output (planes overlay)
* `MultiPlaneGlDemo` - same as MultiPlaneDemo, but with OpenGL
* `ParseH264Demo` - parsing of h264 bitstream
* `RtpPlayerDemo` - RTP player
* `V4L2DecodeDemo` - decoding h264 bitstream via V4L2 stateless decoder
* `V4L2DecodeDrmPreviewDemo` - same as V4L2DecodeDemo but with preview
* `V4L2PrintInfo` - printing information about V4L2 devices