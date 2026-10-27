using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Godot;

namespace OpenScctGame.Hud.Magma;

/// <summary>One glyph of a Magma bitmap font: its cell in a (top-down) atlas page and its metrics in font pixels.</summary>
public readonly record struct MagmaGlyph(int X, int Y, int W, int H, int Page, int XOff, int Base, int Advance);

/// <summary>
/// A Magma bitmap font (<c>shared/ui/fonts/&lt;Font&gt;.json</c>, svm.font/1, from the game's .mft glyph tables). Text
/// of Magma height h scales glyphs by h / <see cref="Size"/>; glyphs hang from a pen line <c>Base</c> rows below
/// the top of their cell.
/// </summary>
public sealed class MagmaFont
{
    public const string Format = "svm.font/1";

    private readonly Dictionary<int, MagmaGlyph> _glyphs = new();
    private readonly List<string> _pageFiles = new();

    public string Name { get; private init; } = "";

    /// <summary>Nominal height in font pixels.</summary>
    public int Size { get; private init; } = 20;

    /// <summary>Transparent pixels round every glyph cell in the pages (0 for the game's edge-to-edge pages).</summary>
    public int Padding { get; private init; }

    public IReadOnlyList<string> PageFiles => _pageFiles;

    /// <summary>
    /// The atlas rect to sample for a glyph. With padded pages it is the exact cell. Without padding a filtered
    /// draw would pick up the neighbouring cells, so the rect is inset half a texel on every side (this loses the
    /// edge rows at small scales, which is why the converter pads the pages).
    /// </summary>
    public Rect2 SourceRect(in MagmaGlyph g) => Padding > 0
        ? new Rect2(g.X, g.Y, g.W, g.H)
        : new Rect2(g.X + 0.5f, g.Y + 0.5f, Math.Max(0f, g.W - 1f), Math.Max(0f, g.H - 1f));

    /// <summary>Loaded page textures, filled by the canvas on first draw (null headless or when missing).</summary>
    public Texture2D?[] Pages { get; private set; } = [];

    public static MagmaFont Load(string path)
    {
        using FileStream stream = File.OpenRead(path);
        using JsonDocument doc = JsonDocument.Parse(stream);
        JsonElement root = doc.RootElement;
        if (!root.TryGetProperty("format", out JsonElement f) || f.GetString() != Format)
        {
            throw new InvalidDataException($"{path}: not a {Format} file");
        }

        var font = new MagmaFont
        {
            Name = root.GetProperty("name").GetString() ?? "",
            Size = Math.Max(1, root.GetProperty("size").GetInt32()),
            Padding = root.TryGetProperty("padding", out JsonElement pad) ? Math.Max(0, pad.GetInt32()) : 0,
        };
        string dir = System.IO.Path.GetDirectoryName(path) ?? ".";
        foreach (JsonElement p in root.GetProperty("pages").EnumerateArray())
        {
            font._pageFiles.Add(System.IO.Path.Combine(dir, p.GetProperty("file").GetString() ?? ""));
        }

        foreach (JsonElement g in root.GetProperty("glyphs").EnumerateArray())
        {
            font._glyphs[g.GetProperty("c").GetInt32()] = new MagmaGlyph(g.GetProperty("x").GetInt32(), g.GetProperty("y").GetInt32(),
                g.GetProperty("w").GetInt32(), g.GetProperty("h").GetInt32(), g.GetProperty("page").GetInt32(),
                g.GetProperty("xoff").GetInt32(), g.GetProperty("base").GetInt32(), g.GetProperty("adv").GetInt32());
        }

        font.Pages = new Texture2D?[font._pageFiles.Count];
        return font;
    }

    public bool TryGlyph(char c, out MagmaGlyph glyph) => _glyphs.TryGetValue(c, out glyph);

    /// <summary>Advance of a character in font pixels (a third of the size for characters the font lacks).</summary>
    public float AdvanceOf(char c) => _glyphs.TryGetValue(c, out MagmaGlyph g) && g.Advance > 0 ? g.Advance : Size / 3f;

    /// <summary>Width in screen pixels of <paramref name="text"/> at <paramref name="heightPx"/>.</summary>
    public float Measure(ReadOnlySpan<char> text, float heightPx, float xScale = 1f, float trackingPx = 0f)
    {
        float k = heightPx / Size * xScale;
        float w = 0f;
        for (int i = 0; i < text.Length; i++)
        {
            w += AdvanceOf(text[i]) * k + (i + 1 < text.Length ? trackingPx : 0f);
        }

        return w;
    }

    /// <summary>Loads the page textures (skipped when a page file is missing).</summary>
    public void LoadPages(Func<string, Texture2D?> loader)
    {
        for (int i = 0; i < _pageFiles.Count; i++)
        {
            Pages[i] ??= loader(_pageFiles[i]);
        }
    }
}
