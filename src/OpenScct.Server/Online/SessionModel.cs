using System;
using System.Collections.Generic;
using OpenScct.Net.Discovery;

namespace OpenScct.Server.Online;

/// <summary>A player of the session as every client sees it in the lobby and on the scoreboard.</summary>
public sealed record LobbyPlayer(int Id, string Name, byte Team, SessionPlayerFlags Flags, int PingMs, MapState Map)
{
    /// <summary>The player's rank with its shown title (OpenScct.Net.Cosmetics.PlayerRank packed; 0 for guests and bots).</summary>
    public int Rank { get; init; }

    public bool Ready => (Flags & SessionPlayerFlags.Ready) != 0;

    public bool IsHost => (Flags & SessionPlayerFlags.Host) != 0;

    public bool IsAdmin => (Flags & SessionPlayerFlags.Admin) != 0;

    /// <summary>The player restricted his microphone to his team in the lobby.</summary>
    public bool TeamVoiceOnly => (Flags & SessionPlayerFlags.TeamVoice) != 0;

    /// <summary>The host or an admin banned this player from voice: nobody hears him and he hears nobody.</summary>
    public bool VoiceBanned => (Flags & SessionPlayerFlags.VoiceBanned) != 0;

    /// <summary>The server lets this player change the setup and launch.</summary>
    public bool CanControl => (Flags & (SessionPlayerFlags.Control | SessionPlayerFlags.Host | SessionPlayerFlags.Admin)) != 0;

    public bool CanModerate => IsHost || IsAdmin;

    public bool IsSpectator => Team == SessionProtocol.SpectatorTeam;

    /// <summary>A server bot, not a person (no ping, no voice, no volume slider).</summary>
    public bool IsBot => (Flags & SessionPlayerFlags.Bot) != 0;

    /// <summary>A signed-in player (the name is the account's, checked by the server).</summary>
    public bool IsVerified => (Flags & SessionPlayerFlags.Verified) != 0;
}

/// <summary>One line of the results table.</summary>
/// <remarks><paramref name="Feats"/> are the round's achievement feats (FeatKeys), server-side only: not sent to clients.</remarks>
public sealed record ResultRow(int Id, string Name, byte Team, int Score, int Kills, int Deaths, int Lives,
    IReadOnlyDictionary<string, int>? Feats = null);

/// <summary>
/// The results of a round: the winning side and reason (EEndReason: 0 objectives or kill limit, 1 time out, 2 a side
/// eliminated), the mission's objectives, the players' lines, and how many games of the series remain.
/// </summary>
public sealed record MatchResults
{
    public string Map { get; init; } = string.Empty;

    /// <summary>0 spies, 1 mercs, 255 none (the round was stopped).</summary>
    public int WinningTeam { get; init; } = 255;

    /// <summary>EEndReason, or 255 when the round was stopped.</summary>
    public int WinningReason { get; init; } = 255;

    public int ObjectivesTaken { get; init; }

    public int ObjectivesNeeded { get; init; }

    /// <summary>Games of the series still to play after this one (0: the lobby is next unless a rematch is asked).</summary>
    public int GamesLeft { get; init; }

    public IReadOnlyList<ResultRow> Rows { get; init; } = [];

    /// <summary>The end-menu wording of the reason ("All objectives taken", "Mission timer elapsed", "Team eradicated").</summary>
    public static string ReasonText(int reason) => reason switch
    {
        0 => "All objectives taken",
        1 => "Mission timer elapsed",
        2 => "Team eradicated",
        _ => "Round stopped",
    };
}

/// <summary>A line for the lobby's message box: chat (with its sender) or a system message.</summary>
public sealed record SessionMessage(string Text, string? Sender = null, byte Team = 255, bool TeamOnly = false)
{
    public bool IsSystem => Sender is null;
}

/// <summary>The lobby as a client holds it: the server, the match setup, the players and the phase.</summary>
public sealed class LobbyState
{
    public bool SidearmsUnrestricted { get; internal set; }
    private readonly List<LobbyPlayer> _players = new();
    private readonly List<SessionMessage> _messages = new();

    public string ServerName { get; internal set; } = string.Empty;

    public bool Dedicated { get; internal set; }

    public int MaxPlayers { get; internal set; }

    /// <summary>This client's player id in the session, or -1 before the server said hello.</summary>
    public int LocalId { get; internal set; } = -1;

    public LobbySetup Setup { get; internal set; } = new();

    /// <summary>The server's content hash of the setup's map.</summary>
    public ulong SetupContentHash { get; internal set; }

    /// <summary>Where the setup's map can be downloaded (the host downloaded it from SCCT-Maps), or null.</summary>
    public MapSourceInfo? SetupSource { get; internal set; }

    public SessionPhase Phase { get; internal set; }

    /// <summary>Seconds left of the launch countdown or of the results, or 0.</summary>
    public float Countdown { get; internal set; }

    public IReadOnlyList<LobbyPlayer> Players => _players;

    /// <summary>Lobby chat and system messages, oldest first (the last 200).</summary>
    public IReadOnlyList<SessionMessage> Messages => _messages;

    public LobbyPlayer? Local => _players.Find(p => p.Id == LocalId);

    public bool IsHost => Local?.IsHost ?? false;

    /// <summary>The local player may change the setup and launch: the host, or an admin.</summary>
    public bool CanControl => Local?.CanControl ?? false;

    public int Count(byte team) => _players.FindAll(p => p.Team == team).Count;

    /// <summary>The people of the lobby (the bots left out).</summary>
    public int HumanCount => _players.FindAll(p => !p.IsBot).Count;

    internal void Upsert(LobbyPlayer player)
    {
        int i = _players.FindIndex(p => p.Id == player.Id);
        if (i >= 0)
        {
            _players[i] = player with { Rank = player.Rank != 0 ? player.Rank : _players[i].Rank };
        }
        else
        {
            _players.Add(player);
        }
    }

    internal void Remove(int id) => _players.RemoveAll(p => p.Id == id);

    /// <summary>A "rank" line: the rank of a player already listed.</summary>
    internal void SetRank(int id, int rank)
    {
        int i = _players.FindIndex(p => p.Id == id);
        if (i >= 0)
        {
            _players[i] = _players[i] with { Rank = rank };
        }
    }

    internal void AddMessage(SessionMessage message)
    {
        _messages.Add(message);
        if (_messages.Count > 200)
        {
            _messages.RemoveAt(0);
        }
    }

    internal void ClearPlayers() => _players.Clear();
}
