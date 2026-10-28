using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using OpenScct.Core;
using OpenScct.Game.SBase;
using OpenScct.Server.Online;

namespace OpenScctGame.UI;

/// <summary>A row of the Create Session page (spec/game/lobby-and-match-creation.md "Data").</summary>
public enum SessionRow
{
    Map,
    Mode,
    Password,
    VoiceChat,
    MaxPlayers,
    Revenge,
    RandomMap,
    VoiceInLobby,

    /// <summary>Game_Settings' "Auto balance" (hidden and always on in the original).</summary>
    AutoBalance,

    /// <summary>Create_Match's "Alternate": the host's ability and gadget switches (AlternateModeSettings).</summary>
    Alternate,
    GamesInARow,
    TimeLimit,
    SpyLives,
    MercLives,
    Respawn,
    ActiveDisks,
    DisksToTake,

    /// <summary>Server bots (not in the original): Off, Per Side, Fill Slots.</summary>
    Bots,

    /// <summary>Bots on each side with Per Side.</summary>
    BotsPerSide,

    /// <summary>How well the bots play.</summary>
    BotSkill,

    /// <summary>Practice server (Reloaded, rl:net.practice): unlimited lives, quick respawns, practice commands, no stats.</summary>
    Practice,
}

/// <summary>
/// The rules of Enhanced 4.0's Create Session page (Create_Match, LAN page) without Godot, so they are tested alone
/// (spec/game/lobby-and-match-creation.md rules 1 to 9): the rows, their captions and ranges, which show in which
/// mode, what a change of map or mode reloads, Revenge's two games, Merc Deathmatch's "Kills to Win", the disk clamps,
/// the voice hint and the password's characters. Three rows are ours, not the original's (which has no bots in a
/// network session): Bots (Off, Per Side, Fill Slots), Bots per Side and Bot Skill. A fourth, Practice, is Reloaded's practice server mode. The same rows make the lobby's Session Parameters, editable for the
/// host and read only for the others. Values are a <see cref="LobbySetup"/> (what the session protocol carries).
/// </summary>
public sealed class CreateSessionModel
{
    /// <summary>The page's mode names (Controller.GetStringGameMode).</summary>
    public static string ModeName(EGameMode mode) => LobbySetup.ModeName(mode);

    private readonly IReadOnlyList<(string Id, string Title)> _maps;
    private readonly Func<string, USMapSettings?> _settings;
    private int _gamesBeforeRevenge = 1;

    /// <param name="maps">The installed maps (id and title), in the order the page lists them.</param>
    /// <param name="settings">A map's SMapSettings (its modes and their values), or null when it has none.</param>
    /// <param name="start">The values the page opens with (the last session's, or a map's defaults).</param>
    public CreateSessionModel(IReadOnlyList<(string Id, string Title)> maps, Func<string, USMapSettings?> settings, LobbySetup start)
    {
        ArgumentNullException.ThrowIfNull(maps);
        ArgumentNullException.ThrowIfNull(start);
        _maps = maps;
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        Setup = start.Clamped() with
        {
            MaxPlayers = start.MaxPlayers is >= LobbySetup.MinMaxPlayers and <= LobbySetup.MaxMaxPlayers ? start.MaxPlayers : LobbySetup.DefaultMaxPlayers,
        };
        _gamesBeforeRevenge = Setup.Revenge ? 1 : Setup.GamesInARow;
    }

    /// <summary>The page's values.</summary>
    public LobbySetup Setup { get; private set; }

    /// <summary>
    /// The maps the Choose Map arrows step through (the map list's playlist, rl:qol.lobbymaps); null, or a filter no
    /// map passes, steps through every map.
    /// </summary>
    public Func<string, bool>? MapFilter { get; set; }

    /// <summary>The session password (at most 16 of the allowed characters).</summary>
    public string Password { get; private set; } = string.Empty;

    /// <summary>Raised after any change.</summary>
    public event Action? Changed;

    /// <summary>The rows in page order: Map / Mode, then the session rows, then the rules boxes.</summary>
    public static IReadOnlyList<SessionRow> Rows { get; } = Enum.GetValues<SessionRow>();

    /// <summary>The modes the current map offers, as the page lists them: the map's own entries, then Merc Deathmatch.</summary>
    public IReadOnlyList<EGameMode> Modes
    {
        get
        {
            var modes = new List<EGameMode>();
            if (_settings(Setup.Map) is { } s)
            {
                foreach (FGameModeInfo entry in s.Modes)
                {
                    if (entry.GameModeId is EGameMode.GM_Multi or EGameMode.GM_DiskHunt or EGameMode.GM_DeathMatch && !modes.Contains(entry.GameModeId))
                    {
                        modes.Add(entry.GameModeId);
                    }
                }
            }

            if (modes.Count == 0)
            {
                modes.Add(EGameMode.GM_Multi);
            }

            modes.Add(EGameMode.GM_MercDeathMatch);
            return modes;
        }
    }

    /// <summary>The page's caption of a row (GUI.int [Create_Match]).</summary>
    public string Caption(SessionRow row) => row switch
    {
        SessionRow.Map => "Choose Map",
        SessionRow.Mode => "Game Mode",
        SessionRow.Password => "Password",
        SessionRow.VoiceChat => "Voice Chat",
        SessionRow.MaxPlayers => "Max Players",
        SessionRow.Revenge => "Revenge",
        SessionRow.RandomMap => "Random Map",
        SessionRow.VoiceInLobby => "Voice in Lobby",
        SessionRow.AutoBalance => "Auto balance",
        SessionRow.Alternate => "Alternate",
        SessionRow.GamesInARow => "Games in a Row",
        SessionRow.TimeLimit => "Game Length (min)",
        SessionRow.SpyLives => Setup.Mode == EGameMode.GM_MercDeathMatch ? "Kills to Win" : "Spy Lives",
        SessionRow.MercLives => "Mercenary Lives",
        SessionRow.Respawn => "Respawn Length (sec)",
        SessionRow.ActiveDisks => "Active Disks",
        SessionRow.DisksToTake => "Disks to Take",
        SessionRow.Bots => "Bots",
        SessionRow.BotsPerSide => "Bots per Side",
        SessionRow.BotSkill => "Bot Skill",
        SessionRow.Practice => "Practice",
        _ => row.ToString(),
    };

    /// <summary>Whether a row shows: the disk rows in Disk Hunt only, Games in a Row not with Revenge (rules 4 and 5).</summary>
    public bool Visible(SessionRow row) => row switch
    {
        SessionRow.ActiveDisks or SessionRow.DisksToTake => Setup.Mode == EGameMode.GM_DiskHunt,
        SessionRow.GamesInARow => !Setup.Revenge,
        SessionRow.BotsPerSide => Setup.Bots == BotFill.PerSide,
        SessionRow.BotSkill => Setup.Bots != BotFill.Off,
        _ => true,
    };

    /// <summary>Whether a row is a check box.</summary>
    public static bool IsCheck(SessionRow row) =>
        row is SessionRow.Revenge or SessionRow.RandomMap or SessionRow.VoiceInLobby or SessionRow.AutoBalance or SessionRow.Alternate
            or SessionRow.Practice;

    /// <summary>The range of a number row (inclusive).</summary>
    public (int Min, int Max) Range(SessionRow row) => row switch
    {
        SessionRow.MaxPlayers => (LobbySetup.MinMaxPlayers, LobbySetup.MaxMaxPlayers),
        SessionRow.GamesInARow => (1, LobbySetup.MaxGamesInARow),
        SessionRow.TimeLimit => (0, LobbySetup.MaxTimeLimit),
        SessionRow.SpyLives => Setup.Mode == EGameMode.GM_MercDeathMatch ? (1, USMatchInfo.MaxKillLimit()) : (0, LobbySetup.MaxLives),
        SessionRow.MercLives => (0, LobbySetup.MaxLives),
        SessionRow.Respawn => (LobbySetup.MinRespawn, LobbySetup.MaxRespawn),
        SessionRow.ActiveDisks => (1, Math.Max(1, Setup.DisksMax)),
        SessionRow.DisksToTake => (1, Math.Max(1, Setup.Disks)),
        SessionRow.BotsPerSide => (1, OnlineBotRoster.MaxPerSide),
        _ => (0, 0),
    };

    /// <summary>A number row's value.</summary>
    public int Number(SessionRow row) => row switch
    {
        SessionRow.MaxPlayers => Setup.MaxPlayers,
        SessionRow.GamesInARow => Setup.GamesInARow,
        SessionRow.TimeLimit => Setup.TimeLimit,
        SessionRow.SpyLives => Setup.Mode == EGameMode.GM_MercDeathMatch ? Setup.ScoreLimit : Setup.SpyLives,
        SessionRow.MercLives => Setup.MercLives,
        SessionRow.Respawn => Setup.RespawnSeconds,
        SessionRow.ActiveDisks => Setup.Disks,
        SessionRow.DisksToTake => Setup.DisksToWin,
        SessionRow.VoiceChat => LobbySetup.VoiceMenuIndex(Setup.VoiceMode),
        SessionRow.BotsPerSide => Setup.BotsPerSide,
        SessionRow.Bots => (int)Setup.Bots,
        SessionRow.BotSkill => (int)Setup.BotSkill,
        _ => 0,
    };

    /// <summary>A check row's value.</summary>
    public bool Checked(SessionRow row) => row switch
    {
        SessionRow.Revenge => Setup.Revenge,
        SessionRow.RandomMap => Setup.RandomMap,
        SessionRow.VoiceInLobby => Setup.VoiceInLobby,
        SessionRow.AutoBalance => Setup.AutoBalance,
        SessionRow.Alternate => Setup.Alternate.On,
        SessionRow.Practice => Setup.Practice,
        _ => false,
    };

    /// <summary>What a row shows: names for lists, "Inf." for an unlimited time or lives, "Yes"/"No" for checks.</summary>
    public string Text(SessionRow row)
    {
        switch (row)
        {
            case SessionRow.Map:
                foreach ((string id, string title) in _maps)
                {
                    if (string.Equals(id, Setup.Map, StringComparison.OrdinalIgnoreCase))
                    {
                        return title.Length > 0 && !string.Equals(title, id, StringComparison.Ordinal) ? title + " (" + id + ")" : id;
                    }
                }

                return Setup.Map;
            case SessionRow.Mode:
                return ModeName(Setup.Mode);
            case SessionRow.Password:
                return new string('*', Password.Length);
            case SessionRow.VoiceChat:
                return LobbySetup.VoiceModeName(Setup.VoiceMode);
            case SessionRow.Bots:
                return OnlineBotRoster.FillName(Setup.Bots);
            case SessionRow.BotSkill:
                return OnlineBotRoster.SkillNames[(int)Setup.BotSkill];
            case SessionRow.Revenge or SessionRow.RandomMap or SessionRow.VoiceInLobby or SessionRow.AutoBalance or SessionRow.Alternate
                or SessionRow.Practice:
                return Checked(row) ? "Yes" : "No";
            case SessionRow.TimeLimit or SessionRow.MercLives:
                return Number(row) == 0 ? InfiniteText : Number(row).ToString(CultureInfo.InvariantCulture);
            case SessionRow.SpyLives:
                return Setup.Mode != EGameMode.GM_MercDeathMatch && Setup.SpyLives == 0 ? InfiniteText : Number(row).ToString(CultureInfo.InvariantCulture);
            default:
                return Number(row).ToString(CultureInfo.InvariantCulture);
        }
    }

    /// <summary>The lobby's text for an unlimited value (GUI.int [Lobby_Create] Infini).</summary>
    public const string InfiniteText = "Inf.";

    /// <summary>The hint under Voice Chat (GUI.int [Create_Match] VoiceChatModeHints).</summary>
    public string VoiceHint => LobbySetup.VoiceModeHints[LobbySetup.VoiceMenuIndex(Setup.VoiceMode)];

    /// <summary>
    /// Steps a row by <paramref name="delta"/> (the arrows): the next map or mode (each reloads the rules from the map,
    /// rule 1), the next voice mode, a number within its range, or a check box flipped.
    /// </summary>
    public void Step(SessionRow row, int delta)
    {
        switch (row)
        {
            case SessionRow.Map when _maps.Count > 0:
            {
                var maps = new List<string>();
                foreach ((string id, _) in _maps)
                {
                    if (MapFilter?.Invoke(id) ?? true)
                    {
                        maps.Add(id);
                    }
                }

                if (maps.Count == 0)
                {
                    foreach ((string id, _) in _maps)
                    {
                        maps.Add(id);
                    }
                }

                // From a map outside the list, the arrows go to the list's first (or last) map.
                int at = maps.FindIndex(m => string.Equals(m, Setup.Map, StringComparison.OrdinalIgnoreCase));
                SelectMap(maps[at < 0 ? (delta > 0 ? 0 : maps.Count - 1) : Wrap(at + delta, maps.Count)]);
                return;
            }

            case SessionRow.Mode:
            {
                IReadOnlyList<EGameMode> modes = Modes;
                int at = Math.Max(0, IndexOf(modes, Setup.Mode));
                SelectMode(modes[Wrap(at + delta, modes.Count)]);
                return;
            }

            case SessionRow.VoiceChat:
                Set(Setup with { VoiceMode = LobbySetup.VoiceModeOf(Wrap(LobbySetup.VoiceMenuIndex(Setup.VoiceMode) + delta, 4)) });
                return;
            case SessionRow.Bots:
                Set(Setup with { Bots = (BotFill)Wrap((int)Setup.Bots + delta, OnlineBotRoster.FillNames.Count) });
                return;
            case SessionRow.BotSkill:
                Set(Setup with { BotSkill = (OpenScct.Game.AI.AiSkill)Wrap((int)Setup.BotSkill + delta, OnlineBotRoster.SkillNames.Count) });
                return;
            case SessionRow.Revenge or SessionRow.RandomMap or SessionRow.VoiceInLobby or SessionRow.AutoBalance or SessionRow.Alternate
                or SessionRow.Practice:
                SetChecked(row, !Checked(row));
                return;
            case SessionRow.Password or SessionRow.Map:
                return;
            default:
                SetNumber(row, Number(row) + delta);
                return;
        }
    }

    /// <summary>Sets a number row, clamped to its range, with the disk rule (Disks to Take never above Active Disks).</summary>
    public void SetNumber(SessionRow row, int value)
    {
        (int min, int max) = Range(row);
        int v = Math.Clamp(value, min, max);
        LobbySetup s = Setup;
        switch (row)
        {
            case SessionRow.MaxPlayers:
                s = s with { MaxPlayers = v };
                break;
            case SessionRow.GamesInARow:
                s = s with { GamesInARow = v };
                _gamesBeforeRevenge = v;
                break;
            case SessionRow.TimeLimit:
                s = s with { TimeLimit = v };
                break;
            case SessionRow.SpyLives:
                s = Setup.Mode == EGameMode.GM_MercDeathMatch ? s with { ScoreLimit = v, SpyLives = 0 } : s with { SpyLives = v };
                break;
            case SessionRow.MercLives:
                s = s with { MercLives = v };
                break;
            case SessionRow.Respawn:
                s = s with { RespawnSeconds = v };
                break;
            case SessionRow.ActiveDisks:
                s = s with { Disks = v, DisksToWin = Math.Min(s.DisksToWin, v) };
                break;
            case SessionRow.DisksToTake:
                s = s with { DisksToWin = v };
                break;
            case SessionRow.BotsPerSide:
                s = s with { BotsPerSide = v };
                break;
            default:
                return;
        }

        Set(s);
    }

    /// <summary>Sets a check row; Revenge forces two games in a row and restores the last count when cleared (rule 5).</summary>
    public void SetChecked(SessionRow row, bool on)
    {
        switch (row)
        {
            case SessionRow.Revenge:
                Set(Setup with { Revenge = on, GamesInARow = on ? LobbySetup.RevengeGames : Math.Max(1, _gamesBeforeRevenge) });
                break;
            case SessionRow.RandomMap:
                Set(Setup with { RandomMap = on });
                break;
            case SessionRow.VoiceInLobby:
                Set(Setup with { VoiceInLobby = on });
                break;
            case SessionRow.AutoBalance:
                Set(Setup with { AutoBalance = on });
                break;
            case SessionRow.Alternate:
                Set(Setup with { Alternate = Setup.Alternate with { On = on } });
                break;
            case SessionRow.Practice:
                Set(Setup with { Practice = on });
                break;
        }
    }

    /// <summary>The Alternate Mode page's values (abilities, gadgets and slots), clamped to the page's ranges.</summary>
    public void SetAlternate(AlternateRules rules) => Set(Setup with { Alternate = rules.Clamped() });

    /// <summary>Another map: the rules reload from its settings for the mode (or its first mode when it lacks this one).</summary>
    public void SelectMap(string map)
    {
        ArgumentException.ThrowIfNullOrEmpty(map);
        LobbySetup s = Setup with { Map = map };
        Setup = s;
        IReadOnlyList<EGameMode> modes = Modes;
        EGameMode mode = IndexOf(modes, s.Mode) >= 0 ? s.Mode : modes[0];
        Reload(map, mode);
    }

    /// <summary>Another mode: the rules reload from the map's settings for it (Merc Deathmatch reads the Deathmatch entry).</summary>
    public void SelectMode(EGameMode mode) => Reload(Setup.Map, mode);

    /// <summary>Sets the password, keeping only the allowed characters, at most 16 (Create_Match.InitComponent).</summary>
    public void SetPassword(string? text)
    {
        Password = FilterPassword(text);
        Changed?.Invoke();
    }

    public static string FilterPassword(string? text)
    {
        var sb = new StringBuilder();
        foreach (char c in text ?? string.Empty)
        {
            if (sb.Length < LobbySetup.MaxPasswordLength && LobbySetup.PasswordCharacters.Contains(c, StringComparison.Ordinal))
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }

    /// <summary>Takes values from elsewhere (the session's current setup), keeping the page's ranges.</summary>
    public void Load(LobbySetup setup)
    {
        ArgumentNullException.ThrowIfNull(setup);
        Set(setup);
    }

    private void Reload(string map, EGameMode mode)
    {
        LobbySetup fresh = LobbySetup.ForMap(map, mode, _settings(map), Setup.MaxPlayers);
        Set(fresh with
        {
            VoiceMode = Setup.VoiceMode, VoiceInLobby = Setup.VoiceInLobby, RandomMap = Setup.RandomMap, Revenge = Setup.Revenge,
            AutoBalance = Setup.AutoBalance, Alternate = Setup.Alternate, Practice = Setup.Practice,
            Bots = Setup.Bots, BotsPerSide = Setup.BotsPerSide, BotSkill = Setup.BotSkill,
            GamesInARow = Setup.Revenge ? LobbySetup.RevengeGames : Setup.GamesInARow, MaxPlayers = Setup.MaxPlayers,
        });
    }

    private void Set(LobbySetup next)
    {
        Setup = next.Clamped() with { MaxPlayers = Math.Clamp(next.MaxPlayers, LobbySetup.MinMaxPlayers, LobbySetup.MaxMaxPlayers) };
        Changed?.Invoke();
    }

    private static int Wrap(int i, int n) => ((i % n) + n) % n;

    private static int IndexOf(IReadOnlyList<EGameMode> modes, EGameMode mode)
    {
        for (int i = 0; i < modes.Count; i++)
        {
            if (modes[i] == mode)
            {
                return i;
            }
        }

        return -1;
    }
}
