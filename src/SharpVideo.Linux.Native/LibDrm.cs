using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace SharpVideo.Linux.Native;

[SupportedOSPlatform("linux")]
public static partial class LibDrm
{
    private const string Drm = "drm";

    [DllImport(Drm, SetLastError = true)]
    public static extern IntPtr drmModeGetResources(int fd);

    [DllImport(Drm)]
    public static extern void drmModeFreeResources(IntPtr ptr);

    [StructLayout(LayoutKind.Sequential)]
    public struct DrmModeRes
    {
        public int count_fbs;
        public IntPtr fbs;

        public int count_crtcs;
        public IntPtr crtcs;

        public int count_connectors;
        public IntPtr connectors;

        public int count_encoders;
        public IntPtr encoders;

        public uint min_width, max_width;
        public uint min_height, max_height;
    }
}