using System;
using System.Collections.Generic;
using System.Text;

namespace OpenScct.CodeGen;

/// <summary>What the generator decided and noticed, grouped by section, sorted for stable output.</summary>
internal sealed class Report
{
    private readonly SortedDictionary<string, SortedSet<string>> _sections = new(StringComparer.Ordinal);
    private readonly SortedDictionary<string, long> _counts = new(StringComparer.Ordinal);

    public void Add(string section, string line)
    {
        if (!_sections.TryGetValue(section, out SortedSet<string>? lines))
        {
            _sections[section] = lines = new SortedSet<string>(StringComparer.Ordinal);
        }

        lines.Add(line);
    }

    public void Count(string name, long delta = 1)
    {
        _counts[name] = _counts.GetValueOrDefault(name) + delta;
    }

    public long GetCount(string name) => _counts.GetValueOrDefault(name);

    public IReadOnlyCollection<string> Section(string section) =>
        _sections.TryGetValue(section, out SortedSet<string>? lines) ? lines : [];

    public IEnumerable<string> SectionNames => _sections.Keys;

    public string ToMarkdown()
    {
        var sb = new StringBuilder();
        sb.Append("# OpenScct.CodeGen report\n\n## Counts\n\n");
        foreach ((string name, long value) in _counts)
        {
            sb.Append("- ").Append(name).Append(": ").Append(value).Append('\n');
        }

        foreach ((string section, SortedSet<string> lines) in _sections)
        {
            sb.Append("\n## ").Append(section).Append(" (").Append(lines.Count).Append(")\n\n");
            foreach (string line in lines)
            {
                sb.Append("- ").Append(line).Append('\n');
            }
        }

        return sb.ToString();
    }
}
