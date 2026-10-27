using Godot;

namespace OpenScctGame.Options;

/// <summary>
/// Controller use of the menus, whenever no controller drives a player (<see cref="GamepadInput.Driving"/>). Godot's
/// own ui actions move the focus with the D-pad and left stick and press the focused control with A; this node adds
/// what they leave out: the first press of a direction or A, when nothing has the focus, focuses the first control of
/// the topmost menu; B, Back and Start press Escape (the key every screen and overlay closes on). It also remembers
/// the controller that last sent input, which <see cref="GamepadInput"/> plays with.
/// </summary>
public partial class PadMenus : Node
{
    public const string NodeName = "PadMenus";

    private static int s_lastDevice = -1;

    public PadMenus()
    {
        Name = NodeName;
        ProcessMode = ProcessModeEnum.Always;
    }

    /// <summary>The controller in use: the last one that sent input while still connected, else the first connected, else -1.</summary>
    public static int CurrentDevice()
    {
        Godot.Collections.Array<int> pads = Input.GetConnectedJoypads();
        if (pads.Count == 0)
        {
            return -1;
        }

        return pads.Contains(s_lastDevice) ? s_lastDevice : pads[0];
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventJoypadButton { Pressed: true } || @event is InputEventJoypadMotion { AxisValue: > 0.5f or < -0.5f })
        {
            s_lastDevice = @event.Device;
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!SettingsStore.Current.Pad.Enabled || GamepadInput.Driving || @event is not (InputEventJoypadButton or InputEventJoypadMotion))
        {
            return;
        }

        if (@event is InputEventJoypadButton { Pressed: true, ButtonIndex: JoyButton.B or JoyButton.Back or JoyButton.Start })
        {
            GetViewport().SetInputAsHandled();
            GamepadInput.Send(GetViewport(), "Escape", true);
            GamepadInput.Send(GetViewport(), "Escape", false);
            return;
        }

        bool moves = @event.IsActionPressed("ui_up") || @event.IsActionPressed("ui_down") || @event.IsActionPressed("ui_left") ||
                     @event.IsActionPressed("ui_right") || @event.IsActionPressed("ui_accept");
        if (moves && GetViewport().GuiGetFocusOwner() is null && FirstFocusable(GetTree().Root) is { } first)
        {
            GetViewport().SetInputAsHandled();
            first.GrabFocus();
        }
    }

    /// <summary>
    /// The first focusable control of the topmost menu: the highest canvas layer wins, then the last-added screen on
    /// that layer, then the first control of that screen in tree order.
    /// </summary>
    public static Control? FirstFocusable(Node root)
    {
        Control? best = null;
        (int Layer, int Screen) bestRank = (int.MinValue, int.MinValue);
        int screen = 0;

        void Walk(Node node, int layer, bool screenTop)
        {
            if (node is Window && node != root)
            {
                // Popups and other windows keep their own focus.
                return;
            }

            if (node is CanvasLayer canvas)
            {
                if (!canvas.Visible)
                {
                    return;
                }

                layer = canvas.Layer;
                screenTop = true;
            }

            if (node is Control control)
            {
                if (!control.IsVisibleInTree())
                {
                    return;
                }

                if (screenTop)
                {
                    screen++;
                    screenTop = false;
                }

                if (control.FocusMode == Control.FocusModeEnum.All && control is not BaseButton { Disabled: true } &&
                    (layer, screen).CompareTo(bestRank) > 0)
                {
                    best = control;
                    bestRank = (layer, screen);
                }
            }

            foreach (Node child in node.GetChildren())
            {
                Walk(child, layer, screenTop);
            }
        }

        Walk(root, 0, true);
        return best;
    }
}
