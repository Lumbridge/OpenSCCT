using System;
using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Net;
using OpenScct.Net.Voice;
using OpenScct.Net.Voice.Proximity;

namespace OpenScct.Server.Hub;

/// <summary>
/// A player's voice chat in the hub (docs/HUB.md): one <see cref="VoiceClient"/> on the visit's voice transport, the
/// local player's talker id, each other player's volume and mute (kept by name, as in an online visit), and proximity
/// playback: every frame each talker's gain and low-pass come from the distance between the listener's ear (the camera,
/// or the local pawn's view point) and the talker's pawn, muffled when the world's BSP is between them
/// (<see cref="ProximityPlayback"/>). The keys and the transmit gate are the voice client's own, so push to talk and
/// voice activation work as in a match.
/// <para>Godot-free and single-threaded: the game's hub, the tests and the bots call <see cref="Update"/> once per
/// frame on the visit's thread.</para>
/// </summary>
public sealed class HubVoice : IDisposable
{
    private readonly HubClient _client;
    private readonly VoiceVolumes _volumes;
    private readonly HashSet<string> _muted = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<ushort, string> _names = new();
    private readonly HubVoicePositions _positions;
    private readonly Func<FVector, FVector, bool> _blocked;
    private PhysicsLineOfSight? _lineOfSight;
    private MapInfo? _lineOfSightMap;
    private bool _connected;

    public HubVoice(HubClient client, VoiceClient voice, ProximityVoiceSettings? settings = null, VoiceVolumes? volumes = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        Voice = voice ?? throw new ArgumentNullException(nameof(voice));
        _volumes = volumes ?? new VoiceVolumes();
        _positions = new HubVoicePositions(client.Session.World);
        Playback = new ProximityPlayback(settings);
        _blocked = Blocked;
    }

    /// <summary>The voice client: settings, keys, capture in, talkers out.</summary>
    public VoiceClient Voice { get; }

    /// <summary>The proximity mixes of the talkers (diagnostics and tests).</summary>
    public ProximityPlayback Playback { get; }

    /// <summary>Talkers' positions as this client sees them.</summary>
    public IVoicePositions Positions => _positions;

    /// <summary>Connected to the hub's voice relay.</summary>
    public bool IsConnected => _connected && Voice.IsConnected;

    /// <summary>The ear position of the last <see cref="Update"/>, or null when the listener had none.</summary>
    public FVector? LastEar { get; private set; }

    /// <summary>
    /// Connects once the session has its server endpoint, refreshes the local talker id and the names, applies the
    /// volumes and mutes, updates the voice client, then each talker's proximity mix as heard from
    /// <paramref name="ear"/> (null: the local pawn's view point).
    /// </summary>
    public void Update(FVector? ear, float dt)
    {
        if (!_connected && _client.Voice is { } transport && _client.ServerEndpoint.IsValid)
        {
            Voice.Connect(transport, _client.ServerEndpoint);
            _connected = true;
        }

        ClientSession session = _client.Session;
        Voice.LocalTalker = session.LocalPlayer?.PlayerReplicationInfo is { } own ? VoiceK.TalkerOf(own.PlayerID) : (ushort)0;
        RefreshNames(session);
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
        LastEar = ear ?? (session.LocalPlayer?.Pawn is { bDeleteMe: false } pawn ? HubVoicePositions.EyeOf(pawn) : null);
        Playback.Update(Voice, LastEar, _positions, _blocked, dt);
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

    /// <summary>The talker id of a player in the hub, or null.</summary>
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

    /// <summary>The name of a talker in the hub, or null.</summary>
    public string? NameOf(ushort talker) => _names.GetValueOrDefault(talker);

    /// <summary>The proximity mix <paramref name="playerName"/> is heard with now (silent when unknown).</summary>
    public VoiceMix MixOf(string playerName) => TalkerOf(playerName) is { } t ? Playback.Listener.MixOf(t) : VoiceMix.Silent;

    private bool Blocked(FVector from, FVector to)
    {
        MapInfo? map = _client.Session.Map;
        if (!ReferenceEquals(map, _lineOfSightMap))
        {
            _lineOfSightMap = map;
            _lineOfSight = map?.Physics is { } physics ? new PhysicsLineOfSight(physics) : null;
        }

        return _lineOfSight is not null && _lineOfSight.IsBlockedByWorld(from, to);
    }

    private void RefreshNames(ClientSession session)
    {
        _names.Clear();
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
        ProximityPlayback.Release(Voice);
        Voice.Disconnect();
        _connected = false;
    }
}
