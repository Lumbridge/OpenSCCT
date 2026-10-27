using Godot;
using OpenScct.Server;

namespace OpenScctGame.Play;

/// <summary>
/// Local player input from keyboard and mouse until the input map from the original key layouts lands:
/// W/S forward and back, A/D strafe, mouse or Q/E to turn. Click captures the mouse, Esc releases it.
/// </summary>
public sealed class KeyboardPilot
{
    /// <summary>Yaw units (65536 per turn) per pixel of mouse motion.</summary>
    public const int MouseYawPerPixel = 24;

    /// <summary>Yaw units per tick while a turn key is held (a half turn per second).</summary>
    public const int KeyYawPerTick = 32768 / 60;

    private int _yaw;

    public int Yaw => _yaw;

    public void HandleInput(InputEvent inputEvent)
    {
        switch (inputEvent)
        {
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }:
                OpenScctGame.Controls.Pointer.Mode = global::Godot.Input.MouseModeEnum.Captured;
                break;
            case InputEventKey { Pressed: true, PhysicalKeycode: Key.Escape }:
                OpenScctGame.Controls.Pointer.Mode = global::Godot.Input.MouseModeEnum.Visible;
                break;
            case InputEventMouseMotion motion when OpenScctGame.Controls.Pointer.Mode == Input.MouseModeEnum.Captured:
                // Mouse right turns right, which is increasing UE yaw.
                _yaw += (int)(motion.Relative.X * MouseYawPerPixel);
                break;
        }
    }

    /// <summary>The input for the next move.</summary>
    public CubeInput Read()
    {
        if (Input.IsPhysicalKeyPressed(Key.Q) || Input.IsPhysicalKeyPressed(Key.Left))
        {
            _yaw -= KeyYawPerTick;
        }

        if (Input.IsPhysicalKeyPressed(Key.E) || Input.IsPhysicalKeyPressed(Key.Right))
        {
            _yaw += KeyYawPerTick;
        }

        int forward = (Input.IsPhysicalKeyPressed(Key.W) ? 127 : 0) - (Input.IsPhysicalKeyPressed(Key.S) ? 127 : 0);
        int strafe = (Input.IsPhysicalKeyPressed(Key.D) ? 127 : 0) - (Input.IsPhysicalKeyPressed(Key.A) ? 127 : 0);
        return new CubeInput((sbyte)forward, (sbyte)strafe, (ushort)(_yaw & 0xFFFF));
    }
}
