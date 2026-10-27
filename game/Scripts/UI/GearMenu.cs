using System;
using Godot;
using OpenScctGame.Hud;

namespace OpenScctGame.UI;

/// <summary>
/// Draws a <see cref="GearMenuModel"/> (spec/additions/gear-change.md): a framed box with one row per gadget slot
/// (and the merc's weapon), each with arrows that cycle it, the rule for when new gear goes on, and Apply and Back.
/// A null model (no side yet) shows only a note and Back.
/// </summary>
public partial class GearMenu : Control
{
    private const float RowHeight = 40f;
    private const float ArrowWidth = 44f;

    private readonly GearMenuModel? _model;
    private Label[] _slotLabels = [];
    private Label? _weaponLabel;

    public GearMenu(GearMenuModel? model)
    {
        Name = "GearMenu";
        _model = model;
    }

    /// <summary>Apply: the model holds the choice to save.</summary>
    public event Action<GearMenuModel>? Applied;

    /// <summary>Back (or Escape through the menu model).</summary>
    public event Action? Closed;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        Build(GetViewportRect().Size);
    }

    private void Build(Vector2 size)
    {
        float width = Math.Max(size.X * 0.5f, 520f);
        float left = (size.X - width) * 0.5f;
        float top = size.Y * 0.2f;
        var box = new Panel
        {
            Name = "Box",
            Position = new Vector2(left, top),
            Size = new Vector2(width, size.Y * 0.6f),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        box.AddThemeStyleboxOverride("panel", UiStyle.MenuPanelBox());
        AddChild(box);

        Label title = UiStyle.Title(GearMenuModel.Title, HudK.SpyColour, 32);
        title.Name = "Title";
        title.Position = new Vector2(left, top + 12f);
        title.Size = new Vector2(width, 44f);
        AddChild(title);

        float y = top + 72f;
        Button? first = null;
        if (_model is null)
        {
            AddText("Join a side to choose your gear.", new Rect2(left, y, width, RowHeight));
            y += RowHeight * 2f;
        }
        else
        {
            _slotLabels = new Label[_model.Slots];
            for (int i = 0; i < _model.Slots; i++)
            {
                int slot = i;
                Button prev = Row($"Slot {i + 1}", left, y, width, out _slotLabels[i], () => Cycle(slot, -1), () => Cycle(slot, 1));
                first ??= prev;
                y += RowHeight + 6f;
            }

            if (_model.Weapons.Count > 1)
            {
                Button prev = Row(GearMenuModel.Weapon, left, y, width, out Label weapon, () => CycleWeapon(-1), () => CycleWeapon(1));
                _weaponLabel = weapon;
                first ??= prev;
                y += RowHeight + 6f;
            }

            Refresh();
            var rule = new Label
            {
                Name = "Rule",
                Text = _model.Rule,
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                HorizontalAlignment = HorizontalAlignment.Center,
                Position = new Vector2(left + 24f, y + 8f),
                Size = new Vector2(width - 48f, RowHeight * 1.6f),
                Modulate = UiStyle.Dim,
            };
            AddChild(rule);
            y += RowHeight * 2f;
        }

        float buttonWidth = Math.Max(width * 0.3f, 160f);
        float buttonTop = Math.Max(y + 8f, top + size.Y * 0.6f - RowHeight - 20f);
        if (_model is not null)
        {
            Button apply = Button(GearMenuModel.Apply, left + width * 0.3f - buttonWidth * 0.5f, buttonTop, buttonWidth, () => Applied?.Invoke(_model));
            first ??= apply;
        }

        float backCentre = _model is null ? left + width * 0.5f : left + width * 0.7f;
        Button back = Button(GearMenuModel.Back, backCentre - buttonWidth * 0.5f, buttonTop, buttonWidth, () => Closed?.Invoke());
        first ??= back;
        first.CallDeferred(Control.MethodName.GrabFocus);
    }

    /// <summary>A row: its caption, a previous arrow, the chosen name, a next arrow. Returns the previous arrow.</summary>
    private Button Row(string caption, float left, float y, float width, out Label value, Action prev, Action next)
    {
        AddText(caption, new Rect2(left + 16f, y, width * 0.25f, RowHeight), HorizontalAlignment.Left);
        float valueLeft = left + width * 0.3f;
        float valueWidth = width * 0.7f - 16f - 2f * ArrowWidth;
        Button before = Button("<", valueLeft, y, ArrowWidth, prev);
        value = AddText("", new Rect2(valueLeft + ArrowWidth, y, valueWidth, RowHeight));
        Button after = Button(">", valueLeft + ArrowWidth + valueWidth, y, ArrowWidth, next);
        before.FocusNeighborRight = after.GetPath();
        after.FocusNeighborLeft = before.GetPath();
        return before;
    }

    private Button Button(string text, float x, float y, float width, Action pressed)
    {
        var b = new Button { Text = text, Position = new Vector2(x, y), Size = new Vector2(width, RowHeight) };
        b.Pressed += pressed;
        AddChild(b);
        return b;
    }

    private Label AddText(string text, Rect2 rect, HorizontalAlignment align = HorizontalAlignment.Center)
    {
        var label = new Label
        {
            Text = text,
            HorizontalAlignment = align,
            VerticalAlignment = VerticalAlignment.Center,
            Position = rect.Position,
            Size = rect.Size,
            Modulate = UiStyle.Text,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(label);
        return label;
    }

    private void Cycle(int slot, int step)
    {
        _model?.Cycle(slot, step);
        Refresh();
    }

    private void CycleWeapon(int step)
    {
        _model?.CycleWeapon(step);
        Refresh();
    }

    private void Refresh()
    {
        if (_model is null)
        {
            return;
        }

        for (int i = 0; i < _slotLabels.Length; i++)
        {
            _slotLabels[i].Text = GearMenuModel.NameOf(_model.Gadgets[i]);
        }

        if (_weaponLabel is not null)
        {
            _weaponLabel.Text = GearMenuModel.NameOf(_model.ChosenWeapon);
        }
    }
}
