using System.Runtime.Versioning;
using Hexa.NET.ImGui;

namespace SharpVideo.ImGui;

/// <summary>
/// Delegate for rendering ImGui UI content.
/// Implement this to define your application's UI.
/// </summary>
/// <param name="deltaTime">Time since last frame in seconds</param>
public delegate void ImGuiRenderDelegate(float deltaTime);

/// <summary>
/// Interface for custom ImGui UI implementations.
/// Implement this interface to define reusable UI components.
/// </summary>
[SupportedOSPlatform("linux")]
public interface IImGuiDrawable
{
    /// <summary>
    /// Renders the ImGui UI. Called every frame.
    /// </summary>
    /// <param name="deltaTime">Time since last frame in seconds</param>
    void Draw(float deltaTime);
}
