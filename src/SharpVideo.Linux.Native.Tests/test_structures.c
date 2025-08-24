#include <stdint.h>
#include <string.h>
#include <xf86drmMode.h>

// We use the real drmModeRes structure from libdrm headers

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