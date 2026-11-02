using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using OpenScctGame.Hud;
using OpenScctGame.Play.Session;
using OpenScctGame.UI;

namespace OpenScctGame.Options;

/// <summary>
/// Role-specific configuration, shared controls and display/audio/HUD settings.
/// Settings and keyboard edits remain in a draft until Apply. Back confirms discarding changes.
/// Main-menu screen or in-game overlay, with display rollback and role-specific crosshair previews.
/// </summary>
public partial class OptionsMenu : Control
{
    private readonly Dictionary<string, Button> _keyButtons = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Control> _controls = new(StringComparer.Ordinal);
    private GameSettings _edit = SettingsStore.Current.Clone();
    private KeyBindingsModel? _keys;
    private Label? _note;
    private string? _listening;
    private TabContainer? _tabs;
    private Control? _frame;
    private VBoxContainer? _column;
    private readonly List<GridContainer> _settingsGrids = new();
    private Vector2 _layoutSize = new(-1, -1);

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
        UpdateLayout();
        UpdateVoicePage();
        TickKeep();
        if (_note is not null) _note.Visible = !string.IsNullOrWhiteSpace(_note.Text);
        if (_dirtyLabel is not null) _dirtyLabel.Text = HasUnsavedChanges ? "Unsaved changes" : "All changes applied";
    }

    public override void _ExitTree()
    {
        StopMeter();
        _micTest?.Dispose();
        _micTest = null;
    }

    private void Build()
    {
        EnsureDrafts();
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

        _tabs = new TabContainer { Name = "Tabs", SizeFlagsVertical = SizeFlags.ExpandFill };
        _tabs.AddChild(RolePage(PlaySide.Spy));
        _tabs.AddChild(RolePage(PlaySide.Merc));
        _tabs.AddChild(DisplayPage());
        _tabs.AddChild(AudioPage());
        _tabs.AddChild(VoicePage());
        _tabs.AddChild(HudPage());
        _tabs.AddChild(PadPage());

        column.AddChild(_tabs);

        _note = new Label { Name = "Note", Modulate = UiStyle.Dim, Text = _edit.LoadNote, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        column.AddChild(_note);
        _dirtyLabel = Help("All changes applied");
        column.AddChild(_dirtyLabel);
        var buttons = new HBoxContainer { Name = "Buttons", Alignment = BoxContainer.AlignmentMode.Center };
        foreach (Button b in new[]
                 {
                     UiStyle.MenuButton("Apply", () => Save(), "Save"),
                     UiStyle.MenuButton("Save & Close", SaveAndClose, "SaveAndClose"),
                     UiStyle.MenuButton("Reset all", ConfirmDefaults, "Defaults"),
                     UiStyle.MenuButton("Back", Back, "Back"),
                 })
        {
            b.CustomMinimumSize = new Vector2(160f, 44f);
            buttons.AddChild(b);
        }

        column.AddChild(buttons);
        LoadLayout(_bindingSide);
        _layoutSize = new Vector2(-1, -1);
        UpdateLayout();
    }

    /// <summary>
    /// The options' frame: most of the screen (so the pages are not crammed), on a solid panel so a busy screen
    /// behind (the lobby) does not show through. Returns the column the title, pages and buttons go in.
    /// </summary>
    private VBoxContainer Frame()
    {
        var frame = new Control
        {
            Name = "Frame", AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorBottom = 1f,
            OffsetTop = 16f, OffsetBottom = -16f,
        };
        _frame = frame;
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
            margin.AddThemeConstantOverride(side, 12);
        }

        margin.AddThemeConstantOverride("margin_top", 8);
        margin.AddThemeConstantOverride("margin_bottom", 16);
        panel.AddChild(margin);
        var column = new VBoxContainer { Name = "Column" };
        _column = column;
        column.AddThemeConstantOverride("separation", 10);
        margin.AddChild(column);
        return column;
    }

    private float FrameWidth => MathF.Min(1500f, MathF.Max(0f, Size.X - 32f));

    /// <summary>Keep controls readable on narrow windows without spreading settings across an ultrawide screen.</summary>
    private void UpdateLayout()
    {
        if (_frame is null || _layoutSize == Size) return;
        _layoutSize = Size;
        float width = FrameWidth;
        _column?.AddThemeConstantOverride("separation", Size.Y < 720f ? 6 : 10);
        _frame.OffsetLeft = -width / 2f;
        _frame.OffsetRight = width / 2f;
        // Leave room for the panel, nested tabs, scroll bar and the numeric value column.
        float flexible = MathF.Max(360f, width - 272f);
        float labelWidth = MathF.Min(300f, flexible * 0.46f);
        float inputWidth = MathF.Min(320f, flexible * 0.54f);
        foreach (GridContainer grid in _settingsGrids)
        {
            if (!IsInstanceValid(grid) || grid.IsQueuedForDeletion()) continue;
            int index = 0;
            foreach (Node node in grid.GetChildren())
            {
                if (node is not Control control) continue;
                int column = index++ % 3;
                if (column == 2) continue;
                if (!control.HasMeta("wide_minimum_width"))
                    control.SetMeta("wide_minimum_width", control.CustomMinimumSize.X);
                float normal = (float)control.GetMeta("wide_minimum_width");
                if (normal >= 220f)
                    control.CustomMinimumSize = new Vector2(MathF.Min(normal, column == 0 ? labelWidth : inputWidth), control.CustomMinimumSize.Y);
                if (control is Label label) label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            }
        }
        if (_crosshairTop is not null) _crosshairTop.Vertical = width < 1000f;
        int columns = width < 1100f ? 3 : 6;
        if (_roleGrids.Values.Any(grid => grid.Columns != columns)) RebuildKeys();
    }

    // ---- pages ---------------------------------------------------------------------------------------------

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
                Slider(g, "Mercenary look", key + ".look", 0.05f, 10f, SensStep, l.Look, v => of().Look = v, SensFormat, typed: true);
            }
            else
            {
                Slider(g, "Spy free look (gun holstered)", key + ".free_look", 0.05f, 30f, SensStep, l.FreeLook, v => of().FreeLook = v, SensFormat, typed: true);
                Slider(g, "Spy gun look (gun drawn)", key + ".look", 0.05f, 10f, SensStep, l.Look, v => of().Look = v, SensFormat, typed: true);
            }
        }

        Slider(g, merc ? "Mercenary sniper scope" : "Spy binoculars", key + ".scope", 0.05f, 10f, SensStep, l.Scope, v => of().Scope = v, SensFormat,
            typed: true);
        if (merc)
        {
            // 2.45 turns the scope up and down as fast as sideways; 1 is the stock (legacy) speed.
            Slider(g, "Mercenary scope vertical (1 = stock)", key + ".scope_vertical", 0.05f, 10f, SensStep, l.ScopeVertical,
                v => of().ScopeVertical = v, SensFormat, typed: true);
        }

        if (skipLooks)
        {
            // Reloaded's mouse path ignores the groups' vertical; only the controller's uses it.
            Slider(g, side + " up/down sensitivity", key + ".vertical", 0.05f, 10f, SensStep, l.Vertical, v => of().Vertical = v, SensFormat, typed: true);
        }

        Slider(g, side + (merc ? " camera views" : " sticky camera and Cam Net"), key + ".cam", 0.05f, 10f, SensStep, l.Cam, v => of().Cam = v,
            SensFormat, typed: true);
        Check(g, side + " hold to modify sensitivity", key + ".clutch_one_enabled", l.ClutchOneEnabled, v => of().ClutchOneEnabled = v);
        Slider(g, side + " held sensitivity multiplier", key + ".clutch_one", 0.1f, 10f, 0.05f, l.ClutchOne > 0f ? l.ClutchOne : 1f, v => of().ClutchOne = v);
        if (merc)
        {
            Check(g, "Mercenary second sensitivity modifier", key + ".clutch_two_enabled", l.ClutchTwoEnabled, v => of().ClutchTwoEnabled = v);
            Slider(g, "Mercenary second held multiplier", key + ".clutch_two", 0.1f, 10f, 0.05f, l.ClutchTwo > 0f ? l.ClutchTwo : 1f, v => of().ClutchTwo = v);
        }

        if (skipLooks)
        {
            // The controller groups only: the stick clicks trade places with the bumpers.
            Check(g, side + ": swap left stick click and left bumper", key + ".swap_ls_lb", l.SwapLsLb, v => of().SwapLsLb = v);
            Check(g, side + ": swap right stick click and right bumper", key + ".swap_rs_rb", l.SwapRsRb, v => of().SwapRsRb = v);
        }
    }

    /// <summary>The clutch whose key waits for a press, or null.</summary>
    private (Button Button, Action<string> Set, string Original)? _listeningClutch;

    /// <summary>A key or mouse button held for a modifier: Escape cancels; Clear explicitly unbinds it.</summary>
    private void ClutchKeyRow(GridContainer g, string label, string key, Func<string> current, Action<string> set)
    {
        var button = new Button { Name = Safe(key), Text = current().Length == 0 ? "(none)" : OpenScctGame.Controls.GodotKeys.DisplayName(current()), CustomMinimumSize = new Vector2(220f, 30f) };
        button.Pressed += () =>
        {
            _listening = null;
            _listeningAction = null;
            string original = current();
            button.Text = "Press key (Esc cancels)";
            _listeningClutch = (button, v =>
            {
                set(v);
                button.Text = v.Length == 0 ? "(none)" : OpenScctGame.Controls.GodotKeys.DisplayName(v);
                if (key.StartsWith("clutch_one_key", StringComparison.Ordinal))
                    foreach ((string id, Control control) in _controls)
                        if (id.StartsWith("clutch_one_key", StringComparison.Ordinal) && control is Button other) other.Text = button.Text;
            }, original);
        };
        g.AddChild(new Label { Text = label, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(300f, 0f) });
        g.AddChild(button);
        var clear = new Button { Text = "Clear" };
        clear.Pressed += () =>
        {
            _listeningClutch = null;
            set(""); button.Text = "(none)";
            if (key.StartsWith("clutch_one_key", StringComparison.Ordinal))
                foreach ((string id, Control control) in _controls)
                    if (id.StartsWith("clutch_one_key", StringComparison.Ordinal) && control is Button other) other.Text = "(none)";
        };
        g.AddChild(clear);
        _controls[key] = button;
    }

    private Control PadPage()
    {
        GridContainer g = Grid("Controller");
        PadSettings p = _edit.Pad;
        // Set here, the first-run question (PadMenus) is never asked.
        Check(g, "Enable controller", "controller.enabled", p.Enabled, v =>
        {
            p.Enabled = v;
            p.Prompted = true;
        });
        Check(g, "Enable stick movement", "controller.move", p.Move, v => p.Move = v);
        Check(g, "Enable stick aiming", "controller.aim", p.Aim, v => p.Aim = v);
        Check(g, "Enable controller buttons", "controller.binds", p.Binds, v => p.Binds = v);
        Slider(g, "Left stick deadzone", "controller.deadzone_left", 0f, 0.9f, 0.01f, p.DeadzoneLeft, v => p.DeadzoneLeft = v);
        Slider(g, "Right stick deadzone", "controller.deadzone_right", 0f, 0.9f, 0.01f, p.DeadzoneRight, v => p.DeadzoneRight = v);
        Slider(g, "Up/down sensitivity multiplier", "controller.vertical_scale", 0.1f, 3f, 0.05f, p.VerticalScale, v => p.VerticalScale = v);
        Check(g, "Movement speed follows stick tilt", "controller.slow_walk", p.SlowWalk, v => p.SlowWalk = v);
        Slider(g, "Base controller turning speed", "controller.look_speed", 100f, 3000f, 10f, p.LookSpeed, v => p.LookSpeed = v, "0");
        Slider(g, "Trigger press threshold", "controller.trigger_threshold", 0f, 0.99f, 0.01f, p.TriggerThreshold, v => p.TriggerThreshold = v);
        Check(g, "Vibration", "controller.rumble", p.Rumble, v => p.Rumble = v);
        Slider(g, "Vibration strength", "controller.rumble_strength", 0f, 100f, 1f, p.RumbleStrength * 100f, v => p.RumbleStrength = v / 100f, "0");
        g.AddChild(new Label { Text = "Test vibration", CustomMinimumSize = new Vector2(320f, 0f) });
        // Reloaded's trigger test: the blast's rumble (both motors, 0.35 s) at the strength set above.
        g.AddChild(UiStyle.MenuButton("Test", () => PadRumble.Play(PadMenus.CurrentDevice(), new RumbleShape(7, 65535, 65535, 0.35f), _edit.Pad.RumbleStrength),
            "RumbleTest"));
        g.AddChild(new Control());
        Godot.Collections.Array<int> pads = Input.GetConnectedJoypads();
        string names = pads.Count == 0 ? "No controller connected" : string.Join(", ", System.Linq.Enumerable.Select(pads, i => Input.GetJoyName(i)));
        g.AddChild(new Label { Text = "Connected controllers" });
        g.AddChild(new Label { Name = "Pads", Text = names, Modulate = UiStyle.Dim });
        g.AddChild(new Control());

        var content = new VBoxContainer { Name = "ControllerContent", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        content.AddChild(Help("These controller settings apply to both roles. For role-specific sensitivity and button bindings, open Controller under Spy or Mercenary."));
        // Keep device setup and vibration together; detailed stick behavior is a separate group.
        var tuning = Grid("ControllerTuning");
        Node[] cells = g.GetChildren().ToArray();
        for (int row = 1; row <= 9; row++)
            for (int col = 0; col < 3; col++)
            {
                Node child = cells[row * 3 + col];
                g.RemoveChild(child);
                tuning.AddChild(child);
            }
        content.AddChild(g);
        var advanced = new VBoxContainer { Visible = false, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        advanced.AddChild(new Label { Text = "Stick behavior", Modulate = UiStyle.MenuFocus });
        advanced.AddChild(Help("Deadzones ignore small stick movements near the center; increase them if the view or character drifts. Movement speed follows stick tilt lets a partial push move more slowly."));
        advanced.AddChild(tuning);
        advanced.AddChild(Help("Base turning speed affects both roles and combines with each role's sensitivity. The up/down multiplier changes vertical turning relative to horizontal."));
        AddAdvanced(content, advanced);
        return Page("Controller (Shared)", content);
    }

    /// <summary>Pad control names as the options list them.</summary>
    private static string PadControlLabel(string control)
    {
        if (control.Contains('+')) return string.Join(" + ", control.Split('+').Select(PadControlLabel));
        if (control.EndsWith(":Hold", StringComparison.Ordinal)) return "Hold " + PadControlLabel(control[..^5]);
        return control switch
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
    }

    /// <summary>One row per pad control: what it does on the shown side (saved with Save, like the other settings).</summary>
    private void RebuildPadButtons(PlaySide padSide, GridContainer padGrid)
    {
        foreach (Node child in padGrid.GetChildren())
        {
            padGrid.RemoveChild(child);
            child.QueueFree();
        }

        bool merc = padSide == PlaySide.Merc;
        Dictionary<string, string> buttons = _edit.Pad.ButtonsOf(merc);
        var model = ModelOf(padSide);
        List<(string Action, string Label)> choices = model.PadChoices(forSide: padSide, wheelMode: _edit.MouseWheel);
        List<(string Action, string Label)> every = model.PadChoices(otherTeam: true, forSide: padSide, wheelMode: _edit.MouseWheel);
        foreach (string control in PadSettings.Controls)
        {
            string current = buttons.TryGetValue(control, out string? a) ? a : "";
            var items = new List<(string Action, string Label)>(choices);
            int selected = items.FindIndex(c => string.Equals(c.Action, current, StringComparison.OrdinalIgnoreCase));
            if (selected < 0)
            {
                // A saved action this team does not use, or one its layout has no key for: kept, and shown as such.
                int other = every.FindIndex(c => string.Equals(c.Action, current, StringComparison.OrdinalIgnoreCase));
                items.Add((current, other >= 0 ? every[other].Label + " (saved)" : current + " (saved custom assignment)"));
                selected = items.Count - 1;
            }

            var option = new OptionButton
            {
                Name = Safe("Pad_" + control).Replace(':', '_').Replace('+', '_'), ClipText = true, FitToLongestItem = false, CustomMinimumSize = new Vector2(320f, 0f), SizeFlagsHorizontal = SizeFlags.ExpandFill,
            };
            for (int i = 0; i < items.Count; i++)
            {
                option.AddItem(items[i].Label, i);
                option.SetItemTooltip(i, items[i].Label);
            }

            option.Selected = selected;
            string c = control;
            // The edited settings are read when chosen: Save swaps them for a fresh copy.
            option.ItemSelected += index => _edit.Pad.ButtonsOf(merc)[c] = items[(int)index].Action;
            padGrid.AddChild(new Label { Text = PadControlLabel(control), CustomMinimumSize = new Vector2(320f, 0f) });
            padGrid.AddChild(option);
            padGrid.AddChild(new Control());
            _controls[(merc ? "controls_merc_pad.buttons." : "controls_spy_pad.buttons.") + control] = option;
        }
    }

    private Control DisplayPage()
    {
        GridContainer g = Grid("Display");
        Section(g, "Screen", "Choose the display, window mode and resolution.");
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
        Section(g, "Widescreen layout", "Widen the view and adjust how the HUD fits a widescreen display.");
        Check(g, "Wider field of view on widescreen", "widescreen", _edit.Widescreen, v => _edit.Widescreen = v);
        Check(g, "Fit HUD to widescreen", "widescreen_hud", _edit.WidescreenHud, v => _edit.WidescreenHud = v);
        Check(g, "Small Enhanced Reality icons in widescreen", "ws_er_small", _edit.WideErSmall, v => _edit.WideErSmall = v);
        Section(g, "Image quality", "Lower render scale improves performance at the cost of sharpness. Texture filtering keeps angled surfaces clearer; anti-aliasing smooths jagged edges.");
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
        Choice(g, "Post-processing quality", "post_quality", GameSettings.PostQualities, GameSettings.PostQualities[Math.Clamp(_edit.PostQuality, 0, 1)],
            v => _edit.PostQuality = Array.IndexOf(GameSettings.PostQualities, v));
        g.AddChild(new Label { Text = "Brightness", CustomMinimumSize = new Vector2(320f, 0f) });
        g.AddChild(UiStyle.MenuButton("Brightness configuration", OpenBrightnessTest, "BrightnessConfiguration"));
        g.AddChild(new Control());
        Section(g, "Performance", "These settings require restarting the game.");
        Choice(g, "Reduce input lag (restart)", "low_latency_mode", GameSettings.LatencyModes, _edit.LowLatencyMode, v => _edit.LowLatencyMode = v);
        if (OperatingSystem.IsWindows())
        {
            Check(g, "Stop Windows power throttling (restart)", "throttle_stop", _edit.ThrottleStop, v => _edit.ThrottleStop = v);
        }

        return Page("Display", SectionedContent(g));
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
        Section(g, "Volume", "Adjust the overall volume or individual sound categories. Voice volume is on the Voice tab.");
        Slider(g, "Overall volume", "master_volume", 0f, 100f, 1f, _edit.MasterVolume * 100f, v => _edit.MasterVolume = v / 100f, "0");
        Slider(g, "Sound effects volume", "sound_volume", 0f, 100f, 1f, _edit.SoundVolume * 100f, v => _edit.SoundVolume = v / 100f, "0");
        Slider(g, "Music volume", "music_volume", 0f, 100f, 1f, _edit.MusicVolume * 100f, v => _edit.MusicVolume = v / 100f, "0");
        Section(g, "Gameplay audio", "");
        Check(g, "Objective sounds", "objective_sounds", _edit.ObjectiveSounds, v => _edit.ObjectiveSounds = v);
        return Page("Audio", SectionedContent(g));
    }

    private Control HudPage()
    {
        GridContainer g = Grid("HUD");
        Section(g, "Player list", "");
        Check(g, "Show connected players", "show_scoreboard", _edit.ShowScoreboard, v => _edit.ShowScoreboard = v);
        Check(g, "Show latency in player list", "show_ping", _edit.ShowPing, v => _edit.ShowPing = v);
        Choice(g, "Split player list when team size reaches", "scoreboard_split_team_size", ["1", "2", "3", "4"],
            _edit.ScoreboardSplitTeamSize.ToString(CultureInfo.InvariantCulture), v => _edit.ScoreboardSplitTeamSize = int.Parse(v, CultureInfo.InvariantCulture));
        Section(g, "Performance & network information", "Show diagnostic information while playing.");
        Check(g, "Show frames per second (FPS)", "show_fps", _edit.ShowFps, v => _edit.ShowFps = v);
        Check(g, "Network readout (ping, corrections; a host sees its players' best and worst ping)", "show_net", _edit.ShowNet,
            v => _edit.ShowNet = v);
        Check(g, "Packet loss in the network readout", "show_net_loss", _edit.ShowNetLoss, v => _edit.ShowNetLoss = v);
        Section(g, "Online feedback", "");
        Check(g, "Fire prediction (your bullet impacts show at once online)", "fire_prediction", _edit.FirePrediction,
            v => _edit.FirePrediction = v);
        Section(g, "Gameplay display", "");
        Check(g, "Show current location", "show_location", _edit.ShowLocation, v => _edit.ShowLocation = v);
        Check(g, "Use feet for distances", "hud_feet", _edit.HudFeet, v => _edit.HudFeet = v);
        Check(g, "Classic crosshair position (merc, slightly off-centre)", "classic_crosshair", _edit.ClassicCrosshair,
            v => _edit.ClassicCrosshair = v);
        // Reloaded's settings page offers 20 % to 100 %; stored 0 to 255.
        Slider(g, "Objective icon opacity while hacking (%)", "hack_alpha", 20f, 100f, 1f, Math.Max(20f, _edit.HackAlpha * 100f / 255f),
            v => _edit.HackAlpha = (int)MathF.Round(v * 255f / 100f), "0");
        Section(g, "Discord", "Choose whether Discord displays your game and lets friends join.");
        Check(g, "Discord status (shows your game, lets friends join)", "discord_presence", _edit.DiscordPresence, v => _edit.DiscordPresence = v);
        return Page("HUD", SectionedContent(g));
    }

    // ---- building blocks -----------------------------------------------------------------------------------

    private static void Section(GridContainer grid, string title, string description = "")
    {
        var heading = new VBoxContainer();
        heading.SetMeta("options_section", true);
        heading.AddChild(new Label { Text = title, Modulate = UiStyle.MenuFocus });
        if (description.Length > 0) heading.AddChild(Help(description));
        grid.AddChild(heading);
    }

    private VBoxContainer SectionedContent(GridContainer source)
    {
        var content = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        content.AddThemeConstantOverride("separation", 12);
        GridContainer? group = null;
        foreach (Node child in source.GetChildren())
        {
            source.RemoveChild(child);
            if (child.HasMeta("options_section"))
            {
                content.AddChild(child);
                group = null;
            }
            else
            {
                if (group is null) { group = Grid("Settings"); content.AddChild(group); }
                group.AddChild(child);
            }
        }
        source.QueueFree();
        return content;
    }

    private GridContainer Grid(string name)
    {
        var grid = new GridContainer { Name = name, Columns = 3, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        grid.AddThemeConstantOverride("h_separation", 16);
        grid.AddThemeConstantOverride("v_separation", 6);
        _settingsGrids.Add(grid);
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
        grid.AddChild(new Label { Text = label, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(300f, 0f) });
        grid.AddChild(control);
        grid.AddChild(new Control());
        _controls[key] = control;
        if (control is LineEdit edit)
        {
            edit.TextChanged += _ => changed(control);
        }
    }

    /// <summary>The step of the look sensitivities: three decimals, as Reloaded's settings page shows them (%.3f).</summary>
    private const float SensStep = 0.001f;

    private const string SensFormat = "0.000";

    /// <param name="typed">An editable number box in place of the value label, so an exact value can be typed.</param>
    private void Slider(GridContainer grid, string label, string key, float min, float max, float step, float value, Action<float> changed,
        string format = "0.00", bool typed = false)
    {
        var slider = new HSlider
        {
                Scrollable = false,
            Name = Safe(key), MinValue = min, MaxValue = max, Step = step, Value = value,
            CustomMinimumSize = new Vector2(320f, 24f), SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        grid.AddChild(new Label { Text = label, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(300f, 0f) });
        grid.AddChild(slider);
        _controls[key] = slider;
        if (typed)
        {
            var box = new SpinBox
            {
                Name = Safe(key) + "_Value", MinValue = min, MaxValue = max, Step = step, Value = value, Rounded = false,
                CustomArrowStep = step * 10f, SelectAllOnFocus = true, CustomMinimumSize = new Vector2(110f, 0f),
            };

            // The two stay in step without echoing: SetValueNoSignal does not raise value_changed.
            slider.ValueChanged += v =>
            {
                box.SetValueNoSignal(v);
                changed((float)v);
            };
            slider.DragEnded += _ => changed((float)slider.Value);
            box.ValueChanged += v =>
            {
                slider.SetValueNoSignal(v);
                changed((float)v);
            };
            grid.AddChild(box);
            return;
        }

        var shown = new Label { Text = value.ToString(format, CultureInfo.InvariantCulture), CustomMinimumSize = new Vector2(70f, 0f) };
        void Apply(double v)
        {
            shown.Text = ((float)v).ToString(format, CultureInfo.InvariantCulture);
            changed((float)v);
        }

        // A click on the track moves the grabber without value_changed (Godot's Slider blocks it on press).
        slider.ValueChanged += Apply;
        slider.DragEnded += _ => Apply(slider.Value);
        grid.AddChild(shown);
    }

    private void Check(GridContainer grid, string label, string key, bool value, Action<bool> changed)
    {
        var box = new CheckBox { Name = Safe(key), ButtonPressed = value };
        box.Toggled += v => changed(v);
        grid.AddChild(new Label { Text = label, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(300f, 0f) });
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
            Name = Safe(key), ClipText = true, FitToLongestItem = false, CustomMinimumSize = new Vector2(320f, 0f), SizeFlagsHorizontal = SizeFlags.ExpandFill,
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
        grid.AddChild(new Label { Text = label, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(300f, 0f) });
        grid.AddChild(option);
        grid.AddChild(new Control());
        _controls[key] = option;
    }

    private static string Safe(string key) => key.Replace('.', '_');

    // ---- key layouts ---------------------------------------------------------------------------------------

    /// <summary>Shows the key layout of <paramref name="side"/>.</summary>
    public void LoadLayout(PlaySide side)
    {
        _bindingSide = side;
        _keys = ModelOf(side);
        _listening = null;
        RebuildKeys();
    }

    private void RebuildKeys()
    {
        _layoutSize = new Vector2(-1, -1);
        _keyButtons.Clear();
        foreach ((PlaySide side, GridContainer grid) in _roleGrids)
            BuildActionRows(side, grid);
        _keys = ModelOf(_bindingSide);
        foreach ((PlaySide side, GridContainer grid) in _rolePadGrids) RebuildPadButtons(side, grid);
    }

    private void Listen(string key)
    {
        _listening = key;
        if (_keyButtons.TryGetValue(key, out Button? b))
        {
            b.Text = "Press a key...";
        }
    }

    public override void _Input(InputEvent @event)
    {
        if (_dialogOpen || (_keep is not null && IsInstanceValid(_keep))) return;
        if (_listeningClutch is { } clutch && @event is InputEventKey { Pressed: true } or InputEventMouseButton { Pressed: true })
        {
            GetViewport().SetInputAsHandled();
            _listeningClutch = null;
            if (@event is InputEventKey { Keycode: Key.Escape }) clutch.Button.Text = clutch.Original.Length == 0 ? "(none)" : OpenScctGame.Controls.GodotKeys.DisplayName(clutch.Original);
            else clutch.Set(KeyBindingsModel.KeyOf(@event) ?? clutch.Original);
            return;
        }

        if (_listening is not null && @event is InputEventKey { Pressed: true } or InputEventMouseButton { Pressed: true })
        {
            string old = _listening;
            GetViewport().SetInputAsHandled();
            if (@event is InputEventKey { Keycode: Key.Escape })
            {
                _listening = null;
                _listeningAction = null;
                RebuildKeys();
                return;
            }

            if (KeyBindingsModel.KeyOf(@event) is { } key)
            {
                if (_listeningAction is { } action)
                {
                    _listening = null;
                    _listeningAction = null;
                    AssignAction(_bindingSide, action, key);
                }
                else Rebind(old, key);
            }

            return;
        }

        if (Overlay && @event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.F12 })
        {
            GetViewport().SetInputAsHandled();
            Back();
        }
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape }) return;
        GetViewport().SetInputAsHandled();
        Back();
    }

    /// <summary>Moves the command of <paramref name="oldKey"/> to <paramref name="newKey"/> (staged until Apply).</summary>
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
        // No disk or live input mutation takes place before Apply.
        foreach (KeyBindingsModel model in ModelsToCommit())
        {
            if (!model.Commit())
            {
                if (_note is not null) _note.Text = "Could not save bindings. Your draft is still open; retry Apply.";
                return Error.FileCantWrite;
            }
        }
        Error result = SettingsStore.Save(_edit);
        if (result != Error.Ok)
        {
            if (_note is not null) _note.Text = "Could not save settings. Your draft is still open; retry Apply.";
            return result;
        }
        foreach (KeyBindingsModel model in ModelsToCommit()) model.Draft?.Accept();
        SettingsApplier.ApplyDisplay(_edit, GetViewport());
        SettingsApplier.ApplyAudio(_edit);
        _edit = SettingsStore.Current.Clone();
        _baseline = _edit.ToJson();
        // Voice reads its own InputMap. Apply the played role, or Spy outside a round, without seeding gameplay keys.
        PlaySide voiceSide = GetTree().CurrentScene?.FindChildren("*", nameof(OpenScctGame.Controls.PlayInput), true, false)
            .OfType<OpenScctGame.Controls.PlayInput>().FirstOrDefault(p => p.Bindings is not null)?.Bindings?.Side ?? PlaySide.Spy;
        OpenScctGame.Voice.VoiceInput.BindLayout(ModelOf(voiceSide).Draft!.Layout);
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

        RebuildPages();
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
            Yes = () =>
            {
                _keep = null;
                if (_closeAfterKeep) CloseMenu();
                _closeAfterKeep = false;
            },
            No = () =>
            {
                _keep = null;
                _closeAfterKeep = false;
                GameSettings back = SettingsStore.Current.Clone();
                back.DisplayMode = before.DisplayMode;
                back.Monitor = before.Monitor;
                back.Resolution = before.Resolution;
                SettingsStore.Save(back);
                SettingsApplier.ApplyDisplay(back, GetViewport());
                _edit.DisplayMode = back.DisplayMode;
                _edit.Monitor = back.Monitor;
                _edit.Resolution = back.Resolution;
                _baseline = _edit.ToJson();
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
        foreach (PlaySide side in new[] { PlaySide.Spy, PlaySide.Merc }) ResetBindings(side);
        RebuildPages();
    }

    private void RebuildPages()
    {
        if (_tabs is null)
        {
            return;
        }

        StopMeter();
        _micTest?.Dispose();
        _micTest = null;
        int tab = _tabs.CurrentTab;
        var sections = new Dictionary<string, int>();
        foreach (Node role in _tabs.GetChildren())
            if (role.GetNodeOrNull<TabContainer>("Sections") is { } nested) sections[role.Name] = nested.CurrentTab;
        foreach (Node child in GetChildren())
        {
            RemoveChild(child);
            child.QueueFree();
        }

        _controls.Clear();
        _settingsGrids.Clear();
        _roleGrids.Clear();
        _bindingHelp.Clear();
        _sharingLabels.Clear();
        _rolePadGrids.Clear();
        _listening = null;
        _listeningAction = null;
        _listeningClutch = null;
        _keyButtons.Clear();
        Build();
        if (_tabs is not null)
        {
            _tabs.CurrentTab = Math.Min(tab, _tabs.GetTabCount() - 1);
            foreach (Node role in _tabs.GetChildren())
                if (role.GetNodeOrNull<TabContainer>("Sections") is { } nested && sections.TryGetValue(role.Name, out int selected))
                    nested.CurrentTab = selected;
        }
    }

    /// <summary>Leaves without saving.</summary>
    public void Back()
    {
        if (_dialogOpen) return;
        if (HasUnsavedChanges)
        {
            Confirm("Discard changes?", "Discard all unapplied settings and binding changes?", CloseMenu, "Discard");
            return;
        }
        CloseMenu();
    }

    private void CloseMenu()
    {
        if (Overlay)
        {
            Closed?.Invoke();
            return;
        }

        FrontEnd.ShowMainMenu(GetTree());
    }
}
