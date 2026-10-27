using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Godot;
using OpenScct.Net.Cosmetics;

namespace OpenScctGame.UI;

/// <summary>
/// The rank pictures (spec/additions/progression.md), drawn from SVG here so they are sharp at any size and need no
/// built content: a level insignia per five levels (its shape and colour by tier, one to five chevrons inside), an
/// emblem per prestige (a star with more points and a richer colour each prestige, round the trifocal goggles) and the
/// master prestige emblem. Cached per picture and size.
/// </summary>
public static class RankIcons
{
    private static readonly string[] s_tierColours =
    [
        "#7d8f81", "#93b38c", "#a6d38c", "#bfe07c", "#dbe46b", "#ecd25a", "#f0b24a", "#ef8f45", "#e96a4a", "#dc5072", "#c45ae0",
    ];

    private static readonly string[] s_prestigeColours =
    [
        "#ffffff", "#c8463a", "#e6862b", "#f1c40f", "#7fd34a", "#1fc4a0", "#3a9be0", "#6c63f0", "#b356e6", "#eef3f4", "#ffcf3a",
    ];

    private static readonly Dictionary<(int Kind, int Index, int Size), Texture2D?> s_cache = [];

    /// <summary>The picture of a rank: its prestige emblem once prestiged (the master one at the very end), else its level insignia.</summary>
    public static Texture2D? For(PlayerRank rank, int size) =>
        rank.IsNone ? null : rank.IsMaster ? Master(size) : rank.Prestige > 0 ? Prestige(rank.Prestige, size) : Level(rank.Level, size);

    public static Texture2D? Level(int level, int size) => Cached(0, Math.Clamp(level, 1, ProgressionK.MaxLevel), size, LevelSvg);

    public static Texture2D? Prestige(int prestige, int size) => Cached(1, Math.Clamp(prestige, 1, ProgressionK.MaxPrestige), size, PrestigeSvg);

    public static Texture2D? Master(int size) => Cached(2, 0, size, _ => MasterSvg());

    /// <summary>The colour of a rank's emblem (for its text beside it).</summary>
    public static Color ColourOf(PlayerRank rank) => new(rank.IsMaster ? s_prestigeColours[ProgressionK.MaxPrestige]
        : rank.Prestige > 0 ? s_prestigeColours[rank.Prestige]
        : s_tierColours[Math.Clamp((rank.Level - 1) / 5, 0, s_tierColours.Length - 1)]);

    private static Texture2D? Cached(int kind, int index, int size, Func<int, string> body)
    {
        size = Math.Clamp(size, 8, 512);
        if (!s_cache.TryGetValue((kind, index, size), out Texture2D? texture))
        {
            texture = Svg(size, body(index));
            s_cache[(kind, index, size)] = texture;
        }

        return texture;
    }

    // ---- shapes, in a 64 x 64 box ------------------------------------------------------------------------------------

    private static string F(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    private static string Points(IEnumerable<(double X, double Y)> points) => string.Join(" ", points.Select(p => F(p.X) + "," + F(p.Y)));

    private static IEnumerable<(double, double)> Polygon(int sides, double r, double turnDegrees)
    {
        for (int i = 0; i < sides; i++)
        {
            double a = (turnDegrees + 360.0 * i / sides - 90.0) * Math.PI / 180.0;
            yield return (32 + r * Math.Cos(a), 32 + r * Math.Sin(a));
        }
    }

    private static IEnumerable<(double, double)> Star(int points, double outer, double inner)
    {
        for (int i = 0; i < points * 2; i++)
        {
            double r = i % 2 == 0 ? outer : inner;
            double a = (180.0 * i / points - 90.0) * Math.PI / 180.0;
            yield return (32 + r * Math.Cos(a), 32 + r * Math.Sin(a));
        }
    }

    private static string Poly(IEnumerable<(double, double)> points, string fill, string stroke, double width) =>
        $"<polygon points=\"{Points(points)}\" fill=\"{fill}\" stroke=\"{stroke}\" stroke-width=\"{F(width)}\" stroke-linejoin=\"round\"/>";

    private const string Ink = "#0a140e";

    private static string TierShape(int tier, string colour) => tier switch
    {
        0 => $"<circle cx=\"32\" cy=\"32\" r=\"27\" fill=\"{Ink}\" stroke=\"{colour}\" stroke-width=\"3\"/>",
        1 => Poly(Polygon(4, 29, 45), Ink, colour, 3),
        2 => Poly(Polygon(4, 30, 0), Ink, colour, 3),
        3 => $"<path d=\"M32 4 L56 12 V30 C56 46 45 56 32 61 C19 56 8 46 8 30 V12 Z\" fill=\"{Ink}\" stroke=\"{colour}\" stroke-width=\"3\" stroke-linejoin=\"round\"/>",
        4 => Poly(Polygon(6, 29, 30), Ink, colour, 3),
        5 => Poly(Polygon(8, 29, 22.5), Ink, colour, 3),
        6 => Poly(Star(5, 31, 21), Ink, colour, 2.5),
        7 => Poly(Star(6, 30, 22), Ink, colour, 2.5),
        8 => Poly(Star(8, 30, 24), Ink, colour, 2.5),
        9 => Poly(Star(10, 30, 25), Ink, colour, 2.5),
        _ => Poly(Star(12, 31, 26), Ink, colour, 2.5) + $"<circle cx=\"32\" cy=\"32\" r=\"22\" fill=\"none\" stroke=\"{colour}\" stroke-width=\"1.2\" opacity=\"0.6\"/>",
    };

    private static string LevelSvg(int level)
    {
        int tier = Math.Clamp((level - 1) / 5, 0, s_tierColours.Length - 1);
        int chevrons = (level - 1) % 5 + 1;
        string colour = s_tierColours[tier];
        var body = new StringBuilder(TierShape(tier, colour));
        double height = 7 + (chevrons - 1) * 6;
        double top = 32 - height / 2;
        for (int i = 0; i < chevrons; i++)
        {
            double y = top + i * 6;
            body.Append(CultureInfo.InvariantCulture,
                $"<path d=\"M21 {F(y + 7)} L32 {F(y)} L43 {F(y + 7)}\" fill=\"none\" stroke=\"{colour}\" stroke-width=\"3.2\" stroke-linecap=\"round\" stroke-linejoin=\"round\"/>");
        }

        return body.ToString();
    }

    /// <summary>The trifocal goggles at the heart of every prestige emblem.</summary>
    private static string Goggles(string ring) =>
        $"<path d=\"M17 34 Q32 22 47 34\" fill=\"none\" stroke=\"{ring}\" stroke-width=\"2\" opacity=\"0.7\"/>" +
        string.Concat(new[] { (24.0, 33.0), (32.0, 27.5), (40.0, 33.0) }.Select(p =>
            $"<circle cx=\"{F(p.Item1)}\" cy=\"{F(p.Item2)}\" r=\"4.6\" fill=\"#1d2a20\" stroke=\"{ring}\" stroke-width=\"1.4\"/>" +
            $"<circle cx=\"{F(p.Item1)}\" cy=\"{F(p.Item2)}\" r=\"2.6\" fill=\"#7cff6b\"/>"));

    private static string PrestigeSvg(int prestige)
    {
        string colour = s_prestigeColours[prestige];
        int points = 4 + prestige;
        var body = new StringBuilder();
        body.Append(Poly(Star(points, 31, 23), colour, colour, 1.5));
        body.Append(CultureInfo.InvariantCulture, $"<circle cx=\"32\" cy=\"32\" r=\"20\" fill=\"{Ink}\" stroke=\"{colour}\" stroke-width=\"2\"/>");
        if (prestige >= 5)
        {
            body.Append(CultureInfo.InvariantCulture, $"<circle cx=\"32\" cy=\"32\" r=\"16.5\" fill=\"none\" stroke=\"{colour}\" stroke-width=\"1\" opacity=\"0.55\"/>");
        }

        body.Append(Goggles(colour));
        // Small pips under the goggles count the prestige within its five (1-5, 6-10).
        int pips = (prestige - 1) % 5 + 1;
        for (int i = 0; i < pips; i++)
        {
            double x = 32 + (i - (pips - 1) / 2.0) * 5;
            body.Append(CultureInfo.InvariantCulture, $"<circle cx=\"{F(x)}\" cy=\"44\" r=\"1.6\" fill=\"{colour}\"/>");
        }

        return body.ToString();
    }

    private static string MasterSvg()
    {
        const string gold = "#ffcf3a";
        const string light = "#fff3c4";
        return Poly(Star(16, 31.5, 26), gold, light, 1.2) +
            $"<circle cx=\"32\" cy=\"32\" r=\"23\" fill=\"{Ink}\" stroke=\"{light}\" stroke-width=\"2\"/>" +
            $"<circle cx=\"32\" cy=\"32\" r=\"19.5\" fill=\"none\" stroke=\"{gold}\" stroke-width=\"1.4\"/>" +
            Goggles(gold) +
            Poly(Star(5, 4.5, 2), light, light, 0.5).Replace("points=\"", "transform=\"translate(0 14)\" points=\"", StringComparison.Ordinal);
    }

    private static Texture2D? Svg(int size, string body)
    {
        string svg = $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{size}\" height=\"{size}\" viewBox=\"0 0 64 64\">{body}</svg>";
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
        if (image.LoadSvgFromString(svg, 2f) != Error.Ok)
        {
            return null;
        }

        image.Resize(size, size, Image.Interpolation.Lanczos);
        return ImageTexture.CreateFromImage(image);
    }
}

/// <summary>A rank as the menus show it: the emblem, the level ("P3 · 42") and, when asked, the title under it.</summary>
public sealed partial class RankBadge : HBoxContainer
{
    private readonly TextureRect _icon;
    private readonly Label _level;
    private readonly Label _title;
    private readonly int _size;

    public RankBadge()
        : this(24)
    {
    }

    public RankBadge(int size, int fontSize = 0)
    {
        _size = size;
        AddThemeConstantOverride("separation", Math.Max(4, size / 5));
        MouseFilter = MouseFilterEnum.Ignore;
        _icon = new TextureRect
        {
            CustomMinimumSize = new Vector2(size, size), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, SizeFlagsVertical = SizeFlags.ShrinkCenter,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(_icon);
        var text = new VBoxContainer { SizeFlagsVertical = SizeFlags.ShrinkCenter, MouseFilter = MouseFilterEnum.Ignore };
        text.AddThemeConstantOverride("separation", 0);
        AddChild(text);
        _level = new Label { MouseFilter = MouseFilterEnum.Ignore };
        _title = new Label { Modulate = UiStyle.Dim, Visible = false, MouseFilter = MouseFilterEnum.Ignore };
        if (fontSize > 0)
        {
            _level.AddThemeFontSizeOverride("font_size", fontSize);
            _title.AddThemeFontSizeOverride("font_size", Math.Max(10, fontSize * 3 / 4));
        }

        text.AddChild(_level);
        text.AddChild(_title);
    }

    /// <summary>Shows a rank (hidden for none), with its title under the level when <paramref name="withTitle"/>.</summary>
    public RankBadge Show(PlayerRank rank, bool withTitle = false, string? titleOverride = null)
    {
        Visible = !rank.IsNone;
        if (rank.IsNone)
        {
            return this;
        }

        _icon.Texture = RankIcons.For(rank, _size);
        _level.Text = Text(rank);
        _level.Modulate = RankIcons.ColourOf(rank);
        string? title = titleOverride ?? rank.Title?.Text;
        _title.Text = title ?? string.Empty;
        _title.Visible = withTitle && !string.IsNullOrEmpty(title);
        TooltipText = Describe(rank);
        return this;
    }

    /// <summary>
    /// The signed-in account's rank as the master last told this game, with the chosen title when the account has it;
    /// none when signed out or the master has no levels.
    /// </summary>
    public static PlayerRank OfAccount(OpenScct.Net.Discovery.AccountInfo? info)
    {
        if (info?.Progress is not { } p)
        {
            return PlayerRank.None;
        }

        string? chosen = OpenScctGame.Options.SettingsStore.Current.Title;
        int title = chosen is not null && p.Titles.Contains(chosen) && TitleCatalog.Find(chosen) is { } t ? t.Bit : -1;
        return PlayerRank.FromXp(p.Prestige, p.Xp, title);
    }

    /// <summary>"42", "P3 · 42" or "Master · 55".</summary>
    public static string Text(PlayerRank rank) =>
        rank.IsNone ? string.Empty : rank.IsMaster ? $"Master · {rank.Level}" : rank.Prestige > 0 ? $"P{rank.Prestige} · {rank.Level}" : rank.Level.ToString(CultureInfo.InvariantCulture);

    /// <summary>A tooltip line: "Prestige 3, level 42 - Field Agent".</summary>
    public static string Describe(PlayerRank rank)
    {
        string what = rank.IsMaster ? $"Master prestige, level {rank.Level}" : rank.Prestige > 0 ? $"Prestige {rank.Prestige}, level {rank.Level}" : $"Level {rank.Level}";
        return rank.Title is { } t ? what + " - " + t.Text : what;
    }
}
