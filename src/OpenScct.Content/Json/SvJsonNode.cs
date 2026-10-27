using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;

namespace OpenScct.Content.Json;

/// <summary>Kinds of SVMap JSON values. Numbers keep the float / integer split the hash rules depend on.</summary>
public enum SvJsonKind : byte
{
    Null,
    False,
    True,
    Integer,
    Float,
    String,
    Array,
    Object,
}

/// <summary>
/// A parsed SVMap JSON value. Unlike a general JSON DOM it keeps whether a number token was a float (it had a
/// <c>.</c>, <c>e</c> or <c>E</c>) and stores floats as binary32, as the SVMap conventions require.
/// </summary>
public abstract class SvJsonNode
{
    private protected SvJsonNode()
    {
    }

    public abstract SvJsonKind Kind { get; }

    public static SvJsonNode Null => SvJsonLiteral.NullValue;

    public static SvJsonNode True => SvJsonLiteral.TrueValue;

    public static SvJsonNode False => SvJsonLiteral.FalseValue;

    public static SvJsonNode Bool(bool value) => value ? SvJsonLiteral.TrueValue : SvJsonLiteral.FalseValue;

    public static SvJsonNode Float(float value) => SvJsonNumber.FromFloat(value);

    public static SvJsonNode Integer(long value) => SvJsonNumber.FromInteger(value);

    public static SvJsonNode String(string value) => new SvJsonString(value);

    /// <summary>Canonical compact text (conventions.md), handy in messages and tests.</summary>
    public override string ToString() => CanonicalJson.ToCanonicalString(this);
}

/// <summary><c>null</c>, <c>true</c> or <c>false</c>.</summary>
public sealed class SvJsonLiteral : SvJsonNode
{
    internal static readonly SvJsonLiteral NullValue = new(SvJsonKind.Null);
    internal static readonly SvJsonLiteral TrueValue = new(SvJsonKind.True);
    internal static readonly SvJsonLiteral FalseValue = new(SvJsonKind.False);

    private readonly SvJsonKind _kind;

    private SvJsonLiteral(SvJsonKind kind)
    {
        _kind = kind;
    }

    public override SvJsonKind Kind => _kind;
}

/// <summary>A string value.</summary>
public sealed class SvJsonString : SvJsonNode
{
    public SvJsonString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Value = value;
    }

    public string Value { get; }

    public override SvJsonKind Kind => SvJsonKind.String;
}

/// <summary>
/// A number. A float token is held as the binary32 it rounds to; an integer token is held as its canonical
/// decimal text (any size, <c>-0</c> becomes <c>0</c>).
/// </summary>
public sealed class SvJsonNumber : SvJsonNode
{
    private readonly float _float;
    private readonly string? _integer;

    private SvJsonNumber(float value)
    {
        _float = value;
    }

    private SvJsonNumber(string integerText)
    {
        _integer = integerText;
    }

    public bool IsFloat => _integer is null;

    public override SvJsonKind Kind => IsFloat ? SvJsonKind.Float : SvJsonKind.Integer;

    /// <summary>The binary32 value of a float token.</summary>
    public float FloatValue => IsFloat ? _float : throw new InvalidOperationException("The number is an integer.");

    /// <summary>Canonical decimal text of an integer token.</summary>
    public string IntegerText => _integer ?? throw new InvalidOperationException("The number is a float.");

    public static SvJsonNumber FromFloat(float value)
    {
        if (!float.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "SVMap floats must be finite.");
        }

        return new SvJsonNumber(value);
    }

    public static SvJsonNumber FromInteger(long value) => new(value.ToString(CultureInfo.InvariantCulture));

    /// <summary>From a JSON integer token (<c>-?(0|[1-9][0-9]*)</c>).</summary>
    public static SvJsonNumber FromIntegerToken(ReadOnlySpan<char> token)
    {
        if (!IsIntegerToken(token))
        {
            throw new FormatException($"'{token}' is not a JSON integer token.");
        }

        return new SvJsonNumber(token is "-0" ? "0" : token.ToString());
    }

    public bool TryGetInt32(out int value)
    {
        value = 0;
        return _integer is not null && int.TryParse(_integer, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);
    }

    public bool TryGetInt64(out long value)
    {
        value = 0;
        return _integer is not null && long.TryParse(_integer, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);
    }

    internal static bool IsIntegerToken(ReadOnlySpan<char> token)
    {
        int i = 0;
        if (i < token.Length && token[i] == '-')
        {
            i++;
        }

        if (i >= token.Length)
        {
            return false;
        }

        if (token[i] == '0')
        {
            return i + 1 == token.Length;
        }

        for (; i < token.Length; i++)
        {
            if (token[i] is < '0' or > '9')
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>An array value.</summary>
public sealed class SvJsonArray : SvJsonNode, IReadOnlyList<SvJsonNode>
{
    private readonly List<SvJsonNode> _items;

    public SvJsonArray()
    {
        _items = new List<SvJsonNode>();
    }

    public SvJsonArray(IEnumerable<SvJsonNode> items)
    {
        _items = new List<SvJsonNode>(items);
    }

    public override SvJsonKind Kind => SvJsonKind.Array;

    public int Count => _items.Count;

    public SvJsonNode this[int index] => _items[index];

    public void Add(SvJsonNode item)
    {
        ArgumentNullException.ThrowIfNull(item);
        _items.Add(item);
    }

    public List<SvJsonNode>.Enumerator GetEnumerator() => _items.GetEnumerator();

    IEnumerator<SvJsonNode> IEnumerable<SvJsonNode>.GetEnumerator() => _items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();
}

/// <summary>An object value. Members keep document order; keys are unique (exact match), as SVMap requires.</summary>
public sealed class SvJsonObject : SvJsonNode, IReadOnlyList<KeyValuePair<string, SvJsonNode>>
{
    private readonly List<KeyValuePair<string, SvJsonNode>> _members = new();
    private readonly Dictionary<string, SvJsonNode> _byKey = new(StringComparer.Ordinal);

    public override SvJsonKind Kind => SvJsonKind.Object;

    public int Count => _members.Count;

    public KeyValuePair<string, SvJsonNode> this[int index] => _members[index];

    /// <summary>The member called <paramref name="key"/> (exact match), or null.</summary>
    public SvJsonNode? this[string key] => _byKey.GetValueOrDefault(key);

    public bool ContainsKey(string key) => _byKey.ContainsKey(key);

    public bool TryGetValue(string key, out SvJsonNode value)
    {
        if (_byKey.TryGetValue(key, out SvJsonNode? found))
        {
            value = found;
            return true;
        }

        value = SvJsonLiteral.NullValue;
        return false;
    }

    /// <summary>Adds a member. Throws on a duplicate key.</summary>
    public void Add(string key, SvJsonNode value)
    {
        if (!TryAdd(key, value))
        {
            throw new ArgumentException($"Duplicate key '{key}'.", nameof(key));
        }
    }

    public bool TryAdd(string key, SvJsonNode value)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);
        if (!_byKey.TryAdd(key, value))
        {
            return false;
        }

        _members.Add(new KeyValuePair<string, SvJsonNode>(key, value));
        return true;
    }

    public List<KeyValuePair<string, SvJsonNode>>.Enumerator GetEnumerator() => _members.GetEnumerator();

    IEnumerator<KeyValuePair<string, SvJsonNode>> IEnumerable<KeyValuePair<string, SvJsonNode>>.GetEnumerator() =>
        _members.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _members.GetEnumerator();
}
