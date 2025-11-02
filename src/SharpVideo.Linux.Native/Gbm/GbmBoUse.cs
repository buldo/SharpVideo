namespace SharpVideo.Linux.Native.Gbm;

[Flags]
public enum GbmBoUse : uint
{
    GBM_BO_USE_SCANOUT = 1 << 0,
    GBM_BO_USE_CURSOR = 1 << 1,
    GBM_BO_USE_RENDERING = 1 << 2,
    GBM_BO_USE_WRITE = 1 << 3,
    GBM_BO_USE_LINEAR = 1 << 4
}