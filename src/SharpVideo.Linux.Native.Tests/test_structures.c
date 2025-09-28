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

    s->count_fbs = 0xDEAD;        // Distinctive pattern for count_fbs
    s->fbs = NULL; // We'll handle pointer arrays separately in tests
    s->count_crtcs = 0xBEEF;      // Distinctive pattern for count_crtcs
    s->crtcs = NULL;
    s->count_connectors = 0xCAFE; // Distinctive pattern for count_connectors
    s->connectors = NULL;
    s->count_encoders = 0xBABE;   // Distinctive pattern for count_encoders
    s->encoders = NULL;
    s->min_width = 0x12345678;    // Distinctive pattern for min_width
    s->max_width = 0x87654321;    // Distinctive pattern for max_width
    s->min_height = 0xFEDCBA98;   // Distinctive pattern for min_height
    s->max_height = 0x89ABCDEF;   // Distinctive pattern for max_height
}

// Function to get drmModeRes structure size for verification
int get_native_drm_mode_res_size(void) {
    return sizeof(drmModeRes);
}

// Function to fill drmModeEncoder structure with test data
void fill_native_drm_mode_encoder(drmModeEncoder* s) {
    if (!s) return;

    s->encoder_id = 0xDEADBEEF;       // Distinctive pattern for encoder_id
    s->encoder_type = 0xCAFE;         // Distinctive pattern for encoder_type
    s->crtc_id = 0xBABEFACE;          // Distinctive pattern for crtc_id
    s->possible_crtcs = 0x12345678;   // Distinctive bitmask pattern
    s->possible_clones = 0x87654321;  // Distinctive bitmask pattern
}

// Function to get drmModeEncoder structure size for verification
int get_native_drm_mode_encoder_size(void) {
    return sizeof(drmModeEncoder);
}

// Function to fill drmModeConnector structure with test data
void fill_native_drm_mode_connector(drmModeConnector* s) {
    if (!s) return;

    s->connector_id = 0xFEEDFACE;     // Distinctive pattern for connector_id
    s->encoder_id = 0xDEADBEEF;       // Distinctive pattern for encoder_id
    s->connector_type = 0xCAFEBABE;   // Distinctive pattern for connector_type
    s->connector_type_id = 0x12345;   // Distinctive pattern for connector_type_id
    s->connection = 0xABCD;           // Distinctive pattern for connection
    s->mmWidth = 0x11223344;          // Distinctive pattern for mmWidth
    s->mmHeight = 0x55667788;         // Distinctive pattern for mmHeight
    s->subpixel = 0x9900AABB;         // Distinctive pattern for subpixel
    s->count_modes = 0xCCDDEEFF;      // Distinctive pattern for count_modes
    s->modes = NULL;
    s->count_props = 0x13579BDF;      // Distinctive pattern for count_props
    s->props = NULL;
    s->prop_values = NULL;
    s->count_encoders = 0x2468ACE0;   // Distinctive pattern for count_encoders
    s->encoders = NULL;
}

// Function to get drmModeConnector structure size for verification
int get_native_drm_mode_connector_size(void) {
    return sizeof(drmModeConnector);
}

// Function to fill drmModeCrtc structure with test data
void fill_native_drm_mode_crtc(drmModeCrtc* s) {
    if (!s) return;

    s->crtc_id = 0xDEADBEEF;      // Distinctive pattern for crtc_id
    s->buffer_id = 0xCAFEBABE;    // Distinctive pattern for buffer_id
    s->x = 0x12345678;            // Distinctive pattern for x
    s->y = 0x87654321;            // Distinctive pattern for y
    s->width = 0xFEDCBA98;        // Distinctive pattern for width
    s->height = 0x89ABCDEF;       // Distinctive pattern for height
    s->mode_valid = 0xABCDEF01;   // Distinctive pattern for mode_valid
    // Fill mode info with test data
    s->mode.clock = 0x12345678;         // Distinctive pattern for clock
    s->mode.hdisplay = 0x1111;          // Distinctive pattern for hdisplay
    s->mode.hsync_start = 0x2222;       // Distinctive pattern for hsync_start
    s->mode.hsync_end = 0x3333;         // Distinctive pattern for hsync_end
    s->mode.htotal = 0x4444;            // Distinctive pattern for htotal
    s->mode.hskew = 0x5555;             // Distinctive pattern for hskew
    s->mode.vdisplay = 0x6666;          // Distinctive pattern for vdisplay
    s->mode.vsync_start = 0x7777;       // Distinctive pattern for vsync_start
    s->mode.vsync_end = 0x8888;         // Distinctive pattern for vsync_end
    s->mode.vtotal = 0x9999;            // Distinctive pattern for vtotal
    s->mode.vscan = 0xAAAA;             // Distinctive pattern for vscan
    s->mode.vrefresh = 0xBBBB;          // Distinctive pattern for vrefresh
    s->mode.flags = 0xCCCCCCCC;         // Distinctive flag pattern
    s->mode.type = 0xDDDDDDDD;          // Distinctive type pattern
    strcpy(s->mode.name, "TEST_MODE_PATTERN_12345"); // Distinctive test string
    s->gamma_size = 0xEEEEEEEE;         // Distinctive pattern for gamma_size
}

// Function to get drmModeCrtc structure size for verification
int get_native_drm_mode_crtc_size(void) {
    return sizeof(drmModeCrtc);
}

// Function to fill drmModeModeInfo structure with test data
void fill_native_drm_mode_mode_info(drmModeModeInfo* s) {
    if (!s) return;

    s->clock = 0x11111111;         // Distinctive pattern for clock
    s->hdisplay = 0x2222;          // Distinctive pattern for hdisplay
    s->hsync_start = 0x3333;       // Distinctive pattern for hsync_start
    s->hsync_end = 0x4444;         // Distinctive pattern for hsync_end
    s->htotal = 0x5555;            // Distinctive pattern for htotal
    s->hskew = 0x6666;             // Distinctive pattern for hskew
    s->vdisplay = 0x7777;          // Distinctive pattern for vdisplay
    s->vsync_start = 0x8888;       // Distinctive pattern for vsync_start
    s->vsync_end = 0x9999;         // Distinctive pattern for vsync_end
    s->vtotal = 0xAAAA;            // Distinctive pattern for vtotal
    s->vscan = 0xBBBB;             // Distinctive pattern for vscan
    s->vrefresh = 0xCCCC;          // Distinctive pattern for vrefresh
    s->flags = 0xDDDDDDDD;         // Distinctive flag pattern
    s->type = 0xEEEEEEEE;          // Distinctive type pattern
    strcpy(s->name, "TEST_MODE_INFO_ABCDEF");  // Distinctive test string
}

// Function to get off_t size
int get_off_t_size() {
    return sizeof(__off_t);
}

int get_native_drm_mode_mode_info_size(void) {
    return sizeof(drmModeModeInfo);
}

// Function to fill drmModePlane structure with test data
void fill_native_drm_mode_plane(drmModePlane* s) {
    if (!s) return;

    s->count_formats = 0xDEADC0DE;     // Distinctive pattern for count_formats
    s->formats = NULL;
    s->plane_id = 0xFEEDBEEF;          // Distinctive pattern for plane_id
    s->crtc_id = 0xCAFED00D;           // Distinctive pattern for crtc_id
    s->fb_id = 0xBADDCAFE;             // Distinctive pattern for fb_id
    s->crtc_x = 0x12121212;            // Distinctive pattern for crtc_x
    s->crtc_y = 0x34343434;            // Distinctive pattern for crtc_y
    s->x = 0x56565656;                 // Distinctive pattern for x
    s->y = 0x78787878;                 // Distinctive pattern for y
    s->possible_crtcs = 0x9ABCDEF0;    // Distinctive bitmask pattern
    s->gamma_size = 0x13579BDF;        // Distinctive pattern for gamma_size
}

// Function to get drmModePlane structure size for verification
int get_native_drm_mode_plane_size(void) {
    return sizeof(drmModePlane);
}

// Function to fill drmModeFB structure with test data
void fill_native_drm_mode_fb(drmModeFB* s) {
    if (!s) return;

    s->fb_id = 0xFACADE00;        // Distinctive pattern for fb_id
    s->width = 0xDEADBEEF;        // Distinctive pattern for width
    s->height = 0xCAFEBABE;       // Distinctive pattern for height
    s->pitch = 0x12345678;        // Distinctive pattern for pitch
    s->bpp = 0x87654321;          // Distinctive pattern for bpp
    s->depth = 0xFEDCBA98;        // Distinctive pattern for depth
    s->handle = 0x89ABCDEF;       // Distinctive pattern for handle
}

// Function to get drmModeFB structure size for verification
int get_native_drm_mode_fb_size(void) {
    return sizeof(drmModeFB);
}

// Function to fill dma_heap_allocation_data structure with test data
void fill_native_dma_heap_allocation_data(struct dma_heap_allocation_data* s) {
    if (!s) return;

    s->len = 0xDEADBEEFCAFEBABE;  // Distinctive 64-bit pattern for len
    s->fd = 0x12345678;           // Distinctive 32-bit pattern for fd
    s->fd_flags = 0x87654321;     // Distinctive 32-bit pattern for fd_flags
    s->heap_flags = 0xFEDCBA98;   // Distinctive 64-bit pattern for heap_flags
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

    strcpy((char*)s->driver, "TEST_DRV_DEAD");        // Distinctive test driver name (fits in 16 chars)
    strcpy((char*)s->card, "TEST_CARD_CAFE");         // Distinctive test card name (fits in 32 chars)
    strcpy((char*)s->bus_info, "TEST_BUS_12345");     // Distinctive test bus info (fits in 32 chars)
    s->version = 0xDEADBEEF;           // Distinctive pattern for version
    s->capabilities = 0xCAFEBABE;      // Distinctive pattern for capabilities
    s->device_caps = 0x12345678;       // Distinctive pattern for device_caps
    s->reserved[0] = 0x87654321;       // Distinctive pattern for reserved[0]
    s->reserved[1] = 0xFEDCBA98;       // Distinctive pattern for reserved[1]
    s->reserved[2] = 0x13579BDF;       // Distinctive pattern for reserved[2]
}

// Function to get v4l2_capability structure size for verification
int get_native_v4l2_capability_size(void) {
    return sizeof(struct v4l2_capability);
}

// Function to fill v4l2_pix_format_mplane structure with test data
void fill_native_v4l2_pix_format_mplane(struct v4l2_pix_format_mplane* s) {
    if (!s) return;

    s->width = 0xDEADBEEF;              // Distinctive pattern for width
    s->height = 0xCAFEBABE;             // Distinctive pattern for height
    s->pixelformat = 0x12345678;        // Distinctive pattern for pixelformat
    s->field = 0x87654321;              // Distinctive pattern for field
    s->colorspace = 0xFEDCBA98;         // Distinctive pattern for colorspace
    s->num_planes = 0xAB;               // Distinctive pattern for num_planes (byte)
    s->flags = 0xCD;                    // Distinctive pattern for flags (byte)
    s->ycbcr_enc = 0xEF;                // Distinctive pattern for ycbcr_enc (byte)
    s->quantization = 0x12;             // Distinctive pattern for quantization (byte)
    s->xfer_func = 0x34;                // Distinctive pattern for xfer_func (byte)

    // Fill plane format info with distinctive patterns
    s->plane_fmt[0].sizeimage = 0xDEADC0DE;     // Distinctive pattern for plane 0 sizeimage
    s->plane_fmt[0].bytesperline = 0xFEEDFACE;  // Distinctive pattern for plane 0 bytesperline
    s->plane_fmt[1].sizeimage = 0xBADDCAFE;     // Distinctive pattern for plane 1 sizeimage
    s->plane_fmt[1].bytesperline = 0x13579BDF;  // Distinctive pattern for plane 1 bytesperline

    // Fill remaining plane formats with patterns
    for (int i = 2; i < 8; i++) {
        s->plane_fmt[i].sizeimage = 0x11111111 + i;     // Sequential pattern
        s->plane_fmt[i].bytesperline = 0x22222222 + i;  // Sequential pattern
    }

    // Reserved fields with distinctive patterns
    for (int i = 0; i < 7; i++) {
        s->reserved[i] = 0x99 + i;    // Sequential distinctive pattern (byte values)
    }
}

// Function to get v4l2_pix_format_mplane structure size for verification
int get_native_v4l2_pix_format_mplane_size(void) {
    return sizeof(struct v4l2_pix_format_mplane);
}

// Function to fill v4l2_format structure with test data
void fill_native_v4l2_format(struct v4l2_format* s) {
    if (!s) return;

    s->type = 0xFACADE00;      // Distinctive pattern for type
    fill_native_v4l2_pix_format_mplane(&s->fmt.pix_mp);
}

// Function to get v4l2_format structure size for verification
int get_native_v4l2_format_size(void) {
    return sizeof(struct v4l2_format);
}

// Function to fill v4l2_requestbuffers structure with test data
void fill_native_v4l2_requestbuffers(struct v4l2_requestbuffers* s) {
    if (!s) return;

    s->count = 0xDEADBEEF;        // Distinctive pattern for count
    s->type = 0xCAFEBABE;         // Distinctive pattern for type
    s->memory = 0x12345678;       // Distinctive pattern for memory
    s->capabilities = 0x87654321; // Distinctive pattern for capabilities
    s->flags = 0x98;              // Distinctive byte pattern for flags (matches low byte of 0xFEDCBA98)
    s->reserved[0] = 0x44;        // Distinctive byte pattern for reserved[0]
    s->reserved[1] = 0x88;        // Distinctive byte pattern for reserved[1]
    s->reserved[2] = 0xCC;        // Distinctive byte pattern for reserved[2]
}

// Function to get v4l2_requestbuffers structure size for verification
int get_native_v4l2_requestbuffers_size(void) {
    return sizeof(struct v4l2_requestbuffers);
}

// Function to fill v4l2_buffer structure with test data
void fill_native_v4l2_buffer(struct v4l2_buffer* s) {
    if (!s) return;

    s->index = 0xDEADBEEF;            // Distinctive pattern for index
    s->type = 0xCAFEBABE;             // Distinctive pattern for type
    s->bytesused = 0x12345678;        // Distinctive pattern for bytesused
    s->flags = 0x87654321;            // Distinctive pattern for flags
    s->field = 0xFEDCBA98;            // Distinctive pattern for field
    s->timestamp.tv_sec = 0x11111111; // Distinctive pattern for timestamp seconds
    s->timestamp.tv_usec = 0x22222222; // Distinctive pattern for timestamp microseconds
    s->timecode.type = 0x33;          // Distinctive pattern for timecode type
    s->timecode.flags = 0x44;         // Distinctive pattern for timecode flags
    s->timecode.frames = 0x55;        // Distinctive pattern for timecode frames
    s->timecode.seconds = 0x66;       // Distinctive pattern for timecode seconds
    s->timecode.minutes = 0x77;       // Distinctive pattern for timecode minutes
    s->timecode.hours = 0x88;         // Distinctive pattern for timecode hours
    s->timecode.userbits[0] = 0x99;   // Distinctive pattern for userbits[0]
    s->timecode.userbits[1] = 0xAA;   // Distinctive pattern for userbits[1]
    s->timecode.userbits[2] = 0xBB;   // Distinctive pattern for userbits[2]
    s->timecode.userbits[3] = 0xCC;   // Distinctive pattern for userbits[3]
    s->sequence = 0x33333333;         // Distinctive pattern for sequence
    s->memory = 0x44444444;           // Distinctive pattern for memory
    s->length = 0x55555555;           // Distinctive pattern for length
    s->reserved2 = 0x66666666;        // Distinctive pattern for reserved2
    s->request_fd = 0x77777777;       // Distinctive pattern for request_fd

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

    s->type = 0xDEADBEEF;        // Distinctive pattern for type
    s->index = 0xCAFEBABE;       // Distinctive pattern for index
    s->plane = 0x12345678;       // Distinctive pattern for plane
    s->flags = 0x87654321;       // Distinctive pattern for flags
    s->fd = 0xFEDCBA98;          // Distinctive pattern for fd

    for (int i = 0; i < 11; i++) {
        s->reserved[i] = 0x11223344 + i;  // Sequential distinctive pattern
    }
}

// Function to get v4l2_exportbuffer structure size for verification
int get_native_v4l2_exportbuffer_size(void) {
    return sizeof(struct v4l2_exportbuffer);
}

// Function to fill v4l2_decoder_cmd structure with test data
void fill_native_v4l2_decoder_cmd(struct v4l2_decoder_cmd* s) {
    if (!s) return;

    s->cmd = 0xDEADBEEF;      // Distinctive pattern for cmd
    s->flags = 0xCAFEBABE;    // Distinctive pattern for flags

    // Initialize the union data with a distinctive pattern
    memset(&s->start, 0xAB, sizeof(s->start));  // Fill with 0xAB pattern
}

// Function to get v4l2_decoder_cmd structure size for verification
int get_native_v4l2_decoder_cmd_size(void) {
    return sizeof(struct v4l2_decoder_cmd);
}

// Function to fill v4l2_ext_control structure with test data
void fill_native_v4l2_ext_control(struct v4l2_ext_control* s) {
    if (!s) return;

    s->id = 0xDEADBEEF;          // Distinctive pattern for id
    s->size = 0xCAFEBABE;        // Distinctive pattern for size
    s->reserved2[0] = 0x12345678; // Distinctive pattern for reserved2[0]
    s->ptr = (void*)0x87654321UL; // Distinctive pattern for ptr (cast to avoid warnings)
}

// Function to get v4l2_ext_control structure size for verification
int get_native_v4l2_ext_control_size(void) {
    return sizeof(struct v4l2_ext_control);
}

// Function to fill v4l2_ctrl_h264_sps structure with test data
void fill_native_v4l2_ctrl_h264_sps(struct v4l2_ctrl_h264_sps* s) {
    if (!s) return;

    s->profile_idc = 0xAA;                         // Distinctive pattern for profile_idc
    s->constraint_set_flags = 0x3F;                // Set all defined constraint set bits
    s->level_idc = 0xBB;                           // Distinctive pattern for level_idc
    s->seq_parameter_set_id = 0xCC;                // Distinctive pattern for seq_parameter_set_id
    s->chroma_format_idc = 0x01;                   // Distinctive pattern for chroma_format_idc
    s->bit_depth_luma_minus8 = 0x02;               // Distinctive pattern for bit_depth_luma_minus8
    s->bit_depth_chroma_minus8 = 0x03;             // Distinctive pattern for bit_depth_chroma_minus8
    s->log2_max_frame_num_minus4 = 0x04;           // Distinctive pattern
    s->pic_order_cnt_type = 0x05;                  // Distinctive pattern
    s->log2_max_pic_order_cnt_lsb_minus4 = 0x06;   // Distinctive pattern
    s->max_num_ref_frames = 0x07;                  // Distinctive pattern
    s->num_ref_frames_in_pic_order_cnt_cycle = 0x08; // Distinctive pattern

    // Fill offset_for_ref_frame with a recognizable sequence
    for (int i = 0; i < 255; i++) {
        s->offset_for_ref_frame[i] = 0x1000 + i;
    }

    s->offset_for_non_ref_pic = 0xDEADBEEF;       // Distinctive 32-bit pattern
    s->offset_for_top_to_bottom_field = 0xCAFEBABE; // Distinctive 32-bit pattern
    s->pic_width_in_mbs_minus1 = 0x1234;          // Distinctive 16-bit pattern
    s->pic_height_in_map_units_minus1 = 0x5678;   // Distinctive 16-bit pattern
    s->flags = 0xDEADBEEF;                        // Distinctive 32-bit flags pattern
}

// Function to get v4l2_ctrl_h264_sps structure size for verification
int get_native_v4l2_ctrl_h264_sps_size(void) {
    return sizeof(struct v4l2_ctrl_h264_sps);
}