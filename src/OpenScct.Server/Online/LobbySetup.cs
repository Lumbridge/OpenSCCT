using System;
using System.Collections.Generic;
using System.Globalization;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Provenance;
using GameK = OpenScct.Game.GameFlow.K;
using ModeSetup = OpenScct.Game.Modes.MatchSetup;

namespace OpenScct.Server.Online;

/// <summary>
/// The match the lobby sets up, as the original's network match creation page holds it (spec/game/match-options.md
/// rules 26 and 27, spec/game/game-modes.md): map, mode, time limit, both sides' lives (the kill limit in Merc
/// Deathmatch), respawn delay, the disk counts of Disk Hunt, revenge and the number of games in a row.
/// <see cref="ForMap"/> starts from the page's untouched values (<see cref="ModeSetup.Defaults"/>);
/// <see cref="ToGameOptions"/> is the URL the launch travels with and <see cref="ApplyTo"/> the host settings the
/// rules read.
/// </summary>
public sealed record LobbySetup
{
    /// <summary>Highest time limit the menu offers, in minutes.</summary>
    [Provenance(ProvenanceKind.Decoded, "GUI.Create_Match.SetModeSettings: TimeLimit clamp 0..20")]
    public const int MaxTimeLimit = 20;

    /// <summary>Highest lives value the menu offers (0 is unlimited).</summary>
    [Provenance(ProvenanceKind.Decoded, "GUI.Create_Match.SetModeSettings: lives clamp 0..10")]
    public const int MaxLives = 10;

    /// <summary>Highest respawn delay the menu offers, in seconds.</summary>
    [Provenance(ProvenanceKind.Decoded, "GUI.Create_Match.SetModeSettings: RespawnTime clamp floor..15")]
    public const int MaxRespawn = 15;

    /// <summary>Lowest respawn delay the page's row accepts once edited (the map's values start at 5, 1 in Merc Deathmatch).</summary>
    [Provenance(ProvenanceKind.Decoded, "GUI.u SessionRespawn MinValue 1; Create_Match.ApplyMatchRules clamps to the row's minimum")]
    public const int MinRespawn = 1;

    /// <summary>Most games in a row the page offers.</summary>
    [Provenance(ProvenanceKind.Decoded, "GUI.u SessionGamesInARow 1..10")]
    public const int MaxGamesInARow = 10;

    /// <summary>Fewest players the page's Max Players row offers.</summary>
    [Provenance(ProvenanceKind.Decoded, "GUI.u SessionMaxPlayers MinValue 2")]
    public const int MinMaxPlayers = 2;

    /// <summary>Most players the page's Max Players row offers.</summary>
    [Provenance(ProvenanceKind.Decoded, "GUI.u SessionMaxPlayers MaxValue 8")]
    public const int MaxMaxPlayers = 8;

    /// <summary>The page's Max Players when the profile holds no valid value.</summary>
    [Provenance(ProvenanceKind.Decoded, "GUI.Create_Match.InitComponent: MaxPlayers outside the row's range becomes 4")]
    public const int DefaultMaxPlayers = 4;

    /// <summary>Most characters of a session password.</summary>
    [Provenance(ProvenanceKind.Decoded, "GUI.Create_Match.InitComponent: PasswordBox.MaxWidth(16)")]
    public const int MaxPasswordLength = 16;

    /// <summary>Characters a session password may hold.</summary>
    [Provenance(ProvenanceKind.Decoded, "GUI.Create_Match.InitComponent: SetAllowedCharSet")]
    public const string PasswordCharacters = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ._-1234567890";

    /// <summary>Games in a row of a revenge pair.</summary>
    [Provenance(ProvenanceKind.Decoded, "GUI.Create_Match.ApplyMatchRules: Revenge forces GamesInARow 2")]
    public const int RevengeGames = 2;

    public string Map { get; init; } = MapCatalog.TestBox;

    public EGameMode Mode { get; init; } = EGameMode.GM_Multi;

    /// <summary>Minutes; 0 is no limit.</summary>
    public int TimeLimit { get; init; }

    /// <summary>Kill limit of Merc Deathmatch, disks to take in Disk Hunt, else the map's score limit.</summary>
    public int ScoreLimit { get; init; }

    /// <summary>Lives of each spy; 0 is unlimited.</summary>
    public int SpyLives { get; init; }

    /// <summary>Lives of each merc; 0 is unlimited.</summary>
    public int MercLives { get; init; }

    /// <summary>Seconds before a respawn.</summary>
    public int RespawnSeconds { get; init; } = 5;

    /// <summary>Disk Hunt: active disks ("Active Disks").</summary>
    public int Disks { get; init; }

    /// <summary>Disk Hunt: disks to take ("Disks to take").</summary>
    public int DisksToWin { get; init; }

    /// <summary>Disk Hunt: the map's most disks.</summary>
    public int DisksMax { get; init; }

    /// <summary>A revenge pair: two games, sides swapped for the second, points carried over.</summary>
    public bool Revenge { get; init; }

    /// <summary>Games in a row on this map before the lobby (1 is a single game).</summary>
    public int GamesInARow { get; init; } = 1;

    /// <summary>Players the match takes (the GameInfo's MaxPlayers).</summary>
    public int MaxPlayers { get; init; } = 8;

    /// <summary>
    /// The host's "Voice Chat" mode (Enhanced 4.0's Create Session row): who hears whom in the match
    /// (spec/game/voice-chat.md). Modern by default.
    /// </summary>
    public EVoiceChatMode VoiceMode { get; init; } = EVoiceChatMode.VCM_Modern;

    /// <summary>"Voice in Lobby": players talk in the lobby (allowVoiceChatInLobby, on by default).</summary>
    public bool VoiceInLobby { get; init; } = true;

    /// <summary>"Random Map": once a match or a series is over, the next map is drawn at random (Ending_Menu.Timer).</summary>
    public bool RandomMap { get; init; }

    /// <summary>
    /// "Auto balance" (Game_Settings' AutoBalance row, SPersistentData.bAutoBalance): a player who joins asking for the
    /// bigger side goes to the smaller one, and in the lobby nobody may switch to a side that would then have two more
    /// people than the other (SGameInfo.PickTeam with bPlayersBalanceTeams; bots are not counted). The original's row is hidden and always on; the rewrite makes it
    /// the host's choice, off by default.
    /// </summary>
    public bool AutoBalance { get; init; }

    /// <summary>Alternate Mode: the host's ability switches and gadget restrictions (<see cref="AlternateRules"/>).</summary>
    public AlternateRules Alternate { get; init; } = AlternateRules.Default;

    /// <summary>
    /// Server bots (not in the original, which has no bots in its network sessions): off, a number on each side, or
    /// filling the empty places. They join the lobby as rows and play the rounds on the server with the game's AI.
    /// </summary>
    public BotFill Bots { get; init; }

    /// <summary>Bots on each side with <see cref="BotFill.PerSide"/> (1 to <see cref="OnlineBotRoster.MaxPerSide"/>).</summary>
    public int BotsPerSide { get; init; } = 1;

    /// <summary>How well the bots play (docs/AI.md, "Difficulty").</summary>
    public OpenScct.Game.AI.AiSkill BotSkill { get; init; } = OpenScct.Game.AI.AiSkill.Normal;

    /// <summary>
    /// Practice server (FEATURES rl:net.practice): the round plays with unlimited lives on both sides and the
    /// shortest respawn, players may use the practice chat commands (<c>!heal</c>, <c>!regen</c>, <c>!tank</c>,
    /// <c>!kill</c>), and its results do not count for stats or achievements.
    /// </summary>
    public bool Practice { get; init; }

    /// <summary>A mode by its menu name or number.</summary>
    public static bool TryParseMode(string? text, out EGameMode mode)
    {
        mode = (text ?? string.Empty).Trim().ToUpperInvariant().Replace(" ", string.Empty, StringComparison.Ordinal) switch
        {
            "STORY" or "5" => EGameMode.GM_Multi,
            "DISKHUNT" or "4" => EGameMode.GM_DiskHunt,
            "DEATHMATCH" or "6" => EGameMode.GM_DeathMatch,
            "MERCDEATHMATCH" or "7" => EGameMode.GM_MercDeathMatch,
            "EXTRACTION" or "1" => EGameMode.GM_Extraction,
            "SABOTAGE" or "2" => EGameMode.GM_Sabotage,
            "NEUTRALIZATION" or "3" => EGameMode.GM_Neutralization,
            _ => EGameMode.GM_Undefined,
        };
        return mode != EGameMode.GM_Undefined;
    }

    /// <summary>The menu name of a mode (Story is GM_Multi).</summary>
    public static string ModeName(EGameMode mode) => mode switch
    {
        EGameMode.GM_Multi => "Story",
        EGameMode.GM_DiskHunt => "Disk Hunt",
        EGameMode.GM_DeathMatch => "Deathmatch",
        EGameMode.GM_MercDeathMatch => "Merc Deathmatch",
        EGameMode.GM_Extraction => "Extraction",
        EGameMode.GM_Sabotage => "Sabotage",
        EGameMode.GM_Neutralization => "Neutralization",
        _ => "Undefined",
    };

    /// <summary>The match creation page's untouched values for <paramref name="mode"/> on a map with <paramref name="settings"/>.</summary>
    public static LobbySetup ForMap(string map, EGameMode mode, USMapSettings? settings, int maxPlayers = 8)
    {
        ArgumentException.ThrowIfNullOrEmpty(map);
        ModeSetup page = ModeSetup.Defaults(settings, mode, map, maxPlayers: maxPlayers);
        USMatchInfo m = page.Match;
        return new LobbySetup
        {
            Map = map, Mode = mode, TimeLimit = m.TimeLimit, ScoreLimit = m.ScoreLimit, SpyLives = m.NumLivesATT,
            MercLives = m.NumLivesDEF, RespawnSeconds = m.TimesRespawn, Disks = page.NumDisks, DisksToWin = page.NumDisksToWin,
            DisksMax = page.NumDisksMax, MaxPlayers = maxPlayers,
        };
    }

    /// <summary>The setup with every value inside the menu's ranges.</summary>
    public LobbySetup Clamped()
    {
        int disks = DisksMax > 0 ? Math.Clamp(Disks, 1, DisksMax) : Math.Max(0, Disks);
        return this with
        {
            TimeLimit = Math.Clamp(TimeLimit, 0, MaxTimeLimit),
            SpyLives = Math.Clamp(SpyLives, 0, MaxLives),
            MercLives = Math.Clamp(MercLives, 0, MaxLives),
            ScoreLimit = Math.Clamp(ScoreLimit, 0, USMatchInfo.MaxKillLimit()),
            RespawnSeconds = Math.Clamp(RespawnSeconds, MinRespawn, MaxRespawn),
            Disks = disks,
            DisksToWin = disks > 0 ? Math.Clamp(DisksToWin, 1, disks) : Math.Max(0, DisksToWin),
            GamesInARow = Revenge ? RevengeGames : Math.Clamp(GamesInARow, 1, MaxGamesInARow),
            MaxPlayers = Math.Clamp(MaxPlayers, 1, GameK.MaxPlayersCap),
            VoiceMode = VoiceModeOf(VoiceMenuIndex(VoiceMode)),
            Bots = Bots is BotFill.PerSide or BotFill.Fill ? Bots : BotFill.Off,
            BotsPerSide = Math.Clamp(BotsPerSide, 1, OnlineBotRoster.MaxPerSide),
            BotSkill = (OpenScct.Game.AI.AiSkill)Math.Clamp((int)BotSkill, 0, (int)OpenScct.Game.AI.AiSkill.Expert),
            Alternate = Alternate.Clamped(),
        };
    }

    /// <summary>The match settings object the setup stands for (the values as set, before the practice rules).</summary>
    public USMatchInfo ToMatchInfo() => new()
    {
        LevelName = Map,
        MaxPlayers = MaxPlayers,
        TimeLimit = TimeLimit,
        ScoreLimit = Mode == EGameMode.GM_DiskHunt ? DisksToWin : ScoreLimit,
        GameMode = Mode,
        NumLivesATT = Mode == EGameMode.GM_MercDeathMatch ? 0 : SpyLives,
        NumLivesDEF = MercLives,
        TimesRespawn = RespawnSeconds,
        Handicap = Disks,
        BalanceTeams = false,
        Revenge = Revenge,
        GamesInARow = Revenge ? RevengeGames : GamesInARow,
    };

    /// <summary>
    /// The URL options of the launch (loading.Timer, networked branch): GetOptionsString with BalanceTeams false, then
    /// the number of players the server waits for.
    /// </summary>
    public string ToGameOptions(int players) =>
        RoundMatchInfo().GetOptionsString() + "?NBPlayers=" + Math.Max(1, players).ToString(CultureInfo.InvariantCulture);

    /// <summary>The round's match settings: a practice round has unlimited lives on both sides and the shortest respawn.</summary>
    private USMatchInfo RoundMatchInfo()
    {
        USMatchInfo m = ToMatchInfo();
        if (Practice)
        {
            m.NumLivesATT = 0;
            m.NumLivesDEF = 0;
            m.TimesRespawn = MinRespawn;
        }

        return m;
    }

    /// <summary>The disk counts and map in the host's settings object, as the match creation page stores them.</summary>
    public void ApplyTo(USPersistentData persistent)
    {
        ArgumentNullException.ThrowIfNull(persistent);
        persistent.NumDisks = Disks;
        persistent.NumDisksToWin = DisksToWin;
        persistent.NumDisksMax = DisksMax;
        persistent.LevelName = Map;
        persistent.VoiceChatMode = VoiceMenuIndex(VoiceMode);
        persistent.allowVoiceChatInLobby = VoiceInLobby;
        persistent.bAutoBalance = AutoBalance;
        Alternate.ApplyTo(persistent, Mode);
    }

    /// <summary>
    /// The live values of a running server's replication info, as the page's Apply writes them
    /// (spec/game/lobby-and-match-creation.md rule 10): the voice mode, voice in the lobby and the random map switch.
    /// </summary>
    public void ApplyTo(GameReplicationInfo gri)
    {
        ArgumentNullException.ThrowIfNull(gri);
        gri.VoiceChatMode = VoiceMode;
        gri.allowVoiceChatInLobby = VoiceInLobby;
        gri.bRandomMap = RandomMap;
    }

    /// <summary>The page's voice row index of a mode (0 Modern, 1 Classic, 2 Team, 3 Open; SPersistentData.VoiceChatMode).</summary>
    public static byte VoiceMenuIndex(EVoiceChatMode mode) => mode switch
    {
        EVoiceChatMode.VCM_Classic => 1,
        EVoiceChatMode.VCM_Team => 2,
        EVoiceChatMode.VCM_Open => 3,
        _ => 0,
    };

    /// <summary>The mode of a voice row index (spec/game/lobby-and-match-creation.md rule 7: anything else is Modern).</summary>
    public static EVoiceChatMode VoiceModeOf(int menuIndex) => menuIndex switch
    {
        1 => EVoiceChatMode.VCM_Classic,
        2 => EVoiceChatMode.VCM_Team,
        3 => EVoiceChatMode.VCM_Open,
        _ => EVoiceChatMode.VCM_Modern,
    };

    /// <summary>The page's name of a voice mode.</summary>
    public static string VoiceModeName(EVoiceChatMode mode) => VoiceModeNames[VoiceMenuIndex(mode)];

    /// <summary>The voice row's choices in page order (GUI.int [Create_Match] VoiceChatModes).</summary>
    [Provenance(ProvenanceKind.Decoded, "System/_PC_/GUI.int [Create_Match] VoiceChatModes")]
    public static IReadOnlyList<string> VoiceModeNames { get; } = ["Modern", "Classic", "Team", "Open"];

    /// <summary>The voice row's hints in page order (GUI.int [Create_Match] VoiceChatModeHints).</summary>
    [Provenance(ProvenanceKind.Decoded, "System/_PC_/GUI.int [Create_Match] VoiceChatModeHints")]
    public static IReadOnlyList<string> VoiceModeHints { get; } =
        ["Team always - hackable coms", "Living team - hackable coms", "Team always - no com hacking", "Everyone hears everyone"];

    /// <summary>The setup as one options line (the lobby protocol and config files): GetOptionsString, then the disk counts.</summary>
    public string ToOptions() =>
        ToMatchInfo().GetOptionsString() + string.Create(CultureInfo.InvariantCulture,
            $"?SvmDisks={Disks}?SvmDisksToWin={DisksToWin}?SvmDisksMax={DisksMax}?SvmVoice={VoiceMenuIndex(VoiceMode)}?SvmVoiceLobby={(VoiceInLobby ? 1 : 0)}?SvmRandomMap={(RandomMap ? 1 : 0)}") +
        string.Create(CultureInfo.InvariantCulture, $"?SvmBots={(int)Bots}?SvmBotsPerSide={BotsPerSide}?SvmBotSkill={(int)BotSkill}?SvmBal={(AutoBalance ? 1 : 0)}?SvmPr={(Practice ? 1 : 0)}") +
        Alternate.ToOptions();

    /// <summary>Reads a setup from options (as <see cref="ToOptions"/> writes them); missing values keep <paramref name="fallback"/>'s.</summary>
    public static LobbySetup FromOptions(string options, LobbySetup? fallback = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        LobbySetup f = fallback ?? new LobbySetup();
        string map = UcOptions.ParseOption(options, "MapName");
        var mode = (EGameMode)(byte)UcOptions.GetIntOption(options, "GameMode", (byte)f.Mode);
        int livesAtt = UcOptions.GetIntOption(options, "LivesATT", f.SpyLives);
        int livesDef = UcOptions.GetIntOption(options, "LivesDEF", f.MercLives);
        int score = UcOptions.GetIntOption(options, "ScoreLimit", f.ScoreLimit);
        return new LobbySetup
        {
            Map = map.Length > 0 ? map : f.Map,
            Mode = mode,
            TimeLimit = UcOptions.GetIntOption(options, "TimeLimit", f.TimeLimit),
            ScoreLimit = score,
            SpyLives = livesAtt == GameK.UnlimitedLives ? 0 : livesAtt,
            MercLives = livesDef == GameK.UnlimitedLives ? 0 : livesDef,
            RespawnSeconds = UcOptions.GetIntOption(options, "TimesRespawnsATT", f.RespawnSeconds),
            Disks = UcOptions.GetIntOption(options, "SvmDisks", f.Disks),
            DisksToWin = UcOptions.GetIntOption(options, "SvmDisksToWin", mode == EGameMode.GM_DiskHunt ? score : f.DisksToWin),
            DisksMax = UcOptions.GetIntOption(options, "SvmDisksMax", f.DisksMax),
            Revenge = UcOptions.ParseOption(options, "Revenge") is { Length: > 0 } r ? UcStrings.EqualsIgnoreCase(r, "true") : f.Revenge,
            GamesInARow = UcOptions.GetIntOption(options, "GamesInARow", f.GamesInARow),
            MaxPlayers = UcOptions.GetIntOption(options, "MaxPlayers", f.MaxPlayers),
            VoiceMode = VoiceModeOf(UcOptions.GetIntOption(options, "SvmVoice", VoiceMenuIndex(f.VoiceMode))),
            VoiceInLobby = UcOptions.GetIntOption(options, "SvmVoiceLobby", f.VoiceInLobby ? 1 : 0) != 0,
            RandomMap = UcOptions.GetIntOption(options, "SvmRandomMap", f.RandomMap ? 1 : 0) != 0,
            Bots = (BotFill)Math.Clamp(UcOptions.GetIntOption(options, "SvmBots", (int)f.Bots), 0, 255),
            BotsPerSide = UcOptions.GetIntOption(options, "SvmBotsPerSide", f.BotsPerSide),
            BotSkill = (OpenScct.Game.AI.AiSkill)Math.Clamp(UcOptions.GetIntOption(options, "SvmBotSkill", (int)f.BotSkill), 0, 255),
            AutoBalance = UcOptions.GetIntOption(options, "SvmBal", f.AutoBalance ? 1 : 0) != 0,
            Alternate = AlternateRules.FromOptions(options, f.Alternate),
            Practice = UcOptions.GetIntOption(options, "SvmPr", f.Practice ? 1 : 0) != 0,
        }.Clamped();
    }

    /// <summary>One line for logs and the admin status.</summary>
    public string Describe()
    {
        string rules = Mode switch
        {
            EGameMode.GM_MercDeathMatch => string.Create(CultureInfo.InvariantCulture, $"kill limit {ScoreLimit}, merc lives {Lives(MercLives)}"),
            EGameMode.GM_DiskHunt => string.Create(CultureInfo.InvariantCulture,
                $"disks {DisksToWin} of {Disks}, lives {Lives(SpyLives)} spy / {Lives(MercLives)} merc"),
            _ => string.Create(CultureInfo.InvariantCulture, $"lives {Lives(SpyLives)} spy / {Lives(MercLives)} merc"),
        };
        string time = TimeLimit > 0 ? string.Create(CultureInfo.InvariantCulture, $"{TimeLimit} min") : "no time limit";
        string series = Revenge ? ", revenge" : GamesInARow > 1 ? string.Create(CultureInfo.InvariantCulture, $", {GamesInARow} games") : string.Empty;
        string random = RandomMap ? ", random map" : string.Empty;
        string bots = Bots switch
        {
            BotFill.PerSide => string.Create(CultureInfo.InvariantCulture, $", {BotsPerSide} {BotSkill} bots per side"),
            BotFill.Fill => string.Create(CultureInfo.InvariantCulture, $", {BotSkill} bots fill the places"),
            _ => string.Empty,
        };
        if (Practice)
        {
            rules = Mode == EGameMode.GM_MercDeathMatch
                ? string.Create(CultureInfo.InvariantCulture, $"kill limit {ScoreLimit}, unlimited lives")
                : "unlimited lives";
        }

        int respawn = Practice ? MinRespawn : RespawnSeconds;
        string practice = Practice ? ", practice" : string.Empty;
        return string.Create(CultureInfo.InvariantCulture,
            $"{Map} {ModeName(Mode)}{practice}, {time}, {rules}, respawn {respawn} s{series}{random}{bots}, voice {VoiceModeName(VoiceMode)}{(AutoBalance ? ", auto balance" : "")}{Alternate.Describe()}");
    }

    private static string Lives(int lives) => lives <= 0 ? "unlimited" : lives.ToString(CultureInfo.InvariantCulture);
}
