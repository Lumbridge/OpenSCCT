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

    /// <summary>How close a key and a controller press must be to count as one press sent twice.</summary>
    private const ulong MirrorMs = 150;

    /// <summary>How long another controller must send input alone before it becomes the one in use.</summary>
    private const ulong AdoptMs = 300;

    private static int s_lastDevice = -1;
    private static int s_candidate = -1;
    private static ulong s_candidateAt;
    private static ulong s_buttonAt;
    private static bool s_listening;
    private readonly PadMenuNavigation _navigation = new();
    private readonly System.Collections.Generic.Dictionary<string, ulong> _padAt = new(System.StringComparer.Ordinal);
    private readonly System.Collections.Generic.Dictionary<string, ulong> _keyAt = new(System.StringComparer.Ordinal);
    private bool _pushing;
    private int _mirrored;
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

    /// <summary>A controller is connected and on, so a key press may be Steam Input's copy of a button press.</summary>
    public static bool MayMirrorKeys => SettingsStore.Current.Pad.Enabled && Input.GetConnectedJoypads().Count > 0;

    /// <summary>A controller button went down within <see cref="MirrorMs"/> of <paramref name="ms"/> (engine ticks).</summary>
    public static bool ButtonPressedNear(ulong ms) =>
        s_buttonAt != 0 && (s_buttonAt > ms ? s_buttonAt - ms : ms - s_buttonAt) <= MirrorMs;

    /// <summary>
    /// The binding editor is waiting for a control to bind, so controller input is its to read. Its dialogs and
    /// menus also suspend key capture but must still answer the controller, hence the separate flag.
    /// </summary>
    public static bool ListeningForBinding
    {
        get => s_listening && BindingInput.Current?.CaptureSuspended == true;
        set => s_listening = value;
    }

    /// <summary>
    /// Steam Input can show one controller as two devices that both send every press (the Steam Deck's own pad and
    /// Steam's virtual one). The device in use therefore changes only when another one sends input while the
    /// current one stays silent and idle; following every event would flip between the two on each press.
    /// </summary>
    private static void Adopt(InputEvent @event)
    {
        bool strong = @event is InputEventJoypadButton { Pressed: true } or InputEventJoypadMotion { AxisValue: > 0.5f or < -0.5f };
        if (@event.Device == s_lastDevice)
        {
            if (strong || @event is InputEventJoypadButton) s_candidate = -1;
            return;
        }
        if (!strong) return;
        if (s_lastDevice < 0 || !Input.GetConnectedJoypads().Contains(s_lastDevice))
        {
            s_lastDevice = @event.Device;
            s_candidate = -1;
        }
        else if (s_candidate != @event.Device)
        {
            s_candidate = @event.Device;
            s_candidateAt = Time.GetTicksMsec();
        }
    }

    private static void AdoptCandidate()
    {
        if (s_candidate < 0 || Time.GetTicksMsec() - s_candidateAt < AdoptMs) return;
        if (Input.GetConnectedJoypads().Contains(s_lastDevice) && Busy(s_lastDevice)) return;
        GD.Print($"SVM_PAD controller in use: device {s_candidate} ({Input.GetJoyName(s_candidate)}), was device {s_lastDevice}");
        s_lastDevice = s_candidate;
        s_candidate = -1;
    }

    private static bool Busy(int device)
    {
        foreach (JoyButton button in GamepadInput.Buttons.Values)
            if (Input.IsJoyButtonPressed(device, button)) return true;
        for (JoyAxis axis = JoyAxis.LeftX; axis <= JoyAxis.TriggerRight; axis++)
            if (Mathf.Abs(Input.GetJoyAxis(device, axis)) > 0.5f) return true;
        return false;
    }

    private static string? MenuKeyOf(InputEventKey key) => (key.Keycode == Key.None ? key.PhysicalKeycode : key.Keycode) switch
    {
        Key.Up => "Up", Key.Down => "Down", Key.Left => "Left", Key.Right => "Right",
        Key.Enter or Key.KpEnter => "Enter", Key.Escape => "Escape", _ => null,
    };

    /// <summary>
    /// Steam Input layouts that send keys with the buttons (the Deck's desktop layout: D-pad as arrows, A as Enter,
    /// B as Escape) would move the menus twice per press. A key that arrives with the same controller press, or
    /// while that button is held, is dropped here.
    /// </summary>
    private void DropMirroredKey(InputEventKey key)
    {
        if (_pushing || !SettingsStore.Current.Pad.Enabled || s_lastDevice < 0 || GamepadInput.GameplayActive ||
            MenuKeyOf(key) is not { } name) return;
        ulong now = Time.GetTicksMsec();
        if (_navigation.Holds(name) || _padAt.TryGetValue(name, out ulong at) && now - at <= MirrorMs)
        {
            GetViewport().SetInputAsHandled();
            if (key.Pressed && !key.Echo) NoteMirror();
            return;
        }
        if (key.Pressed && !key.Echo) _keyAt[name] = now;
    }

    /// <summary>
    /// The third doubled press says so on screen, once: the menus cope, but in play the same layout also sends
    /// mouse clicks, mouse movement and other keys with the controller, which only a gamepad layout in Steam stops.
    /// </summary>
    private void NoteMirror()
    {
        if (++_mirrored != 3) return;
        GD.Print("SVM_PAD Steam Input is sending keys together with the controller's buttons (a keyboard and mouse layout)");
        ShowNotice("Steam is sending keyboard and mouse input with the controller. Start the game from Steam with a gamepad layout.");
        _noticeLeft = 12.0;
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventKey typed)
        {
            DropMirroredKey(typed);
            return;
        }
        if (@event is not (InputEventJoypadButton or InputEventJoypadMotion))
        {
            return;
        }
        if (@event is InputEventJoypadButton { Pressed: true }) s_buttonAt = Time.GetTicksMsec();
        Adopt(@event);
        if (ListeningForBinding)
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
        // Only the controller in use moves the menus (a mirrored second device would repeat every step).
        if (@event.Device == s_lastDevice && (@event is InputEventJoypadMotion { Axis: JoyAxis.LeftX or JoyAxis.LeftY } ||
            @event is InputEventJoypadButton { ButtonIndex: JoyButton.DpadUp or JoyButton.DpadDown or JoyButton.DpadLeft or JoyButton.DpadRight or
                JoyButton.A or JoyButton.B or JoyButton.Back or JoyButton.Start or JoyButton.LeftShoulder or JoyButton.RightShoulder }))
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
        if (command is not null)
        {
            ulong at = Time.GetTicksMsec();
            _padAt[command] = at;
            // The same press already arrived as a key and moved the menu.
            if (_keyAt.TryGetValue(command, out ulong keyAt) && at - keyAt <= MirrorMs)
            {
                command = null;
                NoteMirror();
            }
        }
        Navigate(command);
    }

    private string? Button(InputEventJoypadButton button, double now)
    {
        string? key = button.ButtonIndex switch
        {
            JoyButton.DpadUp => "Up", JoyButton.DpadDown => "Down", JoyButton.DpadLeft => "Left", JoyButton.DpadRight => "Right",
            JoyButton.A => "Enter", JoyButton.B or JoyButton.Back or JoyButton.Start => "Escape",
            JoyButton.LeftShoulder => PaneLeft, JoyButton.RightShoulder => PaneRight, _ => null,
        };
        return key is null ? null : _navigation.Button(key, button.Pressed, now);
    }

    /// <summary>
    /// Where a menu key goes: the dialog or popup that has the focus (controller events reach only the main
    /// window, so its focused control would get them otherwise), else the main window.
    /// </summary>
    private Viewport FocusedView()
    {
        Window? focused = null;
        foreach (Node node in GetTree().Root.FindChildren("*", nameof(Window), true, false))
        {
            if (node is Window { Visible: true } window && window.HasFocus()) focused = window;
        }

        return focused ?? GetViewport();
    }

    /// <summary>The shoulder buttons: jump to the column (scroll list) on the left or right.</summary>
    private const string PaneLeft = "PaneLeft", PaneRight = "PaneRight";

    private void Navigate(string? key)
    {
        if (key is null) return;
        Viewport view = FocusedView();
        Control? focus = view.GuiGetFocusOwner();
        if (key is PaneLeft or PaneRight)
        {
            if (focus is not null) PadFocusPanes.Step(focus, key == PaneLeft ? -1 : 1);
            return;
        }
        // A popup menu takes the keys itself; anything else gets a focused control first.
        if (key != "Escape" && focus is null && view is not PopupMenu && FirstFocusable(view) is { } first)
        {
            first.GrabFocus();
            return;
        }
        // Left or right past the edge of a list goes to the neighbouring list's selected or last used entry, not
        // to whatever control happens to sit at the same height.
        if (key is "Left" or "Right" && focus is not null && PadFocusPanes.LeavesPane(focus, key == "Left") &&
            PadFocusPanes.Step(focus, key == "Left" ? -1 : 1))
        {
            return;
        }
        if (GodotKeys.ToEvent(key) is not InputEventKey press) return;
        press.Keycode = press.PhysicalKeycode;
        press.Pressed = true;
        _pushing = true;
        try
        {
            BindingInput.Current?.Resolve(press);
            view.PushInput(press);
            var release = new InputEventKey { Keycode = press.Keycode, PhysicalKeycode = press.PhysicalKeycode };
            BindingInput.Current?.Resolve(release);
            if (GodotObject.IsInstanceValid(view) && view.IsInsideTree()) view.PushInput(release);
        }
        finally
        {
            _pushing = false;
        }
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
        GetViewport().GuiFocusChanged += PadFocusPanes.Remember;
        // One line per device: a controller listed twice here is being shown to the game as two devices.
        foreach (int device in Input.GetConnectedJoypads()) GD.Print("SVM_PAD at start: " + Describe(device));
        // A controller already plugged in at start gets the prompt too (no connection event comes for it).
        Callable.From(PromptIfWanted).CallDeferred();
    }

    public override void _ExitTree()
    {
        Input.JoyConnectionChanged -= OnJoyConnectionChanged;
        GetViewport().GuiFocusChanged -= PadFocusPanes.Remember;
    }

    private static string Describe(int device) => $"device {device}, {Input.GetJoyName(device)}, guid {Input.GetJoyGuid(device)}";

    public override void _Process(double delta)
    {
        AdoptCandidate();
        if (!SettingsStore.Current.Pad.Enabled || GamepadInput.GameplayActive || ListeningForBinding ||
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
        GD.Print("SVM_PAD " + NoticeText(connected, name) + (connected ? " (" + Describe((int)device) + ")" : " (device " + device + ")"));
        if (!connected && device == s_candidate) s_candidate = -1;
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
