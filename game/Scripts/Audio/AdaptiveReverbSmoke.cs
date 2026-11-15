using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Godot;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Content.Collision;
using OpenScct.Physics.Collision;
using OpenScctGame.Smoke;

namespace OpenScctGame.Audio;

/// <summary>Actual collision probes and native PCM: room scale, open air, and a live door with no revision signal.</summary>
public sealed class AdaptiveReverbSmoke : SmokeTest
{
    public const string TestName="audio-adaptive-reverb";
    private static readonly string[] Labels=["small-room","large-room","open-air","door-closed","door-open","door-closed-again","door-reopened"];
    private readonly GeometryWorld _world=new();
    private readonly List<object> _evidence=[];
    private readonly List<ReverbGeometrySnapshot> _rooms=[];
    private readonly List<double> _lateEnergy=[];
    private SoundSystem? _sound;
    private AudioLibrary? _library;
    private Camera3D? _camera;
    private AudioMixCapture? _wet;
    private int _stage,_phase;
    private double _started,_last;
    private double _tail;
    private string _folder="",_roomBus="";
    private ReverbGeometrySnapshot _profile;
    public override string Name=>TestName;

    public override SmokeResult Step(SmokeContext context)
    {
        AudioPeaks.Quiet();
        if (_sound is null)
        {
            if (ValidateNearestMover() is { } problem) return SmokeResult.Fail(problem);
            _folder=Path.Combine(System.Environment.GetEnvironmentVariable("SVM_AUDIO_CAPTURE_DIR") ??
                Path.Combine(Path.GetTempPath(),"openscct-audio"),Name);
            _library=Fixture(_folder); _library.PrepareSession();
            _library.Preload("Probe.Silence"); _library.Preload("Probe.Burst");
            _camera=new Camera3D { Current=true }; context.Tree.Root.AddChild(_camera);
            _sound=new SoundSystem { Acoustics=new AcousticScene([new(1,2,new(0,1,0),
                [new(-100,320,-100),new(100,320,-100),new(100,320,100),new(-100,320,100)])],
                new Dictionary<string,int> { ["Room"]=1,["Outside"]=2 }) };
            context.Tree.Root.AddChild(_sound); _sound.SetProcess(false); _sound.Bind(_library,_world);
            _sound.SetListener(default,default); _sound.Update(0);
            if (!_sound.PlaySound(99,"Probe.Silence",1,false,default,1,100,1)) return Finish(SmokeResult.Fail("Silent probe anchor failed."));
            _roomBus=_sound.Buses!.CaptureSourceRooms().Single().Bus;
            _started=_last=-1;
            return SmokeResult.Running;
        }
        if (_last<0) _started=_last=context.ElapsedSeconds;
        double delta=context.ElapsedSeconds-_last; _last=context.ElapsedSeconds;
        _sound.Update(delta);
        double elapsed=context.ElapsedSeconds-_started;
        if (_phase==0)
        {
            if (elapsed<2.2) return SmokeResult.Running;
            _profile=_sound.Buses!.CaptureGeometry().Single(p=>p.Role=="source");
            if (!_profile.Geometry.Valid || _profile.Sweeps<2) return Finish(SmokeResult.Fail("Live collision geometry never produced a room estimate."));
            if (_sound.Buses.CaptureSourceRooms().Single().Bus!=_roomBus)
                return Finish(SmokeResult.Fail("A geometry/door change reassigned the emitting room."));
            _sound.Buses.ClearReverbTails(); _phase=1; _started=context.ElapsedSeconds;
            return SmokeResult.Running;
        }
        if (_phase==1)
        {
            if (elapsed<0.15) return SmokeResult.Running;
            _wet=new AudioMixCapture(_roomBus); _tail=0;
            if (!_sound.PlaySound(100,"Probe.Burst",0,false,default,1,100,1)) return Finish(SmokeResult.Fail("Probe impulse failed."));
            _phase=2; _started=context.ElapsedSeconds;
            return SmokeResult.Running;
        }
        _wet!.Sample();
        if (elapsed<1.4) return SmokeResult.Running;
        double rate=AudioServer.GetMixRate();
        ReadOnlySpan<Vector2> pcm=_wet.Samples;
        for (int i=(int)(rate*0.6);i<pcm.Length;i++) _tail+=(double)pcm[i].X*pcm[i].X+(double)pcm[i].Y*pcm[i].Y;
        string label=(_sound.Headphones?"headphones-":"speakers-")+Labels[_stage%Labels.Length];
        _evidence.Add(new { label,profile=_profile,listener=_sound.Buses!.CaptureGeometry().Single(p=>p.Role=="listener"),
            tailEnergy=_tail,output=_wet.Save(Path.Combine(_folder,label+"-wet.wav")),
            _sound.Buses.GeometryQueries,_sound.Buses.MaxGeometryBatchMilliseconds });
        _rooms.Add(_profile); _lateEnergy.Add(_tail);
        if (_wet.Energy()<=1e-12 || _wet.DiscardedFrames!=0) return Finish(SmokeResult.Fail("No complete native reverb PCM capture."));
        _wet.Dispose(); _wet=null;
        if (++_stage<Labels.Length*2)
        {
            _world.Mode=_stage%Labels.Length; _sound.Headphones=_stage>=Labels.Length;
            // Revision deliberately stays zero: moving doors must refresh periodically.
            _phase=0; _started=context.ElapsedSeconds;
            return SmokeResult.Running;
        }
        File.WriteAllText(Path.Combine(_folder,"adaptive-reverb.json"),JsonSerializer.Serialize(new {
            captures=_evidence,_sound.MaxUpdateMilliseconds,_sound.Buses.GeometryQueries,_sound.Buses.MaxGeometryBatchMilliseconds,
            _sound.Buses.GeometryRejectedPackets,
            starts=_sound.Starts,revision=_world.AcousticRevision,
        },new JsonSerializerOptions { WriteIndented=true,IncludeFields=true }));
        if (_sound.Buses.GeometryRejectedPackets!=0) return Finish(SmokeResult.Fail("The native EAX validator rejected adaptive packets."));
        for (int at=0;at<_rooms.Count;at+=Labels.Length)
        {
            if (_rooms[at+1].AppliedDecay<=_rooms[at].AppliedDecay*1.5f || _lateEnergy[at+1]<=_lateEnergy[at]*2)
                return Finish(SmokeResult.Fail("Larger collision geometry did not produce a longer actual reverb tail."));
            if (_rooms[at+2].Geometry.Openness<0.4f || _rooms[at+2].AppliedDecay>=_rooms[at+1].AppliedDecay*0.5f)
                return Finish(SmokeResult.Fail("Open air retained an enclosed-room decay."));
            if (_rooms[at+4].AppliedDecay>=_rooms[at+3].AppliedDecay*0.95f || _rooms[at+5].AppliedDecay<=_rooms[at+4].AppliedDecay*1.05f ||
                _rooms[at+6].AppliedDecay>=_rooms[at+5].AppliedDecay*0.95f)
                return Finish(SmokeResult.Fail("Opening/closing/reopening the same live door did not adapt decay."));
        }
        if (_sound.Starts.Count(s=>s.Call=="Probe.Silence")!=1)
            return Finish(SmokeResult.Fail("Adaptive geometry restarted an existing emitter."));
        return Finish(SmokeResult.Pass("Measured room scale/open air and repeated live-door changes adapt native reverb/PCM with stable room ownership; "+_folder));
    }

    private SmokeResult Finish(SmokeResult result)
    {
        _wet?.Dispose(); _sound?.StopAll(); _sound?.Free(); _camera?.Free(); _library?.ReleaseStreams();
        return result;
    }

    private static AudioLibrary Fixture(string folder)
    {
        Directory.CreateDirectory(Path.Combine(folder,"map/audio"));
        int rate=(int)AudioServer.GetMixRate(),frames=rate/2;
        foreach (bool silence in new[] {false,true})
        {
            using var wav=new BinaryWriter(File.Create(Path.Combine(folder,silence?"silence.wav":"burst.wav")));
            wav.Write("RIFF"u8); wav.Write(36+frames*2); wav.Write("WAVEfmt "u8); wav.Write(16);
            wav.Write((ushort)1); wav.Write((ushort)1); wav.Write(rate); wav.Write(rate*2); wav.Write((ushort)2); wav.Write((ushort)16);
            wav.Write("data"u8); wav.Write(frames*2);
            var random=new Random(17);
            for (int i=0;i<frames;i++) wav.Write((short)(!silence && i>rate*0.05 && i<rate*0.1?random.Next(-10000,10001):0));
        }
        object Def(bool loop)=>new { kind="wave",flags=0,volume=1,radius=100,loop,positional=true,
            file=loop?"silence.wav":"burst.wav",frames,duration=0.5f };
        File.WriteAllText(Path.Combine(folder,"audio.json"),JsonSerializer.Serialize(new {
            format="svm.audio/1",sounds=new Dictionary<string,object> { ["Probe.Silence"]=Def(true),["Probe.Burst"]=Def(false) } }));
        File.WriteAllText(Path.Combine(folder,"map/audio/audio.json"),JsonSerializer.Serialize(new {
            format="svm.mapaudio/1",sounds=new Dictionary<string,object>(),actors=Array.Empty<object>(),zones=new[] {
                new { id="Room",effect=new { @class="Probe.Room",@params=new Dictionary<string,float> {
                    ["DecayTime"]=1.49f,["Room"]=-500,["RoomHF"]=-500,["Reflections"]=-1000,["Reverb"]=0 } } } } }));
        return AudioLibrary.Load(folder,Path.Combine(folder,"map"));
    }

    private sealed class GeometryWorld : IAudioWorld
    {
        private readonly CollisionWorld[] _worlds;
        private readonly Actor _door;
        private int _mode;
        public GeometryWorld()
        {
            var world=new World(17);
            _door=world.Level.SpawnActor(new ProbeDoor(),new(0,320,0),default)!;
            _worlds=[Room(new(160,200,140)),Room(new(800,900,500)),Outside(),Room(new(240,320,180),true,world,_door)];
        }
        public int Mode
        {
            get=>_mode;
            set { _mode=value; _door.Location=new(value is 3 or 5?0:1000,320,0); }
        }
        private CollisionWorld Collision=>_worlds[Mode>=3?3:Mode];
        public uint AcousticRevision=>0;
        public bool SupportsReverbGeometry=>true;
        public AcousticRayHit TraceReverb(FVector origin,FVector direction,float range)=>LevelAudioWorld.TraceReverbGeometry(Collision,origin,direction,range);
        public bool TryGetActorLocation(int id,out FVector at) { at=default; return id is 99 or 100; }
        public void CollectAmbient(List<AmbientSource> into,FVector listener) { }
        public string? ZoneIdAt(FVector at)=>"Room";
        public int ZoneIndexAt(FVector at)=>1;
        public int SurfaceOf(FVector a,FVector b)=>5;
        public byte SoundOcclusionOf(int id)=>0;
        public bool OcclusionBetween(FVector a,FVector b,out OcclusionHits hit)=>LevelAudioWorld.TraceOcclusion(Collision,a,b,out hit);
        private static CollisionWorld Room(FVector h,bool door=false,World? world=null,Actor? mover=null)
        {
            var b=new CollisionDataBuilder(); ushort m=b.AddMaterial(5);
            b.AddBox(new(-h.X-16,-h.Y-16,-h.Z-16),new(-h.X,h.Y+16,h.Z+16),m);
            b.AddBox(new(h.X,-h.Y-16,-h.Z-16),new(h.X+16,h.Y+16,h.Z+16),m);
            b.AddBox(new(-h.X,-h.Y-16,-h.Z-16),new(h.X,-h.Y,h.Z+16),m);
            b.AddBox(new(-h.X,-h.Y,-h.Z-16),new(h.X,h.Y,-h.Z),m);
            b.AddBox(new(-h.X,-h.Y,h.Z),new(h.X,h.Y,h.Z+16),m);
            if (!door) b.AddBox(new(-h.X,h.Y,-h.Z),new(h.X,h.Y+16,h.Z),m);
            else
            {
                b.AddBox(new(-h.X,h.Y,-h.Z),new(-100,h.Y+16,h.Z),m);
                b.AddBox(new(100,h.Y,-h.Z),new(h.X,h.Y+16,h.Z),m);
                b.AddBox(new(-100,h.Y,100),new(100,h.Y+16,h.Z),m);
                b.AddBox(new(-100,h.Y,-h.Z),new(100,h.Y+16,-100),m);
            }
            if (mover is not null) b.AddBoxHullCollider(ColliderKind.Mover,"liveDoor",CollisionDataBuilder.WorldFlags,
                new(-100,-8,-100),new(100,8,100),mover.Location,material:m);
            CollisionWorld collision=CollisionWorld.Build(b.Build());
            if (world is not null) collision.BindLevel(world.Level,_=>mover);
            return collision;
        }
        private static CollisionWorld Outside()
        {
            var b=new CollisionDataBuilder(); b.AddBox(new(-100000,-100000,-100),new(100000,100000,-84));
            return CollisionWorld.Build(b.Build());
        }
    }

    private static string? ValidateNearestMover()
    {
        var world=new World(18);
        Actor door=world.Level.SpawnActor(new ProbeDoor(),new(0,320,0),default)!;
        var b=new CollisionDataBuilder();
        b.AddBox(new(-500,1000,-500),new(500,1016,500));
        b.AddBoxHullCollider(ColliderKind.Mover,"door",CollisionDataBuilder.WorldFlags,new(-100,-8,-100),new(100,8,100),door.Location);
        CollisionWorld collision=CollisionWorld.Build(b.Build()); collision.BindLevel(world.Level,_=>door);
        AcousticRayHit closed=LevelAudioWorld.TraceReverbGeometry(collision,default,new(0,1,0),8192);
        door.Location=new(1000,320,0);
        AcousticRayHit open=LevelAudioWorld.TraceReverbGeometry(collision,default,new(0,1,0),8192);
        if (!closed.Hit || closed.Distance>330 || !open.Hit || open.Distance<950)
            return "Geometry probes ignored a live door in front of a farther BSP wall.";
        return null;
    }

    private sealed class ProbeDoor : Actor
    {
        public ProbeDoor() { bCollideActors=true; bBlockActors=true; bBlockPlayers=true; }
    }
}
