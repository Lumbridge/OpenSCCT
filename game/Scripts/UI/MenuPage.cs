using System;
using System.Collections.Generic;
using Godot;
using OpenScctGame.Hud;

namespace OpenScctGame.UI;

/// <summary>
/// The look of the original's PC menu pages (GUI.GUIPage and the sSTY_Pc* styles; docs/MENUS.md): the page background
/// (GUIContent "fond"), the title typed out ten letters a second with a blinking cursor, the four corner brackets
/// (GUIContent "MN_cadre03"), the framed panels (Menu_PC "Saquare_BOX"), and the page buttons spread along the bottom
/// (B first, then X, Y and A, from 5% across 70% of the width). The original pages are UnrealScript GUI pages, not
/// Magma pages, so they are rebuilt here with the converted textures (the ui step's GUIContent and Menu_PC groups) and
/// the decoded style colours; without converted content the page falls back to plain colours. That is the
/// <see cref="UiLook.Original"/> look; the default <see cref="UiLook.Modern"/> look keeps the same layout and draws our
/// own art instead (<see cref="UiArt"/>: shader background, <see cref="UiCorner"/> brackets, <see cref="UiBox"/> panels
/// and buttons, thin rules under the title and above the buttons, the bundled fonts).
/// </summary>
public partial class MenuPage : Control
{
    /// <summary>GUIPage: TimeTitle += DeltaTime * 10 (letters a second).</summary>
    public const float TitleLettersPerSecond = 10f;

    /// <summary>GUIPage.MyDrawPage: the title's top in the 640 x 480 page.</summary>
    public const float TitleTop = 35f;

    private readonly List<Button> _pageButtons = new();
    private Label? _title;
    private string _titleText = string.Empty;
    private float _titleTime;
    private HBoxContainer? _bar;

    /// <summary>The page's content, between the title and the page buttons.</summary>
    protected VBoxContainer Body { get; private set; } = null!;

    /// <summary>The page title (typed out when the page opens).</summary>
    public string TitleText
    {
        get => _titleText;
        set
        {
            _titleText = value ?? string.Empty;
            _titleTime = 0f;
        }
    }

    /// <summary>The page buttons in bar order (B, X, Y, A).</summary>
    public IReadOnlyList<Button> PageButtons => _pageButtons;

    /// <summary>Every menu page frees the mouse pointer, however it was reached (a round's end, a disconnect, the In-Game Menu).</summary>
    public override void _EnterTree() => OpenScctGame.Controls.Pointer.Release();

    /// <summary>Builds the frame: background, title, corner brackets, body and the button bar. Call first in _Ready.</summary>
    protected void BuildFrame(string title, float bodyWidth = 1100f)
    {
        Theme = UiStyle.MenuTheme;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(UiStyle.MenuBackdrop());
        _titleText = title;
        _title = new Label
        {
            Name = "Title", HorizontalAlignment = HorizontalAlignment.Center, Position = new Vector2(0f, 24f),
            LabelSettings = new LabelSettings { FontSize = 30, FontColor = UiStyle.MenuTitle, ShadowColor = new Color(0f, 0f, 0f, 0.7f), ShadowOffset = new Vector2(2f, 2f) },
        };
        _title.SetAnchorsPreset(LayoutPreset.TopWide);
        _title.OffsetTop = 24f;
        if (!UiStyle.IsOriginal)
        {
            // The modern title: Inter, spaced out, capitals, a faint green glow.
            _title.LabelSettings.Font = UiArt.TitleFont;
            _title.LabelSettings.FontSize = UiArt.TitleSize;
            _title.LabelSettings.FontColor = UiArt.MenuTitle;
            _title.LabelSettings.ShadowColor = UiArt.Accent with { A = 0.25f };
            _title.LabelSettings.ShadowOffset = Vector2.Zero;
            _title.LabelSettings.ShadowSize = 10;
            _title.Uppercase = true;
            AddRule("TitleRule", top: true);
            AddRule("ButtonRule", top: false);
        }

        AddChild(_title);
        foreach ((string name, Vector2 anchor, bool flipH, bool flipV) in new[]
                 {
                     ("CornerTL", new Vector2(0f, 0f), true, false), ("CornerTR", new Vector2(1f, 0f), false, false),
                     ("CornerBR", new Vector2(1f, 1f), false, true), ("CornerBL", new Vector2(0f, 1f), true, true),
                 })
        {
            if (!UiStyle.IsOriginal)
            {
                AddChild(new UiCorner(flipH, flipV)
                {
                    Name = name, AnchorLeft = anchor.X, AnchorRight = anchor.X, AnchorTop = anchor.Y, AnchorBottom = anchor.Y,
                    OffsetLeft = anchor.X > 0 ? -76f : 14f, OffsetRight = anchor.X > 0 ? -14f : 76f,
                    OffsetTop = anchor.Y > 0 ? -62f : 12f, OffsetBottom = anchor.Y > 0 ? -12f : 62f,
                });
            }
            else if (UiStyle.MenuTexture("GUIContent", "MN_cadre03") is { } corner)
            {
                var c = new TextureRect
                {
                    Name = name, Texture = corner, FlipH = flipH, FlipV = flipV, MouseFilter = MouseFilterEnum.Ignore,
                    AnchorLeft = anchor.X, AnchorRight = anchor.X, AnchorTop = anchor.Y, AnchorBottom = anchor.Y,
                    OffsetLeft = anchor.X > 0 ? -42f : 10f, OffsetRight = anchor.X > 0 ? -10f : 42f,
                    OffsetTop = anchor.Y > 0 ? -40f : 8f, OffsetBottom = anchor.Y > 0 ? -8f : 40f,
                };
                AddChild(c);
            }
        }

        var scroll = new ScrollContainer
        {
            Name = "BodyScroll", AnchorRight = 1f, AnchorBottom = 1f, OffsetTop = 78f, OffsetBottom = -78f, OffsetLeft = 24f, OffsetRight = -24f,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        AddChild(scroll);
        var centre = new CenterContainer { Name = "Centre", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        scroll.AddChild(centre);
        Body = new VBoxContainer { Name = "Body", CustomMinimumSize = new Vector2(bodyWidth, 0f) };
        Body.AddThemeConstantOverride("separation", 8);
        centre.AddChild(Body);

        _bar = new HBoxContainer
        {
            Name = "PageButtons", AnchorLeft = 0.05f, AnchorRight = 0.95f, AnchorTop = 1f, AnchorBottom = 1f, OffsetTop = -66f, OffsetBottom = -20f,
            Alignment = BoxContainer.AlignmentMode.Center,
        };
        _bar.AddThemeConstantOverride("separation", 24);
        AddChild(_bar);
    }

    /// <summary>
    /// Turns the page title into a splash line (the main menu's trivia): smaller, in sentence case, in the menu's
    /// highlight green, still typed out. Call after <see cref="BuildFrame"/>.
    /// </summary>
    protected void UseSplashTitle()
    {
        if (_title is null)
        {
            return;
        }

        _title.Uppercase = false;
        _title.LabelSettings.FontSize = UiStyle.IsOriginal ? 20 : UiArt.BodySize + 3;
        _title.LabelSettings.FontColor = UiStyle.IsOriginal ? UiStyle.MenuFocus : UiArt.Accent;
        _title.OffsetTop = 30f;
        _title.OffsetLeft = 90f;
        _title.OffsetRight = -90f;
        _title.ClipText = true;
        _title.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
    }

    /// <summary>The modern look's thin rules under the title and above the page buttons.</summary>
    private void AddRule(string name, bool top)
    {
        Control rule = UiArt.Rule(name);
        rule.AnchorLeft = 0.06f;
        rule.AnchorRight = 0.94f;
        rule.AnchorTop = rule.AnchorBottom = top ? 0f : 1f;
        rule.OffsetTop = top ? 66f : -76f;
        rule.OffsetBottom = rule.OffsetTop + 8f;
        AddChild(rule);
    }

    /// <summary>Adds a page button to the bottom bar (call in B, X, Y, A order, as GUIPage lays them out).</summary>
    protected Button PageButton(string caption, Action pressed, string name)
    {
        Button b = UiStyle.MenuButton(caption, pressed, name);
        b.CustomMinimumSize = new Vector2(220f, 44f);
        _bar!.AddChild(b);
        _pageButtons.Add(b);
        return b;
    }

    public override void _Process(double delta)
    {
        if (_title is null)
        {
            return;
        }

        // GUIPage.MyDrawPage: the letters typed so far, the next one white, then a blinking underscore.
        _titleTime += (float)delta * TitleLettersPerSecond;
        int shown = Math.Min(_titleText.Length, (int)_titleTime);
        string text = _titleText[..shown];
        if (shown < _titleText.Length)
        {
            text += _titleText[shown] + ((Time.GetTicksMsec() / 250 & 1) == 0 ? "_" : " ");
        }

        if (_title.Text != text)
        {
            _title.Text = text;
        }
    }

    /// <summary>A framed panel (Menu_PC Saquare_BOX) holding a column, with an optional caption.</summary>
    protected static VBoxContainer Panel(Container parent, string name, string? caption, float minWidth = 0f)
    {
        var frame = new PanelContainer { Name = name, CustomMinimumSize = new Vector2(minWidth, 0f) };
        frame.AddThemeStyleboxOverride("panel", UiStyle.MenuPanelBox());
        parent.AddChild(frame);
        var column = new VBoxContainer { Name = "Column" };
        column.AddThemeConstantOverride("separation", 4);
        frame.AddChild(column);
        if (caption is not null)
        {
            var label = new Label { Name = "Caption", Text = caption, Modulate = UiStyle.MenuFocus };
            if (!UiStyle.IsOriginal)
            {
                // Captions in small spaced capitals of the mono font.
                label.Uppercase = true;
                label.AddThemeFontSizeOverride("font_size", UiArt.CaptionSize);
                if (UiArt.CaptionFont is { } mono)
                {
                    label.AddThemeFontOverride("font", mono);
                }
            }

            column.AddChild(label);
        }

        return column;
    }

    /// <summary>Saves the window's frame as a PNG when <c>SVM_PLAY_SHOTS</c> names a folder (windowed runs only).</summary>
    public static string? Shot(Node node, string label)
    {
        string? folder = System.Environment.GetEnvironmentVariable("SVM_PLAY_SHOTS");
        if (string.IsNullOrWhiteSpace(folder) || DisplayServer.GetName() == "headless" || node.GetViewport() is not { } viewport)
        {
            return null;
        }

        System.IO.Directory.CreateDirectory(folder);
        string file = System.IO.Path.Combine(folder, label + ".png");
        viewport.GetTexture().GetImage().SavePng(file);
        return file;
    }

    /// <summary>The side colour of the lists (the HUD's spy and merc colours).</summary>
    protected static Color SideColour(HudSide side) => UiStyle.SideColour(side);
}
