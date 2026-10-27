using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;

namespace OpenScct.Content.Loading;

/// <summary>
/// Writes a class table (<c>sim/classes.json</c> content) from the generated classes: for each concrete class, the
/// non-zero values a new object of it holds (its generated defaults), keyed by UnrealScript name; abstract super
/// classes are listed with their super only. A native map can ship this table for the classes it places, and it is
/// what the loader compares a converted map's table with. Actor and object references are left out (a class default
/// object refers to no map actor).
/// </summary>
public static class ClassTableBuilder
{
    /// <summary>
    /// The class table of <paramref name="classes"/> and their super chains. Names that are object internals or that
    /// a map sets per actor (Tag, Location, Rotation...) are left out.
    /// </summary>
    public static ClassTable Build(IEnumerable<UcClassInfo> classes)
    {
        ArgumentNullException.ThrowIfNull(classes);
        var applier = new PropApplier();
        var entries = new Dictionary<string, ClassEntry>(AsciiCaseComparer.Instance);
        var order = new List<string>();
        foreach (UcClassInfo info in classes)
        {
            for (UcClassInfo? c = info; c is not null; c = c.Super)
            {
                bool listed = entries.TryGetValue(c.Path, out ClassEntry? existing);
                if (listed && (existing!.Defaults is not null || !ReferenceEquals(c, info)))
                {
                    continue;
                }

                PropBag? defaults = ReferenceEquals(c, info) ? DefaultsOf(applier, c) : null;
                if (!listed)
                {
                    order.Add(c.Path);
                }

                entries[c.Path] = new ClassEntry(c.Path, c.Super?.Path, defaults);
            }
        }

        var list = new List<ClassEntry>(order.Count);
        order.Sort(StringComparer.Ordinal);
        foreach (string path in order)
        {
            list.Add(entries[path]);
        }

        return ClassTable.Create(list);
    }

    /// <summary>The non-zero generated defaults of one concrete class, or null when it cannot be created.</summary>
    public static PropBag? Defaults(UcClassInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        return DefaultsOf(new PropApplier(), info);
    }

    private static PropBag? DefaultsOf(PropApplier applier, UcClassInfo info)
    {
        if (info.IsAbstract || info.Create() is not { } obj)
        {
            return null;
        }

        PropApplier.TypeMembers members = applier.MembersOf(obj.GetType(), obj.GetUcClass());
        var items = new List<KeyValuePair<string, PropValue>>();
        foreach (KeyValuePair<UcPropertyInfo, PropApplier.Member> p in members.ScriptMembers)
        {
            if (MapLoader.SkipsDefault(p.Key.Name) || p.Value.HoldsActors || p.Value.HoldsObjects)
            {
                continue;
            }

            object? value = p.Value.Get(obj);
            if (PropApplier.IsZero(value, p.Value.Type) || ToPropValue(value, p.Value.Type) is not PropValue v)
            {
                continue;
            }

            items.Add(new KeyValuePair<string, PropValue>(p.Key.Name, v));
        }

        items.Sort(static (a, b) => string.CompareOrdinal(a.Key, b.Key));
        return new PropBag(items);
    }

    /// <summary>A C# member value as an SVMap prop value (zero members of structs left out), or null when it has no prop form.</summary>
    internal static PropValue? ToPropValue(object? value, Type type)
    {
        switch (value)
        {
            case null:
                return null;
            case bool b:
                return PropValue.FromBool(b);
            case int i:
                return PropValue.FromInt(i);
            case byte by:
                return PropValue.FromInt(by);
            case float f:
                return float.IsFinite(f) ? PropValue.FromFloat(f) : null;
            case string s:
                return PropValue.FromString(s);
            case Name n:
                return PropValue.FromString(n.ToString());
            case Enum e:
                return PropValue.FromInt(Convert.ToInt32(e, System.Globalization.CultureInfo.InvariantCulture));
            case OpenScct.Core.Uc.AssetRef a:
                return a.IsNone ? PropValue.Null : AssetRef.IsValidPath(a.Path!) ? PropValue.FromAsset(new AssetRef(a.Path!)) : null;
            case ClassRef c:
                return c.IsNone ? PropValue.Null : PropValue.FromClass(c.Path!);
            case DelegateRef or UObject:
                return null;
            case Array array:
            {
                Type element = type.GetElementType()!;
                var elements = new List<KeyValuePair<int, PropValue>>();
                for (int k = 0; k < array.Length; k++)
                {
                    object? item = array.GetValue(k);
                    if (!PropApplier.IsZero(item, element) && ToPropValue(item, element) is PropValue pv)
                    {
                        elements.Add(new KeyValuePair<int, PropValue>(k, pv));
                    }
                }

                return elements.Count == 0 ? null : PropValue.FromStaticArray(new PropStaticArray(elements));
            }

            case IList list when type.IsGenericType:
            {
                Type element = type.GetGenericArguments()[0];
                var items = new List<PropValue>(list.Count);
                foreach (object? item in list)
                {
                    if (ToPropValue(item, element) is not PropValue v)
                    {
                        return null;
                    }

                    items.Add(v);
                }

                return PropValue.FromArray(items);
            }
        }

        if (!type.IsValueType || type.IsPrimitive)
        {
            return null;
        }

        if (PropApplier.InlineLength(type) is int length)
        {
            Type element = type.GetGenericArguments()[0];
            var elements = new List<KeyValuePair<int, PropValue>>();
            for (int k = 0; k < length; k++)
            {
                object? item = PropApplier.InlineGet(value, k);
                if (!PropApplier.IsZero(item, element) && ToPropValue(item, element) is PropValue pv)
                {
                    elements.Add(new KeyValuePair<int, PropValue>(k, pv));
                }
            }

            return elements.Count == 0 ? null : PropValue.FromStaticArray(new PropStaticArray(elements));
        }

        var members = new List<KeyValuePair<string, PropValue>>();
        foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            if (field.IsInitOnly)
            {
                continue;
            }

            object? item = field.GetValue(value);
            if (PropApplier.IsZero(item, field.FieldType))
            {
                continue;
            }

            if (ToPropValue(item, field.FieldType) is not PropValue pv)
            {
                return null;
            }

            members.Add(new KeyValuePair<string, PropValue>(field.Name, pv));
        }

        return PropValue.FromStruct(new PropBag(members));
    }
}
