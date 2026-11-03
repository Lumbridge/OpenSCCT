using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace OpenScct.Launcher.GitHub;

/// <summary>A short, plain-text preview of release notes; the full Markdown remains unchanged.</summary>
public static partial class ReleaseNotesPreview
{
    private const int MaxEntries = 3;
    private const int MaxTextElements = 160;

    public static IReadOnlyList<string> Create(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return [];

        List<string> lines = WithoutFences(markdown);
        int? topIndent = lines.Where(line => BulletRegex().IsMatch(line.TrimStart()) || OrderedListRegex().IsMatch(line.TrimStart()))
            .Select(line => (int?)Indent(line)).Min();
        var entries = new List<string>();
        var current = new StringBuilder();
        bool nested = false;

        void Flush()
        {
            string text = PlainText(current.ToString());
            if (text.Length > 0 && entries.Count < MaxEntries) entries.Add(Shorten(text));
            current.Clear();
            nested = false;
        }

        if (topIndent is not null)
        {
            foreach (string line in lines)
            {
                string trimmed = line.Trim();
                if (IsBreak(trimmed))
                {
                    Flush();
                    continue;
                }

                Match bullet = BulletRegex().Match(trimmed);
                if (bullet.Success)
                {
                    if (Indent(line) == topIndent)
                    {
                        Flush();
                        current.Append(bullet.Groups[1].Value);
                    }
                    else nested = true;
                    continue;
                }

                if (OrderedListRegex().IsMatch(trimmed))
                {
                    if (Indent(line) == topIndent) Flush();
                    nested = true;
                    continue;
                }

                if (current.Length == 0 || (nested && Indent(line) > topIndent)) continue;
                current.Append(' ').Append(trimmed);
            }
            Flush();
        }

        if (entries.Count > 0) return entries;

        // Older or hand-authored releases may contain prose without a list.
        bool inList = false;
        bool afterListBreak = false;
        foreach (string line in lines.Append(string.Empty))
        {
            string trimmed = line.Trim();
            bool listItem = BulletRegex().IsMatch(trimmed) || OrderedListRegex().IsMatch(trimmed);
            if (IsBreak(trimmed) || listItem)
            {
                string text = PlainText(current.ToString());
                if (text.Length > 0) return [Shorten(text)];
                current.Clear();
                if (listItem)
                {
                    inList = true;
                    afterListBreak = false;
                }
                else if (trimmed.Length == 0) afterListBreak = inList;
                else inList = false;
                continue;
            }
            // A list's wrapped or indented paragraphs are not ordinary prose fallback.
            if (inList)
            {
                if (!afterListBreak || Indent(line) > topIndent) continue;
                inList = false;
            }
            if (current.Length > 0) current.Append(' ');
            current.Append(trimmed);
        }

        return [];
    }

    private static List<string> WithoutFences(string markdown)
    {
        var lines = new List<string>();
        char fence = '\0';
        int fenceLength = 0;
        foreach (string line in markdown.ReplaceLineEndings("\n").Split('\n'))
        {
            string trimmed = line.TrimStart();
            int run = 0;
            if (trimmed.Length > 0 && trimmed[0] is '`' or '~')
                while (run < trimmed.Length && trimmed[run] == trimmed[0]) run++;

            if (fence != '\0')
            {
                if (run >= fenceLength && trimmed[0] == fence && string.IsNullOrWhiteSpace(trimmed[run..]))
                    fence = '\0';
                continue;
            }
            if (run >= 3)
            {
                fence = trimmed[0];
                fenceLength = run;
                lines.Add(string.Empty);
                continue;
            }
            lines.Add(line);
        }
        return lines;
    }

    private static int Indent(string line)
    {
        int indent = 0;
        foreach (char character in line)
        {
            if (character == ' ') indent++;
            else if (character == '\t') indent += 4;
            else break;
        }
        return indent;
    }

    private static bool IsBreak(string line) => line.Length == 0 || HeadingRegex().IsMatch(line)
        || DividerRegex().IsMatch(line) || ReferenceDefinitionRegex().IsMatch(line);

    private static string PlainText(string text)
    {
        text = LinkRegex().Replace(text, match => match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value);
        text = CodeRegex().Replace(text, "$1");
        // Repeating handles the simple nested emphasis used in release notes.
        for (int pass = 0; pass < 3; pass++)
            text = EmphasisRegex().Replace(text, match => match.Groups[2].Success ? match.Groups[2].Value : match.Groups[4].Value);
        text = EscapeRegex().Replace(text, "$1");
        text = WhitespaceRegex().Replace(WebUtility.HtmlDecode(text), " ").Trim();
        return MarkersOnlyRegex().IsMatch(text) ? string.Empty : text;
    }

    private static string Shorten(string text)
    {
        int[] boundaries = StringInfo.ParseCombiningCharacters(text);
        if (boundaries.Length <= MaxTextElements) return text;

        int end = boundaries[MaxTextElements - 1];
        for (int element = MaxTextElements - 2; element >= 0; element--)
        {
            if (!char.IsWhiteSpace(text, boundaries[element])) continue;
            end = boundaries[element];
            break;
        }
        return text[..end].TrimEnd() + "…";
    }

    [GeneratedRegex(@"^[-*+](?:\s+(.*))?$")]
    private static partial Regex BulletRegex();

    [GeneratedRegex(@"^\d+[.)]\s+")]
    private static partial Regex OrderedListRegex();

    [GeneratedRegex(@"^#{1,6}(?:\s|$)")]
    private static partial Regex HeadingRegex();

    [GeneratedRegex(@"^(?:[-*_]\s*){3,}$")]
    private static partial Regex DividerRegex();

    [GeneratedRegex(@"^\[[^\]]+\]:\s*")]
    private static partial Regex ReferenceDefinitionRegex();

    [GeneratedRegex(@"!?\[([^\]]*)\]\((?:[^()\r\n]|\([^()\r\n]*\))*\)|!?\[([^\]]*)\]\[[^\]]*\]")]
    private static partial Regex LinkRegex();

    [GeneratedRegex(@"`+([^`]+)`+")]
    private static partial Regex CodeRegex();

    [GeneratedRegex(@"(?<!\\)(\*\*|__|~~)(.+?)(?<!\\)\1|(?<![\w\\])([*_])([^\r\n]+?)(?<!\\)\3(?!\w)")]
    private static partial Regex EmphasisRegex();

    [GeneratedRegex(@"\\([\\`*_{}\[\]()#+.!>~\-])")]
    private static partial Regex EscapeRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"^[\s*`_~\-+.\[\]()\\#]*$")]
    private static partial Regex MarkersOnlyRegex();
}
