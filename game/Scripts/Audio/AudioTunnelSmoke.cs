using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Godot;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Camera;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using OpenScct.Physics.Collision;
using OpenScctGame.Render;
using OpenScctGame.Render.Data;
using OpenScctGame.Smoke;

namespace OpenScctGame.Audio;

/// <summary>The reported stationary-spy AquaE tunnel repro, with the actual following-camera solver and collision.</summary>
public sealed class AudioTunnelSmoke : SmokeTest
{
    public const string TestName = "audio-aquarius-tunnel";
    private static readonly FVector PawnPosition = new(3042.6035f,2304.6663f,-38.995003f);
    private static readonly FRotator[] Views = [new(-637,-16408,0), new(-323,16535,0)];
    private const string Water = "Amb_Aqua.Ambiances.Aqua_bubble";
    private SoundSystem? _sound;
    private AudioLibrary? _library;
    private CollisionWorld? _collision;
    private AudioMixCapture? _capture;
    private AudioMixCapture? _waterCapture;
    private AudioMixCapture? _waterRoomCapture;
    private Camera3D? _camera;
    private readonly FVector[] _positions = new FVector[2];
    private readonly List<object> _evidence = [];
    private string _folder = "";
    private double _started, _last;
    private int _phase;
    private string _waterRoomBus="";
    private long _waterStarts;
    private string? _failure;
    public override string Name => TestName;

    public override SmokeResult Step(SmokeContext context)
    {
        AudioPeaks.Quiet();
        if (_sound is null)
        {
            string? map = MapBuilder.FindMapFolder("AquaE",context.App.Options.MapRoots);
            if (map is null) return SmokeResult.Fail("AquaE converted content is required.");
            _folder = Path.Combine(System.Environment.GetEnvironmentVariable("SVM_AUDIO_CAPTURE_DIR") ??
                Path.Combine(Path.GetTempPath(),"openscct-audio"),Name);
            Directory.CreateDirectory(_folder);
            var lighting = LightingDoc.Load(Path.Combine(map,"render/lighting.json"));
            _library = AudioLibrary.Load(AudioLibrary.SharedDirFor(map),map);
            _library.PrepareSession();
            PawnRig rig = PawnRig.ForMap(PawnTeam.Spy,map,
                new PawnRigOptions { Hooks=new CameraMovementHooks() });
            _collision = rig.Collision!;
            // Each view settles the normal fast, standing spy camera without moving the reported pawn.
            for (int i=0;i<Views.Length;i++)
            {
                rig.Controller.Player = new SimViewport();
                rig.Place(PawnPosition,new(0,25015376,0),Views[i],false);
                rig.SetFastSpeed(true);
                rig.Controller.bBehindView = true;
                rig.Level.Info.m_dT=1f/60;
                SPlayerCam following = PlayerCamera.Of(rig.Controller)!;
                following.SetMode(ECamMode.ECM_Running);
                for (int frame=0;frame<240;frame++)
                    PlayerCalcView.NativeGlobalPlayerCalcView(rig.Controller,out _,out _positions[i],out _);
            }
            _camera = new Camera3D { Current=true }; context.Tree.Root.AddChild(_camera);
            _sound = new SoundSystem { Acoustics=MapAcoustics.FromLighting(lighting) };
            context.Tree.Root.AddChild(_sound); _sound.SetProcess(false);
            _sound.Bind(_library,new LevelAudioWorld(rig.Level,_collision,a=>rig.Map?.RecordOf(a)?.Id,_library.Map!.Actors));
            _capture = new AudioMixCapture(ReverbBuses.WorldBus);
            _sound.SetListener(_positions[0],Views[0]);
            _sound.Update(0);
            FVector waterAt=_library.Map!.Actors.Single(a=>a.Id=="AmbientSound1957").Location;
            var player=_sound.GetChildren().OfType<AudioStreamPlayer3D>().Single(p=>p.Name.ToString().StartsWith("Voice3D",StringComparison.Ordinal)
                && p.Position.DistanceTo(UeSpace.ToGodot(waterAt))<0.01f);
            _waterCapture=new AudioMixCapture(player.Bus);
            _waterRoomBus=_sound.Buses!.CaptureSourceRooms().Single(f=>f.Zone=="ZoneInfo294").Bus;
            _waterRoomCapture=new AudioMixCapture(_waterRoomBus);
            _waterStarts=_sound.Started.Count(s=>s==Water);
            _started=_last=-1;
            return SmokeResult.Running;
        }
        if (_last<0) { _started=_last=context.ElapsedSeconds; _capture!.Reset(); _waterCapture!.Reset(); _waterRoomCapture!.Reset(); }
        double delta=context.ElapsedSeconds-_last; _last=context.ElapsedSeconds;
        if (_phase==4)
        {
            _waterRoomCapture!.Sample();
            // Allow the already queued output block and the filter's 64-sample
            // click-removal ramp to leave the hardware/capture buffers.
            if (context.ElapsedSeconds-_started<0.15) _waterRoomCapture.Reset();
            if (context.ElapsedSeconds-_started<0.4) return SmokeResult.Running;
            if (_sound.Buses!.CaptureSourceRooms().Length!=0 || _waterRoomCapture.Energy()>1e-8)
                _failure="Stopped room retained stale reflected audio.";
            _failure ??= CheckRoomPool();
            return Finish();
        }
        _sound.Update(delta); _capture!.Sample(); _waterCapture!.Sample(); _waterRoomCapture!.Sample();
        if (context.ElapsedSeconds-_started<2) return SmokeResult.Running;
        int sample=_phase%2;
        var sources=_library!.Map!.Actors.Where(a=>a.AmbientSound==Water).Select(a=>new {
            a.Id,a.Location,zone=_collision!.GetZoneIndex(a.Location),
            distance=FVector.Dist(_positions[sample],a.Location),
            zoneDistance=_library.Map.ZoneDistance(_collision!.GetZoneIndex(a.Location),_collision.GetZoneIndex(_positions[sample])),
            path=_sound.Acoustics!.PathTo(_collision.GetZoneIndex(a.Location)),
            occluded=LevelAudioWorld.TraceOcclusion(_collision,_positions[sample],a.Location,out _)
        }).ToArray();
        _evidence.Add(new { label=sample==0?"camera-inside":"camera-outside",headphones=_sound.Headphones,
            pawn=PawnPosition,listener=_positions[sample],rotation=Views[sample],zone=_collision!.GetZoneIndex(_positions[sample]),
            sources,voices=_sound.CaptureVoices(),rooms=_sound.Buses!.CaptureSourceRooms(),geometry=_sound.Buses.CaptureGeometry(),
            _sound.Buses.GeometryQueries,_sound.Buses.GeometryRejectedPackets,_sound.Buses.MaxGeometryBatchMilliseconds,starts=_sound.StartCount,
            output=_capture.Save(Path.Combine(_folder,$"view-{_phase}.wav")),
            waterOutput=_waterCapture.Save(Path.Combine(_folder,$"water-{_phase}.wav")),
            waterReflections=_waterRoomCapture.Save(Path.Combine(_folder,$"water-reflections-{_phase}.wav")) });
        if (_waterCapture.Energy()<1e-8 || _waterRoomCapture.Energy()<1e-8)
            _failure=$"Bubbling dry/reflected PCM disappeared in view {_phase}.";
        if (_sound.Buses.GeometryRejectedPackets!=0 || !_sound.Buses.CaptureGeometry().Any(p=>p.Role=="source" && p.Geometry.Valid))
            _failure="Aquarius live geometry failed to adapt a source-room environment.";
        if (_sound.Buses.CaptureSourceRooms().SingleOrDefault(f=>f.Zone=="ZoneInfo294").Bus!=_waterRoomBus ||
            _sound.Started.Count(s=>s==Water)!=_waterStarts)
            _failure="Crossing the tunnel mouth restarted the source or its reflected field.";
        _phase++;
        if (_phase==4)
        {
            _sound.SetDemoSeek(true);
            if (_sound.Buses.CaptureSourceRooms().Length!=0) _failure="Demo seek retained the previous room field.";
            _sound.StopAll(); _waterRoomCapture.Reset(); _started=context.ElapsedSeconds;
            return SmokeResult.Running;
        }
        _sound.Headphones=_phase>=2;
        _sound.SetListener(_positions[_phase%2],Views[_phase%2]);
        _capture.Reset(); _waterCapture.Reset(); _waterRoomCapture.Reset(); _started=context.ElapsedSeconds;
        return SmokeResult.Running;
    }

    private SmokeResult Finish()
    {
        File.WriteAllText(Path.Combine(_folder,"tunnel.json"),JsonSerializer.Serialize(_evidence,
            new JsonSerializerOptions { WriteIndented=true,IncludeFields=true }));
        _capture!.Dispose(); _waterCapture!.Dispose(); _waterRoomCapture!.Dispose();
        _sound!.StopAll(); _sound.Free(); _camera!.Free(); _library!.ReleaseStreams();
        return _failure is { } failure ? SmokeResult.Fail(failure)
            : SmokeResult.Pass($"Bubbling and its persistent tunnel reflections reach both camera views in both modes; {_folder}");
    }

    private string? CheckRoomPool()
    {
        string[] zones=_library!.Map!.Zones.Values.Where(z=>z.Effect.Count>0 && EaxEnvironment.FromParameters(z.Effect).IsOriginalValid)
            .Select(z=>z.Id).Take(5).ToArray();
        if (zones.Length<5) return "The bounded-room fixture needs five valid Aquarius environments.";
        var portals=zones.Select((_,i)=>new AcousticPortal(0,i+1,new(1,0,0),
            [new(0,-50,-50),new(0,50,-50),new(0,50,50),new(0,-50,50)])).ToArray();
        var scene=new AcousticScene(portals,new Dictionary<string,int>());
        scene.Update(0,new(100,0,0),0,(_,_)=>PortalTransmission.Open);
        scene.Update(0,new(100,0,0),1,(_,_)=>PortalTransmission.Open);
        ReverbBuses buses=_sound!.Buses!;
        for (int i=0;i<4;i++)
            if (buses.RoomForSource(zones[i],i+1,scene) is null) return "Preallocated room slot was unavailable.";
        if (buses.RoomForSource(zones[4],5,scene) is not null || buses.CaptureSourceRooms().Length!=4)
            return "Reflected ambience exceeded its four-room budget.";
        buses.UpdateSourceRooms(scene,0.5f);
        if (buses.CaptureSourceRooms().Any(f=>f.Gain<0.5f)) return "An open passage did not carry its reflected field.";
        var retained=buses.RoomForSource(zones[0],1,scene);
        scene.Update(0,new(100,0,0),2,(_,_)=>new(0,0.01f));
        scene.Update(0,new(100,0,0),2.1,(_,_)=>new(0,0.01f));
        if (!ReferenceEquals(retained,buses.RoomForSource(zones[0],1,scene)))
            return "A closed passage discarded its room network instead of fading its output.";
        buses.UpdateSourceRooms(scene,0.5f);
        if (buses.CaptureSourceRooms().Any(f=>f.Gain>0.02f))
            return "Closed passages did not silence their reflected fields.";
        buses.ResetSourceRooms();
        // Restore the openings after the closed-passage checks above.
        scene.Update(0,new(100,0,0),3,(_,_)=>PortalTransmission.Open);
        scene.Update(0,new(100,0,0),3.1,(_,_)=>PortalTransmission.Open);
        buses.ResetSourceRooms();
        for (int i=0;i<3;i++)
            if (buses.RoomForSource(zones[i],i+1,scene,ambient:true) is null)
                return "Ambient source used the gameplay room reservation.";
        if (buses.RoomForSource(zones[3],4,scene,ambient:true) is not null ||
            buses.RoomForSource(zones[3],4,scene) is null)
            return "Ambient rooms starved a newly audible gameplay cue of its reflected field.";
        buses.ResetSourceRooms();
        return null;
    }
}
