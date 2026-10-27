using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Equipment;
using OpenScct.Physics.Collision;
using OpenScct.Provenance;
using K = OpenScct.Game.Equipment.K;

namespace OpenScct.Game.SBase;

/// <summary>
/// SBase.SMineLaser: the laser mine (spec/equipment/mine-launcher.md rules 15 to 19 and 22). Armed after 5 s, its beam
/// crosses to the opposite wall; a spy crossing the beam starts a 0.5 s warning, then the blast.
/// </summary>
public partial class SMineLaser
{
    public static new readonly StateTable ClassStates =
        new(SMines.ClassStates, s_Activated.Instance, s_LockedByChaff.Instance, s_wait4boom.Instance);

    public override StateTable GetStateTable() => ClassStates;

    /// <summary>The beam probe (decoded 0x10a93a4b): flags 0x80096 (level, movers, other actors, world geometry, backdrops skipped).</summary>
    [Provenance(ProvenanceKind.Decoded, "CheckForOppositeWall 0x10a93a4b: flags 0x80096")]
    public const ETraceFlags BeamTraceFlags = ETraceFlags.TRACE_BackdropSkip | ETraceFlags.TRACE_LevelGeometry | ETraceFlags.TRACE_Others |
        ETraceFlags.TRACE_Level | ETraceFlags.TRACE_Movers;

    private float LaserSoundRadius => fDistanceMax / K.LaserSoundRadiusDivisor;

    /// <summary>A moving mover touching the mine blows it (server).</summary>
    [Uc("SBase.SMineLaser.Touch", Sha1 = "601e605e1cc0c49b9412bcda6e1f5fe9ad1046c0")]
    [PortOf("uc:SBase.SMineLaser:Touch", Spec = "spec/equipment/mine-launcher.md#mines")]
    protected override void Touch(Actor Other)
    {
        if (Other is not Mover mover)
        {
            return;
        }

        MoverWhereIAm = mover;
        if (!mover.Velocity.IsZero() && Role == ENetRole.ROLE_Authority)
        {
            Explosion();
        }
    }

    /// <summary>The beam was crossed: the warning state.</summary>
    [Uc("SBase.SMineLaser.DetectedPawnInLaser", Sha1 = "dfcf6bdf19255f97a41c33b901c91b4f608266d7")]
    [PortOf("uc:SBase.SMineLaser:DetectedPawnInLaser", Spec = "spec/equipment/mine-launcher.md#mines")]
    public void DetectedPawnInLaser() => GotoState(GadgetStates.s_wait4boom);

    /// <summary>The reference point 5 uu ahead; then the mine's start.</summary>
    [Uc("SBase.SMineLaser.PostBeginPlay", Sha1 = "23222cb630b91759e26f5c620a07f3fde69b80a7")]
    [PortOf("uc:SBase.SMineLaser:PostBeginPlay", Spec = "spec/equipment/mine-launcher.md#mines")]
    protected override void PostBeginPlay()
    {
        Rotation.GetAxes(out FVector x, out _, out _);
        refloc = Location + x * K.MineRefAhead;
        base.PostBeginPlay();
    }

    /// <summary>Any damage blows the mine.</summary>
    [Uc("SBase.SMineLaser.TakeDamage", Sha1 = "2c791e2bae007546d2756abdd5bdd2b72aea06fb")]
    [PortOf("uc:SBase.SMineLaser:TakeDamage", Spec = "spec/equipment/mine-launcher.md#mines")]
    protected override void TakeDamage(int Damage, Pawn? EventInstigator, FVector HitLocation, FVector Momentum,
        SDamageType DamageType, int PillTag = 0) => Explosion();

    /// <summary>The interaction goes on the server, the beam on every machine, then the mine's own clean-up.</summary>
    [Uc("SBase.SMineLaser.Destroyed", Sha1 = "4249a9976272572b54f7d60f116ab2bc7a2f6d0f")]
    [PortOf("uc:SBase.SMineLaser:Destroyed", Spec = "spec/equipment/mine-launcher.md#mines")]
    protected override void Destroyed()
    {
        if (Role == ENetRole.ROLE_Authority)
        {
            InteractionObj?.Destroy();
        }

        DetruitFaisceau();
        pLaser?.Destroy();
        pLaser = null;
        base.Destroyed();
    }

    /// <summary>The beam at the mine is removed (the "to all clients" flag: here and on every client).</summary>
    [Uc("SBase.SMineLaser.DetruitFaisceau", Sha1 = "454aaae6e3cf92e6c7c8cba303240758e9b02b70")]
    [PortOf("uc:SBase.SMineLaser:DetruitFaisceau", Spec = "spec/equipment/mine-launcher.md#mines")]
    [Provenance(ProvenanceKind.Estimated, "Clients lose the beam with the server's beam actor (replication); the call itself runs here")]
    public void DetruitFaisceau()
    {
        foreach (SLaserBeam_MineLaser beam in XLevel.AllActors<SLaserBeam_MineLaser>())
        {
            if (!beam.bDeleteMe && FVector.DistSquared(beam.Location, Location) <= 1f)
            {
                beam.Destroy();
                break;
            }
        }
    }

    /// <summary>
    /// The mine's native tick (decoded 0x10a93c40): after the actor tick, on the server, a moving support mover blows
    /// it, and the beam is re-measured every 2 s.
    /// </summary>
    [UcNative("ASMineLaser::Tick", Exe = 0x10a93c40)]
    [PortOf("eng:ASMineLaser::Tick", Spec = "spec/equipment/mine-launcher.md#mines")]
    protected override void NativeTick(float DeltaSeconds)
    {
        base.NativeTick(DeltaSeconds);
        if (bDeleteMe || Role != ENetRole.ROLE_Authority)
        {
            return;
        }

        if (MoverUnderMineMoves)
        {
            Explosion();
            return;
        }

        if (Level.TimeSeconds - fLastUpadtedLaser > K.LaserRemeasure)
        {
            CheckForOppositeWall();
        }
    }

    /// <summary>
    /// CheckForOppositeWall (decoded 0x10a93930): the mine's axes; the beam from 2 uu ahead to fDistanceMax ahead stops
    /// at the first wall (soft bodies passed over); with no wall the beam end is the mine itself and the length
    /// fDistanceMax + 0.1. The beam actor takes the length.
    /// </summary>
    [UcNative("ASMineLaser::CheckForOppositeWall", Exe = 0x10a93930)]
    [PortOf("eng:ASMineLaser::Tick", Spec = "spec/equipment/mine-launcher.md#mines")]
    public void CheckForOppositeWall()
    {
        Rotation.GetAxes(out FVector x, out FVector y, out FVector z);
        vAxeX = x;
        vAxeY = y;
        vAxeZ = z;
        FVector start = Location + x * K.LaserProbeStart;
        FVector end = Location + x * fDistanceMax;
        if (GadgetWorld.Line(this, end, start, BeamTraceFlags, out FCheckResult hit))
        {
            vMurTouche = hit.Location;
            fTailleFaisceau = FVector.Dist(Location, vMurTouche);
        }
        else
        {
            vMurTouche = Location;
            fTailleFaisceau = fDistanceMax + K.LaserNoWallExtra;
        }

        if (pLaser is { } beam)
        {
            beam.fTailleFaisceau = fTailleFaisceau;
        }

        fLastUpadtedLaser = Level.TimeSeconds;
    }

    /// <summary>SpawnLaser (native 1324, decoded 0x10a93b80): the beam measured, then spawned at the mine towards the wall.</summary>
    [UcNative("ASMineLaser::execSpawnLaser", Exe = 0x10a93b80)]
    [PortOf("nat:SBase.SMineLaser.SpawnLaser", Spec = "spec/equipment/mine-launcher.md#mines")]
    public void SpawnLaser()
    {
        CheckForOppositeWall();
        FRotator toward = (vMurTouche - Location).Rotation();
        pLaser = XLevel.SpawnActor(new SLaserBeam_MineLaser(), Location, new FRotator(toward.Pitch, toward.Yaw, 0), this, Instigator);
    }

    /// <summary>
    /// CheckPresence (native 1323, decoded 0x10a93ce0): an opposing pawn near the beam (within its length plus the
    /// pawn's radius, close and in front or inside the 0.9 cone) whose cylinder the beam segment crosses.
    /// </summary>
    [UcNative("ASMineLaser::execCheckPresence", Exe = 0x10a93ce0)]
    [PortOf("nat:SBase.SMineLaser.CheckPresence", Spec = "spec/equipment/mine-launcher.md#mines")]
    public void CheckPresence()
    {
        foreach (SBasePawn p in GadgetWorld.Pawns(XLevel))
        {
            if (p.bDeleteMe || (bMineDefenseur ? p is not SPawnAttaque : p is not SPAWNDEFENSE))
            {
                continue;
            }

            float reach = fTailleFaisceau + p.CollisionRadius;
            FVector d = p.Location - Location;
            if (!(d.SizeSquared() < reach * reach))
            {
                continue;
            }

            float dist = d.Size();
            float c = dist > 0f ? (d * (1f / dist)) | vAxeX : 0f;
            if (!((dist < K.LaserCloseRange && c >= 0f) || c > K.LaserConeCos))
            {
                continue;
            }

            if (GadgetWorld.SegmentHitsCylinder(Location, vMurTouche, p.Location, p.CollisionRadius, p.CollisionHeight))
            {
                DetectedPawnInLaser();
                return;
            }
        }
    }

    /// <summary>Explosion (native 1068, natives2-damage.md rule 5): the shared mine blast, reach MyDamageRange from refloc.</summary>
    [UcNative("ASMineLaser::execExplosion", Exe = 0x10a93f00)]
    [PortOf("nat:SBase.SMineLaser.Explosion", Spec = "spec/native/natives2-damage.md#rules")]
    public void Explosion()
    {
        if (bDeleteMe || bExploding)
        {
            return;
        }

        MineExplosion(ExplosionEffect, SND_Explo, refloc, MyDamageRange, false, MyDamage, fDistForBlast, PercentDamageRefillable, ShakeRange);
    }

    protected override void CleanupDestroyedReferences()
    {
        base.CleanupDestroyedReferences();
        pLaser = Live(pLaser);
    }

    /// <summary>Armed: the activation sound, the beam on the server and the 0.05 s presence check.</summary>
    [Uc("SBase.SMineLaser.s_Activated")]
    public new class s_Activated : SMines.s_Activated
    {
        public static new readonly s_Activated Instance = new(UcMeta.s_Activated, SMines.s_Activated.Instance);

        protected s_Activated(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SMineLaser.s_Activated.BeginState", Sha1 = "f95953d907cf5184443be3778f4e1eb6aa435308")]
        [PortOf("uc:SBase.SMineLaser:s_Activated.BeginState", Spec = "spec/equipment/mine-launcher.md#mines")]
        public override void BeginState(Actor self)
        {
            var mine = (SMineLaser)self;
            mine.PlaySound(mine.SND_Active, ESoundSlot.SLOT_None, 1f, false, mine.LaserSoundRadius);
            if (mine.Role == ENetRole.ROLE_Authority)
            {
                mine.SpawnLaser();
            }

            mine.SetTimer(K.LaserScanPeriod, true);
        }

        [Uc("SBase.SMineLaser.s_Activated.Timer", Sha1 = "379689fa28242bb387674875cf9916e445bedd9f")]
        [PortOf("uc:SBase.SMineLaser:s_Activated.Timer", Spec = "spec/equipment/mine-launcher.md#mines")]
        public override void Timer(Actor self)
        {
            if (self.Role == ENetRole.ROLE_Authority)
            {
                ((SMineLaser)self).CheckPresence();
            }
        }
    }

    /// <summary>The beam was crossed: the warning bip and the blast 0.5 s later.</summary>
    [Uc("SBase.SMineLaser.s_wait4boom")]
    public class s_wait4boom : UcActorState
    {
        public static readonly s_wait4boom Instance = new(UcMeta.s_wait4boom, null);

        protected s_wait4boom(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SMineLaser.s_wait4boom.BeginState", Sha1 = "ced88afc67f17bf25cf80be14f144d20ca37be91")]
        [PortOf("uc:SBase.SMineLaser:s_wait4boom.BeginState", Spec = "spec/equipment/mine-launcher.md#mines")]
        public override void BeginState(Actor self)
        {
            var mine = (SMineLaser)self;
            mine.PlaySound(mine.SND_BipExplo_Def, ESoundSlot.SLOT_None, 1f, false, mine.LaserSoundRadius);
            mine.SetTimer(mine.TempsAvantExplosion, false);
        }

        [Uc("SBase.SMineLaser.s_wait4boom.Timer", Sha1 = "2c791e2bae007546d2756abdd5bdd2b72aea06fb")]
        [PortOf("uc:SBase.SMineLaser:s_wait4boom.Timer", Spec = "spec/equipment/mine-launcher.md#mines")]
        public override void Timer(Actor self) => ((SMineLaser)self).Explosion();
    }

    /// <summary>Chaffed: the deactivation sound, no beam, no ambient sound; at the end the mine re-arms (a new beam).</summary>
    [Uc("SBase.SMineLaser.s_LockedByChaff")]
    public new class s_LockedByChaff : SBasicGamePlayObject.s_LockedByChaff
    {
        public static new readonly s_LockedByChaff Instance = new(UcMeta.s_LockedByChaff, SBasicGamePlayObject.s_LockedByChaff.Instance);

        protected s_LockedByChaff(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SMineLaser.s_LockedByChaff.BeginState", Sha1 = "41ad9ee786c76db31f498a14f2aa4ce555b91829")]
        [PortOf("uc:SBase.SMineLaser:s_LockedByChaff.BeginState", Spec = "spec/equipment/mine-launcher.md#mines")]
        public override void BeginState(Actor self)
        {
            var mine = (SMineLaser)self;
            mine.PlaySound(mine.SND_Desactive, ESoundSlot.SLOT_None, 1f, false, mine.LaserSoundRadius);
            mine.DetruitFaisceau();
            mine.pLaser = null;
            mine.AmbientSound = AssetRef.None;
        }

        [Uc("SBase.SMineLaser.s_LockedByChaff.EndState", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
        [PortOf("uc:SBase.SMineLaser:s_LockedByChaff.EndState", Spec = "spec/equipment/mine-launcher.md#mines")]
        public override void EndState(Actor self)
        {
        }

        [Uc("SBase.SMineLaser.s_LockedByChaff.Timer", Sha1 = "a6e40b2ca727d929aef6c9651ae749d62f0fe8e8")]
        [PortOf("uc:SBase.SMineLaser:s_LockedByChaff.Timer", Spec = "spec/equipment/mine-launcher.md#mines")]
        public override void Timer(Actor self) => self.GotoState(GadgetStates.s_Activated);
    }
}
