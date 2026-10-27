using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenScct.CodeGen.Model;

/// <summary>A type named in source text that is not resolved yet (an enum, struct or class short name).</summary>
internal sealed record UNamed(string Name) : UType
{
    public override string UcText => Name;
}

/// <summary>
/// Parses UnrealScript type text (<c>bool</c>, <c>array&lt;class&lt;Pawn&gt; &gt;</c>, <c>Sound</c>...) for vars the
/// reflection dumps do not cover. Short names stay <see cref="UNamed"/> until <see cref="Resolve"/>.
/// </summary>
internal static class TypeTextParser
{
    public static UType Parse(string text)
    {
        string t = text.Trim();
        switch (t.ToLowerInvariant())
        {
            case "bool":
                return UBool.Instance;
            case "byte":
                return UByte.Instance;
            case "int":
                return UInt.Instance;
            case "float":
                return UFloat.Instance;
            case "name":
                return UName.Instance;
            case "string":
                return UString.Instance;
            case "class":
                return new UClassRef("Core.Object");
        }

        if (TryGeneric(t, "array", out string? inner))
        {
            return new UArray(Parse(inner));
        }

        if (TryGeneric(t, "class", out string? meta))
        {
            return new UClassRef(new UNamed(meta).Name);
        }

        if (TryGeneric(t, "delegate", out string? func))
        {
            return new UDelegate(func);
        }

        return new UNamed(t);
    }

    private static bool TryGeneric(string text, string keyword, out string inner)
    {
        inner = string.Empty;
        if (!text.StartsWith(keyword + "<", StringComparison.OrdinalIgnoreCase) || !text.EndsWith('>'))
        {
            return false;
        }

        inner = text[(keyword.Length + 1)..^1].Trim();
        return inner.Length > 0;
    }

    /// <summary>
    /// Replaces short names with resolved types: an enum, then a struct, then a class with that short name.
    /// A class<> meta class short name is resolved to a class path. Unknown names stay objects of Core.Object.
    /// </summary>
    public static UType Resolve(UType type, UcModel model, List<string> notes, string where)
    {
        switch (type)
        {
            case UNamed named:
            {
                string? e = model.Enums.Keys.Where(k => UType.ShortName(k).Equals(named.Name, StringComparison.OrdinalIgnoreCase)).Order(StringComparer.Ordinal).FirstOrDefault();
                if (e is not null)
                {
                    return new UEnum(e);
                }

                string? s = model.Structs.Keys.Where(k => UType.ShortName(k).Equals(named.Name, StringComparison.OrdinalIgnoreCase)).Order(StringComparer.Ordinal).FirstOrDefault();
                if (s is not null)
                {
                    return new UStruct(s);
                }

                return new UObjectRef(ResolveClass(named.Name, model, notes, where));
            }

            case UClassRef c when !c.MetaClassPath.Contains('.', StringComparison.Ordinal):
                return new UClassRef(ResolveClass(c.MetaClassPath, model, notes, where));
            case UArray a:
                return new UArray(Resolve(a.Inner, model, notes, where));
            default:
                return type;
        }
    }

    private static string ResolveClass(string shortName, UcModel model, List<string> notes, string where)
    {
        string? c = model.Classes.Keys.Where(k => UType.ShortName(k).Equals(shortName, StringComparison.OrdinalIgnoreCase)).Order(StringComparer.Ordinal).FirstOrDefault();
        if (c is null)
        {
            notes.Add($"{where}: type '{shortName}' is unknown; mapped as Core.Object.");
            return "Core.Object";
        }

        return c;
    }
}
