using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using OpenScct.Core;

namespace OpenScct.Content.Maps;

/// <summary>A map mission of one mode: its objectives (or Disk Hunt disks) and the minimum to take.</summary>
public sealed record MapMissionFacts(EGameMode Mode, int Objectives, int MinimumObjectives, int Disks);

/// <summary>A map's <c>SMapSettings.Modes</c> entry (the values the host menu starts from).</summary>
public sealed record MapModeSettingsFacts(EGameMode Mode, int TimeLimit, int SpiesLives, int SentriesLives, int RespawnTime,
    int ScoreLimit, int Handicap, int NumDisksMax);

/// <summary>
/// What the menus need from one SVMap folder without loading it: the names and legend of its <c>SMapSettings</c>
/// (<c>ui/mapinfo.json</c>), and from <c>sim/gameplay.json</c> its player starts per side, its missions per mode and
/// its per-mode settings. Reads two small JSON files.
/// </summary>
public sealed class MapFacts
{
    private MapFacts()
    {
    }

    /// <summary><c>SMapSettings.MapName</c>, the name the original's menus show, or null.</summary>
    public string? MapName { get; private init; }

    /// <summary><c>SMapSettings.LegendMap</c> (lines split by <c>|</c>), or null.</summary>
    public string? Legend { get; private init; }

    /// <summary>Normal player starts of the spies (TeamNumber 0) and the mercs (1), without the Merc Deathmatch set.</summary>
    public int SpyStarts { get; private init; }

    public int MercStarts { get; private init; }

    /// <summary>Starts with <c>bMercDeathMatchStart</c>.</summary>
    public int MercDeathmatchStarts { get; private init; }

    public IReadOnlyList<MapMissionFacts> Missions { get; private init; } = [];

    public IReadOnlyList<MapModeSettingsFacts> Settings { get; private init; } = [];

    /// <summary>The mission of a mode (the first one), or null.</summary>
    public MapMissionFacts? Mission(EGameMode mode)
    {
        foreach (MapMissionFacts m in Missions)
        {
            if (m.Mode == mode)
            {
                return m;
            }
        }

        return null;
    }

    /// <summary>The settings entry of a mode, or null.</summary>
    public MapModeSettingsFacts? SettingsFor(EGameMode mode)
    {
        foreach (MapModeSettingsFacts s in Settings)
        {
            if (s.Mode == mode)
            {
                return s;
            }
        }

        return null;
    }

    /// <summary>Reads the facts of an SVMap folder (missing files give empty facts).</summary>
    public static MapFacts Load(string folder)
    {
        string info = Path.Combine(folder, "ui", "mapinfo.json");
        string gameplay = Path.Combine(folder, "sim", "gameplay.json");
        return Parse(File.Exists(info) ? File.ReadAllBytes(info) : null, File.Exists(gameplay) ? File.ReadAllBytes(gameplay) : null);
    }

    /// <summary>Parses the two documents (either may be null).</summary>
    public static MapFacts Parse(byte[]? mapInfo, byte[]? gameplay)
    {
        string? name = null;
        string? legend = null;
        if (mapInfo is not null)
        {
            using JsonDocument doc = JsonDocument.Parse(WithoutBom(mapInfo));
            name = OptString(doc.RootElement, "MapName");
            legend = OptString(doc.RootElement, "LegendMap");
        }

        int spy = 0, merc = 0, mdm = 0;
        var missions = new List<MapMissionFacts>();
        var settings = new List<MapModeSettingsFacts>();
        if (gameplay is not null)
        {
            using JsonDocument doc = JsonDocument.Parse(WithoutBom(gameplay));
            JsonElement root = doc.RootElement;
            if (root.TryGetProperty("actors", out JsonElement actors) && actors.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement a in actors.EnumerateArray())
                {
                    string cls = OptString(a, "class") ?? string.Empty;
                    if (!cls.EndsWith("PlayerStart", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    JsonElement props = a.TryGetProperty("props", out JsonElement p) ? p : default;
                    if (props.ValueKind == JsonValueKind.Object && props.TryGetProperty("bMercDeathMatchStart", out JsonElement m) &&
                        m.ValueKind == JsonValueKind.True)
                    {
                        mdm++;
                    }
                    else if (Int(props, "TeamNumber") == 1)
                    {
                        merc++;
                    }
                    else
                    {
                        spy++;
                    }
                }
            }

            if (root.TryGetProperty("missionTree", out JsonElement tree) && tree.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement m in tree.EnumerateArray())
                {
                    missions.Add(new MapMissionFacts((EGameMode)(byte)Int(m, "gameMode"), Count(m, "objectives"), Int(m, "minimumObjectives"),
                        Count(m, "triggers")));
                }
            }

            if (root.TryGetProperty("mapSettings", out JsonElement ms) && ms.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty mode in ms.EnumerateObject())
                {
                    JsonElement v = mode.Value;
                    settings.Add(new MapModeSettingsFacts((EGameMode)(byte)Int(v, "GameModeId"), Int(v, "TimeLimit"), Int(v, "SpiesLives"),
                        Int(v, "SentriesLives"), Int(v, "RespawnTime"), Int(v, "ScoreLimit"), Int(v, "Handicap"), Int(v, "NumDisksMax")));
                }
            }
        }

        return new MapFacts
        {
            MapName = name,
            Legend = legend,
            SpyStarts = spy,
            MercStarts = merc,
            MercDeathmatchStarts = mdm,
            Missions = missions,
            Settings = settings,
        };
    }

    private static ReadOnlyMemory<byte> WithoutBom(byte[] utf8) =>
        utf8.Length >= 3 && utf8[0] == 0xEF && utf8[1] == 0xBB && utf8[2] == 0xBF ? utf8.AsMemory(3) : utf8;

    private static string? OptString(JsonElement obj, string key) =>
        obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(key, out JsonElement v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static int Int(JsonElement obj, string key) =>
        obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(key, out JsonElement v) && v.ValueKind == JsonValueKind.Number &&
        v.TryGetInt32(out int i)
            ? i
            : 0;

    private static int Count(JsonElement obj, string key) =>
        obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(key, out JsonElement v) && v.ValueKind == JsonValueKind.Array
            ? v.GetArrayLength()
            : 0;
}
