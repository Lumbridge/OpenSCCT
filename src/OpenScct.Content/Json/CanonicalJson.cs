using System;
using System.Collections.Generic;
using System.Text;

namespace OpenScct.Content.Json;

/// <summary>
/// Canonical SVMap JSON (conventions.md, "Canonical sim hash"): keys sorted by their ASCII bytes, no whitespace,
/// only <c>"</c>, <c>\</c> and control characters escaped, floats in the float32 text form. Also the on-disk
/// pretty form the pipeline writes, so files written here are byte-identical to the Python ones.
/// </summary>
public static class CanonicalJson
{
    /// <summary>Containers of scalars stay on one line in the pretty form when they fit this width.</summary>
    public const int InlineWidth = 100;

    private static readonly UTF8Encoding s_strictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>Parses <paramref name="utf8"/> strictly and returns its canonical bytes.</summary>
    public static byte[] Canonicalize(ReadOnlySpan<byte> utf8, string? file = null) => ToCanonicalBytes(SvJson.Parse(utf8, file));

    public static byte[] ToCanonicalBytes(SvJsonNode node) => s_strictUtf8.GetBytes(ToCanonicalString(node));

    public static string ToCanonicalString(SvJsonNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        var sb = new StringBuilder(256);
        WriteCompact(sb, node);
        return sb.ToString();
    }

    /// <summary>Pretty text with sorted keys and a one-space indent (no trailing newline).</summary>
    public static string ToPrettyString(SvJsonNode node, int indent = 1)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentOutOfRangeException.ThrowIfNegative(indent);
        var sb = new StringBuilder(1024);
        WritePretty(sb, node, 0, indent);
        return sb.ToString();
    }

    /// <summary>The on-disk form of an SVMap JSON file: pretty text plus a trailing newline, UTF-8.</summary>
    public static byte[] ToFileBytes(SvJsonNode node) => s_strictUtf8.GetBytes(ToPrettyString(node) + "\n");

    // ---- compact (canonical) ------------------------------------------------------------------------------

    private static void WriteCompact(StringBuilder sb, SvJsonNode node)
    {
        switch (node)
        {
            case SvJsonObject obj:
            {
                sb.Append('{');
                bool first = true;
                foreach (KeyValuePair<string, SvJsonNode> member in SortedMembers(obj))
                {
                    if (!first)
                    {
                        sb.Append(',');
                    }

                    first = false;
                    AppendString(sb, member.Key);
                    sb.Append(':');
                    WriteCompact(sb, member.Value);
                }

                sb.Append('}');
                break;
            }

            case SvJsonArray array:
            {
                sb.Append('[');
                for (int i = 0; i < array.Count; i++)
                {
                    if (i != 0)
                    {
                        sb.Append(',');
                    }

                    WriteCompact(sb, array[i]);
                }

                sb.Append(']');
                break;
            }

            default:
                AppendScalar(sb, node);
                break;
        }
    }

    // ---- pretty (on-disk layout, same as svmbuild.canonical.dumps with an indent) --------------------------

    private static void WritePretty(StringBuilder sb, SvJsonNode node, int level, int indent)
    {
        if (IsFlat(node))
        {
            int start = sb.Length;
            AppendInline(sb, node);
            if (CodePoints(sb, start) + indent * level <= InlineWidth)
            {
                return;
            }

            sb.Length = start;
        }

        switch (node)
        {
            case SvJsonObject obj:
            {
                if (obj.Count == 0)
                {
                    sb.Append("{}");
                    return;
                }

                sb.Append('{');
                bool first = true;
                foreach (KeyValuePair<string, SvJsonNode> member in SortedMembers(obj))
                {
                    if (!first)
                    {
                        sb.Append(',');
                    }

                    first = false;
                    sb.Append('\n').Append(' ', indent * (level + 1));
                    AppendString(sb, member.Key);
                    sb.Append(": ");
                    WritePretty(sb, member.Value, level + 1, indent);
                }

                sb.Append('\n').Append(' ', indent * level).Append('}');
                return;
            }

            case SvJsonArray array:
            {
                if (array.Count == 0)
                {
                    sb.Append("[]");
                    return;
                }

                sb.Append('[');
                for (int i = 0; i < array.Count; i++)
                {
                    if (i != 0)
                    {
                        sb.Append(',');
                    }

                    sb.Append('\n').Append(' ', indent * (level + 1));
                    WritePretty(sb, array[i], level + 1, indent);
                }

                sb.Append('\n').Append(' ', indent * level).Append(']');
                return;
            }

            default:
                AppendScalar(sb, node);
                return;
        }
    }

    /// <summary>A non-empty container whose members are all scalars.</summary>
    private static bool IsFlat(SvJsonNode node)
    {
        switch (node)
        {
            case SvJsonObject obj when obj.Count > 0:
                foreach (KeyValuePair<string, SvJsonNode> member in obj)
                {
                    if (member.Value is SvJsonObject or SvJsonArray)
                    {
                        return false;
                    }
                }

                return true;
            case SvJsonArray array when array.Count > 0:
                foreach (SvJsonNode item in array)
                {
                    if (item is SvJsonObject or SvJsonArray)
                    {
                        return false;
                    }
                }

                return true;
            default:
                return false;
        }
    }

    private static void AppendInline(StringBuilder sb, SvJsonNode node)
    {
        if (node is SvJsonObject obj)
        {
            sb.Append('{');
            bool first = true;
            foreach (KeyValuePair<string, SvJsonNode> member in SortedMembers(obj))
            {
                if (!first)
                {
                    sb.Append(", ");
                }

                first = false;
                AppendString(sb, member.Key);
                sb.Append(": ");
                AppendScalar(sb, member.Value);
            }

            sb.Append('}');
            return;
        }

        var array = (SvJsonArray)node;
        sb.Append('[');
        for (int i = 0; i < array.Count; i++)
        {
            if (i != 0)
            {
                sb.Append(", ");
            }

            AppendScalar(sb, array[i]);
        }

        sb.Append(']');
    }

    /// <summary>Text length in code points from <paramref name="start"/>, the measure the Python writer uses.</summary>
    private static int CodePoints(StringBuilder sb, int start)
    {
        int n = 0;
        for (int i = start; i < sb.Length; i++)
        {
            if (!char.IsLowSurrogate(sb[i]))
            {
                n++;
            }
        }

        return n;
    }

    // ---- shared --------------------------------------------------------------------------------------------

    private static List<KeyValuePair<string, SvJsonNode>> SortedMembers(SvJsonObject obj)
    {
        var members = new List<KeyValuePair<string, SvJsonNode>>(obj.Count);
        foreach (KeyValuePair<string, SvJsonNode> member in obj)
        {
            if (!IsAscii(member.Key))
            {
                throw new SvMapFormatException($"object key '{member.Key}' is not ASCII");
            }

            members.Add(member);
        }

        // Keys are ASCII, so ordinal order is byte order.
        members.Sort(static (a, b) => string.CompareOrdinal(a.Key, b.Key));
        return members;
    }

    private static bool IsAscii(string s)
    {
        foreach (char c in s)
        {
            if (c > 0x7F)
            {
                return false;
            }
        }

        return true;
    }

    private static void AppendScalar(StringBuilder sb, SvJsonNode node)
    {
        switch (node)
        {
            case SvJsonString s:
                AppendString(sb, s.Value);
                break;
            case SvJsonNumber { IsFloat: true } f:
                Float32Text.Append(sb, f.FloatValue);
                break;
            case SvJsonNumber i:
                sb.Append(i.IntegerText);
                break;
            default:
                sb.Append(node.Kind switch
                {
                    SvJsonKind.Null => "null",
                    SvJsonKind.True => "true",
                    SvJsonKind.False => "false",
                    _ => throw new InvalidOperationException($"{node.Kind} is not a scalar."),
                });
                break;
        }
    }

    /// <summary>Quotes a string escaping only <c>"</c>, <c>\</c> and characters below U+0020.</summary>
    internal static void AppendString(StringBuilder sb, string value)
    {
        sb.Append('"');
        foreach (char c in value)
        {
            switch (c)
            {
                case '"':
                    sb.Append("\\\"");
                    break;
                case '\\':
                    sb.Append("\\\\");
                    break;
                case '\b':
                    sb.Append("\\b");
                    break;
                case '\t':
                    sb.Append("\\t");
                    break;
                case '\n':
                    sb.Append("\\n");
                    break;
                case '\f':
                    sb.Append("\\f");
                    break;
                case '\r':
                    sb.Append("\\r");
                    break;
                default:
                    if (c < 0x20)
                    {
                        sb.Append("\\u00").Append(HexDigit(c >> 4)).Append(HexDigit(c & 0xF));
                    }
                    else
                    {
                        sb.Append(c);
                    }

                    break;
            }
        }

        sb.Append('"');
    }

    private static char HexDigit(int v) => (char)(v < 10 ? '0' + v : 'a' + v - 10);
}
