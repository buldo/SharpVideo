using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;

namespace SharpVideo.RtpPlayerDemo;

/// <summary>
/// Handles graceful application shutdown on SIGINT (Ctrl+C) and SIGTERM
/// </summary>
[SupportedOSPlatform("linux")]
public class ShutdownHandler : IDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private readonly ILogger _logger;
    private bool _disposed;

    public ShutdownHandler(ILogger logger)
    {
        _logger = logger;
        
        // Setup signal handlers
        Console.CancelKeyPress += OnCancelKeyPress;
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
        
        _logger.LogDebug("Shutdown handler initialized");
    }

    public CancellationToken Token => _cts.Token;

    private void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        _logger.LogInformation("Received SIGINT (Ctrl+C), initiating graceful shutdown...");
        e.Cancel = true; // Prevent immediate termination
        _cts.Cancel();
    }

    private void OnProcessExit(object? sender, EventArgs e)
    {
        _logger.LogInformation("Process exit requested, initiating shutdown...");
        _cts.Cancel();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        
        Console.CancelKeyPress -= OnCancelKeyPress;
        AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
        
        _cts.Dispose();
        
        _logger.LogDebug("Shutdown handler disposed");
    }
}
