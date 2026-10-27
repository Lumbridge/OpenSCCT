using System;
using Godot;
using OpenScctGame.Hud;
using OpenScctGame.Hud.Magma;

namespace OpenScctGame.UI;

/// <summary>Which art the menus draw.</summary>
public enum UiLook
{
    /// <summary>Our own art, drawn at the window's size (<see cref="UiArt"/>). The default.</summary>
    Modern,

    /// <summary>The original's GUI textures and the HUD's bitmap font (the converted UI content), as before.</summary>
    Original,
}

/// <summary>
/// The front end's look. <see cref="UiLook.Modern"/> (the default) is our own art (<see cref="UiArt"/>);
/// <see cref="UiLook.Original"/> is the HUD's HandelGothic bitmap font turned into a Godot font and the original's PC
/// menu textures (when the UI content is converted). Pick the original with <c>SVM_UI_LOOK=original</c>,
/// <c>--svm-ui-look=original</c> or <c>look="original"</c> under <c>[ui]</c> in <c>user://ui.cfg</c>.
/// </summary>
public static class UiStyle
{
    /// <summary>The file that keeps the chosen look.</summary>
    public const string LookFile = "user://ui.cfg";

    /// <summary>The theme type variation of the map picker's tiles.</summary>
    public const string TileVariation = "MapTile";

    private static readonly Color OriginalBackground = new(0.03f, 0.05f, 0.05f, 1f);
    private static readonly Color OriginalText = new(0.86f, 0.9f, 0.86f, 1f);
    private static readonly Color OriginalDim = new(0.55f, 0.6f, 0.55f, 1f);

    public static readonly Color Panel = new(0f, 0f, 0f, 0.55f);

    private static UiLook? _look;
    private static Theme? _theme;
    private static HudAssets? _assets;
    private static bool _loaded;

    /// <summary>The look in use: the command line, then SVM_UI_LOOK, then user://ui.cfg, else modern. Setting it rebuilds the themes.</summary>
    public static UiLook Look
    {
        get => _look ??= ReadLook();
        set
        {
            _look = value;
            _theme = null;
            _menuTheme = null;
        }
    }

    /// <summary>True when the menus draw the original's textures.</summary>
    public static bool IsOriginal => Look == UiLook.Original;

    public static Color Background => IsOriginal ? OriginalBackground : UiArt.Ink;

    public static Color Text => IsOriginal ? OriginalText : UiArt.Text;

    public static Color Dim => IsOriginal ? OriginalDim : UiArt.TextDim;

    /// <summary>Parses a look's name ("original", "modern"), or null.</summary>
    public static UiLook? ParseLook(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "original" or "classic" => UiLook.Original,
        "modern" => UiLook.Modern,
        _ => null,
    };

    private static UiLook ReadLook()
    {
        const string flag = "--svm-ui-look=";
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith(flag, StringComparison.OrdinalIgnoreCase) && ParseLook(arg[flag.Length..]) is { } fromArg)
            {
                return fromArg;
            }
        }

        if (ParseLook(System.Environment.GetEnvironmentVariable("SVM_UI_LOOK")) is { } fromEnv)
        {
            return fromEnv;
        }

        var file = new ConfigFile();
        if (file.Load(LookFile) == Error.Ok && ParseLook(file.GetValue("ui", "look", string.Empty).AsString()) is { } fromFile)
        {
            return fromFile;
        }

        return UiLook.Modern;
    }

    /// <summary>The converted UI content, or null when it is not there.</summary>
    public static HudAssets? Assets
    {
        get
        {
            if (!_loaded)
            {
                _loaded = true;
                _assets = HudAssets.TryLoad(null, out _);
            }

            return _assets;
        }
    }

    public static Theme Theme => _theme ??= BuildTheme();

    // ---- the original's PC menu pages (docs/MENUS.md) ------------------------------------------------------------
    // Colours are the sSTY_Pc* styles' FontColors and GUIPage's title colour, doubled: the menu canvas draws a colour
    // of 128 as neutral (every style's ImgColors is 128 and the page background is drawn at 128), so the decoded
    // values are half the shown ones (an estimate: the original menus cannot be captured under the test rules).

    private static readonly Color OriginalMenuText = Doubled(58, 73, 58);
    private static readonly Color OriginalMenuFocus = Doubled(93, 117, 92);
    private static readonly Color OriginalMenuTitle = Doubled(69, 85, 69);

    /// <summary>Page text. Original: sSTY_PcButton / sSTY_PcList FontColors[0] (58, 73, 58), doubled.</summary>
    public static Color MenuText => IsOriginal ? OriginalMenuText : UiArt.MenuText;

    /// <summary>Focused text. Original: the styles' focused FontColors[2] (93, 117, 92), doubled.</summary>
    public static Color MenuFocus => IsOriginal ? OriginalMenuFocus : UiArt.MenuFocus;

    /// <summary>Page titles. Original: GUIPage.MyDrawPage's title colour (69, 85, 69), doubled.</summary>
    public static Color MenuTitle => IsOriginal ? OriginalMenuTitle : UiArt.MenuTitle;

    /// <summary>Disabled text (the styles' disabled FontColors[4], alpha 64).</summary>
    public static Color MenuDisabled => MenuText with { A = 0.35f };

    private static Theme? _menuTheme;

    /// <summary>The front end's theme in the PC menus' look: the HUD font, the PC button and list boxes, the style colours.</summary>
    public static Theme MenuTheme => _menuTheme ??= BuildMenuTheme();

    private static Color Doubled(int r, int g, int b) => new(Math.Min(1f, r * 2 / 255f), Math.Min(1f, g * 2 / 255f), Math.Min(1f, b * 2 / 255f), 1f);

    /// <summary>
    /// A converted PC menu texture (the ui step's GUIContent and Menu_PC groups), or null. The modern look has none,
    /// except our own drawing of the ones a page shows as an icon (<see cref="UiArt.Icon"/>: the speaking icon).
    /// </summary>
    public static Texture2D? MenuTexture(string package, string name) => IsOriginal ? Assets?.Texture(package, name) : UiArt.Icon(name);

    private static StyleBox Box(string texture, Color fallbackFill, Color fallbackBorder)
    {
        if (MenuTexture("Menu_PC", texture) is { } t)
        {
            var box = new StyleBoxTexture { Texture = t, ContentMarginLeft = 10f, ContentMarginRight = 10f, ContentMarginTop = 4f, ContentMarginBottom = 4f };
            box.TextureMarginLeft = box.TextureMarginRight = box.TextureMarginTop = box.TextureMarginBottom = 4f;
            return box;
        }

        return new StyleBoxFlat
        {
            BgColor = fallbackFill, BorderColor = fallbackBorder, BorderWidthLeft = 2, BorderWidthRight = 2, BorderWidthTop = 2, BorderWidthBottom = 2,
            ContentMarginLeft = 10f, ContentMarginRight = 10f, ContentMarginTop = 4f, ContentMarginBottom = 4f,
        };
    }

    /// <summary>The framed panel of the pages (Menu_PC Saquare_BOX).</summary>
    public static StyleBox MenuPanelBox()
    {
        if (!IsOriginal)
        {
            return UiArt.PanelBox();
        }

        StyleBox box = Box("Saquare_BOX", new Color(0.16f, 0.2f, 0.16f, 0.35f), new Color(0.55f, 0.67f, 0.55f, 0.8f));
        box.ContentMarginLeft = box.ContentMarginRight = 12f;
        box.ContentMarginTop = box.ContentMarginBottom = 8f;
        return box;
    }

    /// <summary>The frame around a map picture.</summary>
    public static StyleBox ThumbBox()
    {
        if (!IsOriginal)
        {
            return UiArt.ThumbBox();
        }

        return new StyleBoxFlat
        {
            BgColor = new Color(0.02f, 0.04f, 0.03f, 0.9f), BorderColor = MenuText,
            BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1, BorderWidthBottom = 1,
        };
    }

    /// <summary>The OpenSCCT name on the main menu: the wordmark in the modern look, the plain caption in the original.</summary>
    public static Control Wordmark()
    {
        if (!IsOriginal && UiArt.BodyFont is not null)
        {
            return new UiWordmark { Name = "Wordmark" };
        }

        return new Label { Name = "Wordmark", Text = "OpenSCCT", HorizontalAlignment = HorizontalAlignment.Center, Modulate = MenuFocus };
    }

    private static Theme BuildMenuTheme()
    {
        var theme = (Theme)Theme.Duplicate();
        if (!IsOriginal)
        {
            return theme;
        }

        theme.SetTypeVariation(TileVariation, "Button");
        // sSTY_PcButton: buton_select (normal, watched, disabled), buton_validate (focused), buton_push (pressed).
        StyleBox normal = Box("buton_select", new Color(0.32f, 0.41f, 0.31f, 0.5f), new Color(0.44f, 0.53f, 0.43f, 0.5f));
        StyleBox focus = Box("buton_validate", new Color(0.4f, 0.52f, 0.38f, 0.5f), new Color(0.56f, 0.7f, 0.56f, 0.5f));
        StyleBox pressed = Box("buton_push", new Color(0.32f, 0.41f, 0.31f, 0.6f), new Color(0.43f, 0.52f, 0.43f, 0.6f));
        foreach (string type in new[] { "Button", "OptionButton", "CheckBox", "CheckButton" })
        {
            bool framed = type is "Button" or "OptionButton";
            if (framed)
            {
                theme.SetStylebox("normal", type, normal);
                theme.SetStylebox("hover", type, focus);
                theme.SetStylebox("pressed", type, pressed);
                theme.SetStylebox("focus", type, focus);
                var disabled = (StyleBox)normal.Duplicate();
                if (disabled is StyleBoxTexture dt)
                {
                    dt.ModulateColor = new Color(1f, 1f, 1f, 0.35f);
                }

                theme.SetStylebox("disabled", type, disabled);
            }

            theme.SetColor("font_color", type, MenuText);
            theme.SetColor("font_hover_color", type, MenuFocus);
            theme.SetColor("font_focus_color", type, MenuFocus);
            theme.SetColor("font_pressed_color", type, MenuFocus);
            theme.SetColor("font_hover_pressed_color", type, MenuFocus);
            theme.SetColor("font_disabled_color", type, MenuDisabled);
        }

        // sSTY_PcList: TABS_NOselect for edit boxes and lists.
        StyleBox list = Box("TABS_NOselect", new Color(0.33f, 0.4f, 0.33f, 0.12f), new Color(0.28f, 0.35f, 0.28f, 1f));
        foreach (string type in new[] { "LineEdit", "Tree", "RichTextLabel", "ItemList", "SpinBox" })
        {
            theme.SetStylebox("normal", type, list);
            theme.SetStylebox("focus", type, focus);
            theme.SetStylebox("panel", type, list);
            theme.SetColor("font_color", type, MenuText);
            theme.SetColor("default_color", type, MenuText);
        }

        theme.SetColor("font_color", "Label", MenuText);
        theme.SetColor("font_selected_color", "LineEdit", MenuFocus);
        theme.SetColor("title_button_color", "Tree", MenuFocus);
        return theme;
    }

    /// <summary>
    /// The page background (GUIPage.Background: GUIContent "fond"), stretched to cover the window, over the plain dark
    /// colour that shows without converted content.
    /// </summary>
    public static Control MenuBackdrop()
    {
        if (!IsOriginal)
        {
            var modern = new Control { Name = "Backdrop", AnchorRight = 1f, AnchorBottom = 1f, MouseFilter = Control.MouseFilterEnum.Ignore };
            modern.AddChild(UiArt.Backdrop());
            return modern;
        }

        var root = new Control { Name = "Backdrop", AnchorRight = 1f, AnchorBottom = 1f, MouseFilter = Control.MouseFilterEnum.Ignore };
        root.AddChild(Backdrop());
        if (MenuTexture("GUIContent", "fond") is { } fond)
        {
            root.AddChild(new TextureRect
            {
                Name = "Fond", Texture = fond, AnchorRight = 1f, AnchorBottom = 1f, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered, MouseFilter = Control.MouseFilterEnum.Ignore,
            });
        }

        return root;
    }

    public static Color SideColour(HudSide side) => side == HudSide.Merc ? HudK.MercColour : HudK.SpyColour;

    private static Theme BuildTheme()
    {
        if (!IsOriginal)
        {
            return UiArt.BuildTheme();
        }

        var theme = new Theme { DefaultFontSize = 18 };
        if (Assets?.Font(HudAssets.DefaultFont) is { } magma && ToFontFile(magma, Assets) is { } font)
        {
            theme.DefaultFont = font;
            // A bitmap font reads best at its own size: an 18 px draw of a 20 px font drops rows.
            theme.DefaultFontSize = magma.Size;
        }

        theme.SetColor("font_color", "Label", Text);
        theme.SetColor("font_color", "Button", Text);
        theme.SetColor("font_hover_color", "Button", HudK.SpyColour);
        theme.SetColor("font_focus_color", "Button", HudK.SpyColour);
        theme.SetColor("font_disabled_color", "Button", Dim);
        return theme;
    }

    /// <summary>
    /// A Godot font from a Magma bitmap font: one fixed-size cache whose glyphs are the atlas cells (glyph index =
    /// character code, as for imported bitmap fonts). Null when a page image is missing.
    /// </summary>
    public static FontFile? ToFontFile(MagmaFont magma, HudAssets assets)
    {
        ArgumentNullException.ThrowIfNull(magma);
        ArgumentNullException.ThrowIfNull(assets);
        var font = new FontFile { FixedSize = magma.Size, FixedSizeScaleMode = TextServer.FixedSizeScaleMode.Enabled };
        var size = new Vector2I(magma.Size, 0);
        for (int i = 0; i < magma.PageFiles.Count; i++)
        {
            Image? image = System.IO.File.Exists(magma.PageFiles[i]) ? Image.LoadFromFile(magma.PageFiles[i]) : null;
            if (image is null || image.IsEmpty())
            {
                return null;
            }

            font.SetTextureImage(0, size, i, image);
        }

        int ascent = 0, descent = 0;
        for (int c = 32; c < 256; c++)
        {
            if (!magma.TryGlyph((char)c, out MagmaGlyph g))
            {
                continue;
            }

            font.SetGlyphAdvance(0, magma.Size, c, new Vector2(g.Advance, 0f));
            font.SetGlyphOffset(0, size, c, new Vector2(g.XOff, -g.Base));
            font.SetGlyphSize(0, size, c, new Vector2(g.W, g.H));
            font.SetGlyphUVRect(0, size, c, magma.SourceRect(g));
            font.SetGlyphTextureIdx(0, size, c, g.Page);
            ascent = Math.Max(ascent, g.Base);
            descent = Math.Max(descent, g.H - g.Base);
        }

        font.SetCacheAscent(0, magma.Size, ascent);
        font.SetCacheDescent(0, magma.Size, descent);
        return font;
    }

    /// <summary>A full-screen dark background for a front-end screen (the modern look's shader background).</summary>
    public static ColorRect Backdrop() => !IsOriginal ? UiArt.Backdrop() : new()
    {
        Name = "Backdrop",
        Color = Background,
        AnchorRight = 1f,
        AnchorBottom = 1f,
        MouseFilter = Control.MouseFilterEnum.Ignore,
    };

    public static Label Title(string text, Color colour, int size = 40)
    {
        var label = new Label
        {
            Text = text,
            HorizontalAlignment = HorizontalAlignment.Center,
            Modulate = colour,
            LabelSettings = new LabelSettings { FontSize = size, FontColor = Colors.White, ShadowColor = new Color(0f, 0f, 0f, 0.8f), ShadowOffset = new Vector2(2f, 2f) },
        };
        if (!IsOriginal)
        {
            // The modern titles: Inter, spaced out, capitals, a faint green glow.
            label.LabelSettings.Font = UiArt.TitleFont;
            label.LabelSettings.FontSize = Math.Max(12, (int)(size * 0.8f));
            label.LabelSettings.ShadowColor = UiArt.Accent with { A = 0.22f };
            label.LabelSettings.ShadowOffset = Vector2.Zero;
            label.LabelSettings.ShadowSize = 8;
            label.Uppercase = true;
        }

        return label;
    }

    public static Button MenuButton(string text, Action pressed, string? name = null)
    {
        var button = new Button { Text = text, CustomMinimumSize = new Vector2(320f, 44f), Name = name ?? text.Replace(" ", "", StringComparison.Ordinal) };
        button.Pressed += pressed;
        return button;
    }

    /// <summary>A centred column of controls inside a margin.</summary>
    public static VBoxContainer CentreColumn(Control parent, float width = 520f)
    {
        var centre = new CenterContainer { Name = "Centre", AnchorRight = 1f, AnchorBottom = 1f };
        parent.AddChild(centre);
        var column = new VBoxContainer { Name = "Column", CustomMinimumSize = new Vector2(width, 0f) };
        column.AddThemeConstantOverride("separation", 10);
        centre.AddChild(column);
        return column;
    }
}
