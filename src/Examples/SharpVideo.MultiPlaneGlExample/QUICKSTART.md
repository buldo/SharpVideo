# Quick Start Guide - Remote Development

## If You Get "EGL_NOT_INITIALIZED" Error

### Quick Fix (run on remote Linux machine):
```bash
cd Examples/SharpVideo.MultiPlaneGlExample

# Make scripts executable
chmod +x fix-egl.sh check-egl.sh

# Run the fix script
./fix-egl.sh
```

This will diagnose and guide you through fixing common EGL issues.

### Most Common Issue: User Groups
If the fix script says you need to add groups:
```bash
# Add your user to required groups
sudo usermod -a -G video,render $USER

# IMPORTANT: You MUST logout and login again!
# In VS Code: disconnect from remote, then reconnect
```

### Second Most Common: Missing Packages
```bash
# Ubuntu/Debian
sudo apt install -y libgl1-mesa-dev libgles2-mesa-dev libegl1-mesa-dev

# Then try running again
dotnet run
```

## Running the Application

### Normal Run:
```bash
cd Examples/SharpVideo.MultiPlaneGlExample
dotnet run
```

### With Debug Logging:
```bash
export EGL_LOG_LEVEL=debug
dotnet run
```

### Software Rendering Fallback (if GPU issues):
```bash
export LIBGL_ALWAYS_SOFTWARE=1
dotnet run
```

## What This Demo Does

- ? Renders a rotating triangle using **hardware-accelerated OpenGL ES**
- ?? Uses **DMA-BUF** for **zero-copy** GPU?Display rendering
- ???  Composites two planes: OpenGL graphics + NV12 video
- ?? Direct DRM/KMS rendering (no X11/Wayland needed)

## Troubleshooting

See [TROUBLESHOOTING.md](TROUBLESHOOTING.md) for detailed diagnostics.

Quick commands:
```bash
# Check EGL status
./check-egl.sh

# Verify DRM access
ls -la /dev/dri/
groups

# Test if EGL works at all
eglinfo  # requires: mesa-utils package
```

## Expected Output

When working correctly:
```
=== Multi-Plane OpenGL ES Compositing Demo ===
? EGL initialized: version 1.5
? Got display using GBM platform
GL Vendor: Intel/Mesa
GL Renderer: Mesa DRI Intel(R) HD Graphics
GL Version: OpenGL ES 3.2 Mesa
? EGL DMA-BUF extensions loaded successfully
? Shader program created and linked
? Triangle geometry created
? OpenGL ES renderer initialized successfully!

Starting frame presentation (300 frames)...
Frame 0: GPU rendered -> DMA-BUF -> Display scanout (zero-copy!)
...
```

## Still Having Issues?

1. Run diagnostic: `./check-egl.sh`
2. Check troubleshooting guide: [TROUBLESHOOTING.md](TROUBLESHOOTING.md)
3. Open an issue with the diagnostic output

## Architecture

```
Your VS Code (Windows/Mac/Linux)
    ?
    ? SSH/Remote
    ?
Remote Linux Machine
    ?
?? Your App (C# + Silk.NET)
    ?    ?
    ?   ?? EGL Context (libEGL.so)
    ?      ?? OpenGL ES (libGLESv2.so)
    ?        ?
    ?             ?
  ?? GPU Driver (Mesa/Proprietary)
    ?      ?
    ?      ?
    ?? DMA-BUF (shared memory)
    ?      ?
    ?      ?
    ?? DRM/KMS (direct display output)
    ?
           ?
       Monitor ???
```

No X11 or Wayland needed! Direct GPU?Display rendering.
