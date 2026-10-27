using System;
using System.Globalization;

namespace OpenScct.Content.Json;

/// <summary>Typed access to parsed SVMap JSON with errors that name the file and the JSON path.</summary>
internal readonly struct JsonRead
{
    public JsonRead(string? file)
    {
        File = file;
    }

    public string? File { get; }

    public SvMapFormatException Error(string path, string message) => new(File, $"{path}: {message}");

    public SvJsonObject Object(SvJsonNode node, string path) =>
        node as SvJsonObject ?? throw Error(path, $"expected an object, found {Describe(node)}");

    public SvJsonArray Array(SvJsonNode node, string path) =>
        node as SvJsonArray ?? throw Error(path, $"expected an array, found {Describe(node)}");

    public SvJsonNode Member(SvJsonObject obj, string key, string path) =>
        obj.TryGetValue(key, out SvJsonNode value) ? value : throw Error(path, $"missing '{key}'");

    public SvJsonObject ObjectMember(SvJsonObject obj, string key, string path) =>
        Object(Member(obj, key, path), $"{path}/{key}");

    public SvJsonArray ArrayMember(SvJsonObject obj, string key, string path) =>
        Array(Member(obj, key, path), $"{path}/{key}");

    public string String(SvJsonNode node, string path) =>
        node is SvJsonString s ? s.Value : throw Error(path, $"expected a string, found {Describe(node)}");

    public string StringMember(SvJsonObject obj, string key, string path) => String(Member(obj, key, path), $"{path}/{key}");

    public string? OptionalString(SvJsonObject obj, string key, string path) =>
        obj.TryGetValue(key, out SvJsonNode value) ? String(value, $"{path}/{key}") : null;

    public long Integer(SvJsonNode node, string path, long min = long.MinValue, long max = long.MaxValue)
    {
        if (node is not SvJsonNumber { IsFloat: false } n)
        {
            throw Error(path, $"expected an integer, found {Describe(node)}");
        }

        if (!n.TryGetInt64(out long v) || v < min || v > max)
        {
            throw Error(path, $"integer {n.IntegerText} is outside {min}..{max}");
        }

        return v;
    }

    public long IntegerMember(SvJsonObject obj, string key, string path, long min = long.MinValue, long max = long.MaxValue) =>
        Integer(Member(obj, key, path), $"{path}/{key}", min, max);

    public long? OptionalInteger(SvJsonObject obj, string key, string path, long min = long.MinValue, long max = long.MaxValue) =>
        obj.TryGetValue(key, out SvJsonNode value) ? Integer(value, $"{path}/{key}", min, max) : null;

    /// <summary>A float-typed value; an integer token is accepted and converted exactly as binary32 rounding does.</summary>
    public float Number(SvJsonNode node, string path)
    {
        return node switch
        {
            SvJsonNumber { IsFloat: true } f => f.FloatValue,
            SvJsonNumber i => float.Parse(i.IntegerText, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture) is var v && float.IsFinite(v)
                ? v
                : throw Error(path, $"integer {i.IntegerText} overflows binary32"),
            _ => throw Error(path, $"expected a number, found {Describe(node)}"),
        };
    }

    /// <summary>Checks a <c>formatVersion</c> of SVMap major version 1 (minor versions only add).</summary>
    public string FormatVersion(SvJsonObject obj, string path)
    {
        string version = StringMember(obj, "formatVersion", path);
        string[] parts = version.Split('.');
        if (parts.Length != 3 || parts[0] != "1" || !IsDecimal(parts[1]) || !IsDecimal(parts[2]))
        {
            throw Error($"{path}/formatVersion", $"'{version}' is not an SVMap 1.x version");
        }

        return version;
    }

    public static string Describe(SvJsonNode node) => node.Kind switch
    {
        SvJsonKind.Null => "null",
        SvJsonKind.True or SvJsonKind.False => "a bool",
        SvJsonKind.Integer => "an integer",
        SvJsonKind.Float => "a float",
        SvJsonKind.String => "a string",
        SvJsonKind.Array => "an array",
        _ => "an object",
    };

    private static bool IsDecimal(string s)
    {
        if (s.Length == 0 || (s.Length > 1 && s[0] == '0'))
        {
            return false;
        }

        foreach (char c in s)
        {
            if (!char.IsAsciiDigit(c))
            {
                return false;
            }
        }

        return true;
    }
}
