using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace OpenScct.Coverage;

/// <summary>A unit id listed under a spec's <c>Units:</c> heading.</summary>
/// <param name="SpecFile">Repository-relative path with forward slashes.</param>
/// <param name="Line">1-based line of the <c>Units:</c> block that lists it.</param>
internal sealed record SpecUnitRef(string Id, string SpecFile, int Line);

/// <summary>
/// Reads the <c>Units:</c> lists of the behaviour specs. A list starts at a line beginning with <c>Units:</c> and
/// runs to the next blank line or heading. It may be inline (<c>Units: a, b</c>) or a bullet list, and may use
/// brace groups (<c>uc:SBase.SPlayerController:s_Grab.{BeginState, AnimEnd}</c>) and notes in parentheses.
/// </summary>
internal static partial class SpecUnits
{
    public static IReadOnlyList<SpecUnitRef> ReadAll(string repoRoot)
    {
        string specRoot = Path.Combine(repoRoot, "spec");
        if (!Directory.Exists(specRoot))
        {
            return [];
        }

        var refs = new List<SpecUnitRef>();
        foreach (string file in Directory.EnumerateFiles(specRoot, "*.md", SearchOption.AllDirectories)
                     .Select(f => (Full: f, Rel: Relative(repoRoot, f)))
                     .OrderBy(f => f.Rel, StringComparer.Ordinal)
                     .Select(f => f.Full))
        {
            string rel = Relative(repoRoot, file);
            foreach ((string id, int line) in Parse(File.ReadAllText(file)))
            {
                refs.Add(new SpecUnitRef(id, rel, line));
            }
        }

        return refs;
    }

    /// <summary>Unit ids of every <c>Units:</c> block in a spec's text, with the block's line.</summary>
    public static IEnumerable<(string Id, int Line)> Parse(string text)
    {
        string[] lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            if (!lines[i].StartsWith("Units:", StringComparison.Ordinal))
            {
                continue;
            }

            var block = new List<string> { lines[i]["Units:".Length..] };
            int j = i + 1;
            while (j < lines.Length && lines[j].Trim().Length > 0 && !lines[j].StartsWith('#'))
            {
                block.Add(lines[j]);
                j++;
            }

            string expanded = ExpandBraces(string.Join(" ", block).Replace('`', ' '));
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (Match m in IdPattern().Matches(expanded))
            {
                string id = m.Value.TrimEnd('.', ':');
                if (id.Contains('*', StringComparison.Ordinal) || !seen.Add(id))
                {
                    continue;
                }

                yield return (id, i + 1);
            }

            i = j - 1;
        }
    }

    /// <summary>Expands <c>prefix.{a, b}suffix</c> into <c>prefix.a suffix prefix.b suffix</c>, innermost first.</summary>
    public static string ExpandBraces(string text)
    {
        string current = text;
        for (int guard = 0; guard < 64; guard++)
        {
            string next = BracePattern().Replace(current, m =>
            {
                string prefix = m.Groups[1].Value;
                string suffix = m.Groups[3].Value;
                IEnumerable<string> alts = m.Groups[2].Value.Split(',')
                    .Select(a => a.Trim())
                    .Where(a => a.Length > 0)
                    .Select(a => prefix + a + suffix);
                return " " + string.Join(" ", alts) + " ";
            });
            if (string.Equals(next, current, StringComparison.Ordinal))
            {
                return next;
            }

            current = next;
        }

        return current;
    }

    internal static string Relative(string root, string path) =>
        Path.GetRelativePath(root, path).Replace('\\', '/');

    [GeneratedRegex(@"(?<![A-Za-z0-9_])(?:uc|nat|eng|rep|def|fmt|rl):[A-Za-z0-9_.:@*]+", RegexOptions.CultureInvariant)]
    private static partial Regex IdPattern();

    [GeneratedRegex(@"([^\s{}(),]*)\{([^{}]*)\}([A-Za-z0-9_.:@]*)", RegexOptions.CultureInvariant)]
    private static partial Regex BracePattern();
}
