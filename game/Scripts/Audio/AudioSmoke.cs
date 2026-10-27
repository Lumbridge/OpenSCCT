using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Movement;
using OpenScctGame.Render;
using OpenScctGame.Smoke;

namespace OpenScctGame.Audio;

/// <summary>
/// <c>--svm-smoke=audio</c>: loads SteeE's converted audio (the shared library and the map's bank) and the map's
/// simulation through <see cref="PawnRig"/>, binds a <see cref="SoundSystem"/> to the rig's world as its event sink,
/// and plays a footstep (the merc's PlayFootStepRight port, a switch sound that finds the floor's surface), a rifle
/// shot (the merc rifle's FireSingleShotSound, a sequence) and a hack beep through the simulation's SimEvents. It
/// then runs the level for a while with the listener in the merc's spawn. Passes when every file loads, all
/// three calls start a sound, no event names an unknown sound and the zone ambience and reverb are set up. Needs
/// <c>python -m svmbuild audio audio-map --maps SteeE</c> (it fails with that hint otherwise); headless it runs on
/// Godot's dummy audio driver.
/// </summary>
public sealed class AudioSmoke : SmokeTest
{
    public const string TestName = "audio";
    public const string MapId = "SteeE";

    [OpenScct.Provenance.Provenance(OpenScct.Provenance.ProvenanceKind.Decoded, "SWeaponDefense defaults FireSingleShotSound (shared/audio classes)")]
    public const string RifleShot = "Armes_impacts.Armes.RA_GunDef_1shot";

    [OpenScct.Provenance.Provenance(OpenScct.Provenance.ProvenanceKind.Decoded, "Armes_impacts.Gadgets: the hacking beeps of RA_Hack_try")]
    public const string HackBeep = "Armes_impacts.Gadgets.Hack_try1";

    /// <summary>The merc's spawn in SteeE (tools/golden/poses/SteeE.json, merc-spawn): pawn location and yaw.</summary>
    private static readonly FVector MercSpawn = new(-1547.605f, -2329.877f, -1041.977f);

    private const int MercSpawnYaw = -53636;

    private const int RunFrames = 90;

    /// <summary>Frames between stopping the voices and passing, so the audio server lets go of the playbacks.</summary>
    private const int TeardownFrames = 10;

    private SmokeResult _final;

    private int _phase;
    private int _frames;
    private PawnRig? _rig;
    private SoundSystem? _sound;
    private AudioLibrary? _library;
    private readonly List<string> _notes = [];

    public override string Name => TestName;

    public override SmokeResult Step(SmokeContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        try
        {
            return _phase switch
            {
                0 => Load(context),
                1 => Play(),
                2 => Run(),
                _ => ++_frames < TeardownFrames ? SmokeResult.Running : _final,
            };
        }
        catch (Exception e) when (e is IOException or InvalidDataException or InvalidOperationException or KeyNotFoundException or ArgumentException)
        {
            return SmokeResult.Fail($"{e.GetType().Name}: {e.Message}");
        }
    }

    private SmokeResult Load(SmokeContext context)
    {
        string? folder = MapBuilder.FindMapFolder(MapId, context.App.Options.MapRoots);
        if (folder is null)
        {
            return SmokeResult.Fail($"no converted {MapId} (python -m svmbuild maps --maps {MapId})");
        }

        string shared = AudioLibrary.SharedDirFor(folder);
        if (!File.Exists(Path.Combine(shared, "audio.json")) || !File.Exists(Path.Combine(folder, "audio", "audio.json")))
        {
            return SmokeResult.Fail($"no converted audio for {MapId} (python -m svmbuild audio audio-map --maps {MapId})");
        }

        var clock = System.Diagnostics.Stopwatch.StartNew();
        _library = AudioLibrary.Load(shared, folder);
        if (_library.Map is null || _library.Map.Zones.Count == 0)
        {
            return SmokeResult.Fail("the map's audio.json has no zones");
        }

        // The merc stands (the crouched and walking footstep sounds do not exist in the original's packages; the run
        // does) and carries the rifle.
        _rig = PawnRig.ForMap(PawnTeam.Merc, folder);
        _rig.Teleport(MercSpawn - new FVector(0f, 0f, _rig.Pawn.CollisionHeight), yaw: MercSpawnYaw);
        _rig.WaitStill(maxFrames: 180);
        _sound = new SoundSystem { Name = "SoundSystem" };
        context.Tree.Root.AddChild(_sound);
        MapLoadResultIds ids = new(_rig);
        _sound.Bind(_library, new LevelAudioWorld(_rig.Level, _rig.Collision, ids.Of), seed: 3);
        _rig.World.EventSink = _sound;
        Listen();
        _notes.Add($"library {_library.Count} sounds, {_library.Map.Zones.Count} zones, {_sound.Buses!.ReverbBusCount} reverb buses " +
                   $"({clock.ElapsedMilliseconds} ms with the map), output {AudioServer.OutputDevice} at {AudioServer.GetMixRate()} Hz");
        _phase = 1;
        return SmokeResult.Running;
    }

    private SmokeResult Play()
    {
        PawnRig rig = _rig!;
        SoundSystem sound = _sound!;
        // Calls made between ticks land in the buffer of the last tick: hand over just the new ones.
        int before = rig.World.Events.Count;

        // A running step: the walk-cycle notify on the right foot (SW_FootRun and the gear sound).
        rig.Pawn.Velocity = new FVector(rig.Pawn.RunningSpeed, 0f, 0f);
        rig.Pawn.PlayFootStepRight();
        rig.Pawn.Velocity = default;
        int afterStep = rig.World.Events.Count;

        // The merc rifle's shot (SWeaponDefense.FireSingleShotSound) and a hack beep, at the pawn.
        rig.Pawn.PlaySound(new AssetRef(RifleShot), ESoundSlot.SLOT_None, null, false, 2000f);
        rig.Pawn.PlaySound(new AssetRef(HackBeep), ESoundSlot.SLOT_Interact, 1f, false, 80f);
        ReadOnlySpan<SimEvent> fresh = rig.World.Events.Current[before..];
        if (afterStep - before < 2)
        {
            return SmokeResult.Fail($"the running footstep raised {afterStep - before} events (the step and the gear expected); " +
                $"crouched {rig.Pawn.bIsCrouched}, physics {rig.Pawn.Physics}; " + string.Join("; ", _notes));
        }

        int raised = fresh.Length;
        sound.OnSimEvents(rig.World.TickCount, fresh);
        sound.Update(0.0);
        string[] started = [.. sound.Started];
        _notes.Add($"events {raised}, started {string.Join(", ", started)}");
        if (!started.Any(s => s.StartsWith("Sound_Persos.FootSteps.", StringComparison.OrdinalIgnoreCase)))
        {
            return SmokeResult.Fail("no footstep started: " + string.Join(", ", started));
        }

        if (!started.Any(s => s.StartsWith("Armes_impacts.Armes.", StringComparison.OrdinalIgnoreCase)))
        {
            return SmokeResult.Fail("no rifle shot started: " + string.Join(", ", started));
        }

        if (!started.Contains(HackBeep, StringComparer.OrdinalIgnoreCase))
        {
            return SmokeResult.Fail("no hack beep started: " + string.Join(", ", started));
        }

        _phase = 2;
        return SmokeResult.Running;
    }

    private SmokeResult Run()
    {
        PawnRig rig = _rig!;
        SoundSystem sound = _sound!;
        rig.Step(new PlayerInputFrame());
        Listen();
        if (++_frames < RunFrames)
        {
            return SmokeResult.Running;
        }

        if (sound.Unknown.Count > 0)
        {
            return SmokeResult.Fail("unknown sounds: " + string.Join(", ", sound.Unknown.Distinct()));
        }

        if (_library!.LoadErrors.Count > 0)
        {
            return SmokeResult.Fail("load errors: " + string.Join("; ", _library.LoadErrors.Take(5)));
        }

        if (sound.ListenerZone is null)
        {
            return SmokeResult.Fail("the listener has no zone");
        }

        _notes.Add($"after {RunFrames} frames: zone {sound.ListenerZone} on bus {sound.Buses!.BusForZone(sound.ListenerZone)}, " +
                   $"{sound.ActiveVoices} voices, {sound.Started.Count} sounds started, {sound.Silent} silent calls");
        string detail = string.Join("; ", _notes);
        sound.StopAll();
        sound.QueueFree();
        rig.World.EventSink = null;
        _library.ReleaseStreams();
        _final = SmokeResult.Pass(detail);
        _frames = 0;
        _phase = 3;
        return SmokeResult.Running;
    }

    private void Listen()
    {
        PawnRig rig = _rig!;
        FRotator view = rig.Controller.Rotation;
        _sound!.SetListener(rig.Pawn.Location + new FVector(0f, 0f, 40f), view);
    }

    /// <summary>Map ids of the rig's actors (the export names the map's audio.json uses).</summary>
    private sealed class MapLoadResultIds(PawnRig rig)
    {
        public string? Of(Actor actor) => rig.Map?.RecordOf(actor)?.Id;
    }
}
