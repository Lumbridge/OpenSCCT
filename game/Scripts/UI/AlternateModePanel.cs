using System;
using System.Collections.Generic;
using System.Numerics;
using Godot;
using OpenScct.Core.Uc;
using OpenScct.Game.SBase;
using OpenScct.Server.Online;
using Vector2 = Godot.Vector2;

namespace OpenScctGame.UI;

/// <summary>
/// Enhanced 4.0's Alternate Mode page (GUI.AlternateModeSettings; spec/game/lobby-and-match-creation.md "Alternate
/// Mode"; docs/MENUS.md): the spies' abilities, their eight gadgets and "Number of gadgets" on the left, the mercs'
/// on the right, and the cinematic camera. The host edits it (from Create Session, or the lobby's "Alternate" button)
/// and OK hands the values back; for everyone else it is read only. "Number of gadgets" is 0 to 4, and no more than
/// the gadgets left allowed when four or fewer are.
/// </summary>
public partial class AlternateModePanel : Control
{
    private readonly bool _editable;
    private readonly Action<AlternateRules>? _ok;
    private readonly Dictionary<AlternateAbility, CheckBox> _abilities = new();
    private readonly CheckBox[,] _gadgets = new CheckBox[2, AlternateRules.GadgetOptions];
    private readonly SpinBox[] _slots = new SpinBox[2];
    private AlternateRules _rules;
    private readonly AlternateRules _original;
    private PanelContainer? _panel;
    private BoxContainer? _sides;
    private readonly Dictionary<Control, FocusModeEnum> _backgroundFocus = new();
    private Control? _previousFocus;

    /// <param name="rules">The values the page opens with.</param>
    /// <param name="editable">The host may change them (OK then gives them back through <paramref name="ok"/>).</param>
    public AlternateModePanel(AlternateRules rules, bool editable, Action<AlternateRules>? ok)
    {
        _rules = rules;
        _original = rules;
        _editable = editable;
        _ok = ok;
        Name = "AlternateMode";
    }

    /// <summary>The values as the page shows them now.</summary>
    public AlternateRules Rules => _rules;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        Theme = UiStyle.MenuTheme;
        _previousFocus = GetViewport().GuiGetFocusOwner();
        foreach (Node node in GetParent().FindChildren("*", nameof(Control), true, false))
            if (node is Control control && control != this && !IsAncestorOf(control) && control.FocusMode != FocusModeEnum.None)
            {
                _backgroundFocus[control] = control.FocusMode;
                control.FocusMode = FocusModeEnum.None;
            }
        var dim = new ColorRect { Name = "Dim", Color = new Color(0, 0, 0, 0.8f), MouseFilter = MouseFilterEnum.Stop };
        dim.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); AddChild(dim);
        _panel = new PanelContainer { Name = "AlternatePanel", MouseFilter = MouseFilterEnum.Stop };
        _panel.AddThemeStyleboxOverride("panel", UiStyle.MenuPanelBox());
        AddChild(_panel);
        var margin = new MarginContainer();
        foreach (string edge in new[] { "left", "right", "top", "bottom" }) margin.AddThemeConstantOverride("margin_" + edge, 12);
        _panel.AddChild(margin);
        var column = new VBoxContainer { Name = "Column" };
        column.AddThemeConstantOverride("separation", 14);
        margin.AddChild(column);
        var title = new Label { Name = "Title", Text = "Alternate rules", Modulate = UiStyle.MenuFocus };
        title.AddThemeFontSizeOverride("font_size", 26); column.AddChild(title);
        column.AddChild(LobbyPanels.Text(_editable ? "Choose which abilities and gadgets each team can use. Save rules when you are done."
            : "The host chooses which abilities and gadgets each team can use."));

        var scroll = new ScrollContainer
        {
            Name = "Scroll", FollowFocus = true, SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        column.AddChild(scroll);
        var sides = new BoxContainer { Name = "Sides", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _sides = sides;
        sides.AddThemeConstantOverride("separation", 14);
        scroll.AddChild(sides);
        BuildSide(sides, 0, "Spies", AlternateRules.SpyAbilityRows, new STeamAttaquant().ClassInventory);
        BuildSide(sides, 1, "Mercenaries", AlternateRules.MercAbilityRows, new STeamDefenseur().ClassInventory);

        CheckBox cinematic = Check("Cinematic Camera", "CinematicCamera");
        cinematic.Toggled += on => Change(_rules.With(AlternateAbility.CinematicCamera, on));
        _abilities[AlternateAbility.CinematicCamera] = cinematic;
        column.AddChild(cinematic);

        var buttons = new HBoxContainer { Name = "Buttons", Alignment = BoxContainer.AlignmentMode.End };
        buttons.AddThemeConstantOverride("separation", 16);
        Button close = UiStyle.MenuButton(_editable ? "Cancel" : "Close", RequestClose, "AlternateClose");
        close.CustomMinimumSize = new Vector2(140f, 42f);
        buttons.AddChild(close);
        if (_editable)
        {
            Button ok = UiStyle.MenuButton("Save rules", Accept, "AlternateOk");
            ok.CustomMinimumSize = new Vector2(160f, 42f);
            ok.AddThemeColorOverride("font_color", UiStyle.MenuFocus);
            buttons.AddChild(ok);
        }
        column.AddChild(buttons);
        ShowRules(_rules);
        FitPanel();
        close.GrabFocus();
    }

    public override void _Process(double delta) => FitPanel();

    private void FitPanel()
    {
        if (_panel is null) return;
        LobbyPanels.FitDialog(_panel, Size, new Vector2(1000, 780));
        if (_sides is not null) _sides.Vertical = _panel.Size.X < 680;
    }

    public override void _ExitTree()
    {
        foreach (var entry in _backgroundFocus)
            if (IsInstanceValid(entry.Key)) entry.Key.FocusMode = entry.Value;
        if (IsInstanceValid(_previousFocus) && _previousFocus!.IsVisibleInTree()) _previousFocus.GrabFocus();
    }

    /// <summary>Shows other values (a client's page follows the host's setup).</summary>
    public void ShowRules(AlternateRules rules)
    {
        _rules = rules;
        foreach ((AlternateAbility a, CheckBox box) in _abilities)
        {
            box.SetPressedNoSignal(rules.Has(a));
        }

        for (int team = 0; team < 2; team++)
        {
            for (int g = 0; g < AlternateRules.GadgetOptions; g++)
            {
                _gadgets[team, g]?.SetPressedNoSignal(rules.GadgetAllowed(team, g));
            }

            if (_slots[team] is { } spin)
            {
                int allowed = team == 0 ? rules.AvailableSpyGadgets : BitOperations.PopCount(rules.MercGadgets);
                spin.MaxValue = Math.Min(AlternateRules.MaxGadgetCount, allowed);
                spin.SetValueNoSignal(team == 0 ? rules.SpyGadgetCount : rules.MercGadgetCount);
            }
        }
    }

    private void BuildSide(BoxContainer parent, int team, string title, IReadOnlyList<(AlternateAbility Ability, string Caption)> abilities,
        List<ClassRef> gadgets)
    {
        var side = LobbyPanels.Card(parent, title, title);
        side.AddChild(new Label { Text = "Abilities", Modulate = UiStyle.MenuFocus });
        foreach ((AlternateAbility ability, string caption) in abilities)
        {
            CheckBox box = Check(caption, ability.ToString());
            box.Toggled += on => Change(_rules.With(ability, on));
            _abilities[ability] = box;
            side.AddChild(box);
        }

        side.AddChild(new Label { Text = "Gadgets", Modulate = UiStyle.MenuFocus });
        for (int g = 0; g < AlternateRules.GadgetOptions; g++)
        {
            if (g < gadgets.Count && OpenScct.Game.Equipment.SpyCloak.IsLegacyGadget(gadgets[g].Path)) continue;
            int option = g;
            string name = g < gadgets.Count ? GearMenuModel.NameOf(gadgets[g].Path ?? string.Empty) : "Gadget " + (g + 1);
            CheckBox box = Check(name, (team == 0 ? "SpyGadget" : "MercGadget") + (g + 1));
            box.Toggled += on => Change(_rules.WithGadget(team, option, on).Clamped());
            _gadgets[team, g] = box;
            side.AddChild(box);
        }

        var line = new HBoxContainer { Name = "Slots" };
        line.AddChild(new Label { Text = "Number of gadgets", SizeFlagsHorizontal = SizeFlags.ExpandFill });
        var spin = new SpinBox
        {
            Name = team == 0 ? "SpySlots" : "MercSlots", MinValue = 0, MaxValue = AlternateRules.MaxGadgetCount, Step = 1, Rounded = true,
            Editable = _editable,
        };
        spin.ValueChanged += v => Change(team == 0 ? _rules with { SpyGadgetCount = (byte)v } : _rules with { MercGadgetCount = (byte)v });
        _slots[team] = spin;
        line.AddChild(spin);
        side.AddChild(line);
    }

    private CheckBox Check(string caption, string name) => new()
    {
        Name = name, Text = caption, Disabled = !_editable, FocusMode = _editable ? FocusModeEnum.All : FocusModeEnum.None,
        CustomMinimumSize = new Vector2(0, 32),
    };

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape }) return;
        GetViewport().SetInputAsHandled();
        RequestClose();
    }

    private void RequestClose()
    {
        if (_editable && _rules != _original)
            ConfirmBox.Open(this, new ConfirmBox
            {
                Question = "Discard unapplied alternate rules?", YesCaption = "Discard",
                NoCaption = "Keep editing", Yes = QueueFree,
            });
        else QueueFree();
    }

    private void Change(AlternateRules next)
    {
        if (!_editable)
        {
            ShowRules(_rules);
            return;
        }

        ShowRules(next.Clamped());
    }

    private void Accept()
    {
        _ok?.Invoke(_rules.Clamped());
        QueueFree();
    }
}
