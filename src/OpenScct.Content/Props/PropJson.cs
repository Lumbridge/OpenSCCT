using System;
using System.Collections.Generic;
using System.Globalization;
using OpenScct.Content.Json;

namespace OpenScct.Content;

/// <summary>Converts between gameplay.json property values and <see cref="PropValue"/> (README, value table).</summary>
internal static class PropJson
{
    public const int MaxNameLength = 63;

    public static PropBag BagFromJson(JsonRead j, SvJsonObject obj, string path, ActorIndex? index)
    {
        var items = new List<KeyValuePair<string, PropValue>>(obj.Count);
        var seen = new HashSet<string>(AsciiCaseComparer.Instance);
        foreach (KeyValuePair<string, SvJsonNode> member in obj)
        {
            string memberPath = $"{path}/{member.Key}";
            if (!IsIdentifier(member.Key))
            {
                throw j.Error(memberPath, "is not an UnrealScript property name");
            }

            if (!seen.Add(member.Key))
            {
                throw j.Error(memberPath, "is given twice (property names ignore case)");
            }

            items.Add(new KeyValuePair<string, PropValue>(member.Key, ValueFromJson(j, member.Value, memberPath, index)));
        }

        return new PropBag(items);
    }

    public static PropValue ValueFromJson(JsonRead j, SvJsonNode node, string path, ActorIndex? index)
    {
        switch (node)
        {
            case SvJsonNumber { IsFloat: true } f:
                return PropValue.FromFloat(f.FloatValue);
            case SvJsonNumber i:
                return i.TryGetInt32(out int v) ? PropValue.FromInt(v) : throw j.Error(path, $"integer {i.IntegerText} is outside the int range");
            case SvJsonString s:
                return PropValue.FromString(s.Value);
            case SvJsonArray array:
            {
                var items = new PropValue[array.Count];
                for (int k = 0; k < array.Count; k++)
                {
                    items[k] = ValueFromJson(j, array[k], $"{path}/{k}", index);
                }

                return PropValue.FromArray(items);
            }

            case SvJsonObject obj:
                return ObjectFromJson(j, obj, path, index);
            default:
                return node.Kind switch
                {
                    SvJsonKind.True => PropValue.FromBool(true),
                    SvJsonKind.False => PropValue.FromBool(false),
                    _ => PropValue.Null,
                };
        }
    }

    private static PropValue ObjectFromJson(JsonRead j, SvJsonObject obj, string path, ActorIndex? index)
    {
        bool special = false;
        foreach (KeyValuePair<string, SvJsonNode> member in obj)
        {
            if (member.Key.StartsWith('$'))
            {
                special = true;
                break;
            }
        }

        if (!special)
        {
            return PropValue.FromStruct(BagFromJson(j, obj, path, index));
        }

        if (obj.Count != 1)
        {
            throw j.Error(path, "a $actor, $asset, $class or $elements object holds exactly one member");
        }

        KeyValuePair<string, SvJsonNode> only = obj[0];
        string at = $"{path}/{only.Key}";
        switch (only.Key)
        {
            case "$actor":
            {
                string id = j.String(only.Value, at);
                return IsIdentifier(id) ? PropValue.FromActor(new ActorRef(id, index)) : throw j.Error(at, $"'{id}' is not an actor id");
            }

            case "$asset":
            {
                string asset = j.String(only.Value, at);
                return AssetRef.IsValidPath(asset)
                    ? PropValue.FromAsset(new AssetRef(asset))
                    : throw j.Error(at, $"'{asset}' is not an asset path");
            }

            case "$class":
            {
                string cls = j.String(only.Value, at);
                return IsClassPath(cls) ? PropValue.FromClass(cls) : throw j.Error(at, $"'{cls}' is not Package.Class");
            }

            case "$elements":
            {
                SvJsonObject elements = j.Object(only.Value, at);
                if (elements.Count == 0)
                {
                    throw j.Error(at, "a static array stores at least one element");
                }

                var items = new List<KeyValuePair<int, PropValue>>(elements.Count);
                foreach (KeyValuePair<string, SvJsonNode> e in elements)
                {
                    if (!TryParseIndex(e.Key, out int elementIndex))
                    {
                        throw j.Error($"{at}/{e.Key}", "is not a decimal element index");
                    }

                    items.Add(new KeyValuePair<int, PropValue>(elementIndex, ValueFromJson(j, e.Value, $"{at}/{e.Key}", index)));
                }

                return PropValue.FromStaticArray(new PropStaticArray(items));
            }

            default:
                throw j.Error(at, "unknown special member");
        }
    }

    public static SvJsonObject BagToJson(PropBag bag)
    {
        var obj = new SvJsonObject();
        foreach (KeyValuePair<string, PropValue> item in bag)
        {
            obj.Add(item.Key, ToJson(item.Value));
        }

        return obj;
    }

    public static SvJsonNode ToJson(PropValue value)
    {
        switch (value.Kind)
        {
            case PropKind.Null:
                return SvJsonNode.Null;
            case PropKind.Bool:
                return SvJsonNode.Bool(value.AsBool());
            case PropKind.Int:
                return SvJsonNode.Integer(value.AsInt());
            case PropKind.Float:
                return SvJsonNode.Float(value.AsFloat());
            case PropKind.String:
                return SvJsonNode.String(value.AsString());
            case PropKind.Actor:
                return Single("$actor", SvJsonNode.String(value.AsActor().Id));
            case PropKind.Asset:
                return Single("$asset", SvJsonNode.String(value.AsAsset().Path));
            case PropKind.Class:
                return Single("$class", SvJsonNode.String(value.AsClass()));
            case PropKind.Struct:
                return BagToJson(value.AsStruct());
            case PropKind.Array:
            {
                var array = new SvJsonArray();
                foreach (PropValue item in value.AsArray())
                {
                    array.Add(ToJson(item));
                }

                return array;
            }

            default:
            {
                var elements = new SvJsonObject();
                foreach (KeyValuePair<int, PropValue> e in value.AsStaticArray())
                {
                    elements.Add(e.Key.ToString(CultureInfo.InvariantCulture), ToJson(e.Value));
                }

                return Single("$elements", elements);
            }
        }
    }

    private static SvJsonObject Single(string key, SvJsonNode value)
    {
        var obj = new SvJsonObject();
        obj.Add(key, value);
        return obj;
    }

    /// <summary><c>^[A-Za-z_][A-Za-z0-9_]*$</c>, at most 63 characters (UE2 NAME_SIZE).</summary>
    public static bool IsIdentifier(string s)
    {
        if (s.Length is 0 or > MaxNameLength || !(char.IsAsciiLetter(s[0]) || s[0] == '_'))
        {
            return false;
        }

        foreach (char c in s)
        {
            if (!(char.IsAsciiLetterOrDigit(c) || c == '_'))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary><c>Package.Class</c>, both identifiers.</summary>
    public static bool IsClassPath(string s)
    {
        int dot = s.IndexOf('.', StringComparison.Ordinal);
        return dot > 0 && s.IndexOf('.', dot + 1) < 0 && IsIdentifier(s[..dot]) && IsIdentifier(s[(dot + 1)..]);
    }

    private static bool TryParseIndex(string s, out int value)
    {
        value = 0;
        if (s.Length == 0 || (s.Length > 1 && s[0] == '0'))
        {
            return false;
        }

        foreach (char c in s)
        {
            if (!char.IsAsciiDigit(c))
            {
                return false;
            }
        }

        return int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }
}
