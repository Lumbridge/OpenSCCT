using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace OpenScct.Launcher.GitHub;

/// <summary>
/// Turns a release's notes (the GitHub markdown our releases use) into RTF for the launcher's What's new window:
/// headings (<c>#</c> to <c>###</c>), paragraphs (soft-wrapped lines joined), bullets (<c>-</c>, <c>*</c>, with
/// indented continuation lines), <c>**bold**</c>, <c>*italic*</c>, <c>`code`</c>, <c>[text](url)</c> and bare
/// http(s) links. Anything else shows as its text. Colours are 0xRRGGBB.
/// </summary>
public static partial class ReleaseNotesRtf
{
    /// <summary>The RTF of <paramref name="markdown"/>.</summary>
    /// <param name="text">Body text colour.</param>
    /// <param name="accent">Heading and link colour.</param>
    /// <param name="muted">Code colour.</param>
    /// <param name="fontSize">Body size in points.</param>
    public static string Convert(string? markdown, int text, int accent, int muted, float fontSize = 9.5f)
    {
        int body = (int)Math.Round(fontSize * 2);
        var rtf = new StringBuilder();
        rtf.Append(@"{\rtf1\ansi\ansicpg1252\deff0\uc1");
        rtf.Append(@"{\fonttbl{\f0\fnil Segoe UI;}{\f1\fnil Segoe UI Semibold;}{\f2\fmodern Consolas;}}");
        rtf.Append(@"{\colortbl;").Append(Colour(text)).Append(Colour(accent)).Append(Colour(muted)).Append('}');
        rtf.Append(CultureInfo.InvariantCulture, $@"\viewkind4\f0\fs{body}\cf1 ");

        foreach (Block block in Blocks(markdown ?? string.Empty))
        {
            switch (block.Kind)
            {
                case BlockKind.Heading:
                    int size = block.Level <= 1 ? body + 10 : block.Level == 2 ? body + 6 : body + 2;
                    rtf.Append(CultureInfo.InvariantCulture, $@"\pard\sb120\sa80\f1\fs{size}\cf2 ");
                    Inline(rtf, block.Text);
                    rtf.Append(CultureInfo.InvariantCulture, $@"\f0\fs{body}\cf1\par").Append('\n');
                    break;
                case BlockKind.Bullet:
                    // A hanging indent: the bullet at the margin, the text and its wrapped lines aligned after it.
                    int indent = 360 + (block.Level * 360);
                    rtf.Append(CultureInfo.InvariantCulture, $@"\pard\sa60\li{indent}\fi-240\tx{indent}\bullet\tab ");
                    Inline(rtf, block.Text);
                    rtf.Append(@"\par").Append('\n');
                    break;
                default:
                    rtf.Append(@"\pard\sa120 ");
                    Inline(rtf, block.Text);
                    rtf.Append(@"\par").Append('\n');
                    break;
            }
        }

        rtf.Append('}');
        return rtf.ToString();
    }

    private static string Colour(int rgb) =>
        string.Create(CultureInfo.InvariantCulture, $@"\red{(rgb >> 16) & 0xFF}\green{(rgb >> 8) & 0xFF}\blue{rgb & 0xFF};");

    private enum BlockKind
    {
        Paragraph,
        Heading,
        Bullet,
    }

    private sealed record Block(BlockKind Kind, string Text, int Level);

    /// <summary>The blocks of the markdown: headings, bullets (with their continuation lines) and paragraphs.</summary>
    private static List<Block> Blocks(string markdown)
    {
        var blocks = new List<Block>();
        BlockKind? kind = null;
        int level = 0;
        var text = new StringBuilder();

        void Flush()
        {
            if (kind is { } k && text.Length > 0)
            {
                blocks.Add(new Block(k, text.ToString().Trim(), level));
            }

            kind = null;
            text.Clear();
        }

        foreach (string raw in markdown.ReplaceLineEndings("\n").Split('\n'))
        {
            string line = raw.TrimEnd();
            string trimmed = line.TrimStart();
            int lead = line.Length - trimmed.Length;
            if (trimmed.Length == 0)
            {
                Flush();
                continue;
            }

            if (HeadingRegex().Match(trimmed) is { Success: true } h)
            {
                Flush();
                blocks.Add(new Block(BlockKind.Heading, h.Groups[2].Value.Trim(), h.Groups[1].Length));
                continue;
            }

            if (BulletRegex().Match(trimmed) is { Success: true } b)
            {
                Flush();
                kind = BlockKind.Bullet;
                level = Math.Min(lead / 2, 3);
                text.Append(b.Groups[1].Value);
                continue;
            }

            // A line under a bullet or a paragraph continues it (markdown joins soft-wrapped lines with a space).
            if (kind is null)
            {
                kind = BlockKind.Paragraph;
                level = 0;
            }
            else
            {
                text.Append(' ');
            }

            text.Append(trimmed);
        }

        Flush();
        return blocks;
    }

    /// <summary>Appends text with its inline markup: links, code, bold and italic.</summary>
    private static void Inline(StringBuilder rtf, string text)
    {
        int at = 0;
        foreach (Match m in InlineRegex().Matches(text))
        {
            Plain(rtf, text[at..m.Index]);
            at = m.Index + m.Length;
            if (m.Groups["linktext"].Success)
            {
                Link(rtf, m.Groups["linktext"].Value, m.Groups["linkurl"].Value);
            }
            else if (m.Groups["url"].Success)
            {
                Link(rtf, m.Groups["url"].Value, m.Groups["url"].Value);
            }
            else if (m.Groups["code"].Success)
            {
                rtf.Append(@"{\f2\cf3 ");
                Plain(rtf, m.Groups["code"].Value);
                rtf.Append('}');
            }
            else if (m.Groups["bold"].Success)
            {
                rtf.Append(@"{\b ");
                Inline(rtf, m.Groups["bold"].Value);
                rtf.Append('}');
            }
            else if (m.Groups["italic"].Success)
            {
                rtf.Append(@"{\i ");
                Inline(rtf, m.Groups["italic"].Value);
                rtf.Append('}');
            }
        }

        Plain(rtf, text[at..]);
    }

    private static void Link(StringBuilder rtf, string text, string url)
    {
        rtf.Append(@"{\field{\*\fldinst{HYPERLINK """);
        Plain(rtf, url.Replace("\"", "%22", StringComparison.Ordinal));
        rtf.Append(@"""}}{\fldrslt{\ul\cf2 ");
        Plain(rtf, text);
        rtf.Append("}}}");
    }

    /// <summary>Text escaped for RTF (backslash, braces, anything outside ASCII as \u).</summary>
    private static void Plain(StringBuilder rtf, string text)
    {
        foreach (char c in text)
        {
            switch (c)
            {
                case '\\':
                case '{':
                case '}':
                    rtf.Append('\\').Append(c);
                    break;
                case '\t':
                    rtf.Append(@"\tab ");
                    break;
                default:
                    if (c < 0x80)
                    {
                        rtf.Append(c);
                    }
                    else
                    {
                        rtf.Append(CultureInfo.InvariantCulture, $@"\u{(short)c}?");
                    }

                    break;
            }
        }
    }

    [GeneratedRegex(@"^(#{1,6})\s+(.*?)\s*#*$")]
    private static partial Regex HeadingRegex();

    [GeneratedRegex(@"^[-*+]\s+(.*)$")]
    private static partial Regex BulletRegex();

    [GeneratedRegex(@"\[(?<linktext>[^\]]+)\]\((?<linkurl>https?://[^)\s]+)\)|`(?<code>[^`]+)`|\*\*(?<bold>.+?)\*\*|__(?<bold>.+?)__|(?<![\w*])\*(?<italic>[^*\s][^*]*?)\*(?![\w*])|(?<![\w_])_(?<italic>[^_\s][^_]*?)_(?![\w_])|(?<url>https?://[^\s)\]]+)")]
    private static partial Regex InlineRegex();
}
