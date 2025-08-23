using System.Runtime.InteropServices;

namespace SharpVideo.Linux.Native;

/// <summary>
/// Managed representation of the native <c>drmModeRes</c> structure.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe readonly struct DrmModeRes
{
    /* Primary object counts */
    public readonly int CountFbs;
    private readonly nint _fbs;                /* uint32_t* */

    public readonly int CountCrtcs;
    private readonly nint _crtcs;              /* uint32_t* */

    public readonly int CountConnectors;
    private readonly nint _connectors;         /* uint32_t* */

    public readonly int CountEncoders;
    private readonly nint _encoders;           /* uint32_t* */

    /* Screen limits */
    public readonly int MinWidth;
    public readonly int MaxWidth;
    public readonly int MinHeight;
    public readonly int MaxHeight;

    /* -----------------------------------------------------------------
     *  Helpers for managed access to the id arrays
     *  NOTE: The spans are valid only as long as the underlying
     *  native structure is pinned and has not been freed with
     *  drmModeFreeResources.
     * ----------------------------------------------------------------*/
    public ReadOnlySpan<uint> FramebufferIds => _fbs == 0
        ? ReadOnlySpan<uint>.Empty
        : new ReadOnlySpan<uint>((void*)_fbs, CountFbs);

    public ReadOnlySpan<uint> CrtcIds => _crtcs == 0
        ? ReadOnlySpan<uint>.Empty
        : new ReadOnlySpan<uint>((void*)_crtcs, CountCrtcs);

    public ReadOnlySpan<uint> ConnectorIds => _connectors == 0
        ? ReadOnlySpan<uint>.Empty
        : new ReadOnlySpan<uint>((void*)_connectors, CountConnectors);

    public ReadOnlySpan<uint> EncoderIds => _encoders == 0
        ? ReadOnlySpan<uint>.Empty
        : new ReadOnlySpan<uint>((void*)_encoders, CountEncoders);
}