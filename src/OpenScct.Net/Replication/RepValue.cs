using System;
using OpenScct.Core;

namespace OpenScct.Net;

/// <summary>What a replication condition node evaluates to.</summary>
public enum RepValueKind : byte
{
    /// <summary>The None reference (also what a member of None reads as).</summary>
    None,
    Bool,

    /// <summary>Ints, bytes, enums and name indexes.</summary>
    Int,
    Float,

    /// <summary>A UObject reference (never null; null is <see cref="None"/>).</summary>
    Object,

    /// <summary>A string, an asset path or a class path (never null; null is <see cref="None"/>).</summary>
    Text,
}

/// <summary>
/// A value in a replication condition, with UnrealScript comparison rules: numbers compare by value (bools as 0
/// and 1, ints widened to float when the other side is a float), references by identity, text ignoring ASCII case
/// (asset and class paths), and None equals a null reference or zero.
/// </summary>
public readonly struct RepValue
{
    private RepValue(RepValueKind kind, int i, float f, object? o)
    {
        Kind = kind;
        I = i;
        F = f;
        O = o;
    }

    public RepValueKind Kind { get; }

    public int I { get; }

    public float F { get; }

    public object? O { get; }

    public static RepValue None => default;

    public static RepValue Bool(bool value) => new(RepValueKind.Bool, value ? 1 : 0, 0f, null);

    public static RepValue Int(int value) => new(RepValueKind.Int, value, 0f, null);

    public static RepValue Float(float value) => new(RepValueKind.Float, 0, value, null);

    public static RepValue Object(UObject? value) => value is null ? default : new(RepValueKind.Object, 0, 0f, value);

    public static RepValue Text(string? value) => value is null ? default : new(RepValueKind.Text, 0, 0f, value);

    /// <summary>UnrealScript truth: non-zero numbers and non-None references.</summary>
    public bool AsBool => Kind switch
    {
        RepValueKind.Bool or RepValueKind.Int => I != 0,
        RepValueKind.Float => F != 0f,
        RepValueKind.Object or RepValueKind.Text => true,
        _ => false,
    };

    private bool IsNumeric => Kind is RepValueKind.Bool or RepValueKind.Int or RepValueKind.Float or RepValueKind.None;

    /// <summary>Equality as UnrealScript <c>==</c> sees it.</summary>
    public static bool AreEqual(in RepValue a, in RepValue b)
    {
        if (a.Kind is RepValueKind.Object or RepValueKind.Text || b.Kind is RepValueKind.Object or RepValueKind.Text)
        {
            if (a.Kind == RepValueKind.Text && b.Kind == RepValueKind.Text)
            {
                return string.Equals((string)a.O!, (string)b.O!, StringComparison.OrdinalIgnoreCase);
            }

            return ReferenceEquals(a.O, b.O);
        }

        if (a.Kind == RepValueKind.Float || b.Kind == RepValueKind.Float)
        {
            return a.Number == b.Number;
        }

        return a.I == b.I;
    }

    /// <summary>
    /// Ordering for <c>&lt;</c>, <c>&lt;=</c>, <c>&gt;</c> and <c>&gt;=</c>: -1, 0 or 1, or null when either side is a
    /// reference or text (the comparison is then false).
    /// </summary>
    public static int? Order(in RepValue a, in RepValue b)
    {
        if (!a.IsNumeric || !b.IsNumeric)
        {
            return null;
        }

        if (a.Kind == RepValueKind.Float || b.Kind == RepValueKind.Float)
        {
            float x = a.Number;
            float y = b.Number;
            return x < y ? -1 : x > y ? 1 : 0;
        }

        return a.I < b.I ? -1 : a.I > b.I ? 1 : 0;
    }

    private float Number => Kind == RepValueKind.Float ? F : I;

    public override string ToString() => Kind switch
    {
        RepValueKind.None => "None",
        RepValueKind.Bool => I != 0 ? "true" : "false",
        RepValueKind.Int => I.ToString(System.Globalization.CultureInfo.InvariantCulture),
        RepValueKind.Float => F.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
        _ => O?.ToString() ?? "None",
    };
}
