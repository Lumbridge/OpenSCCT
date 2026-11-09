using Godot;
using OpenScctGame.Controls;

namespace OpenScctGame.Options;

/// <summary>
/// Controller use of the menus, whenever no controller drives a player. Raw pad events are consumed before GUI
/// dispatch; a paced keyboard pulse moves focus with the D-pad and left stick and activates with A. The first
/// press of a direction or A, when nothing has focus, focuses the first control of
/// the topmost menu; B, Back and Start press Escape (the key every screen and overlay closes on). It also remembers
/// the controller that last sent input, which <see cref="GamepadInput"/> plays with, shows Reloaded's hot plug lines
/// ("Gamepad connected", "Gamepad disconnected") and asks once whether to turn controller support on when a
/// controller is connected while it is off.
/// </summary>
public partial class PadMenus : Node
{
    public const string NodeName = "PadMenus";

    private static int s_lastDevice = -1;
    private readonly PadMenuNavigation _navigation = new();
    private int _navigationDevice = -1;

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
        if (@event is not (InputEventJoypadButton or InputEventJoypadMotion) || BindingInput.Current?.CaptureSuspended == true)
        {
            return;
        }
        // Native ui_accept/ui_cancel must never activate the chat or menu behind gameplay.
        GetViewport().SetInputAsHandled();
        if (!SettingsStore.Current.Pad.Enabled || GamepadInput.GameplayActive)
        {
            _navigation.Reset();
            return;
        }
        if (_navigationDevice != @event.Device)
        {
            _navigation.Reset();
            _navigationDevice = @event.Device;
        }
        double now = Time.GetTicksMsec() / 1000d;
        string? command = @event switch
        {
            InputEventJoypadButton button => Button(button, now),
            InputEventJoypadMotion { Axis: JoyAxis.LeftX } axis => _navigation.Axis(true, axis.AxisValue, now),
            InputEventJoypadMotion { Axis: JoyAxis.LeftY } axis => _navigation.Axis(false, axis.AxisValue, now),
            _ => null,
        };
        Navigate(command);
        // Nested keyboard dispatch changes the viewport's handled flag. Consume the original pad event again.
        GetViewport().SetInputAsHandled();
    }

    private string? Button(InputEventJoypadButton button, double now)
    {
        string? key = button.ButtonIndex switch
        {
            JoyButton.DpadUp => "Up", JoyButton.DpadDown => "Down", JoyButton.DpadLeft => "Left", JoyButton.DpadRight => "Right",
            JoyButton.A => "Enter", JoyButton.B or JoyButton.Back or JoyButton.Start => "Escape", _ => null,
        };
        return key is null ? null : _navigation.Button(key, button.Pressed, now);
    }

    private void Navigate(string? key)
    {
        if (key is null) return;
        if (key != "Escape" && GetViewport().GuiGetFocusOwner() is null && FirstFocusable(GetViewport()) is { } first)
        {
            first.GrabFocus();
            return;
        }
        if (GodotKeys.ToEvent(key) is not InputEventKey press) return;
        press.Keycode = press.PhysicalKeycode;
        press.Pressed = true;
        BindingInput.Current?.Resolve(press);
        GetViewport().PushInput(press);
        var release = new InputEventKey { Keycode = press.Keycode, PhysicalKeycode = press.PhysicalKeycode };
        BindingInput.Current?.Resolve(release);
        GetViewport().PushInput(release);
        if (GamepadInput.GameplayActive) GamepadInput.SuppressHeldButtons();
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

    // ---- hot plug messages and the first-run prompt (rl:input.pad) ------------------------------------------

    /// <summary>How long a "Gamepad connected" line stays.</summary>
    public const double NoticeSeconds = 3.0;

    /// <summary>Above the menus and the HUD.</summary>
    private const int NoticeLayer = 120;

    private CanvasLayer? _noticeLayer;
    private Label? _notice;
    private double _noticeLeft;
    private Control? _prompt;

    public override void _Ready()
    {
        Input.JoyConnectionChanged += OnJoyConnectionChanged;
        // A controller already plugged in at start gets the prompt too (no connection event comes for it).
        Callable.From(PromptIfWanted).CallDeferred();
    }

    public override void _ExitTree() => Input.JoyConnectionChanged -= OnJoyConnectionChanged;

    public override void _Process(double delta)
    {
        if (!SettingsStore.Current.Pad.Enabled || GamepadInput.GameplayActive || BindingInput.Current?.CaptureSuspended == true ||
            !GetWindow().HasFocus() && !Pointer.Detached)
            _navigation.Reset();
        else Navigate(_navigation.Advance(Time.GetTicksMsec() / 1000d));
        if (_notice is not null && _noticeLeft > 0 && (_noticeLeft -= delta) <= 0)
        {
            _notice.Visible = false;
        }
    }

    /// <summary>The line shown for a connection change (Reloaded's "Gamepad connected").</summary>
    public static string NoticeText(bool connected, string name) =>
        connected ? (name.Length > 0 ? "Gamepad connected: " + name : "Gamepad connected") : "Gamepad disconnected";

    private void OnJoyConnectionChanged(long device, bool connected)
    {
        if (!connected && device == _navigationDevice) _navigation.Reset();
        string name = connected ? Input.GetJoyName((int)device) : string.Empty;
        GD.Print("SVM_PAD " + NoticeText(connected, name));
        ShowNotice(NoticeText(connected, name));
        if (connected)
        {
            PromptIfWanted();
        }
    }

    private void EnsureLayer()
    {
        if (_noticeLayer is not null)
        {
            return;
        }

        _noticeLayer = new CanvasLayer { Name = "PadNotices", Layer = NoticeLayer };
        AddChild(_noticeLayer);
    }

    private void ShowNotice(string text)
    {
        EnsureLayer();
        if (_notice is null)
        {
            _notice = new Label
            {
                Name = "PadNotice", HorizontalAlignment = HorizontalAlignment.Center, Theme = OpenScctGame.UI.UiStyle.Theme,
                Modulate = OpenScctGame.UI.UiStyle.Text, MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            _notice.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterBottom);
            _notice.OffsetTop = -64f;
            _notice.OffsetBottom = -40f;
            _notice.GrowHorizontal = Control.GrowDirection.Both;
            _noticeLayer!.AddChild(_notice);
        }

        _notice.Text = text;
        _notice.Visible = true;
        _noticeLeft = NoticeSeconds;
    }

    /// <summary>
    /// Reloaded's first-run question, asked once: a controller is connected while native controller support is off and
    /// the player was never asked. Smoke runs never ask.
    /// </summary>
    private void PromptIfWanted()
    {
        PadSettings pad = SettingsStore.Current.Pad;
        if (_prompt is not null || pad.Enabled || pad.Prompted || SettingsStore.Isolated || Input.GetConnectedJoypads().Count == 0)
        {
            return;
        }

        EnsureLayer();
        var panel = new PanelContainer { Name = "PadPrompt", Theme = OpenScctGame.UI.UiStyle.Theme };
        panel.AddThemeStyleboxOverride("panel", OpenScctGame.UI.UiStyle.MenuPanelBox());
        panel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center);
        panel.GrowHorizontal = Control.GrowDirection.Both;
        panel.GrowVertical = Control.GrowDirection.Both;
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 12);
        panel.AddChild(column);
        column.AddChild(new Label
        {
            Text = "Would you like to enable native controller support?", HorizontalAlignment = HorizontalAlignment.Center,
            Modulate = OpenScctGame.UI.UiStyle.Text,
        });
        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        Button yes = OpenScctGame.UI.UiStyle.MenuButton("Yes", () => Answer(true), "PadPromptYes");
        Button no = OpenScctGame.UI.UiStyle.MenuButton("No", () => Answer(false), "PadPromptNo");
        buttons.AddChild(yes);
        buttons.AddChild(no);
        column.AddChild(buttons);
        _noticeLayer!.AddChild(panel);
        _prompt = panel;
        yes.CallDeferred(Control.MethodName.GrabFocus);
    }

    private void Answer(bool enable)
    {
        GameSettings settings = SettingsStore.Current.Clone();
        settings.Pad.Enabled = enable;
        settings.Pad.Prompted = true;
        SettingsStore.Save(settings);
        _prompt?.QueueFree();
        _prompt = null;
    }
}
