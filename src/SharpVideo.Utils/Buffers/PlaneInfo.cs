namespace SharpVideo.Utils.Buffers;

/// <summary>
/// Contains per-plane buffer information for DRM framebuffer operations.
/// </summary>
/// <param name="Pitch">Bytes per row (stride) for this plane, including alignment padding.</param>
/// <param name="Offset">Byte offset from buffer start to this plane's data.</param>
/// <param name="Size">Total size of this plane in bytes.</param>
public readonly record struct PlaneInfo(uint Pitch, uint Offset, uint Size);