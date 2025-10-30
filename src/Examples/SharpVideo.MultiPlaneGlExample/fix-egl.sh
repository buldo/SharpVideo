#!/bin/bash
# Quick EGL fix script

echo "=== EGL Quick Fix Script ==="
echo ""

# Check if running as root
if [ "$EUID" -eq 0 ]; then
  echo "??  Don't run this script as root!"
    exit 1
fi

# 1. Check groups
echo "1. Checking user groups..."
if groups | grep -q video && groups | grep -q render; then
    echo "   ? User is in video and render groups"
else
    echo "   ? User not in required groups"
    echo "   Run: sudo usermod -a -G video,render $USER"
    echo "   Then logout and login again"
 NEED_GROUPS=1
fi
echo ""

# 2. Check packages
echo "2. Checking required packages..."
if ldconfig -p | grep -q libEGL.so && ldconfig -p | grep -q libGLESv2.so; then
    echo "   ? EGL libraries found"
else
    echo "   ? EGL libraries missing"
    echo "   Run: sudo apt install libgl1-mesa-dev libgles2-mesa-dev libegl1-mesa-dev"
    NEED_PACKAGES=1
fi
echo ""

# 3. Check DRM devices
echo "3. Checking DRM devices..."
if [ -e /dev/dri/card0 ]; then
    echo "   ? /dev/dri/card0 exists"
    if [ -r /dev/dri/card0 ] && [ -w /dev/dri/card0 ]; then
        echo "   ? Have read/write access"
    else
        echo "   ? No access to /dev/dri/card0"
        NEED_PERMISSIONS=1
    fi
else
    echo "   ? /dev/dri/card0 not found"
    NEED_DRM=1
fi
echo ""

# 4. Test EGL
echo "4. Testing EGL..."
cat > /tmp/test-egl.c << 'EOF'
#include <EGL/egl.h>
#include <stdio.h>
int main() {
    EGLDisplay display = eglGetDisplay(EGL_DEFAULT_DISPLAY);
    if (display == EGL_NO_DISPLAY) {
        printf("Failed to get display\n");
        return 1;
    }
    EGLint major, minor;
    if (!eglInitialize(display, &major, &minor)) {
        printf("Failed to initialize\n");
        return 1;
    }
  printf("EGL %d.%d\n", major, minor);
    eglTerminate(display);
 return 0;
}
EOF

if gcc /tmp/test-egl.c -o /tmp/test-egl -lEGL 2>/dev/null; then
if /tmp/test-egl 2>/dev/null; then
   echo "   ? EGL test passed"
    else
  echo "   ? EGL test failed"
        NEED_FIX=1
    fi
    rm -f /tmp/test-egl
else
    echo "   ??  Can't compile test (gcc not available)"
fi
rm -f /tmp/test-egl.c
echo ""

# Summary
echo "=== Summary ==="
if [ -n "$NEED_GROUPS" ]; then
    echo "? Fix groups:  sudo usermod -a -G video,render $USER"
fi
if [ -n "$NEED_PACKAGES" ]; then
    echo "? Install packages: sudo apt install libgl1-mesa-dev libgles2-mesa-dev libegl1-mesa-dev"
fi
if [ -n "$NEED_PERMISSIONS" ]; then
    echo "? Fix permissions: check user groups and /dev/dri/ permissions"
fi
if [ -n "$NEED_DRM" ]; then
    echo "? No DRM device: check if GPU driver is loaded (lsmod | grep drm)"
fi

if [ -z "$NEED_GROUPS" ] && [ -z "$NEED_PACKAGES" ] && [ -z "$NEED_PERMISSIONS" ] && [ -z "$NEED_DRM" ] && [ -z "$NEED_FIX" ]; then
    echo "? Everything looks good!"
    echo ""
    echo "If you still get EGL errors, try:"
    echo "  export LIBGL_ALWAYS_SOFTWARE=1"
    echo "  dotnet run"
else
    echo ""
    echo "Fix the issues above, then run this script again to verify."
fi
