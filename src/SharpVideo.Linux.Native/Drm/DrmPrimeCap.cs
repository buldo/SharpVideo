namespace SharpVideo.Linux.Native;

[Flags]
public enum DrmPrimeCap
{
    /// <summary>
    /// If this bit is set in &DRM_CAP_PRIME, the driver supports importing PRIME buffers via the &DRM_IOCTL_PRIME_FD_TO_HANDLE ioctl.
    ///
    /// Starting from kernel version 6.6, this bit is always set in &DRM_CAP_PRIME.
    /// </summary>
    DRM_PRIME_CAP_IMPORT = 0x1,

    ///<summary>
    /// If this bit is set in &DRM_CAP_PRIME, the driver supports exporting PRIME buffers via the &DRM_IOCTL_PRIME_HANDLE_TO_FD ioctl.
    ///
    /// Starting from kernel version 6.6, this bit is always set in &DRM_CAP_PRIME.
    /// </summary>
    DRM_PRIME_CAP_EXPORT = 0x2
}