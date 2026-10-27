using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Equipment;
using OpenScct.Physics.Collision;
using OpenScct.Provenance;
using K = OpenScct.Game.Equipment.K;

namespace OpenScct.Game.SBase;

/// <summary>
/// SBase.SMinePiegeSpy: the spy trap, and the base of the poison mine (spec/equipment/mine-launcher.md rule 23): a
/// beam from the trap to the opposite wall, checked every 0.05 s on the server.
/// </summary>
public partial class SMinePiegeSpy
{
    public static new readonly StateTable ClassStates = new(SMines.ClassStates, s_Activated.Instance, s_LockedByChaff.Instance);

    public override StateTable GetStateTable() => ClassStates;

    private static readonly ClassRef CREF_SGameMessages = new("SBase.SGameMessages");

    [Provenance(ProvenanceKind.Decoded, "SMinePiegeSpy.PostBeginPlay SetLocation(Location + 2 X)")]
    private const float TrapOffset = 2f;

    [Provenance(ProvenanceKind.Decoded, "SMinePiegeSpy.DetectedPawn BroadcastTeamLocalizedMessage(1, SGameMessages, 13)")]
    private const int MsgSpyTrapped = 13;

    /// <summary>The mine's start, 2 uu off the wall, the beam on the server, then armed at once.</summary>
    [Uc("SBase.SMinePiegeSpy.PostBeginPlay", Sha1 = "a71e82ef388cb62e118497806cdc413594514464")]
    [PortOf("uc:SBase.SMinePiegeSpy:PostBeginPlay", Spec = "spec/equipment/mine-launcher.md#mines")]
    protected override void PostBeginPlay()
    {
        base.PostBeginPlay();
        Rotation.GetAxes(out FVector x, out _, out _);
        SetLocation(Location + x * TrapOffset);
        if (Role == ENetRole.ROLE_Authority)
        {
            SpawnLaser();
        }

        GotoState(GadgetStates.s_Activated);
    }

    /// <summary>Any damage removes the trap.</summary>
    [Uc("SBase.SMinePiegeSpy.TakeDamage", Sha1 = "842d47d9443f6db763a74955d860559be935f9af")]
    [PortOf("uc:SBase.SMinePiegeSpy:TakeDamage", Spec = "spec/equipment/mine-launcher.md#mines")]
    protected override void TakeDamage(int Damage, Pawn? EventInstigator, FVector HitLocation, FVector Momentum,
        SDamageType DamageType, int PillTag = 0) => Destroy();

    /// <summary>The trap's beam line (0x80096): level, movers, other actors, world geometry, backdrops skipped.</summary>
    [Provenance(ProvenanceKind.Decoded, "SMinePiegeSpy.CheckForOppositeWall 0x10ab5330: flags 0x80096")]
    public const ETraceFlags BeamTraceFlags = SMineLaser.BeamTraceFlags;

    /// <summary>
    /// CheckForOppositeWall (decoded 0x10ab5330): a line along the trap's facing from 1 uu ahead to fDistanceMax ahead
    /// (soft bodies passed over); the beam ends at the first hit, or at the trap itself when nothing is hit. The beam
    /// actor takes the length plus 0.1. The trap's tick does not call it again.
    /// </summary>
    [UcNative("ASMinePiegeSpy::CheckForOppositeWall", Exe = 0x10ab5330, Editor = 0x111cd300)]
    [PortOf("nat:SBase.SMinePiegeSpy.SpawnLaser", Spec = "spec/equipment/mine-launcher.md#mines")]
    public void CheckForOppositeWall()
    {
        Rotation.GetAxes(out FVector x, out _, out _);
        FVector start = Location + x * K.TrapProbeStart;
        FVector end = Location + x * fDistanceMax;
        LaserEndLocation = GadgetWorld.Line(this, end, start, BeamTraceFlags, out FCheckResult hit) ? hit.Location : Location;
        if (Laser is { } beam)
        {
            beam.fTailleFaisceau = FVector.Dist(Location, LaserEndLocation) + K.LaserNoWallExtra;
        }

        fLastUpadtedLaser = Level.TimeSeconds;
    }

    /// <summary>The beam's length: from the trap to the beam end.</summary>
    public float TrapBeamLength => FVector.Dist(Location, LaserEndLocation);

    /// <summary>
    /// CheckPresence (native 1325, decoded 0x10ab5140): the first spy within the beam's length plus his radius, not
    /// behind the beam, whose cylinder the beam crosses, is detected.
    /// </summary>
    [UcNative("ASMinePiegeSpy::execCheckPresence", Exe = 0x10ab5140, Editor = 0x111cd650)]
    [PortOf("nat:SBase.SMinePiegeSpy.CheckPresence", Spec = "spec/equipment/mine-launcher.md#mines")]
    public virtual void CheckPresence()
    {
        float length = TrapBeamLength;
        FVector beamDir = (LaserEndLocation - Location).SafeNormal();
        foreach (SBasePawn p in GadgetWorld.Pawns(XLevel))
        {
            if (p is not SPawnAttaque spy || spy.bDeleteMe || !(FVector.Dist(spy.Location, Location) < length + spy.CollisionRadius))
            {
                continue;
            }

            if (((spy.Location - Location).SafeNormal() | beamDir) < 0f)
            {
                continue;
            }

            if (GadgetWorld.SegmentHitsCylinder(Location, LaserEndLocation, spy.Location, spy.CollisionRadius, spy.CollisionHeight))
            {
                DetectedPawn(spy);
                return;
            }
        }
    }

    /// <summary>
    /// SpawnLaser (native 1791, decoded 0x10ab5510): the beam measured; without a beam actor one is made at the trap,
    /// turned towards the beam end, without collision, heat 0.4, shown (bLaserHidden off). Its skin scaler and draw
    /// scale are view effects.
    /// </summary>
    [UcNative("ASMinePiegeSpy::execSpawnLaser", Exe = 0x10ab5510, Editor = 0x111cd8a0)]
    [PortOf("nat:SBase.SMinePiegeSpy.SpawnLaser", Spec = "spec/equipment/mine-launcher.md#mines")]
    public void SpawnLaser()
    {
        CheckForOppositeWall();
        if (Laser is not null)
        {
            return;
        }

        FRotator toward = (LaserEndLocation - Location).Rotation();
        Laser = XLevel.SpawnActor(new SLaserBeam_MineLaser(), Location, toward, this, Instigator);
        bLaserHidden = false;
        if (Laser is { } beam)
        {
            beam.SetCollision(false, false, false);
            beam.HeatIntensity = K.TrapBeamHeat;
            beam.fTailleFaisceau = TrapBeamLength + K.LaserNoWallExtra;
        }
    }

    /// <summary>A spy crossed the beam: the merc team is told, the spy is marked, the trap is gone.</summary>
    [Uc("SBase.SMinePiegeSpy.DetectedPawn", Sha1 = "8649f560feb225cd2d025c39ce70d99987c2310e")]
    [PortOf("uc:SBase.SMinePiegeSpy:DetectedPawn", Spec = "spec/equipment/mine-launcher.md#mines")]
    public void DetectedPawn(SBasePawn HitActor)
    {
        if (Laser is null || bLaserHidden || HitActor is not SPawnAttaque)
        {
            return;
        }

        XLevel.GetService<Devices.IDeviceHooks>()?.BroadcastTeamLocalizedMessage(1, CREF_SGameMessages, MsgSpyTrapped, null);
        HitActor.HitBySpyBullet(1);
        if (PcOwner?.PlayerReplicationInfo?.Stats is { } stats)
        {
            stats.Merc_SpyTrapTargets++;
        }

        Destroy();
    }

    /// <summary>Only a trap with a beam is chaffed, for the long neutralization time.</summary>
    [Uc("SBase.SMinePiegeSpy.SetLockedByChaff", Sha1 = "a9f63405e8bbd03a76a1fea6fc9534b6e034166a")]
    [PortOf("uc:SBase.SMinePiegeSpy:SetLockedByChaff", Spec = "spec/equipment/mine-launcher.md#mines")]
    public override void SetLockedByChaff(float Length)
    {
        if (Laser is null)
        {
            return;
        }

        base.SetLockedByChaff(StickyNeutralizationTime);
        GotoState(GadgetStates.s_LockedByChaff);
    }

    /// <summary>The beam goes; the server's interaction goes; then the mine's clean-up.</summary>
    [Uc("SBase.SMinePiegeSpy.Destroyed", Sha1 = "a089ccb73ac84fe04c4a843345d358388bcee17e")]
    [PortOf("uc:SBase.SMinePiegeSpy:Destroyed", Spec = "spec/equipment/mine-launcher.md#mines")]
    protected override void Destroyed()
    {
        Laser?.Destroy();
        if (Role == ENetRole.ROLE_Authority)
        {
            InteractionObj?.Destroy();
        }

        base.Destroyed();
    }

    protected override void CleanupDestroyedReferences()
    {
        base.CleanupDestroyedReferences();
        Laser = Live(Laser);
    }

    /// <summary>Armed (auto): the 0.05 s presence check on the server.</summary>
    [Uc("SBase.SMinePiegeSpy.s_Activated")]
    public new class s_Activated : SMines.s_Activated
    {
        public static new readonly s_Activated Instance = new(UcMeta.s_Activated, SMines.s_Activated.Instance);

        protected s_Activated(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SMinePiegeSpy.s_Activated.Timer", Sha1 = "379689fa28242bb387674875cf9916e445bedd9f")]
        [PortOf("uc:SBase.SMinePiegeSpy:s_Activated.Timer", Spec = "spec/equipment/mine-launcher.md#mines")]
        public override void Timer(Actor self)
        {
            if (self.Role == ENetRole.ROLE_Authority)
            {
                ((SMinePiegeSpy)self).CheckPresence();
            }
        }

        [Uc("SBase.SMinePiegeSpy.s_Activated.EndState", Sha1 = "e91d84dc65e56ece126b9958c72e57ddcb3dd7b4")]
        [PortOf("uc:SBase.SMinePiegeSpy:s_Activated.EndState", Spec = "spec/equipment/mine-launcher.md#mines")]
        public override void EndState(Actor self)
        {
        }

        [Uc("SBase.SMinePiegeSpy.s_Activated.BeginState", Sha1 = "cd66ce7ae16386b216aa176f500e2e0508cdcf8c")]
        [PortOf("uc:SBase.SMinePiegeSpy:s_Activated.BeginState", Spec = "spec/equipment/mine-launcher.md#mines")]
        public override void BeginState(Actor self)
        {
            if (self.Role == ENetRole.ROLE_Authority)
            {
                self.SetTimer(K.LaserScanPeriod, true);
            }
        }
    }

    /// <summary>Chaffed: the beam hidden on every machine while locked; a second lock is ignored.</summary>
    [Uc("SBase.SMinePiegeSpy.s_LockedByChaff")]
    public new class s_LockedByChaff : SBasicGamePlayObject.s_LockedByChaff, ISetLockedByChaff
    {
        public static new readonly s_LockedByChaff Instance = new(UcMeta.s_LockedByChaff, null);

        protected s_LockedByChaff(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SMinePiegeSpy.s_LockedByChaff.SetLockedByChaff", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
        [PortOf("uc:SBase.SMinePiegeSpy:s_LockedByChaff.SetLockedByChaff", Spec = "spec/equipment/mine-launcher.md#mines")]
        public new void SetLockedByChaff(SBasicGamePlayObject self, float Length)
        {
        }

        [Uc("SBase.SMinePiegeSpy.s_LockedByChaff.BeginState", Sha1 = "6e3dc6478de6c49a5668dc5a8f5abab1ce13719c")]
        [PortOf("uc:SBase.SMinePiegeSpy:s_LockedByChaff.BeginState", Spec = "spec/equipment/mine-launcher.md#mines")]
        public override void BeginState(Actor self)
        {
            base.BeginState(self);
            var trap = (SMinePiegeSpy)self;
            trap.bLaserHidden = true;
            if (trap.Laser is { } beam)
            {
                beam.bHidden = true;
            }
        }

        [Uc("SBase.SMinePiegeSpy.s_LockedByChaff.EndState", Sha1 = "ec6b3609c0492d4ccedeb53ba59ff3040eab2102")]
        [PortOf("uc:SBase.SMinePiegeSpy:s_LockedByChaff.EndState", Spec = "spec/equipment/mine-launcher.md#mines")]
        public override void EndState(Actor self)
        {
            base.EndState(self);
            var trap = (SMinePiegeSpy)self;
            trap.bLaserHidden = false;
            if (trap.Laser is { } beam)
            {
                beam.bHidden = false;
            }
        }
    }
}
