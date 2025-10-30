#!/bin/bash
# EGL Diagnostic Script

echo "=== EGL/OpenGL ES Environment Check ==="
echo ""

echo "1. Checking EGL libraries:"
ldconfig -p | grep -E "(libEGL|libGLES)" || echo "  ? No EGL/GLES libraries found"
echo ""

echo "2. Checking DRM devices:"
ls -la /dev/dri/ 2>/dev/null || echo "  ? No /dev/dri/ directory"
echo ""

echo "3. Current user and groups:"
echo "  User: $(whoami)"
echo "  Groups: $(groups)"
echo ""

echo "4. Checking GPU info:"
if command -v lspci &> /dev/null; then
    lspci | grep -i "VGA\|3D\|Display" || echo "  ? No GPU found"
else
    echo "  ??  lspci not available"
fi
echo ""

echo "5. Environment variables:"
echo "  DISPLAY=${DISPLAY:-<not set>}"
echo "  WAYLAND_DISPLAY=${WAYLAND_DISPLAY:-<not set>}"
echo "  XDG_SESSION_TYPE=${XDG_SESSION_TYPE:-<not set>}"
echo ""

echo "6. Testing EGL (if eglinfo available):"
if command -v eglinfo &> /dev/null; then
    eglinfo 2>&1 | head -20
else
    echo "  ??  eglinfo not installed (install: mesa-utils)"
fi
echo ""

echo "7. Mesa/DRI info:"
if [ -f /usr/lib/dri/swrast_dri.so ] || [ -f /usr/lib/x86_64-linux-gnu/dri/swrast_dri.so ]; then
    echo "  ? Mesa software renderer available"
fi
if ls /usr/lib/dri/*_dri.so 2>/dev/null | grep -v swrast; then
    echo "  ? Hardware DRI drivers found:"
ls /usr/lib/dri/*_dri.so | grep -v swrast | xargs -n1 basename
fi
echo ""

echo "8. Checking permissions for DRM:"
for dev in /dev/dri/card*; do
    if [ -e "$dev" ]; then
        echo "  $dev: $(stat -c '%A %U:%G' $dev)"
    fi
done
echo ""

echo "=== Recommendations ==="
echo "If EGL fails to initialize:"
echo "  1. Add user to 'video' and 'render' groups:"
echo "     sudo usermod -a -G video,render $(whoami)"
echo "     (then logout and login again)"
echo ""
echo "  2. Install Mesa and EGL development packages:"
echo "     # Ubuntu/Debian:"
echo "     sudo apt install libgl1-mesa-dev libgles2-mesa-dev libegl1-mesa-dev"
echo ""
echo "  3. For headless/DRM rendering, try setting:"
echo "   export LIBGL_ALWAYS_SOFTWARE=1  # Use software rendering"
echo "     export EGL_PLATFORM=drm         # Force DRM platform"
echo ""
