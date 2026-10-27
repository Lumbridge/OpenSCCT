using System.Globalization;
using OpenScct.Core;
using OpenScct.Game.SBase;
using OpenScct.Provenance;

namespace OpenScct.Game.Modes;

/// <summary>
/// The settings a network match starts with when the host leaves the menu untouched (spec/game/game-modes.md rules
/// 1 to 5): the map's entry for the mode, clamped as the match creation page does, Merc Deathmatch's kill limit
/// defaults, and what the page stores: the URL options and the host's disk counts.
/// </summary>
public sealed class MatchSetup
{
    private MatchSetup(USMatchInfo match, int numDisks, int numDisksToWin, int numDisksMax)
    {
        Match = match;
        NumDisks = numDisks;
        NumDisksToWin = numDisksToWin;
        NumDisksMax = numDisksMax;
    }

    /// <summary>The match settings the URL carries (SMatchInfo after ApplyMatchRules).</summary>
    public USMatchInfo Match { get; }

    /// <summary>Active disks ("Active Disks", SPersistentData.NumDisks).</summary>
    public int NumDisks { get; }

    /// <summary>Disks to take ("Disks to take", SPersistentData.NumDisksToWin).</summary>
    public int NumDisksToWin { get; }

    /// <summary>The map entry's NumDisksMax (SPersistentData.NumDisksMax), 0 without an entry.</summary>
    public int NumDisksMax { get; }

    /// <summary>
    /// The untouched page for <paramref name="mode"/> on a map with <paramref name="settings"/> (null: no entry):
    /// LoadMatchRules (LoadModeRules, then SetMercDeathMatchRules in Merc Deathmatch) and ApplyMatchRules. A revenge
    /// pair forces two games in a row; the lobby launch turns team balancing off.
    /// </summary>
    [PortOf("uc:GUI.Create_Match:LoadMatchRules", Spec = "spec/game/game-modes.md#rules")]
    [PortOf("uc:GUI.Create_Match:LoadModeRules", Spec = "spec/game/game-modes.md#rules")]
    [PortOf("uc:GUI.Create_Match:SetMercDeathMatchRules", Spec = "spec/game/game-modes.md#rules")]
    [PortOf("uc:GUI.Create_Match:SetDiskLimits", Spec = "spec/game/game-modes.md#rules")]
    [PortOf("uc:GUI.Create_Match:ApplyMatchRules", Spec = "spec/game/match-options.md#rules")]
    [Provenance(ProvenanceKind.Decoded, "Create_Match.LoadModeRules, SetMercDeathMatchRules and ApplyMatchRules with untouched settings")]
    [Provenance(ProvenanceKind.Estimated, "The numeric boxes' own min and max clamping is not modelled (values outside them only occur for disk counts of modes without disks)")]
    public static MatchSetup Defaults(USMapSettings? settings, EGameMode mode, string levelName,
        bool revenge = false, int gamesInARow = 1, int maxPlayers = 4)
    {
        // The six boxes: time, spy lives (the kill limit in Merc Deathmatch), merc lives, respawn, active disks, disks
        // to take.
        int time, spies, mercs, respawn, handicap, toWin, disksMax = 0;
        int index = settings?.SearchForGameMode(USMatchInfo.RulesModeFor(mode)) ?? -1;
        if (index < 0)
        {
            time = 0;
            spies = 0;
            mercs = 0;
            respawn = K.MenuNoEntryRespawn;
            handicap = K.MenuNoEntryDisks;
            toWin = K.MenuNoEntryDisks;
        }
        else
        {
            FGameModeInfo entry = settings!.Modes[index];
            time = SvmMath.Clamp(entry.TimeLimit, 0, K.MenuMaxTimeLimit);
            spies = SvmMath.Clamp(entry.SpiesLives, 0, K.MenuMaxLives);
            mercs = SvmMath.Clamp(entry.SentriesLives, 0, K.MenuMaxLives);
            int floor = USMatchInfo.RespawnFloor(mode);
            respawn = entry.RespawnTime < floor ? floor : SvmMath.Min(entry.RespawnTime, K.MenuMaxRespawn);
            disksMax = entry.NumDisksMax;
            handicap = entry.Handicap < 1 ? 1 : entry.Handicap;
            if (handicap > disksMax)
            {
                handicap = disksMax;
            }

            toWin = entry.ScoreLimit < 1 ? 1 : entry.ScoreLimit;
            if (toWin > handicap)
            {
                toWin = handicap;
            }
        }

        bool mercDeathMatch = mode == EGameMode.GM_MercDeathMatch;
        int scoreLimit = toWin;
        if (mercDeathMatch)
        {
            scoreLimit = USMatchInfo.DefaultKillLimit();
            spies = scoreLimit;
            mercs = K.MercDeathMatchDefaultLives;
            respawn = K.MercDeathMatchDefaultRespawn;
        }

        var match = new USMatchInfo
        {
            LevelName = levelName,
            MaxPlayers = maxPlayers,
            GameMode = mode,
            TimeLimit = time,
            NumLivesATT = mercDeathMatch ? 0 : spies,
            NumLivesDEF = mercs,
            TimesRespawn = respawn,
            Handicap = handicap,
            ScoreLimit = scoreLimit,
            Revenge = revenge,
            GamesInARow = revenge ? K.RevengeGamesInARow : gamesInARow,
            BalanceTeams = false,
        };
        return new MatchSetup(match, handicap, scoreLimit, disksMax);
    }

    /// <summary>
    /// The map URL options the lobby launch travels with (match-options.md rule 27): the match options and the
    /// expected player count.
    /// </summary>
    public string GameOptions(int nbPlayers) =>
        Match.GetOptionsString() + "?NBPlayers=" + nbPlayers.ToString(CultureInfo.InvariantCulture);

    /// <summary>Stores the disk counts in the host's settings object, as ApplyMatchRules and LoadModeRules do.</summary>
    public void ApplyTo(USPersistentData persistent)
    {
        persistent.NumDisks = NumDisks;
        persistent.NumDisksToWin = NumDisksToWin;
        persistent.NumDisksMax = NumDisksMax;
        persistent.LevelName = Match.LevelName;
    }

    /// <summary>The host's settings object for a match with these settings.</summary>
    public USPersistentData ToPersistentData()
    {
        USPersistentData persistent = USPersistentData.CreateInitialized();
        ApplyTo(persistent);
        return persistent;
    }
}
