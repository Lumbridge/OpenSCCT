using System;
using OpenScct.Core;
using OpenScct.Core.Uc;
using OpenScct.Provenance;

namespace OpenScct.Net;

/// <summary>
/// How one replicated value goes over the wire (spec/native/net-replication.md rule 17,
/// <c>UStructProperty::NetSerializeItem</c>): ints, floats, names and strings exact, bytes in 8 bits, enums in as
/// few bits as the enum needs, bools in 1 bit, vectors rounded to whole units, rotators reduced to their high byte,
/// planes rounded to 16-bit ints, a compressed position as rounded location and velocity with pitch and yaw bytes,
/// and object references through the receiving connection's map.
/// </summary>
public enum RepLeafKind : byte
{
    Bool,
    Byte,
    Enum,
    Int,
    Float,
    Name,
    String,
    Asset,
    Class,
    Object,
    Vector,
    Rotator,
    Plane,
    CompressedPosition,

    /// <summary>Hand-written extensions only: a vector in 1/16 uu (the M0 packed vector).</summary>
    VectorFine,

    /// <summary>Hand-written extensions only: a rotator with all 16 bits per axis.</summary>
    RotatorFine,
}

/// <summary>
/// Maps object references for one connection (UE2 UPackageMapLevel::SerializeObject). A dynamic actor goes as its
/// net id, and counts as mapped only when the receiver has its channel open; a map actor (bStatic or bNoDelete) goes
/// as its SVMap actor index; None, non-actor objects and actors the receiver cannot resolve go as None.
/// </summary>
public interface INetObjectMap
{
    /// <summary>Writes <paramref name="value"/>; <paramref name="mapped"/> is false when it had to be written as None.</summary>
    void WriteObject(FBitWriter writer, UObject? value, out bool mapped);

    UObject? ReadObject(FBitReader reader);
}

/// <summary>
/// One argument value of a replicated function call. Which fields are used depends on the parameter's
/// <see cref="RepLeafKind"/>: <see cref="I"/> for bools, bytes, enums and ints; <see cref="F"/> for floats (and
/// a plane's W); <see cref="V"/> for vectors (a plane's XYZ, a compressed position's location);
/// <see cref="V2"/> for a compressed position's velocity; <see cref="R"/> for rotators; <see cref="O"/> for
/// objects, strings, names (their text), asset and class paths.
/// </summary>
public struct NetValue
{
    public int I;
    public float F;
    public FVector V;
    public FVector V2;
    public FRotator R;
    public object? O;

    public static NetValue FromBool(bool value) => new() { I = value ? 1 : 0 };

    public static NetValue FromInt(int value) => new() { I = value };

    public static NetValue FromFloat(float value) => new() { F = value };

    public static NetValue FromVector(FVector value) => new() { V = value };

    public static NetValue FromRotator(FRotator value) => new() { R = value };

    public static NetValue FromObject(UObject? value) => new() { O = value };

    public static NetValue FromName(Name value) => new() { O = value.IsNone ? null : value.ToString() };

    public static NetValue FromString(string? value) => new() { O = value };

    public readonly bool AsBool => I != 0;

    public readonly Name AsName => O is string s ? new Name(s) : Name.None;

    public readonly string AsString => O as string ?? string.Empty;

    /// <summary>True when the value is the zero value of its kind (UE2 sends a single 0 bit for it).</summary>
    public readonly bool IsZero(RepLeafKind kind) => kind switch
    {
        RepLeafKind.Bool or RepLeafKind.Byte or RepLeafKind.Enum or RepLeafKind.Int => I == 0,
        RepLeafKind.Float => F == 0f,
        RepLeafKind.Name or RepLeafKind.String or RepLeafKind.Asset or RepLeafKind.Class => string.IsNullOrEmpty(O as string),
        RepLeafKind.Object => O is null,
        RepLeafKind.Vector or RepLeafKind.VectorFine => V.IsZero(),
        RepLeafKind.Rotator or RepLeafKind.RotatorFine => R.Pitch == 0 && R.Yaw == 0 && R.Roll == 0,
        RepLeafKind.Plane => V.IsZero() && F == 0f,
        RepLeafKind.CompressedPosition => V.IsZero() && V2.IsZero() && R.Pitch == 0 && R.Yaw == 0,
        _ => true,
    };
}

/// <summary>
/// Encoding of every <see cref="RepLeafKind"/>, shared by properties and function parameters. Properties are
/// compared and sent in quantised form (shadow words), so a client ends up with exactly the value the server's
/// comparison saw.
/// </summary>
public static class NetWire
{
    /// <summary>Longest string a replicated value may carry, in UTF-8 bytes (longer text is clipped).</summary>
    public const int MaxStringBytes = 4096;

    /// <summary>Shadow words a leaf of this kind uses.</summary>
    public static int WordCount(RepLeafKind kind) => kind switch
    {
        RepLeafKind.String or RepLeafKind.Asset or RepLeafKind.Class => 0,
        RepLeafKind.Vector or RepLeafKind.Rotator or RepLeafKind.VectorFine or RepLeafKind.RotatorFine => 3,
        RepLeafKind.Plane => 4,
        RepLeafKind.CompressedPosition => 8,
        _ => 1,
    };

    /// <summary>Shadow object slots a leaf of this kind uses.</summary>
    public static int ObjectCount(RepLeafKind kind) =>
        kind is RepLeafKind.Name or RepLeafKind.String or RepLeafKind.Asset or RepLeafKind.Class or RepLeafKind.Object ? 1 : 0;

    /// <summary>A vector component rounded to the nearest whole unit (<c>floor(x + 0.5)</c>).</summary>
    [Provenance(ProvenanceKind.Decoded, "spec/native/net-replication.md rule 17: vectors rounded to the nearest integer")]
    public static int RoundComponent(float value) => FBitWriter.ClampVectorComponent(SvmMath.Floor(value + 0.5f));

    /// <summary>A rotator axis reduced to its high byte (truncation; negative angles wrap to 0..255).</summary>
    [Provenance(ProvenanceKind.Decoded, "spec/native/net-replication.md rule 17: rotators reduced to (r >> 8) & 255")]
    public static int RotatorByte(int axis) => (axis >> 8) & 255;

    /// <summary>A plane component rounded to a 16-bit int.</summary>
    public static int PlaneComponent(float value) => Math.Clamp(SvmMath.Floor(value + 0.5f), short.MinValue, short.MaxValue);

    /// <summary>Bit width of an enum with <paramref name="count"/> values, as WriteInt over the count.</summary>
    public static uint EnumMax(int count) => (uint)Math.Max(2, count);

    // ---- quantise (properties) -------------------------------------------------------------------------------

    public static void QuantizeVector(FVector v, Span<int> words)
    {
        words[0] = RoundComponent(v.X);
        words[1] = RoundComponent(v.Y);
        words[2] = RoundComponent(v.Z);
    }

    public static void QuantizeRotator(FRotator r, Span<int> words)
    {
        words[0] = RotatorByte(r.Pitch);
        words[1] = RotatorByte(r.Yaw);
        words[2] = RotatorByte(r.Roll);
    }

    public static void QuantizePlane(FVector xyz, float w, Span<int> words)
    {
        words[0] = PlaneComponent(xyz.X);
        words[1] = PlaneComponent(xyz.Y);
        words[2] = PlaneComponent(xyz.Z);
        words[3] = PlaneComponent(w);
    }

    /// <summary>Location rounded (3 words), pitch and yaw bytes (2), velocity rounded (3).</summary>
    public static void QuantizeCompressedPosition(FVector location, FRotator rotation, FVector velocity, Span<int> words)
    {
        QuantizeVector(location, words);
        words[3] = RotatorByte(rotation.Pitch);
        words[4] = RotatorByte(rotation.Yaw);
        QuantizeVector(velocity, words[5..]);
    }

    // ---- write and read quantised words ----------------------------------------------------------------------

    /// <summary>Writes a leaf from its shadow words and objects; false when an object could not be mapped.</summary>
    public static bool WriteWords(FBitWriter w, RepLeafKind kind, int enumCount, ReadOnlySpan<int> words,
        ReadOnlySpan<object?> objects, INetObjectMap map)
    {
        switch (kind)
        {
            case RepLeafKind.Bool:
                w.WriteBit(words[0] != 0);
                return true;
            case RepLeafKind.Byte:
                w.WriteByte((byte)words[0]);
                return true;
            case RepLeafKind.Enum:
                w.WriteInt((uint)Math.Clamp(words[0], 0, (int)EnumMax(enumCount) - 1), EnumMax(enumCount));
                return true;
            case RepLeafKind.Int:
            case RepLeafKind.Float:
                w.WriteInt32(words[0]);
                return true;
            case RepLeafKind.Name:
            case RepLeafKind.String:
            case RepLeafKind.Asset:
            case RepLeafKind.Class:
                WriteText(w, objects[0] as string);
                return true;
            case RepLeafKind.Object:
                map.WriteObject(w, objects[0] as UObject, out bool mapped);
                return mapped;
            case RepLeafKind.Vector:
                w.WriteRoundedVector(words[0], words[1], words[2]);
                return true;
            case RepLeafKind.Rotator:
                WriteRotatorBytes(w, words[0], words[1], words[2]);
                return true;
            case RepLeafKind.Plane:
                for (int i = 0; i < 4; i++)
                {
                    w.WriteBits((ushort)(short)words[i], 16);
                }

                return true;
            case RepLeafKind.CompressedPosition:
                w.WriteRoundedVector(words[0], words[1], words[2]);
                WriteByteAxis(w, words[3]);
                WriteByteAxis(w, words[4]);
                w.WriteRoundedVector(words[5], words[6], words[7]);
                return true;
            case RepLeafKind.VectorFine:
                w.WritePackedVector(words[0], words[1], words[2]);
                return true;
            case RepLeafKind.RotatorFine:
                w.WriteCompressedRotator(new FRotator(words[0], words[1], words[2]));
                return true;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
        }
    }

    /// <summary>Reads a leaf written by <see cref="WriteWords"/> or <see cref="WriteValue"/> into a <see cref="NetValue"/>.</summary>
    public static void Read(FBitReader r, RepLeafKind kind, int enumCount, ref NetValue value, INetObjectMap map)
    {
        switch (kind)
        {
            case RepLeafKind.Bool:
                value.I = r.ReadBit() ? 1 : 0;
                break;
            case RepLeafKind.Byte:
                value.I = r.ReadByte();
                break;
            case RepLeafKind.Enum:
                value.I = (int)r.ReadInt(EnumMax(enumCount));
                break;
            case RepLeafKind.Int:
                value.I = r.ReadInt32();
                break;
            case RepLeafKind.Float:
                value.F = r.ReadFloat();
                break;
            case RepLeafKind.Name:
            case RepLeafKind.String:
            case RepLeafKind.Asset:
            case RepLeafKind.Class:
                value.O = r.ReadString(MaxStringBytes);
                break;
            case RepLeafKind.Object:
                value.O = map.ReadObject(r);
                break;
            case RepLeafKind.Vector:
                r.ReadRoundedVector(out int x, out int y, out int z);
                value.V = new FVector(x, y, z);
                break;
            case RepLeafKind.Rotator:
                value.R = ReadRotatorBytes(r);
                break;
            case RepLeafKind.Plane:
                value.V = new FVector((short)r.ReadBits(16), (short)r.ReadBits(16), (short)r.ReadBits(16));
                value.F = (short)r.ReadBits(16);
                break;
            case RepLeafKind.CompressedPosition:
            {
                r.ReadRoundedVector(out int lx, out int ly, out int lz);
                int pitch = r.ReadBit() ? (int)r.ReadBits(8) << 8 : 0;
                int yaw = r.ReadBit() ? (int)r.ReadBits(8) << 8 : 0;
                r.ReadRoundedVector(out int vx, out int vy, out int vz);
                value.V = new FVector(lx, ly, lz);
                value.R = new FRotator(pitch, yaw, 0);
                value.V2 = new FVector(vx, vy, vz);
                break;
            }

            case RepLeafKind.VectorFine:
                value.V = r.ReadPackedVector();
                break;
            case RepLeafKind.RotatorFine:
                value.R = r.ReadCompressedRotator();
                break;
            default:
                r.SetError();
                break;
        }
    }

    /// <summary>Writes a function argument (the caller already handled the zero bit).</summary>
    public static bool WriteValue(FBitWriter w, RepLeafKind kind, int enumCount, in NetValue value, INetObjectMap map)
    {
        Span<int> words = stackalloc int[8];
        object?[]? none = null;
        switch (kind)
        {
            case RepLeafKind.Bool:
                w.WriteBit(value.I != 0);
                return true;
            case RepLeafKind.Byte:
                w.WriteByte((byte)value.I);
                return true;
            case RepLeafKind.Enum:
                w.WriteInt((uint)Math.Clamp(value.I, 0, (int)EnumMax(enumCount) - 1), EnumMax(enumCount));
                return true;
            case RepLeafKind.Int:
                w.WriteInt32(value.I);
                return true;
            case RepLeafKind.Float:
                w.WriteFloat(value.F);
                return true;
            case RepLeafKind.Name:
            case RepLeafKind.String:
            case RepLeafKind.Asset:
            case RepLeafKind.Class:
                WriteText(w, value.O as string);
                return true;
            case RepLeafKind.Object:
                map.WriteObject(w, value.O as UObject, out bool mapped);
                return mapped;
            case RepLeafKind.Vector:
                QuantizeVector(value.V, words);
                break;
            case RepLeafKind.Rotator:
                QuantizeRotator(value.R, words);
                break;
            case RepLeafKind.Plane:
                QuantizePlane(value.V, value.F, words);
                break;
            case RepLeafKind.CompressedPosition:
                QuantizeCompressedPosition(value.V, value.R, value.V2, words);
                break;
            case RepLeafKind.VectorFine:
                NetQuantize.QuantizeVector(value.V, NetQuantize.DefaultVectorScale, NetQuantize.DefaultVectorBits, out words[0], out words[1], out words[2]);
                break;
            case RepLeafKind.RotatorFine:
                words[0] = value.R.Pitch & 0xFFFF;
                words[1] = value.R.Yaw & 0xFFFF;
                words[2] = value.R.Roll & 0xFFFF;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
        }

        return WriteWords(w, kind, enumCount, words, none, map);
    }

    /// <summary>
    /// Writes the arguments of a call (spec/native/net-replication.md rule 19.7): parameters in declaration order,
    /// a bool as its bit, any other parameter as a bit saying whether it differs from zero followed by the value
    /// when it does (a struct member by member). Unmapped object references go as None.
    /// </summary>
    [UcNative("AActor::InternalProcessRemoteFunction", Exe = 0x10a29530, Editor = 0x1118f6f0)]
    public static void WriteArguments(FBitWriter w, RepFunction function, NetCallArgs? args, INetObjectMap map)
    {
        ArgumentNullException.ThrowIfNull(function);
        NetValue[]? values = args?.Values;
        foreach (RepParam p in function.Parameters)
        {
            if (p.Kind is RepLeafKind kind)
            {
                NetValue v = values is null ? default : values[p.ValueOffset];
                if (kind == RepLeafKind.Bool)
                {
                    w.WriteBit(v.I != 0);
                    continue;
                }

                bool nonZero = !v.IsZero(kind);
                w.WriteBit(nonZero);
                if (nonZero)
                {
                    WriteValue(w, kind, p.EnumCount, v, map);
                }

                continue;
            }

            bool any = false;
            for (int l = 0; l < p.Leaves.Length && values is not null; l++)
            {
                any |= !values[p.ValueOffset + l].IsZero(p.Leaves[l].Kind);
            }

            w.WriteBit(any);
            if (!any)
            {
                continue;
            }

            for (int l = 0; l < p.Leaves.Length; l++)
            {
                RepParamLeaf leaf = p.Leaves[l];
                WriteValue(w, leaf.Kind, leaf.EnumCount, values![p.ValueOffset + l], map);
            }
        }
    }

    /// <summary>Reads arguments written by <see cref="WriteArguments"/> into <paramref name="args"/> (cleared first).</summary>
    public static void ReadArguments(FBitReader r, RepFunction function, NetCallArgs args, INetObjectMap map)
    {
        ArgumentNullException.ThrowIfNull(function);
        ArgumentNullException.ThrowIfNull(args);
        NetValue[] values = args.Values;
        Array.Clear(values, 0, Math.Min(values.Length, function.ValueCount));
        foreach (RepParam p in function.Parameters)
        {
            if (p.Kind is RepLeafKind kind)
            {
                if (kind == RepLeafKind.Bool)
                {
                    values[p.ValueOffset].I = r.ReadBit() ? 1 : 0;
                }
                else if (r.ReadBit())
                {
                    Read(r, kind, p.EnumCount, ref values[p.ValueOffset], map);
                }

                continue;
            }

            if (!r.ReadBit())
            {
                continue;
            }

            for (int l = 0; l < p.Leaves.Length; l++)
            {
                RepParamLeaf leaf = p.Leaves[l];
                Read(r, leaf.Kind, leaf.EnumCount, ref values[p.ValueOffset + l], map);
            }
        }
    }

    private static void WriteText(FBitWriter w, string? text) =>
        w.WriteString(ControlMessage.Clip(text, MaxStringBytes), MaxStringBytes);

    private static void WriteRotatorBytes(FBitWriter w, int pitch, int yaw, int roll)
    {
        WriteByteAxis(w, pitch);
        WriteByteAxis(w, yaw);
        WriteByteAxis(w, roll);
    }

    private static void WriteByteAxis(FBitWriter w, int b)
    {
        w.WriteBit(b != 0);
        if (b != 0)
        {
            w.WriteBits((uint)b & 0xFF, 8);
        }
    }

    private static FRotator ReadRotatorBytes(FBitReader r)
    {
        int pitch = r.ReadBit() ? (int)r.ReadBits(8) << 8 : 0;
        int yaw = r.ReadBit() ? (int)r.ReadBits(8) << 8 : 0;
        int roll = r.ReadBit() ? (int)r.ReadBits(8) << 8 : 0;
        return new FRotator(pitch, yaw, roll);
    }

    /// <summary>The leaf kind of a C# value type used by a script property or parameter, or null for a struct to split.</summary>
    public static RepLeafKind? KindOf(Type type)
    {
        if (type == typeof(bool))
        {
            return RepLeafKind.Bool;
        }

        if (type == typeof(byte))
        {
            return RepLeafKind.Byte;
        }

        if (type.IsEnum)
        {
            return RepLeafKind.Enum;
        }

        if (type == typeof(int))
        {
            return RepLeafKind.Int;
        }

        if (type == typeof(float))
        {
            return RepLeafKind.Float;
        }

        if (type == typeof(Name))
        {
            return RepLeafKind.Name;
        }

        if (type == typeof(string))
        {
            return RepLeafKind.String;
        }

        if (type == typeof(AssetRef))
        {
            return RepLeafKind.Asset;
        }

        if (type == typeof(ClassRef))
        {
            return RepLeafKind.Class;
        }

        if (type == typeof(FVector))
        {
            return RepLeafKind.Vector;
        }

        if (type == typeof(FRotator))
        {
            return RepLeafKind.Rotator;
        }

        if (type == typeof(FPlane))
        {
            return RepLeafKind.Plane;
        }

        if (type == typeof(FCompressedPosition))
        {
            return RepLeafKind.CompressedPosition;
        }

        if (!type.IsValueType)
        {
            return RepLeafKind.Object;
        }

        return null;
    }

    /// <summary>Number of values of an enum type (its largest value plus one).</summary>
    public static int EnumCount(Type enumType)
    {
        int max = 0;
        foreach (object v in Enum.GetValues(enumType))
        {
            max = Math.Max(max, Convert.ToInt32(v, System.Globalization.CultureInfo.InvariantCulture));
        }

        return max + 1;
    }
}
