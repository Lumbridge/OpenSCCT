using System;
using System.Collections.Generic;
using OpenScct.Core;

namespace OpenScctGame.Audio;

public sealed partial class SoundSystem
{
    public AcousticScene? Acoustics { get; set; }
    private Func<FVector, FVector, PortalTransmission>? _portalTrace;
    private Func<FVector,FVector,bool>? _cornerTrace;
    private int _cornerBudget=12;
    private double _nextCornerBatch;
    private readonly Dictionary<string, (int Actor, float Score)> _zoneBeds = new(StringComparer.OrdinalIgnoreCase);

    private PortalTransmission TracePassage(FVector a, FVector b)
    {
        if (World is null || !World.OcclusionBetween(a, b, out OcclusionHits hit) ||
            hit.TypeNearListener == 1 && hit.TypeNearSource == 1) return PortalTransmission.Open;
        TransmissionLevels material = AcousticTransmission.Through(FVector.Dist(hit.NearListener, hit.NearSource),
            hit.TypeNearListener, hit.TypeNearSource);
        return new(material.DirectGain * 0.2f, material.DirectHF * 0.5f);
    }

    private void UpdateAcousticScene() => Acoustics?.Update(_listenerZoneIndex, ListenerLocation, _now, _portalTrace ??= TracePassage);

    private (float Gain, float HF, float Room) ZoneBed(string id)
    {
        if (Acoustics is not { PortalCount: > 0 } scene || !scene.TryZone(id, out int zone))
            return (string.Equals(id, _listenerZone, StringComparison.OrdinalIgnoreCase) ? 1 : 0, 1, 0);
        AcousticPath path = scene.PathTo(zone);
        float distanceMix = path.Distance / (path.Distance + 250);
        return (AcousticScene.AmbientGain(path), Math.Clamp(path.HF * MathF.Pow(0.75f, path.Distance / 650), 0.002f, 1),
            path.Valid && path.Hops > 0 ? 0.45f * (0.25f + 0.75f * distanceMix) : 0);
    }

    private void SelectZoneBeds()
    {
        _zoneBeds.Clear();
        foreach (AmbientSource source in _ambient)
        {
            if (!source.Loop || source.RepeatInterval > 0 || !source.IsZone || Library!.Find(source.Sound) is not { Positional: false } def) continue;
            float score = SoundRules.SourceVolume(AmbientVolume(source), def.Volume, false) * ZoneBed(source.Id).Gain;
            if (!_zoneBeds.TryGetValue(source.Sound, out var previous) || score > previous.Score)
                _zoneBeds[source.Sound] = (source.ActorId, score);
        }
        if (Acoustics is not { PortalCount: > 0 }) return;
        // A weather bed authored in several zones and local area actors is one
        // continuous field, rather than several copies whose volumes add up.
        // Ordinary independent speakers without a zone bed keep their semantics.
        foreach (AmbientSource source in _ambient)
        {
            if (!source.Loop || source.RepeatInterval > 0 || source.IsZone || !_zoneBeds.TryGetValue(source.Sound, out var previous) ||
                Library!.Find(source.Sound) is not { Positional: false } def) continue;
            float level = SoundRules.SourceVolume(SoundRules.Ambient2DPlayingVolume(AmbientVolume(source),
                FVector.Dist(source.Location, ListenerLocation), def.Radius > 0 ? def.Radius : source.SoundRadius), def.Volume, false);
            float score = level * AmbientTransitions.CutGain(level, AudioK.Ambient2DCut);
            if (score > previous.Score) _zoneBeds[source.Sound] = (source.ActorId, score);
        }
    }

    private static void RetireAmbient(Voice voice)
    {
        if (voice.Virtual) voice.Stop();
        else { voice.AmbientEligible = false; SetAmbientTarget(voice, 0); voice.FadeOut(0.35f); }
    }

    private static void SetAmbientTarget(Voice voice, float gain, float hf = 1, float room = 0)
    {
        voice.AmbientGainTarget = gain;
        voice.AmbientHFTarget = hf;
        voice.AmbientRoomTarget = room;
    }

    private TransmissionLevels PropagatePoint(Voice voice, TransmissionLevels wall, float delta, out float distance)
    {
        float directDistance = FVector.Dist(voice.Location, ListenerLocation);
        AcousticPath path = World is null ? default : Acoustics?.PathTo(World.ZoneIndexAt(voice.Location)) ?? default;
        if ((!path.Valid || path.Hops==0) && voice.WallBlocked && !voice.FollowListener && World is { } world)
        {
            CornerPathSearch search=voice.Corner;
            bool moved=!search.Initialized || FVector.DistSquared(search.Listener,ListenerLocation)>32*32 ||
                FVector.DistSquared(search.Source,voice.Location)>32*32 || voice.CornerRevision!=world.AcousticRevision;
            if (moved || search.Complete && _now>=voice.NextCornerSearch)
            {
                OcclusionHits hits=voice.WallHits;
                search.Begin(ListenerLocation,voice.Location,hits.NearListener,hits.NearSource,hits.NormalNearListener,hits.NormalNearSource);
                voice.CornerRevision=world.AcousticRevision;
                voice.NextCornerSearch=_now+0.25;
            }
            int used=search.Step(Math.Min(3,_cornerBudget),_cornerTrace??=CornerClear);
            _cornerBudget-=used;
            AcousticPath corner=search.PathFrom(ListenerLocation);
            if (corner.Valid) path=corner;
        }
        bool indirect = path.Valid && path.Hops > 0 && wall.DirectHF < 0.999f && !voice.FollowListener;
        if (indirect) voice.LastPropagationPath = path;
        float target = indirect ? 1 : 0;
        voice.PropagationMix += (target - voice.PropagationMix) * (delta > 0 ? 1 - MathF.Exp(-delta / 0.06f) : 1);
        path = voice.LastPropagationPath;
        float blend = voice.PropagationMix;
        FVector opening = path.Valid ? path.FirstOpening : voice.Location;
        voice.RenderLocation = voice.Location + (opening - voice.Location) * blend;
        float routedDistance = path.Distance + FVector.Dist(path.LastOpening, voice.Location);
        distance = directDistance + (routedDistance - directDistance) * blend;
        float gain = path.Gain * 0.85f;
        return new(wall.DirectGain + (gain - wall.DirectGain) * blend,
            wall.DirectHF + (path.HF - wall.DirectHF) * blend,
            wall.RoomGain + (Math.Max(gain, 0.35f) - wall.RoomGain) * blend,
            wall.RoomHF + (MathF.Sqrt(Math.Max(path.HF, 0.001f)) - wall.RoomHF) * blend);
    }

    private bool CornerClear(FVector from,FVector to)
    {
        OcclusionQueries++;
        return World is null || !World.OcclusionBetween(from,to,out _);
    }

    private sealed partial class Voice
    {
        public bool DiffuseAmbient;
        public float AmbientGain = 1, AmbientGainTarget = 1, AmbientHF = 1, AmbientHFTarget = 1, AmbientRoom, AmbientRoomTarget;
        public float AmbientRenderedVolume;
        public float ReflectedInput;
        public string? ReflectionRoom;
        public float PropagationMix;
        public AcousticPath LastPropagationPath;
        public FVector RenderLocation;
        public readonly CornerPathSearch Corner=new();
        public uint CornerRevision;
        public double NextCornerSearch;
    }
}
