using System;
using Godot;
using OpenScctGame.Hud;
using OpenScctGame.UI;

namespace OpenScctGame.Options;

/// <summary>
/// The Crosshair page: the merc's CS2-style custom crosshair (<see cref="CrosshairSettings"/>) with a live preview
/// and CS2 share code import and copy. Off, the original reticle draws.
/// </summary>
public partial class OptionsMenu
{
    private Label? _crosshairCodeNote;

    private Control CrosshairPage()
    {
        CrosshairSettings c = _edit.Crosshair;
        var page = new VBoxContainer { Name = "CrosshairContent", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        page.AddThemeConstantOverride("separation", 10);

        var top = new HBoxContainer { Name = "Top" };
        top.AddThemeConstantOverride("separation", 16);
        top.AddChild(new CrosshairPreview(() => _edit.Crosshair) { Name = "Preview", CustomMinimumSize = new Vector2(260f, 160f) });

        var share = new VBoxContainer { Name = "Share", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        share.AddChild(new Label
        {
            Text = "Mercenary crosshair. Off, the original reticle draws. The sound arrows and the red flash on a hit stay either way.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart, Modulate = UiStyle.Dim,
        });
        var code = new LineEdit
        {
            Name = "crosshair_code", PlaceholderText = "CS2 share code (CS...)", SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        _controls["crosshair_code"] = code;
        share.AddChild(code);
        var buttons = new HBoxContainer();
        var import = new Button { Name = "CrosshairImport", Text = "Import code", CustomMinimumSize = new Vector2(150f, 32f) };
        import.Pressed += () => ImportCrosshair(code.Text);
        var copy = new Button { Name = "CrosshairCopy", Text = "Copy my code", CustomMinimumSize = new Vector2(150f, 32f) };
        copy.Pressed += () =>
        {
            string mine = _edit.Crosshair.ToShareCode();
            code.Text = mine;
            DisplayServer.ClipboardSet(mine);
            SetCrosshairNote("Copied. It pastes into CS2 too.");
        };
        var reset = new Button { Name = "CrosshairReset", Text = "Reset crosshair", CustomMinimumSize = new Vector2(150f, 32f) };
        reset.Pressed += () =>
        {
            _edit.Crosshair = new CrosshairSettings { Custom = _edit.Crosshair.Custom };
            RebuildPages();
        };
        buttons.AddChild(import);
        buttons.AddChild(copy);
        buttons.AddChild(reset);
        share.AddChild(buttons);
        _crosshairCodeNote = new Label { Name = "CrosshairCodeNote", Modulate = UiStyle.Dim, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        share.AddChild(_crosshairCodeNote);
        top.AddChild(share);
        page.AddChild(top);

        GridContainer g = Grid("Crosshair");
        Check(g, "Custom crosshair", "crosshair.custom", c.Custom, v => _edit.Crosshair.Custom = v);
        Choice(g, "Style", "crosshair.style", CrosshairSettings.StyleNames, CrosshairSettings.StyleNames[c.Style],
            v => _edit.Crosshair.Style = Array.IndexOf(CrosshairSettings.StyleNames, v));
        ColourRow(g, "Colour", "crosshair.colour", c.Red, c.Green, c.Blue, c.Alpha, col =>
        {
            _edit.Crosshair.Red = Byte(col.R);
            _edit.Crosshair.Green = Byte(col.G);
            _edit.Crosshair.Blue = Byte(col.B);
            _edit.Crosshair.Alpha = Byte(col.A);
        });
        Slider(g, "Length", "crosshair.length", 0f, 60f, 1f, c.Length, v => _edit.Crosshair.Length = (int)v, "0");
        Slider(g, "Thickness", "crosshair.thickness", 0f, 32f, 1f, c.Thickness, v => _edit.Crosshair.Thickness = (int)v, "0");
        Slider(g, "Gap", "crosshair.gap", -20f, 40f, 1f, Math.Clamp(c.Gap, -20, 40), v => _edit.Crosshair.Gap = (int)v, "0");
        Check(g, "Centre dot", "crosshair.center_dot", c.CenterDot, v => _edit.Crosshair.CenterDot = v);
        Check(g, "T style (no top line)", "crosshair.t_style", c.TStyle, v => _edit.Crosshair.TStyle = v);
        Choice(g, "Outline", "crosshair.outline_mode", CrosshairSettings.OutlineNames, CrosshairSettings.OutlineNames[c.OutlineMode],
            v => _edit.Crosshair.OutlineMode = Array.IndexOf(CrosshairSettings.OutlineNames, v));
        ColourRow(g, "Outline colour", "crosshair.outline_colour", c.OutlineRed, c.OutlineGreen, c.OutlineBlue, c.OutlineAlpha, col =>
        {
            _edit.Crosshair.OutlineRed = Byte(col.R);
            _edit.Crosshair.OutlineGreen = Byte(col.G);
            _edit.Crosshair.OutlineBlue = Byte(col.B);
            _edit.Crosshair.OutlineAlpha = Byte(col.A);
        });
        Check(g, "Follow recoil (moves with the aim kick)", "crosshair.follow_recoil", c.FollowRecoil, v => _edit.Crosshair.FollowRecoil = v);
        Check(g, "Red flash on a hit (as the original)", "crosshair.hit_flash", c.HitFlash, v => _edit.Crosshair.HitFlash = v);
        Slider(g, "Dynamic spread limit", "crosshair.dynamic_spread_limit", 0f, 255f, 1f, c.DynamicSpreadLimit,
            v => _edit.Crosshair.DynamicSpreadLimit = (int)v, "0");
        Slider(g, "Split distance (split style)", "crosshair.split_distance", 0f, 127f, 1f, c.SplitDistance,
            v => _edit.Crosshair.SplitDistance = (int)v, "0");
        Slider(g, "Inner split opacity", "crosshair.inner_split_alpha", 0f, 1f, 0.01f, c.InnerSplitAlpha, v => _edit.Crosshair.InnerSplitAlpha = v);
        Slider(g, "Outer split opacity", "crosshair.outer_split_alpha", 0.3f, 1f, 0.01f, c.OuterSplitAlpha, v => _edit.Crosshair.OuterSplitAlpha = v);
        Slider(g, "Outer split size", "crosshair.split_size_ratio", 0f, 1f, 0.01f, c.SplitSizeRatio, v => _edit.Crosshair.SplitSizeRatio = v);
        Slider(g, "Sizes are pixels at screen height", "crosshair.screen_height", 240f, 2160f, 1f, Math.Clamp(c.ScreenHeight, 240, 2160),
            v => _edit.Crosshair.ScreenHeight = (int)v, "0");
        page.AddChild(g);
        return Page("Crosshair", page);
    }

    private void ImportCrosshair(string text)
    {
        if (CrosshairSettings.FromShareCode(text, _edit.Crosshair) is not { } imported)
        {
            SetCrosshairNote("That is not a CS2 crosshair code (they start with CS and have no dashes; old CSGO- codes are not read).");
            return;
        }

        _edit.Crosshair = imported;
        RebuildPages();
        if (_controls.TryGetValue("crosshair_code", out Control? box) && box is LineEdit edit)
        {
            edit.Text = text.Trim();
        }

        SetCrosshairNote("Imported. Save to keep it.");
    }

    private void SetCrosshairNote(string text)
    {
        // A rebuild makes a new page; the note there is the one to fill.
        if (_crosshairCodeNote is { } note && IsInstanceValid(note))
        {
            note.Text = text;
        }
    }

    private void ColourRow(GridContainer grid, string label, string key, int r, int g, int b, int a, Action<Color> changed)
    {
        var picker = new ColorPickerButton
        {
            Name = key.Replace('.', '_'), Color = new Color(r / 255f, g / 255f, b / 255f, a / 255f), EditAlpha = true,
            CustomMinimumSize = new Vector2(320f, 28f), SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        picker.ColorChanged += col => changed(col);
        grid.AddChild(new Label { Text = label, CustomMinimumSize = new Vector2(320f, 0f) });
        grid.AddChild(picker);
        grid.AddChild(new Control());
        _controls[key] = picker;
    }

    private static int Byte(float v) => Math.Clamp((int)MathF.Round(v * 255f), 0, 255);
}

/// <summary>
/// A live look at the custom crosshair on a dark and a light half (so the outline shows), at the HUD's size for this
/// screen. The dynamic styles breathe as if firing. Off, it says the original reticle draws.
/// </summary>
public partial class CrosshairPreview : Control
{
    private readonly Func<CrosshairSettings> _of;
    private float _time;

    public CrosshairPreview(Func<CrosshairSettings> of)
    {
        _of = of;
        ClipContents = true;
    }

    // Godot builds nodes it restores with no arguments.
    public CrosshairPreview()
        : this(() => new CrosshairSettings())
    {
    }

    public override void _Process(double delta)
    {
        _time += (float)delta;
        QueueRedraw();
    }

    public override void _Draw()
    {
        Vector2 size = Size;
        DrawRect(new Rect2(0f, 0f, size.X / 2f, size.Y), new Color(0.08f, 0.1f, 0.09f));
        DrawRect(new Rect2(size.X / 2f, 0f, size.X / 2f, size.Y), new Color(0.62f, 0.64f, 0.6f));
        CrosshairSettings c = _of();
        if (!c.Custom)
        {
            DrawString(ThemeDB.FallbackFont, new Vector2(8f, size.Y - 10f), "Off: the original reticle", HorizontalAlignment.Left, -1f, 14,
                new Color(1f, 1f, 1f, 0.7f));
            return;
        }

        // The HUD's scale for this screen's height, and a spread that opens and closes like steady fire.
        float screenHeight = GetViewportRect().Size.Y;
        float spread = c.IsDynamic ? (0.5f - 0.5f * MathF.Cos(_time * 2.2f)) * 18f * screenHeight / 480f : 0f;
        foreach (CrosshairQuad q in CrosshairShape.Build(c, size / 2f, screenHeight, spread, hit: false))
        {
            DrawColoredPolygon([q.A, q.B, q.C, q.D], q.Colour);
        }
    }
}
