using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using OpenScctGame.Hud;
using OpenScctGame.Play.Session;
using OpenScctGame.UI;

namespace OpenScctGame.Options;

/// <summary>
/// The options (rl:qol.settingsui): player name and pages for keys (the spy and merc layouts, click a key and press
/// the new one, Escape cancels), mouse (profile sensitivity, inversions, the wheel, Reloaded's per-side and per-view
/// look with its clutches and their keys), controller (Reloaded's SDL defaults, vibration, the pad look groups),
/// display (mode, monitor, resolution, window position, vsync, merc FOV cap, the per-side frame caps, render scale,
/// texture filtering, shadows, input lag, power throttling), audio (master, effects, music, objective sounds), voice
/// (Reloaded's voice page: OptionsMenu.Voice.cs) and HUD (scoreboard, ping, frame-rate and network readouts, location,
/// hack icon opacity, distance unit). Save writes
/// <c>user://settings.json</c> and applies at once; Back discards. From the main menu it is a screen; in play (F12)
/// an overlay.
/// </summary>
public partial class OptionsMenu : Control
{
    private readonly Dictionary<string, Button> _keyButtons = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Control> _controls = new(StringComparer.Ordinal);
    private GameSettings _edit = SettingsStore.Current.Clone();
    private KeyBindingsModel? _keys;
    private GridContainer? _keyGrid;
    private Label? _keySource;
    private Label? _note;
    private string? _listening;
    private TabContainer? _tabs;

    public OptionsMenu()
    {
        Name = "Options";
    }

    /// <summary>Shown over the game: Back and Save close it instead of returning to the main menu.</summary>
    public bool Overlay { get; set; }

    /// <summary>Raised when an overlay closes.</summary>
    public event Action? Closed;

    /// <summary>The settings being edited (a copy of the saved ones).</summary>
    public GameSettings Editing => _edit;

    /// <summary>Key buttons of the shown layout by UE2 key name.</summary>
    public IReadOnlyDictionary<string, Button> KeyButtons => _keyButtons;

    /// <summary>Every setting's control by settings key (for tests).</summary>
    public IReadOnlyDictionary<string, Control> Controls => _controls;

    public TabContainer? Tabs => _tabs;

    public KeyBindingsModel? Keys => _keys;

    /// <summary>The key whose command waits for a new key, or null.</summary>
    public string? Listening => _listening;

    /// <summary>Replaces the current scene with the options screen (the main menu's Options).</summary>
    public static OptionsMenu Show(SceneTree tree)
    {
        ArgumentNullException.ThrowIfNull(tree);
        var screen = new OptionsMenu();
        Node? previous = tree.CurrentScene;
        tree.Root.AddChild(screen);
        tree.CurrentScene = screen;
        previous?.QueueFree();
        return screen;
    }

    public override void _Ready() => Build();

    public override void _Process(double delta)
    {
        UpdateVoicePage();
        TickKeep();
    }

    public override void _ExitTree()
    {
        StopMeter();
        _micTest?.Dispose();
        _micTest = null;
    }

    private void Build()
    {
        Theme = UiStyle.Theme;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        if (Overlay)
        {
            var dim = new ColorRect { Name = "Dim", Color = new Color(0f, 0f, 0f, 0.8f) };
            dim.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            AddChild(dim);
        }
        else
        {
            AddChild(UiStyle.Backdrop());
        }

        AddChild(BuildLabel.Corner());

        VBoxContainer column = Frame();
        column.AddChild(UiStyle.Title("OPTIONS", HudK.SpyColour));
        GridContainer player = Grid("Player");
        if (OpenScctGame.Online.AccountSettings.Current.AccountName is { } accountName)
        {
            // A signed-in player's name is the account's; it changes on the Account page.
            Row(player, "Player name", "player_name", new LineEdit
            {
                Text = accountName, Editable = false, SizeFlagsHorizontal = SizeFlags.ExpandFill,
                TooltipText = "Your account name; change it on the Account page.",
            }, _ => { });
            column.AddChild(player);
            column.AddChild(new Label
            {
                Name = "AccountNameNote", Text = "Your account name; change it on the Account page.", Modulate = UiStyle.Dim,
            });
        }
        else
        {
            Row(player, "Player name", "player_name", new LineEdit
            {
                Text = _edit.PlayerName, MaxLength = 24, SizeFlagsHorizontal = SizeFlags.ExpandFill,
            }, c => _edit.PlayerName = ((LineEdit)c).Text);
            column.AddChild(player);
        }

        _tabs = new TabContainer { Name = "Tabs", CustomMinimumSize = new Vector2(0f, 440f), SizeFlagsVertical = SizeFlags.ExpandFill };
        _tabs.AddChild(KeysPage());
        _tabs.AddChild(MousePage());
        _tabs.AddChild(PadPage());
        _tabs.AddChild(DisplayPage());
        _tabs.AddChild(AudioPage());
        _tabs.AddChild(VoicePage());
        _tabs.AddChild(HudPage());
        column.AddChild(_tabs);

        _note = new Label { Name = "Note", Modulate = UiStyle.Dim, Text = _edit.LoadNote, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        column.AddChild(_note);
        var buttons = new HBoxContainer { Name = "Buttons", Alignment = BoxContainer.AlignmentMode.Center };
        foreach (Button b in new[]
                 {
                     UiStyle.MenuButton("Save", () => Save(), "Save"),
                     UiStyle.MenuButton("Defaults", Defaults, "Defaults"),
                     UiStyle.MenuButton("Back", Back, "Back"),
                 })
        {
            b.CustomMinimumSize = new Vector2(200f, 44f);
            buttons.AddChild(b);
        }

        column.AddChild(buttons);
        LoadLayout(PlaySide.Spy);
    }

    /// <summary>
    /// The options' frame: most of the screen (so the pages are not crammed), on a solid panel so a busy screen
    /// behind (the lobby) does not show through. Returns the column the title, pages and buttons go in.
    /// </summary>
    private VBoxContainer Frame()
    {
        var frame = new Control
        {
            Name = "Frame", AnchorLeft = 0.08f, AnchorRight = 0.92f, AnchorTop = 0.04f, AnchorBottom = 0.96f,
            CustomMinimumSize = new Vector2(860f, 0f),
        };
        AddChild(frame);
        var fill = new ColorRect { Name = "Fill", Color = UiStyle.Background with { A = 0.96f }, MouseFilter = MouseFilterEnum.Ignore };
        fill.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        frame.AddChild(fill);
        var panel = new PanelContainer { Name = "Panel" };
        panel.AddThemeStyleboxOverride("panel", UiStyle.MenuPanelBox());
        panel.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        frame.AddChild(panel);
        var margin = new MarginContainer { Name = "Margin" };
        foreach (string side in new[] { "margin_left", "margin_right" })
        {
            margin.AddThemeConstantOverride(side, 24);
        }

        margin.AddThemeConstantOverride("margin_top", 8);
        margin.AddThemeConstantOverride("margin_bottom", 16);
        panel.AddChild(margin);
        var column = new VBoxContainer { Name = "Column" };
        column.AddThemeConstantOverride("separation", 10);
        margin.AddChild(column);
        return column;
    }

    // ---- pages ---------------------------------------------------------------------------------------------

    private Control KeysPage()
    {
        var page = new VBoxContainer { Name = "Keys" };
        GridContainer grid = Grid("Layout");
        var side = new OptionButton { Name = "Side" };
        side.AddItem("Spy", (int)PlaySide.Spy);
        side.AddItem("Mercenary", (int)PlaySide.Merc);
        side.ItemSelected += index => LoadLayout((PlaySide)side.GetItemId((int)index));
        grid.AddChild(new Label { Text = "Key layout" });
        grid.AddChild(side);
        _controls["key_layout"] = side;
        grid.AddChild(new Control());
        // Reloaded's split layouts (rl:input.keyboard); off, both sides play the spy's keys. Takes effect on Save.
        Check(grid, "Separate spy and mercenary layouts", "use_split_spy_merc_keyboard", _edit.SplitKeyboard, v => _edit.SplitKeyboard = v);
        page.AddChild(grid);
        _keySource = new Label { Name = "Source", Modulate = UiStyle.Dim, AutowrapMode = TextServer.AutowrapMode.Arbitrary };
        page.AddChild(_keySource);
        var scroll = new ScrollContainer { Name = "Bindings", CustomMinimumSize = new Vector2(0f, 300f), SizeFlagsVertical = SizeFlags.ExpandFill };
        // Two side-by-side lists (label, key, gap, label, key) so the rows use the page's width.
        _keyGrid = new GridContainer { Columns = 5, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _keyGrid.AddThemeConstantOverride("h_separation", 24);
        scroll.AddChild(_keyGrid);
        page.AddChild(scroll);
        return page;
    }

    private Control MousePage()
    {
        GridContainer g = Grid("Mouse");
        Slider(g, "Mouse sensitivity", "mouse_sensitivity", GameSettings.MinSensitivity, GameSettings.MaxSensitivity, 1, _edit.MouseSensitivity,
            v => _edit.MouseSensitivity = v, "0");
        Check(g, "Invert mouse", "invert_y", _edit.InvertY, v => _edit.InvertY = v);
        Check(g, "Invert X axis (spy)", "invert_x_spy", _edit.InvertXSpy, v => _edit.InvertXSpy = v);
        Check(g, "Shocker lock-on snap when drawing (spy)", "shocker_snap", _edit.ShockerSnap, v => _edit.ShockerSnap = v);
        Choice(g, "Mouse wheel", "mouse_wheel", GameSettings.WheelModes, _edit.MouseWheel, v => _edit.MouseWheel = v,
            m => m == "Gadget" ? "Changes gadget" : "Walk and run");
        LookRows(g, "controls_spy_kbm", () => _edit.SpyKbm, merc: false);
        LookRows(g, "controls_merc_kbm", () => _edit.MercKbm, merc: true);
        ClutchKeyRow(g, "Spy and mercenary clutch key", "clutch_one_key", () => _edit.ClutchOneKey, v => _edit.ClutchOneKey = v);
        ClutchKeyRow(g, "Mercenary second clutch key", "clutch_two_key", () => _edit.ClutchTwoKey, v => _edit.ClutchTwoKey = v);
        return Page("Mouse", g);
    }

    /// <summary>
    /// One side's look group (rl:input.sens and rl:input.clutch): the looks, the scope or binoculars, the camera
    /// views, the vertical scales and the clutches.
    /// </summary>
    private void LookRows(GridContainer g, string key, Func<LookSettings> of, bool merc, bool skipLooks = false)
    {
        // The edited settings are read when changed: Save swaps them for a fresh copy.
        LookSettings l = of();
        string side = merc ? "Mercenary" : "Spy";
        if (!skipLooks)
        {
            if (merc)
            {
                Slider(g, "Mercenary look", key + ".look", 0.1f, 4f, 0.05f, l.Look, v => of().Look = v);
            }
            else
            {
                Slider(g, "Spy free look (gun holstered)", key + ".free_look", 0.1f, 4f, 0.05f, l.FreeLook, v => of().FreeLook = v);
                Slider(g, "Spy gun look (gun drawn)", key + ".look", 0.1f, 4f, 0.05f, l.Look, v => of().Look = v);
            }
        }

        Slider(g, merc ? "Mercenary sniper scope" : "Spy binoculars", key + ".scope", 0.1f, 4f, 0.05f, l.Scope, v => of().Scope = v);
        if (merc)
        {
            // 2.45 turns the scope up and down as fast as sideways; 1 is the stock (legacy) speed.
            Slider(g, "Mercenary scope vertical (1 = stock)", key + ".scope_vertical", 0.5f, 4f, 0.05f, l.ScopeVertical, v => of().ScopeVertical = v);
        }

        Slider(g, side + " vertical", key + ".vertical", 0.1f, 8f, 0.05f, l.Vertical, v => of().Vertical = v);
        Slider(g, side + (merc ? " camera views" : " sticky camera and Cam Net"), key + ".cam", 0.1f, 4f, 0.05f, l.Cam, v => of().Cam = v);
        Check(g, side + " clutch", key + ".clutch_one_enabled", l.ClutchOneEnabled, v => of().ClutchOneEnabled = v);
        Slider(g, side + " clutch multiplier", key + ".clutch_one", 0.1f, 4f, 0.05f, l.ClutchOne > 0f ? l.ClutchOne : 1f, v => of().ClutchOne = v);
        if (merc)
        {
            Check(g, "Mercenary second clutch", key + ".clutch_two_enabled", l.ClutchTwoEnabled, v => of().ClutchTwoEnabled = v);
            Slider(g, "Mercenary second clutch multiplier", key + ".clutch_two", 0.1f, 4f, 0.05f, l.ClutchTwo > 0f ? l.ClutchTwo : 1f, v => of().ClutchTwo = v);
        }
    }

    /// <summary>The clutch whose key waits for a press, or null.</summary>
    private (Button Button, Action<string> Set)? _listeningClutch;

    /// <summary>A key or mouse button held for a clutch: click, then press it (Escape clears it).</summary>
    private void ClutchKeyRow(GridContainer g, string label, string key, Func<string> current, Action<string> set)
    {
        var button = new Button { Name = Safe(key), Text = current().Length == 0 ? "(none)" : current(), CustomMinimumSize = new Vector2(220f, 30f) };
        button.Pressed += () =>
        {
            button.Text = "press a key...";
            _listeningClutch = (button, v =>
            {
                set(v);
                button.Text = v.Length == 0 ? "(none)" : v;
            });
        };
        g.AddChild(new Label { Text = label, CustomMinimumSize = new Vector2(320f, 0f) });
        g.AddChild(button);
        g.AddChild(new Control());
        _controls[key] = button;
    }

    private Control PadPage()
    {
        GridContainer g = Grid("Controller");
        PadSettings p = _edit.Pad;
        // Set here, the first-run question (PadMenus) is never asked.
        Check(g, "Native controller support", "controller.enabled", p.Enabled, v =>
        {
            p.Enabled = v;
            p.Prompted = true;
        });
        Check(g, "Stick movement", "controller.move", p.Move, v => p.Move = v);
        Check(g, "Stick aiming", "controller.aim", p.Aim, v => p.Aim = v);
        Check(g, "Button binds", "controller.binds", p.Binds, v => p.Binds = v);
        Slider(g, "Left stick deadzone", "controller.deadzone_left", 0f, 0.9f, 0.01f, p.DeadzoneLeft, v => p.DeadzoneLeft = v);
        Slider(g, "Right stick deadzone", "controller.deadzone_right", 0f, 0.9f, 0.01f, p.DeadzoneRight, v => p.DeadzoneRight = v);
        Slider(g, "Vertical aim scale", "controller.vertical_scale", 0.1f, 3f, 0.05f, p.VerticalScale, v => p.VerticalScale = v);
        Slider(g, "Spy run threshold", "controller.run_threshold", 0.1f, 1f, 0.01f, p.RunThreshold, v => p.RunThreshold = v);
        Check(g, "Analog slow walk (a part push walks slower)", "controller.slow_walk", p.SlowWalk, v => p.SlowWalk = v);
        Slider(g, "Look speed", "controller.look_speed", 100f, 3000f, 10f, p.LookSpeed, v => p.LookSpeed = v, "0");
        Check(g, "Spy eight-way movement", "controller.spy_move8", p.SpyMove8, v => p.SpyMove8 = v);
        Check(g, "Mercenary eight-way movement", "controller.merc_move8", p.MercMove8, v => p.MercMove8 = v);
        Check(g, "Vibration", "controller.rumble", p.Rumble, v => p.Rumble = v);
        Slider(g, "Vibration strength", "controller.rumble_strength", 0f, 100f, 1f, p.RumbleStrength * 100f, v => p.RumbleStrength = v / 100f, "0");
        g.AddChild(new Label { Text = "Test vibration", CustomMinimumSize = new Vector2(320f, 0f) });
        // Reloaded's trigger test: the blast's rumble (both motors, 0.35 s) at the strength set above.
        g.AddChild(UiStyle.MenuButton("Test", () => PadRumble.Play(PadMenus.CurrentDevice(), new RumbleShape(7, 65535, 65535, 0.35f), _edit.Pad.RumbleStrength),
            "RumbleTest"));
        g.AddChild(new Control());
        Slider(g, "Spy free look", "controls_spy_pad.free_look", 0.1f, 4f, 0.05f, _edit.SpyPad.FreeLook, v => _edit.SpyPad.FreeLook = v);
        Slider(g, "Spy gun look", "controls_spy_pad.look", 0.1f, 4f, 0.05f, _edit.SpyPad.Look, v => _edit.SpyPad.Look = v);
        Choice(g, "Spy look response", "controls_spy_pad.look_response", GameSettings.Responses, _edit.SpyPad.LookResponse, v => _edit.SpyPad.LookResponse = v);
        LookRows(g, "controls_spy_pad", () => _edit.SpyPad, merc: false, skipLooks: true);
        Slider(g, "Mercenary look", "controls_merc_pad.look", 0.1f, 4f, 0.05f, _edit.MercPad.Look, v => _edit.MercPad.Look = v);
        Choice(g, "Mercenary look response", "controls_merc_pad.look_response", GameSettings.Responses, _edit.MercPad.LookResponse, v => _edit.MercPad.LookResponse = v);
        LookRows(g, "controls_merc_pad", () => _edit.MercPad, merc: true, skipLooks: true);
        Godot.Collections.Array<int> pads = Input.GetConnectedJoypads();
        string names = pads.Count == 0 ? "No controller connected" : string.Join(", ", System.Linq.Enumerable.Select(pads, i => Input.GetJoyName(i)));
        g.AddChild(new Label { Text = "Detected" });
        g.AddChild(new Label { Name = "Pads", Text = names, Modulate = UiStyle.Dim });
        g.AddChild(new Control());

        var content = new VBoxContainer { Name = "ControllerContent", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        content.AddChild(g);
        content.AddChild(new HSeparator());
        GridContainer layout = Grid("PadLayout");
        var side = new OptionButton { Name = "PadSide", CustomMinimumSize = new Vector2(320f, 0f), SizeFlagsHorizontal = SizeFlags.ExpandFill };
        side.AddItem("Spy", (int)PlaySide.Spy);
        side.AddItem("Mercenary", (int)PlaySide.Merc);
        side.Selected = _padSide == PlaySide.Merc ? 1 : 0;
        side.ItemSelected += index =>
        {
            _padSide = (PlaySide)side.GetItemId((int)index);
            RebuildPadButtons();
        };
        layout.AddChild(new Label { Text = "Button layout", CustomMinimumSize = new Vector2(320f, 0f) });
        layout.AddChild(side);
        layout.AddChild(UiStyle.MenuButton("Reset buttons", ResetPadButtons, "ResetPadButtons"));
        _controls["controller.layout_side"] = side;
        content.AddChild(layout);
        _padGrid = Grid("PadButtons");
        content.AddChild(_padGrid);
        RebuildPadButtons();
        return Page("Controller", content);
    }

    /// <summary>The side whose controller buttons the Controller page shows.</summary>
    private PlaySide _padSide = PlaySide.Spy;
    private GridContainer? _padGrid;

    /// <summary>Pad control names as the options list them.</summary>
    private static string PadControlLabel(string control) => control switch
    {
        "LB" => "Left bumper",
        "RB" => "Right bumper",
        "LT" => "Left trigger",
        "RT" => "Right trigger",
        "LS" => "Left stick click",
        "RS" => "Right stick click",
        "DpadUp" => "D-pad up",
        "DpadDown" => "D-pad down",
        "DpadLeft" => "D-pad left",
        "DpadRight" => "D-pad right",
        "Back" => "Back / View",
        "Start" => "Start / Menu",
        _ => control + " button",
    };

    /// <summary>One row per pad control: what it does on the shown side (saved with Save, like the other settings).</summary>
    private void RebuildPadButtons()
    {
        if (_padGrid is null)
        {
            return;
        }

        foreach (Node child in _padGrid.GetChildren())
        {
            _padGrid.RemoveChild(child);
            child.QueueFree();
        }

        bool merc = _padSide == PlaySide.Merc;
        Dictionary<string, string> buttons = _edit.Pad.ButtonsOf(merc);
        var model = new KeyBindingsModel(_padSide);
        List<(string Action, string Label)> choices = model.PadChoices();
        List<(string Action, string Label)> every = model.PadChoices(otherTeam: true);
        foreach (string control in PadSettings.Controls)
        {
            string current = buttons.TryGetValue(control, out string? a) ? a : "";
            var items = new List<(string Action, string Label)>(choices);
            int selected = items.FindIndex(c => string.Equals(c.Action, current, StringComparison.OrdinalIgnoreCase));
            if (selected < 0)
            {
                // A saved action this team does not use, or one its layout has no key for: kept, and shown as such.
                int other = every.FindIndex(c => string.Equals(c.Action, current, StringComparison.OrdinalIgnoreCase));
                items.Add((current, other >= 0 ? every[other].Label + " (other team only)" : current + " (no key in this layout)"));
                selected = items.Count - 1;
            }

            var option = new OptionButton
            {
                Name = "Pad_" + control, ClipText = true, CustomMinimumSize = new Vector2(320f, 0f), SizeFlagsHorizontal = SizeFlags.ExpandFill,
            };
            for (int i = 0; i < items.Count; i++)
            {
                option.AddItem(items[i].Label, i);
            }

            option.Selected = selected;
            string c = control;
            // The edited settings are read when chosen: Save swaps them for a fresh copy.
            option.ItemSelected += index => _edit.Pad.ButtonsOf(merc)[c] = items[(int)index].Action;
            _padGrid.AddChild(new Label { Text = PadControlLabel(control), CustomMinimumSize = new Vector2(320f, 0f) });
            _padGrid.AddChild(option);
            _padGrid.AddChild(new Control());
            _controls["controller.buttons." + control] = option;
        }
    }

    /// <summary>The shown side's controller buttons back to the defaults (saved with Save).</summary>
    private void ResetPadButtons()
    {
        if (_padSide == PlaySide.Merc)
        {
            _edit.Pad.MercButtons = PadSettings.DefaultButtons(merc: true);
        }
        else
        {
            _edit.Pad.SpyButtons = PadSettings.DefaultButtons();
        }

        RebuildPadButtons();
    }

    private Control DisplayPage()
    {
        GridContainer g = Grid("Display");
        Choice(g, "Display mode", "display_mode", GameSettings.DisplayModes, _edit.DisplayMode, v => _edit.DisplayMode = v, DisplayModeLabel);
        int screens = Math.Max(1, DisplayServer.GetScreenCount());
        var monitors = new string[screens];
        for (int i = 0; i < screens; i++)
        {
            monitors[i] = (i + 1).ToString(CultureInfo.InvariantCulture);
        }

        Choice(g, "Monitor", "display_monitor", monitors, Math.Clamp(_edit.Monitor, 1, screens).ToString(CultureInfo.InvariantCulture),
            v => _edit.Monitor = int.Parse(v, CultureInfo.InvariantCulture));
        Choice(g, "Window size", "resolution", Resolutions(), _edit.Resolution.Length == 0 ? DesktopLabel : _edit.Resolution,
            v => _edit.Resolution = v == DesktopLabel ? "" : v);
        Check(g, "Vertical sync", "vsync", _edit.VSync, v => _edit.VSync = v);
        Check(g, "Widescreen (Hor+ view)", "widescreen", _edit.Widescreen, v => _edit.Widescreen = v);
        Check(g, "Widescreen HUD (not stretched)", "widescreen_hud", _edit.WidescreenHud, v => _edit.WidescreenHud = v);
        Check(g, "Small Enhanced Reality icons in widescreen", "ws_er_small", _edit.WideErSmall, v => _edit.WideErSmall = v);
        Slider(g, "Mercenary FOV cap (ws_fov)", "ws_fov", GameSettings.MinMercFov, GameSettings.MaxMercFov, 1, _edit.MercFovCap, v => _edit.MercFovCap = v, "0");
        // Max frame rate (Options/DisplayFrameCap): Monitor (the window's screen rate), Unlimited or 30 to 240.
        double hz = SettingsApplier.MonitorHz;
        Choice(g, "Max frame rate as spy", "fps_spy", DisplayFrameCap.Choices(_edit.FpsSpy, hz), DisplayFrameCap.Label(_edit.FpsSpy, hz),
            v => _edit.FpsSpy = DisplayFrameCap.Parse(v) ?? _edit.FpsSpy);
        Choice(g, "Max frame rate as mercenary", "fps_merc", DisplayFrameCap.Choices(_edit.FpsMerc, hz), DisplayFrameCap.Label(_edit.FpsMerc, hz),
            v => _edit.FpsMerc = DisplayFrameCap.Parse(v) ?? _edit.FpsMerc);
        Slider(g, "Render scale", "resolution_scale", 0.5f, 1f, 0.05f, _edit.RenderScale, v => _edit.RenderScale = v);
        Choice(g, "Window position (windowed)", "window_position", GameSettings.WindowPositions, _edit.WindowPosition, v => _edit.WindowPosition = v,
            w => w switch { "Default" => "Top left", "Stretched" => "Fill the screen", _ => w });
        Choice(g, "Texture filtering", "filter_level", ["0", "2", "4", "8", "16"], _edit.FilterLevel.ToString(CultureInfo.InvariantCulture),
            v => _edit.FilterLevel = int.Parse(v, CultureInfo.InvariantCulture), v => v == "0" ? "Trilinear (off)" : v + "x anisotropic");
        Check(g, "Anti-aliasing: MSAA", "msaa", _edit.Msaa, v => _edit.Msaa = v);
        Choice(g, "MSAA samples", "msaa_samples", ["2", "4", "8"], _edit.MsaaSamples.ToString(CultureInfo.InvariantCulture),
            v => _edit.MsaaSamples = int.Parse(v, CultureInfo.InvariantCulture), v => v + "x");
        Check(g, "Anti-aliasing: SMAA", "smaa", _edit.Smaa, v => _edit.Smaa = v);
        Slider(g, "Shadow quality (next map)", "shadow_quality", 0.5f, 16f, 0.5f, _edit.ShadowQuality, v => _edit.ShadowQuality = v, "0.0");
        Check(g, "Smooth shadow edges", "shadow_edge_smoothing", _edit.ShadowEdgeSmoothing, v => _edit.ShadowEdgeSmoothing = v);

        // Video_Settings' Post-Render Quality (Normal, High) and its Brightness Configuration button (GUI.Mire).
        Choice(g, "Post-render quality", "post_quality", GameSettings.PostQualities, GameSettings.PostQualities[Math.Clamp(_edit.PostQuality, 0, 1)],
            v => _edit.PostQuality = Array.IndexOf(GameSettings.PostQualities, v));
        g.AddChild(new Label { Text = "Brightness", CustomMinimumSize = new Vector2(320f, 0f) });
        g.AddChild(UiStyle.MenuButton("Brightness configuration", OpenBrightnessTest, "BrightnessConfiguration"));
        g.AddChild(new Control());
        Choice(g, "Reduce input lag (restart)", "low_latency_mode", GameSettings.LatencyModes, _edit.LowLatencyMode, v => _edit.LowLatencyMode = v);
        if (OperatingSystem.IsWindows())
        {
            Check(g, "Stop Windows power throttling (restart)", "throttle_stop", _edit.ThrottleStop, v => _edit.ThrottleStop = v);
        }

        return Page("Display", g);
    }

    private const string DesktopLabel = "Desktop";

    private static string[] Resolutions()
    {
        var list = new List<string> { DesktopLabel };
        Vector2I desktop = DisplayServer.GetName() == "headless" ? new Vector2I(1920, 1080) : DisplayServer.ScreenGetSize();
        foreach ((int w, int h) in new[] { (1280, 720), (1366, 768), (1600, 900), (1920, 1080), (2560, 1440), (3840, 2160), (1024, 768), (1600, 1200) })
        {
            if (w <= desktop.X && h <= desktop.Y)
            {
                list.Add(GameSettings.FormatResolution(w, h));
            }
        }

        return list.ToArray();
    }

    private Control AudioPage()
    {
        GridContainer g = Grid("Audio");
        Slider(g, "Master", "master_volume", 0f, 100f, 1f, _edit.MasterVolume * 100f, v => _edit.MasterVolume = v / 100f, "0");
        Slider(g, "Effects", "sound_volume", 0f, 100f, 1f, _edit.SoundVolume * 100f, v => _edit.SoundVolume = v / 100f, "0");
        Slider(g, "Music", "music_volume", 0f, 100f, 1f, _edit.MusicVolume * 100f, v => _edit.MusicVolume = v / 100f, "0");
        Check(g, "Objective sounds", "objective_sounds", _edit.ObjectiveSounds, v => _edit.ObjectiveSounds = v);
        return Page("Audio", g);
    }

    private Control HudPage()
    {
        GridContainer g = Grid("HUD");
        Check(g, "Connected players list on (F10)", "show_scoreboard", _edit.ShowScoreboard, v => _edit.ShowScoreboard = v);
        Check(g, "Ping in the players list", "show_ping", _edit.ShowPing, v => _edit.ShowPing = v);
        Choice(g, "Split the players list at team size", "scoreboard_split_team_size", ["1", "2", "3", "4"],
            _edit.ScoreboardSplitTeamSize.ToString(CultureInfo.InvariantCulture), v => _edit.ScoreboardSplitTeamSize = int.Parse(v, CultureInfo.InvariantCulture));
        Check(g, "Frame rate readout", "show_fps", _edit.ShowFps, v => _edit.ShowFps = v);
        Check(g, "Network readout (ping, corrections; a host sees its players' best and worst ping)", "show_net", _edit.ShowNet,
            v => _edit.ShowNet = v);
        Check(g, "Packet loss in the network readout", "show_net_loss", _edit.ShowNetLoss, v => _edit.ShowNetLoss = v);
        Check(g, "Fire prediction (your bullet impacts show at once online)", "fire_prediction", _edit.FirePrediction,
            v => _edit.FirePrediction = v);
        Check(g, "Location (the place you are in)", "show_location", _edit.ShowLocation, v => _edit.ShowLocation = v);
        Check(g, "Distances in feet", "hud_feet", _edit.HudFeet, v => _edit.HudFeet = v);
        // Reloaded's settings page offers 20 % to 100 %; stored 0 to 255.
        Slider(g, "Objective icon opacity while hacking (%)", "hack_alpha", 20f, 100f, 1f, Math.Max(20f, _edit.HackAlpha * 100f / 255f),
            v => _edit.HackAlpha = (int)MathF.Round(v * 255f / 100f), "0");
        Check(g, "Discord status (shows your game, lets friends join)", "discord_presence", _edit.DiscordPresence, v => _edit.DiscordPresence = v);
        return Page("HUD", g);
    }

    // ---- building blocks -----------------------------------------------------------------------------------

    private static GridContainer Grid(string name)
    {
        var grid = new GridContainer { Name = name, Columns = 3, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        grid.AddThemeConstantOverride("h_separation", 16);
        grid.AddThemeConstantOverride("v_separation", 6);
        return grid;
    }

    private static Control Page(string name, Control content)
    {
        // Pages scroll up and down only: a horizontal bar sat over the last row (the Voice page's Test my microphone).
        var scroll = new ScrollContainer { Name = name, SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        scroll.AddChild(content);
        return scroll;
    }

    private void Row(GridContainer grid, string label, string key, Control control, Action<Control> changed)
    {
        control.Name = Safe(key);
        grid.AddChild(new Label { Text = label, CustomMinimumSize = new Vector2(320f, 0f) });
        grid.AddChild(control);
        grid.AddChild(new Control());
        _controls[key] = control;
        if (control is LineEdit edit)
        {
            edit.TextChanged += _ => changed(control);
        }
    }

    private void Slider(GridContainer grid, string label, string key, float min, float max, float step, float value, Action<float> changed,
        string format = "0.00")
    {
        var slider = new HSlider
        {
            Name = Safe(key), MinValue = min, MaxValue = max, Step = step, Value = value,
            CustomMinimumSize = new Vector2(320f, 24f), SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        var shown = new Label { Text = value.ToString(format, CultureInfo.InvariantCulture), CustomMinimumSize = new Vector2(70f, 0f) };
        void Apply(double v)
        {
            shown.Text = ((float)v).ToString(format, CultureInfo.InvariantCulture);
            changed((float)v);
        }

        // A click on the track moves the grabber without value_changed (Godot's Slider blocks it on press).
        slider.ValueChanged += Apply;
        slider.DragEnded += _ => Apply(slider.Value);
        grid.AddChild(new Label { Text = label, CustomMinimumSize = new Vector2(320f, 0f) });
        grid.AddChild(slider);
        grid.AddChild(shown);
        _controls[key] = slider;
    }

    private void Check(GridContainer grid, string label, string key, bool value, Action<bool> changed)
    {
        var box = new CheckBox { Name = Safe(key), ButtonPressed = value };
        box.Toggled += v => changed(v);
        grid.AddChild(new Label { Text = label, CustomMinimumSize = new Vector2(320f, 0f) });
        grid.AddChild(box);
        grid.AddChild(new Control());
        _controls[key] = box;
    }

    /// <summary>The words the Display mode list shows for a setting value (the settings file keeps Reloaded's values).</summary>
    private static string DisplayModeLabel(string mode) => mode switch
    {
        "BorderlessFullscreen" => "Borderless fullscreen",
        _ => mode,
    };

    private void Choice(GridContainer grid, string label, string key, string[] items, string value, Action<string> changed, Func<string, string>? itemText = null)
    {
        // Clipped to the column: a long item (a microphone's device name) must not widen the whole page.
        var option = new OptionButton
        {
            Name = Safe(key), ClipText = true, CustomMinimumSize = new Vector2(320f, 0f), SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        for (int i = 0; i < items.Length; i++)
        {
            option.AddItem(itemText?.Invoke(items[i]) ?? items[i], i);
            option.SetItemTooltip(i, items[i]);
            if (string.Equals(items[i], value, StringComparison.OrdinalIgnoreCase))
            {
                option.Selected = i;
            }
        }

        option.ItemSelected += index => changed(items[(int)index]);
        grid.AddChild(new Label { Text = label, CustomMinimumSize = new Vector2(320f, 0f) });
        grid.AddChild(option);
        grid.AddChild(new Control());
        _controls[key] = option;
    }

    private static string Safe(string key) => key.Replace('.', '_');

    // ---- key layouts ---------------------------------------------------------------------------------------

    /// <summary>Shows the key layout of <paramref name="side"/>.</summary>
    public void LoadLayout(PlaySide side)
    {
        _keys = new KeyBindingsModel(side);
        _listening = null;
        RebuildKeys();
    }

    private void RebuildKeys()
    {
        if (_keyGrid is null || _keys is null)
        {
            return;
        }

        foreach (Node child in _keyGrid.GetChildren())
        {
            _keyGrid.RemoveChild(child);
            child.QueueFree();
        }

        _keyButtons.Clear();
        // The first half of the rows runs down the left list, the rest down the right one.
        var rows = new List<KeyBindingRow>(_keys.Rows());
        int half = (rows.Count + 1) / 2;
        for (int i = 0; i < half; i++)
        {
            AddKeyRow(rows[i]);
            _keyGrid.AddChild(new Control { CustomMinimumSize = new Vector2(48f, 0f) });
            if (i + half < rows.Count)
            {
                AddKeyRow(rows[i + half]);
            }
        }

        if (_keySource is not null)
        {
            // The file's name, not the install's whole path.
            _keySource.Text = "Layout: " + System.IO.Path.GetFileName(_keys.Source) +
                (OpenScctGame.Controls.InputBindings.Shared ? " (shared by both sides)" : string.Empty);
        }
    }

    private void AddKeyRow(KeyBindingRow r)
    {
        _keyGrid!.AddChild(new Label { Text = r.Label, SizeFlagsHorizontal = SizeFlags.ExpandFill, ClipText = true, CustomMinimumSize = new Vector2(200f, 0f) });
        string key = r.Key;
        var button = new Button { Name = key, Text = key, CustomMinimumSize = new Vector2(220f, 30f) };
        button.Pressed += () => Listen(key);
        _keyButtons[key] = button;
        _keyGrid.AddChild(button);
    }

    private void Listen(string key)
    {
        _listening = key;
        if (_keyButtons.TryGetValue(key, out Button? b))
        {
            b.Text = "press a key...";
        }
    }

    public override void _Input(InputEvent @event)
    {
        if (_listeningClutch is { } clutch && @event is InputEventKey { Pressed: true } or InputEventMouseButton { Pressed: true })
        {
            GetViewport().SetInputAsHandled();
            _listeningClutch = null;
            clutch.Set(@event is InputEventKey { Keycode: Key.Escape } ? "" : KeyBindingsModel.KeyOf(@event) ?? "");
            return;
        }

        if (_listening is not null && @event is InputEventKey { Pressed: true } or InputEventMouseButton { Pressed: true })
        {
            string old = _listening;
            GetViewport().SetInputAsHandled();
            if (@event is InputEventKey { Keycode: Key.Escape })
            {
                _listening = null;
                RebuildKeys();
                return;
            }

            if (KeyBindingsModel.KeyOf(@event) is { } key)
            {
                Rebind(old, key);
            }

            return;
        }

        if (Overlay && @event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape or Key.F12 })
        {
            GetViewport().SetInputAsHandled();
            Back();
        }
    }

    /// <summary>Moves the command of <paramref name="oldKey"/> to <paramref name="newKey"/> (saved by the input layer at once).</summary>
    public void Rebind(string oldKey, string newKey)
    {
        _listening = null;
        _keys?.Rebind(oldKey, newKey);
        RebuildKeys();
    }

    // ---- actions -------------------------------------------------------------------------------------------

    /// <summary>Saves the edited settings and puts them into effect.</summary>
    public Error Save()
    {
        GameSettings before = SettingsStore.Current.Clone();
        bool screenChanged = !string.Equals(before.DisplayMode, _edit.DisplayMode, StringComparison.Ordinal) || before.Monitor != _edit.Monitor ||
            !string.Equals(before.Resolution, _edit.Resolution, StringComparison.Ordinal);
        bool splitChanged = _edit.SplitKeyboard != before.SplitKeyboard;
        Error result = SettingsStore.Save(_edit);
        SettingsApplier.ApplyDisplay(_edit, GetViewport());
        SettingsApplier.ApplyAudio(_edit);
        _edit = SettingsStore.Current.Clone();
        if (splitChanged)
        {
            // The layout files the sides play changed: the page and a running round load them again.
            OpenScctGame.Controls.InputBindings.RaiseReboundAll();
            if (_keys is not null)
            {
                LoadLayout(_keys.Side);
            }
        }
        if (_note is not null)
        {
            _note.Text = result == Error.Ok ? "Saved to " + SettingsStore.FilePath : "Could not save the settings";
        }

        if (screenChanged && DisplayServer.GetName() != "headless")
        {
            AskKeepScreen(before);
        }

        return result;
    }

    /// <summary>Seconds the "keep these settings?" question waits before it puts the old screen back.</summary>
    [OpenScct.Provenance.Provenance(OpenScct.Provenance.ProvenanceKind.Decoded, "GUI.Validate_VideoSettings s_Init: TimeTest + 15")]
    public const int KeepSeconds = 15;

    private ConfirmBox? _keep;
    private double _keepEnd;

    /// <summary>
    /// GUI.Validate_VideoSettings: after a change of the screen (display mode, monitor, window size), "Do you want to
    /// keep these settings? Original settings will be reset in: 15" counting down; Yes keeps them, No or the end of
    /// the count puts the old ones back (saved and applied). The original asked after its resolution and quality rows;
    /// the rewrite asks after the rows that can leave the player without a usable screen.
    /// </summary>
    [OpenScct.Provenance.PortOf("uc:GUI.Validate_VideoSettings:s_Init@code", Spec = "spec/game/lobby-and-match-creation.md#front-end-pages")]
    [OpenScct.Provenance.PortOf("uc:GUI.Validate_VideoSettings:YesButtonClick", Spec = "spec/game/lobby-and-match-creation.md#front-end-pages")]
    [OpenScct.Provenance.PortOf("uc:GUI.Validate_VideoSettings:NoButtonClick", Spec = "spec/game/lobby-and-match-creation.md#front-end-pages")]
    private void AskKeepScreen(GameSettings before)
    {
        _keep?.Answer(true);
        _keepEnd = Time.GetTicksMsec() / 1000.0 + KeepSeconds;
        _keep = ConfirmBox.Open(this, new ConfirmBox
        {
            Name = "KeepScreen",
            Question = KeepText(KeepSeconds),
            Yes = () => _keep = null,
            No = () =>
            {
                _keep = null;
                GameSettings back = SettingsStore.Current.Clone();
                back.DisplayMode = before.DisplayMode;
                back.Monitor = before.Monitor;
                back.Resolution = before.Resolution;
                SettingsStore.Save(back);
                SettingsApplier.ApplyDisplay(back, GetViewport());
                _edit.DisplayMode = back.DisplayMode;
                _edit.Monitor = back.Monitor;
                _edit.Resolution = back.Resolution;
                RebuildPages();
                if (_note is not null)
                {
                    _note.Text = "The screen settings were put back.";
                }
            },
        });
    }

    private static string KeepText(int seconds) =>
        OpenScctGame.Tutorials.TutorialText.Lines(OpenScctGame.Tutorials.TutorialText.Caption("Validate_VideoSettings", "KeepText",
            "Do you want to keep these settings?|Original settings will be reset in:")) + " " + seconds.ToString(CultureInfo.InvariantCulture);

    /// <summary>The countdown of the keep question; at 0 it answers No.</summary>
    private void TickKeep()
    {
        if (_keep is null || !IsInstanceValid(_keep))
        {
            _keep = null;
            return;
        }

        int left = (int)Math.Ceiling(_keepEnd - Time.GetTicksMsec() / 1000.0);
        if (left <= 0)
        {
            _keep.Answer(false);
            return;
        }

        _keep.SetQuestion(KeepText(left));
    }

    /// <summary>The brightness test pattern (GUI.Mire) over the options.</summary>
    private void OpenBrightnessTest()
    {
        if (GetNodeOrNull("BrightnessTest") is null)
        {
            AddChild(new BrightnessTest());
        }
    }

    /// <summary>Back to Reloaded's first-run values in the editor (not saved until Save).</summary>
    public void Defaults()
    {
        string name = _edit.PlayerName;
        _edit = new GameSettings { PlayerName = name };
        RebuildPages();
    }

    private void RebuildPages()
    {
        if (_tabs is null)
        {
            return;
        }

        int tab = _tabs.CurrentTab;
        foreach (Node child in GetChildren())
        {
            RemoveChild(child);
            child.QueueFree();
        }

        _controls.Clear();
        _keyButtons.Clear();
        Build();
        if (_tabs is not null)
        {
            _tabs.CurrentTab = tab;
        }
    }

    /// <summary>Leaves without saving.</summary>
    public void Back()
    {
        if (Overlay)
        {
            Closed?.Invoke();
            return;
        }

        FrontEnd.ShowMainMenu(GetTree());
    }
}
