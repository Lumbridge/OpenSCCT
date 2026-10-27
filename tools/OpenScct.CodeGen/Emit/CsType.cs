using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenScct.CodeGen.Emit;

/// <summary>A C# type the generator writes.</summary>
internal abstract record CsType
{
    /// <summary>True for reference types (written with ? on fields).</summary>
    public abstract bool IsReference { get; }
}

/// <summary>A keyword type: bool, byte, int, float, string.</summary>
internal sealed record CsKeyword(string Keyword) : CsType
{
    public static readonly CsKeyword Bool = new("bool");
    public static readonly CsKeyword Byte = new("byte");
    public static readonly CsKeyword Int = new("int");
    public static readonly CsKeyword Float = new("float");
    public static readonly CsKeyword String = new("string");

    public override bool IsReference => Keyword == "string";
}

/// <summary>A named type in a namespace (generated, hand-written or from the runtime support library).</summary>
internal sealed record CsNamed(string Namespace, string Name, bool Reference, string Kind) : CsType
{
    public override bool IsReference => Reference;

    public string FullName => Namespace + "." + Name;
}

/// <summary>List&lt;T&gt;.</summary>
internal sealed record CsList(CsType Element) : CsType
{
    public override bool IsReference => true;
}

/// <summary>T[] (a class static array).</summary>
internal sealed record CsArray(CsType Element) : CsType
{
    public override bool IsReference => true;
}

/// <summary>FixedArrayN&lt;T&gt; (a struct static array, inline).</summary>
internal sealed record CsInlineArray(int Length, CsType Element) : CsType
{
    public override bool IsReference => false;
}

/// <summary>
/// Writes type names as short as they can be without changing meaning. A simple name is used only when C# name
/// lookup from the file's namespace (enclosing namespaces first, then the file's using directives) finds exactly
/// the intended type and nothing in the enclosing class chain shadows it; otherwise the name is written with
/// <c>global::</c>.
/// </summary>
internal sealed class TypeNamer
{
    public const string UcNamespace = "OpenScct.Core.Uc";
    public const string ListNamespace = "System.Collections.Generic";

    private readonly Dictionary<string, HashSet<string>> _typesByNamespace = new(StringComparer.Ordinal);

    public TypeNamer(IEnumerable<(string Namespace, string Name)> knownTypes)
    {
        foreach ((string ns, string name) in knownTypes)
        {
            Add(ns, name);
        }

        Add(ListNamespace, "List");
    }

    /// <summary>The using directives every generated file carries.</summary>
    public static readonly string[] Imports = ["System.Collections.Generic", "OpenScct.Core", "OpenScct.Core.Engine", UcNamespace];

    public void Add(string ns, string name)
    {
        if (!_typesByNamespace.TryGetValue(ns, out HashSet<string>? set))
        {
            _typesByNamespace[ns] = set = new HashSet<string>(StringComparer.Ordinal);
        }

        set.Add(name);
    }

    public bool Exists(string ns, string name) => _typesByNamespace.TryGetValue(ns, out HashSet<string>? set) && set.Contains(name);

    /// <summary>
    /// Writes <paramref name="type"/> for a file in <paramref name="fileNamespace"/>. <paramref name="shadowing"/>
    /// holds names that hide types in this context (nested types, and in expressions also members).
    /// </summary>
    public string Write(CsType type, string fileNamespace, IReadOnlySet<string>? shadowing = null, bool nullable = false)
    {
        string text = type switch
        {
            CsKeyword k => k.Keyword,
            CsNamed n => Name(n.Namespace, n.Name, fileNamespace, shadowing),
            CsList l => Name(ListNamespace, "List", fileNamespace, shadowing) + "<" + Write(l.Element, fileNamespace, shadowing, nullable: true) + ">",
            CsArray a => Write(a.Element, fileNamespace, shadowing, nullable: true) + "[]",
            CsInlineArray i => Name(UcNamespace, "FixedArray" + i.Length, fileNamespace, shadowing) + "<" + Write(i.Element, fileNamespace, shadowing, nullable: true) + ">",
            _ => throw new InvalidOperationException($"Unknown C# type {type}."),
        };
        bool addQuestion = nullable && type is CsNamed { Reference: true };
        return addQuestion ? text + "?" : text;
    }

    /// <summary>A named type as short as it can safely be written.</summary>
    public string Name(string ns, string name, string fileNamespace, IReadOnlySet<string>? shadowing = null)
    {
        if (shadowing is not null && shadowing.Contains(name))
        {
            return "global::" + ns + "." + name;
        }

        return Resolve(name, fileNamespace) == ns ? name : "global::" + ns + "." + name;
    }

    /// <summary>The namespace a simple name binds to from <paramref name="fileNamespace"/>, or null when ambiguous or unknown.</summary>
    public string? Resolve(string name, string fileNamespace)
    {
        for (string? ns = fileNamespace; ns is not null; ns = Parent(ns))
        {
            if (Exists(ns, name))
            {
                return ns;
            }
        }

        string[] hits = Imports.Where(i => Exists(i, name)).ToArray();
        return hits.Length == 1 ? hits[0] : null;
    }

    private static string? Parent(string ns)
    {
        int dot = ns.LastIndexOf('.');
        return dot < 0 ? null : ns[..dot];
    }
}
