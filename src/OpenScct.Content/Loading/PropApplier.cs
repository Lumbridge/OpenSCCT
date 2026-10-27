using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;

namespace OpenScct.Content.Loading;

/// <summary>Why a map value was not (or not fully) applied to an actor.</summary>
public enum UnappliedReason : byte
{
    /// <summary>The C# class has no member of that UnrealScript name (the class is not ported or generated).</summary>
    NoMember,

    /// <summary>The value does not fit the member's type.</summary>
    TypeMismatch,

    /// <summary>
    /// A reference to an actor that only the client draws (<c>{"$asset": "Map.Name"}</c>): the simulation has no such
    /// actor, so the member keeps None (array elements become None; the rest of the value is applied).
    /// </summary>
    RenderOnlyActor,

    /// <summary>A reference to a non-actor object the simulation does not create (the member keeps None).</summary>
    ObjectReference,

    /// <summary>An actor reference whose target is missing or failed to spawn (it loads as None).</summary>
    MissingActor,

    /// <summary>An actor reference whose target does not fit the member's class.</summary>
    WrongActorClass,

    /// <summary>
    /// An actor reference to an <see cref="Actors.UnportedActor"/> marker (its class has no C# class), which does not
    /// fit the member's class (the member keeps None).
    /// </summary>
    UnportedTarget,

    /// <summary>
    /// The actor is an <see cref="Actors.UnportedActor"/> marker and the value does not fit the Actor member of that
    /// name (its original class redeclares the property with another type).
    /// </summary>
    UnportedMismatch,
}

/// <summary>
/// Copies map property values onto actor members by UnrealScript name (ignoring ASCII case), so every generated or
/// ported class takes its map values without per-class code. Public instance fields and settable properties are
/// matched, most derived declaration first, plus the original names of members the generator renamed; readonly
/// fields only take static array elements. Types: bool, int, byte, float, string, Name, enums, structs, static arrays
/// (also inline arrays in structs), dynamic arrays and lists, actor references, <see cref="OpenScct.Core.Uc.AssetRef"/>,
/// <see cref="ClassRef"/>, and None for <see cref="DelegateRef"/> and other object references. Values that do not fit
/// are reported, never guessed.
/// <para>
/// Overlay (spec/svmap/1.1/README.md): a stored struct overlays the member's current value one level deep, each
/// stored member replacing that member whole; a stored static array element replaces that element; a dynamic array
/// replaces the whole value, its struct elements starting from zero.
/// </para>
/// <para>Member lookups are cached per type; not thread-safe.</para>
/// </summary>
internal sealed class PropApplier
{
    private readonly Dictionary<Type, TypeMembers> _members = new();
    private readonly Dictionary<Type, Dictionary<string, FieldInfo>> _structFields = new();
    private readonly Dictionary<Type, bool> _holdsActors = new();

    public enum Pass
    {
        /// <summary>Every member that does not hold actor references.</summary>
        Values,

        /// <summary>Members holding actor references (also inside structs), once every actor exists.</summary>
        References,
    }

    /// <summary>Receives problems found while converting one value.</summary>
    public delegate void Report(string prop, UnappliedReason reason);

    /// <summary>The members of a type, by UnrealScript name.</summary>
    public TypeMembers MembersOf(Type type, UcClassInfo? meta)
    {
        if (_members.TryGetValue(type, out TypeMembers? cached))
        {
            return cached;
        }

        var byName = new Dictionary<string, Member>(AsciiCaseComparer.Instance);
        for (Type? t = type; t is not null && t != typeof(object); t = t.BaseType)
        {
            foreach (FieldInfo f in t.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (f.IsInitOnly && !IsInPlace(f.FieldType))
                {
                    continue;
                }

                Action<object, object?>? set = f.IsInitOnly ? null : f.SetValue;
                byName.TryAdd(f.Name, new Member(f.Name, f.FieldType, f.GetValue, set, HoldsActorRefs(f.FieldType), HoldsObjectRefs(f.FieldType)));
            }

            foreach (PropertyInfo p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (p.GetIndexParameters().Length != 0 || p.GetGetMethod() is null || p.GetSetMethod() is null)
                {
                    continue;
                }

                byName.TryAdd(p.Name, new Member(p.Name, p.PropertyType, p.GetValue, p.SetValue, HoldsActorRefs(p.PropertyType),
                    HoldsObjectRefs(p.PropertyType)));
            }
        }

        // Script properties the generator gave another C# name (keywords, names equal to their class).
        var script = new List<KeyValuePair<UcPropertyInfo, Member>>();
        var seen = new HashSet<string>(AsciiCaseComparer.Instance);
        for (UcClassInfo? c = meta; c is not null; c = c.Super)
        {
            foreach (UcPropertyInfo p in c.Properties)
            {
                if (!seen.Add(p.Name) || p.CSharpName.Length == 0)
                {
                    continue;
                }

                string csharp = p.CSharpName.TrimStart('@');
                if (!byName.TryGetValue(p.Name, out Member? m) && byName.TryGetValue(csharp, out m))
                {
                    byName[p.Name] = m;
                }

                if (m is not null)
                {
                    script.Add(new KeyValuePair<UcPropertyInfo, Member>(p, m));
                }
            }
        }

        var members = new TypeMembers(byName, script);
        _members[type] = members;
        return members;
    }

    /// <summary>
    /// Applies the props of one pass. <paramref name="report"/> gets each prop that has no member (reported in the
    /// values pass), whose value does not fit the member, or that was applied with references left None.
    /// <paramref name="skip"/> names props the caller applies itself.
    /// </summary>
    public void Apply(object target, UcClassInfo? meta, PropBag props, Pass pass, Func<ActorRef, Actor?>? resolve,
        Report report, Func<string, bool>? skip = null)
    {
        TypeMembers members = MembersOf(target.GetType(), meta);
        foreach (KeyValuePair<string, PropValue> prop in props)
        {
            if (skip is not null && skip(prop.Key))
            {
                continue;
            }

            if (!members.ByName.TryGetValue(prop.Key, out Member? m))
            {
                if (pass == Pass.Values)
                {
                    report(prop.Key, UnappliedReason.NoMember);
                }

                continue;
            }

            if (m.HoldsActors != (pass == Pass.References))
            {
                continue;
            }

            ApplyOne(target, m, prop.Key, prop.Value, resolve, report);
        }
    }

    /// <summary>Converts <paramref name="value"/> onto one member of <paramref name="target"/>; false when nothing was applied.</summary>
    public bool ApplyOne(object target, Member m, string prop, PropValue value, Func<ActorRef, Actor?>? resolve, Report report)
    {
        var ctx = new Context(resolve);
        if (TryConvert(value, m.Type, m.Get(target), m.Set is not null, ref ctx, out object? converted))
        {
            m.Set?.Invoke(target, converted);
            if (ctx.Note is UnappliedReason note)
            {
                report(prop, note);
            }

            return true;
        }

        report(prop, ctx.Note ?? (target is Actors.UnportedActor ? UnappliedReason.UnportedMismatch : UnappliedReason.TypeMismatch));
        return false;
    }

    /// <summary>
    /// What <paramref name="value"/> would make of a copy of <paramref name="current"/>, without touching it; false
    /// when the value does not fit. Actor references convert to None.
    /// </summary>
    public bool TryPreview(Member m, object? current, PropValue value, out object? converted)
    {
        var ctx = new Context(static _ => null);
        return TryConvert(value, m.Type, Clone(current), m.Set is not null, ref ctx, out converted);
    }

    /// <summary>Sets a member to its zero value (0, false, None, empty), in place for arrays and lists.</summary>
    public static void Reset(object target, Member m)
    {
        object? current = m.Get(target);
        if (current is Array array)
        {
            Array.Clear(array);
            if (array.GetType().GetElementType() == typeof(string))
            {
                Array.Fill((string[])array, string.Empty);
            }

            return;
        }

        if (current is IList list && m.Type.IsGenericType)
        {
            list.Clear();
            return;
        }

        m.Set?.Invoke(target, Zero(m.Type));
    }

    /// <summary>True when <paramref name="value"/> is the zero value of <paramref name="type"/> (null strings count as empty).</summary>
    public static bool IsZero(object? value, Type type)
    {
        switch (value)
        {
            case null:
                return true;
            case string s:
                return s.Length == 0;
            case Array a:
                foreach (object? e in a)
                {
                    if (!IsZero(e, a.GetType().GetElementType()!))
                    {
                        return false;
                    }
                }

                return true;
            case IList l when type.IsGenericType:
                return l.Count == 0;
        }

        if (type.IsValueType)
        {
            if (type.IsPrimitive || type.IsEnum)
            {
                return value.Equals(Zero(type));
            }

            if (InlineLength(type) is int n)
            {
                for (int i = 0; i < n; i++)
                {
                    if (!IsZero(InlineGet(value, i), type.GetGenericArguments()[0]))
                    {
                        return false;
                    }
                }

                return true;
            }

            foreach (FieldInfo f in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                if (!IsZero(f.GetValue(value), f.FieldType))
                {
                    return false;
                }
            }

            return true;
        }

        return false;
    }

    /// <summary>Value equality for member values: arrays and lists by element, strings with null as empty.</summary>
    public static bool ValueEquals(object? a, object? b)
    {
        if (a is null || b is null)
        {
            return (a ?? b) is null || (a ?? b) is string { Length: 0 };
        }

        if (a is string sa)
        {
            return b is string sb && string.Equals(sa, sb, StringComparison.Ordinal);
        }

        if (a is Array xa)
        {
            if (b is not Array xb || xa.Length != xb.Length)
            {
                return false;
            }

            for (int i = 0; i < xa.Length; i++)
            {
                if (!ValueEquals(xa.GetValue(i), xb.GetValue(i)))
                {
                    return false;
                }
            }

            return true;
        }

        if (a is IList la && a.GetType().IsGenericType)
        {
            if (b is not IList lb || la.Count != lb.Count)
            {
                return false;
            }

            for (int i = 0; i < la.Count; i++)
            {
                if (!ValueEquals(la[i], lb[i]))
                {
                    return false;
                }
            }

            return true;
        }

        return a.Equals(b);
    }

    // ---- conversion ----------------------------------------------------------------------------------------

    private struct Context
    {
        public Context(Func<ActorRef, Actor?>? resolve)
        {
            Resolve = resolve;
            Note = null;
        }

        public Func<ActorRef, Actor?>? Resolve { get; }

        /// <summary>The first problem met (a reference left None, a missing actor...).</summary>
        public UnappliedReason? Note { get; private set; }

        public void Add(UnappliedReason reason) => Note ??= reason;
    }

    private bool TryConvert(PropValue v, Type t, object? current, bool settable, ref Context ctx, out object? result)
    {
        result = null;
        if (t == typeof(bool))
        {
            bool ok = v.TryGetBool(out bool b);
            result = b;
            return ok;
        }

        if (t == typeof(int))
        {
            bool ok = v.TryGetInt(out int i);
            result = i;
            return ok;
        }

        if (t == typeof(byte))
        {
            bool ok = v.TryGetInt(out int i) && i is >= 0 and <= 255;
            result = (byte)i;
            return ok;
        }

        if (t == typeof(float))
        {
            bool ok = v.TryGetFloat(out float f);
            result = f;
            return ok;
        }

        if (t == typeof(string))
        {
            result = v.Kind switch
            {
                PropKind.String => v.AsString(),
                PropKind.Asset => v.AsAsset().Path,
                PropKind.Class => v.AsClass(),
                _ => null,
            };
            return result is not null;
        }

        if (t == typeof(Name))
        {
            bool ok = v.TryGetString(out string s);
            result = new Name(s);
            return ok;
        }

        if (t.IsEnum)
        {
            if (!v.TryGetInt(out int i) || (Enum.GetUnderlyingType(t) == typeof(byte) && i is < 0 or > 255))
            {
                return false;
            }

            result = Enum.ToObject(t, i);
            return true;
        }

        if (t == typeof(OpenScct.Core.Uc.AssetRef))
        {
            result = v.Kind switch
            {
                PropKind.Asset => new OpenScct.Core.Uc.AssetRef(v.AsAsset().Path),
                PropKind.Null => OpenScct.Core.Uc.AssetRef.None,
                _ => null,
            };
            return result is not null;
        }

        if (t == typeof(ClassRef))
        {
            result = v.Kind switch
            {
                PropKind.Class => new ClassRef(v.AsClass()),
                PropKind.Null => ClassRef.None,
                _ => null,
            };
            return result is not null;
        }

        if (t == typeof(DelegateRef))
        {
            result = default(DelegateRef);
            return v.IsNull;
        }

        if (typeof(Actor).IsAssignableFrom(t))
        {
            return TryConvertActor(v, t, ref ctx, out result);
        }

        if (!t.IsValueType && typeof(UObject).IsAssignableFrom(t))
        {
            // Non-actor objects (interactions, mesh instances...) are not created from map data: the member keeps
            // what it holds (an array element is None).
            if (v.Kind == PropKind.Asset)
            {
                ctx.Add(UnappliedReason.ObjectReference);
                result = current;
                return true;
            }

            return v.IsNull;
        }

        if (t.IsArray && t.GetArrayRank() == 1)
        {
            return TryConvertArray(v, t.GetElementType()!, current as Array, settable, ref ctx, out result);
        }

        if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>))
        {
            if (v.Kind != PropKind.Array)
            {
                return false;
            }

            Type element = t.GetGenericArguments()[0];
            var items = new List<object?>();
            foreach (PropValue item in v.AsArray())
            {
                // A dynamic array replaces the whole value; struct elements start from zero.
                if (!TryConvert(item, element, null, true, ref ctx, out object? converted))
                {
                    return false;
                }

                items.Add(converted);
            }

            var list = (IList)(current ?? Activator.CreateInstance(t)!);
            list.Clear();
            foreach (object? item in items)
            {
                list.Add(item);
            }

            result = list;
            return current is not null || settable;
        }

        if (InlineLength(t) is int inline)
        {
            if (v.Kind != PropKind.StaticArray)
            {
                return false;
            }

            Type element = t.GetGenericArguments()[0];
            object boxed = current ?? Activator.CreateInstance(t)!;
            foreach (KeyValuePair<int, PropValue> e in v.AsStaticArray())
            {
                if (e.Key >= inline || !TryConvert(e.Value, element, null, true, ref ctx, out object? converted))
                {
                    return false;
                }

                boxed = InlineSet(boxed, e.Key, converted);
            }

            result = boxed;
            return true;
        }

        if (t.IsValueType && !t.IsPrimitive && v.Kind == PropKind.Struct)
        {
            object boxed = current ?? Activator.CreateInstance(t)!;
            Dictionary<string, FieldInfo> fields = StructFieldsOf(t);
            foreach (KeyValuePair<string, PropValue> member in v.AsStruct())
            {
                if (!fields.TryGetValue(member.Key, out FieldInfo? field) ||
                    !TryConvert(member.Value, field.FieldType, null, true, ref ctx, out object? converted))
                {
                    return false;
                }

                field.SetValue(boxed, converted);
            }

            result = boxed;
            return true;
        }

        return false;
    }

    private static bool TryConvertActor(PropValue v, Type t, ref Context ctx, out object? result)
    {
        result = null;
        switch (v.Kind)
        {
            case PropKind.Null:
                return true;
            case PropKind.Asset:
                // An actor of the map that only the client draws: None in the simulation.
                ctx.Add(UnappliedReason.RenderOnlyActor);
                return true;
            case PropKind.Actor:
                if (ctx.Resolve is null)
                {
                    return false;
                }

                Actor? actor = ctx.Resolve(v.AsActor());
                if (actor is null)
                {
                    // UE2 loads a reference to an actor that is gone as None.
                    ctx.Add(UnappliedReason.MissingActor);
                    return true;
                }

                if (!t.IsInstanceOfType(actor))
                {
                    ctx.Add(actor is Actors.UnportedActor ? UnappliedReason.UnportedTarget : UnappliedReason.WrongActorClass);
                    return false;
                }

                result = actor;
                return true;
            default:
                return false;
        }
    }

    private bool TryConvertArray(PropValue v, Type element, Array? current, bool settable, ref Context ctx, out object? result)
    {
        result = null;
        if (v.Kind == PropKind.StaticArray)
        {
            PropStaticArray elements = v.AsStaticArray();
            Array? array = current;
            if (array is null)
            {
                if (!settable)
                {
                    return false;
                }

                array = Array.CreateInstance(element, elements.MaxIndex + 1);
            }

            foreach (KeyValuePair<int, PropValue> e in elements)
            {
                if (e.Key >= array.Length || !TryConvert(e.Value, element, null, true, ref ctx, out object? converted))
                {
                    return false;
                }

                array.SetValue(converted, e.Key);
            }

            result = array;
            return true;
        }

        if (v.Kind == PropKind.Array && settable)
        {
            IReadOnlyList<PropValue> items = v.AsArray();
            Array array = Array.CreateInstance(element, items.Count);
            for (int i = 0; i < items.Count; i++)
            {
                if (!TryConvert(items[i], element, null, true, ref ctx, out object? converted))
                {
                    return false;
                }

                array.SetValue(converted, i);
            }

            result = array;
            return true;
        }

        return false;
    }

    private Dictionary<string, FieldInfo> StructFieldsOf(Type type)
    {
        if (!_structFields.TryGetValue(type, out Dictionary<string, FieldInfo>? fields))
        {
            fields = new Dictionary<string, FieldInfo>(AsciiCaseComparer.Instance);
            foreach (FieldInfo f in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!f.IsInitOnly)
                {
                    fields.TryAdd(f.Name, f);
                }
            }

            _structFields[type] = fields;
        }

        return fields;
    }

    private bool HoldsActorRefs(Type t)
    {
        if (_holdsActors.TryGetValue(t, out bool cached))
        {
            return cached;
        }

        _holdsActors[t] = false; // cycle guard
        bool holds;
        if (typeof(Actor).IsAssignableFrom(t))
        {
            holds = true;
        }
        else if (t.IsArray)
        {
            holds = HoldsActorRefs(t.GetElementType()!);
        }
        else if (t.IsGenericType && (t.GetGenericTypeDefinition() == typeof(List<>) || InlineLength(t) is not null))
        {
            holds = HoldsActorRefs(t.GetGenericArguments()[0]);
        }
        else if (t.IsValueType && !t.IsPrimitive && !t.IsEnum && t.Namespace?.StartsWith("OpenScct.", StringComparison.Ordinal) == true)
        {
            holds = false;
            foreach (FieldInfo f in t.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (HoldsActorRefs(f.FieldType))
                {
                    holds = true;
                    break;
                }
            }
        }
        else
        {
            holds = false;
        }

        _holdsActors[t] = holds;
        return holds;
    }

    /// <summary>A reference to a non-actor object, or an array or list of them (map data never creates those).</summary>
    private static bool HoldsObjectRefs(Type t)
    {
        Type e = t.IsArray ? t.GetElementType()! : t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>) ? t.GetGenericArguments()[0] : t;
        return !e.IsValueType && typeof(UObject).IsAssignableFrom(e) && !typeof(Actor).IsAssignableFrom(e);
    }

    // ---- values --------------------------------------------------------------------------------------------

    private static bool IsInPlace(Type t) => t.IsArray;

    private static object? Clone(object? value) => value switch
    {
        Array a => a.Clone(),
        IList l when l.GetType().IsGenericType => CloneList(l),
        _ => value,
    };

    private static IList CloneList(IList list)
    {
        var copy = (IList)Activator.CreateInstance(list.GetType())!;
        foreach (object? item in list)
        {
            copy.Add(item);
        }

        return copy;
    }

    private static object? Zero(Type t)
    {
        if (t == typeof(string))
        {
            return string.Empty;
        }

        return t.IsValueType ? Activator.CreateInstance(t) : null;
    }

    /// <summary>The element count of an <see cref="InlineArrayAttribute"/> struct (FixedArrayN), or null.</summary>
    internal static int? InlineLength(Type t) =>
        t.IsValueType && t.IsGenericType && t.GetCustomAttribute<InlineArrayAttribute>() is { } a ? a.Length : null;

    internal static object? InlineGet(object boxed, int index) =>
        typeof(PropApplier).GetMethod(nameof(InlineGetT), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(boxed.GetType(), boxed.GetType().GetGenericArguments()[0]).Invoke(null, [boxed, index]);

    private static object InlineSet(object boxed, int index, object? value) =>
        typeof(PropApplier).GetMethod(nameof(InlineSetT), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(boxed.GetType(), boxed.GetType().GetGenericArguments()[0]).Invoke(null, [boxed, index, value])!;

    private static TElement InlineGetT<TArray, TElement>(object boxed, int index)
        where TArray : struct
    {
        TArray array = (TArray)boxed;
        return Unsafe.Add(ref Unsafe.As<TArray, TElement>(ref array), index);
    }

    private static object InlineSetT<TArray, TElement>(object boxed, int index, TElement value)
        where TArray : struct
    {
        TArray array = (TArray)boxed;
        Unsafe.Add(ref Unsafe.As<TArray, TElement>(ref array), index) = value;
        return array;
    }

    /// <summary>One member of a type: its C# name, type, accessors and whether it holds actor or other object references.</summary>
    internal sealed record Member(string CSharpName, Type Type, Func<object, object?> Get, Action<object, object?>? Set, bool HoldsActors,
        bool HoldsObjects);

    /// <summary>The members of one type by UnrealScript name, and the script properties of its metadata with their member.</summary>
    internal sealed record TypeMembers(Dictionary<string, Member> ByName, List<KeyValuePair<UcPropertyInfo, Member>> ScriptMembers);
}
