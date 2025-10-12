using SharpVideo.Linux.Native;

namespace SharpVideo.Drm;

public class DrmCapabilitiesState
{
    public bool DumbBuffer { get; init; }
    public bool VblankHighCrtc { get; init; }
    public UInt64 DumbPreferredDepth { get; init; }
    public bool DumbPreferShadow { get; init; }
    public DrmPrimeCap Prime { get; init; }
    public bool TimestampMonotonic { get; init; }
    public bool AsyncPageFlip { get; init; }
    public UInt64 CursorWidth { get; init; }
    public UInt64 CursorHeight { get; init; }
    public bool AddFB2Modifiers { get; init; }
    public bool PageFlipTarget { get; init; }
    public bool CrtcInVblankEvent { get; init; }
    public bool SyncObj { get; init; }
    public bool SyncObjTimeline { get; init; }
    public bool AtomicAsyncPageFlip { get; init; }
}