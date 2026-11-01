using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using OpenScctGame.UI;
using OpenScctGame.Play.Session;

namespace OpenScctGame.Options;

public partial class OptionsMenu
{
    private readonly Dictionary<PlaySide, KeyBindingsModel> _drafts = new();
    private readonly Dictionary<PlaySide, GridContainer> _roleGrids = new();
    private readonly Dictionary<PlaySide, Label> _sharingLabels = new();
    private readonly Dictionary<PlaySide, Label> _bindingHelp = new();
    private readonly Dictionary<PlaySide, GridContainer> _rolePadGrids = new();
    private string? _baseline;
    private Label? _dirtyLabel;
    private PlaySide _bindingSide = PlaySide.Spy;
    private ControlAction? _listeningAction;
    private bool _dialogOpen;
    private bool _closeAfterKeep;

    public bool HasUnsavedChanges => _baseline != _edit.ToJson() || ModelsToCommit().Any(m => m.Draft?.Dirty == true);

    private void EnsureDrafts()
    {
        _baseline ??= _edit.ToJson();
        foreach (PlaySide side in new[] { PlaySide.Spy, PlaySide.Merc })
            if (!_drafts.ContainsKey(side)) _drafts[side] = new KeyBindingsModel(side, staged: true, shared: false);
    }

    private KeyBindingsModel ModelOf(PlaySide side) => _drafts[!_edit.SplitKeyboard ? PlaySide.Spy : side];
    private IEnumerable<KeyBindingsModel> ModelsToCommit() => _drafts.Values.Where(m => m.Draft?.Dirty == true);

    private static Label Help(string text) => new()
    {
        Text = text, Modulate = UiStyle.Dim, AutowrapMode = TextServer.AutowrapMode.WordSmart,
        SizeFlagsHorizontal = SizeFlags.ExpandFill,
    };

    private Control RolePage(PlaySide side)
    {
        bool merc = side == PlaySide.Merc;
        string role = merc ? "Mercenary" : "Spy";
        var page = new VBoxContainer { Name = role, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var header = new HBoxContainer();
        var sharing = new Label
        {
            Text = role + " configuration", SizeFlagsHorizontal = SizeFlags.ExpandFill,
            AutowrapMode = TextServer.AutowrapMode.WordSmart, MouseFilter = MouseFilterEnum.Pass,
        };
        _sharingLabels[side] = sharing;
        header.AddChild(sharing);
        header.AddChild(UiStyle.MenuButton("Reset " + role, () => Confirm("Reset " + role,
            "Reset this role's bindings, mouse, controller and crosshair settings? Shared keyboard changes affect both roles.",
            () => ResetRole(side), "Reset role"), "Reset" + role));
        page.AddChild(header);
        var hud = Grid("RoleHud");
        Check(hud, "Show teammate names through walls", merc ? "teammate_names_merc" : "teammate_names_spy",
            merc ? _edit.TeammateNamesMerc : _edit.TeammateNamesSpy,
            v => { if (merc) _edit.TeammateNamesMerc = v; else _edit.TeammateNamesSpy = v; });
        page.AddChild(hud);
        var sections = new TabContainer { Name = "Sections", SizeFlagsVertical = SizeFlags.ExpandFill };
        sections.AddChild(RoleBindingsPage(side));
        sections.AddChild(RoleMousePage(side));
        sections.AddChild(RolePadPage(side));
        sections.AddChild(merc ? CrosshairPage() : Page("Crosshair", Help("The Spy uses the original contextual reticle. Custom crosshair controls apply to the Mercenary.")));
        page.AddChild(sections);
        return page;
    }

    private Control RoleBindingsPage(PlaySide side)
    {
        var page = new VBoxContainer { Name = "Bindings", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var content = new VBoxContainer { Name = "BindingList", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var reset = new Button { Text = "Reset bindings", CustomMinimumSize = new Vector2(170, 32) };
        reset.Pressed += () => Confirm("Reset bindings", "Reset this role's keyboard bindings? Shared layouts affect both roles.",
            () => { ResetBindings(side); RebuildKeys(); }, "Reset bindings");
        bool merc = side == PlaySide.Merc;
        var behavior = new HBoxContainer();
        void BehaviorChoice(string label, string key, string value, Action<string> change)
        {
            behavior.AddChild(new Label { Text = label });
            var choice = new OptionButton { Selected = -1, CustomMinimumSize = new Vector2(120, 32),
                TooltipText = "Hold: active while pressed. Toggle: press again to stop. Wheel bindings always toggle. Applies to keyboard, mouse and controller." };
            choice.AddItem("Hold"); choice.AddItem("Toggle");
            choice.Selected = value == "Hold" ? 0 : 1;
            choice.ItemSelected += index => change(index == 0 ? "Hold" : "Toggle");
            _controls[key] = choice;
            behavior.AddChild(choice);
        }
        BehaviorChoice("Walk:", "walk_behavior." + side, _edit.LookOf(merc, false).WalkBehavior,
            v => _edit.LookOf(merc, false).WalkBehavior = v);
        if (merc) BehaviorChoice("Scope aim:", "scope_behavior", _edit.MercKbm.ScopeBehavior,
            v => _edit.MercKbm.ScopeBehavior = v);
        behavior.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        behavior.AddChild(reset);
        content.AddChild(behavior);
        var grid = new GridContainer { Name = "Actions", Columns = 6, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        grid.AddThemeConstantOverride("h_separation", 12);
        content.AddChild(grid);
        _roleGrids[side] = grid;
        // Binding controls scroll with the actions on short screens; the live explanation remains visible below.
        page.AddChild(Page("ActionList", content));
        var help = Help("Select an action to rebind. Esc cancels; Clear unbinds it. Combined bindings stay intact until edited.");
        help.CustomMinimumSize = new Vector2(0, 44);
        help.MaxLinesVisible = 2;
        help.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        help.MouseFilter = MouseFilterEnum.Pass;
        help.TooltipText = help.Text;
        _bindingHelp[side] = help;
        page.AddChild(help);
        return page;
    }

    private void BuildActionRows(PlaySide side, GridContainer grid)
    {
        var scroll = grid.GetParent()?.GetParent() as ScrollContainer;
        int scrollOffset = scroll?.ScrollVertical ?? 0;
        grid.Columns = FrameWidth < 1100f ? 3 : 6;
        int actionsPerRow = grid.Columns / 3;
        foreach (Node child in grid.GetChildren()) { grid.RemoveChild(child); child.QueueFree(); }
        var model = ModelOf(side);
        ActionBindingDraft draft = model.Draft!;
        var actions = ControlActions.ForRole(side == PlaySide.Merc, _edit.MouseWheel).ToArray();
        _sharingLabels[side].Text = (side == PlaySide.Merc ? "Mercenary" : "Spy") + " configuration\n" +
            (_edit.SplitKeyboard ? "Separate bindings" : "Shared bindings (Spy layout)");
        _sharingLabels[side].TooltipText = _edit.SplitKeyboard ? "Separate role bindings" : "Shared bindings: edits here affect both roles (Spy layout).";
        string category = "";
        int inCategory = 0;
        foreach (ControlAction action in actions)
        {
            if (category != action.Category)
            {
                if (inCategory % actionsPerRow != 0) for (int i = 0; i < 3; i++) grid.AddChild(new Control());
                category = action.Category;
                inCategory = 0;
                grid.AddChild(new Label { Text = category, Modulate = UiStyle.MenuFocus });
                for (int i = 0; i < grid.Columns - 1; i++) grid.AddChild(new Control());
            }
            inCategory++;
            string[] keys = draft.Keys(action).ToArray();
            grid.AddChild(Help(action.Label));
            string keyText = keys.Length == 0 ? "Unbound" : string.Join(", ", keys.Select(OpenScctGame.Controls.GodotKeys.DisplayName));
            var button = new Button { Text = keyText, ClipText = true, CustomMinimumSize = new Vector2(120, 32), TooltipText = action.Description };
            button.Pressed += () =>
            {
                _listeningClutch = null;
                _bindingSide = side; _keys = model; _listeningAction = action; _listening = action.Id;
                button.Text = "Press key...";
                ShowBindingHelp(side, action.Description + " Press a key; Esc cancels capture.");
            };
            // Show every behavior sharing a key, including the other role's actions and custom commands.
            string combined = string.Join("\n", keys.Select(k => OpenScctGame.Controls.GodotKeys.DisplayName(k) + ": " + DescribeBinding(draft, k, side)));
            button.TooltipText += combined.Length > 0 ? "\n" + combined : "";
            void Explain() => ShowBindingHelp(side, action.Description + (combined.Length > 0 ? "\n" + combined : " Currently unbound."));
            button.FocusEntered += Explain;
            button.MouseEntered += Explain;
            foreach (string key in keys) if (side == _bindingSide) _keyButtons[key] = button;
            grid.AddChild(button);
            var clear = new Button { Text = "Clear", Disabled = keys.Length == 0, CustomMinimumSize = new Vector2(60, 32) };
            clear.Pressed += () => { draft.Remove(action); RebuildKeys(); };
            grid.AddChild(clear);
        }
        if (inCategory % actionsPerRow != 0) for (int i = 0; i < 3; i++) grid.AddChild(new Control());
        // Unknown/custom chains retain their exact command text and can still be moved as a whole.
        foreach (var row in model.Rows().Where(r => draft.Layout.Expanded(r.Command).Any(p => !ControlActions.All.Any(a => a.Matches(p)))))
        {
            grid.AddChild(Help("Custom binding: " + row.Command));
            var button = new Button { Text = OpenScctGame.Controls.GodotKeys.DisplayName(row.Key), ClipText = true, CustomMinimumSize = new Vector2(120, 32) };
            button.Pressed += () => { _bindingSide = side; _keys = model; _listeningAction = null; Listen(row.Key); };
            grid.AddChild(button);
            var clear = new Button { Text = "Clear" };
            clear.Pressed += () => { draft.Layout.Bind(row.Key, ""); RebuildKeys(); };
            grid.AddChild(clear);
        }
        scroll?.SetDeferred(ScrollContainer.PropertyName.ScrollVertical, scrollOffset);
    }

    private string DescribeBinding(ActionBindingDraft draft, string key, PlaySide side)
    {
        var role = ControlActions.ForRole(side == PlaySide.Merc, _edit.MouseWheel).ToArray();
        return string.Join(" / ", draft.Layout.Expanded(draft.CommandOf(key)).Select(p =>
            role.FirstOrDefault(a => a.Matches(p))?.Label ?? (ControlActions.All.Any(a => a.Matches(p)) ? null : p))
            .Where(p => p is not null).Distinct());
    }

    private void ShowBindingHelp(PlaySide side, string text)
    {
        _bindingHelp[side].Text = text;
        _bindingHelp[side].TooltipText = text;
    }

    private void AssignAction(PlaySide side, ControlAction action, string key)
    {
        if ((action.Category == "Interface" || action.Id is "chat" or "teamchat" or "er") &&
            OpenScctGame.Controls.GodotKeys.ToEvent(key) is not InputEventKey)
        {
            ShowBindingHelp(side, "This shortcut requires a keyboard key in the current build. Its binding was left unchanged.");
            RebuildKeys();
            return;
        }
        ActionBindingDraft draft = ModelOf(side).Draft!;
        string[] oldKeys = draft.Keys(action).ToArray();
        if (oldKeys.Contains(key, StringComparer.OrdinalIgnoreCase)) { RebuildKeys(); return; }
        if (draft.CommandOf(key).Length == 0)
        {
            draft.Assign(action, key); RebuildKeys(); return;
        }
        _dialogOpen = true;
        var dialog = new ConfirmationDialog
        {
            Title = "Key already assigned", DialogText = OpenScctGame.Controls.GodotKeys.DisplayName(key) + " currently performs: " + DescribeBinding(draft, key, side) +
                "\nChoose Replace to clear its current actions, Share key to keep them, or Cancel.",
            OkButtonText = "Replace", CancelButtonText = "Cancel", Exclusive = true,
        };
        dialog.AddButton("Share key", false, "share");
        if (oldKeys.Length == 1) dialog.AddButton("Swap", false, "swap");
        void Finish() { _dialogOpen = false; dialog.QueueFree(); RebuildKeys(); }
        dialog.Confirmed += () => { draft.Assign(action, key, replace: true); Finish(); };
        dialog.Canceled += Finish;
        dialog.CustomAction += which =>
        {
            if (which == "swap") draft.Swap(action, oldKeys[0], key);
            else draft.Assign(action, key);
            Finish();
        };
        AddChild(dialog); dialog.PopupCentered(new Vector2I(640, 220));
    }

    private Control RoleMousePage(PlaySide side)
    {
        bool merc = side == PlaySide.Merc;
        var content = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        content.AddChild(Help(merc ? "Sensitivity controls how far the view turns for the same mouse movement. Scope, remote camera and held sensitivity modifiers are under Advanced settings."
            : "Sensitivity controls how far the view turns for the same mouse movement. Free look is used with the weapon holstered; gun look is used with it drawn."));
        content.AddChild(UiStyle.MenuButton("Reset mouse & aim", () => ResetLook(side, false), "ResetMouse"));
        var basic = Grid("MouseAim");
        Check(basic, "Invert vertical mouse movement (both roles)", "invert_y." + side, _edit.InvertY, v =>
        {
            _edit.InvertY = v;
            foreach (PlaySide role in new[] { PlaySide.Spy, PlaySide.Merc })
                if (_controls.TryGetValue("invert_y." + role, out Control? control) && control is CheckBox box)
                    box.SetPressedNoSignal(v);
        });
        if (!merc)
        {
            Check(basic, "Invert horizontal free look", "invert_x_spy", _edit.InvertXSpy, v => _edit.InvertXSpy = v);
            Check(basic, "Shocker lock-on snap when drawing", "shocker_snap", _edit.ShockerSnap, v => _edit.ShockerSnap = v);
        }
        var details = Grid("MouseDetails");
        LookRows(details, merc ? "controls_merc_kbm" : "controls_spy_kbm", () => _edit.LookOf(merc, false), merc);
        // Keep the main sensitivity rows visible; scope, cameras and held modifiers are advanced.
        int basicCells = merc ? 3 : 6;
        foreach (Node child in details.GetChildren().Take(basicCells).ToArray()) { details.RemoveChild(child); basic.AddChild(child); }
        content.AddChild(basic);
        var view = Grid("RoleView");
        double hz = SettingsApplier.MonitorHz;
        int fps = merc ? _edit.FpsMerc : _edit.FpsSpy;
        Choice(view, "Maximum frame rate for this role", merc ? "fps_merc" : "fps_spy", DisplayFrameCap.Choices(fps, hz), DisplayFrameCap.Label(fps, hz),
            v => { if (merc) _edit.FpsMerc = DisplayFrameCap.Parse(v) ?? _edit.FpsMerc; else _edit.FpsSpy = DisplayFrameCap.Parse(v) ?? _edit.FpsSpy; });
        if (merc) Slider(view, "Field of view cap", "ws_fov", GameSettings.MinMercFov, GameSettings.MaxMercFov, 1, _edit.MercFovCap, v => _edit.MercFovCap = v, "0");
        content.AddChild(view);
        var advanced = new VBoxContainer { Visible = false, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        advanced.AddChild(Help("Binoculars / scope multiply the current sensitivity. Camera sensitivity applies to remote camera views. Scope vertical: 1 is legacy speed; 2.45 restores equal horizontal and vertical scope speed."));
        advanced.AddChild(details);
        GridContainer keys = Grid("ModifierKeys");
        ClutchKeyRow(keys, "Hold to modify sensitivity: key", "clutch_one_key." + side, () => _edit.ClutchOneKey, v => _edit.ClutchOneKey = v);
        if (merc) ClutchKeyRow(keys, "Second modifier: key", "clutch_two_key", () => _edit.ClutchTwoKey, v => _edit.ClutchTwoKey = v);
        advanced.AddChild(keys);
        advanced.AddChild(Help("Enable the modifier and assign a key. While held: 0.5× halves mouse sensitivity, 1× keeps it unchanged, 2× doubles it. Release to restore normal speed. The first modifier key is shared by both roles; its strength is role-specific. Both Mercenary modifiers multiply together."));
        AddAdvanced(content, advanced);
        return Page("Mouse & Aim", content);
    }

    private Control RolePadPage(PlaySide side)
    {
        bool merc = side == PlaySide.Merc;
        string prefix = merc ? "controls_merc_pad" : "controls_spy_pad";
        LookSettings Look() => _edit.LookOf(merc, true);
        var content = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        content.AddChild(UiStyle.MenuButton("Reset controller for this role", () => ResetLook(side, true), "ResetController"));
        var basic = Grid("PadAim");
        if (!merc) Slider(basic, "Free look sensitivity (weapon holstered)", prefix + ".free_look", 0.05f, 10f, SensStep, Look().FreeLook, v => Look().FreeLook = v, SensFormat, typed: true);
        Slider(basic, merc ? "Look sensitivity" : "Gun look sensitivity", prefix + ".look", 0.05f, 10f, SensStep, Look().Look, v => Look().Look = v, SensFormat, typed: true);
        Choice(basic, merc ? "Stick response" : "Free look response", prefix + ".look_response", GameSettings.Responses, Look().LookResponse, v => Look().LookResponse = v,
            v => v.Replace("Curve", ""));
        if (!merc) Choice(basic, "Gun look response", prefix + ".gun_look_response", GameSettings.Responses, Look().GunLookResponse,
            v => Look().GunLookResponse = v, v => v.Replace("Curve", ""));
        content.AddChild(basic);
        content.AddChild(Help("Linear turns proportionally to stick input. Soft, Medium and Aggressive boost the outer 25% of each stick axis, reaching 2×, 2.5× and 3× at full input. These are controller response curves, not mouse acceleration."));
        var advanced = new VBoxContainer { Visible = false, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var details = Grid("PadDetails");
        if (!merc) Slider(details, "Run threshold", "controller.run_threshold", 0.1f, 1f, 0.01f, _edit.Pad.RunThreshold, v => _edit.Pad.RunThreshold = v);
        Check(details, "Eight-way movement", merc ? "controller.merc_move8" : "controller.spy_move8",
            merc ? _edit.Pad.MercMove8 : _edit.Pad.SpyMove8, v => { if (merc) _edit.Pad.MercMove8 = v; else _edit.Pad.SpyMove8 = v; });
        LookRows(details, prefix, Look, merc, skipLooks: true);
        advanced.AddChild(details);
        advanced.AddChild(Help("Assign Hold to modify sensitivity to a controller button below. The multiplier scales stick input before the response curve, so its final effect depends on the curve. Both Mercenary modifiers multiply together. Vertical changes up/down speed; scope and camera values apply in their corresponding views."));
        AddAdvanced(content, advanced);
        content.AddChild(Help("For combinations, hold the first control while pressing the second. Hold B uses its separate held assignment. Left stick click selects gadgets with the face buttons."));
        var buttons = Grid("PadButtons");
        RebuildPadButtons(side, buttons);
        _rolePadGrids[side] = buttons;
        content.AddChild(buttons);
        return Page("Controller", content);
    }

    private static void AddAdvanced(VBoxContainer content, Control details)
    {
        var toggle = new CheckButton { Text = "Advanced settings" };
        toggle.Toggled += on => details.Visible = on;
        content.AddChild(toggle); content.AddChild(details);
    }

    private void ResetBindings(PlaySide side)
    {
        var stock = new KeyBindingsModel(!_edit.SplitKeyboard ? PlaySide.Spy : side, staged: true, shared: false, defaults: true);
        ModelOf(side).Draft!.Reset(stock.Draft!.Layout);
    }

    private void ResetLook(PlaySide side, bool pad, bool rebuild = true)
    {
        var defaults = new GameSettings();
        bool merc = side == PlaySide.Merc;
        if (pad)
        {
            _edit.Pad.ResetButtons(merc);
            if (merc) { _edit.MercPad = defaults.MercPad; _edit.Pad.MercMove8 = defaults.Pad.MercMove8; }
            else { _edit.SpyPad = defaults.SpyPad; _edit.Pad.SpyMove8 = defaults.Pad.SpyMove8; _edit.Pad.RunThreshold = defaults.Pad.RunThreshold; }
        }
        else if (merc) { _edit.MercKbm = defaults.MercKbm; _edit.MercFovCap = defaults.MercFovCap; _edit.FpsMerc = defaults.FpsMerc; }
        else { _edit.SpyKbm = defaults.SpyKbm; _edit.InvertXSpy = defaults.InvertXSpy; _edit.ShockerSnap = defaults.ShockerSnap; _edit.FpsSpy = defaults.FpsSpy; }
        if (rebuild) RebuildPages();
    }

    private void ResetRole(PlaySide side)
    {
        ResetBindings(side);
        ResetLook(side, false, rebuild: false);
        ResetLook(side, true, rebuild: false);
        if (side == PlaySide.Merc) _edit.TeammateNamesMerc = false;
        else _edit.TeammateNamesSpy = false;
        var defaults = new GameSettings();
        if (side == PlaySide.Merc) { _edit.Crosshair = new CrosshairSettings(); _edit.MercFovCap = defaults.MercFovCap; _edit.FpsMerc = defaults.FpsMerc; }
        else _edit.FpsSpy = defaults.FpsSpy;
        RebuildPages();
    }

    private void Confirm(string title, string question, Action action, string yes)
    {
        if (_dialogOpen) return;
        _dialogOpen = true;
        ConfirmBox.Open(this, new ConfirmBox
        {
            Name = "OptionsConfirmation", Question = title + "\n" + question, YesCaption = yes, NoCaption = "Cancel",
            Yes = () => { _dialogOpen = false; action(); }, No = () => _dialogOpen = false,
        });
    }

    private void ConfirmDefaults() => Confirm("Reset all settings", "Reset every options page and both keyboard layouts? Changes are staged until Apply.", Defaults, "Reset all");

    private void SaveAndClose()
    {
        if (Save() != Error.Ok) return;
        if (_keep is not null) _closeAfterKeep = true;
        else CloseMenu();
    }
}
