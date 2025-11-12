using System.Numerics;
using System.Runtime.Versioning;
using Hexa.NET.ImGui;
using SharpVideo.Utils;

namespace SharpVideo.RtpPlayerDemo;

/// <summary>
/// Renders OSD overlay with player statistics using ImGui
/// </summary>
[SupportedOSPlatform("linux")]
public class OsdRenderer
{
    private readonly PlayerStatistics _statistics;
    private readonly RtpReceiverService _rtpReceiver;
    private bool _showStatistics = true;
    private bool _showHelp = true;
    
    // Track previous key state for toggle detection
    private bool _prevF1State;
    private bool _prevF2State;

    public OsdRenderer(PlayerStatistics statistics, RtpReceiverService rtpReceiver)
    {
        _statistics = statistics;
        _rtpReceiver = rtpReceiver;
    }

    /// <summary>
    /// Render OSD overlay
    /// </summary>
    public void Render()
    {
        if (_showStatistics)
        {
            RenderStatistics();
        }

        if (_showHelp)
        {
            RenderHelp();
        }
    }

    private void RenderStatistics()
    {
        Hexa.NET.ImGui.ImGui.SetNextWindowPos(new Vector2(10, 10), ImGuiCond.FirstUseEver);
        Hexa.NET.ImGui.ImGui.SetNextWindowSize(new Vector2(400, 250), ImGuiCond.FirstUseEver);
        
        Hexa.NET.ImGui.ImGui.Begin("RTP Player Statistics", ref _showStatistics, 
            ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.AlwaysAutoResize);

        // RTP Stream Info
        Hexa.NET.ImGui.ImGui.SeparatorText("RTP Stream");
        Hexa.NET.ImGui.ImGui.Text($"Received Frames: {_rtpReceiver.ReceivedFramesCount}");
        Hexa.NET.ImGui.ImGui.Text($"Dropped (RTP): {_rtpReceiver.DroppedFramesCount}");
        
        Hexa.NET.ImGui.ImGui.Spacing();
        
        // Decoder Info
        Hexa.NET.ImGui.ImGui.SeparatorText("Video Decoder");
        Hexa.NET.ImGui.ImGui.Text($"Decoded Frames: {_statistics.DecodedFrames}");
        Hexa.NET.ImGui.ImGui.Text($"Decode FPS (current): {_statistics.CurrentDecodeFps:F2}");
        Hexa.NET.ImGui.ImGui.Text($"Decode FPS (average): {_statistics.AverageDecodeFps:F2}");
        Hexa.NET.ImGui.ImGui.Text($"Avg Decode Time: {_statistics.AverageDecodeTimeMs:F2} ms/frame");
        
        Hexa.NET.ImGui.ImGui.Spacing();
        
        // Display Info
        Hexa.NET.ImGui.ImGui.SeparatorText("Display");
        Hexa.NET.ImGui.ImGui.Text($"Presented Frames: {_statistics.PresentedFrames}");
        Hexa.NET.ImGui.ImGui.Text($"Present FPS (current): {_statistics.CurrentPresentFps:F2}");
        Hexa.NET.ImGui.ImGui.Text($"Present FPS (average): {_statistics.AveragePresentFps:F2}");
        
        Hexa.NET.ImGui.ImGui.Spacing();
        
        // Performance indicator
        var latency = _statistics.DecodedFrames - _statistics.PresentedFrames;
        var color = latency < 5 ? new Vector4(0, 1, 0, 1) : 
                    latency < 10 ? new Vector4(1, 1, 0, 1) : 
                    new Vector4(1, 0, 0, 1);
        Hexa.NET.ImGui.ImGui.TextColored(color, $"Frame Latency: {latency} frames");
        
        Hexa.NET.ImGui.ImGui.End();
    }

    private void RenderHelp()
    {
        Hexa.NET.ImGui.ImGui.SetNextWindowPos(new Vector2(10, 280), ImGuiCond.FirstUseEver);
        Hexa.NET.ImGui.ImGui.SetNextWindowSize(new Vector2(350, 150), ImGuiCond.FirstUseEver);
        
        Hexa.NET.ImGui.ImGui.Begin("Controls", ref _showHelp, 
            ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.AlwaysAutoResize);

        Hexa.NET.ImGui.ImGui.TextColored(new Vector4(0, 1, 1, 1), "Keyboard Controls:");
        Hexa.NET.ImGui.ImGui.BulletText("ESC - Exit application");
        Hexa.NET.ImGui.ImGui.BulletText("F1 - Toggle statistics window");
        Hexa.NET.ImGui.ImGui.BulletText("F2 - Toggle this help window");
        
        Hexa.NET.ImGui.ImGui.Spacing();
        Hexa.NET.ImGui.ImGui.SeparatorText("Stream Info");
        Hexa.NET.ImGui.ImGui.Text("Listening on: 0.0.0.0:5600");
        Hexa.NET.ImGui.ImGui.Text("Expected codec: H.264");
        Hexa.NET.ImGui.ImGui.Text("Architecture: RTP ? V4L2 ? DRM + ImGui");
        
        Hexa.NET.ImGui.ImGui.End();
    }

    /// <summary>
    /// Process keyboard input for OSD controls
    /// </summary>
    public void ProcessInput(InputManager inputManager)
    {
        // F1 - Toggle statistics (detect key press with state tracking)
        var currentF1State = inputManager.IsKeyDown(59); // KEY_F1 = 59
        if (currentF1State && !_prevF1State)
        {
            _showStatistics = !_showStatistics;
        }
        _prevF1State = currentF1State;

        // F2 - Toggle help
        var currentF2State = inputManager.IsKeyDown(60); // KEY_F2 = 60
        if (currentF2State && !_prevF2State)
        {
            _showHelp = !_showHelp;
        }
        _prevF2State = currentF2State;
    }
}
