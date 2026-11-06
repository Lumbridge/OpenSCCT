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
        var sharing = new Label
        {
            Name = "BindingSharing", Visible = !_edit.SplitKeyboard,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            AutowrapMode = TextServer.AutowrapMode.WordSmart, MouseFilter = MouseFilterEnum.Pass,
        };
        _sharingLabels[side] = sharing;
        page.AddChild(sharing);
        var sections = new TabContainer { Name = "Sections", SizeFlagsVertical = SizeFlags.ExpandFill };
        sections.AddChild(RoleBindingsPage(side));
        sections.AddChild(RoleMousePage(side));
        sections.AddChild(RolePadPage(side));
        if (merc) sections.AddChild(CrosshairPage());
        page.AddChild(sections);
        return page;
    }

    private Control RoleBindingsPage(PlaySide side) => CreateBindingEditor(side, pad: false);

    // Compatibility for callers using the older key-capture path.
    private void AssignAction(PlaySide side, ControlAction action, string key) =>
        AssignKeyboardBinding(side, action, null, key);

    private string DescribeBinding(ActionBindingDraft draft, string key, PlaySide side) =>
        draft.Describe(key, side == PlaySide.Merc, _edit.MouseWheel);

    private void ShowBindingHelp(PlaySide side, string text)
    {
        if (_bindingHelp.TryGetValue(side, out Label? label))
        {
            label.Text = text;
            label.TooltipText = text;
        }
    }

    private Control RoleMousePage(PlaySide side) => CreateMouseAimEditor(side);

    private Control RolePadPage(PlaySide side) => CreateControllerRoleEditor(side);

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
        else if (merc) { _edit.MercKbm = defaults.MercKbm; _edit.MercFovCap = defaults.MercFovCap; }
        else { _edit.SpyKbm = defaults.SpyKbm; _edit.InvertXSpy = defaults.InvertXSpy; _edit.SpyRollFreeLook = defaults.SpyRollFreeLook; }
        if (rebuild) RebuildPages();
    }

    private void ConfirmResetRole(PlaySide side)
    {
        bool merc = side == PlaySide.Merc;
        string role = merc ? "Mercenary" : "Spy";
        string scope = "keyboard and mouse bindings, controller bindings, sensitivity and response settings, camera options, frame-rate limit and teammate-name display";
        if (merc) scope += ", plus crosshair settings and field of view";
        Confirm("Reset " + role + " settings", "Restore default " + role + " " + scope + "?\nIf keyboard bindings are shared, both roles are affected. Changes take effect when you Apply or Save & Close.",
            () => ResetRole(side), "Reset role");
    }

    private void ResetRole(PlaySide side)
    {
        ResetBindings(side);
        ResetLook(side, false, rebuild: false);
        ResetLook(side, true, rebuild: false);
        if (side == PlaySide.Merc) _edit.TeammateNamesMerc = false;
        else _edit.TeammateNamesSpy = false;
        var defaults = new GameSettings();
        if (side == PlaySide.Merc) { _edit.Crosshair = new CrosshairSettings(); _edit.MercFovCap = defaults.MercFovCap; }
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
