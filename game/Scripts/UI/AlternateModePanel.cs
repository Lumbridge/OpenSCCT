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
public partial class AlternateModePanel : PanelContainer
{
    private readonly bool _editable;
    private readonly Action<AlternateRules>? _ok;
    private readonly Dictionary<AlternateAbility, CheckBox> _abilities = new();
    private readonly CheckBox[,] _gadgets = new CheckBox[2, AlternateRules.GadgetOptions];
    private readonly SpinBox[] _slots = new SpinBox[2];
    private AlternateRules _rules;
    private readonly AlternateRules _original;

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
        SetAnchorsPreset(LayoutPreset.Center);
        AnchorLeft = 0.18f;
        AnchorRight = 0.82f;
        AnchorTop = 0.12f;
        AnchorBottom = 0.88f;
        AddThemeStyleboxOverride("panel", UiStyle.MenuPanelBox());
        var column = new VBoxContainer { Name = "Column" };
        column.AddThemeConstantOverride("separation", 8);
        AddChild(column);
        column.AddChild(new Label
        {
            Name = "Title", Text = "Alternate Mode", HorizontalAlignment = HorizontalAlignment.Center, Modulate = UiStyle.MenuTitle,
        });
        if (!_editable)
        {
            column.AddChild(new Label
            {
                Name = "ReadOnly", Text = "Set by the host", HorizontalAlignment = HorizontalAlignment.Center, Modulate = UiStyle.MenuFocus,
            });
        }

        var scroll = new ScrollContainer
        {
            Name = "Scroll", SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        column.AddChild(scroll);
        var sides = new HBoxContainer { Name = "Sides", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        sides.AddThemeConstantOverride("separation", 24);
        scroll.AddChild(sides);
        BuildSide(sides, 0, "Spies", AlternateRules.SpyAbilityRows, new STeamAttaquant().ClassInventory);
        BuildSide(sides, 1, "Mercenaries", AlternateRules.MercAbilityRows, new STeamDefenseur().ClassInventory);

        CheckBox cinematic = Check("Cinematic Camera", "CinematicCamera");
        cinematic.Toggled += on => Change(_rules.With(AlternateAbility.CinematicCamera, on));
        _abilities[AlternateAbility.CinematicCamera] = cinematic;
        column.AddChild(cinematic);

        var buttons = new HBoxContainer { Name = "Buttons", Alignment = BoxContainer.AlignmentMode.Center };
        buttons.AddThemeConstantOverride("separation", 16);
        if (_editable)
        {
            Button ok = UiStyle.MenuButton("OK", Accept, "AlternateOk");
            ok.CustomMinimumSize = new Vector2(160f, 40f);
            buttons.AddChild(ok);
        }

        Button close = UiStyle.MenuButton(_editable ? "Cancel" : "Close", RequestClose, "AlternateClose");
        close.CustomMinimumSize = new Vector2(160f, 40f);
        buttons.AddChild(close);
        column.AddChild(buttons);
        ShowRules(_rules);
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
                int allowed = BitOperations.PopCount(team == 0 ? rules.SpyGadgets : rules.MercGadgets);
                spin.MaxValue = Math.Min(AlternateRules.MaxGadgetCount, allowed);
                spin.SetValueNoSignal(team == 0 ? rules.SpyGadgetCount : rules.MercGadgetCount);
            }
        }
    }

    private void BuildSide(HBoxContainer parent, int team, string title, IReadOnlyList<(AlternateAbility Ability, string Caption)> abilities,
        List<ClassRef> gadgets)
    {
        var side = new VBoxContainer { Name = title, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        parent.AddChild(side);
        side.AddChild(new Label { Text = title, Modulate = UiStyle.MenuFocus });
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
