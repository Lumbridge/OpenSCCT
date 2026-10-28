using System;
using OpenScct.Maps;
using System.Globalization;
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
        // A map download carries on: the map is kept for next time.
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

    public void SendLook()
    {
        OpenScctGame.Options.GameSettings s = OpenScctGame.Options.SettingsStore.Current;
        _client.SetLook(s.SharedLook(OpenScct.Net.Cosmetics.CosmeticSide.Spy).ToHex(), s.SharedLook(OpenScct.Net.Cosmetics.CosmeticSide.Merc).ToHex());
    }

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

    private ContentPacks.DownloadJob? _job;
    private int _generation = ContentPacks.Generation;

    /// <summary>
    /// The line under the lobby status when the local player lacks the setup's map (or has another version): the map
    /// from OpenSCCT-Content (docs/CONTENT-PACKS.md) whose content hash is the host's, with a Download button and the
    /// download's progress, or why there is none to download.
    /// </summary>
    public LobbyMapFetch? MapFetch
    {
        get
        {
            if (_job is { } job && string.Equals(job.Map?.Code, State.Setup.Map, StringComparison.OrdinalIgnoreCase))
            {
                if (job.Running)
                {
                    return new LobbyMapFetch(job.Describe(), false, true, job.Fraction, false);
                }

                if (job.Done)
                {
                    return State.Local is { Map: MapState.Missing or MapState.Differs }
                        ? new LobbyMapFetch($"{job.Title} downloaded; checking it against the host's...", false, false, 1, false)
                        : null;
                }

                if (job.Error == "stopped")
                {
                    return new LobbyMapFetch($"Stopped downloading {job.Title}.", true, false, 0, false);
                }

                return new LobbyMapFetch($"Could not download {job.Title}: {job.Error}", true, false, 0, true);
            }

            if (State.Local is not { Map: MapState.Missing or MapState.Differs })
            {
                return null;
            }

            string map = State.Setup.Map;
            if (Downloadable(out ContentMap? pack, out string? why))
            {
                string verb = State.Local.Map == MapState.Missing ? "You don't have" : "Your copy differs from the host's for";
                return new LobbyMapFetch(
                    string.Create(CultureInfo.InvariantCulture, $"{verb} {pack!.DisplayTitle}. Download it ({pack.Full.Size / 1048576.0:0} MB) to play."),
                    true, false, 0, false);
            }

            return why is null ? null : new LobbyMapFetch(why, false, false, 0, true);
        }
    }

    /// <summary>
    /// The setup's map as OpenSCCT-Content has it, when downloading it would give the host's version: the index's
    /// copy has the host's content hash, and a copy converted from the player's own install (which wins over a
    /// download) does not stand in the way. Otherwise <paramref name="why"/> says what the player can do, or is null
    /// while the index is still loading.
    /// </summary>
    private bool Downloadable(out ContentMap? pack, out string? why)
    {
        pack = null;
        why = null;
        string map = State.Setup.Map;
        string? hostHash = State.SetupContentHash != 0
            ? State.SetupContentHash.ToString("x16", CultureInfo.InvariantCulture)
            : null;
        if (ContentPacks.Index is not { } index)
        {
            why = ContentPacks.IndexError is { } error
                ? $"You don't have the host's {map}, and the map list could not be read: {error}"
                : null;
            return false;
        }

        ContentMap? found = index.FindMap(map, hostHash);
        if (found is null)
        {
            why = $"You don't have the host's {map}, and OpenSCCT-Content does not have it. Ask the host to share it.";
            return false;
        }

        if (hostHash is not null && !string.Equals(found.ContentHash, hostHash, StringComparison.OrdinalIgnoreCase))
        {
            why = $"The host's {found.DisplayTitle} is a different version from OpenSCCT-Content's. Ask the host to share theirs.";
            return false;
        }

        // A map converted from the player's install is found before downloaded ones, so a download would not replace it.
        if (State.Local?.Map == MapState.Differs && _client.Maps.FindFolder(map) is { } folder &&
            !System.IO.Path.GetFullPath(folder).StartsWith(System.IO.Path.GetFullPath(ContentPacks.Store.SvMapRoot), StringComparison.OrdinalIgnoreCase))
        {
            why = $"Your copy of {found.DisplayTitle} (converted from your game) differs from the host's.";
            return false;
        }

        pack = found;
        return true;
    }

    public void StartMapFetch()
    {
        if (_job is { Running: true } || !Downloadable(out ContentMap? pack, out _))
        {
            return;
        }

        _job = ContentPacks.Download(pack!);
    }

    public void CancelMapFetch() => _job?.Cancel();

    public void UpdateMapFetch()
    {
        if (ContentPacks.Generation != _generation)
        {
            // A map came in: read the content folders again and tell the server.
            _generation = ContentPacks.Generation;
            _client.Maps.Refresh();
            if (!ReferenceEquals(_service.Catalog.Maps, _client.Maps))
            {
                _service.Catalog.Maps.Refresh();
            }

            _client.RecheckMap();
            Raise();
        }

        if (_job is { Running: false } job && !string.Equals(job.Map?.Code, State.Setup.Map, StringComparison.OrdinalIgnoreCase))
        {
            // A finished, failed or stopped download of a map the host has since changed away from.
            _job = null;
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
            Mode = client.Lobby.Setup.Mode,
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
