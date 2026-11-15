using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Godot;
using OpenScct.Core;
using OpenScct.Physics.Collision;
using OpenScctGame.Render;
using OpenScctGame.Smoke;

namespace OpenScctGame.Audio;

/// <summary>Real remote footstep/gunshot events through openings, closed doors and corners within a single zone.</summary>
public sealed class GameplayPropagationSmoke : SmokeTest
{
    public const string TestName="audio-gameplay-propagation";
    private static readonly string[] Cues=["Sound_Persos.FootSteps.SW_FootRun","Armes_impacts.Armes.RA_GunDef_1shot"];
    private static readonly FVector Ear=new(200,0,0), Emitter=new(-200,0,0);
    private readonly TestWorld _world=new();
    private readonly List<object> _evidence=[];
    private AudioLibrary? _library;
    private SoundSystem? _sound;
    private Camera3D? _camera;
    private AudioMixCapture? _mix,_reflections,_tail;
    private double _started,_last;
    private int _phase;
    private bool _playing,_stopped,_arrivalObserved;
    private SoundVoice _initial,_lastVoice;
    private string _folder="";
    public override string Name=>TestName;

    public override SmokeResult Step(SmokeContext context)
    {
        AudioPeaks.Quiet();
        if (_sound is null)
        {
            string? map=MapBuilder.FindMapFolder("AquaE",context.App.Options.MapRoots);
            if (map is null) return SmokeResult.Fail("Converted original audio is required.");
            _folder=Path.Combine(System.Environment.GetEnvironmentVariable("SVM_AUDIO_CAPTURE_DIR") ??
                Path.Combine(Path.GetTempPath(),"openscct-audio"),Name);
            Directory.CreateDirectory(Path.Combine(_folder,"audio"));
            var sourceRoom=new Dictionary<string,float> { ["DecayTime"]=0.9f,["Room"]=-500,["RoomHF"]=-500,
                ["Reflections"]=0,["Reverb"]=500,["RoomRolloffFactor"]=0.4f };
            File.WriteAllText(Path.Combine(_folder,"audio/audio.json"),JsonSerializer.Serialize(new {
                format="svm.mapaudio/1",sounds=new Dictionary<string,object>(),actors=Array.Empty<object>(),zones=new[] {
                    new { id="Source",effect=new { @class="Probe.Room",@params=sourceRoom } },
                    new { id="Listener",effect=new { @class="Probe.Room",@params=sourceRoom } } } }));
            _library=AudioLibrary.Load(AudioLibrary.SharedDirFor(map),_folder); _library.PrepareSession();
            foreach (string cue in Cues) _library.Preload(cue);
            _camera=new Camera3D { Current=true }; context.Tree.Root.AddChild(_camera);
            _sound=new SoundSystem { Acoustics=Scene(),LocalPawnId=1,ViewActorId=1 };
            context.Tree.Root.AddChild(_sound); _sound.SetProcess(false); _sound.Bind(_library,_world);
            _sound.SetListener(Ear,default); _sound.Update(0);
            _last=context.ElapsedSeconds;
            return SmokeResult.Running;
        }
        double delta=context.ElapsedSeconds-_last; _last=context.ElapsedSeconds;
        if (!_playing)
        {
            _world.Mode=(_phase/2)%3; _world.Revision++;
            _sound.Headphones=_phase>=6; _sound.Acoustics=Scene(); _sound.SetListener(Ear,default); _sound.Update(0.02);
            string cue=Cues[_phase%2];
            // Exercise the presentation path for a sound the server delivered.
            _sound.Handle(new SimEvent { Kind=SimEventKind.PlaySound,ActorId=99,What=cue,Heard=true,
                Location=Emitter,Volume=1,Radius=100,Pitch=1 });
            SoundVoice[] voices=_sound.CaptureVoices();
            if (voices.Length!=1 || voices[0].Ambient || voices[0].ReflectionRoom!="Source" || voices[0].RoomGain<=0)
                return Finish(SmokeResult.Fail("A remote one-shot did not excite its source room immediately."));
            _initial=_lastVoice=voices[0];
            var field=_sound.Buses!.CaptureSourceRooms().Single(f=>f.Zone=="Source");
            if (field.Gain<=0) return Finish(SmokeResult.Fail("A one-shot's first block was lost to the room allocation fade."));
            _mix=new AudioMixCapture(ReverbBuses.WorldBus); _reflections=new AudioMixCapture(field.Bus);
            _tail=new AudioMixCapture(field.Bus);
            _started=context.ElapsedSeconds; _playing=true; _stopped=_arrivalObserved=false;
            return SmokeResult.Running;
        }
        _sound.Update(delta); _mix!.Sample(); _reflections!.Sample();
        double elapsed=context.ElapsedSeconds-_started;
        foreach (SoundVoice voice in _sound.CaptureVoices())
        {
            if (voice.ActorId!=99) continue;
            _lastVoice=voice;
            if (FVector.Dist(voice.Arrival,Emitter)>25) _arrivalObserved=true;
        }
        if (elapsed<0.55) _tail!.Reset(); else _tail!.Sample();
        if (!_stopped && elapsed>=0.35) { _sound.StopSound(99,Cues[_phase%2]); _stopped=true; }
        if (elapsed<1.25) return SmokeResult.Running;
        var room=_sound.Buses!.CaptureSourceRooms().Single(f=>f.Zone=="Source");
        string label=$"{(_sound.Headphones?"headphones":"speakers")}-{new[] {"opening","door-closed","same-zone-corner"}[_world.Mode]}-{(_phase%2==0?"footstep":"gunshot")}";
        _evidence.Add(new { label,initial=_initial,lastVoice=_lastVoice,room,
            mix=_mix.Save(Path.Combine(_folder,label+".wav")),
            reflections=_reflections.Save(Path.Combine(_folder,label+"-room.wav")),
            tail=_tail!.Save(Path.Combine(_folder,label+"-tail.wav")),_arrivalObserved });
        if (!_arrivalObserved || _mix.Energy()<=1e-10 || _reflections.Energy()<=1e-10 || _tail.Energy()<=1e-12)
            return Finish(SmokeResult.Fail($"Missing opening/corner arrival, actual PCM or post-source reflection tail: {label}."));
        if (_world.Mode==1 && room.Gain>=0.1f)
            return Finish(SmokeResult.Fail("Closed door did not attenuate the propagated room field."));
        _mix.Dispose(); _reflections.Dispose(); _tail.Dispose(); _mix=_reflections=_tail=null;
        _sound.StopAll(); _playing=false; _phase++;
        if (_phase<12) return SmokeResult.Running;
        File.WriteAllText(Path.Combine(_folder,"gameplay-propagation.json"),JsonSerializer.Serialize(new {
            captures=_evidence,_sound.OcclusionQueries,_sound.MaxUpdateMilliseconds,
            native=_sound.Buses.CaptureSpatializerMetrics(),_library!.RuntimeFilesRead,
        },new JsonSerializerOptions { WriteIndented=true,IncludeFields=true }));
        if (_library.RuntimeFilesRead!=0) return Finish(SmokeResult.Fail("A preloaded gameplay cue read a file during playback."));
        return Finish(SmokeResult.Pass("Original footsteps/gunshots: openings, closed doors, same-zone wall corners, first-block room input and retained tails in both modes; "+_folder));
    }

    private static AcousticScene Scene()=>new([new(1,2,new(1,0,0),
        [new(0,128,-200),new(0,256,-200),new(0,256,200),new(0,128,200)])],
        new Dictionary<string,int> { ["Source"]=1,["Listener"]=2 });

    private SmokeResult Finish(SmokeResult result)
    {
        _mix?.Dispose(); _reflections?.Dispose(); _tail?.Dispose();
        _sound?.StopAll(); _sound?.Free(); _camera?.Free(); _library?.ReleaseStreams();
        return result;
    }

    private sealed class TestWorld : IAudioWorld
    {
        private readonly CollisionWorld _wall=Wall(100),_closed=Wall(10000);
        public int Mode;
        public uint Revision;
        public uint AcousticRevision=>Revision;
        private static CollisionWorld Wall(float half)
        {
            var data=new CollisionDataBuilder(); data.AddBox(new(-16,-half,-1000),new(16,half,1000));
            return CollisionWorld.Build(data.Build());
        }
        public bool TryGetActorLocation(int id,out FVector at) { at=id==99?Emitter:Ear; return id is 1 or 99; }
        public void CollectAmbient(List<AmbientSource> into,FVector listener) { }
        public string? ZoneIdAt(FVector at)=>ZoneIndexAt(at)==1?"Source":"Listener";
        public int ZoneIndexAt(FVector at)=>Mode==2 || at.X<0?1:2;
        public int SurfaceOf(FVector a,FVector b)=>5;
        public byte SoundOcclusionOf(int id)=>0;
        public bool OcclusionBetween(FVector a,FVector b,out OcclusionHits hit)
        {
            bool blocked=LevelAudioWorld.TraceOcclusion(Mode==1?_closed:_wall,a,b,out hit);
            if (blocked) hit=hit with { TypeNearListener=9,TypeNearSource=9 };
            return blocked;
        }
    }
}
