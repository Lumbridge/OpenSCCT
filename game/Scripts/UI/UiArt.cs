using System;
using Godot;

namespace OpenScctGame.UI;

/// <summary>
/// The modern look of the menus (<see cref="UiLook.Modern"/>): our own art, drawn by the game at the window's size so it
/// stays sharp from 720p to 4K. Night vision green on near black, thin lines, cut corners and corner ticks
/// (<see cref="UiBox"/>), a shader background with a fine grid, scanlines, a slow sweep and grain, icons from SVG
/// written here (<see cref="DpiTexture"/>, rasterised at the needed scale), and two bundled SIL Open Font License fonts
/// (game/branding/fonts: Inter for text and titles, JetBrains Mono for captions). Nothing here comes from the original
/// game.
/// </summary>
public static class UiArt
{
    // ---- palette --------------------------------------------------------------------------------------------------
    public static readonly Color Accent = new(0.49f, 1f, 0.58f, 1f);
    public static readonly Color AccentDim = new(0.23f, 0.55f, 0.29f, 1f);
    public static readonly Color Text = new(0.84f, 0.91f, 0.86f, 1f);
    public static readonly Color TextDim = new(0.52f, 0.62f, 0.55f, 1f);
    public static readonly Color Ink = new(0.015f, 0.024f, 0.02f, 1f);

    /// <summary>The menu page text (buttons, lists, labels).</summary>
    public static readonly Color MenuText = new(0.76f, 0.86f, 0.79f, 1f);

    /// <summary>Focused and highlighted text.</summary>
    public static readonly Color MenuFocus = new(0.68f, 1f, 0.74f, 1f);

    public static readonly Color MenuTitle = new(0.8f, 1f, 0.84f, 1f);

    public const int BodySize = 17;
    public const int TitleSize = 30;
    public const int CaptionSize = 13;

    private const string InterPath = "res://branding/fonts/inter/InterVariable.ttf";
    private const string MonoPath = "res://branding/fonts/jetbrains-mono/JetBrainsMono-Medium.ttf";
    private const string MonoBoldPath = "res://branding/fonts/jetbrains-mono/JetBrainsMono-Bold.ttf";

    private static bool _fontsLoaded;
    private static Font? _body, _title, _caption, _wordLight, _wordBold;
    private static Shader? _backdropShader;

    // ---- fonts ----------------------------------------------------------------------------------------------------

    /// <summary>Inter at weight 500, or null when the font file is missing.</summary>
    public static Font? BodyFont { get { LoadFonts(); return _body; } }

    /// <summary>Inter at weight 640, spaced out, for page titles.</summary>
    public static Font? TitleFont { get { LoadFonts(); return _title; } }

    /// <summary>JetBrains Mono, spaced out, for panel captions and small labels.</summary>
    public static Font? CaptionFont { get { LoadFonts(); return _caption; } }

    public static Font? WordLightFont { get { LoadFonts(); return _wordLight; } }

    public static Font? WordBoldFont { get { LoadFonts(); return _wordBold; } }

    private static void LoadFonts()
    {
        if (_fontsLoaded)
        {
            return;
        }

        _fontsLoaded = true;
        FontFile? inter = LoadFont(InterPath);
        FontFile? mono = LoadFont(MonoPath);
        FontFile? monoBold = LoadFont(MonoBoldPath) ?? mono;
        if (inter is not null)
        {
            _body = Weighted(inter, 500, 0);
            _title = Weighted(inter, 640, 4);
            _wordLight = Weighted(inter, 300, 6);
        }

        if (mono is not null)
        {
            _caption = new FontVariation { BaseFont = mono, SpacingGlyph = 2 };
        }

        if (monoBold is not null)
        {
            _wordBold = new FontVariation { BaseFont = monoBold, SpacingGlyph = 4 };
        }

        _caption ??= _body;
        _wordBold ??= _title;
    }

    private static FontVariation Weighted(Font font, int weight, int spacing) => new()
    {
        BaseFont = font,
        VariationOpentype = new Godot.Collections.Dictionary { { "wght", weight } },
        SpacingGlyph = spacing,
    };

    /// <summary>The imported font, or the raw file read directly (a checkout whose fonts were not imported yet).</summary>
    private static FontFile? LoadFont(string path)
    {
        FontFile? font = null;
        if (ResourceLoader.Exists(path))
        {
            font = ResourceLoader.Load<FontFile>(path);
        }

        if (font is null)
        {
            string file = ProjectSettings.GlobalizePath(path);
            if (System.IO.File.Exists(file))
            {
                var raw = new FontFile();
                if (raw.LoadDynamicFont(file) == Error.Ok)
                {
                    font = raw;
                }
            }
        }

        if (font is not null)
        {
            font.Antialiasing = TextServer.FontAntialiasing.Gray;
            font.Hinting = TextServer.Hinting.Light;
            font.SubpixelPositioning = TextServer.SubpixelPositioning.Auto;
            font.GenerateMipmaps = false;
        }

        return font;
    }

    // ---- boxes ----------------------------------------------------------------------------------------------------

    private static UiBox NewBox(float notch, float hMargin, float vMargin) => new()
    {
        Notch = notch,
        ContentMarginLeft = hMargin, ContentMarginRight = hMargin, ContentMarginTop = vMargin, ContentMarginBottom = vMargin,
    };

    /// <summary>The framed panel: dark glass, a faint border, bright corner ticks and faint scanlines.</summary>
    public static StyleBox PanelBox()
    {
        UiBox box = NewBox(12f, 14f, 10f);
        box.Fill = new Color(0.02f, 0.035f, 0.028f, 0.82f);
        box.Border = Accent with { A = 0.22f };
        box.Ticks = Accent with { A = 0.75f };
        box.TickLength = 16f;
        box.Scan = Accent with { A = 0.018f };
        return box;
    }

    /// <summary>A button state.</summary>
    public static StyleBox ButtonBox(string state)
    {
        UiBox box = NewBox(7f, 16f, 6f);
        switch (state)
        {
            case "hover":
                box.Fill = new Color(0.07f, 0.16f, 0.1f, 0.88f);
                box.Border = Accent with { A = 0.6f };
                box.Ticks = Accent;
                box.Bar = Accent with { A = 0.9f };
                break;
            case "pressed":
                box.Fill = new Color(0.12f, 0.3f, 0.17f, 0.92f);
                box.Border = Accent with { A = 0.85f };
                box.Ticks = Accent;
                box.Bar = Accent;
                box.Scan = Accent with { A = 0.05f };
                break;
            case "disabled":
                box.Fill = new Color(0.02f, 0.03f, 0.025f, 0.55f);
                box.Border = Accent with { A = 0.08f };
                break;
            case "focus":
                box.Expand = 3f;
                box.Border = Accent with { A = 0.7f };
                box.Ticks = Accent;
                box.TickLength = 6f;
                break;
            default:
                box.Fill = new Color(0.025f, 0.05f, 0.035f, 0.8f);
                box.Border = Accent with { A = 0.26f };
                box.Ticks = Accent with { A = 0.55f };
                break;
        }

        return box;
    }

    /// <summary>A map picker tile state: like a button, the selected tile (pressed) clearly marked.</summary>
    public static StyleBox TileBox(string state)
    {
        UiBox box = NewBox(10f, 6f, 6f);
        box.TickLength = 14f;
        switch (state)
        {
            case "hover":
                box.Fill = new Color(0.05f, 0.11f, 0.07f, 0.9f);
                box.Border = Accent with { A = 0.5f };
                box.Ticks = Accent;
                break;
            case "pressed":
                box.Fill = new Color(0.08f, 0.2f, 0.11f, 0.94f);
                box.Border = Accent;
                box.BorderWidth = 2f;
                box.Ticks = Accent;
                box.TickWidth = 3f;
                box.TickLength = 20f;
                break;
            case "disabled":
                box.Fill = new Color(0.02f, 0.03f, 0.025f, 0.5f);
                box.Border = Accent with { A = 0.08f };
                break;
            case "focus":
                box.Expand = 3f;
                box.Border = Accent with { A = 0.6f };
                break;
            default:
                box.Fill = new Color(0.02f, 0.04f, 0.03f, 0.82f);
                box.Border = Accent with { A = 0.18f };
                box.Ticks = Accent with { A = 0.4f };
                break;
        }

        return box;
    }

    /// <summary>The frame around a map picture.</summary>
    public static StyleBox ThumbBox()
    {
        UiBox box = NewBox(0f, 0f, 0f);
        box.Fill = new Color(0.01f, 0.02f, 0.015f, 0.95f);
        box.Border = Accent with { A = 0.3f };
        box.Scan = Accent with { A = 0.03f };
        return box;
    }

    /// <summary>Edit boxes and lists.</summary>
    public static StyleBox FieldBox(bool focused)
    {
        UiBox box = NewBox(0f, 10f, 5f);
        box.Fill = new Color(0.008f, 0.016f, 0.012f, 0.85f);
        box.Border = focused ? Accent with { A = 0.75f } : Accent with { A = 0.2f };
        if (focused)
        {
            box.Ticks = Accent;
            box.TickLength = 6f;
        }

        return box;
    }

    private static StyleBoxFlat Flat(Color fill, float margin = 0f, Color? border = null, int borderWidth = 0) => new()
    {
        BgColor = fill, BorderColor = border ?? Colors.Transparent,
        BorderWidthLeft = borderWidth, BorderWidthRight = borderWidth, BorderWidthTop = borderWidth, BorderWidthBottom = borderWidth,
        ContentMarginLeft = margin, ContentMarginRight = margin, ContentMarginTop = margin, ContentMarginBottom = margin,
        AntiAliasing = false,
    };

    private static StyleBoxEmpty Empty(float h, float v) => new()
    {
        ContentMarginLeft = h, ContentMarginRight = h, ContentMarginTop = v, ContentMarginBottom = v,
    };

    // ---- icons ----------------------------------------------------------------------------------------------------

    private static string Hex(Color c) => "#" + c.ToHtml(false);

    /// <summary>An icon from SVG, drawn sharp at the size it is shown (an image texture where DpiTexture is not available).</summary>
    private static Texture2D? Svg(int w, int h, string body)
    {
        string svg = $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{w}\" height=\"{h}\" viewBox=\"0 0 {w} {h}\">{body}</svg>";
        try
        {
            if (DpiTexture.CreateFromString(svg, 1f, 1f, new Godot.Collections.Dictionary()) is { } dpi)
            {
                return dpi;
            }
        }
        catch (Exception)
        {
            // fall through to a plain image
        }

        var image = new Image();
        return image.LoadSvgFromString(svg, 2f) == Error.Ok ? ImageTexture.CreateFromImage(Resized(image, w, h)) : null;
    }

    private static Image Resized(Image image, int w, int h)
    {
        image.Resize(w, h, Image.Interpolation.Lanczos);
        return image;
    }

    private static Texture2D? CheckIcon(bool on, bool disabled)
    {
        string line = Hex(disabled ? TextDim : Accent);
        string op = disabled ? "0.35" : "0.85";
        string fill = on ? $"<rect x=\"6\" y=\"6\" width=\"8\" height=\"8\" fill=\"{line}\" opacity=\"{(disabled ? "0.4" : "1")}\"/>" : string.Empty;
        return Svg(20, 20, $"<path d=\"M4.5 1.5H18.5V15.5L15.5 18.5H1.5V4.5Z\" fill=\"#08100b\" fill-opacity=\"0.9\" stroke=\"{line}\" stroke-opacity=\"{op}\" stroke-width=\"1.2\"/>{fill}");
    }

    private static Texture2D? RadioIcon(bool on, bool disabled)
    {
        string line = Hex(disabled ? TextDim : Accent);
        string dot = on ? $"<path d=\"M10 5.5L14.5 10L10 14.5L5.5 10Z\" fill=\"{line}\"/>" : string.Empty;
        return Svg(20, 20, $"<path d=\"M10 1.8L18.2 10L10 18.2L1.8 10Z\" fill=\"#08100b\" fill-opacity=\"0.9\" stroke=\"{line}\" stroke-opacity=\"0.85\" stroke-width=\"1.2\"/>{dot}");
    }

    private static Texture2D? ToggleIcon(bool on, bool disabled)
    {
        string line = Hex(disabled ? TextDim : Accent);
        string knob = on
            ? $"<rect x=\"21\" y=\"4\" width=\"12\" height=\"12\" fill=\"{line}\"/>"
            : $"<rect x=\"5\" y=\"4\" width=\"12\" height=\"12\" fill=\"{Hex(TextDim)}\"/>";
        string track = on ? $"fill=\"{line}\" fill-opacity=\"0.2\"" : "fill=\"#08100b\"";
        return Svg(38, 20, $"<rect x=\"1.5\" y=\"1.5\" width=\"35\" height=\"17\" {track} stroke=\"{line}\" stroke-opacity=\"0.8\" stroke-width=\"1.2\"/>{knob}");
    }

    private static Texture2D? GrabberIcon(Color colour) =>
        Svg(14, 24, $"<path d=\"M2 1H12V19L7 23L2 19Z\" fill=\"#0b1a10\" stroke=\"{Hex(colour)}\" stroke-width=\"1.4\"/><rect x=\"6\" y=\"6\" width=\"2\" height=\"10\" fill=\"{Hex(colour)}\"/>");

    private static Texture2D? ArrowIcon() =>
        Svg(14, 10, $"<path d=\"M1.5 2L7 7.5L12.5 2\" fill=\"none\" stroke=\"{Hex(Accent)}\" stroke-width=\"1.6\"/>");

    private static Texture2D? _speaking;

    /// <summary>Our icon in place of an original menu texture a page shows as an icon, or null: CommunicatorON (speaking).</summary>
    public static Texture2D? Icon(string name) => name switch
    {
        "CommunicatorON" => _speaking ??= Svg(24, 24,
            $"<path d=\"M3 9H7L12 4.5V19.5L7 15H3Z\" fill=\"{Hex(Accent)}\"/>" +
            $"<path d=\"M15 8.5Q17 12 15 15.5M17.8 6Q21.2 12 17.8 18\" fill=\"none\" stroke=\"{Hex(Accent)}\" stroke-width=\"1.6\" stroke-linecap=\"round\"/>"),
        _ => null,
    };

    // ---- theme ----------------------------------------------------------------------------------------------------

    /// <summary>The modern theme: every control the menus use, in our art.</summary>
    public static Theme BuildTheme()
    {
        var theme = new Theme { DefaultFontSize = BodySize };
        if (BodyFont is { } body)
        {
            theme.DefaultFont = body;
        }

        StyleBox normal = ButtonBox("normal"), hover = ButtonBox("hover"), pressed = ButtonBox("pressed");
        StyleBox disabled = ButtonBox("disabled"), focus = ButtonBox("focus");
        foreach (string type in new[] { "Button", "OptionButton", "MenuButton" })
        {
            theme.SetStylebox("normal", type, normal);
            theme.SetStylebox("hover", type, hover);
            theme.SetStylebox("pressed", type, pressed);
            theme.SetStylebox("hover_pressed", type, pressed);
            theme.SetStylebox("disabled", type, disabled);
            theme.SetStylebox("focus", type, focus);
            TextColours(theme, type);
        }

        theme.SetIcon("arrow", "OptionButton", ArrowIcon());
        theme.SetConstant("arrow_margin", "OptionButton", 10);

        // The map picker's tiles.
        theme.SetTypeVariation(UiStyle.TileVariation, "Button");
        foreach (string state in new[] { "normal", "hover", "pressed", "disabled", "focus" })
        {
            theme.SetStylebox(state, UiStyle.TileVariation, TileBox(state));
        }

        theme.SetStylebox("hover_pressed", UiStyle.TileVariation, TileBox("pressed"));

        // Check boxes, radio items and toggles: no frame, a faint highlight under the pointer.
        StyleBox checkHover = Flat(Accent with { A = 0.06f }, 4f);
        foreach (string type in new[] { "CheckBox", "CheckButton" })
        {
            theme.SetStylebox("normal", type, Empty(4f, 4f));
            theme.SetStylebox("pressed", type, Empty(4f, 4f));
            theme.SetStylebox("disabled", type, Empty(4f, 4f));
            theme.SetStylebox("hover", type, checkHover);
            theme.SetStylebox("hover_pressed", type, checkHover);
            theme.SetStylebox("focus", type, FieldBox(focused: true) is UiBox f ? WithExpand(f, 1f, transparentFill: true) : Empty(0f, 0f));
            TextColours(theme, type);
            theme.SetConstant("h_separation", type, 10);
        }

        Texture2D? on = CheckIcon(true, false), off = CheckIcon(false, false);
        Texture2D? onDis = CheckIcon(true, true), offDis = CheckIcon(false, true);
        Texture2D? rOn = RadioIcon(true, false), rOff = RadioIcon(false, false);
        Texture2D? rOnDis = RadioIcon(true, true), rOffDis = RadioIcon(false, true);
        theme.SetIcon("checked", "CheckBox", on);
        theme.SetIcon("unchecked", "CheckBox", off);
        theme.SetIcon("checked_disabled", "CheckBox", onDis);
        theme.SetIcon("unchecked_disabled", "CheckBox", offDis);
        theme.SetIcon("radio_checked", "CheckBox", rOn);
        theme.SetIcon("radio_unchecked", "CheckBox", rOff);
        theme.SetIcon("radio_checked_disabled", "CheckBox", rOnDis);
        theme.SetIcon("radio_unchecked_disabled", "CheckBox", rOffDis);
        Texture2D? tOn = ToggleIcon(true, false), tOff = ToggleIcon(false, false);
        theme.SetIcon("checked", "CheckButton", tOn);
        theme.SetIcon("unchecked", "CheckButton", tOff);
        theme.SetIcon("checked_disabled", "CheckButton", ToggleIcon(true, true));
        theme.SetIcon("unchecked_disabled", "CheckButton", ToggleIcon(false, true));
        theme.SetIcon("checked_mirrored", "CheckButton", tOn);
        theme.SetIcon("unchecked_mirrored", "CheckButton", tOff);

        // Option lists.
        UiBox popup = (UiBox)PanelBox();
        popup.Notch = 0f;
        popup.ContentMarginLeft = popup.ContentMarginRight = 4f;
        popup.ContentMarginTop = popup.ContentMarginBottom = 4f;
        popup.Fill = new Color(0.015f, 0.03f, 0.02f, 0.97f);
        theme.SetStylebox("panel", "PopupMenu", popup);
        theme.SetStylebox("hover", "PopupMenu", Flat(Accent with { A = 0.16f }, 2f));
        theme.SetColor("font_color", "PopupMenu", UiArt.MenuText);
        theme.SetColor("font_hover_color", "PopupMenu", MenuFocus);
        theme.SetColor("font_disabled_color", "PopupMenu", MenuText with { A = 0.35f });
        theme.SetColor("font_accelerator_color", "PopupMenu", TextDim);
        theme.SetIcon("checked", "PopupMenu", on);
        theme.SetIcon("unchecked", "PopupMenu", off);
        theme.SetIcon("radio_checked", "PopupMenu", rOn);
        theme.SetIcon("radio_unchecked", "PopupMenu", rOff);
        theme.SetConstant("v_separation", "PopupMenu", 8);
        theme.SetConstant("h_separation", "PopupMenu", 10);

        // Sliders: a thin dark track, the filled part in the accent colour, a notched grabber.
        StyleBoxFlat track = Flat(new Color(0.02f, 0.04f, 0.03f, 0.95f), 0f, Accent with { A = 0.25f }, 1);
        track.ContentMarginTop = track.ContentMarginBottom = 3f;
        StyleBoxFlat area = Flat(Accent with { A = 0.55f });
        area.ContentMarginTop = area.ContentMarginBottom = 3f;
        StyleBoxFlat areaHi = Flat(Accent with { A = 0.8f });
        areaHi.ContentMarginTop = areaHi.ContentMarginBottom = 3f;
        foreach (string type in new[] { "HSlider", "VSlider" })
        {
            theme.SetStylebox("slider", type, track);
            theme.SetStylebox("grabber_area", type, area);
            theme.SetStylebox("grabber_area_highlight", type, areaHi);
            theme.SetIcon("grabber", type, GrabberIcon(Accent with { A = 0.85f }));
            theme.SetIcon("grabber_highlight", type, GrabberIcon(MenuFocus));
            theme.SetIcon("grabber_disabled", type, GrabberIcon(TextDim));
        }

        // Scroll bars: thin, square, green.
        foreach (string type in new[] { "VScrollBar", "HScrollBar" })
        {
            StyleBoxFlat scroll = Flat(new Color(0f, 0f, 0f, 0.35f), 0f);
            scroll.ContentMarginLeft = scroll.ContentMarginRight = scroll.ContentMarginTop = scroll.ContentMarginBottom = 3f;
            theme.SetStylebox("scroll", type, scroll);
            theme.SetStylebox("scroll_focus", type, scroll);
            theme.SetStylebox("grabber", type, Flat(Accent with { A = 0.35f }, 3f));
            theme.SetStylebox("grabber_highlight", type, Flat(Accent with { A = 0.6f }, 3f));
            theme.SetStylebox("grabber_pressed", type, Flat(Accent with { A = 0.85f }, 3f));
        }

        // Edit boxes and lists.
        StyleBox field = FieldBox(false), fieldFocus = FieldBox(true);
        StyleBox selected = Flat(Accent with { A = 0.2f }, 2f), selectedFocus = Flat(Accent with { A = 0.3f }, 2f);
        StyleBox hovered = Flat(Accent with { A = 0.08f }, 2f);
        foreach (string type in new[] { "LineEdit", "TextEdit", "Tree", "ItemList", "RichTextLabel" })
        {
            theme.SetStylebox("normal", type, field);
            theme.SetStylebox("read_only", type, field);
            theme.SetStylebox("panel", type, field);
            theme.SetStylebox("focus", type, fieldFocus);
            theme.SetStylebox("selected", type, selected);
            theme.SetStylebox("selected_focus", type, selectedFocus);
            theme.SetStylebox("hovered", type, hovered);
            theme.SetStylebox("cursor", type, Flat(Colors.Transparent, 0f, Accent with { A = 0.6f }, 1));
            theme.SetStylebox("cursor_unfocused", type, Flat(Colors.Transparent, 0f, Accent with { A = 0.25f }, 1));
            theme.SetColor("font_color", type, MenuText);
            theme.SetColor("default_color", type, MenuText);
            theme.SetColor("font_selected_color", type, MenuFocus);
            theme.SetColor("font_hovered_color", type, MenuFocus);
            theme.SetColor("font_placeholder_color", type, TextDim with { A = 0.7f });
            theme.SetColor("caret_color", type, Accent);
            theme.SetColor("selection_color", type, Accent with { A = 0.3f });
        }

        theme.SetColor("title_button_color", "Tree", MenuFocus);
        theme.SetStylebox("title_button_normal", "Tree", Flat(new Color(0.04f, 0.08f, 0.05f, 0.9f), 4f));
        theme.SetStylebox("title_button_hover", "Tree", Flat(new Color(0.07f, 0.14f, 0.09f, 0.9f), 4f));
        theme.SetStylebox("title_button_pressed", "Tree", Flat(new Color(0.1f, 0.2f, 0.12f, 0.9f), 4f));
        theme.SetColor("guide_color", "Tree", Accent with { A = 0.08f });

        // Tabs (the options pages).
        StyleBoxFlat tabOn = Flat(new Color(0.06f, 0.14f, 0.08f, 0.9f), 0f);
        tabOn.BorderColor = Accent;
        tabOn.BorderWidthBottom = 2;
        StyleBoxFlat tabOff = Flat(new Color(0.02f, 0.035f, 0.028f, 0.75f), 0f);
        tabOff.BorderColor = Accent with { A = 0.18f };
        tabOff.BorderWidthBottom = 1;
        StyleBoxFlat tabHover = Flat(new Color(0.04f, 0.09f, 0.06f, 0.85f), 0f);
        tabHover.BorderColor = Accent with { A = 0.55f };
        tabHover.BorderWidthBottom = 1;
        foreach (StyleBoxFlat t in new[] { tabOn, tabOff, tabHover })
        {
            t.ContentMarginLeft = t.ContentMarginRight = 16f;
            t.ContentMarginTop = t.ContentMarginBottom = 7f;
        }

        foreach (string type in new[] { "TabContainer", "TabBar" })
        {
            theme.SetStylebox("tab_selected", type, tabOn);
            theme.SetStylebox("tab_unselected", type, tabOff);
            theme.SetStylebox("tab_hovered", type, tabHover);
            theme.SetStylebox("tab_disabled", type, tabOff);
            theme.SetStylebox("tab_focus", type, ButtonBox("focus"));
            theme.SetColor("font_selected_color", type, MenuFocus);
            theme.SetColor("font_unselected_color", type, TextDim);
            theme.SetColor("font_hovered_color", type, MenuText);
            theme.SetColor("font_disabled_color", type, TextDim with { A = 0.4f });
        }

        theme.SetStylebox("panel", "TabContainer", PanelBox());
        theme.SetStylebox("tabbar_background", "TabContainer", Empty(0f, 0f));

        // Panels, separators, progress bars, tooltips.
        theme.SetStylebox("panel", "PanelContainer", PanelBox());
        StyleBoxLine hLine = new() { Color = Accent with { A = 0.2f }, Thickness = 1 };
        StyleBoxLine vLine = new() { Color = Accent with { A = 0.2f }, Thickness = 1, Vertical = true };
        theme.SetStylebox("separator", "HSeparator", hLine);
        theme.SetStylebox("separator", "VSeparator", vLine);
        theme.SetStylebox("background", "ProgressBar", Flat(new Color(0.02f, 0.04f, 0.03f, 0.95f), 0f, Accent with { A = 0.25f }, 1));
        theme.SetStylebox("fill", "ProgressBar", Flat(Accent with { A = 0.7f }, 0f));
        theme.SetColor("font_color", "ProgressBar", Text);
        UiBox tip = (UiBox)PanelBox();
        tip.Notch = 6f;
        tip.TickLength = 6f;
        tip.ContentMarginLeft = tip.ContentMarginRight = 8f;
        tip.ContentMarginTop = tip.ContentMarginBottom = 4f;
        tip.Fill = new Color(0.015f, 0.03f, 0.02f, 0.96f);
        theme.SetStylebox("panel", "TooltipPanel", tip);
        theme.SetColor("font_color", "TooltipLabel", MenuText);

        theme.SetColor("font_color", "Label", Text);
        return theme;
    }

    private static StyleBox WithExpand(UiBox box, float expand, bool transparentFill)
    {
        box.Expand = expand;
        if (transparentFill)
        {
            box.Fill = Colors.Transparent;
        }

        return box;
    }

    private static void TextColours(Theme theme, string type)
    {
        theme.SetColor("font_color", type, MenuText);
        theme.SetColor("font_hover_color", type, MenuFocus);
        theme.SetColor("font_focus_color", type, MenuFocus);
        theme.SetColor("font_pressed_color", type, MenuFocus);
        theme.SetColor("font_hover_pressed_color", type, MenuFocus);
        theme.SetColor("font_disabled_color", type, MenuText with { A = 0.35f });
        theme.SetColor("icon_normal_color", type, Colors.White);
        theme.SetColor("icon_hover_color", type, Colors.White);
        theme.SetColor("icon_pressed_color", type, Colors.White);
        theme.SetColor("icon_focus_color", type, Colors.White);
    }

    // ---- background -----------------------------------------------------------------------------------------------

    /// <summary>
    /// The page background: a dark green-black gradient, a fine grid with a coarser one over it, scanlines, a slow
    /// sweep down the page, a little moving grain and a vignette. Sizes follow the window height (1080 px is 1).
    /// </summary>
    public static ColorRect Backdrop()
    {
        _backdropShader ??= new Shader { Code = BackdropShader };
        return new ColorRect
        {
            Name = "Backdrop", Color = Ink, AnchorRight = 1f, AnchorBottom = 1f, MouseFilter = Control.MouseFilterEnum.Ignore,
            Material = new ShaderMaterial { Shader = _backdropShader },
        };
    }

    private const string BackdropShader = """
        shader_type canvas_item;

        uniform vec3 top_colour = vec3(0.030, 0.046, 0.038);
        uniform vec3 bottom_colour = vec3(0.008, 0.012, 0.010);
        uniform vec3 accent = vec3(0.49, 1.0, 0.58);

        float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }

        float lines(vec2 p, float spacing, float width) {
            vec2 d = abs(fract(p / spacing + 0.5) - 0.5) * spacing;
            vec2 aa = max(fwidth(p), vec2(1e-4));
            vec2 l = 1.0 - smoothstep(vec2(width * 0.5), vec2(width * 0.5) + aa, d);
            return max(l.x, l.y);
        }

        void fragment() {
            vec2 screen = 1.0 / SCREEN_PIXEL_SIZE;
            float s = max(screen.y / 1080.0, 0.5);
            vec2 px = FRAGCOORD.xy - screen * 0.5;
            vec2 uv = UV;
            vec3 col = mix(top_colour, bottom_colour, smoothstep(0.0, 1.0, uv.y));
            // A soft light behind the page's middle.
            float glow = 1.0 - smoothstep(0.0, 0.85, length((uv - vec2(0.5, 0.42)) * vec2(1.4, 1.0)));
            col += accent * 0.03 * glow;
            // Grid: fine every 32 px, coarse every 128 px, both stronger near the middle.
            float fine = lines(px, 32.0 * s, 1.0);
            float coarse = lines(px, 128.0 * s, 1.0 * s);
            col += accent * (fine * 0.012 + coarse * 0.03) * (0.4 + 0.6 * glow);
            // Coarse grid crossings get a small bright cross.
            vec2 cell = abs(fract(px / (128.0 * s) + 0.5) - 0.5) * 128.0 * s;
            float cross = step(min(cell.x, cell.y), 0.75 * s) * step(max(cell.x, cell.y), 5.0 * s);
            col += accent * 0.05 * cross;
            // Scanlines and a slow sweep down the page.
            col *= 0.94 + 0.06 * sin(FRAGCOORD.y * 3.14159 / (1.5 * s));
            float sweep = fract(TIME * 0.045) * 1.3 - 0.15;
            float band = uv.y - sweep;
            col += accent * (0.035 * exp(-band * band * 900.0) + 0.012 * exp(-max(-band, 0.0) * 12.0) * step(band, 0.0));
            // Grain and vignette.
            col += (hash(floor(FRAGCOORD.xy / s) + floor(TIME * 20.0)) - 0.5) * 0.012;
            vec2 v = uv - 0.5;
            col *= 1.0 - dot(v, v) * 1.1;
            COLOR = vec4(max(col, vec3(0.0)), 1.0);
        }
        """;

    /// <summary>A thin rule across the page with fading ends and a small centre mark (under the title, above the buttons).</summary>
    public static Control Rule(string name) => new UiRule { Name = name, MouseFilter = Control.MouseFilterEnum.Ignore };
}
