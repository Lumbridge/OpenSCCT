using System;
using System.Globalization;
using System.Text;
using static OpenScct.Master.Web.WebHtml;

namespace OpenScct.Master.Web;

/// <summary>A small, HTML-encoding Markdown renderer for public release notes.</summary>
internal static class ReleaseNotesHtml
{
    public static string Render(string markdown)
    {
        var html = new StringBuilder();
        var paragraph = new StringBuilder();
        string? list = null;
        char fence = '\0';
        int fenceLength = 0;

        void EndParagraph()
        {
            if (paragraph.Length == 0) return;
            html.Append("<p>").Append(Inline(paragraph.ToString())).Append("</p>");
            paragraph.Clear();
        }

        void EndList()
        {
            if (list is null) return;
            html.Append("</").Append(list).Append('>');
            list = null;
        }

        foreach (string raw in markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n'))
        {
            string line = raw.Trim();
            int markerLength = 0;
            if (line.Length > 0 && line[0] is '`' or '~')
                while (markerLength < line.Length && line[markerLength] == line[0]) markerLength++;

            if (fence != '\0')
            {
                if (markerLength >= fenceLength && line[0] == fence && line.AsSpan(markerLength).Trim().IsEmpty)
                {
                    html.Append("</code></pre>");
                    fence = '\0';
                }
                else html.Append(H(raw)).Append('\n');
                continue;
            }

            if (markerLength >= 3)
            {
                EndParagraph();
                EndList();
                fence = line[0];
                fenceLength = markerLength;
                html.Append("<pre><code>");
                continue;
            }
            if (line.Length == 0)
            {
                EndParagraph();
                EndList();
                continue;
            }

            int heading = 0;
            while (heading < line.Length && line[heading] == '#') heading++;
            if (heading is >= 1 and <= 6 && heading < line.Length && char.IsWhiteSpace(line[heading]))
            {
                EndParagraph();
                EndList();
                string level = heading <= 2 ? "h3" : "h4";
                html.Append('<').Append(level).Append('>').Append(Inline(line[heading..].Trim()))
                    .Append("</").Append(level).Append('>');
                continue;
            }

            (string? kind, int offset, int start) = ListItem(line);
            if (kind is not null)
            {
                EndParagraph();
                if (list != kind)
                {
                    EndList();
                    list = kind;
                    html.Append('<').Append(kind);
                    if (kind == "ol" && start != 1) html.Append(" start=\"").Append(start.ToString(CultureInfo.InvariantCulture)).Append('"');
                    html.Append('>');
                }
                html.Append("<li>").Append(Inline(line[offset..].Trim())).Append("</li>");
                continue;
            }

            EndList();
            if (paragraph.Length > 0) paragraph.Append(' ');
            paragraph.Append(line);
        }

        EndParagraph();
        EndList();
        if (fence != '\0') html.Append("</code></pre>");
        return html.ToString();
    }

    private static (string? Kind, int Offset, int Start) ListItem(string line)
    {
        if (line.Length >= 2 && line[0] is '-' or '*' or '+' && char.IsWhiteSpace(line[1])) return ("ul", 2, 1);
        int digits = 0;
        while (digits < line.Length && line[digits] is >= '0' and <= '9') digits++;
        if (digits is >= 1 and <= 9 && digits + 1 < line.Length && line[digits] is '.' or ')' &&
            char.IsWhiteSpace(line[digits + 1]) && int.TryParse(line.AsSpan(0, digits), NumberStyles.None, CultureInfo.InvariantCulture, out int start))
            return ("ol", digits + 2, start);
        return (null, 0, 1);
    }

    private static string Inline(string text, int depth = 0)
    {
        // Limit nesting in externally supplied notes; unsupported syntax remains visible as text.
        if (depth >= 4) return H(text);
        var html = new StringBuilder();
        int plain = 0;
        int cursor = 0;
        while (cursor < text.Length)
        {
            string? formatted = null;
            int next = cursor + 1;
            if (text[cursor] == '\\' && next < text.Length && char.IsPunctuation(text[next]))
            {
                formatted = H(text[next].ToString());
                next++;
            }
            else if (text[cursor] == '`')
            {
                while (next < text.Length && text[next] == '`') next++;
                string marker = text[cursor..next];
                int end = text.IndexOf(marker, next, StringComparison.Ordinal);
                if (end >= 0)
                {
                    formatted = "<code>" + H(text[next..end]) + "</code>";
                    next = end + marker.Length;
                }
            }
            else if (text.AsSpan(cursor).StartsWith("**", StringComparison.Ordinal))
            {
                int end = text.IndexOf("**", cursor + 2, StringComparison.Ordinal);
                if (end > cursor + 2)
                {
                    formatted = "<strong>" + Inline(text[(cursor + 2)..end], depth + 1) + "</strong>";
                    next = end + 2;
                }
            }
            else if (text[cursor] == '[')
            {
                // Nested links are unsupported. Stop at the next bracket to avoid repeatedly
                // scanning the rest of a malformed note containing many opening brackets.
                int labelEnd = text.IndexOfAny(['[', ']'], cursor + 1);
                bool hasTarget = labelEnd > cursor && text[labelEnd] == ']' && labelEnd + 1 < text.Length && text[labelEnd + 1] == '(';
                int targetEnd = hasTarget ? text.IndexOf(')', labelEnd + 2) : -1;
                if (targetEnd >= 0)
                {
                    string target = text[(labelEnd + 2)..targetEnd].Trim();
                    if (Uri.TryCreate(target, UriKind.Absolute, out Uri? url) && url.Scheme is "https" or "http")
                    {
                        formatted = "<a href=\"" + H(url.AbsoluteUri) + "\">" + Inline(text[(cursor + 1)..labelEnd], depth + 1) + "</a>";
                        next = targetEnd + 1;
                    }
                }
            }

            if (formatted is null)
            {
                cursor = next;
                continue;
            }
            html.Append(H(text[plain..cursor])).Append(formatted);
            plain = cursor = next;
        }
        return html.Append(H(text[plain..])).ToString();
    }
}
