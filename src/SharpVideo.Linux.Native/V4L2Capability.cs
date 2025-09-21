using System.Runtime.InteropServices;

namespace SharpVideo.Linux.Native;

/// <summary>
/// Managed representation of the native <c>v4l2_capability</c> structure.
/// Used to query device capabilities.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct V4L2Capability
{
    /// <summary>
    /// Name of the driver ("bttv", "msp3400", etc.)
    /// </summary>
    public fixed byte Driver[16];

    /// <summary>
    /// Name of the card (device name)
    /// </summary>
    public fixed byte Card[32];

    /// <summary>
    /// Location of the device (USB bus, PCI slot, etc.)
    /// </summary>
    public fixed byte BusInfo[32];

    /// <summary>
    /// Kernel version (usually KERNEL_VERSION)
    /// </summary>
    public uint Version;

    /// <summary>
    /// Available capabilities of the physical device
    /// </summary>
    public V4L2Capabilities Capabilities;

    /// <summary>
    /// Available capabilities of the opened device node
    /// </summary>
    public V4L2Capabilities DeviceCaps;

    /// <summary>
    /// Reserved for future extensions
    /// </summary>
    public fixed uint Reserved[3];

    /// <summary>
    /// Gets the driver name as a managed string
    /// </summary>
    public readonly string DriverString
    {
        get
        {
            fixed (byte* ptr = Driver)
            {
                return Marshal.PtrToStringUTF8((nint)ptr) ?? string.Empty;
            }
        }
    }

    /// <summary>
    /// Gets the card name as a managed string
    /// </summary>
    public readonly string CardString
    {
        get
        {
            fixed (byte* ptr = Card)
            {
                return Marshal.PtrToStringUTF8((nint)ptr) ?? string.Empty;
            }
        }
    }

    /// <summary>
    /// Gets the bus info as a managed string
    /// </summary>
    public readonly string BusInfoString
    {
        get
        {
            fixed (byte* ptr = BusInfo)
            {
                return Marshal.PtrToStringUTF8((nint)ptr) ?? string.Empty;
            }
        }
    }
}