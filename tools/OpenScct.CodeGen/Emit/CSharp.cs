using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace OpenScct.CodeGen.Emit;

/// <summary>C# identifiers and literals, written the same way on every machine.</summary>
internal static class CSharp
{
    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class", "const",
        "continue", "decimal", "default", "delegate", "do", "double", "else", "enum", "event", "explicit", "extern",
        "false", "finally", "fixed", "float", "for", "foreach", "goto", "if", "implicit", "in", "int", "interface",
        "internal", "is", "lock", "long", "namespace", "new", "null", "object", "operator", "out", "override",
        "params", "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed", "short",
        "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true", "try", "typeof",
        "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual", "void", "volatile", "while",
    };

    /// <summary>The identifier, escaped with @ when it is a C# keyword.</summary>
    public static string Ident(string name) => Keywords.Contains(name) ? "@" + name : name;

    public static bool IsKeyword(string name) => Keywords.Contains(name);

    /// <summary>A regular string literal. Non-ASCII and control characters become \u escapes.</summary>
    public static string String(string? value)
    {
        if (value is null)
        {
            return "null";
        }

        var sb = new StringBuilder(value.Length + 2);
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
                case '\n':
                    sb.Append("\\n");
                    break;
                case '\r':
                    sb.Append("\\r");
                    break;
                case '\t':
                    sb.Append("\\t");
                    break;
                case '\0':
                    sb.Append("\\0");
                    break;
                default:
                    if (c < 0x20 || c > 0x7E)
                    {
                        sb.Append("\\u").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        sb.Append(c);
                    }

                    break;
            }
        }

        sb.Append('"');
        return sb.ToString();
    }

    /// <summary>A float literal that round-trips the exact binary32 value.</summary>
    public static string Float(float value)
    {
        if (float.IsNaN(value))
        {
            return "float.NaN";
        }

        if (float.IsPositiveInfinity(value))
        {
            return "float.PositiveInfinity";
        }

        if (float.IsNegativeInfinity(value))
        {
            return "float.NegativeInfinity";
        }

        if (value == 0f && float.IsNegative(value))
        {
            return "-0f";
        }

        string text = value.ToString("R", CultureInfo.InvariantCulture);
        if (float.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture) != value)
        {
            throw new InvalidOperationException($"Float {value} does not round-trip as '{text}'.");
        }

        return text + "f";
    }

    public static string Int(long value) => value.ToString(CultureInfo.InvariantCulture);

    public static string Bool(bool value) => value ? "true" : "false";

    /// <summary>A uint literal in hex, for flag casts.</summary>
    public static string Hex(uint value) => "0x" + value.ToString("X8", CultureInfo.InvariantCulture) + "u";

    /// <summary>True for a valid UnrealScript (and C#) identifier.</summary>
    public static bool IsIdentifier(string name)
    {
        if (string.IsNullOrEmpty(name) || !(char.IsAsciiLetter(name[0]) || name[0] == '_'))
        {
            return false;
        }

        foreach (char c in name)
        {
            if (!(char.IsAsciiLetterOrDigit(c) || c == '_'))
            {
                return false;
            }
        }

        return true;
    }
}
