using System;
using System.Collections.Generic;
using System.Reflection;
using OpenScct.Provenance;

namespace OpenScct.Core.Uc;

/// <summary>
/// Checks <c>[Uc]</c> attributes against the generated unit manifests: every path must name a known function or
/// state code unit, and a given Sha1 must equal the bytecode hash the manifest holds. Coverage tooling and tests
/// run it over the assemblies that hold ports; a mismatch means the original changed or the attribute is wrong.
/// </summary>
public static class UcManifestCheck
{
    /// <summary>One <c>[Uc]</c> attribute that does not match the manifests.</summary>
    public readonly record struct Problem(string Member, string UnitPath, string Message)
    {
        public override string ToString() => $"{Member}: [Uc(\"{UnitPath}\")] {Message}";
    }

    /// <summary>Every problem found in <paramref name="assemblies"/>, sorted by member and path.</summary>
    public static IReadOnlyList<Problem> Check(UcClassIndex index, params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(index);
        var problems = new List<Problem>();
        foreach (Assembly assembly in assemblies)
        {
            foreach (Type type in assembly.GetTypes())
            {
                CheckMember(index, type, type.FullName ?? type.Name, problems);
                const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance |
                    BindingFlags.Static | BindingFlags.DeclaredOnly;
                foreach (MemberInfo member in type.GetMembers(all))
                {
                    if (member is Type)
                    {
                        continue;
                    }

                    CheckMember(index, member, (type.FullName ?? type.Name) + "." + member.Name, problems);
                }
            }
        }

        problems.Sort(static (a, b) =>
        {
            int c = string.CompareOrdinal(a.Member, b.Member);
            return c != 0 ? c : string.CompareOrdinal(a.UnitPath, b.UnitPath);
        });
        return problems;
    }

    /// <summary>Checks one attribute value; returns null when it matches.</summary>
    public static string? CheckOne(UcClassIndex index, string unitPath, string? sha1)
    {
        ArgumentNullException.ThrowIfNull(index);
        string? expected;
        if (unitPath.EndsWith("@code", StringComparison.Ordinal))
        {
            expected = index.FindStateCode(unitPath)?.Sha1;
        }
        else if (index.FindFunction(unitPath) is { } function)
        {
            expected = function.Sha1;
        }
        else
        {
            // [Uc("Pkg.Class")] on a type names the class itself, [Uc("Pkg.Class.State")] a state class.
            if (index.Find(unitPath) is not null)
            {
                return null;
            }

            int dot = unitPath.LastIndexOf('.');
            UcClassInfo? owner = dot > 0 ? index.Find(unitPath[..dot]) : null;
            return owner?.FindOwnState(unitPath[(dot + 1)..]) is not null
                ? null
                : "names no known function, state code, state or class";
        }

        if (expected is null)
        {
            return "names no known function, state code or class";
        }

        if (sha1 is not null && !string.Equals(sha1, expected, StringComparison.OrdinalIgnoreCase))
        {
            return $"has Sha1 {sha1}, the manifest has {expected}";
        }

        return null;
    }

    private static void CheckMember(UcClassIndex index, MemberInfo member, string memberName, List<Problem> problems)
    {
        foreach (UcAttribute uc in member.GetCustomAttributes<UcAttribute>(inherit: false))
        {
            if (CheckOne(index, uc.UnitPath, uc.Sha1) is { } message)
            {
                problems.Add(new Problem(memberName, uc.UnitPath, message));
            }
        }
    }
}
