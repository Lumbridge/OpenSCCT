using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Threading;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;

namespace OpenScct.Net;

/// <summary>
/// One scalar value of a replicated property: the whole property for simple types, or one field of a struct that
/// is sent member by member. Reads and writes the object through a compiled <see cref="PropertyPath"/>.
/// </summary>
public sealed class RepLeaf
{
    private Delegate? _get;
    private Delegate? _set;
    private bool _setBuilt;

    internal RepLeaf(RepLeafKind kind, int enumCount, PropertyPath path, int wordOffset, int objectOffset)
    {
        Kind = kind;
        EnumCount = enumCount;
        Path = path;
        WordOffset = wordOffset;
        ObjectOffset = objectOffset;
    }

    public RepLeafKind Kind { get; }

    /// <summary>Value count of an enum leaf.</summary>
    public int EnumCount { get; }

    public PropertyPath Path { get; }

    /// <summary>First shadow word, relative to the property.</summary>
    public int WordOffset { get; }

    /// <summary>First shadow object slot, relative to the property.</summary>
    public int ObjectOffset { get; }

    public int Words => NetWire.WordCount(Kind);

    public int Objects => NetWire.ObjectCount(Kind);

    /// <summary>Captures the leaf of <paramref name="self"/> in quantised form.</summary>
    internal void Capture(UObject self, Span<int> words, Span<object?> objects)
    {
        switch (Kind)
        {
            case RepLeafKind.Bool:
                words[0] = Get<bool>()(self) ? 1 : 0;
                break;
            case RepLeafKind.Byte:
            case RepLeafKind.Enum:
            case RepLeafKind.Int:
                words[0] = Get<int>()(self);
                break;
            case RepLeafKind.Float:
                words[0] = BitConverter.SingleToInt32Bits(Get<float>()(self));
                break;
            case RepLeafKind.Name:
            {
                Name n = Get<Name>()(self);
                words[0] = n.Index;
                objects[0] = n.IsNone ? null : n.ToString();
                break;
            }

            case RepLeafKind.String:
                objects[0] = Get<string>()(self) ?? string.Empty;
                break;
            case RepLeafKind.Asset:
                objects[0] = Get<AssetRef>()(self).Path;
                break;
            case RepLeafKind.Class:
                objects[0] = Get<ClassRef>()(self).Path;
                break;
            case RepLeafKind.Object:
            {
                UObject? o = Get<UObject>()(self);
                if (o is Actor { bDeleteMe: false } a)
                {
                    words[0] = a.ActorId;
                    objects[0] = a;
                }
                else
                {
                    words[0] = 0;
                    objects[0] = null;
                }

                break;
            }

            case RepLeafKind.Vector:
                NetWire.QuantizeVector(Get<FVector>()(self), words);
                break;
            case RepLeafKind.Rotator:
                NetWire.QuantizeRotator(Get<FRotator>()(self), words);
                break;
            case RepLeafKind.Plane:
            {
                FPlane p = Get<FPlane>()(self);
                NetWire.QuantizePlane(new FVector(p.X, p.Y, p.Z), p.W, words);
                break;
            }

            case RepLeafKind.CompressedPosition:
            {
                FCompressedPosition c = Get<FCompressedPosition>()(self);
                NetWire.QuantizeCompressedPosition(c.Location, c.Rotation, c.Velocity, words);
                break;
            }

            case RepLeafKind.VectorFine:
                NetQuantize.QuantizeVector(Get<FVector>()(self), NetQuantize.DefaultVectorScale, NetQuantize.DefaultVectorBits,
                    out words[0], out words[1], out words[2]);
                break;
            case RepLeafKind.RotatorFine:
            {
                FRotator r = Get<FRotator>()(self);
                words[0] = r.Pitch & 0xFFFF;
                words[1] = r.Yaw & 0xFFFF;
                words[2] = r.Roll & 0xFFFF;
                break;
            }
        }
    }

    /// <summary>True when two captures of this leaf are the same value.</summary>
    internal bool Same(ReadOnlySpan<int> a, ReadOnlySpan<int> b, ReadOnlySpan<object?> oa, ReadOnlySpan<object?> ob)
    {
        if (Objects > 0 && (ReferenceEquals(oa[0], RepLayout.ConfigMarker) || ReferenceEquals(ob[0], RepLayout.ConfigMarker)))
        {
            return false;
        }

        switch (Kind)
        {
            case RepLeafKind.String:
                return string.Equals(oa[0] as string ?? string.Empty, ob[0] as string ?? string.Empty, StringComparison.Ordinal);
            case RepLeafKind.Asset:
            case RepLeafKind.Class:
                return string.Equals(oa[0] as string, ob[0] as string, StringComparison.OrdinalIgnoreCase);
            default:
                return a[..Words].SequenceEqual(b[..Words]);
        }
    }

    /// <summary>Stores a received value on <paramref name="target"/>.</summary>
    internal void Apply(UObject target, in NetValue v)
    {
        switch (Kind)
        {
            case RepLeafKind.Bool:
                Set<bool>()?.Invoke(target, v.I != 0);
                break;
            case RepLeafKind.Byte:
            case RepLeafKind.Enum:
            case RepLeafKind.Int:
                Set<int>()?.Invoke(target, v.I);
                break;
            case RepLeafKind.Float:
                Set<float>()?.Invoke(target, v.F);
                break;
            case RepLeafKind.Name:
                Set<Name>()?.Invoke(target, v.AsName);
                break;
            case RepLeafKind.String:
                Set<string>()?.Invoke(target, v.AsString);
                break;
            case RepLeafKind.Asset:
                Set<AssetRef>()?.Invoke(target, new AssetRef(v.O as string));
                break;
            case RepLeafKind.Class:
                Set<ClassRef>()?.Invoke(target, new ClassRef(v.O as string));
                break;
            case RepLeafKind.Object:
                Set<UObject>()?.Invoke(target, (v.O as UObject)!);
                break;
            case RepLeafKind.Vector:
            case RepLeafKind.VectorFine:
                Set<FVector>()?.Invoke(target, v.V);
                break;
            case RepLeafKind.Rotator:
            case RepLeafKind.RotatorFine:
                Set<FRotator>()?.Invoke(target, v.R);
                break;
            case RepLeafKind.Plane:
                Set<FPlane>()?.Invoke(target, new FPlane { X = v.V.X, Y = v.V.Y, Z = v.V.Z, W = v.F });
                break;
            case RepLeafKind.CompressedPosition:
                Set<FCompressedPosition>()?.Invoke(target, new FCompressedPosition { Location = v.V, Rotation = v.R, Velocity = v.V2 });
                break;
        }
    }

    /// <summary>The current value as a function argument (for tests and tools).</summary>
    public NetValue Read(UObject self)
    {
        Span<int> words = stackalloc int[8];
        var objects = new object?[1];
        Capture(self, words, objects);
        var v = new NetValue { I = words[0], O = objects[0] };
        switch (Kind)
        {
            case RepLeafKind.Float:
                v.F = BitConverter.Int32BitsToSingle(words[0]);
                break;
            case RepLeafKind.Vector:
                v.V = new FVector(words[0], words[1], words[2]);
                break;
            case RepLeafKind.Rotator:
                v.R = new FRotator(words[0] << 8, words[1] << 8, words[2] << 8);
                break;
        }

        return v;
    }

    private Func<UObject, T> Get<T>()
    {
        if (_get is Func<UObject, T> g)
        {
            return g;
        }

        g = Path.Getter<T>();
        _get = g;
        return g;
    }

    private Action<UObject, T>? Set<T>()
    {
        // Shared across threads: store the setter before publishing the flag (see PropertyPath.Setter).
        if (!Volatile.Read(ref _setBuilt))
        {
            _set = Path.Setter<T>();
            Volatile.Write(ref _setBuilt, true);
        }

        return _set as Action<UObject, T>;
    }
}

/// <summary>
/// A replicated property element of a <see cref="RepLayout"/> (static arrays get one per element, sent with the
/// element index as UE2 does): its rep index, condition, leaves and place in the shadow block.
/// </summary>
public sealed class RepProperty
{
    internal RepProperty(int index, string name, UcPropertyInfo? info, int arrayIndex, RepLeaf[] leaves, int wordOffset,
        int words, int objectOffset, int objects, RepCondition condition, string conditionSource, string declaringClass)
    {
        Index = index;
        Name = name;
        Info = info;
        ArrayIndex = arrayIndex;
        Leaves = leaves;
        WordOffset = wordOffset;
        Words = words;
        ObjectOffset = objectOffset;
        Objects = objects;
        Condition = condition;
        ConditionSource = conditionSource;
        DeclaringClass = declaringClass;
        IsConfig = info is not null && (info.Flags & (UcPropertyFlags.CPF_Config | UcPropertyFlags.CPF_GlobalConfig)) != 0;
    }

    /// <summary>Rep index (properties first, then functions).</summary>
    public int Index { get; }

    /// <summary>Property name, with <c>[i]</c> for a static array element.</summary>
    public string Name { get; }

    /// <summary>The script property, or null for a hand-written extension member.</summary>
    public UcPropertyInfo? Info { get; }

    public int ArrayIndex { get; }

    public RepLeaf[] Leaves { get; }

    public int WordOffset { get; }

    public int Words { get; }

    public int ObjectOffset { get; }

    public int Objects { get; }

    /// <summary>The condition the server evaluates (script, native list or extension).</summary>
    public RepCondition Condition { get; }

    /// <summary>"script", "native" or "extension".</summary>
    public string ConditionSource { get; }

    /// <summary>Path of the class whose replication block (or extension) declares the property.</summary>
    public string DeclaringClass { get; }

    /// <summary>A config property: zeroed in the initial shadow so it always goes in the first bunch (rule 16).</summary>
    public bool IsConfig { get; }

    /// <summary>Slot of the distinct condition in <see cref="RepLayout.Conditions"/> (evaluated once per update).</summary>
    public int ConditionSlot { get; internal set; }

    /// <summary>Rep index the value is written under: Role and RemoteRole exchange slots (spec/native/net-replication.md rule 14).</summary>
    public int WriteIndex { get; internal set; }

    /// <summary>The property holds Role, RemoteRole or bNetOwner, whose sent value is per connection.</summary>
    internal ENetVarSlot NetSlot { get; set; }

    public override string ToString() => DeclaringClass + "." + Name;
}

internal enum ENetVarSlot : byte
{
    None,
    Role,
    RemoteRole,
    bNetOwner,
}

/// <summary>One parameter of a replicated function: its type, and the leaves it takes in <see cref="NetCallArgs"/>.</summary>
public sealed class RepParam
{
    internal RepParam(string name, string type, RepLeafKind? kind, int enumCount, Type? structType, RepParamLeaf[] leaves,
        int valueOffset, Type? clrType)
    {
        Name = name;
        Type = type;
        Kind = kind;
        EnumCount = enumCount;
        StructType = structType;
        Leaves = leaves;
        ValueOffset = valueOffset;
        ClrType = clrType;
    }

    public string Name { get; }

    /// <summary>The UnrealScript type text.</summary>
    public string Type { get; }

    /// <summary>The leaf kind of a simple parameter, or null for a struct sent member by member.</summary>
    public RepLeafKind? Kind { get; }

    public int EnumCount { get; }

    /// <summary>The C# struct of a member-by-member parameter.</summary>
    public Type? StructType { get; }

    /// <summary>The members of a struct parameter, in order (empty for a simple one).</summary>
    public RepParamLeaf[] Leaves { get; }

    /// <summary>First slot in <see cref="NetCallArgs.Values"/>.</summary>
    public int ValueOffset { get; }

    /// <summary>Slots used in <see cref="NetCallArgs.Values"/>.</summary>
    public int ValueCount => Kind is null ? Leaves.Length : 1;

    /// <summary>The C# type the receiving method takes (enum, actor class...), when known.</summary>
    public Type? ClrType { get; }

    public bool IsBool => Kind == RepLeafKind.Bool;
}

/// <summary>One member of a struct parameter: its field chain from the struct and its leaf kind.</summary>
public sealed record RepParamLeaf(string Name, RepLeafKind Kind, int EnumCount, FieldInfo[] Fields);

/// <summary>
/// A replicated function of a <see cref="RepLayout"/>: its rep index, the condition of its first declaration, its
/// flags (FUNC_Net, FUNC_NetReliable, FUNC_Simulated, the SCCT FUNC_NetAllClients) and its parameters.
/// </summary>
public sealed class RepFunction
{
    internal RepFunction(int index, string name, string declaringClass, UcFunctionInfo? info, UcFunctionFlags flags,
        RepCondition condition, RepParam[] parameters, int valueCount)
    {
        Index = index;
        Name = name;
        DeclaringClass = declaringClass;
        Info = info;
        Flags = flags;
        Condition = condition;
        Parameters = parameters;
        ValueCount = valueCount;
    }

    /// <summary>Index among the layout's functions (the rep index is <c>PropertyCount + Index</c>).</summary>
    public int Index { get; }

    public string Name { get; }

    /// <summary>Path of the class whose replication block lists the function (its first declaration).</summary>
    public string DeclaringClass { get; }

    public UcFunctionInfo? Info { get; }

    public UcFunctionFlags Flags { get; }

    public RepCondition Condition { get; }

    public RepParam[] Parameters { get; }

    /// <summary>Slots a call takes in <see cref="NetCallArgs.Values"/>.</summary>
    public int ValueCount { get; }

    public bool IsNet => (Flags & UcFunctionFlags.FUNC_Net) != 0;

    public bool IsReliable => (Flags & UcFunctionFlags.FUNC_NetReliable) != 0;

    public bool IsSimulated => (Flags & UcFunctionFlags.FUNC_Simulated) != 0;

    public bool IsNative => (Flags & UcFunctionFlags.FUNC_Native) != 0;

    public bool IsStatic => (Flags & UcFunctionFlags.FUNC_Static) != 0;

    /// <summary>The SCCT broadcast flag 0x01000000 (spec/native/net-replication.md rule 20).</summary>
    public bool IsNetAllClients => (Flags & UcFunctionFlags.FUNC_NetAllClients) != 0;

    /// <summary>Set for hand-written extension functions, which carry their own wire format.</summary>
    public RepExtensionFunction? Extension { get; internal set; }

    public override string ToString() => DeclaringClass + "." + Name;
}

/// <summary>
/// How one replicated class replicates (UE2 FClassNetCache plus the native property list): every replicated
/// property element and function of the class and its ancestors, root class first and each class's blocks in source
/// order, with the conditions bound to the class, the shadow block layout and the initial shadow (the class
/// defaults with config properties zeroed, spec/native/net-replication.md rule 16).
/// </summary>
public sealed class RepLayout
{
    private readonly Dictionary<string, RepFunction> _functionsByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RepProperty> _propertiesByName = new(StringComparer.OrdinalIgnoreCase);
    private int[]? _defaultWords;
    private object?[]? _defaultObjects;

    internal RepLayout(UcClassInfo? classInfo, Type type, RepProperty[] properties, RepFunction[] functions,
        RepCondition[] conditions, int wordCount, int objectCount, bool interpolateMovement, string signature)
    {
        ClassInfo = classInfo;
        Type = type;
        Properties = properties;
        Functions = functions;
        Conditions = conditions;
        WordCount = wordCount;
        ObjectCount = objectCount;
        InterpolateMovement = interpolateMovement;
        Signature = signature;
        foreach (RepFunction f in functions)
        {
            _functionsByName.TryAdd(f.Name, f);
        }

        foreach (RepProperty p in properties)
        {
            _propertiesByName.TryAdd(p.Name, p);
        }

        RoleIndex = _propertiesByName.TryGetValue("Role", out RepProperty? role) ? role.Index : -1;
        RemoteRoleIndex = _propertiesByName.TryGetValue("RemoteRole", out RepProperty? remote) ? remote.Index : -1;
    }

    /// <summary>The generated class (for an extension class, its nearest generated ancestor).</summary>
    public UcClassInfo? ClassInfo { get; }

    /// <summary>The C# type replicated with this layout.</summary>
    public Type Type { get; }

    public RepProperty[] Properties { get; }

    public RepFunction[] Functions { get; }

    /// <summary>The distinct conditions of the properties, each evaluated at most once per update.</summary>
    public RepCondition[] Conditions { get; }

    public int PropertyCount => Properties.Length;

    public int FunctionCount => Functions.Length;

    /// <summary>Size of the rep index space (properties plus functions).</summary>
    public int MaxRepIndex => Math.Max(1, Properties.Length + Functions.Length);

    public int WordCount { get; }

    public int ObjectCount { get; }

    /// <summary>Clients interpolate Location, Rotation and Velocity of this class (<see cref="ProxyMotion"/>).</summary>
    public bool InterpolateMovement { get; }

    /// <summary>Stable text of the wire format, part of the protocol hash.</summary>
    public string Signature { get; }

    public int RoleIndex { get; }

    public int RemoteRoleIndex { get; }

    public RepFunction? FindFunction(string name) => _functionsByName.GetValueOrDefault(name);

    public RepProperty? FindProperty(string name) => _propertiesByName.GetValueOrDefault(name);

    /// <summary>Captures every replicated property of <paramref name="self"/> in quantised form.</summary>
    public void Capture(UObject self, Span<int> words, Span<object?> objects)
    {
        RepProperty[] props = Properties;
        for (int i = 0; i < props.Length; i++)
        {
            CaptureProperty(props[i], self, words, objects);
        }
    }

    internal static void CaptureProperty(RepProperty p, UObject self, Span<int> words, Span<object?> objects)
    {
        RepLeaf[] leaves = p.Leaves;
        for (int l = 0; l < leaves.Length; l++)
        {
            RepLeaf leaf = leaves[l];
            leaf.Capture(self, words[(p.WordOffset + leaf.WordOffset)..], objects[(p.ObjectOffset + leaf.ObjectOffset)..]);
        }
    }

    /// <summary>True when property <paramref name="p"/> has the same value in two shadow blocks.</summary>
    internal static bool Same(RepProperty p, ReadOnlySpan<int> a, ReadOnlySpan<int> b, ReadOnlySpan<object?> oa, ReadOnlySpan<object?> ob)
    {
        RepLeaf[] leaves = p.Leaves;
        for (int l = 0; l < leaves.Length; l++)
        {
            RepLeaf leaf = leaves[l];
            int w = p.WordOffset + leaf.WordOffset;
            int o = p.ObjectOffset + leaf.ObjectOffset;
            if (!leaf.Same(a[w..], b[w..], oa[o..], ob[o..]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The initial shadow of a channel: the class defaults (a fresh object of the class, as the class default
    /// object), with config properties zeroed so they always go in the first bunch.
    /// </summary>
    public void CopyDefaults(Span<int> words, Span<object?> objects)
    {
        if (_defaultWords is null)
        {
            var w = new int[WordCount];
            var o = new object?[ObjectCount];
            UObject? cdo = ClassInfo?.Type == Type ? ClassInfo.Create() : CreateDefault();
            if (cdo is not null)
            {
                Capture(cdo, w, o);
            }

            foreach (RepProperty p in Properties)
            {
                if (p.IsConfig)
                {
                    w.AsSpan(p.WordOffset, p.Words).Fill(int.MinValue);
                    o.AsSpan(p.ObjectOffset, p.Objects).Fill(ConfigMarker);
                }
            }

            _defaultObjects = o;
            _defaultWords = w;
        }

        _defaultWords.CopyTo(words);
        _defaultObjects!.CopyTo(objects);
    }

    /// <summary>Placeholder in the initial shadow of config properties: equal to no captured value.</summary>
    internal static readonly object ConfigMarker = new();

    private UObject? CreateDefault()
    {
        ConstructorInfo? ctor = Type.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, Type.EmptyTypes);
        return ctor?.Invoke(null) as UObject;
    }

    public override string ToString() => (ClassInfo?.Path ?? Type.Name) + " (" + Properties.Length.ToString(CultureInfo.InvariantCulture) +
        " properties, " + Functions.Length.ToString(CultureInfo.InvariantCulture) + " functions)";

    internal static string SignatureOf(IEnumerable<RepProperty> properties, IEnumerable<RepFunction> functions)
    {
        var sb = new StringBuilder();
        foreach (RepProperty p in properties)
        {
            sb.Append(p.Name).Append(':');
            foreach (RepLeaf leaf in p.Leaves)
            {
                sb.Append(leaf.Kind.ToString());
                if (leaf.Kind == RepLeafKind.Enum)
                {
                    sb.Append(leaf.EnumCount.ToString(CultureInfo.InvariantCulture));
                }

                sb.Append(',');
            }

            sb.Append(';');
        }

        foreach (RepFunction f in functions)
        {
            sb.Append(f.Name).Append('(');
            if (f.Extension is { } ext)
            {
                sb.Append(ext.Signature);
                if (ext.AcceptReordered) sb.Append(":reordered");
            }

            foreach (RepParam p in f.Parameters)
            {
                sb.Append(p.Kind?.ToString() ?? p.StructType?.Name ?? "?").Append(',');
            }

            sb.Append(((uint)f.Flags).ToString("X", CultureInfo.InvariantCulture)).Append(");");
        }

        return sb.ToString();
    }
}
