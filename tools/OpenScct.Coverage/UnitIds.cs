using System;
using System.Text.RegularExpressions;

namespace OpenScct.Coverage;

/// <summary>A unit id read from an attribute, with what went wrong when it could not be used as is.</summary>
/// <param name="Id">The coverage id, or null when the source names no unit (a class or state marker) or is wrong.</param>
/// <param name="Warning">Set when the id was usable after a correction, or names nothing that has weight.</param>
/// <param name="Error">Set when the id is wrong: CI should fail on it.</param>
internal readonly record struct Resolved(string? Id, string? Warning = null, string? Error = null);

/// <summary>Turns the paths of <c>[Uc]</c>, <c>[UcNative]</c> and <c>[PortOf]</c> into coverage ids.</summary>
internal static class UnitIds
{
    /// <summary>
    /// A <c>[Uc]</c> path, <c>[Pkg.]Class[.State].Function</c> or <c>[Pkg.]Class.State@code</c>, as a unit id
    /// (<c>uc:Pkg.Class:Function</c>, <c>uc:Pkg.Class:State.Function</c>, <c>uc:Pkg.Class:State@code</c>, or
    /// <c>nat:Pkg.Class.Function</c> for a native). A path naming a class or a state is a marker with no unit.
    /// </summary>
    public static Resolved FromUcPath(Inventory inv, string path)
    {
        bool code = path.EndsWith("@code", StringComparison.Ordinal);
        string core = code ? path[..^"@code".Length] : path;
        string[] segs = core.Split('.');
        if (segs.Length == 0 || segs[0].Length == 0)
        {
            return new Resolved(null, Error: $"[Uc(\"{path}\")] is empty");
        }

        string cls;
        string[] rest;
        if (segs.Length >= 2 && Inventory.IsPackage(segs[0]) && inv.HasClass(segs[0] + "." + segs[1]))
        {
            cls = segs[0] + "." + segs[1];
            rest = segs[2..];
        }
        else
        {
            var named = inv.ClassesNamed(segs[0]);
            if (named.Count == 0)
            {
                return new Resolved(null, Error: $"[Uc(\"{path}\")]: no class {segs[0]} in the inventory");
            }

            if (named.Count > 1)
            {
                return new Resolved(null, Error: $"[Uc(\"{path}\")]: class {segs[0]} is in several packages; add the package");
            }

            cls = named[0];
            rest = segs[1..];
        }

        if (code)
        {
            if (rest.Length != 1)
            {
                return new Resolved(null, Error: $"[Uc(\"{path}\")]: state code needs Class.State@code");
            }

            string id = $"uc:{cls}:{rest[0]}@code";
            return inv.Contains(id) ? new Resolved(id) : new Resolved(null, Error: $"[Uc(\"{path}\")]: {id} is not in the inventory");
        }

        switch (rest.Length)
        {
            case 0:
                return new Resolved(null);
            case 1:
            {
                string f = rest[0];
                if (inv.Contains($"uc:{cls}:{f}"))
                {
                    return new Resolved($"uc:{cls}:{f}");
                }

                if (inv.Contains($"nat:{cls}.{f}"))
                {
                    return new Resolved($"nat:{cls}.{f}");
                }

                if (inv.HasState(cls, f))
                {
                    return new Resolved(null);
                }

                return new Resolved(null, Error: $"[Uc(\"{path}\")]: {cls} has no function or state {f} in the inventory");
            }

            case 2:
            {
                string id = $"uc:{cls}:{rest[0]}.{rest[1]}";
                return inv.Contains(id) ? new Resolved(id) : new Resolved(null, Error: $"[Uc(\"{path}\")]: {id} is not in the inventory");
            }

            default:
                return new Resolved(null, Error: $"[Uc(\"{path}\")]: too many segments");
        }
    }

    /// <summary>
    /// A <c>[UcNative]</c> name. An exec thunk of a script native (<c>AActor::execPlayAnim</c>) is the inventory's
    /// <c>nat:Engine.Actor.PlayAnim</c>; any other engine function is the engine unit <c>eng:AActor::Tick</c>.
    /// </summary>
    public static string FromUcNative(Inventory inv, string name)
    {
        int sep = name.IndexOf("::", StringComparison.Ordinal);
        if (sep < 0)
        {
            return inv.Contains("nat:" + name) ? "nat:" + name : "eng:" + name;
        }

        string cpp = name[..sep];
        string method = name[(sep + 2)..];
        if (method.StartsWith("exec", StringComparison.Ordinal) && method.Length > 4)
        {
            string func = method[4..];
            foreach (string candidate in CppClassCandidates(cpp))
            {
                foreach (string cls in inv.ClassesNamed(candidate))
                {
                    if (inv.Contains($"nat:{cls}.{func}"))
                    {
                        return $"nat:{cls}.{func}";
                    }
                }
            }
        }

        return "eng:" + name;
    }

    /// <summary>A <c>[PortOf]</c>, scenario or status file id, checked against the inventory.</summary>
    public static Resolved Normalize(Inventory inv, string id, string where)
    {
        int colon = id.IndexOf(':', StringComparison.Ordinal);
        if (colon <= 0)
        {
            return new Resolved(null, Error: $"{where}: \"{id}\" has no kind prefix");
        }

        string kind = id[..colon];
        string body = id[(colon + 1)..];
        switch (kind)
        {
            case "uc":
            case "rep":
            case "def":
            case "nat":
                if (inv.Contains(id))
                {
                    return new Resolved(id);
                }

                if (kind == "nat" && body.Contains("::", StringComparison.Ordinal))
                {
                    string mapped = FromUcNative(inv, body);
                    return new Resolved(mapped, Warning: $"{where}: {id} names engine code; counted as {mapped} (use eng: for engine functions)");
                }

                return new Resolved(null, Error: $"{where}: {id} is not in the inventory");
            case "eng":
            case "fmt":
            case "rl":
                return body.Length > 0 ? new Resolved(id) : new Resolved(null, Error: $"{where}: \"{id}\" is empty");
            default:
                return new Resolved(null, Error: $"{where}: \"{id}\" has an unknown kind {kind}:");
        }
    }

    private static string[] CppClassCandidates(string cpp) =>
        cpp.Length > 1 && (cpp[0] == 'A' || cpp[0] == 'U') && char.IsUpper(cpp[1]) ? [cpp[1..], cpp] : [cpp];
}

/// <summary>Glob over unit ids: <c>*</c> matches any run of characters, including dots and colons.</summary>
internal sealed class IdGlob
{
    private readonly Regex _regex;

    public IdGlob(string pattern)
    {
        Pattern = pattern;
        _regex = new Regex("^" + Regex.Escape(pattern).Replace("\\*", ".*", StringComparison.Ordinal) + "$",
            RegexOptions.CultureInvariant | RegexOptions.Singleline);
    }

    public string Pattern { get; }

    public bool IsMatch(string id) => _regex.IsMatch(id);
}
