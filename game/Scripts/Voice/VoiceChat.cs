using System;
using System.Collections.Generic;
using Godot;
using OpenScct.Net;
using OpenScct.Net.Voice;

namespace OpenScctGame.Voice;

/// <summary>A row of the voice overlay: one player who is speaking, or muted by the local player.</summary>
/// <param name="Talker">The player id (PlayerReplicationInfo.PlayerID).</param>
/// <param name="Name">The player's name, or "Player n" until the name is known.</param>
/// <param name="Team">The player's team index, -1 when unknown.</param>
/// <param name="IsLocal">The local player's own microphone.</param>
/// <param name="Speaking">Transmitting (local) or being heard (remote).</param>
/// <param name="Channel">All or team.</param>
/// <param name="Muted">Muted by the local player (for the local row: the microphone is muted).</param>
/// <param name="Volume">The local player's volume for this talker, 0 to 2.</param>
public readonly record struct VoiceSpeaker(ushort Talker, string Name, int Team, bool IsLocal, bool Speaking, EVoiceChannel Channel, bool Muted,
    float Volume);

/// <summary>
/// The local player's voice chat in the game (docs/VOICE.md): owns the <see cref="VoiceClient"/> and the
/// <see cref="VoiceAudio"/> node, reads the voice keys each frame, and keeps the speaker list the HUD draws.
/// <para>For the HUD: subscribe to <see cref="SpeakerChanged"/> (raised on the main thread when a row starts or stops
/// speaking, changes channel or is muted) or read <see cref="Speakers"/> each frame. <see cref="VoiceIndicator"/> is a
/// plain overlay that does this until the Magma HUD draws its own.</para>
/// </summary>
public partial class VoiceChat : Node
{
    public const string NodeName = "VoiceChat";

    private readonly List<VoiceSpeaker> _speakers = new();
    private VoiceClient? _client;

    /// <summary>The player's voice settings (the Reloaded voice keys).</summary>
    public VoiceSettings Settings { get; } = new();

    public VoiceClient? Client => _client;

    public VoiceAudio? Audio { get; private set; }

    /// <summary>Name and team of a player id, from the client's replicated players; null when unknown.</summary>
    public Func<ushort, (string Name, int Team)?>? ResolvePlayer { get; set; }

    /// <summary>The local player's id once its controller replicated (0 until then).</summary>
    public Func<ushort>? ResolveLocalTalker { get; set; }

    /// <summary>Read the voice keys from the InputMap (off for smoke runs and bots, which set the keys directly).</summary>
    public bool ReadKeys { get; set; } = true;

    /// <summary>Speaking or muted players, local first, then in the order they first spoke.</summary>
    public IReadOnlyList<VoiceSpeaker> Speakers => _speakers;

    /// <summary>Raised when a row of <see cref="Speakers"/> changes.</summary>
    public event Action<VoiceSpeaker>? SpeakerChanged;

    /// <summary>Talks to a server over the game's transport (a client of a remote host).</summary>
    public VoiceClient Connect(VoiceTransport transport, EndpointId server, IClock clock)
    {
        VoiceClient client = Create(clock);
        client.Connect(transport, server);
        return client;
    }

    /// <summary>Talks to the listen server in this process.</summary>
    public VoiceClient Connect(VoiceLocalLink link, IClock clock)
    {
        VoiceClient client = Create(clock);
        client.Connect(link);
        return client;
    }

    public void Disconnect()
    {
        if (_client is not null)
        {
            _client.ActivityChanged -= OnActivity;
            _client.Dispose();
            _client = null;
        }

        if (Audio is not null)
        {
            Audio.Client = null;
        }

        _speakers.Clear();
    }

    public override void _Ready()
    {
        VoiceInput.EnsureActions();
        Audio = new VoiceAudio { Name = "VoiceAudio", Client = _client };
        AddChild(Audio);
    }

    public override void _Process(double delta)
    {
        VoiceClient? client = _client;
        if (client is null)
        {
            return;
        }

        if (ResolveLocalTalker is not null)
        {
            client.LocalTalker = ResolveLocalTalker();
        }

        if (ReadKeys)
        {
            client.TalkKey = Input.IsActionPressed(VoiceInput.PushToTalk);
            client.TeamKey = Input.IsActionPressed(VoiceInput.TeamPushToTalk);
            if (Input.IsActionJustPressed(VoiceInput.ToggleMute))
            {
                Settings.MicMuted = !Settings.MicMuted;
            }
        }

        VoiceAudio.SetVolume(Settings.OutputVolume);
        client.Update();
    }

    public override void _ExitTree() => Disconnect();

    /// <summary>Mutes or unmutes a player for the local player (the overlay's click).</summary>
    public void SetMuted(ushort talker, bool muted) => _client?.SetMuted(talker, muted);

    /// <summary>Sets the local player's volume for a player (the overlay's scroll wheel).</summary>
    public void SetVolume(ushort talker, float volume)
    {
        _client?.SetVolume(talker, volume);
        int i = _speakers.FindIndex(s => s.Talker == talker && !s.IsLocal);
        if (i >= 0)
        {
            _speakers[i] = _speakers[i] with { Volume = Math.Clamp(volume, 0f, 2f) };
            SpeakerChanged?.Invoke(_speakers[i]);
        }
    }

    private VoiceClient Create(IClock clock)
    {
        Disconnect();
        _client = new VoiceClient(Settings, clock);
        _client.ActivityChanged += OnActivity;
        if (Audio is not null)
        {
            Audio.Client = _client;
            _client.SetCaptureRate(Audio.CaptureRate > 0 ? Audio.CaptureRate : (int)AudioServer.GetMixRate());
        }

        return _client;
    }

    private void OnActivity(VoiceActivity activity)
    {
        (string Name, int Team)? who = activity.IsLocal || activity.Talker == 0 ? null : ResolvePlayer?.Invoke(activity.Talker);
        string name = activity.IsLocal ? "You" : who?.Name ?? "Player " + activity.Talker.ToString(System.Globalization.CultureInfo.InvariantCulture);
        float volume = activity.IsLocal ? 1f : _client?.FindTalker(activity.Talker)?.Volume ?? 1f;
        var row = new VoiceSpeaker(activity.Talker, name, who?.Team ?? -1, activity.IsLocal, activity.Speaking, activity.Channel, activity.Muted, volume);
        int i = _speakers.FindIndex(s => s.IsLocal == row.IsLocal && (row.IsLocal || s.Talker == row.Talker));
        bool keep = row.Speaking || row.Muted;
        if (i >= 0 && !keep)
        {
            _speakers.RemoveAt(i);
        }
        else if (i >= 0)
        {
            _speakers[i] = row;
        }
        else if (keep)
        {
            _speakers.Insert(row.IsLocal ? 0 : _speakers.Count, row);
        }

        SpeakerChanged?.Invoke(row);
    }
}
