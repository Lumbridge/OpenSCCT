using System;
using System.Collections.Generic;
using OpenScct.Core.Engine;
using OpenScct.Net;
using OpenScct.Net.Voice;

namespace OpenScct.Server.Online;

/// <summary>
/// A player's voice chat for a whole online visit (docs/VOICE.md, docs/MENUS.md): keeps one <see cref="VoiceClient"/>
/// connected to each map's voice transport as the visit travels (lobby, rounds, lobby again), tells it the local
/// player's id, and applies the player's own choices for each other player: a volume (0 to 2, Reloaded's "scroll to
/// adjust") and a mute (rl:voice.mix). Choices are kept by player name, because talker ids change with the map: in the
/// lobby a talker is the session player id, in a round the PlayerReplicationInfo id (plus one, <see cref="VoiceK.TalkerOf"/>)
/// that each map's server hands out afresh; names are unique in a session.
/// <para>Godot-free and single-threaded: the game's front end, the bots and the tests call <see cref="Update"/> once
/// per frame on the visit's thread, after the visit's tick.</para>
/// </summary>
public sealed class OnlineVoice : IDisposable
{
    private readonly OnlineClient _client;
    private readonly VoiceVolumes _volumes;
    private readonly HashSet<string> _muted = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<ushort, string> _names = new();
    private VoiceTransport? _connected;
    private EndpointId _server;

    public OnlineVoice(OnlineClient client, VoiceClient voice, VoiceVolumes? volumes = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        Voice = voice ?? throw new ArgumentNullException(nameof(voice));
        _volumes = volumes ?? new VoiceVolumes();
    }

    /// <summary>The voice client: settings, keys, capture in, talkers out.</summary>
    public VoiceClient Voice { get; }

    /// <summary>Connected to the current map's server.</summary>
    public bool IsConnected => _connected is not null && Voice.IsConnected;

    /// <summary>Follows the visit to its current map, refreshes the talker names and applies the volumes and mutes.</summary>
    public void Update()
    {
        VoiceTransport? transport = _client.Voice;
        EndpointId server = _client.ServerEndpoint;
        if (!ReferenceEquals(transport, _connected) || server != _server)
        {
            if (transport is not null && server.IsValid)
            {
                Voice.Connect(transport, server);
                _connected = transport;
                _server = server;
            }
            else if (_connected is not null)
            {
                Voice.Disconnect();
                _connected = null;
                _server = EndpointId.None;
            }
        }

        ClientSession? session = _client.Session;
        bool lobby = _client.CurrentMap is null || MapCatalog.IsMenuMap(_client.CurrentMap);
        if (lobby)
        {
            Voice.LocalTalker = _client.Lobby.LocalId is > 0 and <= ushort.MaxValue ? (ushort)_client.Lobby.LocalId : (ushort)0;
            _names.Clear();
            foreach (LobbyPlayer p in _client.Lobby.Players)
            {
                if (p.Id is > 0 and <= ushort.MaxValue)
                {
                    _names[(ushort)p.Id] = p.Name;
                }
            }
        }
        else
        {
            Voice.LocalTalker = session?.LocalPlayer?.PlayerReplicationInfo is { } own ? VoiceK.TalkerOf(own.PlayerID) : (ushort)0;
            RefreshNames(session);
        }

        foreach ((ushort talker, string name) in _names)
        {
            if (talker == Voice.LocalTalker)
            {
                continue;
            }

            Voice.SetVolume(talker, VolumeOf(name));
            Voice.SetMuted(talker, _muted.Contains(name));
        }

        Voice.Update();
    }

    /// <summary>This player's volume for <paramref name="playerName"/>, 0 to 2 (1 unless set).</summary>
    public float VolumeOf(string playerName) => _volumes.VolumeOf(playerName);

    /// <summary>Sets this player's volume for <paramref name="playerName"/>'s voice, 0 to 2, in the shared preferences.</summary>
    public void SetVolume(string playerName, float volume)
    {
        ArgumentNullException.ThrowIfNull(playerName);
        _volumes.SetVolume(playerName, volume);
        if (TalkerOf(playerName) is { } talker)
        {
            Voice.SetVolume(talker, VolumeOf(playerName));
        }
    }

    public bool IsMuted(string playerName) => _muted.Contains(playerName ?? string.Empty);

    /// <summary>Mutes or unmutes <paramref name="playerName"/> for this player; the server stops sending him.</summary>
    public void SetMuted(string playerName, bool muted)
    {
        ArgumentNullException.ThrowIfNull(playerName);
        if (muted)
        {
            _muted.Add(playerName);
        }
        else
        {
            _muted.Remove(playerName);
        }

        if (TalkerOf(playerName) is { } talker)
        {
            Voice.SetMuted(talker, muted);
        }
    }

    /// <summary>Whether <paramref name="playerName"/>'s voice is being heard now.</summary>
    public bool IsSpeaking(string playerName) => TalkerOf(playerName) is { } t && Voice.FindTalker(t) is { Speaking: true };

    /// <summary>The talker id of a player on the current map, or null.</summary>
    public ushort? TalkerOf(string playerName)
    {
        foreach ((ushort talker, string name) in _names)
        {
            if (string.Equals(name, playerName, StringComparison.OrdinalIgnoreCase))
            {
                return talker;
            }
        }

        return null;
    }

    /// <summary>The name of a talker on the current map, or null.</summary>
    public string? NameOf(ushort talker) => _names.GetValueOrDefault(talker);

    private void RefreshNames(ClientSession? session)
    {
        _names.Clear();
        if (session is null)
        {
            return;
        }

        // Every replicated PlayerReplicationInfo (a client's GRI does not list them all).
        foreach (PlayerReplicationInfo pri in session.World.Level.AllActors<PlayerReplicationInfo>())
        {
            if (!pri.bDeleteMe && pri.PlayerName is { Length: > 0 } && VoiceK.TalkerOf(pri.PlayerID) is > 0 and var talker)
            {
                _names[talker] = pri.PlayerName;
            }
        }
    }

    public void Dispose()
    {
        Voice.Disconnect();
        _connected = null;
    }
}
