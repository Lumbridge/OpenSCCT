using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace OpenScct.Content.Maps;

/// <summary>
/// One translation of a map's client texts, <c>ui/text/&lt;lang&gt;.json</c> (SVMap 1.2, spec/svmap/1.2/README.md):
/// identity texts (name, description, dossier lines, place names) and <c>ui/mapinfo.json</c> strings (the legends, the
/// briefing explanations, the per-mode texts). Each value overrides the map's base text and anything missing keeps it.
/// Reading never throws: a missing or broken file gives <see cref="None"/>. Actor strings are not read yet.
/// </summary>
public sealed class MapText
{
    public const string Folder = "ui/text";

    /// <summary>The <c>ui/mapinfo.json</c> strings a translation may replace (never <c>MapName</c>).</summary>
    public static readonly IReadOnlyList<string> MapInfoKeys = ["LegendMap", "LegendMapInBrief", "ExpliMapInBriefSpy", "ExpliMapInBriefSentry"];

    private static readonly Dictionary<string, string> NoStrings = new(StringComparer.Ordinal);

    /// <summary>No translation (declared after <see cref="NoStrings"/>, which it uses).</summary>
    public static readonly MapText None = new();

    private MapText()
    {
    }

    public string? Name { get; private init; }

    public string? Description { get; private init; }

    public string? When { get; private init; }

    public string? Where { get; private init; }

    public string? Summary { get; private init; }

    /// <summary>Place names by place number.</summary>
    public IReadOnlyDictionary<int, string> Places { get; private init; } = new Dictionary<int, string>();

    /// <summary>Translated <see cref="MapInfoKeys"/> strings.</summary>
    public IReadOnlyDictionary<string, string> MapInfo { get; private init; } = NoStrings;

    /// <summary>Per mode key (Story, DiskHunt...), the translated mode strings by name.</summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Modes { get; private init; } =
        new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

    /// <summary>A mapinfo string: the translation's, else <paramref name="fallback"/>.</summary>
    public string MapInfoOr(string key, string fallback) => MapInfo.TryGetValue(key, out string? text) ? text : fallback;

    /// <summary>A mode string: the translation's, else <paramref name="fallback"/>.</summary>
    public string ModeOr(string mode, string key, string fallback) =>
        Modes.TryGetValue(mode, out IReadOnlyDictionary<string, string>? strings) && strings.TryGetValue(key, out string? text) ? text : fallback;

    /// <summary>
    /// The translation of an SVMap folder into <paramref name="language"/> (a BCP 47 primary tag), or <see cref="None"/>
    /// when the map has none (a map in that language needs none either).
    /// </summary>
    public static MapText Load(string mapFolder, string? language)
    {
        if (string.IsNullOrEmpty(language) || !MapIdentity.IsLanguage(language))
        {
            return None;
        }

        string path = Path.Combine(mapFolder, "ui", "text", language + ".json");
        try
        {
            return File.Exists(path) ? Parse(File.ReadAllBytes(path)) : None;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return None;
        }
    }

    public static MapText Parse(byte[] utf8)
    {
        ArgumentNullException.ThrowIfNull(utf8);
        try
        {
            ReadOnlyMemory<byte> json = utf8.Length >= 3 && utf8[0] == 0xEF && utf8[1] == 0xBB && utf8[2] == 0xBF ? utf8.AsMemory(3) : utf8;
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return None;
            }

            JsonElement identity = Child(root, "identity");
            JsonElement dossier = Child(identity, "dossier");
            JsonElement mapInfo = Child(root, "mapinfo");
            return new MapText
            {
                Name = Text(identity, "name", MapIdentity.MaxNameLength),
                Description = Text(identity, "description", MapIdentity.MaxDescriptionLength),
                When = Text(dossier, "when", 64),
                Where = Text(dossier, "where", 64),
                Summary = Text(dossier, "summary", MapIdentity.MaxDescriptionLength),
                Places = PlacesOf(Child(identity, "places")),
                MapInfo = Strings(mapInfo, MapInfoKeys),
                Modes = ModesOf(Child(mapInfo, "modes")),
            };
        }
        catch (JsonException)
        {
            return None;
        }
    }

    private static Dictionary<int, string> PlacesOf(JsonElement places)
    {
        var names = new Dictionary<int, string>();
        if (places.ValueKind != JsonValueKind.Object)
        {
            return names;
        }

        foreach (JsonProperty p in places.EnumerateObject())
        {
            if (int.TryParse(p.Name, NumberStyles.None, CultureInfo.InvariantCulture, out int number) && number is >= 1 and <= 99 &&
                Text(places, p.Name, MapIdentity.MaxNameLength) is { } name)
            {
                names[number] = name;
            }
        }

        return names;
    }

    private static Dictionary<string, IReadOnlyDictionary<string, string>> ModesOf(JsonElement modes)
    {
        var all = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        if (modes.ValueKind != JsonValueKind.Object)
        {
            return all;
        }

        foreach (JsonProperty mode in modes.EnumerateObject())
        {
            if (mode.Value.ValueKind == JsonValueKind.Object)
            {
                var strings = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (JsonProperty s in mode.Value.EnumerateObject())
                {
                    if (s.Value.ValueKind == JsonValueKind.String && s.Value.GetString() is { Length: > 0 } text)
                    {
                        strings[s.Name] = text;
                    }
                }

                all[mode.Name] = strings;
            }
        }

        return all;
    }

    private static Dictionary<string, string> Strings(JsonElement obj, IReadOnlyList<string> keys)
    {
        var strings = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string key in keys)
        {
            if (obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(key, out JsonElement v) && v.ValueKind == JsonValueKind.String &&
                v.GetString() is { Length: > 0 } text)
            {
                strings[key] = text;
            }
        }

        return strings;
    }

    private static JsonElement Child(JsonElement obj, string key) =>
        obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(key, out JsonElement v) && v.ValueKind == JsonValueKind.Object ? v : default;

    private static string? Text(JsonElement obj, string key, int max)
    {
        if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty(key, out JsonElement v) || v.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        string text = (v.GetString() ?? string.Empty).Trim();
        if (text.Length > max)
        {
            text = text[..max].TrimEnd();
        }

        return text.Length == 0 ? null : text;
    }
}
