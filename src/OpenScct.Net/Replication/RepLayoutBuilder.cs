using System;
using System.Collections.Generic;
using System.Reflection;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;

namespace OpenScct.Net;

/// <summary>
/// A hand-written replicated property of a class that is not an original one (test pawns and other runtime-only
/// actors): a member path, a leaf kind and the condition it is sent under. Sent after the generated members.
/// </summary>
public sealed record RepExtensionProperty(string Name, string Member, RepLeafKind Kind, RepCondition? Condition = null);

/// <summary>
/// A hand-written replicated function with its own wire format: the caller writes the arguments after
/// <see cref="ActorChannel.BeginFunction(RepFunction, bool)"/>, <see cref="Receive"/> reads and runs them.
/// Client-to-server extension functions are accepted only from the connection that owns the actor.
/// </summary>
public sealed record RepExtensionFunction(string Name, bool Reliable, bool ClientToServer, string Signature,
    Action<Actor, FBitReader, NetConnection> Receive);

/// <summary>Hand-written members appended to the generated layout of a runtime-only class.</summary>
public sealed class RepExtension
{
    public List<RepExtensionProperty> Properties { get; } = new();

    public List<RepExtensionFunction> Functions { get; } = new();

    /// <summary>Clients interpolate the class's Location, Rotation and Velocity (<see cref="ProxyMotion"/>).</summary>
    public bool InterpolateMovement { get; set; }
}

/// <summary>Where the server takes property conditions from.</summary>
public enum RepConditionSource : byte
{
    /// <summary>The decoded native property lists where they differ from the script, else the script (faithful).</summary>
    Native,

    /// <summary>The script replication blocks only.</summary>
    Script,
}

/// <summary>
/// Builds a <see cref="RepLayout"/> from the generated metadata of a class (<see cref="UcClassInfo.Replication"/>
/// of the class and every ancestor), binding each condition to the class.
/// </summary>
internal sealed class RepLayoutBuilder
{
    private readonly NetClassRegistry _registry;
    private readonly UcClassInfo? _class;
    private readonly Type _type;
    private readonly RepExtension? _extension;
    private readonly Dictionary<RepExpr, RepCondition> _bound = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, RepCondition> _boundText = new(StringComparer.Ordinal);
    private readonly List<RepProperty> _properties = new();
    private readonly List<RepFunction> _functions = new();
    private readonly List<RepCondition> _conditions = new();
    private readonly Dictionary<RepCondition, int> _conditionSlots = new(ReferenceEqualityComparer.Instance);
    private int _words;
    private int _objects;

    public RepLayoutBuilder(NetClassRegistry registry, UcClassInfo? cls, Type type, RepExtension? extension)
    {
        _registry = registry;
        _class = cls;
        _type = type;
        _extension = extension;
    }

    public RepLayout Build()
    {
        if (_class is not null)
        {
            var chain = new List<UcClassInfo>();
            for (UcClassInfo? c = _class; c is not null && c.IsActor; c = c.Super)
            {
                chain.Add(c);
            }

            chain.Reverse();
            foreach (UcClassInfo c in chain)
            {
                foreach (UcRepBlock block in c.Replication.Blocks)
                {
                    foreach (UcRepMember member in block.Members)
                    {
                        if (member.IsFunction)
                        {
                            AddFunction(c, block, member);
                        }
                        else
                        {
                            AddProperty(c, block, member);
                        }
                    }
                }
            }
        }

        if (_extension is not null)
        {
            foreach (RepExtensionProperty p in _extension.Properties)
            {
                AddExtensionProperty(p);
            }

            foreach (RepExtensionFunction f in _extension.Functions)
            {
                var func = new RepFunction(_functions.Count, f.Name, _type.Name, null,
                    UcFunctionFlags.FUNC_Net | (f.Reliable ? UcFunctionFlags.FUNC_NetReliable : 0),
                    f.ClientToServer ? Bind("Role < ROLE_Authority") : Bind("Role == ROLE_Authority"), [], 0)
                {
                    Extension = f,
                };
                _functions.Add(func);
            }
        }

        RepProperty? role = null;
        RepProperty? remote = null;
        foreach (RepProperty p in _properties)
        {
            p.WriteIndex = p.Index;
            if (p.Info is null)
            {
                continue;
            }

            if (string.Equals(p.Name, "Role", StringComparison.Ordinal))
            {
                role = p;
                p.NetSlot = ENetVarSlot.Role;
            }
            else if (string.Equals(p.Name, "RemoteRole", StringComparison.Ordinal))
            {
                remote = p;
                p.NetSlot = ENetVarSlot.RemoteRole;
            }
            else if (string.Equals(p.Name, "bNetOwner", StringComparison.Ordinal))
            {
                p.NetSlot = ENetVarSlot.bNetOwner;
            }
        }

        if (role is not null && remote is not null)
        {
            role.WriteIndex = remote.Index;
            remote.WriteIndex = role.Index;
        }

        string signature = RepLayout.SignatureOf(_properties, _functions);
        return new RepLayout(_class, _type, _properties.ToArray(), _functions.ToArray(), _conditions.ToArray(), _words, _objects,
            _extension?.InterpolateMovement ?? false, signature);
    }

    private void AddProperty(UcClassInfo declaring, UcRepBlock block, UcRepMember member)
    {
        UcPropertyInfo? info = declaring.FindOwnProperty(member.Name) ?? declaring.FindProperty(member.Name);
        if (info is null || info.Kind is UcPropertyKind.Array or UcPropertyKind.Delegate)
        {
            return;
        }

        RepCondition condition;
        string source;
        string? native = _registry.ConditionSource == RepConditionSource.Native && _class is not null
            ? NativeRepLists.Resolve(_class, info.Name)
            : null;
        if (native is not null)
        {
            condition = Bind(native);
            source = "native";
        }
        else
        {
            condition = Bind(block.Condition);
            source = "script";
        }

        int count = Math.Max(1, info.ArrayDim);
        for (int element = 0; element < count; element++)
        {
            PropertyPath? path = PropertyPath.For(info, info.IsStaticArray ? element : -1);
            if (path is null)
            {
                return;
            }

            var leaves = new List<RepLeaf>();
            int words = 0;
            int objects = 0;
            if (!AddLeaves(path, leaves, ref words, ref objects, 0))
            {
                return;
            }

            string name = info.IsStaticArray ? info.Name + "[" + element.ToString(System.Globalization.CultureInfo.InvariantCulture) + "]" : info.Name;
            var prop = new RepProperty(_properties.Count, name, info, info.IsStaticArray ? element : -1, leaves.ToArray(), _words, words,
                _objects, objects, condition, source, declaring.Path)
            {
                ConditionSlot = Slot(condition),
            };
            _words += words;
            _objects += objects;
            _properties.Add(prop);
        }
    }

    private void AddExtensionProperty(RepExtensionProperty p)
    {
        PropertyPath path = PropertyPath.ForMember(_type, p.Member);
        var leaf = new RepLeaf(p.Kind, p.Kind == RepLeafKind.Enum ? NetWire.EnumCount(path.LeafType) : 0, path, 0, 0);
        RepCondition condition = p.Condition ?? RepCondition.Always;
        var prop = new RepProperty(_properties.Count, p.Name, null, -1, [leaf], _words, leaf.Words, _objects, leaf.Objects, condition,
            "extension", _type.Name)
        {
            ConditionSlot = Slot(condition),
        };
        _words += leaf.Words;
        _objects += leaf.Objects;
        _properties.Add(prop);
    }

    private static bool AddLeaves(PropertyPath path, List<RepLeaf> leaves, ref int words, ref int objects, int depth)
    {
        Type t = path.LeafType;
        RepLeafKind? kind = NetWire.KindOf(t);
        if (kind is RepLeafKind k)
        {
            var leaf = new RepLeaf(k, k == RepLeafKind.Enum ? NetWire.EnumCount(t) : 0, path, words, objects);
            leaves.Add(leaf);
            words += leaf.Words;
            objects += leaf.Objects;
            return true;
        }

        if (!t.IsValueType || depth > 4)
        {
            return false;
        }

        foreach (FieldInfo field in StructFields(t))
        {
            if (!AddLeaves(path.Then(field), leaves, ref words, ref objects, depth + 1))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The instance fields of a struct in declaration order (member by member, as UE2 sends structs).</summary>
    internal static FieldInfo[] StructFields(Type t)
    {
        FieldInfo[] fields = t.GetFields(BindingFlags.Instance | BindingFlags.Public);
        Array.Sort(fields, static (a, b) => a.MetadataToken.CompareTo(b.MetadataToken));
        return fields;
    }

    private void AddFunction(UcClassInfo declaring, UcRepBlock block, UcRepMember member)
    {
        foreach (RepFunction existing in _functions)
        {
            if (string.Equals(existing.Name, member.Name, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        UcFunctionInfo? info = declaring.FindOwnFunction(member.Name) ?? declaring.FindFunction(member.Name);
        UcFunctionFlags flags = member.FunctionFlags != UcFunctionFlags.None ? member.FunctionFlags : info?.Flags ?? UcFunctionFlags.FUNC_Net;
        var parameters = new List<RepParam>();
        int values = 0;
        if (info is not null)
        {
            foreach (UcParamInfo p in info.Parameters)
            {
                if ((p.Flags & UcPropertyFlags.CPF_ReturnParm) != 0 || (p.Flags & UcPropertyFlags.CPF_Parm) == 0)
                {
                    continue;
                }

                RepParam param = _registry.Types.Param(p.Name, p.Type, declaring, values);
                parameters.Add(param);
                values += param.ValueCount;
            }
        }

        _functions.Add(new RepFunction(_functions.Count, member.Name, declaring.Path, info, flags, Bind(block.Condition),
            parameters.ToArray(), values));
    }

    private RepCondition Bind(RepExpr expr)
    {
        if (!_bound.TryGetValue(expr, out RepCondition? c))
        {
            c = RepCondition.Bind(expr, _class ?? _registry.ActorClass, _registry.Readers);
            _bound[expr] = c;
        }

        return c;
    }

    private RepCondition Bind(string text)
    {
        if (!_boundText.TryGetValue(text, out RepCondition? c))
        {
            c = text == "false" ? RepCondition.Never : RepCondition.Bind(RepConditionParser.Parse(text), _class ?? _registry.ActorClass, _registry.Readers);
            _boundText[text] = c;
        }

        return c;
    }

    private int Slot(RepCondition condition)
    {
        if (!_conditionSlots.TryGetValue(condition, out int slot))
        {
            slot = _conditions.Count;
            _conditions.Add(condition);
            _conditionSlots[condition] = slot;
        }

        return slot;
    }
}

/// <summary>
/// Resolves the UnrealScript type text of function parameters to C# types and leaf kinds: the built-in types,
/// Vector, Rotator, Plane and CompressedPosition, generated enums and structs (sent member by member),
/// <c>class&lt;T&gt;</c> and object classes.
/// </summary>
public sealed class NetTypeMap
{
    private readonly Dictionary<string, List<Type>> _byName = new(StringComparer.OrdinalIgnoreCase);
    private readonly UcClassIndex _index;

    public NetTypeMap(UcClassIndex index)
    {
        _index = index ?? throw new ArgumentNullException(nameof(index));
        var assemblies = new List<Assembly> { typeof(Actor).Assembly };
        foreach (UcClassInfo c in index.Classes)
        {
            if (!assemblies.Contains(c.Type.Assembly))
            {
                assemblies.Add(c.Type.Assembly);
            }
        }

        foreach (Assembly assembly in assemblies)
        {
            foreach (Type t in assembly.GetTypes())
            {
                if (!t.IsPublic || !(t.IsEnum || t.IsValueType))
                {
                    continue;
                }

                if (!_byName.TryGetValue(t.Name, out List<Type>? list))
                {
                    list = new List<Type>(1);
                    _byName.Add(t.Name, list);
                }

                list.Add(t);
            }
        }
    }

    /// <summary>The C# enum or struct named <paramref name="name"/>, preferring the namespace of <paramref name="near"/>.</summary>
    public Type? FindValueType(string name, Type? near)
    {
        if (!_byName.TryGetValue(name, out List<Type>? list))
        {
            return null;
        }

        if (near is not null)
        {
            foreach (Type t in list)
            {
                if (t.Namespace == near.Namespace)
                {
                    return t;
                }
            }
        }

        list.Sort(static (a, b) => string.CompareOrdinal(a.FullName, b.FullName));
        return list[0];
    }

    internal RepParam Param(string name, string type, UcClassInfo declaring, int valueOffset)
    {
        string t = type.Trim();
        switch (t.ToLowerInvariant())
        {
            case "bool":
                return Simple(name, type, RepLeafKind.Bool, typeof(bool), valueOffset);
            case "byte":
                return Simple(name, type, RepLeafKind.Byte, typeof(byte), valueOffset);
            case "int":
                return Simple(name, type, RepLeafKind.Int, typeof(int), valueOffset);
            case "float":
                return Simple(name, type, RepLeafKind.Float, typeof(float), valueOffset);
            case "name":
                return Simple(name, type, RepLeafKind.Name, typeof(Name), valueOffset);
            case "string":
                return Simple(name, type, RepLeafKind.String, typeof(string), valueOffset);
            case "vector":
                return Simple(name, type, RepLeafKind.Vector, typeof(FVector), valueOffset);
            case "rotator":
                return Simple(name, type, RepLeafKind.Rotator, typeof(FRotator), valueOffset);
            case "plane":
                return Simple(name, type, RepLeafKind.Plane, typeof(FPlane), valueOffset);
            case "compressedposition":
                return Simple(name, type, RepLeafKind.CompressedPosition, typeof(FCompressedPosition), valueOffset);
        }

        if (t.StartsWith("class<", StringComparison.OrdinalIgnoreCase) || string.Equals(t, "class", StringComparison.OrdinalIgnoreCase))
        {
            return Simple(name, type, RepLeafKind.Class, typeof(ClassRef), valueOffset);
        }

        Type? valueType = FindValueType(t, declaring.Type) ?? FindValueType("F" + t, declaring.Type);
        if (valueType is { IsEnum: true })
        {
            return new RepParam(name, type, RepLeafKind.Enum, NetWire.EnumCount(valueType), null, [], valueOffset, valueType);
        }

        if (valueType is not null && NetWire.KindOf(valueType) is RepLeafKind kind)
        {
            return Simple(name, type, kind, valueType, valueOffset);
        }

        if (valueType is not null)
        {
            var leaves = new List<RepParamLeaf>();
            Flatten(valueType, [], name, leaves, 0);
            return new RepParam(name, type, null, 0, valueType, leaves.ToArray(), valueOffset, valueType);
        }

        UcClassInfo? cls = _index.Find(t);
        return new RepParam(name, type, IsContent(t) ? RepLeafKind.Asset : RepLeafKind.Object, 0, null, [], valueOffset,
            IsContent(t) ? typeof(AssetRef) : cls?.Type);
    }

    private static bool IsContent(string type) => type is "Material" or "Texture" or "Sound" or "StaticMesh" or "Mesh" or "Shader"
        or "MeshAnimation" or "Font" or "Palette";

    private static RepParam Simple(string name, string type, RepLeafKind kind, Type clr, int offset) =>
        new(name, type, kind, 0, null, [], offset, clr);

    private static void Flatten(Type structType, FieldInfo[] prefix, string name, List<RepParamLeaf> leaves, int depth)
    {
        foreach (FieldInfo field in RepLayoutBuilder.StructFields(structType))
        {
            var chain = new FieldInfo[prefix.Length + 1];
            prefix.CopyTo(chain, 0);
            chain[^1] = field;
            if (NetWire.KindOf(field.FieldType) is RepLeafKind kind)
            {
                leaves.Add(new RepParamLeaf(name + "." + field.Name, kind, kind == RepLeafKind.Enum ? NetWire.EnumCount(field.FieldType) : 0, chain));
            }
            else if (field.FieldType.IsValueType && depth < 4)
            {
                Flatten(field.FieldType, chain, name + "." + field.Name, leaves, depth + 1);
            }
        }
    }
}
