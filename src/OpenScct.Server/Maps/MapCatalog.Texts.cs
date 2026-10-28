using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using OpenScct.Core;
using OpenScct.Game.SBase;

namespace OpenScct.Server;

// The map's own words (SMapSettings' localized strings as the converter writes them to ui/mapinfo.json): the
// briefing texts and, per mode, the loading and briefing explanations.
public sealed partial class MapCatalog
{
    private readonly Dictionary<string, MapTexts> _texts = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The words of one map: the map's briefing strings and each mode's strings by mode key (Story, DiskHunt, Deathmatch).</summary>
    private sealed record MapTexts(string LegendMapInBrief, string ExpliMapInBriefSpy, string ExpliMapInBriefSentry,
        IReadOnlyDictionary<string, FGameModeInfoStrings> Modes)
    {
        public static readonly MapTexts Empty = new(string.Empty, string.Empty, string.Empty,
            new Dictionary<string, FGameModeInfoStrings>());
    }

    /// <summary>Puts the map's words into <paramref name="settings"/>: the briefing strings and one ModesStrings entry per Modes entry.</summary>
    private void FillTexts(string folder, USMapSettings settings, IReadOnlyList<string> modeKeys)
    {
        // MapName stays the map's id (the catalog's callers key on it); the shown name comes from MapTitles.
        MapTexts texts = TextsOf(folder);
        settings.LegendMapInBrief = texts.LegendMapInBrief;
        settings.ExpliMapInBriefSpy = texts.ExpliMapInBriefSpy;
        settings.ExpliMapInBriefSentry = texts.ExpliMapInBriefSentry;
        settings.ModesStrings.Clear();
        foreach (string key in modeKeys)
        {
            settings.ModesStrings.Add(texts.Modes.TryGetValue(key, out FGameModeInfoStrings words) ? words : new FGameModeInfoStrings());
        }
    }

    private MapTexts TextsOf(string folder)
    {
        lock (_gate)
        {
            if (_texts.TryGetValue(folder, out MapTexts? cached))
            {
                return cached;
            }
        }

        MapTexts texts = ReadTexts(Path.Combine(folder, "ui", "mapinfo.json"));
        lock (_gate)
        {
            _texts[folder] = texts;
        }

        return texts;
    }

    private static MapTexts ReadTexts(string path)
    {
        if (!File.Exists(path))
        {
            return MapTexts.Empty;
        }

        try
        {
            byte[] bytes = File.ReadAllBytes(path);
            ReadOnlySpan<byte> json = bytes.AsSpan();
            if (json.Length >= 3 && json[0] == 0xEF && json[1] == 0xBB && json[2] == 0xBF)
            {
                json = json[3..];
            }

            using JsonDocument doc = JsonDocument.Parse(json.ToArray());
            JsonElement root = doc.RootElement;
            var modes = new Dictionary<string, FGameModeInfoStrings>(StringComparer.OrdinalIgnoreCase);
            if (root.TryGetProperty("modes", out JsonElement m) && m.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty mode in m.EnumerateObject())
                {
                    JsonElement v = mode.Value;
                    modes[mode.Name] = new FGameModeInfoStrings
                    {
                        ExplicationModeSpy = Text(v, "ExplicationModeSpy"),
                        ExplicationModeSentry = Text(v, "ExplicationModeSentry"),
                        ExpliModeInBriefSpy = Text(v, "ExpliModeInBriefSpy"),
                        ExpliModeInBriefSentry = Text(v, "ExpliModeInBriefSentry"),
                        LegendModeInBriefSpy = Text(v, "LegendModeInBriefSpy"),
                        LegendModeInBriefSentry = Text(v, "LegendModeInBriefSentry"),
                    };
                }
            }

            return new MapTexts(Text(root, "LegendMapInBrief"), Text(root, "ExpliMapInBriefSpy"),
                Text(root, "ExpliMapInBriefSentry"), modes);
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return MapTexts.Empty;
        }
    }

    private static string Text(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? string.Empty
            : string.Empty;
}
