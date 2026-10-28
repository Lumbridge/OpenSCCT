using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Net.Cosmetics;
using OpenScct.Net.Discovery;

namespace OpenScct.Server.Online;

// The lobby's lines: what the server sends, what players ask, and the admin and chat commands.
public sealed partial class OnlineServer
{
    // ---- sending ------------------------------------------------------------------------------------------------

    /// <summary>Sends a session line to one player (null: every player on the current map).</summary>
    private void Send(SessionPlayer? to, string line)
    {
        var message = ControlMessage.TextLine(line);
        if (to is not null)
        {
            to.Slot?.Connection.ControlChannel?.Send(message);
            return;
        }

        foreach (PlayerSlot slot in Host.Players)
        {
            slot.Connection.ControlChannel?.Send(message);
        }
    }

    /// <summary>A system line for every player's lobby box and the log.</summary>
    public void Announce(string text)
    {
        Log(text);
        Send(null, SessionProtocol.Line("msg", text));
    }

    private void SendMessage(SessionPlayer to, string text) => Send(to, SessionProtocol.Line("msg", text));

    /// <summary>Everything a player needs on joining a map: who it is, the server, the setup, the players, the phase.</summary>
    private void SendWelcome(SessionPlayer p)
    {
        Send(p, SessionProtocol.Line("hello", SessionProtocol.Version, p.Id, Settings.Dedicated, Settings.MaxPlayers, Settings.Name));
        SendSetup(p);
        foreach (SessionPlayer other in _players)
        {
            if (!ReferenceEquals(other, p))
            {
                Send(p, PlayerLine(other));
                if (other.Rank != 0)
                {
                    Send(p, RankLine(other));
                }
            }
        }

        Send(p, PhaseLine());
        if (_notice is { } notice)
        {
            SendMessage(p, notice);
        }

        if (Phase == SessionPhase.Results && LastResults is { } results)
        {
            SendResults(p, results);
        }
    }

    private void SendSetup(SessionPlayer? to)
    {
        ulong content = Maps.FindMap(Setup.Map)?.ContentHash ?? 0;
        Send(to, SessionProtocol.Line("setup", MasterApi.Hex(content), Setup.ToOptions()));

        // Right after the setup (which clears it on the client): where a downloaded map can be fetched. Clients from
        // before this line ignore it. The id is last, so it keeps its spaces.
        if (MapSourceOf(Setup.Map) is { } source)
        {
            Send(to, SessionProtocol.Line("mapsource", Setup.Map, source.Fingerprint, source.Commit, source.Id));
        }
    }

    private MapSourceInfo? MapSourceOf(string map)
    {
        try
        {
            return Settings.MapSource?.Invoke(map);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private string PlayerLine(SessionPlayer p) =>
        SessionProtocol.Line("player", p.Id, p.Team, (int)(p.Flags(Phase) | (CanControl(p) ? SessionPlayerFlags.Control : 0)), p.PingMs,
            (int)p.Map, p.Name);

    private void BroadcastPlayer(SessionPlayer p)
    {
        p.SentPing = p.PingMs;
        Send(null, PlayerLine(p));
        if (p.Rank != 0)
        {
            Send(null, RankLine(p));
        }
    }

    /// <summary>A player's rank (a line of its own: clients before levels ignore it).</summary>
    private static string RankLine(SessionPlayer p) => SessionProtocol.Line("rank", p.Id, p.Rank);

    private string PhaseLine()
    {
        long end = Phase == SessionPhase.Lobby ? _countdownEnd : Phase == SessionPhase.Results ? _resultsEnd : -1;
        long tenths = end >= 0 ? Math.Max(0, (end - _now) / 100_000) : 0;
        return SessionProtocol.Line("phase", SessionProtocol.PhaseName(Phase), tenths);
    }

    private void BroadcastPhase() => Send(null, PhaseLine());

    private void SendResults(SessionPlayer? to, MatchResults r)
    {
        foreach (ResultRow row in r.Rows)
        {
            Send(to, SessionProtocol.Line("row", row.Id, row.Team, row.Score, row.Kills, row.Deaths, row.Lives, row.Name));
        }

        Send(to, SessionProtocol.Line("results", r.WinningTeam, r.WinningReason, r.ObjectivesTaken, r.ObjectivesNeeded, r.GamesLeft, r.Map));
    }

    // ---- receiving ----------------------------------------------------------------------------------------------

    private void OnSessionLine(NetConnection connection, PlayerSlot? slot, string line)
    {
        if (!_byConnection.TryGetValue(connection, out SessionPlayer? p) || !SessionProtocol.TryParse(line, out string command, out List<string> f, 2))
        {
            return;
        }

        switch (command)
        {
            case "team" when Phase == SessionPhase.Lobby:
                ChangeTeam(p, (byte)Math.Clamp(SessionProtocol.Int(f, 0, p.Team), 0, 255));
                break;
            case "ready" when Phase is SessionPhase.Lobby or SessionPhase.Results:
                p.Ready = SessionProtocol.Int(f, 0) != 0;
                BroadcastPlayer(p);
                break;
            case "look" when !p.IsBot:
                // OpenSCCT's own: a look changed in the Armoury over the lobby. Kept to what the player unlocks; it is
                // worn from the next match login (MatchLoginOptions), not on a pawn already in a round.
                p.SpyLook = CosmeticLook.ParseHex(SessionProtocol.Text(f, 0)).Validate(CosmeticSide.Spy, p.Unlocks);
                p.MercLook = CosmeticLook.ParseHex(SessionProtocol.Text(f, 1)).Validate(CosmeticSide.Merc, p.Unlocks);
                break;
            case "teamvoice":
                // The lobby's "Restrict Mic to Team" (Lobby_Create.TeamVoiceOnlyChange -> ServerSetTeamVoiceOnly).
                p.TeamVoiceOnly = SessionProtocol.Int(f, 0) != 0;
                if (p.Slot?.Controller is OpenScct.Game.SBase.SPlayerController pc)
                {
                    pc.bTeamVoiceOnly = p.TeamVoiceOnly;
                }

                _relay?.Invalidate();
                BroadcastPlayer(p);
                break;
            case "mapcheck":
                if (string.Equals(SessionProtocol.Text(f, 0), Setup.Map, StringComparison.OrdinalIgnoreCase))
                {
                    var state = (MapState)Math.Clamp(SessionProtocol.Int(f, 1), 0, 3);
                    if (state != p.Map)
                    {
                        p.Map = state;
                        BroadcastPlayer(p);
                    }
                }

                break;
            case "setup" when CanControl(p) && Phase == SessionPhase.Lobby:
                ChangeSetup(p, SessionProtocol.Text(f, 0));
                break;
            case "launch" when CanLaunch(p):
                if (Phase == SessionPhase.Lobby)
                {
                    if (LaunchProblem() is { } problem)
                    {
                        SendMessage(p, "Could not start. " + problem);
                    }
                    else if (_countdownEnd < 0)
                    {
                        Announce($"{p.Name} launched the match.");
                        StartCountdown();
                    }
                    else
                    {
                        // The page button reads "Cancel" while the launch waits (Lobby_Create.ButtonLaunch).
                        _countdownEnd = -1;
                        Announce($"{p.Name} cancelled the launch.");
                        BroadcastPhase();
                    }
                }
                else if (Phase == SessionPhase.Results)
                {
                    NextRound();
                }

                break;
            case "lobby" when CanControl(p) && Phase is SessionPhase.Results or SessionPhase.Match or SessionPhase.Loading:
                BackToLobby($"{p.Name} went back to the lobby.");
                break;
            case "kick" when CanControl(p):
                if (FindPlayer(SessionProtocol.Text(f, 0)) is { } kicked && !ReferenceEquals(kicked, p))
                {
                    Kick(kicked, $"Kicked by {p.Name}.");
                }

                break;
            case "ban" when CanControl(p):
                if (FindPlayer(SessionProtocol.Text(f, 0)) is { } banned && !ReferenceEquals(banned, p))
                {
                    Ban(banned, 0, $"Banned by {p.Name}.");
                }

                break;
            case "voiceban" when CanControl(p):
                if (FindPlayer(SessionProtocol.Text(f, 0)) is { } muted && !ReferenceEquals(muted, p))
                {
                    SetVoiceBanned(muted, SessionProtocol.Int(f, 1, 1) != 0, p.Name);
                }

                break;
            case "admin":
                if (Settings.AdminPassword is { Length: > 0 } password && string.Equals(SessionProtocol.Text(f, 0) + (f.Count > 1 ? " " + f[1] : ""), password, StringComparison.Ordinal))
                {
                    p.IsAdmin = true;
                    BroadcastPlayer(p);
                    SendMessage(p, "You are an admin of this server.");
                    Log($"{p.Name} logged in as admin");
                }
                else
                {
                    SendMessage(p, "Wrong admin password.");
                }

                break;
            case "cmd" when p.IsAdmin || p.IsHost:
                foreach (string reply in Execute(SessionProtocol.Text(f, 0) + (f.Count > 1 ? " " + f[1] : string.Empty)))
                {
                    SendMessage(p, reply);
                }

                break;
        }
    }

    /// <summary>The host, an admin, or on an open dedicated server anyone, may change the setup and kick.</summary>
    private bool CanControl(SessionPlayer p) =>
        p.IsHost || p.IsAdmin || (Settings.Dedicated && Settings.OpenMapCommands && Settings.AdminPassword is not { Length: > 0 });

    private bool CanLaunch(SessionPlayer p) => CanControl(p) || (Settings.Dedicated && !p.IsSpectator);

    private void ChangeTeam(SessionPlayer p, byte team)
    {
        if (team is not (0 or 1 or SessionProtocol.SpectatorTeam) || team == p.Team)
        {
            return;
        }

        // Merc Deathmatch puts everyone on the mercenaries; Switch Team does nothing there (Lobby_Create.ButtonClick).
        if (team == 0 && Setup.Mode == OpenScct.Core.EGameMode.GM_MercDeathMatch)
        {
            SendMessage(p, "Everyone plays a mercenary in Merc Deathmatch.");
            return;
        }

        if (team == SessionProtocol.SpectatorTeam && CountPlayers(spectators: true) >= Settings.MaxSpectators)
        {
            SendMessage(p, "The spectator places are taken.");
            return;
        }

        if (p.IsSpectator && team != SessionProtocol.SpectatorTeam && CountHumans(spectators: false) >= Settings.MaxPlayers)
        {
            SendMessage(p, "The sides are full.");
            return;
        }

        // Auto balance (SGameInfo.PickTeam with bPlayersBalanceTeams): nobody moves to a side that would then have two
        // more people than the other.
        if (Setup.AutoBalance && team is 0 or 1 && !p.IsBot &&
            CountTeam(team) + 1 > CountTeam((byte)(1 - team)) - (p.Team == 1 - team ? 1 : 0) + 1)
        {
            SendMessage(p, "Auto balance is on: that side has more players.");
            return;
        }

        p.Team = team;
        p.Ready = false;
        if (p.Slot is { } slot)
        {
            slot.LoginOptions = MatchLoginOptions(p);
        }

        BroadcastPlayer(p);
        UpdateBots();
    }

    private void ChangeSetup(SessionPlayer by, string options)
    {
        LobbySetup next = LobbySetup.FromOptions(options, Setup) with { MaxPlayers = Setup.MaxPlayers };
        if (Maps.FindMap(next.Map) is not { } entry)
        {
            SendMessage(by, $"Unknown map '{next.Map}'.");
            return;
        }

        ApplySetup(next with { Map = entry.Id }, announce: false);
        Announce($"{by.Name} set up {Setup.Describe()}.");
    }

    /// <summary>
    /// Takes a new setup. Any change un-readies the players (the host need not be ready): a ready client un-readies
    /// itself when a value it was shown changes (Lobby_Create.Tick, spec/game/lobby-and-match-creation.md rule 12). A
    /// change of map or mode also clears the map checks; Merc Deathmatch moves everyone to the mercenaries
    /// (ApplyMatchRules, MoveEveryoneToMercs). The running map's replication info takes the voice values at once.
    /// </summary>
    private void ApplySetup(LobbySetup next, bool announce)
    {
        LobbySetup clamped = next.Clamped();
        bool practiceChanged = clamped.Practice != Setup.Practice;
        bool mapChanged = !string.Equals(clamped.Map, Setup.Map, StringComparison.OrdinalIgnoreCase) || clamped.Mode != Setup.Mode;
        bool changed = clamped != Setup;
        Setup = clamped;
        _countdownEnd = -1;
        if (practiceChanged)
        {
            PracticeChanged();
        }

        foreach (SessionPlayer p in _players)
        {
            if (changed && !p.IsHost && !p.IsBot)
            {
                p.Ready = false;
            }

            if (mapChanged && !p.IsBot)
            {
                p.Map = MapState.Unknown;
            }

            if (Setup.Mode == OpenScct.Core.EGameMode.GM_MercDeathMatch && p.Team == 0)
            {
                p.Team = 1;
                if (p.Slot is { } slot)
                {
                    slot.LoginOptions = MatchLoginOptions(p);
                }
            }
        }

        if (Phase == SessionPhase.Lobby && Host.Game is { } game)
        {
            Setup.ApplyTo(game.PersistentData);
            if (game.GameReplicationInfo is { } gri)
            {
                Setup.ApplyTo(gri);
                AlternateRules.ApplyTo(gri, game.PersistentData);
                foreach (SPlayerController pc in game.XLevel.AllActors<SPlayerController>())
                {
                    pc.ServerGetVoiceChatters(pc);
                }
            }

            _relay?.Invalidate();
        }

        SendSetup(null);
        foreach (SessionPlayer p in _players)
        {
            BroadcastPlayer(p);
        }

        UpdateBots();
        BroadcastPhase();
        if (announce)
        {
            Announce("Next: " + Setup.Describe() + ".");
        }

        Master?.HeartbeatSoon();
    }

    private SessionPlayer? FindPlayer(string idOrName)
    {
        if (int.TryParse(idOrName, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id) && _players.Find(p => p.Id == id) is { } byId)
        {
            return byId;
        }

        return _players.Find(p => string.Equals(p.Name, idOrName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Removes a player, telling it why first.</summary>
    public void Kick(SessionPlayer p, string reason)
    {
        ArgumentNullException.ThrowIfNull(p);
        if (p.IsBot)
        {
            // Bots follow the setup's "Bots" and the people on each side; a kicked one would only come back.
            Log($"{p.Name} is a bot: change the Bots setting instead");
            return;
        }

        Send(p, SessionProtocol.Line("kicked", reason));
        p.Kicked = true;
        _kicks.Add((p, _now + (Second / 2)));
        Log($"kick {p.Name}: {reason}");
    }

    /// <summary>Bans a player (its install id and address) for <paramref name="minutes"/> (0: for good) and kicks it.</summary>
    public void Ban(SessionPlayer p, int minutes, string reason)
    {
        ArgumentNullException.ThrowIfNull(p);
        if (p.IsBot)
        {
            Kick(p, reason);
            return;
        }

        // A signed-in player is banned by his account (with install id and address); his name is only the ban's label.
        Settings.Bans?.Add(p.InstallId, p.Address, p.Name, reason, minutes, p.Verified ? p.AccountId : null, p.PublicKey);
        Kick(p, reason);
    }

    /// <summary>
    /// Bans a player from voice, or lifts it (SPlayerController.SetVoiceBanned): the relay sends nobody his voice and
    /// him nobody's, in the lobby and in every round of the visit, until it is lifted or he leaves.
    /// </summary>
    public void SetVoiceBanned(SessionPlayer p, bool banned, string? by = null)
    {
        ArgumentNullException.ThrowIfNull(p);
        if (p.IsBot || p.VoiceBanned == banned)
        {
            return;
        }

        p.VoiceBanned = banned;
        if (p.Slot?.Controller?.PlayerReplicationInfo is { } pri)
        {
            pri.bVoiceBanned = banned;
        }

        _relay?.Invalidate();
        BroadcastPlayer(p);
        string who = by is { Length: > 0 } ? " by " + by : string.Empty;
        Announce(banned ? $"{p.Name} was banned from voice chat{who}." : $"{p.Name} may use voice chat again{who}.");
        Log((banned ? "voice ban " : "voice unban ") + p.Name);
    }

    /// <summary>Sends a line from the server to every player's chat (and the lobby box).</summary>
    public void Say(string text)
    {
        var line = new ChatLine(EChatScope.Say, 0, "Server", ChatK.NoTeam, ChatRelay.Clean(text));
        foreach (PlayerSlot slot in Host.Players)
        {
            slot.Connection.ControlChannel?.Send(ControlMessage.Chat(line));
        }

        Log("say: " + text);
    }

    // ---- commands -----------------------------------------------------------------------------------------------

    public const string CommandHelp =
        "status | players | kick <id|name> [reason] | ban <id|name> [minutes] [reason] | unban <account|player key|install id|address|name> | " +
        "voiceban <id|name> | unvoiceban <id|name> | " +
        "map <name> [mode] | mode <name> | set <time|spylives|merclives|respawn|revenge|games|maxplayers|bots|botsperside|botskill|balance|alternate|practice> <value> | set alternate <ability|spygadgetN|mercgadgetN|spyslots|mercslots> <value> | " +
        "lagcomp [on|off|reset|<ms>] | practice [on|off] | launch | lobby | restart | next | say <text> | close";

    /// <summary>
    /// Runs an admin command (the dedicated server's console, an admin's <c>cmd</c> line, a listen host) and returns
    /// what it printed.
    /// </summary>
    public IReadOnlyList<string> Execute(string commandLine)
    {
        var output = new List<string>();
        string[] parts = (commandLine ?? string.Empty).Trim().Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return output;
        }

        string verb = parts[0].ToLowerInvariant();
        string arg = parts.Length > 1 ? parts[1] : string.Empty;
        string rest = parts.Length > 2 ? parts[2] : string.Empty;
        switch (verb)
        {
            case "help":
                output.Add(CommandHelp);
                break;
            case "status":
                output.Add(Status());
                goto case "players";
            case "players":
                foreach (SessionPlayer p in _players)
                {
                    output.Add(string.Create(CultureInfo.InvariantCulture,
                        $"  {p.Id,3} {p.Name,-20} {TeamName(p.Team),-10} {(p.Ready ? "ready" : "     ")} {p.PingMs,4} ms  {p.Address}{(p.IsHost ? " host" : "")}{(p.IsAdmin ? " admin" : "")}{(p.Slot is null ? " (loading)" : "")}{(p.IsBot ? "" : p.AccountId is { } account ? $" (signed in, account {account})" : " (guest)")}{(p.PublicKey is { } key ? " key " + PlayerKey.Short(key) : "")}"));
                }

                break;
            case "kick" when FindPlayer(arg) is { } kicked:
                Kick(kicked, rest.Length > 0 ? rest : "Kicked by the admin.");
                output.Add($"kicked {kicked.Name}");
                break;
            case "ban" when FindPlayer(arg) is { } banned:
            {
                string[] r = rest.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                int minutes = 0;
                string reason = "Banned by the admin.";
                if (r.Length > 0 && int.TryParse(r[0], NumberStyles.None, CultureInfo.InvariantCulture, out int m))
                {
                    minutes = m;
                    if (r.Length > 1)
                    {
                        reason = r[1];
                    }
                }
                else if (rest.Length > 0)
                {
                    reason = rest;
                }

                Ban(banned, minutes, reason);
                output.Add(minutes > 0 ? $"banned {banned.Name} for {minutes} min" : $"banned {banned.Name}");
                break;
            }

            case "unban":
                output.Add($"removed {Settings.Bans?.Remove(arg) ?? 0} ban(s)");
                break;
            case "voiceban" or "unvoiceban" when FindPlayer(arg) is { } voiced:
                SetVoiceBanned(voiced, verb == "voiceban", "the admin");
                output.Add(verb == "voiceban" ? $"{voiced.Name} is banned from voice" : $"{voiced.Name} may talk again");
                break;
            case "voiceban" or "unvoiceban":
                output.Add($"no player '{arg}'");
                break;
            case "map" when arg.Length > 0:
            {
                LobbySetup setup = Setup;
                if (rest.Length > 0 && LobbySetup.TryParseMode(rest, out OpenScct.Core.EGameMode mode))
                {
                    setup = setup with { Mode = mode };
                }

                if (Maps.FindMap(arg) is not { } entry)
                {
                    output.Add($"unknown map '{arg}'");
                    break;
                }

                LobbySetup fresh = LobbySetup.ForMap(entry.Id, setup.Mode, Maps.GetMapSettings(entry.Id), Setup.MaxPlayers) with { AutoBalance = Setup.AutoBalance, Alternate = Setup.Alternate };
                ApplySetup(KeepPractice(fresh), announce: true);
                if (Phase != SessionPhase.Lobby)
                {
                    BackToLobby("The admin changed the map.");
                }

                output.Add("next: " + Setup.Describe());
                break;
            }

            case "mode" when LobbySetup.TryParseMode(arg + (rest.Length > 0 ? " " + rest : ""), out OpenScct.Core.EGameMode newMode):
                ApplySetup(KeepPractice(LobbySetup.ForMap(Setup.Map, newMode, Maps.GetMapSettings(Setup.Map), Setup.MaxPlayers) with { AutoBalance = Setup.AutoBalance, Alternate = Setup.Alternate }), announce: true);
                output.Add("next: " + Setup.Describe());
                break;
            case "lagcomp":
                output.Add(LagCompensationCommand(arg) ?? LagCompensationUsage);
                break;
            case "set" when arg.Equals("lagcomp", StringComparison.OrdinalIgnoreCase):
                output.Add(LagCompensationCommand(rest) ?? LagCompensationUsage);
                break;
            case "set" when SetOption(arg, rest, out string? problem):
                output.Add("next: " + Setup.Describe());
                break;
            case "set":
                output.Add("usage: set <time|spylives|merclives|respawn|revenge|games|maxplayers|bots|botsperside|botskill|balance|alternate|practice|lagcomp> <value>, or set alternate <spythermal|spynight|spygrab|spyelbow|spydrop|mercemf|mercmotion|mercelbow|mercdrop|merctorch|merclaser|mercberserk|merccharge|cinematic|spygadget1-8|mercgadget1-8|spyslots|mercslots> <value>");
                break;
            case "practice":
                if (arg.Length > 0 && SetOption("practice", arg, out _))
                {
                    output.Add(Setup.Practice ? "practice on" : "practice off");
                }
                else
                {
                    output.Add(Setup.Practice ? "practice is on (practice on|off)" : "practice is off (practice on|off)");
                }

                break;
            case "launch":
                if (Phase == SessionPhase.Lobby)
                {
                    if (LaunchProblem() is { } launchProblem && launchProblem == "Nobody is on a side.")
                    {
                        output.Add("could not start: " + launchProblem);
                    }
                    else
                    {
                        Launch();
                        output.Add("launching " + Setup.Describe());
                    }
                }
                else if (Phase == SessionPhase.Results)
                {
                    NextRound();
                    output.Add("next round");
                }

                break;
            case "lobby":
                BackToLobby("The admin went back to the lobby.");
                output.Add("back to the lobby");
                break;
            case "restart" when Phase != SessionPhase.Lobby:
                BackToLobby("The admin restarted the round.");
                _relaunchAt = _now + (5 * Second);
                output.Add("restarting: back to the lobby, then the same round again once everyone is back");
                break;
            case "restart":
                output.Add("no round to restart (use launch)");
                break;
            case "next" when Settings.Rotation.Count > 0:
                _rotationIndex = (_rotationIndex + 1) % Settings.Rotation.Count;
                ApplySetup(KeepPractice(Settings.Rotation[_rotationIndex]), announce: true);
                if (Phase != SessionPhase.Lobby)
                {
                    BackToLobby(null);
                }

                output.Add("next: " + Setup.Describe());
                break;
            case "say" when arg.Length > 0:
                Say(arg + (rest.Length > 0 ? " " + rest : string.Empty));
                Announce("Server: " + arg + (rest.Length > 0 ? " " + rest : string.Empty));
                break;
            case "close" or "quit" or "exit":
                Close();
                output.Add("closing");
                break;
            default:
                output.Add($"unknown command '{commandLine}' ({CommandHelp})");
                break;
        }

        return output;
    }

    private const string LagCompensationUsage = "lagcomp: !lagcomp [on|off|reset|<ceiling>]";

    /// <summary>
    /// Lag compensation (rl:net.lagcomp, docs/NETCODE.md): <c>on</c>, <c>off</c>, <c>reset</c> (the default ceiling) or
    /// a ceiling in ms (which turns it on); nothing only reports. Changes the running round at once and every later
    /// round, and tells everyone. Returns the setting's line, or null for a bad argument.
    /// </summary>
    private string? LagCompensationCommand(string argument)
    {
        LagCompensation lag = Host.LagCompensation;
        string before = lag.Describe();
        if (lag.Command(argument) is not { } line)
        {
            return null;
        }

        _lagCompensation = lag.Enabled;
        _lagCompensationCeilingMs = lag.CeilingMs;
        if (line != before)
        {
            Announce(line);
            Log(line);
        }

        return line;
    }

    private bool SetOption(string name, string value, out string? problem)
    {
        problem = null;
        bool isInt = int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n);
        bool isBool = value is "1" or "on" or "true" or "0" or "off" or "false";
        bool on = value is "1" or "on" or "true";
        LobbySetup s = Setup;
        LobbySetup? next = name.ToLowerInvariant() switch
        {
            "time" or "timelimit" when isInt => s with { TimeLimit = n },
            "spylives" or "livesatt" when isInt => s with { SpyLives = n },
            "merclives" or "livesdef" when isInt => s with { MercLives = n },
            "respawn" when isInt => s with { RespawnSeconds = n },
            "score" or "killlimit" when isInt => s with { ScoreLimit = n },
            "revenge" when isBool => s with { Revenge = on },
            "games" or "gamesinarow" when isInt => s with { GamesInARow = n },
            "maxplayers" when isInt => s with { MaxPlayers = n },
            "bots" => value.ToLowerInvariant() switch
            {
                "off" or "0" => s with { Bots = BotFill.Off },
                "side" or "perside" or "1" => s with { Bots = BotFill.PerSide },
                "fill" or "2" => s with { Bots = BotFill.Fill },
                _ => null,
            },
            "botsperside" when isInt => s with { BotsPerSide = n },
            "practice" when isBool => s with { Practice = on },
            "botskill" => Enum.TryParse(value, ignoreCase: true, out OpenScct.Game.AI.AiSkill skill) ? s with { BotSkill = skill } : null,
            "balance" or "autobalance" when isBool => s with { AutoBalance = on },
            "alternate" when isBool => s with { Alternate = s.Alternate with { On = on } },
            "alternate" => SetAlternate(s, value),
            _ => null,
        };
        if (next is null)
        {
            problem = "bad option";
            return false;
        }

        ApplySetup(next, announce: true);
        return true;
    }

    /// <summary>The ability names of <c>set alternate &lt;name&gt; &lt;on|off&gt;</c>.</summary>
    public static IReadOnlyList<(string Key, AlternateAbility Ability)> AlternateKeys { get; } =
    [
        ("spythermal", AlternateAbility.SpyThermalVision), ("spynight", AlternateAbility.SpyNightVision),
        ("spygrab", AlternateAbility.SpyGrab), ("spyelbow", AlternateAbility.SpyElbow), ("spydrop", AlternateAbility.SpyLethalDrop),
        ("mercemf", AlternateAbility.MercEmfVision), ("mercmotion", AlternateAbility.MercMotionTracking),
        ("mercelbow", AlternateAbility.MercElbow), ("mercdrop", AlternateAbility.MercLethalDrop), ("merctorch", AlternateAbility.MercTorch),
        ("merclaser", AlternateAbility.MercLaser), ("mercberserk", AlternateAbility.MercBerserk), ("merccharge", AlternateAbility.MercCharge),
        ("cinematic", AlternateAbility.CinematicCamera),
    ];

    /// <summary>
    /// <c>set alternate &lt;item&gt; &lt;value&gt;</c>: an ability (<see cref="AlternateKeys"/>) on or off, a gadget
    /// (<c>spygadget1</c> to <c>spygadget8</c>, <c>mercgadget1</c> to <c>mercgadget8</c>, in the side's gear order)
    /// allowed or not, or a side's gadget slots (<c>spyslots</c>, <c>mercslots</c>, 0 to 4). Null for anything else.
    /// </summary>
    private static LobbySetup? SetAlternate(LobbySetup s, string value)
    {
        string[] p = value.ToLowerInvariant().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (p.Length != 2)
        {
            return null;
        }

        bool on = p[1] is "1" or "on" or "true" or "yes";
        bool isBool = on || p[1] is "0" or "off" or "false" or "no";
        bool isInt = int.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int n);
        AlternateRules a = s.Alternate;
        foreach ((string key, AlternateAbility ability) in AlternateKeys)
        {
            if (p[0] == key && isBool)
            {
                return s with { Alternate = a.With(ability, on) };
            }
        }

        foreach ((string prefix, int team) in new[] { ("spygadget", 0), ("mercgadget", 1) })
        {
            if (p[0].StartsWith(prefix, StringComparison.Ordinal) && isBool &&
                int.TryParse(p[0].AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out int g) && g is >= 1 and <= AlternateRules.GadgetOptions)
            {
                return s with { Alternate = a.WithGadget(team, g - 1, on) };
            }
        }

        return p[0] switch
        {
            "spyslots" when isInt => s with { Alternate = a with { SpyGadgetCount = (byte)Math.Clamp(n, 0, AlternateRules.MaxGadgetCount) } },
            "mercslots" when isInt => s with { Alternate = a with { MercGadgetCount = (byte)Math.Clamp(n, 0, AlternateRules.MaxGadgetCount) } },
            _ => null,
        };
    }

    private static string TeamName(byte team) => team switch
    {
        0 => "spy",
        1 => "merc",
        _ => "spectator",
    };

    /// <summary>One line about the session for the console and the admin status.</summary>
    public string Status()
    {
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"{Settings.Name}: {SessionProtocol.PhaseName(Phase)}, {Setup.Describe()}, ");
        sb.Append(CultureInfo.InvariantCulture, $"{CountPlayers(false)}/{Settings.MaxPlayers} players, {CountPlayers(true)} spectators");
        if (GamePort > 0)
        {
            sb.Append(CultureInfo.InvariantCulture, $", UDP {GamePort}");
        }

        if (Master is { } m)
        {
            sb.Append(m.Registered ? $", master {m.ServerId} ({m.Reachability})" : ", master: " + (m.LastError ?? "registering"));
        }

        return sb.ToString();
    }

    // ---- chat commands --------------------------------------------------------------------------------------------

    /// <summary>
    /// Chat commands (FEATURES rl:qol.chatcmds): <c>!map</c>, <c>!next</c>, <c>!prev</c> (<c>!back</c>), <c>!pings</c>,
    /// <c>!resign</c>, <c>!login</c>, <c>!help</c>, for the host and admins <c>!voiceban</c> and <c>!unvoiceban</c>, and on
    /// a practice server <c>!heal</c>, <c>!regen</c>, <c>!tank</c>, <c>!kill</c> (rl:net.practice). Map commands are open
    /// to everyone on a dedicated server without an admin password, to the host and admins otherwise; the others act on
    /// the sender only. Every other line is chat.
    /// </summary>
    private bool OnChatCommand(PlayerSlot slot, string text)
    {
        if (!text.StartsWith('!') || slot.Tag is not SessionPlayer p)
        {
            return false;
        }

        string[] parts = text[1..].Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        string verb = parts.Length > 0 ? parts[0].ToLowerInvariant() : string.Empty;
        string arg = parts.Length > 1 ? parts[1].Trim() : string.Empty;
        switch (verb)
        {
            case "help":
                SendMessage(p, "!map <name> [mode], !next, !prev, !pings, !resign, !lagcomp [on|off|reset|<ms>], !login <password>" +
                    (CanControl(p) ? ", !voiceban <name>, !unvoiceban <name>" : string.Empty) +
                    (Setup.Practice ? ", " + PracticeHelp : string.Empty));
                return true;
            case "lagcomp":
                if (arg.Length > 0 && !CanControl(p))
                {
                    SendMessage(p, "lagcomp: server-side setting");
                    SendMessage(p, Host.LagCompensation.Describe());
                    return true;
                }

                SendMessage(p, LagCompensationCommand(arg) ?? LagCompensationUsage);
                return true;
            case "pings":
                foreach (SessionPlayer other in _players)
                {
                    SendMessage(p, string.Create(CultureInfo.InvariantCulture,
                        $"{other.Name}: ping {other.PingMs} ms, jitter {JitterMs(other)} ms, loss {Loss(other):F0}%"));
                }

                return true;
            case "resign":
                // Gives up the round by spending the player's lives (SPlayerController.ServerResign, "has resigned").
                if (Phase != SessionPhase.Match || slot.Controller is not SPlayerController resigning)
                {
                    SendMessage(p, "There is no round to resign from.");
                    return true;
                }

                resigning.ServerResign();
                return true;
            case "voiceban" or "unvoiceban":
                if (!CanControl(p))
                {
                    SendMessage(p, "Only the host or an admin may ban players from voice.");
                    return true;
                }

                foreach (string line in Execute(verb + " " + arg))
                {
                    SendMessage(p, line);
                }

                return true;
            case "login":
                OnSessionLine(slot.Connection, slot, SessionProtocol.Line("admin", arg));
                return true;
            case "map" when arg.Length > 0:
                if (!CanControl(p))
                {
                    SendMessage(p, "Only the host or an admin may change the map.");
                    return true;
                }

                foreach (string line in Execute("map " + arg))
                {
                    SendMessage(p, line);
                }

                return true;
            case "next" or "prev" or "back":
                if (!CanControl(p))
                {
                    SendMessage(p, "Only the host or an admin may change the map.");
                    return true;
                }

                StepMap(verb == "next" ? 1 : -1);
                return true;
            default:
                return OnPracticeCommand(p, verb);
        }
    }

    /// <summary>The connection's smoothed round trip jitter (mean absolute change between samples), in milliseconds.</summary>
    private static long JitterMs(SessionPlayer p) => p.Slot is { } s ? s.Connection.Stats.JitterMicros / 1000 : 0;

    private static float Loss(SessionPlayer p) => p.Slot is { } s ? Math.Max(s.Connection.InLoss, s.Connection.OutLoss) * 100f : 0f;

    /// <summary>The next or previous map of the rotation, or of every installed map when there is no rotation.</summary>
    private void StepMap(int step)
    {
        if (Settings.Rotation.Count > 0)
        {
            _rotationIndex = ((_rotationIndex + step) % Settings.Rotation.Count + Settings.Rotation.Count) % Settings.Rotation.Count;
            ApplySetup(KeepPractice(Settings.Rotation[_rotationIndex]), announce: true);
        }
        else
        {
            // The host list in its order (docs/MAPS.md "Order"): never a tutorial, front end or test map.
            IReadOnlyList<string> maps = Maps.RotationMaps();
            int at = 0;
            for (int i = 0; i < maps.Count; i++)
            {
                if (string.Equals(maps[i], Setup.Map, StringComparison.OrdinalIgnoreCase))
                {
                    at = i;
                }
            }

            string map = maps[((at + step) % maps.Count + maps.Count) % maps.Count];
            ApplySetup(KeepPractice(LobbySetup.ForMap(map, Setup.Mode, Maps.GetMapSettings(map), Setup.MaxPlayers) with { AutoBalance = Setup.AutoBalance, Alternate = Setup.Alternate }), announce: true);
        }

        if (Phase != SessionPhase.Lobby)
        {
            BackToLobby(null);
        }
    }
}
