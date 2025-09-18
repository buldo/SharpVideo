#include <stdint.h>
#include <string.h>
#include <fcntl.h>
#include <xf86drmMode.h>
#include <linux/dma-heap.h>
#include <linux/videodev2.h>

// We use the real DRM structures from libdrm headers

// Function to fill drmModeRes structure with test data
void fill_native_drm_mode_res(drmModeRes* s) {
    if (!s) return;

    s->count_fbs = 2;
    s->fbs = NULL; // We'll handle pointer arrays separately in tests
    s->count_crtcs = 3;
    s->crtcs = NULL;
    s->count_connectors = 4;
    s->connectors = NULL;
    s->count_encoders = 5;
    s->encoders = NULL;
    s->min_width = 640;
    s->max_width = 1920;
    s->min_height = 480;
    s->max_height = 1080;
}

// Function to get drmModeRes structure size for verification
int get_native_drm_mode_res_size(void) {
    return sizeof(drmModeRes);
}

// Function to fill drmModeEncoder structure with test data
void fill_native_drm_mode_encoder(drmModeEncoder* s) {
    if (!s) return;

    s->encoder_id = 100;
    s->encoder_type = 1; // DRM_MODE_ENCODER_DAC
    s->crtc_id = 200;
    s->possible_crtcs = 0x07; // Bitmask: can connect to CRTCs 0, 1, 2
    s->possible_clones = 0x03; // Bitmask: can clone encoders 0, 1
}

// Function to get drmModeEncoder structure size for verification
int get_native_drm_mode_encoder_size(void) {
    return sizeof(drmModeEncoder);
}

// Function to fill drmModeConnector structure with test data
void fill_native_drm_mode_connector(drmModeConnector* s) {
    if (!s) return;

    s->connector_id = 300;
    s->encoder_id = 100;
    s->connector_type = 11; // DRM_MODE_CONNECTOR_HDMIA
    s->connector_type_id = 1;
    s->connection = 1; // DRM_MODE_CONNECTED
    s->mmWidth = 510; // 510mm width
    s->mmHeight = 287; // 287mm height
    s->subpixel = 2; // DRM_MODE_SUBPIXEL_HORIZONTAL_RGB
    s->count_modes = 0;
    s->modes = NULL;
    s->count_props = 0;
    s->props = NULL;
    s->prop_values = NULL;
    s->count_encoders = 0;
    s->encoders = NULL;
}

// Function to get drmModeConnector structure size for verification
int get_native_drm_mode_connector_size(void) {
    return sizeof(drmModeConnector);
}

// Function to fill drmModeCrtc structure with test data
void fill_native_drm_mode_crtc(drmModeCrtc* s) {
    if (!s) return;

    s->crtc_id = 400;
    s->buffer_id = 500;
    s->x = 0;
    s->y = 0;
    s->width = 1920;
    s->height = 1080;
    s->mode_valid = 1;
    // Fill mode info with test data
    s->mode.clock = 148500;
    s->mode.hdisplay = 1920;
    s->mode.hsync_start = 2008;
    s->mode.hsync_end = 2052;
    s->mode.htotal = 2200;
    s->mode.hskew = 0;
    s->mode.vdisplay = 1080;
    s->mode.vsync_start = 1084;
    s->mode.vsync_end = 1089;
    s->mode.vtotal = 1125;
    s->mode.vscan = 0;
    s->mode.vrefresh = 60;
    s->mode.flags = 0x5; // DRM_MODE_FLAG_NHSYNC | DRM_MODE_FLAG_NVSYNC
    s->mode.type = 0x40; // DRM_MODE_TYPE_PREFERRED
    strcpy(s->mode.name, "1920x1080");
    s->gamma_size = 256;
}

// Function to get drmModeCrtc structure size for verification
int get_native_drm_mode_crtc_size(void) {
    return sizeof(drmModeCrtc);
}

// Function to fill drmModeModeInfo structure with test data
void fill_native_drm_mode_mode_info(drmModeModeInfo* s) {
    if (!s) return;

    s->clock = 148500;
    s->hdisplay = 1920;
    s->hsync_start = 2008;
    s->hsync_end = 2052;
    s->htotal = 2200;
    s->hskew = 0;
    s->vdisplay = 1080;
    s->vsync_start = 1084;
    s->vsync_end = 1089;
    s->vtotal = 1125;
    s->vscan = 0;
    s->vrefresh = 60;
    s->flags = 0xA; // DRM_MODE_FLAG_NHSYNC | DRM_MODE_FLAG_NVSYNC
    s->type = 0x8; // DRM_MODE_TYPE_PREFERRED
    strcpy(s->name, "1920x1080");
}

// Function to get drmModeModeInfo structure size for verification
int get_off_t_size() {
    return sizeof(__off_t);
}

int get_native_drm_mode_mode_info_size(void) {
    return sizeof(drmModeModeInfo);
}

// Function to fill drmModePlane structure with test data
void fill_native_drm_mode_plane(drmModePlane* s) {
    if (!s) return;

    s->count_formats = 0;
    s->formats = NULL;
    s->plane_id = 600;
    s->crtc_id = 400;
    s->fb_id = 500;
    s->crtc_x = 0;
    s->crtc_y = 0;
    s->x = 0;
    s->y = 0;
    s->possible_crtcs = 0x07; // Can be used with CRTCs 0, 1, 2
    s->gamma_size = 256;
}

// Function to get drmModePlane structure size for verification
int get_native_drm_mode_plane_size(void) {
    return sizeof(drmModePlane);
}

// Function to fill drmModeFB structure with test data
void fill_native_drm_mode_fb(drmModeFB* s) {
    if (!s) return;

    s->fb_id = 700;
    s->width = 1920;
    s->height = 1080;
    s->pitch = 7680; // 1920 * 4 bytes per pixel
    s->bpp = 32;
    s->depth = 24;
    s->handle = 800;
}

// Function to get drmModeFB structure size for verification
int get_native_drm_mode_fb_size(void) {
    return sizeof(drmModeFB);
}

// Function to fill dma_heap_allocation_data structure with test data
void fill_native_dma_heap_allocation_data(struct dma_heap_allocation_data* s) {
    if (!s) return;

    s->len = 4096; // 4KB allocation
    s->fd = 42; // Test file descriptor
    s->fd_flags = 0x80002; // O_RDWR | O_CLOEXEC
    s->heap_flags = 0; // No heap flags defined yet
}

// Function to get dma_heap_allocation_data structure size for verification
int get_native_dma_heap_allocation_data_size(void) {
    return sizeof(struct dma_heap_allocation_data);
}

// Function to get the real DMA_HEAP_IOCTL_ALLOC constant value from Linux headers
uint32_t get_native_dma_heap_ioctl_alloc(void) {
    return DMA_HEAP_IOCTL_ALLOC;
}

// V4L2 structure testing functions

// Function to fill v4l2_capability structure with test data
void fill_native_v4l2_capability(struct v4l2_capability* s) {
    if (!s) return;

    strcpy((char*)s->driver, "test_driver");
    strcpy((char*)s->card, "Test Video Device");
    strcpy((char*)s->bus_info, "platform:test-video");
    s->version = 0x050C00; // Kernel version 5.12.0
    s->capabilities = V4L2_CAP_VIDEO_CAPTURE | V4L2_CAP_VIDEO_CAPTURE_MPLANE | V4L2_CAP_STREAMING;
    s->device_caps = V4L2_CAP_VIDEO_CAPTURE_MPLANE | V4L2_CAP_STREAMING;
    s->reserved[0] = 0;
    s->reserved[1] = 0;
    s->reserved[2] = 0;
}

// Function to get v4l2_capability structure size for verification
int get_native_v4l2_capability_size(void) {
    return sizeof(struct v4l2_capability);
}

// Function to fill v4l2_pix_format_mplane structure with test data
void fill_native_v4l2_pix_format_mplane(struct v4l2_pix_format_mplane* s) {
    if (!s) return;

    s->width = 1920;
    s->height = 1080;
    s->pixelformat = V4L2_PIX_FMT_NV12M;
    s->field = V4L2_FIELD_NONE;
    s->colorspace = V4L2_COLORSPACE_REC709;
    s->num_planes = 2;
    s->flags = 0;
    s->ycbcr_enc = V4L2_YCBCR_ENC_DEFAULT;
    s->quantization = V4L2_QUANTIZATION_DEFAULT;
    s->xfer_func = V4L2_XFER_FUNC_DEFAULT;

    // Fill plane format info
    s->plane_fmt[0].sizeimage = 1920 * 1080;
    s->plane_fmt[0].bytesperline = 1920;
    s->plane_fmt[1].sizeimage = 1920 * 1080 / 2;
    s->plane_fmt[1].bytesperline = 1920;

    // Reserved fields
    for (int i = 0; i < 7; i++) {
        s->reserved[i] = 0;
    }
}

// Function to get v4l2_pix_format_mplane structure size for verification
int get_native_v4l2_pix_format_mplane_size(void) {
    return sizeof(struct v4l2_pix_format_mplane);
}

// Function to fill v4l2_format structure with test data
void fill_native_v4l2_format(struct v4l2_format* s) {
    if (!s) return;

    s->type = V4L2_BUF_TYPE_VIDEO_CAPTURE_MPLANE;
    fill_native_v4l2_pix_format_mplane(&s->fmt.pix_mp);
}

// Function to get v4l2_format structure size for verification
int get_native_v4l2_format_size(void) {
    return sizeof(struct v4l2_format);
}

// Function to fill v4l2_requestbuffers structure with test data
void fill_native_v4l2_requestbuffers(struct v4l2_requestbuffers* s) {
    if (!s) return;

    s->count = 4;
    s->type = V4L2_BUF_TYPE_VIDEO_CAPTURE_MPLANE;
    s->memory = V4L2_MEMORY_DMABUF;
    s->capabilities = 0; // Don't use V4L2_BUF_CAP_SUPPORTS_DMABUF as it might not be available
    s->flags = 0;
    s->reserved[0] = 0;
    s->reserved[1] = 0;
    s->reserved[2] = 0;
}

// Function to get v4l2_requestbuffers structure size for verification
int get_native_v4l2_requestbuffers_size(void) {
    return sizeof(struct v4l2_requestbuffers);
}

// Function to fill v4l2_buffer structure with test data
void fill_native_v4l2_buffer(struct v4l2_buffer* s) {
    if (!s) return;

    s->index = 0;
    s->type = V4L2_BUF_TYPE_VIDEO_CAPTURE_MPLANE;
    s->bytesused = 0; // Not used for multiplanar
    s->flags = V4L2_BUF_FLAG_MAPPED | V4L2_BUF_FLAG_TIMESTAMP_MONOTONIC;
    s->field = V4L2_FIELD_NONE;
    s->timestamp.tv_sec = 12345;
    s->timestamp.tv_usec = 67890;
    s->timecode.type = 0;
    s->timecode.flags = 0;
    s->timecode.frames = 0;
    s->timecode.seconds = 0;
    s->timecode.minutes = 0;
    s->timecode.hours = 0;
    s->timecode.userbits[0] = 0;
    s->timecode.userbits[1] = 0;
    s->timecode.userbits[2] = 0;
    s->timecode.userbits[3] = 0;
    s->sequence = 123;
    s->memory = V4L2_MEMORY_DMABUF;
    s->length = 2; // Number of planes
    s->reserved2 = 0;
    s->request_fd = -1;

    // Note: planes pointer would be set separately in real usage
    s->m.planes = NULL;
}

// Function to get v4l2_buffer structure size for verification
int get_native_v4l2_buffer_size(void) {
    return sizeof(struct v4l2_buffer);
}

// Function to fill v4l2_exportbuffer structure with test data
void fill_native_v4l2_exportbuffer(struct v4l2_exportbuffer* s) {
    if (!s) return;

    s->type = V4L2_BUF_TYPE_VIDEO_CAPTURE_MPLANE;
    s->index = 0;
    s->plane = 0;
    s->flags = O_RDWR | O_CLOEXEC;
    s->fd = 42; // Test file descriptor

    for (int i = 0; i < 11; i++) {
        s->reserved[i] = 0;
    }
}

// Function to get v4l2_exportbuffer structure size for verification
int get_native_v4l2_exportbuffer_size(void) {
    return sizeof(struct v4l2_exportbuffer);
}

// Function to fill v4l2_decoder_cmd structure with test data
void fill_native_v4l2_decoder_cmd(struct v4l2_decoder_cmd* s) {
    if (!s) return;

    s->cmd = V4L2_DEC_CMD_START;
    s->flags = 0;

    // Initialize the union data
    memset(&s->start, 0, sizeof(s->start));
}

// Function to get v4l2_decoder_cmd structure size for verification
int get_native_v4l2_decoder_cmd_size(void) {
    return sizeof(struct v4l2_decoder_cmd);
}