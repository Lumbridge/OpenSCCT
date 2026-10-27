using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;

namespace OpenScct.Net;

/// <summary>
/// One place in an object that a replicated value lives in: the C# member of a script property, an element of a
/// static array, and a chain of struct fields below it. Compiles typed getters and setters on first use, so the
/// replication pass reads and writes fields without reflection or boxing.
/// <para>
/// <c>Actor.Owner</c> and <c>Actor.Base</c> are set through <see cref="Actor.SetOwner"/> and
/// <see cref="Actor.SetBase"/>, as the engine's PostNetReceive applies them (spec/native/net-replication.md rule 24).
/// </para>
/// </summary>
public sealed class PropertyPath
{
    private const BindingFlags MemberFlags =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    private readonly FieldInfo[] _structFields;
    private Delegate? _getter;
    private Delegate? _setter;
    private bool _setterBuilt;

    private PropertyPath(UcPropertyInfo? property, MemberInfo root, int arrayIndex, FieldInfo[] structFields)
    {
        Property = property;
        Root = root;
        ArrayIndex = arrayIndex;
        _structFields = structFields;
        Type rootType = root is FieldInfo f ? f.FieldType : ((PropertyInfo)root).PropertyType;
        if (arrayIndex >= 0 && rootType.IsArray)
        {
            rootType = rootType.GetElementType()!;
        }

        Type leaf = rootType;
        foreach (FieldInfo field in structFields)
        {
            leaf = field.FieldType;
        }

        LeafType = leaf;
    }

    /// <summary>The script property, or null for a hand-written extension member.</summary>
    public UcPropertyInfo? Property { get; }

    public MemberInfo Root { get; }

    /// <summary>Static array element, or -1.</summary>
    public int ArrayIndex { get; }

    /// <summary>C# type of the value at the end of the path.</summary>
    public Type LeafType { get; }

    /// <summary>The C# member that holds <paramref name="property"/>, searched from its declaring class upwards.</summary>
    public static MemberInfo? FindMember(UcPropertyInfo property)
    {
        ArgumentNullException.ThrowIfNull(property);
        return FindMember(property.Class.Type, property.CSharpName);
    }

    /// <summary>A field or parameterless property named <paramref name="name"/> on <paramref name="type"/> or a base type.</summary>
    public static MemberInfo? FindMember(Type type, string name)
    {
        for (Type? t = type; t is not null && t != typeof(object); t = t.BaseType)
        {
            FieldInfo? field = t.GetField(name, MemberFlags);
            if (field is not null)
            {
                return field;
            }

            PropertyInfo? prop = t.GetProperty(name, MemberFlags);
            if (prop is not null && prop.GetIndexParameters().Length == 0)
            {
                return prop;
            }
        }

        return null;
    }

    /// <summary>The path of <paramref name="property"/> (element <paramref name="arrayIndex"/> of a static array), or null when it has no C# member.</summary>
    public static PropertyPath? For(UcPropertyInfo property, int arrayIndex = -1)
    {
        MemberInfo? member = FindMember(property);
        if (member is null)
        {
            return null;
        }

        Type type = member is FieldInfo f ? f.FieldType : ((PropertyInfo)member).PropertyType;
        return new PropertyPath(property, member, type.IsArray ? Math.Max(0, arrayIndex) : -1, []);
    }

    /// <summary>A path to a hand-written member of <paramref name="type"/>.</summary>
    public static PropertyPath ForMember(Type type, string name)
    {
        MemberInfo member = FindMember(type, name) ?? throw new ArgumentException($"{type.Name} has no member {name}.", nameof(name));
        return new PropertyPath(null, member, -1, []);
    }

    /// <summary>This path extended by one struct field.</summary>
    public PropertyPath Then(FieldInfo structField)
    {
        ArgumentNullException.ThrowIfNull(structField);
        var fields = new FieldInfo[_structFields.Length + 1];
        _structFields.CopyTo(fields, 0);
        fields[^1] = structField;
        return new PropertyPath(Property, Root, ArrayIndex, fields);
    }

    /// <summary>A getter returning the leaf converted to <typeparamref name="T"/> (enums convert to and from int).</summary>
    public Func<UObject, T> Getter<T>()
    {
        if (_getter is Func<UObject, T> cached)
        {
            return cached;
        }

        ParameterExpression self = Expression.Parameter(typeof(UObject), "self");
        Expression body = Convert(Leaf(self), typeof(T));
        var getter = Expression.Lambda<Func<UObject, T>>(body, self).Compile();
        _getter = getter;
        return getter;
    }

    /// <summary>A setter, or null when the leaf cannot be written (a property without a setter, a read-only field).</summary>
    public Action<UObject, T>? Setter<T>()
    {
        // Layouts (and so their paths) are shared by drivers on different threads. The setter is stored before the
        // built flag is published: a thread that saw the flag first used to get a null setter and never wrote the
        // property (a client whose own walker kept the simulated proxy role).
        if (Volatile.Read(ref _setterBuilt))
        {
            return _setter as Action<UObject, T>;
        }

        lock (_structFields)
        {
            if (!_setterBuilt)
            {
                _setter = BuildSetter<T>();
                Volatile.Write(ref _setterBuilt, true);
            }
        }

        return _setter as Action<UObject, T>;
    }

    private Action<UObject, T>? BuildSetter<T>()
    {
        if (_structFields.Length == 0 && ArrayIndex < 0 && Root.DeclaringType == typeof(Actor) && typeof(T) == typeof(UObject))
        {
            if (Root.Name == nameof(Actor.Owner))
            {
                return (Action<UObject, T>)(Delegate)new Action<UObject, UObject>(static (self, value) =>
                    ((Actor)self).SetOwner(value as Actor));
            }

            if (Root.Name == nameof(Actor.Base))
            {
                return (Action<UObject, T>)(Delegate)new Action<UObject, UObject>(static (self, value) =>
                    ((Actor)self).SetBase(value as Actor));
            }
        }

        if (Root is FieldInfo { IsInitOnly: true } && ArrayIndex < 0)
        {
            return null;
        }

        if (Root is PropertyInfo prop && prop.GetSetMethod(nonPublic: true) is null)
        {
            return null;
        }

        try
        {
            ParameterExpression self = Expression.Parameter(typeof(UObject), "self");
            ParameterExpression value = Expression.Parameter(typeof(T), "value");
            Expression leaf = Leaf(self);
            Expression assign = Expression.Assign(leaf, Convert(value, leaf.Type));
            return Expression.Lambda<Action<UObject, T>>(assign, self, value).Compile();
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private Expression Leaf(ParameterExpression self)
    {
        Expression instance = Expression.Convert(self, Root.DeclaringType!);
        Expression e = Root is FieldInfo field ? Expression.Field(instance, field) : Expression.Property(instance, (PropertyInfo)Root);
        if (ArrayIndex >= 0 && e.Type.IsArray)
        {
            e = Expression.ArrayAccess(e, Expression.Constant(ArrayIndex));
        }

        foreach (FieldInfo f in _structFields)
        {
            e = Expression.Field(e, f);
        }

        return e;
    }

    private static Expression Convert(Expression value, Type to)
    {
        Type from = value.Type;
        if (from == to)
        {
            return value;
        }

        if (from.IsEnum && to == typeof(int))
        {
            return Expression.Convert(Expression.Convert(value, Enum.GetUnderlyingType(from)), typeof(int));
        }

        if (to.IsEnum && from == typeof(int))
        {
            return Expression.Convert(Expression.Convert(value, Enum.GetUnderlyingType(to)), to);
        }

        if (!to.IsValueType && !from.IsValueType)
        {
            return Expression.TypeAs(value, to);
        }

        return Expression.Convert(value, to);
    }

    public override string ToString()
    {
        string text = Root.DeclaringType?.Name + "." + Root.Name + (ArrayIndex >= 0 ? $"[{ArrayIndex}]" : string.Empty);
        foreach (FieldInfo f in _structFields)
        {
            text += "." + f.Name;
        }

        return text;
    }
}

/// <summary>
/// Reads script properties by name for replication conditions and relevancy tests: the property of a class
/// (looked up through <see cref="UcClassInfo.FindProperty"/>, so subclasses inherit it) as a
/// <see cref="RepValue"/>. Readers are compiled once per property and cached; the cache is shared by every layout
/// of a registry and only grows.
/// </summary>
public sealed class PropertyReaders
{
    private readonly object _gate = new();
    private readonly Dictionary<UcPropertyInfo, Func<UObject, RepValue>?> _byProperty = new();
    private readonly Dictionary<(UcClassInfo, string), Func<UObject, RepValue>?> _byName = new();

    /// <summary>A reader for <paramref name="name"/> on objects of <paramref name="cls"/>, or null when there is no such property.</summary>
    public Func<UObject, RepValue>? Find(UcClassInfo cls, string name)
    {
        ArgumentNullException.ThrowIfNull(cls);
        lock (_gate)
        {
            if (_byName.TryGetValue((cls, name), out Func<UObject, RepValue>? reader))
            {
                return reader;
            }

            UcPropertyInfo? property = cls.FindProperty(name);
            reader = property is null ? HandWrittenReader(cls.Type, name) : ForLocked(property);
            _byName[(cls, name)] = reader;
            return reader;
        }
    }

    /// <summary>A reader for <paramref name="property"/> (its first element for static arrays).</summary>
    public Func<UObject, RepValue>? For(UcPropertyInfo property)
    {
        ArgumentNullException.ThrowIfNull(property);
        lock (_gate)
        {
            return ForLocked(property);
        }
    }

    private Func<UObject, RepValue>? ForLocked(UcPropertyInfo property)
    {
        if (_byProperty.TryGetValue(property, out Func<UObject, RepValue>? reader))
        {
            return reader;
        }

        reader = Build(PropertyPath.For(property, property.IsStaticArray ? 0 : -1));
        _byProperty[property] = reader;
        return reader;
    }

    /// <summary>Reads a field the runtime declares by hand without script metadata (none in the conditions so far).</summary>
    private static Func<UObject, RepValue>? HandWrittenReader(Type type, string name)
    {
        MemberInfo? member = PropertyPath.FindMember(type, name);
        return member is null ? null : Build(PropertyPath.ForMember(member.DeclaringType!, name));
    }

    private static Func<UObject, RepValue>? Build(PropertyPath? path)
    {
        if (path is null)
        {
            return null;
        }

        Type t = path.LeafType;
        if (t == typeof(bool))
        {
            Func<UObject, bool> g = path.Getter<bool>();
            return self => RepValue.Bool(g(self));
        }

        if (t == typeof(byte) || t == typeof(int) || t.IsEnum || t == typeof(sbyte) || t == typeof(short) || t == typeof(ushort))
        {
            Func<UObject, int> g = path.Getter<int>();
            return self => RepValue.Int(g(self));
        }

        if (t == typeof(float))
        {
            Func<UObject, float> g = path.Getter<float>();
            return self => RepValue.Float(g(self));
        }

        if (t == typeof(Name))
        {
            Func<UObject, Name> g = path.Getter<Name>();
            return self => RepValue.Int(g(self).Index);
        }

        if (t == typeof(string))
        {
            Func<UObject, string> g = path.Getter<string>();
            return self => RepValue.Text(g(self));
        }

        if (t == typeof(AssetRef))
        {
            Func<UObject, AssetRef> g = path.Getter<AssetRef>();
            return self => RepValue.Text(g(self).Path);
        }

        if (t == typeof(ClassRef))
        {
            Func<UObject, ClassRef> g = path.Getter<ClassRef>();
            return self => RepValue.Text(g(self).Path);
        }

        if (typeof(UObject).IsAssignableFrom(t))
        {
            Func<UObject, UObject> g = path.Getter<UObject>();
            return self => RepValue.Object(g(self));
        }

        return null;
    }
}
