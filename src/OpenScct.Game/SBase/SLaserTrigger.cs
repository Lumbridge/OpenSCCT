using System;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Devices;
using OpenScct.Game.Equipment;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

/// <summary>
/// SBase.SLaserTrigger: a laser tripwire (spec/devices/laser-trigger.md). Its beam runs LaserLength ahead of the
/// emitter; a spy crossing it fires the Event with the spy as instigator (on the server), while every drawing machine
/// cuts its beam at the spy. Trigger and UnTrigger switch it off and on, chaff or a sticky shot breaks it for a while,
/// and SpawnPerCent can leave it out of the round altogether.
/// </summary>
public partial class SLaserTrigger
{
    public static new readonly StateTable ClassStates =
        new(SBasicGamePlayObject.ClassStates, s_Active.Instance, s_Inactive.Instance, s_LockedByChaff.Instance, s_Absent.Instance);

    public override StateTable GetStateTable() => ClassStates;

    internal static readonly Name NAME_s_Active = new("s_Active");
    internal static readonly Name NAME_s_Inactive = new("s_Inactive");
    internal static readonly Name NAME_s_Absent = new("s_Absent");

    /// <summary>
    /// The U scale AdjustScale gives the beam's texture scaler (LaserLength over the drawn length), so the texture keeps
    /// its size when the beam is cut. The original keeps it on a TexScaler object (LaserScaler); the views read it here.
    /// </summary>
    public float LaserScalerUScale = 1f;

    /// <summary>The beam goes with the tripwire, and so do its sparks; then the gameplay object's clean-up.</summary>
    [Uc("SBase.SLaserTrigger.Destroyed", Sha1 = "fea9911d1874a66d86fc9f81a691f6ab72738356")]
    [PortOf("uc:SBase.SLaserTrigger:Destroyed", Spec = "spec/devices/laser-trigger.md#rules")]
    protected override void Destroyed()
    {
        Laser?.Destroy();
        if (MalfunctionEmitter is not null)
        {
            KillEmitter(MalfunctionEmitter);
        }

        base.Destroyed();
    }

    [Uc("SBase.SLaserTrigger.PostBeginPlay", Sha1 = "030d7feca4773230ec2e35f15db73464e1de913d")]
    [PortOf("uc:SBase.SLaserTrigger:PostBeginPlay", Spec = "spec/devices/laser-trigger.md#rules")]
    protected override void PostBeginPlay() => base.PostBeginPlay();

    /// <summary>
    /// The server rolls SpawnPerCent (above it the tripwire is absent this round). The beam end is LaserLength ahead;
    /// a drawing machine spawns the beam actor (the LasDetectBeam mesh, no collision, unlit, full heat, halved in
    /// rain, hidden in normal vision unless the map says otherwise) and sizes it. Then the tripwire watches.
    /// </summary>
    [Uc("SBase.SLaserTrigger.PostNetBeginPlay", Sha1 = "ef3e4e96d57bccc198a11a3f932af37a685f9a9c")]
    [PortOf("uc:SBase.SLaserTrigger:PostNetBeginPlay", Spec = "spec/devices/laser-trigger.md#rules")]
    protected override void PostNetBeginPlay()
    {
        base.PostNetBeginPlay();
        if (Role == ENetRole.ROLE_Authority)
        {
            if (FRand() * 100f > SpawnPerCent)
            {
                bLaserHidden = true;
                Objectives.ObjectiveNatives.MirrorState(this, NAME_s_Absent);
                return;
            }

            bLaserHidden = false;
        }

        Rotation.GetAxes(out FVector x, out _, out _);
        LaserEndLocation = Location + (x * LaserLength);
        if (Level.NetMode != ENetMode.NM_DedicatedServer)
        {
            Laser = DeviceClasses.Spawn(this, SensorK.LaserBeamClass, this, Name.None, Location, (LaserEndLocation - Location).Rotation()) as SLaserBeam;
            if (Laser is { } beam)
            {
                beam.SetStaticMesh(LaserStaticmesh);
                beam.SetCollision(false);
                beam.SetDrawType(EDrawType.DT_StaticMesh);
                beam.bHidden = bLaserHidden;
                beam.HeatIntensity = 1f;
                if (InRain(beam.Location))
                {
                    beam.HeatIntensity *= SensorK.RainHeatFactor;
                }

                beam.bUnlit = true;
                beam.bHideInNormalVision = bHideLaserInNormalVision;
                beam.bDynamicLight = false;

                // LaserScaler is a new TexScaler over LaserSkin, put first in the beam's skins (its U scale:
                // LaserScalerUScale).
                AdjustScale(LaserEndLocation);
                beam.Skins.Insert(0, LaserSkin);
            }
        }

        GotoState(NAME_s_Active);
    }

    /// <summary>
    /// AdjustScale (native 1326, decoded 0x10ab6970 / 0x10ab6820): the beam is drawn from the emitter to
    /// <paramref name="LaserEnd"/> (DrawScale3D = (distance - 1, DrawScale, DrawScale), the mesh being 1 uu long), the
    /// texture scaler's U scale becomes LaserLength over the distance, and the beam is moved back onto the emitter.
    /// </summary>
    [UcNative("ASLaserTrigger::execAdjustScale", Exe = 0x10ab6970)]
    [PortOf("nat:SBase.SLaserTrigger.AdjustScale", Spec = "spec/devices/laser-trigger.md#rules")]
    public void AdjustScale(FVector LaserEnd)
    {
        if (Laser is not { } beam)
        {
            return;
        }

        float dist = FVector.Dist(Location, LaserEnd);
        beam.DrawScale3D = new FVector(dist - 1f, DrawScale, DrawScale);
        LaserScalerUScale = LaserLength / dist;
        beam.SetLocation(Location);
    }

    /// <summary>
    /// CheckPresence (native 1327, decoded 0x10ab69c0). The check period starts at 1 s. For each spy pawn of the level
    /// (SPawnType SPAWNOFFENSE; DetectedClass is not read) level with the beam (its height from the beam point nearest
    /// it within its half height): a spy away from the beam (flat distance minus radius above 0) brings the period
    /// down to distance / 650 + 0.01 (0.01 to 1 s); a spy on it sets the period to 0.016 s and blocks the beam at its
    /// cylinder (a line check), or at the nearest beam point when it crosses at 250 to 460 uu/s. The nearest block cuts
    /// the drawn beam (AdjustScale when the end moved); on the server it fires the Event with that spy.
    /// </summary>
    [UcNative("ASLaserTrigger::execCheckPresence", Exe = 0x10ab69c0)]
    [PortOf("nat:SBase.SLaserTrigger.CheckPresence", Spec = "spec/devices/laser-trigger.md#rules")]
    [Provenance(ProvenanceKind.Estimated, "The pawn primitive's line check is GadgetWorld.SegmentCylinderHit (entry point, no pull-back)")]
    public void CheckPresence()
    {
        FVector best = LaserEndLocation;
        SBasePawn? blocker = null;
        fCheckFrequency = SensorK.LaserIdlePeriod;
        foreach (SBasePawn p in GadgetWorld.Pawns(XLevel))
        {
            if (p.bDeleteMe || p.SPawnType != PawnType.SPAWNOFFENSE)
            {
                continue;
            }

            FVector nearest = ClosestPointOnSegment(Location, LaserEndLocation, p.Location);
            float dx = nearest.X - p.Location.X;
            float dy = nearest.Y - p.Location.Y;
            float flat = MathF.Sqrt((dx * dx) + (dy * dy)) - p.CollisionRadius;
            if (MathF.Abs(nearest.Z - p.Location.Z) > p.CollisionHeight)
            {
                continue;
            }

            if (flat > SensorK.LaserTouchDistance)
            {
                float d = flat * SensorK.LaserPeriodPerUnit;
                float period = d <= SensorK.LaserPeriodMin ? SensorK.LaserPeriodMin
                    : d <= SensorK.LaserPeriodMax ? d + SensorK.LaserPeriodMin
                    : SensorK.LaserPeriodMax;
                if (period <= fCheckFrequency)
                {
                    fCheckFrequency = period;
                }

                continue;
            }

            fCheckFrequency = SensorK.LaserBlockedPeriod;
            FVector along = Location - LaserEndLocation;
            along.Normalize();
            float across = p.Velocity.Size() - MathF.Abs(along | p.Velocity);
            FVector hit;
            if (across > SensorK.LaserSlowCross && across <= SensorK.LaserFastCross)
            {
                hit = nearest;
            }
            else if (!GadgetWorld.SegmentCylinderHit(Location, LaserEndLocation, p.Location, p.CollisionRadius, p.CollisionHeight, out hit))
            {
                continue;
            }

            if (FVector.DistSquared(best, Location) > FVector.DistSquared(hit, Location))
            {
                best = hit;
                blocker = p;
            }
        }

        if (best != PrevLaserEndLocation)
        {
            PrevLaserEndLocation = best;
            AdjustScale(best);
        }

        if (blocker is not null && Role == ENetRole.ROLE_Authority)
        {
            TriggerEvent(Event, this, blocker);
        }
    }

    /// <summary>A visible, unbroken tripwire is locked for StickyNeutralizationTime, whatever the chaff's length.</summary>
    [Uc("SBase.SLaserTrigger.SetLockedByChaff", Sha1 = "64a44721295809c2793de114d0f2e7e05e4e7a08")]
    [PortOf("uc:SBase.SLaserTrigger:SetLockedByChaff", Spec = "spec/devices/laser-trigger.md#rules")]
    public override void SetLockedByChaff(float Length)
    {
        if (!bHidden && !bIsBroken)
        {
            base.SetLockedByChaff(StickyNeutralizationTime);
        }
    }

    /// <summary>
    /// The shared Tick of s_Active, s_Inactive and s_LockedByChaff (on every machine): the beam follows bLaserHidden,
    /// and a broken tripwire shows the malfunction sparks.
    /// </summary>
    internal void SyncLaserAndSparks()
    {
        if (Laser is { } beam && bLaserHidden != beam.bHidden)
        {
            beam.bHidden = bLaserHidden;
        }

        if (bIsBroken && MalfunctionEmitter is null)
        {
            MalfunctionEmitter = DeviceClasses.Spawn(this, SensorK.MalfunctionEmitterClass, this, Name.None, Location, Rotation) as SMalfunctionEmitter;
        }
        else if (!bIsBroken && MalfunctionEmitter is not null)
        {
            KillEmitter(MalfunctionEmitter);
            MalfunctionEmitter = null;
        }
    }

    /// <summary>Sets bLaserHidden and the beam's bHidden together.</summary>
    internal void HideLaser(bool hidden)
    {
        bLaserHidden = hidden;
        if (Laser is { } beam)
        {
            beam.bHidden = hidden;
        }
    }

    /// <summary>The point of the segment from <paramref name="a"/> to <paramref name="b"/> nearest <paramref name="p"/>.</summary>
    [Provenance(ProvenanceKind.Decoded, "0x10952b10: projection clamped to the segment's ends")]
    internal static FVector ClosestPointOnSegment(FVector a, FVector b, FVector p)
    {
        FVector ab = b - a;
        float t = (p - a) | ab;
        if (t <= 0f)
        {
            return a;
        }

        float len2 = ab.SizeSquared();
        if (t >= len2)
        {
            return b;
        }

        return a + (ab * (t / len2));
    }

    /// <summary>GetRainVolume (native 1789) != None: no converted map places an ERainVolume, so never.</summary>
    [Provenance(ProvenanceKind.Estimated, "GetRainVolume is not ported; no map in .content/svmap places an Engine.ERainVolume")]
    internal static bool InRain(FVector location) => false;

    /// <summary>Emitter.Kill (native 1155): the sparks stop; here the emitter goes at once.</summary>
    [Provenance(ProvenanceKind.Estimated, "Emitter.Kill is not ported: the emitter is destroyed instead of letting its particles die")]
    internal static void KillEmitter(Emitter emitter) => emitter.Destroy();

    protected override void CleanupDestroyedReferences()
    {
        base.CleanupDestroyedReferences();
        Laser = Live(Laser);
        MalfunctionEmitter = Live(MalfunctionEmitter);
    }
}
