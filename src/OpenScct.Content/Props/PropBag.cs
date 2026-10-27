using System;
using System.Collections;
using System.Collections.Generic;
using OpenScct.Core;

namespace OpenScct.Content;

/// <summary>
/// Typed property values keyed by UnrealScript name: an actor's <c>props</c>, a struct's members or a game mode's
/// settings. Lookups ignore ASCII case, as UnrealScript names do; enumeration keeps document order. An actor's bag
/// holds only the values that differ from the class defaults, so the <c>Get</c> helpers take the default to use
/// when a property is absent, and throw when it is present with another type.
/// </summary>
public sealed class PropBag : IReadOnlyCollection<KeyValuePair<string, PropValue>>
{
    public static readonly PropBag Empty = new(Array.Empty<KeyValuePair<string, PropValue>>());

    private readonly KeyValuePair<string, PropValue>[] _items;
    private readonly Dictionary<string, int> _byName;

    /// <summary>Builds a bag; names must be unique ignoring ASCII case.</summary>
    public PropBag(IEnumerable<KeyValuePair<string, PropValue>> props)
    {
        ArgumentNullException.ThrowIfNull(props);
        var items = new List<KeyValuePair<string, PropValue>>(props);
        _byName = new Dictionary<string, int>(items.Count, AsciiCaseComparer.Instance);
        for (int i = 0; i < items.Count; i++)
        {
            ArgumentNullException.ThrowIfNull(items[i].Key, nameof(props));
            if (!_byName.TryAdd(items[i].Key, i))
            {
                throw new ArgumentException($"Property '{items[i].Key}' is given twice (names ignore case).", nameof(props));
            }
        }

        _items = items.ToArray();
    }

    public int Count => _items.Length;

    /// <summary>Property names in document order.</summary>
    public IEnumerable<string> Names
    {
        get
        {
            foreach (KeyValuePair<string, PropValue> item in _items)
            {
                yield return item.Key;
            }
        }
    }

    /// <summary>The value of <paramref name="name"/>; throws <see cref="KeyNotFoundException"/> when it is absent.</summary>
    public PropValue this[string name] =>
        TryGetValue(name, out PropValue v) ? v : throw new KeyNotFoundException($"Property '{name}' is not set.");

    public bool Contains(string name) => _byName.ContainsKey(name);

    public bool TryGetValue(string name, out PropValue value)
    {
        if (_byName.TryGetValue(name, out int i))
        {
            value = _items[i].Value;
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>Lookup by exact spelling, as the derived indexes of gameplay.json are defined.</summary>
    internal bool TryGetExact(string name, out PropValue value)
    {
        if (_byName.TryGetValue(name, out int i) && string.Equals(_items[i].Key, name, StringComparison.Ordinal))
        {
            value = _items[i].Value;
            return true;
        }

        value = default;
        return false;
    }

    public bool GetBool(string name, bool defaultValue = false) =>
        TryGetValue(name, out PropValue v) ? Need(name, v, PropKind.Bool).AsBool() : defaultValue;

    public int GetInt(string name, int defaultValue = 0) =>
        TryGetValue(name, out PropValue v) ? Need(name, v, PropKind.Int).AsInt() : defaultValue;

    /// <summary>A byte or enum property; throws when the stored int is outside 0..255.</summary>
    public byte GetByte(string name, byte defaultValue = 0)
    {
        if (!TryGetValue(name, out PropValue v))
        {
            return defaultValue;
        }

        int i = Need(name, v, PropKind.Int).AsInt();
        return i is >= 0 and <= 255 ? (byte)i : throw new InvalidOperationException($"Property '{name}': {i} is not a byte.");
    }

    /// <summary>A float property; an Int value converts to the nearest binary32.</summary>
    public float GetFloat(string name, float defaultValue = 0f)
    {
        if (!TryGetValue(name, out PropValue v))
        {
            return defaultValue;
        }

        return v.TryGetFloat(out float f) ? f : throw Mismatch(name, v, PropKind.Float);
    }

    public string GetString(string name, string defaultValue = "") =>
        TryGetValue(name, out PropValue v) ? Need(name, v, PropKind.String).AsString() : defaultValue;

    /// <summary>A name property; <c>""</c> and <c>"None"</c> read as <see cref="Name.None"/>.</summary>
    public Name GetName(string name, Name defaultValue = default) =>
        TryGetValue(name, out PropValue v) ? Need(name, v, PropKind.String).AsName() : defaultValue;

    /// <summary>A byte or int property as an enum.</summary>
    public TEnum GetEnum<TEnum>(string name, TEnum defaultValue = default)
        where TEnum : struct, Enum =>
        TryGetValue(name, out PropValue v) ? (TEnum)Enum.ToObject(typeof(TEnum), Need(name, v, PropKind.Int).AsInt()) : defaultValue;

    /// <summary>A Vector struct property; members it does not store come from <paramref name="defaultValue"/>.</summary>
    public FVector GetVector(string name, FVector defaultValue = default) =>
        TryGetValue(name, out PropValue v) ? Need(name, v, PropKind.Struct).AsStruct().ToVector(defaultValue) : defaultValue;

    /// <summary>A Rotator struct property; members it does not store come from <paramref name="defaultValue"/>.</summary>
    public FRotator GetRotator(string name, FRotator defaultValue = default) =>
        TryGetValue(name, out PropValue v) ? Need(name, v, PropKind.Struct).AsStruct().ToRotator(defaultValue) : defaultValue;

    /// <summary>The reference held by an object property, or null when absent or None.</summary>
    public ActorRef? GetActorRef(string name) =>
        TryGetValue(name, out PropValue v) && !v.IsNull ? Need(name, v, PropKind.Actor).AsActor() : null;

    /// <summary>The actor an object property refers to, or null when absent, None or dangling.</summary>
    public ActorRecord? GetActor(string name) => GetActorRef(name)?.Target;

    public AssetRef? GetAsset(string name) =>
        TryGetValue(name, out PropValue v) && !v.IsNull ? Need(name, v, PropKind.Asset).AsAsset() : null;

    /// <summary>The <c>Package.Class</c> of a class property, or null when absent or None.</summary>
    public string? GetClass(string name) =>
        TryGetValue(name, out PropValue v) && !v.IsNull ? Need(name, v, PropKind.Class).AsClass() : null;

    public PropBag? GetStruct(string name) =>
        TryGetValue(name, out PropValue v) ? Need(name, v, PropKind.Struct).AsStruct() : null;

    /// <summary>A dynamic array property, empty when absent.</summary>
    public IReadOnlyList<PropValue> GetArray(string name) =>
        TryGetValue(name, out PropValue v) ? Need(name, v, PropKind.Array).AsArray() : Array.Empty<PropValue>();

    public PropStaticArray? GetStaticArray(string name) =>
        TryGetValue(name, out PropValue v) ? Need(name, v, PropKind.StaticArray).AsStaticArray() : null;

    /// <summary>This bag read as a Vector struct over <paramref name="fallback"/>.</summary>
    public FVector ToVector(FVector fallback) =>
        new(GetFloat("X", fallback.X), GetFloat("Y", fallback.Y), GetFloat("Z", fallback.Z));

    /// <summary>This bag read as a Rotator struct over <paramref name="fallback"/>.</summary>
    public FRotator ToRotator(FRotator fallback) =>
        new(GetInt("Pitch", fallback.Pitch), GetInt("Yaw", fallback.Yaw), GetInt("Roll", fallback.Roll));

    /// <summary>Same names (exact spelling) with equal values, in any order.</summary>
    public bool ContentEquals(PropBag other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (other.Count != Count)
        {
            return false;
        }

        foreach (KeyValuePair<string, PropValue> item in _items)
        {
            if (!other.TryGetExact(item.Key, out PropValue v) || !v.Equals(item.Value))
            {
                return false;
            }
        }

        return true;
    }

    public IEnumerator<KeyValuePair<string, PropValue>> GetEnumerator() =>
        ((IEnumerable<KeyValuePair<string, PropValue>>)_items).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public override string ToString() => PropJson.BagToJson(this).ToString();

    private static PropValue Need(string name, PropValue v, PropKind kind) => v.Kind == kind ? v : throw Mismatch(name, v, kind);

    private static InvalidOperationException Mismatch(string name, PropValue v, PropKind kind) =>
        new($"Property '{name}' is {v.Kind}, not {kind}.");
}
