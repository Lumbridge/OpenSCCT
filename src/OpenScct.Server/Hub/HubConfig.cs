using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Server.Online;

namespace OpenScct.Server.Hub;

/// <summary>
/// The match one pad queues for (an OpenSCCT addition, spec/additions/hub.md "Match pads"): the map (its code), the mode
/// (checked against the map catalog's <see cref="MapCatalogEntry.Modes"/>), the players on each side (Merc Deathmatch:
/// the players of the free-for-all, all mercs), whether bots fill the places nobody took, and how many people must
/// stand on the pad before its countdown starts.
/// </summary>
public sealed record HubPadConfig
{
    public required string Id { get; init; }

    public required string Map { get; init; }

    public EGameMode Mode { get; init; } = EGameMode.GM_Multi;

    /// <summary>Players on each side; Merc Deathmatch: players in all.</summary>
    public int TeamSize { get; init; } = 2;

    public BotFill Bots { get; init; } = BotFill.Off;

    /// <summary>People needed on the pad for the countdown (0: a full lineup). Only below full when bots fill the rest.</summary>
    public int MinPlayers { get; init; }

    /// <summary>The map's name as the menus show it, or the code when the catalog does not know it.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>False when the server cannot run the match (the map is not installed): the pad says so and queues nobody.</summary>
    public bool Playable { get; init; } = true;

    /// <summary>
    /// The rest of the match as the pad's laptop set it (time limit, lives, respawn, disks, voice, alternate rules,
    /// bot skill, practice): a lobby's Session Parameters. Null: the map's defaults (hub.json's pads).
    /// </summary>
    public LobbySetup? Setup { get; init; }

    public bool MercsOnly => Mode == EGameMode.GM_MercDeathMatch;

    /// <summary>Places on the spies' side.</summary>
    public int SpySlots => MercsOnly ? 0 : TeamSize;

    /// <summary>Places on the mercs' side.</summary>
    public int MercSlots => TeamSize;

    public int Slots => SpySlots + MercSlots;

    /// <summary>People that start the countdown.</summary>
    public int Needed => Bots == BotFill.Off || MinPlayers <= 0 ? Slots : Math.Clamp(MinPlayers, 1, Slots);

    /// <summary>"Story 2v2", "Merc Deathmatch 4 players".</summary>
    public string Format => MercsOnly
        ? string.Create(CultureInfo.InvariantCulture, $"{MapCatalog.ModeName(Mode)} {TeamSize} players")
        : string.Create(CultureInfo.InvariantCulture, $"{MapCatalog.ModeName(Mode)} {TeamSize}v{TeamSize}");
}

/// <summary>Where a pad is on the hub map: its floor rectangle (UE x, y), the floor height and the sign's anchor.</summary>
public sealed record HubPadArea(string Id, float LoX, float LoY, float HiX, float HiY, float FloorZ, FVector? SignAnchor, HubSignFace SignFace = default)
{
    /// <summary>A pawn location counts as on the pad up to this far above the floor (a jump stays on the pad).</summary>
    public const float StandHeight = 400f;

    /// <summary>Height of the computed sign anchor over the floor when the map gives none (uu).</summary>
    public const float DefaultSignHeight = 360f;

    public FVector Centre => new((LoX + HiX) / 2f, (LoY + HiY) / 2f, FloorZ);

    /// <summary>The map's sign anchor, else the pad's centre at <see cref="DefaultSignHeight"/>.</summary>
    public FVector Anchor => SignAnchor ?? Centre + new FVector(0f, 0f, DefaultSignHeight);

    /// <summary>
    /// Where a player coming back from this pad's match is put: just off the pad's south edge, at standing height. The
    /// spots start 0.3 of the width in from the west edge, so the nearest stands about 130 uu east of the laptop's
    /// solid desk (<see cref="Laptop"/>; the desk reaches 176 in).
    /// </summary>
    public FVector ReturnPoint(int index)
    {
        float width = HiX - LoX;
        float x = LoX + width * (0.3f + 0.15f * (index % 5));
        return new FVector(x, LoY - 160f - 96f * (index / 5 % 3), FloorZ + 86f);
    }

    /// <summary>
    /// Where the pad's laptop stands (its desk's foot): outside the south rail, west of the rail's gap, clear of the
    /// spots players come back to (<see cref="ReturnPoint"/>). The laptop's screen faces south, to the walkway. The
    /// desk (128 x 64, 72 high, centred here) is solid: the hub map's collision has it (tools/hubmap/layout.py
    /// "{pad}Desk", the same numbers; keep the two in step).
    /// </summary>
    public FVector Laptop => new(LoX + 112f, LoY - 112f, FloorZ);

    public bool Contains(FVector location) =>
        location.X >= LoX && location.X <= HiX && location.Y >= LoY && location.Y <= HiY &&
        location.Z >= FloorZ - 64f && location.Z <= FloorZ + StandHeight;
}

/// <summary>
/// The hub's data (spec/additions/hub.md "Match pads"): the pads' matches from the hub config file
/// (<c>fixtures/hubmap/hub.json</c>, or <see cref="Default"/> when there is none), their places from the map's area
/// manifest (<c>fixtures/hubmap/areas.json</c>, the <c>MatchPads</c> area: each pad's <c>lo</c>, <c>hi</c> and
/// optional <c>signAnchor</c> and <c>floorZ</c>), the match countdown and the UDP ports the matches may use.
/// </summary>
public sealed record HubConfig
{
    public const string FileName = "hub.json";
    public const string AreasFileName = "areas.json";

    /// <summary>The folder next to the hub map that holds both files.</summary>
    public const string FolderName = "hubmap";

    /// <summary>Seconds a full pad counts down before its match starts.</summary>
    public float CountdownSeconds { get; init; } = 10f;

    /// <summary>The first UDP port a pad match may listen on (the hub's default is 28886, the game's 28888).</summary>
    public int MatchPortFirst { get; init; } = 28890;

    public int MatchPortLast { get; init; } = 28897;

    /// <summary>Spectator places in each pad match (players in the hub who walk up and watch).</summary>
    public int Spectators { get; init; } = 4;

    /// <summary>The address players connect to for a match, or null: the address they reached the hub at.</summary>
    public string? MatchHost { get; init; }

    public IReadOnlyList<HubPadConfig> Pads { get; init; } = [];

    public IReadOnlyList<HubPadArea> Areas { get; init; } = [];

    /// <summary>Where the config came from (logged).</summary>
    public string Source { get; init; } = "built in";

    /// <summary>The area of <paramref name="padId"/>, or null.</summary>
    public HubPadArea? Area(string padId) => Areas.FirstOrDefault(a => string.Equals(a.Id, padId, StringComparison.OrdinalIgnoreCase));

    /// <summary>The pads as the generated map lays them out (fixtures/hubmap/areas.json of tools.hubmap 1).</summary>
    public static IReadOnlyList<HubPadArea> DefaultAreas { get; } =
    [
        new("Pad1", 9216f, 4608f, 10240f, 5632f, 0f, null),
        new("Pad2", 10496f, 4608f, 11520f, 5632f, 0f, null),
        new("Pad3", 9216f, 6272f, 10240f, 7296f, 0f, null),
        new("Pad4", 10496f, 6272f, 11520f, 7296f, 0f, null),
    ];

    /// <summary>The pads' matches when no config file is found.</summary>
    public static IReadOnlyList<HubPadConfig> DefaultPads { get; } =
    [
        new() { Id = "Pad1", Map = "SteeE", Mode = EGameMode.GM_Multi, TeamSize = 2 },
        new() { Id = "Pad2", Map = "PolaE", Mode = EGameMode.GM_DeathMatch, TeamSize = 2 },
        new() { Id = "Pad3", Map = "MuseT", Mode = EGameMode.GM_Multi, TeamSize = 3, Bots = BotFill.Fill, MinPlayers = 2 },
        new() { Id = "Pad4", Map = "ClubE", Mode = EGameMode.GM_MercDeathMatch, TeamSize = 4, Bots = BotFill.Fill, MinPlayers = 2 },
    ];

    public static HubConfig Default { get; } = new() { Pads = DefaultPads, Areas = DefaultAreas };

    /// <summary>
    /// The config of <paramref name="path"/> (a hub.json), else the first <c>hubmap/hub.json</c> under the catalog's
    /// roots, else <see cref="Default"/>; the areas from the <c>areas.json</c> beside it (else the default layout).
    /// Each pad is checked against <paramref name="maps"/>: a mode the map does not play becomes the map's default
    /// mode, and a map the server does not have makes the pad unplayable. <paramref name="log"/> gets each fix.
    /// </summary>
    public static HubConfig Load(MapCatalog? maps, string? path = null, Action<string>? log = null)
    {
        string? file = path is { Length: > 0 } ? path : Find(maps, FileName);
        HubConfig config = Default;
        if (file is not null && File.Exists(file))
        {
            config = Parse(File.ReadAllText(file)) with { Source = file };
        }

        string? areasFile = file is not null && File.Exists(Path.Combine(Path.GetDirectoryName(file) ?? ".", AreasFileName))
            ? Path.Combine(Path.GetDirectoryName(file) ?? ".", AreasFileName)
            : Find(maps, AreasFileName);
        if (areasFile is not null && ParseAreas(File.ReadAllText(areasFile)) is { Count: > 0 } areas)
        {
            config = config with { Areas = areas };
        }

        return maps is null ? config : config.Checked(maps, log);
    }

    /// <summary>The pads with their titles, modes fixed to what the map plays, and pads without an area or a map turned off.</summary>
    public HubConfig Checked(MapCatalog maps, Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(maps);
        var pads = new List<HubPadConfig>();
        foreach (HubPadConfig pad in Pads)
        {
            HubPadConfig p = pad with { TeamSize = Math.Clamp(pad.TeamSize, 1, pad.Mode == EGameMode.GM_MercDeathMatch ? 8 : 4) };
            if (Area(p.Id) is null)
            {
                log?.Invoke($"{p.Id}: the hub map has no pad of that name; skipped");
                continue;
            }

            MapCatalogEntry? entry = maps.FindMap(p.Map) is null ? null : maps.GetCatalogEntry(p.Map);
            if (entry is null)
            {
                log?.Invoke($"{p.Id}: map {p.Map} is not installed on this server; the pad is off");
                pads.Add(p with { Title = p.Map, Playable = false });
                continue;
            }

            EGameMode mode = p.Mode;
            if (!entry.PlayableModes.Contains(mode))
            {
                log?.Invoke($"{p.Id}: {entry.Name} does not play {MapCatalog.ModeName(mode)}; using {MapCatalog.ModeName(entry.DefaultMode)}");
                mode = entry.DefaultMode;
            }

            pads.Add(p with { Mode = mode, Title = entry.Name, Map = entry.Id, Setup = p.Setup is { } setup ? setup with { Map = entry.Id, Mode = mode } : null });
        }

        return this with { Pads = pads };
    }

    private static string? Find(MapCatalog? maps, string name)
    {
        var roots = new List<string>();
        if (maps is not null)
        {
            if (maps.FindFolder(HubK.MapName) is { } folder && Path.GetDirectoryName(folder) is { } parent)
            {
                roots.Add(parent);
            }

            roots.AddRange(maps.Roots);
        }

        // An installed build ships hubmap/ beside the hub in maps/ next to the exe.
        roots.AddRange(ContentPaths.BuiltInMapRoots);
        if (MapCatalog.FindRepoRoot(AppContext.BaseDirectory) is { } repo)
        {
            roots.Add(Path.Combine(repo, "fixtures"));
        }

        foreach (string root in roots)
        {
            string candidate = Path.Combine(root, FolderName, name);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>Reads a hub.json (see fixtures/hubmap/hub.json); missing fields keep the defaults.</summary>
    public static HubConfig Parse(string json)
    {
        using JsonDocument doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        JsonElement root = doc.RootElement;
        HubConfig config = Default;
        if (root.TryGetProperty("countdownSeconds", out JsonElement cd) && cd.TryGetDouble(out double seconds))
        {
            config = config with { CountdownSeconds = (float)Math.Clamp(seconds, 1.0, 60.0) };
        }

        if (root.TryGetProperty("matchPorts", out JsonElement ports) && ports.ValueKind == JsonValueKind.Array && ports.GetArrayLength() == 2 &&
            ports[0].TryGetInt32(out int first) && ports[1].TryGetInt32(out int last) && first is > 0 and <= 65535 && last >= first && last <= 65535)
        {
            config = config with { MatchPortFirst = first, MatchPortLast = last };
        }

        if (root.TryGetProperty("spectators", out JsonElement spec) && spec.TryGetInt32(out int spectators))
        {
            config = config with { Spectators = Math.Clamp(spectators, 0, 16) };
        }

        if (root.TryGetProperty("matchHost", out JsonElement host) && host.ValueKind == JsonValueKind.String && host.GetString() is { Length: > 0 } h)
        {
            config = config with { MatchHost = h };
        }

        if (root.TryGetProperty("pads", out JsonElement padsEl) && padsEl.ValueKind == JsonValueKind.Array)
        {
            var pads = new List<HubPadConfig>();
            foreach (JsonElement p in padsEl.EnumerateArray())
            {
                string id = Str(p, "id");
                string map = Str(p, "map");
                if (id.Length == 0 || map.Length == 0)
                {
                    continue;
                }

                LobbySetup.TryParseMode(Str(p, "mode"), out EGameMode mode);
                pads.Add(new HubPadConfig
                {
                    Id = id,
                    Map = map,
                    Mode = mode == EGameMode.GM_Undefined ? EGameMode.GM_Multi : mode,
                    TeamSize = Int(p, "teamSize", 2),
                    Bots = Str(p, "bots").ToUpperInvariant() switch
                    {
                        "FILL" => BotFill.Fill,
                        _ => BotFill.Off,
                    },
                    MinPlayers = Int(p, "minPlayers", 0),
                });
            }

            config = config with { Pads = pads };
        }

        return config;
    }

    /// <summary>The pads of an areas.json (the <c>matchPads</c> area's <c>pads</c>).</summary>
    public static List<HubPadArea> ParseAreas(string json)
    {
        var list = new List<HubPadArea>();
        using JsonDocument doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("areas", out JsonElement areas) || areas.ValueKind != JsonValueKind.Array)
        {
            return list;
        }

        foreach (JsonElement area in areas.EnumerateArray())
        {
            if (Str(area, "kind") != "matchPads" || !area.TryGetProperty("pads", out JsonElement pads) || pads.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            float areaFloor = Float(area, "floorZ", 0f);
            foreach (JsonElement pad in pads.EnumerateArray())
            {
                if (Str(pad, "id") is not { Length: > 0 } id || !TryPair(pad, "lo", out float lx, out float ly) || !TryPair(pad, "hi", out float hx, out float hy))
                {
                    continue;
                }

                FVector? anchor = null;
                if (pad.TryGetProperty("signAnchor", out JsonElement a) && a.ValueKind == JsonValueKind.Array && a.GetArrayLength() == 3)
                {
                    anchor = new FVector((float)a[0].GetDouble(), (float)a[1].GetDouble(), (float)a[2].GetDouble());
                }

                // The sign gantry's board (the map's signYaw and signSize), so the sign's text lies on its face.
                HubSignFace face = HubSignFace.None;
                if (anchor is not null && TryPair(pad, "signSize", out float sw, out float sh))
                {
                    face = new HubSignFace(Float(pad, "signYaw", -1f), sw, sh);
                }

                list.Add(new HubPadArea(id, Math.Min(lx, hx), Math.Min(ly, hy), Math.Max(lx, hx), Math.Max(ly, hy), Float(pad, "floorZ", areaFloor), anchor, face));
            }
        }

        return list;
    }

    private static bool TryPair(JsonElement e, string name, out float x, out float y)
    {
        x = y = 0f;
        if (!e.TryGetProperty(name, out JsonElement v) || v.ValueKind != JsonValueKind.Array || v.GetArrayLength() < 2)
        {
            return false;
        }

        x = (float)v[0].GetDouble();
        y = (float)v[1].GetDouble();
        return true;
    }

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? string.Empty : string.Empty;

    private static int Int(JsonElement e, string name, int fallback) =>
        e.TryGetProperty(name, out JsonElement v) && v.TryGetInt32(out int i) ? i : fallback;

    private static float Float(JsonElement e, string name, float fallback) =>
        e.TryGetProperty(name, out JsonElement v) && v.TryGetDouble(out double d) ? (float)d : fallback;
}
