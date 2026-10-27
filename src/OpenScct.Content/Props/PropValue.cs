using System;
using System.Collections.Generic;
using OpenScct.Core;

namespace OpenScct.Content;

/// <summary>The typed forms a property value takes in <c>sim/gameplay.json</c> (README, "sim/gameplay.json").</summary>
public enum PropKind : byte
{
    /// <summary>An object reference set to None (<c>null</c>).</summary>
    Null,

    Bool,

    /// <summary><c>int</c>, <c>byte</c> or an enum (by value).</summary>
    Int,

    /// <summary>binary32.</summary>
    Float,

    /// <summary><c>string</c> or <c>name</c>.</summary>
    String,

    /// <summary>Reference to an actor of the map (<c>{"$actor": id}</c>).</summary>
    Actor,

    /// <summary>Reference to a non-actor object (<c>{"$asset": "Package.Group.Name"}</c>).</summary>
    Asset,

    /// <summary>Value of a <c>class&lt;...&gt;</c> property (<c>{"$class": "Package.Class"}</c>).</summary>
    Class,

    /// <summary>Struct keyed by member name.</summary>
    Struct,

    /// <summary>Dynamic array.</summary>
    Array,

    /// <summary>Static array (<c>ArrayDim &gt; 1</c>) with only the stored elements.</summary>
    StaticArray,
}

/// <summary>
/// One typed property value. Accessors throw <see cref="InvalidOperationException"/> on a kind mismatch; the
/// <c>TryGet</c> forms return false instead.
/// </summary>
public readonly struct PropValue : IEquatable<PropValue>
{
    private readonly object? _ref;
    private readonly int _int;
    private readonly float _float;

    private PropValue(PropKind kind, object? reference = null, int intValue = 0, float floatValue = 0f)
    {
        Kind = kind;
        _ref = reference;
        _int = intValue;
        _float = floatValue;
    }

    public PropKind Kind { get; }

    public bool IsNull => Kind == PropKind.Null;

    public static PropValue Null => default;

    public static PropValue FromBool(bool value) => new(PropKind.Bool, intValue: value ? 1 : 0);

    public static PropValue FromInt(int value) => new(PropKind.Int, intValue: value);

    public static PropValue FromFloat(float value)
    {
        if (!float.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "SVMap floats must be finite.");
        }

        return new PropValue(PropKind.Float, floatValue: value);
    }

    public static PropValue FromString(string value) => new(PropKind.String, value ?? throw new ArgumentNullException(nameof(value)));

    public static PropValue FromActor(ActorRef value) => new(PropKind.Actor, value ?? throw new ArgumentNullException(nameof(value)));

    public static PropValue FromAsset(AssetRef value) => new(PropKind.Asset, value ?? throw new ArgumentNullException(nameof(value)));

    /// <summary>A class value, <c>Package.Class</c>.</summary>
    public static PropValue FromClass(string classPath) =>
        new(PropKind.Class, classPath ?? throw new ArgumentNullException(nameof(classPath)));

    public static PropValue FromStruct(PropBag members) => new(PropKind.Struct, members ?? throw new ArgumentNullException(nameof(members)));

    public static PropValue FromArray(IReadOnlyList<PropValue> items) =>
        new(PropKind.Array, items ?? throw new ArgumentNullException(nameof(items)));

    public static PropValue FromStaticArray(PropStaticArray elements) =>
        new(PropKind.StaticArray, elements ?? throw new ArgumentNullException(nameof(elements)));

    public bool AsBool() => Kind == PropKind.Bool ? _int != 0 : throw Mismatch(PropKind.Bool);

    public int AsInt() => Kind == PropKind.Int ? _int : throw Mismatch(PropKind.Int);

    /// <summary>A byte or enum value; throws when the int is outside 0..255.</summary>
    public byte AsByte()
    {
        int v = AsInt();
        return v is >= 0 and <= 255 ? (byte)v : throw new InvalidOperationException($"Int {v} is not a byte.");
    }

    /// <summary>The float value. An Int converts to the nearest binary32, so integer-typed authoring still reads.</summary>
    public float AsFloat() => Kind switch
    {
        PropKind.Float => _float,
        PropKind.Int => _int,
        _ => throw Mismatch(PropKind.Float),
    };

    public string AsString() => Kind == PropKind.String ? (string)_ref! : throw Mismatch(PropKind.String);

    /// <summary>A name value; <c>""</c> and <c>"None"</c> are <see cref="Name.None"/>.</summary>
    public Name AsName() => new(AsString());

    public ActorRef AsActor() => Kind == PropKind.Actor ? (ActorRef)_ref! : throw Mismatch(PropKind.Actor);

    public AssetRef AsAsset() => Kind == PropKind.Asset ? (AssetRef)_ref! : throw Mismatch(PropKind.Asset);

    /// <summary>The <c>Package.Class</c> of a class value.</summary>
    public string AsClass() => Kind == PropKind.Class ? (string)_ref! : throw Mismatch(PropKind.Class);

    public PropBag AsStruct() => Kind == PropKind.Struct ? (PropBag)_ref! : throw Mismatch(PropKind.Struct);

    public IReadOnlyList<PropValue> AsArray() => Kind == PropKind.Array ? (IReadOnlyList<PropValue>)_ref! : throw Mismatch(PropKind.Array);

    public PropStaticArray AsStaticArray() =>
        Kind == PropKind.StaticArray ? (PropStaticArray)_ref! : throw Mismatch(PropKind.StaticArray);

    /// <summary>A Vector struct (<c>X</c>, <c>Y</c>, <c>Z</c>); missing members are 0.</summary>
    public FVector AsVector() => AsStruct().ToVector(default);

    /// <summary>A Rotator struct (<c>Pitch</c>, <c>Yaw</c>, <c>Roll</c>); missing members are 0.</summary>
    public FRotator AsRotator() => AsStruct().ToRotator(default);

    public bool TryGetBool(out bool value)
    {
        value = Kind == PropKind.Bool && _int != 0;
        return Kind == PropKind.Bool;
    }

    public bool TryGetInt(out int value)
    {
        value = Kind == PropKind.Int ? _int : 0;
        return Kind == PropKind.Int;
    }

    public bool TryGetFloat(out float value)
    {
        value = Kind switch
        {
            PropKind.Float => _float,
            PropKind.Int => _int,
            _ => 0f,
        };
        return Kind is PropKind.Float or PropKind.Int;
    }

    public bool TryGetString(out string value)
    {
        value = Kind == PropKind.String ? (string)_ref! : string.Empty;
        return Kind == PropKind.String;
    }

    public bool Equals(PropValue other)
    {
        if (Kind != other.Kind)
        {
            return false;
        }

        switch (Kind)
        {
            case PropKind.Null:
                return true;
            case PropKind.Bool:
            case PropKind.Int:
                return _int == other._int;
            case PropKind.Float:
                return BitConverter.SingleToInt32Bits(_float) == BitConverter.SingleToInt32Bits(other._float);
            case PropKind.String:
            case PropKind.Class:
                return string.Equals((string)_ref!, (string)other._ref!, StringComparison.Ordinal);
            case PropKind.Actor:
                return string.Equals(((ActorRef)_ref!).Id, ((ActorRef)other._ref!).Id, StringComparison.Ordinal);
            case PropKind.Asset:
                return string.Equals(((AssetRef)_ref!).Path, ((AssetRef)other._ref!).Path, StringComparison.Ordinal);
            case PropKind.Struct:
                return ((PropBag)_ref!).ContentEquals((PropBag)other._ref!);
            case PropKind.Array:
            {
                var a = (IReadOnlyList<PropValue>)_ref!;
                var b = (IReadOnlyList<PropValue>)other._ref!;
                if (a.Count != b.Count)
                {
                    return false;
                }

                for (int i = 0; i < a.Count; i++)
                {
                    if (!a[i].Equals(b[i]))
                    {
                        return false;
                    }
                }

                return true;
            }

            default:
                return ((PropStaticArray)_ref!).ContentEquals((PropStaticArray)other._ref!);
        }
    }

    public override bool Equals(object? obj) => obj is PropValue other && Equals(other);

    /// <summary>Deterministic across processes (no per-process seed).</summary>
    public override int GetHashCode()
    {
        unchecked
        {
            int k = (int)Kind * 486187739;
            return Kind switch
            {
                PropKind.Bool or PropKind.Int => k ^ _int,
                PropKind.Float => k ^ BitConverter.SingleToInt32Bits(_float),
                PropKind.String or PropKind.Class => k ^ StableHash.OfNameCaseless((string)_ref!),
                PropKind.Actor => k ^ StableHash.OfNameCaseless(((ActorRef)_ref!).Id),
                PropKind.Asset => k ^ StableHash.OfNameCaseless(((AssetRef)_ref!).Path),
                _ => k,
            };
        }
    }

    public static bool operator ==(PropValue left, PropValue right) => left.Equals(right);

    public static bool operator !=(PropValue left, PropValue right) => !left.Equals(right);

    public override string ToString() => PropJson.ToJson(this).ToString();

    private InvalidOperationException Mismatch(PropKind wanted) => new($"The value is {Kind}, not {wanted}.");
}
