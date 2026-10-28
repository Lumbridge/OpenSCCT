using System;
using System.Globalization;
using OpenScct.Content;
using OpenScct.Core;
using OpenScct.Game.Modes;
using OpenScct.Game.SBase;
using OpenScct.Server;

namespace OpenScctGame.Play.Session;

/// <summary>The side the local player joins.</summary>
public enum PlaySide
{
    /// <summary>Team 0, the spies (SPawnAttaque).</summary>
    Spy,

    /// <summary>Team 1, the mercs (SPawnDefense).</summary>
    Merc,
}

/// <summary>Where the match rules come from.</summary>
public enum PlayRules
{
    /// <summary>
    /// A network match made with untouched menu defaults: the map's SMapSettings entry for the mode (SteeE Story:
    /// 10 min, 4 spy and 3 merc lives, 10 s respawn), launched by the host (spec/game/match-options.md rule 27).
    /// </summary>
    MapSettings,

    /// <summary>The editor's Play Level URL: GameMode 5, unlimited lives, no time limit (match-options.md rule 29).</summary>
    PlayLevel,
}

/// <summary>How the listen server runs next to the local client.</summary>
public enum PlayHostMode
{
    /// <summary>The dedicated server code on its own thread (<c>ListenServer</c>), as for online hosting.</summary>
    Thread,

    /// <summary>
    /// The same server ticked on the caller's thread just before the client each tick: deterministic order, and
    /// server state can be read between ticks (smoke tests).
    /// </summary>
    Inline,
}

/// <summary>What <see cref="PlaySession"/> hosts and joins.</summary>
public sealed record PlaySettings
{
    /// <summary>The SVMap folder name (<c>.content/svmap/&lt;Map&gt;</c>).</summary>
    public string Map { get; init; } = "SteeE";

    public PlaySide Side { get; init; } = PlaySide.Spy;

    /// <summary>Game mode name as the map settings key it (Story, DiskHunt, Deathmatch).</summary>
    public string Mode { get; init; } = "Story";

    public PlayRules Rules { get; init; } = PlayRules.MapSettings;

    public PlayHostMode HostMode { get; init; } = PlayHostMode.Thread;

    public string PlayerName { get; init; } = "Player";

    /// <summary>
    /// Replication passes per second of the listen server, 1 to 60 (<c>--svm-netrate=</c>); 0 (the default) takes the
    /// Enhanced profile's rate, every tick (60).
    /// </summary>
    public int NetRate { get; init; }

    public ulong MatchSeed { get; init; } = 1;

    /// <summary>Extra SVMap roots searched before the defaults.</summary>
    public string[] MapRoots { get; init; } = [];

    /// <summary>
    /// Server-side bots that fill the other slots (<c>--bots=3</c>: a 2v2 with the local player), 0 for none
    /// (docs/AI.md).
    /// </summary>
    public int Bots { get; init; }

    /// <summary>The bots' difficulty preset (<c>--difficulty=easy|normal|hard|expert</c>).</summary>
    public string Difficulty { get; init; } = "normal";

    /// <summary>
    /// Terminals block players as the map says (null, the default, or true), or false to let players walk through them
    /// in a match with bots (a fallback from when the terminal collision kept a walker short of the use spot).
    /// </summary>
    public bool? TerminalsBlockPlayers { get; init; }

    /// <summary>
    /// With <see cref="PlayHostMode.Inline"/>: the local player's link to the host goes through a simulated network with
    /// these conditions (latency, jitter, loss) instead of the loopback, so tests can play through a bad link. Null for
    /// the loopback.
    /// </summary>
    public OpenScct.Net.SimLinkProfile? SimLink { get; init; }

    /// <summary>
    /// With <see cref="PlayRules.MapSettings"/>: the original's offline launch rules (a tutorial or a Visit a Map: no
    /// time limit, unlimited lives, 5 s respawn; <see cref="MatchSetup.WithOfflineRules"/>) instead of the map's entry.
    /// </summary>
    public bool OfflineRules { get; init; }

    /// <summary>Records the match as a demo into this folder (docs/DEMOS.md), or null for none.</summary>
    public string? DemoFolder { get; init; }

    /// <summary>The team index the login asks for.</summary>
    public int Team => Side == PlaySide.Spy ? 0 : 1;

    /// <summary>
    /// The mode id of <see cref="Mode"/> (Story 5, DiskHunt 4, Deathmatch 6, MercDeathMatch 7; Extraction 1, Sabotage 2
    /// and Neutralization 3 for maps built for them).
    /// </summary>
    public EGameMode GameMode => MapCatalog.ModeOfKey(Mode)
        ?? throw new ArgumentException($"Unknown mode '{Mode}' (Story, DiskHunt, Deathmatch, MercDeathMatch, Extraction, Sabotage, Neutralization).");

    /// <summary>The map URL options the game rules start with (InitGame).</summary>
    public string GameOptions(GameplayDoc gameplay)
    {
        ArgumentNullException.ThrowIfNull(gameplay);
        string mode = ((byte)GameMode).ToString(CultureInfo.InvariantCulture);
        if (Rules == PlayRules.PlayLevel)
        {
            return $"?GameMode={mode}?Team={Team}?Editeur=true";
        }

        // The untouched match creation page for the mode (spec/game/game-modes.md rules 1 to 5), launched from the
        // lobby (match-options.md rule 27).
        return Setup(gameplay).GameOptions(1);
    }

    /// <summary>
    /// The host's settings object the rules read (the disk counts of Disk Hunt), or null with the Play Level rules,
    /// which start from an empty one.
    /// </summary>
    public USPersistentData? HostSettings(GameplayDoc gameplay)
    {
        ArgumentNullException.ThrowIfNull(gameplay);
        return Rules == PlayRules.PlayLevel ? null : Setup(gameplay).ToPersistentData();
    }

    private MatchSetup Setup(GameplayDoc gameplay)
    {
        MatchSetup setup = MatchSetup.Defaults(PlayMapLoader.MapSettingsOf(Map, gameplay), GameMode, Map);
        return OfflineRules ? setup.WithOfflineRules() : setup;
    }

    /// <summary>The login options of the local player.</summary>
    public string LoginOptions =>
        string.Create(CultureInfo.InvariantCulture, $"?Name={PlayerName}?Team={Team}") + (Rules == PlayRules.PlayLevel ? "?Editeur=true" : string.Empty);
}
