using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Versioning;

using SharpVideo.Linux.Native;

namespace SharpVideo.V4L2;

[SupportedOSPlatform("linux")]
public class V4L2Device : IDisposable
{
    private readonly int _deviceFd;
    private bool _disposed = false;

    internal V4L2Device(int deviceFd, List<V4L2DeviceControl> controls, List<V4L2DeviceExtendedControl> extendedControls)
    {
        _deviceFd = deviceFd;
        Controls = controls.AsReadOnly();
        ExtendedControls = extendedControls.AsReadOnly();
    }

    public IReadOnlyCollection<V4L2DeviceControl> Controls { get; }

    public IReadOnlyCollection<V4L2DeviceExtendedControl> ExtendedControls { get; }

    public void SetFormatMplane(
        V4L2BufferType type,
        V4L2PixFormatMplane pixFormat)
    {
        var format = new V4L2Format
        {
            Type = type,
            Pix_mp = pixFormat
        };

        var formatResult = LibV4L2.SetFormat(_deviceFd, ref format);
        if (!formatResult.Success)
        {
            throw new Exception($"Failed to set {type} format");
        }
    }

    public void StreamOn(V4L2BufferType type)
    {
        var outputResult = LibV4L2.StreamOn(_deviceFd, type);
        if (!outputResult.Success)
        {
            throw new Exception($"Failed to start {type} streaming: {outputResult.ErrorMessage}");
        }
    }

    public void StreamOff(V4L2BufferType type)
    {
        var outputResult = LibV4L2.StreamOff(_deviceFd, type);
        if (!outputResult.Success)
        {
            throw new Exception($"Failed to start {type} streaming: {outputResult.ErrorMessage}");
        }
    }

    /// <summary>
    /// Set a single extended control - much simpler and more predictable
    /// </summary>
    public void SetSingleExtendedControl<T>(uint controlId, T data) where T : struct
    {
        var size = (uint)Marshal.SizeOf<T>();
        var dataPtr = Marshal.AllocHGlobal((int)size);

        try
        {
            // Clear the allocated memory
            unsafe
            {
                byte* ptr = (byte*)dataPtr.ToPointer();
                for (int i = 0; i < size; i++)
                {
                    ptr[i] = 0;
                }
            }

            // Marshal the structure to unmanaged memory
            Marshal.StructureToPtr(data, dataPtr, false);

            // Create the control structure
            var control = new V4L2ExtControl
            {
                Id = controlId,
                Size = size,
                Ptr = dataPtr
            };

            // Allocate memory for single control
            var controlPtr = Marshal.AllocHGlobal(Marshal.SizeOf<V4L2ExtControl>());

            try
            {
                Marshal.StructureToPtr(control, controlPtr, false);

                // Set up extended controls wrapper for single control
                var extControlsWrapper = new V4L2ExtControls
                {
                    Which = V4l2ControlsConstants.V4L2_CTRL_CLASS_CODEC,
                    Count = 1,
                    Controls = controlPtr
                };

                //_logger.LogDebug("Setting control 0x{ControlId:X8} with {Size} bytes", controlId, size);

                // Set the control
                var result = LibV4L2.SetExtendedControls(fd, ref extControlsWrapper);
                if (!result.Success)
                {
                    var errorCode = Marshal.GetLastWin32Error();
                    //_logger.LogError("Failed to set control 0x{ControlId:X8}: {Error} (errno: {ErrorCode})", controlId, result.ErrorMessage, errorCode);

                    throw new InvalidOperationException($"Failed to set control 0x{controlId:X8}: {result.ErrorMessage} (errno: {errorCode})");
                }

                //_logger.LogDebug("Successfully set control 0x{ControlId:X8}", controlId);
            }
            finally
            {
                Marshal.FreeHGlobal(controlPtr);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(dataPtr);
        }
    }

    // TODO: Remove
    public int fd
    {
        get
        {
            ThrowIfDisposed();
            return _deviceFd;
        }
    }

    /// <summary>
    /// Gets a value indicating whether the device has been disposed.
    /// </summary>
    public bool IsDisposed => _disposed;

    /// <summary>
    /// Releases all resources used by the V4L2Device.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Releases the unmanaged resources used by the V4L2Device and optionally releases the managed resources.
    /// </summary>
    /// <param name="disposing">true to release both managed and unmanaged resources; false to release only unmanaged resources.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                // Dispose managed resources here if any
                // Currently no managed resources to dispose
            }

            // Dispose unmanaged resources
            if (_deviceFd >= 0)
            {
                try
                {
                    Libc.close(_deviceFd);
                }
                catch
                {
                    // Ignore exceptions during disposal to prevent resource leaks
                    // Log if logging is available in the future
                }
            }

            _disposed = true;
        }
    }

    /// <summary>
    /// Finalizer that ensures the file descriptor is closed if Dispose was not called.
    /// </summary>
    ~V4L2Device()
    {
        Dispose(false);
    }

    /// <summary>
    /// Throws an ObjectDisposedException if the object has been disposed.
    /// </summary>
    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(V4L2Device));
        }
    }
}