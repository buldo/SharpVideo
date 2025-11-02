namespace SharpVideo.Linux.Native.C;

[Flags]
public enum ProtFlags : int
{
    PROT_NONE = 0x0,
    PROT_READ = 0x1,
    PROT_WRITE = 0x2,
    PROT_EXEC = 0x4
}