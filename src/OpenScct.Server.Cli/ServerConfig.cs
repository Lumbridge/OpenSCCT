using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenScct.Core;
using OpenScct.Net.Discovery;
using OpenScct.Server.Online;

namespace OpenScct.Server.Cli;

/// <summary>One match of the rotation: a map and mode, with optional changes to the map's own settings.</summary>
internal sealed record RotationEntry
{
    public string Map { get; init; } = "SteeE";

    /// <summary>Story, DiskHunt, Deathmatch or MercDeathmatch.</summary>
    public string Mode { get; init; } = "Story";

    public int? TimeLimit { get; init; }

    public int? SpyLives { get; init; }

    public int? MercLives { get; init; }

    public int? Respawn { get; init; }

    public int? KillLimit { get; init; }

    /// <summary>Disk Hunt: active disks and disks to take.</summary>
    public int? Disks { get; init; }

    public int? DisksToWin { get; init; }

    public bool? Revenge { get; init; }

    public int? GamesInARow { get; init; }
}

/// <summary>
/// Alternate Mode for every match of the rotation (docs/ONLINE.md "Dedicated server"): abilities left out keep the
/// stock value; gadgets are the side's gear numbers 1 to 8 that may be taken (left out: all eight).
/// </summary>
internal sealed record AlternateConfig
{
    public bool On { get; init; } = true;

    public bool? SpyThermalVision { get; init; }

    public bool? SpyNightVision { get; init; }

    public bool? SpyGrab { get; init; }

    public bool? SpyElbow { get; init; }

    public bool? SpyLethalDrop { get; init; }

    public bool? MercEmfVision { get; init; }

    public bool? MercMotionTracking { get; init; }

    public bool? MercElbow { get; init; }

    public bool? MercLethalDrop { get; init; }

    public bool? MercTorch { get; init; }

    public bool? MercLaser { get; init; }

    public bool? MercBerserk { get; init; }

    public bool? MercCharge { get; init; }

    public bool? CinematicCamera { get; init; }

    public List<int>? SpyGadgets { get; init; }

    public int? SpySlots { get; init; }

    public List<int>? MercGadgets { get; init; }

    public int? MercSlots { get; init; }

    /// <summary>The rules these values make, from the stock ones.</summary>
    public AlternateRules ToRules()
    {
        AlternateRules r = AlternateRules.Default with { On = On };
        void Set(bool? value, AlternateAbility ability)
        {
            if (value is { } v)
            {
                r = r.With(ability, v);
            }
        }

        Set(SpyThermalVision, AlternateAbility.SpyThermalVision);
        Set(SpyNightVision, AlternateAbility.SpyNightVision);
        Set(SpyGrab, AlternateAbility.SpyGrab);
        Set(SpyElbow, AlternateAbility.SpyElbow);
        Set(SpyLethalDrop, AlternateAbility.SpyLethalDrop);
        Set(MercEmfVision, AlternateAbility.MercEmfVision);
        Set(MercMotionTracking, AlternateAbility.MercMotionTracking);
        Set(MercElbow, AlternateAbility.MercElbow);
        Set(MercLethalDrop, AlternateAbility.MercLethalDrop);
        Set(MercTorch, AlternateAbility.MercTorch);
        Set(MercLaser, AlternateAbility.MercLaser);
        Set(MercBerserk, AlternateAbility.MercBerserk);
        Set(MercCharge, AlternateAbility.MercCharge);
        Set(CinematicCamera, AlternateAbility.CinematicCamera);
        r = r with
        {
            SpyGadgets = Mask(SpyGadgets, r.SpyGadgets),
            MercGadgets = Mask(MercGadgets, r.MercGadgets),
            SpyGadgetCount = (byte)Math.Clamp(SpySlots ?? r.SpyGadgetCount, 0, AlternateRules.MaxGadgetCount),
            MercGadgetCount = (byte)Math.Clamp(MercSlots ?? r.MercGadgetCount, 0, AlternateRules.MaxGadgetCount),
        };
        return r.Clamped();
    }

    private static byte Mask(List<int>? gadgets, byte fallback)
    {
        if (gadgets is null)
        {
            return fallback;
        }

        int mask = 0;
        foreach (int g in gadgets)
        {
            if (g is >= 1 and <= AlternateRules.GadgetOptions)
            {
                mask |= 1 << (g - 1);
            }
        }

        return (byte)mask;
    }
}

/// <summary>The master a dedicated server lists itself on.</summary>
internal sealed record MasterConfig
{
    public string Url { get; init; } = string.Empty;

    public string? InviteKey { get; init; }

    public bool Listed { get; init; } = true;

    public bool NatPunch { get; init; } = true;

    public bool Relay { get; init; } = true;

    /// <summary>The trusted-server key the master's admin gave this server (master admins are admins here, rounds count for stats).</summary>
    public string? ServerKey { get; init; }

    /// <summary>Where the server stands (for example <c>EU</c> or <c>US</c>), shown in the lists.</summary>
    public string? Region { get; init; }
}

/// <summary>Demo recording (docs/DEMOS.md): each round as a .scctdemo file, uploaded to the master's bucket (docs/ONLINE.md, "Demos").</summary>
internal sealed record DemosConfig
{
    /// <summary>Record every round.</summary>
    public bool Record { get; init; } = true;

    /// <summary>Where the files go, relative to the config file.</summary>
    public string Folder { get; init; } = "demos";

    /// <summary>Upload recorded demos to the master's bucket (needs the trusted-server key; the master must have demos on).</summary>
    public bool Upload { get; init; } = true;

    /// <summary>Days a demo file stays in <see cref="Folder"/>; 0 keeps them all.</summary>
    public int KeepLocalDays { get; init; } = 7;
}

/// <summary>
/// <c>svm-server.json</c>: what a dedicated online server serves (docs/ONLINE.md, "Dedicated server"). JSON with
/// comments; every field is optional.
/// </summary>
internal sealed record ServerConfig
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string Name { get; init; } = "OpenSCCT dedicated server";

    public int Port { get; init; } = BuildInfo.DefaultGamePort;

    /// <summary>LAN discovery port; 0 turns LAN discovery off.</summary>
    public int LanPort { get; init; } = BuildInfo.DefaultLanPort;

    /// <summary>Bind to 127.0.0.1 and ::1 only (testing on one machine).</summary>
    public bool LoopbackOnly { get; init; }

    public int MaxPlayers { get; init; } = 8;

    public int MaxSpectators { get; init; } = 2;

    public string? Password { get; init; }

    /// <summary>Password of in-game admins (<c>!login</c>); with one set, only admins change maps.</summary>
    public string? AdminPassword { get; init; }

    /// <summary>Without an admin password, every player may change the map and launch.</summary>
    public bool OpenMapCommands { get; init; } = true;

    public string? Motd { get; init; }

    /// <summary>Replication rate, 1 to 60; 0 (the default) takes the network profile's (Enhanced 60; Stock 20, 35 LAN).</summary>
    public int NetRate { get; init; }

    /// <summary>
    /// Network profile of the matches: "enhanced" (the default: every tick, a send budget per client) or "stock" (the
    /// original's rates and shared send budget); docs/NETCODE.md, "Send rate and budget: Stock and Enhanced".
    /// </summary>
    public string? NetProfile { get; init; }

    /// <summary><see cref="NetProfile"/> parsed; Enhanced when missing or not recognised.</summary>
    [JsonIgnore]
    public OpenScct.Net.NetProfile Profile =>
        OpenScct.Net.NetProfiles.TryParse(NetProfile, out OpenScct.Net.NetProfile p) ? p : OpenScct.Net.NetProfile.Enhanced;

    /// <summary>
    /// Lag compensation (Reloaded's <c>!lagcomp</c>): players' shots are traced against the other players where the
    /// shooter saw them, at most <see cref="LagCompCeilingMs"/> back. Off by default; the console's <c>lagcomp on</c>
    /// and an allowed player's <c>!lagcomp on</c> turn it on while the server runs.
    /// </summary>
    public bool LagComp { get; init; }

    /// <summary>The longest rewind of <see cref="LagComp"/> in milliseconds (0 to 500).</summary>
    public int LagCompCeilingMs { get; init; } = OpenScct.Server.LagCompensation.DefaultCeilingMs;

    /// <summary>The original's -lanplay switch (LAN tick cap and net speeds).</summary>
    public bool LanPlay { get; init; }

    /// <summary>An extra folder searched for SVMap maps (before maps/ next to the executable).</summary>
    public string? Maps { get; init; }

    /// <summary>The matches the server plays in turn; the first is the lobby's first setup.</summary>
    public List<RotationEntry> Rotation { get; init; } = [new RotationEntry()];

    /// <summary>Auto balance in every match of the rotation: joiners go to the smaller side, no switch to the bigger one.</summary>
    public bool AutoBalance { get; init; }

    /// <summary>Alternate Mode (abilities and gadget restrictions) for every match of the rotation; null: off.</summary>
    public AlternateConfig? Alternate { get; init; }

    public int MinPlayersToLaunch { get; init; } = 2;

    public float LaunchCountdownSeconds { get; init; } = 5f;

    public float ResultsSeconds { get; init; } = 30f;

    public float LoadTimeoutSeconds { get; init; } = 60f;

    /// <summary>Where bans are kept (relative to the config file); null keeps them in memory only.</summary>
    public string? BansFile { get; init; } = "bans.json";

    public MasterConfig? Master { get; init; }

    /// <summary>Demo recording and upload; null records nothing.</summary>
    public DemosConfig? Demos { get; init; }

    /// <summary>
    /// Only players signed in on the master may join. Null: on when the server is listed on a master, off otherwise.
    /// Without a master it cannot be on (nobody could have a ticket).
    /// </summary>
    public bool? RequireAccounts { get; init; }

    /// <summary><see cref="RequireAccounts"/> with its default filled in.</summary>
    [JsonIgnore]
    public bool RequiresAccounts => RequireAccounts ?? Master is { Url.Length: > 0, Listed: true };

    /// <summary>
    /// Practice server (rl:net.practice): unlimited lives, the shortest respawn, the !heal, !regen, !tank and !kill
    /// chat commands, and no stats or achievements. The console's <c>practice on|off</c> changes it while running.
    /// </summary>
    public bool Practice { get; init; }

    public ulong Seed { get; init; } = 1;

    public static ServerConfig Load(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<ServerConfig>(File.ReadAllText(path), Json) ?? new ServerConfig();
        }
        catch (JsonException e)
        {
            throw new InvalidOperationException($"{path}: {e.Message}", e);
        }
    }

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    /// <summary>A config to start from (written by <c>--write-config</c>).</summary>
    public static ServerConfig Example() => new()
    {
        Name = "My OpenSCCT server",
        Motd = "Welcome. Be nice.",
        Rotation =
        [
            new RotationEntry { Map = "SteeE", Mode = "Story" },
            new RotationEntry { Map = "SteeE", Mode = "Story", Revenge = true },
        ],
        Master = new MasterConfig { Url = "https://master.example.org", InviteKey = "the-invite-key" },
        RequireAccounts = true,
    };

    /// <summary>The rotation's setups, each from its map's own settings for the mode and the entry's changes.</summary>
    public List<LobbySetup> Setups(MapCatalog maps)
    {
        var list = new List<LobbySetup>();
        foreach (RotationEntry e in Rotation)
        {
            if (maps.FindMap(e.Map) is not { } entry)
            {
                throw new InvalidOperationException($"Rotation map '{e.Map}' is not installed (looked in: {string.Join("; ", maps.Roots)}).");
            }

            if (!LobbySetup.TryParseMode(e.Mode, out EGameMode mode))
            {
                throw new InvalidOperationException($"Rotation mode '{e.Mode}' is not a mode (Story, DiskHunt, Deathmatch, MercDeathmatch).");
            }

            LobbySetup s = LobbySetup.ForMap(entry.Id, mode, maps.GetMapSettings(entry.Id), MaxPlayers);
            s = s with
            {
                TimeLimit = e.TimeLimit ?? s.TimeLimit,
                SpyLives = e.SpyLives ?? s.SpyLives,
                MercLives = e.MercLives ?? s.MercLives,
                RespawnSeconds = e.Respawn ?? s.RespawnSeconds,
                ScoreLimit = e.KillLimit ?? s.ScoreLimit,
                Disks = e.Disks ?? s.Disks,
                DisksToWin = e.DisksToWin ?? s.DisksToWin,
                Revenge = e.Revenge ?? s.Revenge,
                GamesInARow = e.GamesInARow ?? s.GamesInARow,
                AutoBalance = AutoBalance,
                Alternate = Alternate?.ToRules() ?? s.Alternate,
                Practice = Practice,
            };
            list.Add(s.Clamped());
        }

        if (list.Count == 0)
        {
            throw new InvalidOperationException("The rotation is empty.");
        }

        return list;
    }
}
