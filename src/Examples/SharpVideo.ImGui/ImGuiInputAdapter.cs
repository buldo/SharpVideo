using System.Runtime.Versioning;
using Hexa.NET.ImGui;
using SharpVideo.Utils;

namespace SharpVideo.ImGui;

/// <summary>
/// Adapter that bridges libinput events to ImGui input handling.
/// </summary>
[SupportedOSPlatform("linux")]
public class ImGuiInputAdapter
{
    private readonly InputManager _inputManager;
    private readonly ImGuiIOPtr _io;

    // Previous frame mouse state for delta tracking
    private (float x, float y) _prevMousePos;
    private readonly bool[] _prevMouseButtons = new bool[5];

    public ImGuiInputAdapter(InputManager inputManager, ImGuiIOPtr io)
    {
        _inputManager = inputManager;
 _io = io;
    
      var (x, y) = _inputManager.GetMousePosition();
        _prevMousePos = (x, y);
    }

    /// <summary>
    /// Update ImGui input state from libinput. Call this every frame before ImGui.NewFrame().
    /// </summary>
    public void UpdateImGuiInput()
    {
// Update mouse position
        var (mouseX, mouseY) = _inputManager.GetMousePosition();
        _io.MousePos = new System.Numerics.Vector2(mouseX, mouseY);

        // Update mouse buttons
for (int i = 0; i < 5; i++)
   {
       var isDown = _inputManager.IsMouseButtonDown(i);
         _io.MouseDown[i] = isDown;
   
   // Track clicks (transition from up to down)
   if (isDown && !_prevMouseButtons[i])
            {
    _io.MouseClicked[i] = true;
       }
     
  _prevMouseButtons[i] = isDown;
        }

        // Note: Keyboard input requires key mapping from Linux keycodes to ImGui keys
        // This is more complex and would require a full keycode translation table
        // For now, mouse input is sufficient for basic ImGui interaction

   _prevMousePos = (mouseX, mouseY);
    }

    /// <summary>
    /// Maps Linux keycode to ImGui key enum.
    /// TODO: Implement full keycode mapping table.
 /// </summary>
    private ImGuiKey MapLinuxKeyToImGuiKey(uint linuxKeyCode)
    {
      // Linux input event keycodes (from linux/input-event-codes.h)
        return linuxKeyCode switch
  {
            1 => ImGuiKey.Escape,      // KEY_ESC
            14 => ImGuiKey.Backspace,  // KEY_BACKSPACE
            15 => ImGuiKey.Tab,        // KEY_TAB
         28 => ImGuiKey.Enter,      // KEY_ENTER
  29 => ImGuiKey.LeftCtrl,   // KEY_LEFTCTRL
    42 => ImGuiKey.LeftShift,  // KEY_LEFTSHIFT
   56 => ImGuiKey.LeftAlt,    // KEY_LEFTALT
57 => ImGuiKey.Space,      // KEY_SPACE
            103 => ImGuiKey.UpArrow,   // KEY_UP
105 => ImGuiKey.LeftArrow, // KEY_LEFT
            106 => ImGuiKey.RightArrow,// KEY_RIGHT
            108 => ImGuiKey.DownArrow, // KEY_DOWN
      111 => ImGuiKey.Delete,    // KEY_DELETE

  // Add more mappings as needed
    _ => ImGuiKey.None
    };
    }
}
