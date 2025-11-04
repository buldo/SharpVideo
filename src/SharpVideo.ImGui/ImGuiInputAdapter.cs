using System.Runtime.Versioning;
using Hexa.NET.ImGui;
using SharpVideo.Utils;

namespace SharpVideo.ImGui;

/// <summary>
/// Adapter that bridges libinput events to ImGui input handling.
/// Converts Linux input events (mouse, keyboard, touch) to ImGui-compatible input state.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class ImGuiInputAdapter
{
    private readonly InputManager _inputManager;
    private readonly ImGuiIOPtr _io;

    // Previous frame state for delta tracking
    private readonly bool[] _prevMouseButtons = new bool[5];

    /// <summary>
    /// Creates a new ImGui input adapter.
    /// </summary>
    /// <param name="inputManager">InputManager instance (libinput-based)</param>
    /// <param name="io">ImGui IO pointer</param>
    public ImGuiInputAdapter(InputManager inputManager, ImGuiIOPtr io)
    {
        _inputManager = inputManager ?? throw new ArgumentNullException(nameof(inputManager));
        _io = io;
    }

    /// <summary>
    /// Updates ImGui input state from libinput.
    /// Call this every frame before ImGui.NewFrame().
    /// </summary>
    public void UpdateInput()
    {
        // Update mouse position
        var (mouseX, mouseY) = _inputManager.GetMousePosition();
        _io.MousePos = new System.Numerics.Vector2(mouseX, mouseY);

        // Update mouse buttons (0=left, 1=right, 2=middle, 3=side, 4=extra)
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

        // TODO: Full keyboard support with proper keycode mapping
        // For now, basic keys are supported via MapLinuxKeyToImGuiKey
    }

    /// <summary>
    /// Maps Linux keycode to ImGui key enum.
    /// Based on linux/input-event-codes.h
    /// </summary>
    /// <param name="linuxKeyCode">Linux input event keycode</param>
    /// <returns>Corresponding ImGui key or ImGuiKey.None if not mapped</returns>
    public static ImGuiKey MapLinuxKeyToImGuiKey(uint linuxKeyCode)
    {
        // Linux input event keycodes (from linux/input-event-codes.h)
        return linuxKeyCode switch
        {
            1 => ImGuiKey.Escape,       // KEY_ESC
            14 => ImGuiKey.Backspace,   // KEY_BACKSPACE
            15 => ImGuiKey.Tab,         // KEY_TAB
            28 => ImGuiKey.Enter,       // KEY_ENTER
            29 => ImGuiKey.LeftCtrl,    // KEY_LEFTCTRL
            42 => ImGuiKey.LeftShift,   // KEY_LEFTSHIFT
            56 => ImGuiKey.LeftAlt,     // KEY_LEFTALT
            57 => ImGuiKey.Space,       // KEY_SPACE
            103 => ImGuiKey.UpArrow,    // KEY_UP
            105 => ImGuiKey.LeftArrow,  // KEY_LEFT
            106 => ImGuiKey.RightArrow, // KEY_RIGHT
            108 => ImGuiKey.DownArrow,  // KEY_DOWN
            111 => ImGuiKey.Delete,     // KEY_DELETE
            
            // Function keys
            59 => ImGuiKey.F1,          // KEY_F1
            60 => ImGuiKey.F2,          // KEY_F2
            61 => ImGuiKey.F3,          // KEY_F3
            62 => ImGuiKey.F4,          // KEY_F4
            63 => ImGuiKey.F5,          // KEY_F5
            64 => ImGuiKey.F6,          // KEY_F6
            65 => ImGuiKey.F7,          // KEY_F7
            66 => ImGuiKey.F8,          // KEY_F8
            67 => ImGuiKey.F9,          // KEY_F9
            68 => ImGuiKey.F10,         // KEY_F10
            87 => ImGuiKey.F11,         // KEY_F11
            88 => ImGuiKey.F12,         // KEY_F12

            // Alphanumeric keys (A-Z)
            30 => ImGuiKey.A,           // KEY_A
            48 => ImGuiKey.B,           // KEY_B
            46 => ImGuiKey.C,           // KEY_C
            32 => ImGuiKey.D,           // KEY_D
            18 => ImGuiKey.E,           // KEY_E
            33 => ImGuiKey.F,           // KEY_F
            34 => ImGuiKey.G,           // KEY_G
            35 => ImGuiKey.H,           // KEY_H
            23 => ImGuiKey.I,           // KEY_I
            36 => ImGuiKey.J,           // KEY_J
            37 => ImGuiKey.K,           // KEY_K
            38 => ImGuiKey.L,           // KEY_L
            50 => ImGuiKey.M,           // KEY_M
            49 => ImGuiKey.N,           // KEY_N
            24 => ImGuiKey.O,           // KEY_O
            25 => ImGuiKey.P,           // KEY_P
            16 => ImGuiKey.Q,           // KEY_Q
            19 => ImGuiKey.R,           // KEY_R
            31 => ImGuiKey.S,           // KEY_S
            20 => ImGuiKey.T,           // KEY_T
            22 => ImGuiKey.U,           // KEY_U
            47 => ImGuiKey.V,           // KEY_V
            17 => ImGuiKey.W,           // KEY_W
            45 => ImGuiKey.X,           // KEY_X
            21 => ImGuiKey.Y,           // KEY_Y
            44 => ImGuiKey.Z,           // KEY_Z

            // Note: Number keys (0-9) mapping depends on ImGui version
            // Keypad numbers
            82 => ImGuiKey.Keypad0,     // KEY_KP0
            79 => ImGuiKey.Keypad1,     // KEY_KP1
            80 => ImGuiKey.Keypad2,     // KEY_KP2
            81 => ImGuiKey.Keypad3,     // KEY_KP3
            75 => ImGuiKey.Keypad4,     // KEY_KP4
            76 => ImGuiKey.Keypad5,     // KEY_KP5
            77 => ImGuiKey.Keypad6,     // KEY_KP6
            71 => ImGuiKey.Keypad7,     // KEY_KP7
            72 => ImGuiKey.Keypad8,     // KEY_KP8
            73 => ImGuiKey.Keypad9,     // KEY_KP9

            _ => ImGuiKey.None
        };
    }
}
