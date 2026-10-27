using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace OpenScct.Core.Uc;

/// <summary>
/// The game's localized text (the <c>.int</c> strings of its script classes), read from the content the player built
/// from their own install: <c>shared/classes.json</c>, whose class defaults hold that text in the chosen language.
/// The repository never holds the text itself. The generated SetDefaults methods ask for each localized string
/// property here (<see cref="Get(string, string, string)"/>) and keep the inherited value when the table has none.
/// <para>
/// The file is found on first use: the path given to <see cref="Use"/> (an installed build: the launcher's content),
/// else the checkout's <c>.content/shared/classes.json</c> above the running program. With neither, every lookup
/// returns its fallback, so localized strings stay empty.
/// </para>
/// </summary>
public static class UcText
{
    public const string RelativePath = "shared/classes.json";

    private static readonly object Gate = new();
    private static string? _path;
    private static Dictionary<string, Dictionary<string, JsonElement>>? _table;

    /// <summary>Sets the classes.json to read (call before the first object is made). A missing file is ignored.</summary>
    public static void Use(string? classesJson)
    {
        lock (Gate)
        {
            _path = classesJson;
            _table = null;
        }
    }

    /// <summary>True when the text was found (tests that need it skip without it, as CI has no content).</summary>
    public static bool HasText => Table().Count > 0;

    /// <summary>The localized value of <paramref name="property"/> in class <paramref name="classPath"/>, or <paramref name="fallback"/>.</summary>
    public static string Get(string classPath, string property, string fallback) =>
        Find(classPath, property) is { ValueKind: JsonValueKind.String } v ? v.GetString() ?? fallback : fallback;

    /// <summary>Element <paramref name="index"/> of a localized static array, or <paramref name="fallback"/>.</summary>
    public static string Get(string classPath, string property, int index, string fallback)
    {
        if (Find(classPath, property) is not { } v)
        {
            return fallback;
        }

        JsonElement e = default;
        bool found = v.ValueKind switch
        {
            JsonValueKind.Object => v.TryGetProperty(index.ToString(System.Globalization.CultureInfo.InvariantCulture), out e),
            JsonValueKind.Array when index < v.GetArrayLength() => (e = v[index]).ValueKind != JsonValueKind.Undefined,
            _ => false,
        };
        return found && e.ValueKind == JsonValueKind.String ? e.GetString() ?? fallback : fallback;
    }

    /// <summary>
    /// Replaces <paramref name="list"/> with a localized dynamic array of strings; leaves it as it is (the inherited
    /// value) when the table has none.
    /// </summary>
    public static void Fill(List<string> list, string classPath, string property)
    {
        ArgumentNullException.ThrowIfNull(list);
        if (Find(classPath, property) is not { ValueKind: JsonValueKind.Array } v)
        {
            return;
        }

        list.Clear();
        foreach (JsonElement e in v.EnumerateArray())
        {
            list.Add(e.ValueKind == JsonValueKind.String ? e.GetString() ?? string.Empty : string.Empty);
        }
    }

    private static JsonElement? Find(string classPath, string property)
    {
        Dictionary<string, Dictionary<string, JsonElement>> table = Table();
        return table.TryGetValue(classPath, out Dictionary<string, JsonElement>? props) && props.TryGetValue(property, out JsonElement v)
            ? v
            : null;
    }

    private static Dictionary<string, Dictionary<string, JsonElement>> Table()
    {
        lock (Gate)
        {
            return _table ??= Load(_path ?? FindInCheckout());
        }
    }

    private static Dictionary<string, Dictionary<string, JsonElement>> Load(string? path)
    {
        var table = new Dictionary<string, Dictionary<string, JsonElement>>(StringComparer.OrdinalIgnoreCase);
        if (path is null || !File.Exists(path))
        {
            return table;
        }

        using JsonDocument doc = JsonDocument.Parse(File.ReadAllBytes(path));
        if (!doc.RootElement.TryGetProperty("classes", out JsonElement classes) || classes.ValueKind != JsonValueKind.Object)
        {
            return table;
        }

        foreach (JsonProperty c in classes.EnumerateObject())
        {
            if (!c.Value.TryGetProperty("defaults", out JsonElement defaults) || defaults.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            // Only the localized properties' values are kept (CPF_Localized, 0x8000).
            var localized = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string name in LocalizedNames(classes, c.Name))
            {
                localized.Add(name);
            }

            var props = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            foreach (JsonProperty p in defaults.EnumerateObject())
            {
                if (localized.Contains(p.Name))
                {
                    props[p.Name] = p.Value.Clone();
                }
            }

            if (props.Count > 0)
            {
                table[c.Name] = props;
            }
        }

        return table;
    }

    /// <summary>The localized properties a class declares or inherits (its super chain's property lists).</summary>
    private static IEnumerable<string> LocalizedNames(JsonElement classes, string classPath)
    {
        string? path = classPath;
        for (int guard = 0; path is not null && guard < 64; guard++)
        {
            if (!classes.TryGetProperty(path, out JsonElement c))
            {
                yield break;
            }

            if (c.TryGetProperty("properties", out JsonElement props) && props.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement p in props.EnumerateArray())
                {
                    if (p.TryGetProperty("flags", out JsonElement f) && f.GetString() is { } flags &&
                        uint.TryParse(flags.AsSpan(2), System.Globalization.NumberStyles.HexNumber, null, out uint bits) &&
                        (bits & 0x8000u) != 0 && p.TryGetProperty("name", out JsonElement n) && n.GetString() is { } name)
                    {
                        yield return name;
                    }
                }
            }

            path = c.TryGetProperty("super", out JsonElement s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null;
        }
    }

    private static string? FindInCheckout()
    {
        for (DirectoryInfo? d = new(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            if (File.Exists(Path.Combine(d.FullName, "OpenSCCT.slnx")))
            {
                return Path.Combine(d.FullName, ".content", RelativePath);
            }
        }

        return null;
    }
}
