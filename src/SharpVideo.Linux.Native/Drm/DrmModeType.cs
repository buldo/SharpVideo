namespace SharpVideo.Linux.Native;

[Flags]
public enum DrmModeType : uint
{
    BUILTIN = (1 << 0), /* deprecated */
    CLOCK_C = ((1 << 1) | BUILTIN), /* deprecated */
    CRTC_C = ((1 << 2) | BUILTIN), /* deprecated */
    PREFERRED = (1 << 3),
    DEFAULT = (1 << 4), /* deprecated */
    USERDEF = (1 << 5),
    DRIVER = (1 << 6),
    ALL = (PREFERRED | USERDEF | DRIVER)
}