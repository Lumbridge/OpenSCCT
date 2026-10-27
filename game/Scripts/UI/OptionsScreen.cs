using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using OpenScctGame.Hud;
using OpenScctGame.Play.Session;

namespace OpenScctGame.UI;

/// <summary>
/// Options: player name, mouse sensitivity and inversion, and the key bindings of the spy and merc layouts (click a
/// key, then press the new key or mouse button; Escape cancels). Name and mouse go to user://settings.cfg; each key
/// change is saved at once by the input layer.
/// </summary>
public partial class OptionsScreen : Control
{
    private readonly Dictionary<string, Button> _keyButtons = new(StringComparer.OrdinalIgnoreCase);
    private KeyBindingsModel? _model;
    private GridContainer? _keys;
    private Label? _source;
    private Label? _sensitivityValue;
    private string? _listening;

    public LineEdit? NameEdit { get; private set; }

    public HSlider? Sensitivity { get; private set; }

    public CheckBox? Invert { get; private set; }

    public OptionButton? SidePicker { get; private set; }

    public KeyBindingsModel? Model => _model;

    /// <summary>Key buttons by UE2 key name.</summary>
    public IReadOnlyDictionary<string, Button> KeyButtons => _keyButtons;

    /// <summary>The key whose command waits for a new key, or null.</summary>
    public string? Listening => _listening;

    public override void _Ready()
    {
        Theme = UiStyle.Theme;
        SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(UiStyle.Backdrop());
        UiSettings settings = UiSettings.Current;
        VBoxContainer column = UiStyle.CentreColumn(this, 700f);
        column.AddChild(UiStyle.Title("OPTIONS", HudK.SpyColour));

        var grid = new GridContainer { Name = "General", Columns = 3 };
        grid.AddThemeConstantOverride("h_separation", 16);
        grid.AddChild(new Label { Text = "Player name" });
        NameEdit = new LineEdit { Name = "PlayerName", Text = settings.PlayerName, MaxLength = 24, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        grid.AddChild(NameEdit);
        grid.AddChild(new Control());
        grid.AddChild(new Label { Text = "Mouse sensitivity" });
        Sensitivity = new HSlider
        {
            Name = "Sensitivity",
            MinValue = UiSettings.MinSensitivity,
            MaxValue = UiSettings.MaxSensitivity,
            Step = 1,
            Value = settings.MouseSensitivity,
            CustomMinimumSize = new Vector2(280f, 24f),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        _sensitivityValue = new Label { Text = Format(settings.MouseSensitivity), CustomMinimumSize = new Vector2(60f, 0f) };
        Sensitivity.ValueChanged += v => _sensitivityValue.Text = Format((float)v);
        grid.AddChild(Sensitivity);
        grid.AddChild(_sensitivityValue);
        grid.AddChild(new Label { Text = "Invert mouse" });
        Invert = new CheckBox { Name = "Invert", ButtonPressed = settings.InvertMouse };
        grid.AddChild(Invert);
        grid.AddChild(new Control());
        grid.AddChild(new Label { Text = "Key layout" });
        SidePicker = new OptionButton { Name = "Side" };
        SidePicker.AddItem("Spy", (int)PlaySide.Spy);
        SidePicker.AddItem("Mercenary", (int)PlaySide.Merc);
        SidePicker.ItemSelected += index => LoadLayout((PlaySide)SidePicker.GetItemId((int)index));
        grid.AddChild(SidePicker);
        grid.AddChild(new Control());
        column.AddChild(grid);

        _source = new Label { Name = "Source", Modulate = UiStyle.Dim, AutowrapMode = TextServer.AutowrapMode.Arbitrary };
        column.AddChild(_source);
        var scroll = new ScrollContainer { Name = "Bindings", CustomMinimumSize = new Vector2(0f, 300f) };
        _keys = new GridContainer { Columns = 2, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _keys.AddThemeConstantOverride("h_separation", 24);
        scroll.AddChild(_keys);
        column.AddChild(scroll);

        var row = new HBoxContainer { Name = "Buttons", Alignment = BoxContainer.AlignmentMode.Center };
        Button save = UiStyle.MenuButton("Save", () => Save(), "Save");
        Button back = UiStyle.MenuButton("Back", () => FrontEnd.ShowMainMenu(GetTree()), "Back");
        foreach (Button b in new[] { save, back })
        {
            b.CustomMinimumSize = new Vector2(200f, 44f);
            row.AddChild(b);
        }

        column.AddChild(row);
        LoadLayout(PlaySide.Spy);
    }

    private static string Format(float v) => v.ToString("0", CultureInfo.InvariantCulture);

    /// <summary>Shows the key layout of <paramref name="side"/>.</summary>
    public void LoadLayout(PlaySide side)
    {
        _model = new KeyBindingsModel(side);
        _listening = null;
        RebuildKeys();
    }

    private void RebuildKeys()
    {
        if (_keys is null || _model is null)
        {
            return;
        }

        foreach (Node child in _keys.GetChildren())
        {
            _keys.RemoveChild(child);
            child.QueueFree();
        }

        _keyButtons.Clear();
        foreach (KeyBindingRow r in _model.Rows())
        {
            _keys.AddChild(new Label { Text = r.Label, SizeFlagsHorizontal = SizeFlags.ExpandFill, ClipText = true, CustomMinimumSize = new Vector2(420f, 0f) });
            string key = r.Key;
            var button = new Button { Name = key, Text = key, CustomMinimumSize = new Vector2(200f, 30f) };
            button.Pressed += () => Listen(key);
            _keyButtons[key] = button;
            _keys.AddChild(button);
        }

        if (_source is not null)
        {
            _source.Text = "Layout: " + _model.Source;
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
        if (_listening is null || @event is not (InputEventKey { Pressed: true } or InputEventMouseButton { Pressed: true }))
        {
            return;
        }

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
    }

    /// <summary>Moves the command of <paramref name="oldKey"/> to <paramref name="newKey"/> (from a key press or directly).</summary>
    public void Rebind(string oldKey, string newKey)
    {
        _listening = null;
        _model?.Rebind(oldKey, newKey);
        RebuildKeys();
    }

    /// <summary>Stores the name and mouse values and saves them.</summary>
    public Error Save()
    {
        UiSettings settings = UiSettings.Current;
        if (NameEdit is not null && NameEdit.Text.Trim().Length > 0)
        {
            settings.PlayerName = NameEdit.Text.Trim();
        }

        if (Sensitivity is not null)
        {
            settings.MouseSensitivity = (float)Sensitivity.Value;
        }

        if (Invert is not null)
        {
            settings.InvertMouse = Invert.ButtonPressed;
        }

        return settings.Save();
    }
}
