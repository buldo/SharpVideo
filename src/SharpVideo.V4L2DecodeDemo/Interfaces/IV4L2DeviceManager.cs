using SharpVideo.V4L2DecodeDemo.Models;

namespace SharpVideo.V4L2DecodeDemo.Interfaces;

/// <summary>
/// Interface for V4L2 device discovery and management
/// </summary>
public interface IV4L2DeviceManager
{
    /// <summary>
    /// Finds the first available H.264 decoder device
    /// </summary>
    /// <returns>Device path if found, null otherwise</returns>
    Task<string?> FindH264DecoderDeviceAsync();

    /// <summary>
    /// Gets all available V4L2 devices with their capabilities
    /// </summary>
    /// <returns>Collection of device information</returns>
    Task<IEnumerable<V4L2DeviceInfo>> GetAvailableDevicesAsync();
}