using OpenScct.Core;

namespace OpenScctGame.Audio;

public sealed partial class SoundSystem
{
    private int _transmissionBudget = 8;

    private TransmissionLevels SourceTransmission(Voice voice, float delta)
    {
        if (!MaterialTransmission || World is null || voice.FollowListener || voice.SoundOcclusion == AudioK.OcclusionNone ||
            voice.Ambient && voice.ActorId == ViewActorId)
        { voice.WallBlocked=false; return voice.Transmission = TransmissionLevels.Clear; }

        bool changed = voice.AcousticRevision != World.AcousticRevision ||
            FVector.DistSquared(voice.TraceListener, ListenerLocation) > 16 * 16 ||
            FVector.DistSquared(voice.TraceSource, voice.Location) > 16 * 16;
        if (_transmissionBudget > 0 && (!voice.TransmissionInitialized || changed || _now >= voice.NextTransmissionTrace))
        {
            _transmissionBudget--;
            OcclusionQueries++;
            voice.AcousticRevision = World.AcousticRevision;
            voice.TraceListener = ListenerLocation; voice.TraceSource = voice.Location;
            voice.NextTransmissionTrace = _now + 0.08 + ((voice.ActorId & 7) * 0.005);
            voice.WallBlocked=World.OcclusionBetween(ListenerLocation, voice.Location, out OcclusionHits hits);
            voice.WallHits=hits;
            voice.TransmissionTarget = voice.WallBlocked
                ? AcousticTransmission.Through(FVector.Dist(hits.NearListener, hits.NearSource), hits.TypeNearListener, hits.TypeNearSource)
                : TransmissionLevels.Clear;
            if (!voice.TransmissionInitialized) voice.Transmission = voice.TransmissionTarget;
            voice.TransmissionInitialized = true;
        }
        voice.Transmission = voice.Transmission.Approach(voice.TransmissionTarget, delta);
        return voice.Transmission;
    }

    private sealed partial class Voice
    {
        public TransmissionLevels Transmission = TransmissionLevels.Clear, TransmissionTarget = TransmissionLevels.Clear;
        public bool TransmissionInitialized;
        public double NextTransmissionTrace;
        public uint AcousticRevision;
        public FVector TraceListener, TraceSource;
        public bool WallBlocked;
        public OcclusionHits WallHits;
    }
}
