# Troubleshooting EGL Initialization Issues

## Problem
`Failed to initialize EGL: EGL_NOT_INITIALIZED` error when running on a remote Linux machine via VS Code.

## Root Cause
When connecting remotely via SSH/VS Code, the `DISPLAY` environment variable may not be set, causing EGL to fail to find a display server. For DRM/KMS direct rendering (which this project uses), we don't need X11/Wayland, but EGL needs proper configuration.

## Solutions

### 1. Quick Check - Run Diagnostic Script
```bash
cd Examples/SharpVideo.MultiPlaneGlExample
chmod +x check-egl.sh
./check-egl.sh
```

This will show:
- Available EGL/OpenGL ES libraries
- DRM devices and permissions
- GPU information
- Current environment variables

### 2. Verify User Permissions
The user must have access to GPU devices:

```bash
# Check current groups
groups

# Add user to video and render groups (if not already)
sudo usermod -a -G video,render $USER

# IMPORTANT: Logout and login again for groups to take effect!
```

### 3. Install Required Packages

#### Ubuntu/Debian:
```bash
sudo apt update
sudo apt install -y \
    libgl1-mesa-dev \
    libgles2-mesa-dev \
    libegl1-mesa-dev \
    mesa-utils
```

#### Fedora/RHEL:
```bash
sudo dnf install -y \
    mesa-libGL-devel \
    mesa-libGLES-devel \
    mesa-libEGL-devel \
    mesa-demos
```

#### Arch Linux:
```bash
sudo pacman -S mesa mesa-demos
```

### 4. Verify EGL Works
```bash
# Test EGL info (after installing mesa-utils)
eglinfo

# Should show EGL version, vendor, extensions, etc.
```

### 5. Check DRM Devices
```bash
# List DRM devices
ls -la /dev/dri/

# Should show something like:
# drwxr-xr-x  3 root root       100 Jan  1 00:00 .
# crw-rw----+ 1 root video  226,   0 Jan  1 00:00 card0
# crw-rw----+ 1 root render 226, 128 Jan  1 00:00 renderD128
```

If you see **permission denied**, make sure your user is in the `video` and `render` groups (step 2).

### 6. Environment Variables for Remote Development

When running remotely via VS Code, you may need to set:

```bash
# In your terminal or .bashrc
export LIBGL_ALWAYS_SOFTWARE=0     # Use hardware rendering (default)
export MESA_LOADER_DRIVER_OVERRIDE=# Leave empty for auto-detection

# Or for software rendering (slower but works everywhere):
export LIBGL_ALWAYS_SOFTWARE=1
export GALLIUM_DRIVER=llvmpipe
```

### 7. VS Code Remote Development Settings

In your VS Code settings (`.vscode/settings.json`):
```json
{
    "terminal.integrated.inheritEnv": true,
    "remote.SSH.enableAgentForwarding": false
}
```

### 8. Run with Debug Logging

Set environment variables before running:
```bash
# Enable EGL debug output
export EGL_LOG_LEVEL=debug
export MESA_DEBUG=1

# Run the application
dotnet run --project Examples/SharpVideo.MultiPlaneGlExample
```

### 9. Test Minimal EGL Program

Create a test C program to verify EGL works:
```c
// test-egl.c
#include <EGL/egl.h>
#include <stdio.h>

int main() {
    EGLDisplay display = eglGetDisplay(EGL_DEFAULT_DISPLAY);
    if (display == EGL_NO_DISPLAY) {
        printf("? Failed to get EGL display\n");
return 1;
    }
    
    EGLint major, minor;
    if (!eglInitialize(display, &major, &minor)) {
      printf("? Failed to initialize EGL\n");
        return 1;
    }
    
    printf("? EGL initialized: %d.%d\n", major, minor);
    eglTerminate(display);
    return 0;
}
```

Compile and run:
```bash
gcc test-egl.c -o test-egl -lEGL
./test-egl
```

If this fails, the problem is with your EGL installation, not the C# application.

## Common Issues and Fixes

### Issue: "No EGL libraries found"
**Fix:** Install Mesa development packages (see step 3)

### Issue: "Permission denied" on /dev/dri/card0
**Fix:** Add user to `video` and `render` groups, then logout/login

### Issue: "EGL_NOT_INITIALIZED" even after installing packages
**Fix:** 
1. Check if GPU driver is loaded: `lsmod | grep -i "i915\|nouveau\|amdgpu\|radeon"`
2. For Intel: `sudo modprobe i915`
3. For AMD: `sudo modprobe amdgpu`
4. For NVIDIA: Install proprietary drivers

### Issue: Running in Docker/Container
**Fix:** Need to pass GPU devices:
```bash
docker run --device=/dev/dri:/dev/dri \
           --group-add video \
       --group-add render \
           your-container
```

### Issue: Running via systemd service
**Fix:** Add to service file:
```ini
[Service]
SupplementaryGroups=video render
DeviceAllow=/dev/dri/card0 rw
DeviceAllow=/dev/dri/renderD128 rw
```

## What the Code Does Now

The updated `GlRenderer` tries multiple strategies to get an EGL display:

1. **EGL_DEFAULT_DISPLAY** - Standard method (requires DISPLAY variable)
2. **eglGetDisplay(NULL)** - Explicit NULL display
3. **eglGetPlatformDisplayEXT with GBM** - Direct DRM/KMS rendering (best for your case)
4. **eglGetPlatformDisplayEXT with DEVICE** - Device platform

The code will automatically try all methods and use the first one that works.

## Verification

After applying fixes, run:
```bash
cd Examples/SharpVideo.MultiPlaneGlExample
dotnet run
```

You should see:
```
? EGL initialized: version 1.5
? Got display using GBM platform
GL Vendor: Intel/AMD/NVIDIA
GL Renderer: Mesa DRI Intel(R) HD Graphics...
```

If you still get errors, run the diagnostic script and share the output!
