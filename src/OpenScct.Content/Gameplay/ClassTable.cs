using System;
using System.Collections.Generic;
using System.IO;
using OpenScct.Content.Json;

namespace OpenScct.Content;

/// <summary>
/// <c>sim/classes.json</c> (SVMap 1.1, optional): the effective class defaults of every class the map's actors use,
/// with their super chains (<c>{"format": "svm.classes/1", "classes": {path: {"super", "defaults"}}}</c>). An actor's
/// full state is its class defaults overlaid with its props. Classes match ignoring ASCII case; enumeration keeps
/// document order. Members other than <c>super</c> and <c>defaults</c> (the shared pack has more) are ignored.
/// </summary>
public sealed class ClassTable
{
    public const string RelativePath = "sim/classes.json";

    /// <summary>The document format this reader understands.</summary>
    public const string FormatId = "svm.classes/1";

    private readonly List<ClassEntry> _classes;
    private readonly Dictionary<string, int> _byPath;

    private ClassTable(string format, List<ClassEntry> classes)
    {
        Format = format;
        _classes = classes;
        _byPath = new Dictionary<string, int>(classes.Count, AsciiCaseComparer.Instance);
        for (int i = 0; i < classes.Count; i++)
        {
            _byPath.TryAdd(classes[i].Path, i);
        }
    }

    public string Format { get; }

    /// <summary>Every class in document order.</summary>
    public IReadOnlyList<ClassEntry> Classes => _classes;

    public ClassEntry? Find(string classPath) => _byPath.TryGetValue(classPath, out int i) ? _classes[i] : null;

    /// <summary>The class and its super classes, most derived first, as far as the table lists them.</summary>
    public List<ClassEntry> Chain(string classPath)
    {
        var chain = new List<ClassEntry>();
        for (ClassEntry? c = Find(classPath); c is not null && chain.Count < 64; c = c.Super is { } s ? Find(s) : null)
        {
            chain.Add(c);
        }

        return chain;
    }

    /// <summary>True when <paramref name="classPath"/> is <paramref name="basePath"/> or derives from it in the table.</summary>
    public bool IsA(string classPath, string basePath)
    {
        foreach (ClassEntry c in Chain(classPath))
        {
            if (AsciiCaseComparer.AreEqual(c.Path, basePath))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The effective defaults of a class, or null when the table has none for it.</summary>
    public PropBag? DefaultsOf(string classPath) => Find(classPath)?.Defaults;

    /// <summary>
    /// The effective value of one property of a placed actor: its stored value overlaid on the class default
    /// (<see cref="Overlay"/>), else the class default; null when neither has one (the value is zero, None or empty).
    /// </summary>
    public PropValue? Effective(ActorRecord record, string name)
    {
        ArgumentNullException.ThrowIfNull(record);
        bool stored = record.Props.TryGetValue(name, out PropValue value);
        PropBag? defaults = DefaultsOf(record.Class);
        if (defaults is not null && defaults.TryGetValue(name, out PropValue d))
        {
            return stored ? Overlay(d, value) : d;
        }

        return stored ? value : null;
    }

    /// <summary>The full state of a placed actor: its class defaults overlaid with its props (props win, in their order after the defaults).</summary>
    public PropBag EffectiveProps(ActorRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        PropBag? defaults = DefaultsOf(record.Class);
        if (defaults is null)
        {
            return record.Props;
        }

        var items = new List<KeyValuePair<string, PropValue>>(defaults.Count + record.Props.Count);
        var at = new Dictionary<string, int>(AsciiCaseComparer.Instance);
        foreach (KeyValuePair<string, PropValue> d in defaults)
        {
            at[d.Key] = items.Count;
            items.Add(d);
        }

        foreach (KeyValuePair<string, PropValue> p in record.Props)
        {
            if (at.TryGetValue(p.Key, out int i))
            {
                items[i] = new KeyValuePair<string, PropValue>(items[i].Key, Overlay(items[i].Value, p.Value));
            }
            else
            {
                at[p.Key] = items.Count;
                items.Add(p);
            }
        }

        return new PropBag(items);
    }

    /// <summary>
    /// UE2 overlay of a stored value on a default (spec/svmap/1.1/README.md): a stored struct overlays the default
    /// member by member, a stored static array element replaces that element, anything else (a dynamic array
    /// included) replaces the whole value.
    /// </summary>
    public static PropValue Overlay(PropValue defaultValue, PropValue stored)
    {
        if (stored.Kind == PropKind.Struct && defaultValue.Kind == PropKind.Struct)
        {
            PropBag d = defaultValue.AsStruct();
            PropBag s = stored.AsStruct();
            var items = new List<KeyValuePair<string, PropValue>>(d.Count + s.Count);
            var at = new Dictionary<string, int>(AsciiCaseComparer.Instance);
            foreach (KeyValuePair<string, PropValue> m in d)
            {
                at[m.Key] = items.Count;
                items.Add(m);
            }

            foreach (KeyValuePair<string, PropValue> m in s)
            {
                if (at.TryGetValue(m.Key, out int i))
                {
                    items[i] = new KeyValuePair<string, PropValue>(items[i].Key, m.Value);
                }
                else
                {
                    items.Add(m);
                }
            }

            return PropValue.FromStruct(new PropBag(items));
        }

        if (stored.Kind == PropKind.StaticArray && defaultValue.Kind == PropKind.StaticArray)
        {
            var elements = new SortedDictionary<int, PropValue>();
            foreach (KeyValuePair<int, PropValue> e in defaultValue.AsStaticArray())
            {
                elements[e.Key] = e.Value;
            }

            foreach (KeyValuePair<int, PropValue> e in stored.AsStaticArray())
            {
                elements[e.Key] = e.Value;
            }

            return PropValue.FromStaticArray(new PropStaticArray(elements));
        }

        return stored;
    }

    // ---- load and write ------------------------------------------------------------------------------------

    internal static ClassTable Create(List<ClassEntry> classes) => new(FormatId, classes);

    /// <summary>The table as JSON (<c>{"format", "classes"}</c>); <see cref="CanonicalJson"/> turns it into file bytes.</summary>
    public SvJsonObject ToJson()
    {
        var classes = new SvJsonObject();
        foreach (ClassEntry c in _classes)
        {
            var entry = new SvJsonObject();
            entry.Add("super", c.Super is null ? SvJsonNode.Null : SvJsonNode.String(c.Super));
            if (c.Defaults is not null)
            {
                entry.Add("defaults", PropJson.BagToJson(c.Defaults));
            }

            classes.Add(c.Path, entry);
        }

        var root = new SvJsonObject();
        root.Add("format", SvJsonNode.String(Format));
        root.Add("classes", classes);
        return root;
    }

    public static ClassTable Load(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return Parse(File.ReadAllBytes(path));
    }

    public static ClassTable Parse(ReadOnlySpan<byte> utf8, string file = RelativePath) => FromJson(SvJson.Parse(utf8, file), file);

    public static ClassTable FromJson(SvJsonNode node, string file = RelativePath)
    {
        ArgumentNullException.ThrowIfNull(node);
        var j = new JsonRead(file);
        SvJsonObject root = j.Object(node, "");
        string format = j.StringMember(root, "format", "");
        if (format != FormatId)
        {
            throw j.Error("/format", $"'{format}' is not {FormatId}");
        }

        var classes = new List<ClassEntry>();
        var seen = new HashSet<string>(AsciiCaseComparer.Instance);
        foreach (KeyValuePair<string, SvJsonNode> c in j.ObjectMember(root, "classes", ""))
        {
            string at = $"/classes/{c.Key}";
            if (!PropJson.IsClassPath(c.Key))
            {
                throw j.Error(at, "is not Package.Class");
            }

            if (!seen.Add(c.Key))
            {
                throw j.Error(at, "is listed twice (class paths ignore case)");
            }

            SvJsonObject entry = j.Object(c.Value, at);
            string? super = null;
            if (entry.TryGetValue("super", out SvJsonNode superNode) && superNode.Kind != SvJsonKind.Null)
            {
                super = j.String(superNode, $"{at}/super");
                if (!PropJson.IsClassPath(super))
                {
                    throw j.Error($"{at}/super", $"'{super}' is not Package.Class");
                }
            }

            PropBag? defaults = entry.TryGetValue("defaults", out SvJsonNode d)
                ? PropJson.BagFromJson(j, j.Object(d, $"{at}/defaults"), $"{at}/defaults", null)
                : null;
            classes.Add(new ClassEntry(c.Key, super, defaults));
        }

        return new ClassTable(format, classes);
    }

    /// <summary>Classes of the gameplay document whose actors have no defaults here, and supers the table does not list.</summary>
    public List<string> Check(GameplayDoc gameplay)
    {
        ArgumentNullException.ThrowIfNull(gameplay);
        var problems = new List<string>();
        var reported = new HashSet<string>(AsciiCaseComparer.Instance);
        foreach (ActorRecord a in gameplay.AllActors)
        {
            if (Find(a.Class)?.Defaults is null && reported.Add(a.Class))
            {
                problems.Add($"{RelativePath}: no defaults for {a.Class} ({a.Id})");
            }
        }

        foreach (ClassEntry c in _classes)
        {
            if (c.Super is { } super && Find(super) is null)
            {
                problems.Add($"{RelativePath}: {c.Path} names super class {super}, which is missing");
            }
        }

        return problems;
    }
}

/// <summary>One class of <see cref="ClassTable"/>: its path, super class and effective defaults (null for a super class listed only for its chain).</summary>
public sealed record ClassEntry(string Path, string? Super, PropBag? Defaults);
