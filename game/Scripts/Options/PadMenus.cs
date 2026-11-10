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
    private double _deviceLastInput = double.NegativeInfinity;

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
        if (@event is InputEventJoypadMotion { Axis: JoyAxis.LeftX or JoyAxis.LeftY } ||
            @event is InputEventJoypadButton { ButtonIndex: JoyButton.DpadUp or JoyButton.DpadDown or JoyButton.DpadLeft or JoyButton.DpadRight or
                JoyButton.A or JoyButton.B or JoyButton.Back or JoyButton.Start })
            DispatchMenuInput(@event, Time.GetTicksMsec() / 1000d);
        // Nested keyboard dispatch changes the viewport's handled flag. Consume the original pad event again.
        GetViewport().SetInputAsHandled();
    }

    private void DispatchMenuInput(InputEvent @event, double now)
    {
        if (_navigationDevice != @event.Device)
        {
            // Keep one controller in charge of a gesture. A short handover gap also covers virtual
            // controllers that report a duplicate tap just after the physical controller releases it.
            if (_navigation.IsActive || now < _deviceLastInput + .1) return;
            _navigation.Reset();
            _navigationDevice = @event.Device;
        }
        bool wasActive = _navigation.IsActive;
        string? command = @event switch
        {
            InputEventJoypadButton button => Button(button, now),
            InputEventJoypadMotion { Axis: JoyAxis.LeftX } axis => _navigation.Axis(true, axis.AxisValue, now),
            InputEventJoypadMotion { Axis: JoyAxis.LeftY } axis => _navigation.Axis(false, axis.AxisValue, now),
            _ => null,
        };
        if (wasActive || _navigation.IsActive) _deviceLastInput = now;
        Navigate(command);
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
    private PanelContainer? _noticePanel;
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
            _noticePanel!.Visible = false;
        }
        if (_noticePanel is not null && _noticePanel.Visible) FitNotice();
    }

    /// <summary>The line shown for a connection change (Reloaded's "Gamepad connected").</summary>
    public static string NoticeText(bool connected, string name) =>
        connected ? (name.Length > 0 ? "Gamepad connected: " + name : "Gamepad connected") : "Gamepad disconnected";

    private void OnJoyConnectionChanged(long device, bool connected)
    {
        if (!connected && device == _navigationDevice)
        {
            _navigation.Reset();
            _navigationDevice = -1;
            _deviceLastInput = double.NegativeInfinity;
        }
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

    internal void ShowNotice(string text)
    {
        EnsureLayer();
        if (_notice is null)
        {
            _noticePanel = new PanelContainer
            {
                Name = "PadNoticePanel", Theme = OpenScctGame.UI.UiStyle.Theme,
                MouseFilter = Control.MouseFilterEnum.Ignore,
                AnchorLeft = .5f, AnchorRight = .5f, AnchorTop = 1f, AnchorBottom = 1f,
                GrowHorizontal = Control.GrowDirection.Both, GrowVertical = Control.GrowDirection.Begin,
                OversamplingWithScale = CanvasItem.OversamplingWithScaleEnum.Enabled,
            };
            _noticePanel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
            {
                BgColor = new Color(.035f, .055f, .045f, 1f),
                BorderColor = OpenScctGame.UI.UiStyle.MenuFocus,
                BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1, BorderWidthBottom = 1,
                ContentMarginLeft = 16, ContentMarginRight = 16, ContentMarginTop = 8, ContentMarginBottom = 8,
            });
            _notice = new Label
            {
                Name = "PadNotice", HorizontalAlignment = HorizontalAlignment.Center, Theme = OpenScctGame.UI.UiStyle.Theme,
                Modulate = OpenScctGame.UI.UiStyle.Text, MouseFilter = Control.MouseFilterEnum.Ignore,
                ClipText = true, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            };
            _noticePanel.AddChild(_notice);
            _noticeLayer!.AddChild(_noticePanel);
        }

        _notice.Text = text;
        _noticePanel!.Visible = true;
        FitNotice();
        _noticeLeft = NoticeSeconds;
    }

    private void FitNotice()
    {
        Vector2 viewport = GetViewport().GetVisibleRect().Size;
        float scale = System.Math.Max(1f, System.Math.Min(viewport.X / 1600f, viewport.Y / 900f));
        float width = System.Math.Min(560f, System.Math.Max(1f, (viewport.X - 48f) / scale));
        // Leave the menu's footer, build line and decorative corners clear, including scaled dashboards.
        float footer = System.Math.Max(88f, viewport.Y * .1f);
        float height = _noticePanel!.GetCombinedMinimumSize().Y;
        _noticePanel.Scale = new Vector2(scale, scale);
        _noticePanel.OffsetLeft = -width * scale / 2f;
        _noticePanel.OffsetRight = _noticePanel.OffsetLeft + width;
        _noticePanel.OffsetTop = -footer - height * scale;
        _noticePanel.OffsetBottom = _noticePanel.OffsetTop + height;
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
