using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Godot;
using OpenScct.Core;
using OpenScct.Physics.Collision;
using OpenScctGame.Render;
using OpenScctGame.Render.Data;
using OpenScctGame.Smoke;

namespace OpenScctGame.Audio;

/// <summary>Room coupling, reflected rain PCM and repeated boundary crossings, including real Aquarius data.</summary>
public sealed class AmbientPropagationSmoke(bool aquarius = false) : SmokeTest
{
    public const string TestName = "audio-propagation", AquariusName = "audio-aquarius";
    private SoundSystem? _sound;
    private AudioLibrary? _library;
    private AudioMixCapture? _dry, _wet;
    private Camera3D? _camera;
    private IAudioWorld? _world;
    private RoomWorld? _room;
    private FVector _outside, _inside;
    private string _folder = "", _clip = "OpenScctRain.Bed";
    private double _started, _last, _openEnergy;
    private int _phase;
    private long _starts;
    private readonly List<object> _evidence = [];
    private float _largestBoundaryJump;
    public override string Name => aquarius ? AquariusName : TestName;

    public override SmokeResult Step(SmokeContext context)
    {
        AudioPeaks.Quiet();
        if (_sound is null)
        {
            _folder = Path.Combine(System.Environment.GetEnvironmentVariable("SVM_AUDIO_CAPTURE_DIR") ??
                Path.Combine(Path.GetTempPath(), "openscct-audio"), Name);
            Directory.CreateDirectory(_folder);
            AcousticScene scene;
            if (aquarius)
            {
                string? folder = MapBuilder.FindMapFolder("AquaE", context.App.Options.MapRoots);
                if (folder is null) return SmokeResult.Fail("AquaE converted content is required.");
                LightingDoc lighting = LightingDoc.Load(Path.Combine(folder, "render/lighting.json"));
                AudioLibrary original = AudioLibrary.Load(AudioLibrary.SharedDirFor(folder), folder);
                _clip = "Amb_Aqua.Ambiances.MC_aquaExt";
                if (original.Find(_clip) is not { File: not null } weather || original.Map is null)
                    return SmokeResult.Fail("Aquarius exterior weather stream is missing.");
                var collision = CollisionWorld.Load(Path.Combine(folder, "sim/collision.bin"));
                var weatherZones = lighting.Zones.Where(z => original.Map.Zones.TryGetValue(z.Name, out var zone) && zone.AmbientSound == _clip)
                    .Select(z => z.ZoneIndex).ToHashSet();
                bool found = false;
                foreach (var portal in lighting.FogPortals)
                {
                    if (weatherZones.Contains(portal.Back) == weatherZones.Contains(portal.Front)) continue;
                    var geometry = new AcousticPortal(portal.Back, portal.Front, new(portal.Plane.X,portal.Plane.Y,portal.Plane.Z),
                        portal.Vertices.Select(v => new FVector(v.X,v.Y,v.Z)).ToArray());
                    FVector back = geometry.Center - geometry.Normal * 48, front = geometry.Center + geometry.Normal * 48;
                    if (collision.GetZoneIndex(back) != portal.Back || collision.GetZoneIndex(front) != portal.Front ||
                        !collision.FastTrace(front,back)) continue;
                    _outside = weatherZones.Contains(portal.Back) ? back : front;
                    _inside = weatherZones.Contains(portal.Back) ? front : back;
                    found = true; break;
                }
                if (!found) return SmokeResult.Fail("No open Aquarius exterior/interior portal could be validated against collision.");
                _world = new MapWorld(collision, lighting.Zones.ToDictionary(z=>z.ZoneIndex,z=>z.Name));
                scene = MapAcoustics.FromLighting(lighting);
                _library = WeatherFixture(_folder, weather.File!, _clip, original.Map.Zones.Values,
                    original.Map.Actors.Where(a=>a.AmbientSound==_clip && a.Class.EndsWith("ZoneInfo",StringComparison.Ordinal)));
                original.ReleaseStreams();
            }
            else
            {
                AudioLibrary fixture = AudioModernizationSmoke.Fixture(_folder); fixture.ReleaseStreams();
                var levels = new Dictionary<string,float> { ["DecayTime"]=0.8f, ["Room"]=-500, ["RoomHF"]=-500,
                    ["Reverb"]=0, ["Reflections"]=-1000 };
                _library = WeatherFixture(_folder, Path.Combine(_folder,"noise.wav"), _clip,
                    [new MapSoundZone("Outside",_clip,128,64,null,new Dictionary<string,float>()),
                     new MapSoundZone("Inside",null,128,64,"Probe.Room",levels)],
                    [new MapSoundActor("Outside","Engine.ZoneInfo","render",new(-100,0,0),_clip,128,64,64,0,64)]);
                scene = new AcousticScene([new(1,2,new(1,0,0), [new(0,-100,-100),new(0,100,-100),new(0,100,100),new(0,-100,100)])],
                    new Dictionary<string,int> { ["Outside"]=1,["Inside"]=2 });
                _room = new RoomWorld(); _world = _room;
                _outside = new(-100,0,0); _inside = new(300,0,0);
            }
            _library.PrepareSession();
            _camera = new Camera3D { Current=true }; context.Tree.Root.AddChild(_camera);
            _sound = new SoundSystem { Acoustics=scene, Headphones=true };
            context.Tree.Root.AddChild(_sound); _sound.SetProcess(false);
            _sound.Bind(_library,_world); _sound.SetListener(_outside,default); _sound.Update(0);
            _dry = new AudioMixCapture(ReverbBuses.WorldBus);
            _wet = new AudioMixCapture(_sound.Buses!.EnvironmentBus!);
            _started = _last = context.ElapsedSeconds;
            return SmokeResult.Running;
        }
        double delta = context.ElapsedSeconds-_last; _last=context.ElapsedSeconds;
        _sound.Update(delta); _dry!.Sample(); _wet!.Sample();
        double elapsed = context.ElapsedSeconds-_started;
        SoundVoice[] bed = _sound.CaptureVoices().Where(v=>v.Call==_clip).ToArray();
        if (bed.Length != 1) return Finish(SmokeResult.Fail($"Weather bed duplicated or disappeared: {bed.Length}."));
        if (_phase == 3)
        {
            float before = bed[0].AppliedGain;
            _sound.SetListener(((int)(elapsed*8)&1)==0 ? _outside : _inside,default);
            _sound.Update(0);
            float after = _sound.CaptureVoices().Single(v=>v.Call==_clip).AppliedGain;
            _largestBoundaryJump = Math.Max(_largestBoundaryJump,Math.Abs(after-before));
        }
        if (elapsed < (_phase==3 ? 2 : 1.5)) return SmokeResult.Running;
        string label = _phase switch { 0=>"outside",1=>"inside-open",2=>aquarius?"inside-steady":"inside-door-closed",_=>"boundary-reversals" };
        _evidence.Add(new { label, voice=bed[0], dry=_dry.Save(Path.Combine(_folder,label+"-dry.wav")),
            wet=_wet.Save(Path.Combine(_folder,label+"-wet.wav")), starts=_sound.StartCount });
        if (_phase==0)
        {
            if (_dry.Energy() <= 1e-8) return Finish(SmokeResult.Fail("Outside weather fixture produced no actual PCM."));
            _starts=_sound.StartCount;
            float before=bed[0].AppliedGain;
            _sound.SetListener(_inside,default); _sound.Update(0);
            if (Math.Abs(_sound.CaptureVoices().Single(v=>v.Call==_clip).AppliedGain-before)>0.00001f)
                return Finish(SmokeResult.Fail("Crossing the zone boundary changed weather gain discontinuously."));
        }
        else if (_phase==1)
        {
            _openEnergy=_dry.Energy();
            if (_openEnergy<=1e-8 || _wet.Energy()<=1e-12 || bed[0].RoomGain<=0)
                return Finish(SmokeResult.Fail($"Interior lacks carried/reflected weather: dry={_openEnergy}, wet={_wet.Energy()}, send={bed[0].RoomGain}."));
            if (_room is not null) _room.Closed=true;
        }
        else if (_phase==2)
        {
            if (!aquarius && _dry.Energy()>=_openEnergy*0.5)
                return Finish(SmokeResult.Fail("Closed door did not reduce the propagated weather field."));
            if (_room is not null) _room.Closed=false;
        }
        else
        {
            if (_sound.StartCount!=_starts || _largestBoundaryJump>0.00001f)
                return Finish(SmokeResult.Fail("Repeated boundary crossings restarted or hard-switched the weather bed."));
            if (_room is not null)
            {
                _sound.SetListener(_inside,default); _sound.Update(0);
                _room.PointSource = new(-300,300,0);
                if (!_sound.PlaySound(99,"OpenScctRain.Point",0,false,_room.PointSource,1,100,1))
                    return Finish(SmokeResult.Fail("Point propagation source did not start."));
                _sound.Update(0);
                SoundVoice point = _sound.CaptureVoices().Single(v=>v.ActorId==99);
                if (FVector.Dist(point.Arrival,default)>1 || point.RoomGain<=0)
                    return Finish(SmokeResult.Fail("Occluded point sound did not arrive through the opening and feed room reflections."));
                _evidence.Add(new { label="occluded-point-arrival", voice=point });
            }
            File.WriteAllText(Path.Combine(_folder,"propagation.json"),JsonSerializer.Serialize(new {
                map=aquarius?"AquaE":"synthetic two rooms", source=_clip, outside=_outside, inside=_inside,
                portals=_sound.Acoustics!.PortalCount, apertureQueries=_sound.Acoustics.PortalQueries,
                _largestBoundaryJump, captures=_evidence, _sound.MaxUpdateMilliseconds,
            },new JsonSerializerOptions { WriteIndented=true, IncludeFields=true }));
            return Finish(SmokeResult.Pass($"Continuous weather across openings, interior reflected PCM, no restart over boundary reversals; {_folder}"));
        }
        _phase++; _started=context.ElapsedSeconds; _dry.Reset(); _wet.Reset();
        return SmokeResult.Running;
    }

    private SmokeResult Finish(SmokeResult result)
    {
        _dry?.Dispose(); _wet?.Dispose(); _sound?.StopAll(); _sound?.Free(); _sound=null;
        _camera?.Free(); _library?.ReleaseStreams(); return result;
    }

    private static AudioLibrary WeatherFixture(string folder,string file,string clip,IEnumerable<MapSoundZone> zones,IEnumerable<MapSoundActor> actors)
    {
        string shared=Path.Combine(folder,"shared"), map=Path.Combine(folder,"map");
        Directory.CreateDirectory(shared); Directory.CreateDirectory(Path.Combine(map,"audio"));
        File.WriteAllText(Path.Combine(shared,"audio.json"),JsonSerializer.Serialize(new { format="svm.audio/1",
            sounds=new Dictionary<string,object> {
                [clip]=new { kind="stream", flags=16, volume=1, radius=0,
                    loop=false,positional=false,file=Path.GetFullPath(file),duration=48 },
                ["OpenScctRain.Point"]=new { kind="stream", flags=0, volume=1, radius=100,
                    loop=false,positional=true,file=Path.GetFullPath(file),duration=48 } } }));
        File.WriteAllText(Path.Combine(map,"audio/audio.json"),JsonSerializer.Serialize(new {
            format="svm.mapaudio/1",sounds=new Dictionary<string,object>(),
            zones=zones.Select(z=>new { id=z.Id,effect=new { @class=z.EffectClass,@params=z.Effect } }),
            actors=actors.Select(a=>new { id=a.Id,@class=a.Class,category="render",
                location=new { a.Location.X,a.Location.Y,a.Location.Z },ambientSound=a.AmbientSound,
                soundVolume=a.SoundVolume,soundRadius=a.SoundRadius,soundPitch=a.SoundPitch }) }));
        return AudioLibrary.Load(shared,map);
    }

    private sealed class RoomWorld : IAudioWorld
    {
        public bool Closed;
        public FVector PointSource;
        public bool TryGetActorLocation(int id,out FVector at) { at=PointSource; return id==99; }
        public void CollectAmbient(List<AmbientSource> into,FVector listener) { }
        public string? ZoneIdAt(FVector at)=>at.X<0?"Outside":"Inside";
        public int ZoneIndexAt(FVector at)=>at.X<0?1:2;
        public int SurfaceOf(FVector a,FVector b)=>0;
        public byte SoundOcclusionOf(int id)=>0;
        public bool OcclusionBetween(FVector a,FVector b,out OcclusionHits hit)
        {
            bool crosses=a.X*b.X<0;
            float y=crosses ? a.Y+(b.Y-a.Y)*(-a.X/(b.X-a.X)) : 0;
            hit=new(new(-4,y,0),new(4,y,0),Closed?6:9,Closed?6:9,1,2);
            return crosses && (Closed || Math.Abs(y)>100);
        }
    }
    private sealed class MapWorld(CollisionWorld collision,Dictionary<int,string> zones) : IAudioWorld
    {
        public bool TryGetActorLocation(int id,out FVector at) { at=default; return false; }
        public void CollectAmbient(List<AmbientSource> into,FVector listener) { }
        public string? ZoneIdAt(FVector at)=>zones.GetValueOrDefault(collision.GetZoneIndex(at));
        public int ZoneIndexAt(FVector at)=>collision.GetZoneIndex(at);
        public int SurfaceOf(FVector a,FVector b)=>0;
        public byte SoundOcclusionOf(int id)=>0;
        public bool OcclusionBetween(FVector a,FVector b,out OcclusionHits hit)=>LevelAudioWorld.TraceOcclusion(collision,a,b,out hit);
    }
}
