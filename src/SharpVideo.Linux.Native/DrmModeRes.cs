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
    private readonly UInt32* _fbs;                /* uint32_t* */

    public readonly int CountCrtcs;
    private readonly UInt32* _crtcs;              /* uint32_t* */

    public readonly int CountConnectors;
    private readonly UInt32* _connectors;         /* uint32_t* */

    public readonly int CountEncoders;
    private readonly UInt32* _encoders;           /* uint32_t* */

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
    public ReadOnlySpan<uint> FramebufferIds => _fbs == null
        ? ReadOnlySpan<uint>.Empty
        : new ReadOnlySpan<uint>(_fbs, CountFbs);

    public ReadOnlySpan<uint> CrtcIds => _crtcs == null
        ? ReadOnlySpan<uint>.Empty
        : new ReadOnlySpan<uint>(_crtcs, CountCrtcs);

    public ReadOnlySpan<uint> ConnectorIds => _connectors == null
        ? ReadOnlySpan<uint>.Empty
        : new ReadOnlySpan<uint>(_connectors, CountConnectors);

    public ReadOnlySpan<uint> EncoderIds => _encoders == null
        ? ReadOnlySpan<uint>.Empty
        : new ReadOnlySpan<uint>(_encoders, CountEncoders);
}

