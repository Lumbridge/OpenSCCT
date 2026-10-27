using Godot;

namespace OpenScctGame.Controls;

/// <summary>
/// The one place the game takes or frees the mouse pointer. In a test run (<see cref="Detached"/>) nothing touches the
/// real pointer: the requested mode is only remembered, so code and checks that read it behave as when playing, while
/// the person at the machine keeps their mouse. Tests feed their input as Godot events, which need no captured pointer.
/// </summary>
public static class Pointer
{
    private static Input.MouseModeEnum _requested = Input.MouseModeEnum.Visible;

    /// <summary>True in test runs: the real pointer is never captured or confined.</summary>
    public static bool Detached { get; set; }

    /// <summary>The pointer mode the game asked for (the real one when not detached).</summary>
    public static Input.MouseModeEnum Mode
    {
        get => Detached ? _requested : Input.MouseMode;
        set
        {
            _requested = value;
            if (!Detached)
            {
                Input.MouseMode = value;
            }
        }
    }

    public static bool IsCaptured => Mode == Input.MouseModeEnum.Captured;

    public static void Capture() => Mode = Input.MouseModeEnum.Captured;

    public static void Release() => Mode = Input.MouseModeEnum.Visible;
}
