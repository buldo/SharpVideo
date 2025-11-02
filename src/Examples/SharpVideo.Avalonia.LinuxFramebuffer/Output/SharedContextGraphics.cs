using System;
using Avalonia.Platform;

namespace SharpVideo.Avalonia.LinuxFramebuffer.Output;

internal class SharedContextGraphics : IPlatformGraphics
{
    private readonly IPlatformGraphicsContext _context;

    public SharedContextGraphics(IPlatformGraphicsContext context)
    {
        _context = context;
    }
    public bool UsesSharedContext => true;
    public IPlatformGraphicsContext CreateContext() => throw new NotSupportedException();

    public IPlatformGraphicsContext GetSharedContext() => _context;
}