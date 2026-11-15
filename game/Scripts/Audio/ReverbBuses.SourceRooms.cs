using System;
using System.Collections.Generic;
using Godot;
using OpenScct.Core;

namespace OpenScctGame.Audio;

public readonly record struct SourceRoomSnapshot(string Zone, string Bus, float Gain, float HF, float DecayTime);

public sealed partial class ReverbBuses
{
    // Reflected world audio belongs to the emitting room. Keep its network alive as
    // the camera crosses the doorway, and transmit its output through that opening.
    // Four preallocated networks share existing wet players; no extra decoders or
    // mixer-thread creation are needed. Saturation falls back to the listener room.
    private const int MaxSourceRooms = 4;
    private readonly List<SourceRoomField> _sourceRooms = [];
    private double _sourceRoomClock;

    private void CreateSourceRooms()
    {
        for (int i=0;i<MaxSourceRooms;i++)
        {
            string bus=NewBus($"SvmSourceRoom{_id}_{i}",WorldBus);
            AudioEffect reverb=Create(EffectClass), filter=Create(FilterClass);
            _effects.Add(reverb); _effects.Add(filter);
            AudioServer.AddBusEffect(AudioServer.GetBusIndex(bus),reverb);
            AudioServer.AddBusEffect(AudioServer.GetBusIndex(bus),filter);
            var field=new SourceRoomField(bus,reverb,filter,i==MaxSourceRooms-1);
            field.Apply(0,1);
            _sourceRooms.Add(field);
        }
    }

    public SourceRoomSnapshot[] CaptureSourceRooms()
    {
        var result=new List<SourceRoomSnapshot>();
        foreach (SourceRoomField field in _sourceRooms)
            if (field.ZoneId is { } zone) result.Add(new(zone,field.Bus,field.Gain,field.HF,field.Environment.DecayTime));
        return result.ToArray();
    }

    public void UpdateSourceRooms(AcousticScene? scene, float delta)
    {
        if (_disposed) return;
        _sourceRoomClock+=Math.Max(0,delta);
        foreach (SourceRoomField field in _sourceRooms)
        {
            field.CandidateSet=false;
            if (field.ZoneId is null) continue;
            AcousticPath path=scene?.PathTo(field.ZoneIndex) ?? default;
            float target=AcousticScene.ReflectedGain(path);
            if (_sourceRoomClock-field.LastUsed>Math.Max(2,field.Environment.TailSeconds)) target=0;
            float mix=1-MathF.Exp(-Math.Max(0,delta)/0.12f);
            field.Apply(field.Gain+(target-field.Gain)*mix,
                field.HF+((path.Valid?path.HF:0.01f)-field.HF)*mix);
        }
    }

    public SourceRoomField? RoomForSource(string? zoneId, int zoneIndex, AcousticScene? scene, bool ambient = false,FVector? anchor=null)
    {
        if (_disposed) return null;
        if (zoneId is null || scene is null) return null;
        EaxEnvironment environment=_environments.TryGetValue(zoneId,out EaxEnvironment? authored) && authored.IsOriginalValid
            ? authored : EaxEnvironment.NullZoneEffect;
        SourceRoomField? available=null;
        foreach (SourceRoomField field in _sourceRooms)
        {
            if (ambient && field.ReservedForGameplay) continue;
            if (field.ZoneId==zoneId) { field.LastUsed=_sourceRoomClock; field.Consider(anchor); return field; }
            bool retired=field.ZoneId is null || _sourceRoomClock-field.LastUsed>Math.Max(2,field.Environment.TailSeconds)+0.5;
            if (retired)
                available=field;
        }
        // A temporarily closed/disconnected passage silences the existing field;
        // it must not suddenly replace it with the listener's reverb network.
        if (!scene.PathTo(zoneIndex).Valid) return null;
        // Ambience can occupy three networks; a newly audible footstep or shot
        // must still have a room available when all ambient rooms are active.
        if (available is null) return null;
        available.Reverb.Call("reset");
        if (!available.Reverb.Call("configure_eax",environment.ToEaxArray()).AsBool()) return null;
        available.ZoneId=zoneId; available.ZoneIndex=zoneIndex; available.Environment=environment;
        available.Authored=environment; available.Probe.Reset(); available.Anchor=anchor??default;
        available.CandidateSet=false; available.Consider(anchor);
        available.LastUsed=_sourceRoomClock;
        // One-shot transients must excite the room from their first block.
        // Ambient voices already have their own allocation fade.
        available.Apply(AcousticScene.ReflectedGain(scene.PathTo(zoneIndex)),scene.PathTo(zoneIndex).HF);
        return available;
    }

    public void ResetSourceRooms()
    {
        // Godot exits children before their parent. PlayAudio's final StopAll
        // can therefore follow SoundSystem's disposal of the native effects.
        if (_disposed) return;
        _listenerGeometry.Reset(); _listenerGeometryStarted=false; _listenerGeometryZone=-1; _decided=false;
        foreach (SourceRoomField field in _sourceRooms)
        {
            field.Apply(0,1); field.Reverb.Call("reset"); field.ZoneId=null; field.Probe.Reset();
        }
    }

    public sealed class SourceRoomField(string bus, AudioEffect reverb, AudioEffect filter,bool reservedForGameplay)
    {
        public string Bus { get; }=bus;
        internal AudioEffect Reverb { get; }=reverb;
        public string? ZoneId;
        public int ZoneIndex;
        public EaxEnvironment Environment=EaxEnvironment.NullZoneEffect;
        internal EaxEnvironment Authored=EaxEnvironment.NullZoneEffect;
        internal readonly ReverbGeometryProbe Probe=new();
        internal FVector Anchor,Candidate;
        internal bool CandidateSet;
        internal void Consider(FVector? point)
        {
            if (point is { } p && (!CandidateSet || FVector.DistSquared(p,Anchor)<FVector.DistSquared(Candidate,Anchor)))
            { Candidate=p; CandidateSet=true; }
        }
        public double LastUsed;
        internal bool ReservedForGameplay { get; }=reservedForGameplay;
        public float Gain { get; private set; }=float.NaN;
        public float HF { get; private set; }=float.NaN;
        private readonly float[] _values=new float[3];
        internal void Apply(float gain,float hf)
        {
            if (gain==Gain && hf==HF) return;
            Gain=gain; HF=hf;
            _values[0]=gain; _values[1]=hf; _values[2]=Environment.HighFrequencyReference;
            if (!filter.Call("configure",_values).AsBool())
                throw new InvalidOperationException("Invalid reflected ambience transmission.");
        }
    }
}
