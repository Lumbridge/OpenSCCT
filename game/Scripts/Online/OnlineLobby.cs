using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using OpenScct.Core;
using OpenScct.Net.Discovery;
using OpenScct.Server;
using OpenScct.Server.Online;
using OpenScctGame.Hud;
using OpenScctGame.UI;
using UiLobbyPlayer = OpenScctGame.UI.LobbyPlayer;

namespace OpenScctGame.Online;

/// <summary>The lobby screen's view of an online visit: the server's lobby as <see cref="OnlineClient.Lobby"/> holds it.</summary>
public sealed class OnlineLobby : IOnlineLobbySession
{
    private readonly OnlineService _service;
    private readonly OnlineClient _client;
    private readonly List<(string Id, string Title)> _maps = new();

    public OnlineLobby(OnlineService service, OnlineClient client)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _client = client ?? throw new ArgumentNullException(nameof(client));
        // The host list in its order with the names the original shows (docs/MAPS.md "The map catalog").
        foreach (string id in client.Maps.RotationMaps())
        {
            _maps.Add((id, MapTitles.Of(client.Maps, id)));
        }

        _service.Changed += Raise;
    }

    private LobbyState State => _client.Lobby;

    public bool IsHost => State.IsHost;

    public bool CanControl => State.CanControl;

    public string Map => State.Setup.Map;

    public string ServerName => State.ServerName;

    public LobbySetup Setup => State.Setup;

    public float Countdown => State.Countdown;

    public IReadOnlyList<(string Id, string Title)> Maps => _maps;

    public IReadOnlyList<UiLobbyPlayer> Players
    {
        get
        {
            var list = new List<UiLobbyPlayer>();
            foreach (OpenScct.Server.Online.LobbyPlayer p in State.Players)
            {
                if (!p.IsSpectator)
                {
                    list.Add(new UiLobbyPlayer(p.Name, p.Team == 1 ? HudSide.Merc : HudSide.Spy, p.Ready, p.Id == State.LocalId, false));
                }
            }

            return list;
        }
    }

    public IReadOnlyList<OnlineLobbyPlayer> Members
    {
        get
        {
            var list = new List<OnlineLobbyPlayer>();
            foreach (OpenScct.Server.Online.LobbyPlayer p in State.Players)
            {
                string note = p.Map switch
                {
                    MapState.Missing => "missing map",
                    MapState.Differs => "different map version",
                    _ => string.Empty,
                };
                HudSide? side = p.IsSpectator ? null : p.Team == 1 ? HudSide.Merc : HudSide.Spy;
                list.Add(new OnlineLobbyPlayer(p.Id, p.Name, side, p.Ready, p.Id == State.LocalId, p.IsHost, p.PingMs, note, p.TeamVoiceOnly, p.IsBot, p.IsVerified, p.Rank, p.VoiceBanned));
            }

            return list;
        }
    }

    public IReadOnlyList<LobbyMessage> Messages
    {
        get
        {
            var list = new List<LobbyMessage>();
            foreach (string note in _service.Notes)
            {
                list.Add(new LobbyMessage(note, null, null, false));
            }

            foreach (SessionMessage m in State.Messages)
            {
                HudSide? side = m.Team switch
                {
                    0 => HudSide.Spy,
                    1 => HudSide.Merc,
                    _ => null,
                };
                list.Add(new LobbyMessage(m.Text, m.Sender, side, m.TeamOnly));
            }

            return list;
        }
    }

    public string Status
    {
        get
        {
            string host = State.Dedicated ? "Dedicated server" : IsHost ? "You host" : "Hosted by a player";
            string count = State.Countdown > 0 ? $" - starting in {Math.Ceiling(State.Countdown)} s" : string.Empty;
            return $"{State.ServerName} ({host}) - {State.Setup.Describe()}{count}";
        }
    }

    /// <summary>The host or a dedicated lobby may start once everyone on a side is ready (the server checks again).</summary>
    public bool CanStart => (CanControl || State.Dedicated) && State.Players.Count > 0;

    public event Action? Changed;

    private void Raise() => Changed?.Invoke();

    public void SetSide(HudSide side) => _client.SetTeam(side == HudSide.Merc ? 1 : 0);

    public void Spectate() => _client.SetTeam(SessionProtocol.SpectatorTeam);

    public void SetReady(bool ready) => _client.SetReady(ready);

    public void Start(SceneTree tree) => _client.Launch();

    public void Leave()
    {
        _fetch?.Cancel();
        _service.Changed -= Raise;
        _service.Leave();
    }

    public void ApplySetup(LobbySetup setup) => _client.SetSetup(setup);

    public LobbySetup DefaultsFor(string map, EGameMode mode) =>
        LobbySetup.ForMap(map, mode, _client.Maps.GetMapSettings(map), State.Setup.MaxPlayers) with
        {
            Revenge = State.Setup.Revenge,
            GamesInARow = State.Setup.GamesInARow,
        };

    public void Chat(string text, bool teamOnly) => _client.Chat(text, teamOnly);

    public void Kick(int playerId) => _client.Kick(playerId);

    public void Ban(int playerId) => _client.Ban(playerId);

    public void SetVoiceBanned(int playerId, bool banned) => _client.SetVoiceBanned(playerId, banned);

    public OpenScct.Game.SBase.USMapSettings? MapSettings(string map) => _client.Maps.GetMapSettings(map);

    public bool Launching => State.Phase == SessionPhase.Lobby && State.Countdown > 0;

    public bool TeamVoiceOnly => State.Local?.TeamVoiceOnly ?? false;

    public void SetTeamVoiceOnly(bool only) => _client.SetTeamVoiceOnly(only);

    private OpenScct.Server.Online.OnlineVoice? Voice => _service.VoiceChat?.Voice;

    public float VoiceVolume(string playerName) => Voice?.VolumeOf(playerName) ?? 1f;

    public void SetVoiceVolume(string playerName, float volume) => Voice?.SetVolume(playerName, volume);

    public bool VoiceMuted(string playerName) => Voice?.IsMuted(playerName) ?? false;

    public void SetVoiceMuted(string playerName, bool muted) => Voice?.SetMuted(playerName, muted);

    public bool Speaking(string playerName) =>
        State.Local is { } local && string.Equals(local.Name, playerName, StringComparison.Ordinal)
            ? Voice?.Voice.Transmitting ?? false
            : Voice?.IsSpeaking(playerName) ?? false;

    public string MicrophoneLine => _service.VoiceChat?.MicrophoneLine() ?? string.Empty;

    // ---- the setup's map, when this player lacks it ----

    private OpenScctGame.Online.MapFetch? _fetch;

    public LobbyMapFetch? MapFetch
    {
        get
        {
            if (_fetch is { } fetch)
            {
                (MapFetchStage stage, string text, double fraction) = fetch.Snapshot();
                return stage switch
                {
                    MapFetchStage.Listing or MapFetchStage.Downloading or MapFetchStage.Converting => new LobbyMapFetch(text, false, true, fraction, false),
                    MapFetchStage.Done => new LobbyMapFetch(text, false, false, 1, false),
                    _ => new LobbyMapFetch(text, MapOffered, false, 0, stage == MapFetchStage.Failed),
                };
            }

            return MapOffered
                ? new LobbyMapFetch($"You don't have the map {State.Setup.Map}. The host got it from the community maps: download it to play.",
                    true, false, 0, false)
                : null;
        }
    }

    /// <summary>The local player lacks the setup's map (or has another version) and the host said where it is.</summary>
    private bool MapOffered =>
        State.SetupSource is not null && State.Local is { Map: MapState.Missing or MapState.Differs };

    public void StartMapFetch()
    {
        if (_fetch is { Finished: false } || State.SetupSource is not { } source)
        {
            return;
        }

        _fetch = OpenScctGame.Online.MapFetch.Start(State.Setup.Map, source);
    }

    public void CancelMapFetch() => _fetch?.Cancel();

    public void UpdateMapFetch()
    {
        if (_fetch is not { Finished: true } fetch)
        {
            return;
        }

        if (fetch.Snapshot().Stage == MapFetchStage.Done)
        {
            // The new map is on disk: read the content folders again and tell the server.
            _client.Maps.Refresh();
            if (!ReferenceEquals(_service.Catalog.Maps, _client.Maps))
            {
                _service.Catalog.Maps.Refresh();
            }

            _fetch = null;
            _client.RecheckMap();
            Raise();
        }
        else if (!string.Equals(fetch.Map, State.Setup.Map, StringComparison.OrdinalIgnoreCase))
        {
            // A failed or stopped fetch of a map the host has since changed away from.
            _fetch = null;
        }
    }

    /// <summary>The results screen's table from a round's results.</summary>
    public static MatchResult ToMatchResult(OnlineClient client, MatchResults results)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(results);
        var rows = new List<MatchResultRow>();
        foreach (ResultRow r in results.Rows)
        {
            int rank = client.Lobby.Players.FirstOrDefault(p => p.Id == r.Id)?.Rank ?? 0;
            rows.Add(new MatchResultRow(r.Name, r.Team == 1 ? HudSide.Merc : HudSide.Spy, r.Score, r.Kills, r.Deaths, 0, r.Id == client.Lobby.LocalId,
                rank));
        }

        return new MatchResult
        {
            Map = results.Map,
            Winner = results.WinningTeam switch
            {
                0 => HudSide.Spy,
                1 => HudSide.Merc,
                _ => null,
            },
            Reason = results.WinningReason switch
            {
                0 => MatchEndReason.Story,
                1 => MatchEndReason.Timeout,
                2 => MatchEndReason.TeamDeath,
                _ => MatchEndReason.Aborted,
            },
            LocalSide = RoundResultSide.LocalTeam(results, client.Lobby.LocalId, client.Lobby.Local?.Team) == 1 ? HudSide.Merc : HudSide.Spy,
            ObjectivesTaken = results.ObjectivesTaken,
            ObjectivesTotal = results.ObjectivesNeeded,
            Rows = rows,
        };
    }
}
