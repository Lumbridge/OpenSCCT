using System;
using System.Collections.Generic;
using System.Diagnostics;
using OpenScct.Core;
using Godot;

namespace OpenScctGame.Audio;

public readonly record struct ReverbGeometrySnapshot(string Role,string? Zone,FVector Origin,ReverbGeometry Geometry,
    long Rays,long Sweeps,float AppliedDecay,float AppliedEarlyDelay);

public sealed partial class ReverbBuses
{
    private readonly ReverbGeometryProbe _listenerGeometry=new();
    private bool _geometryEnabled,_listenerGeometryStarted;
    private int _listenerGeometryZone=-1,_nextGeometryProbe;
    private double _geometryClock,_nextGeometryBatch,_nextGeometryPacket,_lastGeometryPacket;
    public long GeometryQueries { get; private set; }
    public long GeometryRejectedPackets { get; private set; }
    public double MaxGeometryBatchMilliseconds { get; private set; }

    public void EnableGeometry(bool enabled)
    {
        if (_geometryEnabled==enabled) return;
        _geometryEnabled=enabled; _decided=false; _listenerGeometryStarted=false;
        _listenerGeometry.Reset();
        if (!enabled)
            foreach (SourceRoomField field in _sourceRooms)
                if (field.ZoneId is not null) { field.Environment=field.Authored; field.Reverb.Call("configure_eax",field.Environment.ToEaxArray()); }
    }

    public void UpdateGeometry(IAudioWorld? world,AcousticScene? scene,FVector listener,int listenerZone,float delta)
    {
        if (_disposed || !_geometryEnabled || world is null || _effect is null) return;
        _geometryClock+=Math.Max(0,delta);
        if (_listenerGeometryZone!=listenerZone)
        { _listenerGeometryZone=listenerZone; _listenerGeometryStarted=false; _listenerGeometry.Reset(); }
        if (_geometryClock>=_nextGeometryBatch)
        {
            _nextGeometryBatch=_geometryClock+1.0/60;
            long began=Stopwatch.GetTimestamp();
            Span<FVector> apertures=stackalloc FVector[6]; Span<float> weights=stackalloc float[6];
            for (int ray=0;ray<12;ray++)
            {
                int slot=_nextGeometryProbe++%(_sourceRooms.Count+1);
                ReverbGeometryProbe probe;
                FVector origin; int zone;
                if (slot==0) { probe=_listenerGeometry; origin=listener; zone=listenerZone; }
                else
                {
                    SourceRoomField field=_sourceRooms[slot-1];
                    if (field.ZoneId is null || _sourceRoomClock-field.LastUsed>Math.Max(2,field.Environment.TailSeconds)) continue;
                    probe=field.Probe; origin=field.CandidateSet?field.Candidate:field.Anchor; zone=field.ZoneIndex;
                    field.Anchor=origin;
                }
                if (probe.NeedsSweep)
                {
                    int count=scene?.ReverbApertures(zone,origin,apertures,weights)??0;
                    probe.Begin(origin,apertures[..count],weights[..count]);
                }
                probe.Add(world.TraceReverb(probe.Origin,probe.NextDirection,ReverbGeometryProbe.Range));
                GeometryQueries++;
                if (Stopwatch.GetElapsedTime(began).TotalMilliseconds>=0.75) break;
            }
            MaxGeometryBatchMilliseconds=Math.Max(MaxGeometryBatchMilliseconds,Stopwatch.GetElapsedTime(began).TotalMilliseconds);
        }
        if (_geometryClock<_nextGeometryPacket) return;
        float elapsed=(float)(_geometryClock-_lastGeometryPacket);
        _lastGeometryPacket=_geometryClock; _nextGeometryPacket=_geometryClock+0.1;
        EaxEnvironment authored=_selected is { IsOriginalValid:true } selected?selected:EaxEnvironment.NullZoneEffect;
        if (_listenerGeometry.Geometry.Valid)
        {
            EaxEnvironment next=(_applied??authored).Approach(authored.WithGeometry(_listenerGeometry.Geometry),elapsed);
            if (_applied is null || !_applied.Near(next))
                if (SendGeometry(_effect,next)) _applied=next;
            _listenerGeometryStarted=true;
        }
        foreach (SourceRoomField field in _sourceRooms)
        {
            if (field.ZoneId is null || !field.Probe.Geometry.Valid) continue;
            EaxEnvironment next=field.Environment.Approach(field.Authored.WithGeometry(field.Probe.Geometry),elapsed);
            if (!field.Environment.Near(next) && SendGeometry(field.Reverb,next)) field.Environment=next;
        }
    }

    private bool SendGeometry(AudioEffect effect,EaxEnvironment environment)
    {
        if (ReferenceEquals(effect, _effect) ? Configure(environment) : effect.Call("configure_eax",environment.ToEaxArray()).AsBool()) return true;
        GeometryRejectedPackets++;
        return false;
    }

    public ReverbGeometrySnapshot[] CaptureGeometry()
    {
        var result=new List<ReverbGeometrySnapshot> {
            new("listener",null,_listenerGeometry.Origin,_listenerGeometry.Geometry,_listenerGeometry.Rays,_listenerGeometry.Sweeps,
                _applied?.DecayTime??0,_applied?.ToEaxArray()[10]??0) };
        foreach (SourceRoomField field in _sourceRooms)
            if (field.ZoneId is { } zone) result.Add(new("source",zone,field.Probe.Origin,field.Probe.Geometry,field.Probe.Rays,
                field.Probe.Sweeps,field.Environment.DecayTime,field.Environment.ToEaxArray()[10]));
        return result.ToArray();
    }

    public IReadOnlyDictionary<string, float[]> CaptureEnvironmentPackets()
    {
        var packets = new Dictionary<string, float[]>();
        if (EnvironmentBus is { } listener && _applied is { } environment) packets[listener] = environment.ToEaxArray();
        foreach (SourceRoomField field in _sourceRooms)
            if (field.ZoneId is not null) packets[field.Bus] = field.Environment.ToEaxArray();
        return packets;
    }

    /// <summary>Reference impulse captures only; normal geometry updates retain all existing tails.</summary>
    public void ClearReverbTails()
    {
        if (_disposed) return;
        if (_effect is not AudioEffectReverb) _effect?.Call("reset");
        foreach (SourceRoomField field in _sourceRooms) field.Reverb.Call("reset");
    }
}
