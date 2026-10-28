using System;
using System.Collections.Generic;
using System.Globalization;
using OpenScctGame.Hud;

namespace OpenScctGame.UI;

/// <summary>A player in the lobby.</summary>
public sealed record LobbyPlayer(string Name, HudSide Side, bool Ready, bool IsLocal, bool IsBot);

/// <summary>
/// The lobby of a match before it starts: who is in, on which side, and who is ready. The host starts the match.
/// A networked lobby comes from the play entry (<see cref="IPlayEntry.CreateLobby"/>); otherwise the front end
/// uses <see cref="LocalLobby"/>.
/// </summary>
public interface ILobbySession
{
    bool IsHost { get; }

    string Map { get; }

    IReadOnlyList<LobbyPlayer> Players { get; }

    /// <summary>A line about the session (address, waiting for the host...).</summary>
    string Status { get; }

    /// <summary>True when the host may start: every human player ready and both sides filled.</summary>
    bool CanStart { get; }

    /// <summary>Raised when the list or the status changes.</summary>
    event Action? Changed;

    void SetSide(HudSide side);

    void SetReady(bool ready);

    /// <summary>Starts the match (host only).</summary>
    void Start(Godot.SceneTree tree);

    /// <summary>Leaves the lobby (closes the session).</summary>
    void Leave();
}

/// <summary>A line of the lobby's message box: chat with its sender, or a system line.</summary>
public sealed record LobbyMessage(string Text, string? Sender, HudSide? Side, bool TeamOnly);

/// <summary>A player as an online lobby lists it: the base line, its id, ping, host mark and whether it has the map.</summary>
public sealed record OnlineLobbyPlayer(int Id, string Name, HudSide? Side, bool Ready, bool IsLocal, bool IsHost, int PingMs, string MapNote,
    bool TeamVoiceOnly = false, bool IsBot = false, bool IsVerified = false, int Rank = 0, bool VoiceBanned = false);

/// <summary>
/// An online lobby (docs/ONLINE.md): on top of the sides and ready flags, the server and its setup (map, mode and the
/// match creation page's values, which the host or an admin may change), spectators, pings, the map check, chat and
/// system lines, kicking, and the launch countdown.
/// </summary>
public interface IOnlineLobbySession : ILobbySession
{
    string ServerName { get; }

    /// <summary>The match the lobby sets up.</summary>
    OpenScct.Server.Online.LobbySetup Setup { get; }

    /// <summary>The local player may change the setup, launch and kick.</summary>
    bool CanControl { get; }

    /// <summary>Seconds left of the launch countdown, or 0.</summary>
    float Countdown { get; }

    IReadOnlyList<OnlineLobbyPlayer> Members { get; }

    IReadOnlyList<LobbyMessage> Messages { get; }

    /// <summary>The installed maps the host can pick (id and title).</summary>
    IReadOnlyList<(string Id, string Title)> Maps { get; }

    void Spectate();

    void ApplySetup(OpenScct.Server.Online.LobbySetup setup);

    /// <summary>The map settings' values for a map and mode (what the setup panel resets to when either changes).</summary>
    OpenScct.Server.Online.LobbySetup DefaultsFor(string map, OpenScct.Core.EGameMode mode);

    void Chat(string text, bool teamOnly);

    void Kick(int playerId);

    /// <summary>Bans a player from the server (his account, install id and address) and kicks him.</summary>
    void Ban(int playerId);

    /// <summary>Bans a player from voice chat for the visit, or lifts it.</summary>
    void SetVoiceBanned(int playerId, bool banned);

    /// <summary>A map's settings (its modes and their values), for the Session Parameters rows.</summary>
    OpenScct.Game.SBase.USMapSettings? MapSettings(string map);

    /// <summary>The host's launch countdown is running (the host's page button cancels it).</summary>
    bool Launching { get; }

    // ---- voice (spec/game/lobby-and-match-creation.md rules 16, 21 and 22) ----

    /// <summary>The local player's "Restrict Mic to Team".</summary>
    bool TeamVoiceOnly { get; }

    void SetTeamVoiceOnly(bool only);

    /// <summary>The local player's volume for another player's voice, 0 to 2.</summary>
    float VoiceVolume(string playerName);

    void SetVoiceVolume(string playerName, float volume);

    bool VoiceMuted(string playerName);

    void SetVoiceMuted(string playerName, bool muted);

    /// <summary>Whether a player's voice is heard now (the local player: whether he transmits).</summary>
    bool Speaking(string playerName);

    /// <summary>A line about the local microphone (level, transmitting or silent, push to talk or activation).</summary>
    string MicrophoneLine { get; }

    // ---- the setup's map, when the local player lacks it (docs/ONLINE.md "Missing maps") ----

    /// <summary>What to show about fetching the setup's map, or null when there is nothing to show.</summary>
    LobbyMapFetch? MapFetch { get; }

    /// <summary>Downloads and prepares the setup's map from where the host got it.</summary>
    void StartMapFetch();

    void CancelMapFetch();

    /// <summary>Called every frame by the screen: once a fetch is done, the map list is read again and the map checked.</summary>
    void UpdateMapFetch();
}

/// <summary>
/// The lobby's line about the setup's map when the local player lacks it: the text, whether Download can be pressed,
/// whether a fetch runs (Stop then) and how far it is, and whether the text is a problem.
/// </summary>
public sealed record LobbyMapFetch(string Text, bool CanStart, bool Running, double Fraction, bool Problem);

/// <summary>
/// The lobby of an offline or locally hosted match: the local player and bots that fill the sides, two a side for
/// the slice's 2v2.
/// </summary>
public sealed class LocalLobby : ILobbySession
{
    public const int PerSide = 2;

    private readonly List<LobbyPlayer> _players = new();
    private readonly PlayRequest _request;
    private readonly bool _hostedOnline;

    public LocalLobby(PlayRequest request, bool hostedOnline = false)
    {
        ArgumentNullException.ThrowIfNull(request);
        _request = request;
        _hostedOnline = hostedOnline;
        _players.Add(new LobbyPlayer(request.PlayerName, request.Side, false, true, false));
        FillBots();
    }

    public bool IsHost => true;

    public string Map => _request.Map;

    public IReadOnlyList<LobbyPlayer> Players => _players;

    public string Status => _hostedOnline
        ? string.Create(CultureInfo.InvariantCulture, $"Hosting on port {_request.Port}")
        : "Offline match";

    public bool CanStart => _players.TrueForAll(p => p.IsBot || p.Ready) && Count(HudSide.Spy) > 0 && Count(HudSide.Merc) > 0;

    public event Action? Changed;

    public LobbyPlayer Local => _players.Find(p => p.IsLocal)!;

    public void SetSide(HudSide side)
    {
        int i = _players.FindIndex(p => p.IsLocal);
        _players[i] = _players[i] with { Side = side, Ready = false };
        FillBots();
        Changed?.Invoke();
    }

    public void SetReady(bool ready)
    {
        int i = _players.FindIndex(p => p.IsLocal);
        _players[i] = _players[i] with { Ready = ready };
        Changed?.Invoke();
    }

    public void Start(Godot.SceneTree tree)
    {
        if (!CanStart)
        {
            return;
        }

        PlayRequest request = _request with { Side = Local.Side, Bots = _players.Count - 1 };
        if (_hostedOnline)
        {
            PlayEntry.Current.Host(tree, request);
        }
        else
        {
            PlayEntry.Current.PlayOffline(tree, request);
        }
    }

    public void Leave()
    {
    }

    private int Count(HudSide side) => _players.FindAll(p => p.Side == side).Count;

    /// <summary>Bots fill each side up to <see cref="PerSide"/> players.</summary>
    private void FillBots()
    {
        _players.RemoveAll(p => p.IsBot);
        int n = 1;
        foreach (HudSide side in new[] { HudSide.Spy, HudSide.Merc })
        {
            while (Count(side) < PerSide && _players.Count < _request.Bots + 1)
            {
                string name = "Bot" + n.ToString(CultureInfo.InvariantCulture);
                _players.Add(new LobbyPlayer(name, side, true, false, true));
                n++;
            }
        }
    }
}
