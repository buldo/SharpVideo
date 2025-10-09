# SharpVideo

This is experimenental project for working with video on linux at lowest possible level

# What is available
1. KMS/DRM abstractions with DMA buffers
2. Some V4L2 abstractions for statless decoding
3. H264 bitstream parsing

# Available examples
* DrmDemo - exploring DRM devices
* DrmDmaDemo - video output via DRM with DMA-BUF
* ParseH264Demo - parsing of h264 bitstream
* V4L2DecodeDemo - decoding h264 bitstream via V4L2 stateless decoder
* V4L2PrintInfo - printing information about V4L2 devices