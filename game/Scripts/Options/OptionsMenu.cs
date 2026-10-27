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
/// the new one, Escape cancels), mouse (profile sensitivity, inversion, Reloaded's per-side look), controller
/// (Reloaded's SDL defaults), display (mode, monitor, resolution, vsync, merc FOV cap, the per-side frame caps,
/// render scale), audio (master, effects, music), voice (Reloaded's voice page: OptionsMenu.Voice.cs) and HUD
/// (scoreboard, ping, frame-rate readout). Save writes
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

    public override void _Process(double delta) => UpdateVoicePage();

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

        VBoxContainer column = UiStyle.CentreColumn(this, 860f);
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

        _tabs = new TabContainer { Name = "Tabs", CustomMinimumSize = new Vector2(860f, 440f) };
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
        page.AddChild(grid);
        _keySource = new Label { Name = "Source", Modulate = UiStyle.Dim, AutowrapMode = TextServer.AutowrapMode.Arbitrary };
        page.AddChild(_keySource);
        var scroll = new ScrollContainer { Name = "Bindings", CustomMinimumSize = new Vector2(0f, 300f), SizeFlagsVertical = SizeFlags.ExpandFill };
        _keyGrid = new GridContainer { Columns = 2, SizeFlagsHorizontal = SizeFlags.ExpandFill };
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
        Slider(g, "Spy free look (gun holstered)", "controls_spy_kbm.free_look", 0.1f, 4f, 0.05f, _edit.SpyKbm.FreeLook, v => _edit.SpyKbm.FreeLook = v);
        Slider(g, "Spy gun look (gun drawn)", "controls_spy_kbm.look", 0.1f, 4f, 0.05f, _edit.SpyKbm.Look, v => _edit.SpyKbm.Look = v);
        Slider(g, "Mercenary look", "controls_merc_kbm.look", 0.1f, 4f, 0.05f, _edit.MercKbm.Look, v => _edit.MercKbm.Look = v);
        return Page("Mouse", g);
    }

    private Control PadPage()
    {
        GridContainer g = Grid("Controller");
        PadSettings p = _edit.Pad;
        Check(g, "Native controller support", "controller.enabled", p.Enabled, v => p.Enabled = v);
        Check(g, "Stick movement", "controller.move", p.Move, v => p.Move = v);
        Check(g, "Stick aiming", "controller.aim", p.Aim, v => p.Aim = v);
        Check(g, "Button binds", "controller.binds", p.Binds, v => p.Binds = v);
        Slider(g, "Left stick deadzone", "controller.deadzone_left", 0f, 0.9f, 0.01f, p.DeadzoneLeft, v => p.DeadzoneLeft = v);
        Slider(g, "Right stick deadzone", "controller.deadzone_right", 0f, 0.9f, 0.01f, p.DeadzoneRight, v => p.DeadzoneRight = v);
        Slider(g, "Vertical aim scale", "controller.vertical_scale", 0.1f, 3f, 0.05f, p.VerticalScale, v => p.VerticalScale = v);
        Slider(g, "Spy run threshold", "controller.run_threshold", 0.1f, 1f, 0.01f, p.RunThreshold, v => p.RunThreshold = v);
        Slider(g, "Look speed", "controller.look_speed", 100f, 3000f, 10f, p.LookSpeed, v => p.LookSpeed = v, "0");
        Check(g, "Spy eight-way movement", "controller.spy_move8", p.SpyMove8, v => p.SpyMove8 = v);
        Check(g, "Mercenary eight-way movement", "controller.merc_move8", p.MercMove8, v => p.MercMove8 = v);
        Slider(g, "Spy free look", "controls_spy_pad.free_look", 0.1f, 4f, 0.05f, _edit.SpyPad.FreeLook, v => _edit.SpyPad.FreeLook = v);
        Slider(g, "Spy gun look", "controls_spy_pad.look", 0.1f, 4f, 0.05f, _edit.SpyPad.Look, v => _edit.SpyPad.Look = v);
        Choice(g, "Spy look response", "controls_spy_pad.look_response", GameSettings.Responses, _edit.SpyPad.LookResponse, v => _edit.SpyPad.LookResponse = v);
        Slider(g, "Mercenary look", "controls_merc_pad.look", 0.1f, 4f, 0.05f, _edit.MercPad.Look, v => _edit.MercPad.Look = v);
        Choice(g, "Mercenary look response", "controls_merc_pad.look_response", GameSettings.Responses, _edit.MercPad.LookResponse, v => _edit.MercPad.LookResponse = v);
        Godot.Collections.Array<int> pads = Input.GetConnectedJoypads();
        string names = pads.Count == 0 ? "No controller connected" : string.Join(", ", System.Linq.Enumerable.Select(pads, i => Input.GetJoyName(i)));
        g.AddChild(new Label { Text = "Detected" });
        g.AddChild(new Label { Name = "Pads", Text = names, Modulate = UiStyle.Dim });
        g.AddChild(new Control());
        return Page("Controller", g);
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
        Check(g, "Widescreen (Hor+ view, anchored HUD)", "widescreen", _edit.Widescreen, v => _edit.Widescreen = v);
        Slider(g, "Mercenary FOV cap (ws_fov)", "ws_fov", GameSettings.MinMercFov, GameSettings.MaxMercFov, 1, _edit.MercFovCap, v => _edit.MercFovCap = v, "0");
        // Max frame rate (Options/DisplayFrameCap): Monitor (the window's screen rate), Unlimited or 30 to 240.
        double hz = SettingsApplier.MonitorHz;
        Choice(g, "Max frame rate as spy", "fps_spy", DisplayFrameCap.Choices(_edit.FpsSpy, hz), DisplayFrameCap.Label(_edit.FpsSpy, hz),
            v => _edit.FpsSpy = DisplayFrameCap.Parse(v) ?? _edit.FpsSpy);
        Choice(g, "Max frame rate as mercenary", "fps_merc", DisplayFrameCap.Choices(_edit.FpsMerc, hz), DisplayFrameCap.Label(_edit.FpsMerc, hz),
            v => _edit.FpsMerc = DisplayFrameCap.Parse(v) ?? _edit.FpsMerc);
        Slider(g, "Render scale", "resolution_scale", 0.5f, 1f, 0.05f, _edit.RenderScale, v => _edit.RenderScale = v);
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
        return Page("Audio", g);
    }

    private Control HudPage()
    {
        GridContainer g = Grid("HUD");
        Check(g, "Connected players list on (F10)", "show_scoreboard", _edit.ShowScoreboard, v => _edit.ShowScoreboard = v);
        Check(g, "Ping in the players list", "show_ping", _edit.ShowPing, v => _edit.ShowPing = v);
        Check(g, "Frame rate readout", "show_fps", _edit.ShowFps, v => _edit.ShowFps = v);
        Check(g, "Network readout (ping, loss, corrections)", "show_net", _edit.ShowNet, v => _edit.ShowNet = v);
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
        foreach (KeyBindingRow r in _keys.Rows())
        {
            _keyGrid.AddChild(new Label { Text = r.Label, SizeFlagsHorizontal = SizeFlags.ExpandFill, ClipText = true, CustomMinimumSize = new Vector2(520f, 0f) });
            string key = r.Key;
            var button = new Button { Name = key, Text = key, CustomMinimumSize = new Vector2(220f, 30f) };
            button.Pressed += () => Listen(key);
            _keyButtons[key] = button;
            _keyGrid.AddChild(button);
        }

        if (_keySource is not null)
        {
            // The file's name, not the install's whole path.
            _keySource.Text = "Layout: " + System.IO.Path.GetFileName(_keys.Source);
        }
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
        Error result = SettingsStore.Save(_edit);
        SettingsApplier.ApplyDisplay(_edit, GetViewport());
        SettingsApplier.ApplyAudio(_edit);
        _edit = SettingsStore.Current.Clone();
        if (_note is not null)
        {
            _note.Text = result == Error.Ok ? "Saved to " + SettingsStore.FilePath : "Could not save the settings";
        }

        return result;
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
