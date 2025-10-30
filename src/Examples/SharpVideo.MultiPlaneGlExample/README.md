# Multi-Plane OpenGL ES Compositing Demo

## ?? Quick Start (Remote Development)

**Getting "EGL_NOT_INITIALIZED" error?** ? See [QUICKSTART.md](QUICKSTART.md)

```bash
# On your remote Linux machine:
cd Examples/SharpVideo.MultiPlaneGlExample
chmod +x fix-egl.sh
./fix-egl.sh  # Diagnoses and guides you through fixes

# After fixing (usually just need to add user to groups):
dotnet run
```

**Common issue:** User not in `video`/`render` groups
```bash
sudo usermod -a -G video,render $USER
# Then logout/login (or disconnect/reconnect VS Code Remote)
```

---

## Overview
