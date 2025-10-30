# Environment variables for EGL debugging and configuration
# Source this file before running: source egl-env.sh

# Enable EGL debug logging
export EGL_LOG_LEVEL=debug
export MESA_DEBUG=1
export LIBGL_DEBUG=verbose

# Force software rendering (useful for testing/debugging)
# Uncomment if you want to test with software renderer:
# export LIBGL_ALWAYS_SOFTWARE=1
# export GALLIUM_DRIVER=llvmpipe

# Force specific EGL platform (usually auto-detected)
# Uncomment if needed:
# export EGL_PLATFORM=drm          # For direct DRM/KMS
# export EGL_PLATFORM=wayland      # For Wayland
# export EGL_PLATFORM=x11        # For X11

# Mesa driver selection (usually auto-detected)
# Uncomment to override:
# export MESA_LOADER_DRIVER_OVERRIDE=i965  # Intel
# export MESA_LOADER_DRIVER_OVERRIDE=radeonsi # AMD
# export MESA_LOADER_DRIVER_OVERRIDE=nouveau # NVIDIA open-source

# DRM specific
# export DRM_DEBUG=0x1f  # Enable DRM debug output (very verbose!)

echo "EGL environment variables set:"
echo "  EGL_LOG_LEVEL=$EGL_LOG_LEVEL"
echo "MESA_DEBUG=$MESA_DEBUG"
echo "  LIBGL_DEBUG=$LIBGL_DEBUG"
echo ""
echo "To enable software rendering:"
echo "  export LIBGL_ALWAYS_SOFTWARE=1"
echo ""
echo "Now run: dotnet run"
