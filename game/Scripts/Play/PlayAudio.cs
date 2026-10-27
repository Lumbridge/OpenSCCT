using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Godot;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.SBase;
using OpenScct.Server;
using OpenScctGame.Audio;
using OpenScctGame.Play.Session;

namespace OpenScctGame.Play;

/// <summary>
/// The play scene's sound: a <see cref="SoundSystem"/> with the map's audio (the shared library and the map's
/// table), bound to the client's world once its map is loaded (actor locations, ambient loops, zones, surfaces),
/// fed with the client world's events through a <see cref="PlayAudioRouter"/> (installed as the world's event
/// sink before the HUD wraps it), and listening from the play camera every frame. The server's sounds reach the
/// client's world as ClientHearSound calls (<see cref="NetHearSound"/>), the interface sounds as ClientPlaySound.
/// <para>
/// <c>SVM_AUDIO_MUTE=1</c> turns the Master bus 80 dB down (the mix still runs to the output and is metered),
/// <c>SVM_AUDIO_LOG=1</c> prints the voices and the bus peaks every 10 s.
/// </para>
/// </summary>
public sealed partial class PlayAudio : Node
{
    private const double LogSeconds = 10.0;

    private PlaySession? _session;
    private Func<(FVector Location, FRotator Rotation)?>? _view;
    private bool _bound;
    private bool _log;
    private bool _quiet;
    private double _sinceLog;

    public PlayAudio()
    {
        Name = "PlayAudio";
    }

    public SoundSystem? Sound { get; private set; }

    public PlayAudioRouter? Router { get; private set; }

    public AudioLibrary? Library { get; private set; }

    /// <summary>The loudest bus outputs since the scene started (sampled every frame).</summary>
    public AudioPeaks Peaks { get; } = new();

    /// <summary>Why the scene plays no sound, or null.</summary>
    public string? Problem { get; private set; }

    /// <summary>
    /// Adds the sound of <paramref name="session"/> to <paramref name="parent"/>, listening from <paramref name="view"/>
    /// (the camera's location and rotation in UE space, null while there is none). Returns null, with a printed
    /// reason, when the map has no converted audio.
    /// </summary>
    public static PlayAudio? Attach(Node parent, PlaySession session, Func<(FVector Location, FRotator Rotation)?> view)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(view);
        string shared = AudioLibrary.SharedDirFor(session.MapFolder);
        if (!File.Exists(Path.Combine(shared, "audio.json")))
        {
            GD.Print($"SVM_AUDIO no sound: no converted audio at {shared} (python -m svmbuild audio audio-map --maps {session.Settings.Map})");
            return null;
        }

        var audio = new PlayAudio { _session = session, _view = view };
        audio.Library = AudioLibrary.Load(shared, session.MapFolder);
        audio.Sound = new SoundSystem { Name = "SoundSystem" };
        audio.AddChild(audio.Sound);
        parent.AddChild(audio);
        audio.Sound.Bind(audio.Library, null, seed: (int)(session.Settings.MatchSeed & 0x7fffffff));

        // The router becomes the world's sink now, before the HUD wraps whatever sink is there; it also takes the
        // sounds of the calls received before a step (ClientHearSound, ClientPlaySound) and of the input commands.
        audio.Router = PlayAudioRouter.Install(session.Client, audio.Sound);
        if (audio.Library.Map is null)
        {
            audio.Problem = "the map has no audio table (python -m svmbuild audio-map)";
        }

        audio._log = System.Environment.GetEnvironmentVariable("SVM_AUDIO_LOG") is "1" or "true";
        if (System.Environment.GetEnvironmentVariable("SVM_AUDIO_MUTE") is "1" or "true")
        {
            audio._quiet = true;
            AudioPeaks.Quiet();
        }

        GD.Print($"SVM_AUDIO {audio.Library.Count} sounds, {audio.Library.Map?.Zones.Count ?? 0} zones, {audio.Sound.Buses?.ReverbBusCount ?? 0} reverb buses, " +
                 $"output {AudioServer.OutputDevice} at {AudioServer.GetMixRate()} Hz, Master {AudioServer.GetBusVolumeDb(0):F0} dB{(AudioServer.IsBusMute(0) ? " (muted)" : "")}");
        return audio;
    }

    /// <summary>
    /// A gadget page's sound (the heart sensor's heartbeat and scan ping): 2D with no actor, as the HUD natives play
    /// it on the player's own machine (AudioK.HudSoundRadius).
    /// </summary>
    public void PlayHudSound(AssetRef sound)
    {
        if (!sound.IsNone)
        {
            Sound?.PlaySound(0, sound.Path!, 0, false, FVector.Zero, 1f, AudioK.HudSoundRadius, 1f, attenuate: false, flat: true);
        }
    }

    /// <summary>Gives the sound the client world once its map is in (apart from _Process: its lambda's closure would be made every frame).</summary>
    private void Bind(PlaySession session, SoundSystem sound)
    {
        if (session.Client.Map is { } map)
        {
            _bound = true;
            Level level = session.Client.World.Level;
            sound.SetWorld(new LevelAudioWorld(level, map.Physics?.Collision, a => map.Loaded?.RecordOf(a)?.Id));
        }
    }

    public override void _Process(double delta)
    {
        if (_session is not { } session || Sound is not { } sound)
        {
            return;
        }

        if (!_bound)
        {
            Bind(session, sound);
        }

        if (_view?.Invoke() is { } v)
        {
            sound.SetListener(v.Location, v.Rotation);
        }

        // SetAudioZoom: the local player's scope or binoculars (AudioZoomFactor is 0 outside them).
        if (session.LocalController is { } pc)
        {
            sound.SetAudioZoom(pc.AudioZoomFactor, pc.FovAngle);
        }

        if (_quiet)
        {
            AudioPeaks.Quiet();
        }

        Peaks.Sample();
        if (_log && (_sinceLog += delta) >= LogSeconds)
        {
            _sinceLog = 0;
            GD.Print(Report());
        }
    }

    /// <summary>One line: voices, starts, the router's counts and the bus peaks.</summary>
    public string Report()
    {
        SoundSystem? s = Sound;
        PlayAudioRouter? r = Router;
        string top = s is null ? "" : string.Join(",", s.Calls.GroupBy(c => c.Sound[(c.Sound.LastIndexOf('.') + 1)..])
            .OrderByDescending(g => g.Count()).Take(8).Select(g => g.Key + "=" + g.Count()));
        return string.Create(CultureInfo.InvariantCulture,
            $"SVM_AUDIO voices={s?.ActiveVoices} started={s?.StartCount} silent={s?.Silent} unknown={s?.Unknown.Count} zone={s?.ListenerZone} own={r?.OwnPlayed} heard={r?.HeardPlayed} duplicates={r?.Duplicates} calls: {top} peaks: {Peaks.Summary()}");
    }

    public override void _ExitTree()
    {
        Router?.Uninstall();

        Sound?.StopAll();
        Library?.ReleaseStreams();
        _session = null;
    }
}
