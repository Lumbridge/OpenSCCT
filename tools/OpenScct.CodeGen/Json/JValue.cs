using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;

namespace OpenScct.CodeGen.Json;

internal enum JKind
{
    Null,
    Bool,
    Number,
    String,
    Array,
    Object,
}

/// <summary>
/// An immutable JSON value with object members kept in document order. Small and comparable, so the generator
/// can diff class defaults without depending on JsonDocument lifetimes.
/// </summary>
internal sealed class JValue
{
    public static readonly JValue Null = new(JKind.Null);
    public static readonly JValue True = new(JKind.Bool) { Bool = true };
    public static readonly JValue False = new(JKind.Bool) { Bool = false };

    private JValue(JKind kind)
    {
        Kind = kind;
    }

    public JKind Kind { get; }

    public bool Bool { get; private init; }

    /// <summary>Number value as double (for integers, exact up to 2^53).</summary>
    public double Number { get; private init; }

    /// <summary>The number's JSON text.</summary>
    public string? NumberText { get; private init; }

    public string? String { get; private init; }

    public IReadOnlyList<JValue> Items { get; private init; } = [];

    public IReadOnlyList<KeyValuePair<string, JValue>> Members { get; private init; } = [];

    public bool IsNull => Kind == JKind.Null;

    public bool IsInteger => Kind == JKind.Number && NumberText is not null &&
        NumberText.IndexOfAny(['.', 'e', 'E']) < 0;

    public static JValue FromBool(bool b) => b ? True : False;

    public static JValue FromNumber(double d) =>
        new(JKind.Number) { Number = d, NumberText = d.ToString("R", CultureInfo.InvariantCulture) };

    public static JValue FromInt(long i) =>
        new(JKind.Number) { Number = i, NumberText = i.ToString(CultureInfo.InvariantCulture) };

    public static JValue FromString(string s) => new(JKind.String) { String = s };

    public static JValue FromArray(IReadOnlyList<JValue> items) => new(JKind.Array) { Items = items };

    public static JValue FromObject(IReadOnlyList<KeyValuePair<string, JValue>> members) => new(JKind.Object) { Members = members };

    public static JValue From(JsonElement e)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                return Null;
            case JsonValueKind.True:
                return True;
            case JsonValueKind.False:
                return False;
            case JsonValueKind.Number:
                return new JValue(JKind.Number) { Number = e.GetDouble(), NumberText = e.GetRawText() };
            case JsonValueKind.String:
                return FromString(e.GetString()!);
            case JsonValueKind.Array:
            {
                var items = new List<JValue>(e.GetArrayLength());
                foreach (JsonElement item in e.EnumerateArray())
                {
                    items.Add(From(item));
                }

                return FromArray(items);
            }

            case JsonValueKind.Object:
            {
                var members = new List<KeyValuePair<string, JValue>>();
                foreach (JsonProperty p in e.EnumerateObject())
                {
                    members.Add(new KeyValuePair<string, JValue>(p.Name, From(p.Value)));
                }

                return FromObject(members);
            }

            default:
                throw new InvalidOperationException($"Unexpected JSON value kind {e.ValueKind}.");
        }
    }

    public static JValue Parse(string json)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        return From(doc.RootElement);
    }

    /// <summary>The member named <paramref name="name"/> (exact case first, then ASCII case ignored), or null.</summary>
    public JValue? Get(string name)
    {
        foreach (KeyValuePair<string, JValue> m in Members)
        {
            if (string.Equals(m.Key, name, StringComparison.Ordinal))
            {
                return m.Value;
            }
        }

        foreach (KeyValuePair<string, JValue> m in Members)
        {
            if (string.Equals(m.Key, name, StringComparison.OrdinalIgnoreCase))
            {
                return m.Value;
            }
        }

        return null;
    }

    public override string ToString() => Kind switch
    {
        JKind.Null => "null",
        JKind.Bool => Bool ? "true" : "false",
        JKind.Number => NumberText ?? "0",
        JKind.String => "\"" + String + "\"",
        JKind.Array => "[" + string.Join(", ", Items) + "]",
        _ => "{" + string.Join(", ", Members) + "}",
    };
}
