# SharpVideo

An experimental project for working with video in C#.  
It focuses on Linux ARM environments with V4L2 decoding and DRM presentation. However, some hardware- and OS-independent code is also provided.

# What's Available

* `SharpVideo.Linux.Native` - multiple libs bindings. Covers some parts of DMA, DRM, V4L2, Udev, etc...
* `SharpVideo`
  * some managed abstractions over `SharpVideo.Linux.Native`
  * H264 bitstream parsing
* `SharpVideo.Decoding` - decoding via ffmpeg, v4l2 stateless and stateful
* `SharpVideo.FfmpegBin` - ffmpeg binaries
* `SharpVideo.ImGui` - integration code to run ImGui in DRM environment
* `SharpVideo.Utils` - some common code for example applications

1. KMS/DRM abstractions with DMA buffers
2. V4L2 abstractions for decoding
3. H.264 bitstream parsing

# Available Examples

* `DrmDemo` – Exploring DRM devices
* `DrmDmaDemo` – Video output via DRM with DMA-BUF
* `FfmpegDemo` – Using FFmpeg
* `ImGuiDemo` – Using ImGui in a DRM environment
* `MultiPlaneDemo` – Multi-plane DRM output (plane overlay)
* `MultiPlaneGlDemo` – Same as MultiPlaneDemo, but with OpenGL
* `ParseH264Demo` – Parsing H.264 bitstream
* `RtpPlayerDemo` – RTP player
* `V4L2DecodeDemo` – Decoding H.264 bitstream via V4L2 stateless decoder
* `V4L2DecodeDrmPreviewDemo` – Same as V4L2DecodeDemo, but with preview. Only stateless devices
* `V4L2DecodeDrmPreviewDemo2` – Base on SharpVideo.Decoding. Tested on RPi an RK3566 mainline
* `V4L2PrintInfo` – Printing information about V4L2 devices