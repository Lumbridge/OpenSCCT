using System;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Combat;
using OpenScct.Game.Equipment;
using OpenScct.Provenance;
using K = OpenScct.Game.Equipment.K;

namespace OpenScct.Game.SBase;

/// <summary>
/// SBase.SWeaponDefense_Shotgun: the merc shotgun (spec/equipment/merc-guns.md). The rifle's trigger, clip and
/// states with a six-shell clip, no fire-mode switch, a pump on the third-person body after each shot, a reload that
/// stops after the shells it needs, and Reloaded's nine-pellet trace and accumulated damage on the server.
/// </summary>
public partial class SWeaponDefense_Shotgun
{
    public static new readonly StateTable ClassStates = new(SWeaponDefense.ClassStates, s_Reloading.Instance, s_FullReloading.Instance);

    public override StateTable GetStateTable() => ClassStates;

    private static readonly Name CNAME_s_Selected = new("s_Selected");
    private static readonly AssetRef PumpSound = new("Armes_impacts.Armes.Reload_Def_Pump");

    // The actors hit by one shot, each with its accumulated pellet damage and first hit point.
    private readonly Actor?[] _pelletActor = new Actor?[K.ShotgunPellets];
    private readonly int[] _pelletDamage = new int[K.ShotgunPellets];
    private readonly FVector[] _pelletHit = new FVector[K.ShotgunPellets];

    /// <summary>The shotgun has one fire mode.</summary>
    [Uc("SBase.SWeaponDefense_Shotgun.SwitchROF", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
    [PortOf("uc:SBase.SWeaponDefense_Shotgun:SwitchROF", Spec = "spec/equipment/merc-guns.md#rules")]
    public override void SwitchROF()
    {
    }

    /// <summary>The rifle's shot, then the pump on the third-person body (SA_FIRE).</summary>
    [Uc("SBase.SWeaponDefense_Shotgun.FireWeapon", Sha1 = "2494ec5475d7e4fa33ef0e4d9730bb80fa6e2646")]
    [PortOf("uc:SBase.SWeaponDefense_Shotgun:FireWeapon", Spec = "spec/equipment/merc-guns.md#rules")]
    public override void FireWeapon()
    {
        base.FireWeapon();
        if (Instigator is SBasePawn pawn)
        {
            CombatServices.Ports(this).ChangeEnumAction(pawn, SBasePawn.SA_FIRE, false);
        }
    }

    /// <summary>
    /// Legacy script entry point. Pellet marks now come from the same nine traces as damage, rather than a
    /// separately randomized projector pattern. Owner prediction runs through PredictTraceFire.
    /// </summary>
    [Uc("SBase.SWeaponDefense_Shotgun.SpawnImpacts", Sha1 = "23634eafdb43f0533b798bdb8b6f95b04d88e4ca")]
    [Provenance(ProvenanceKind.Estimated, "OpenSCCT: actual pellet impacts replace the original decorative projectors")]
    public void SpawnImpacts()
    {
    }

    /// <summary>The owner's immediate pellet impacts use exactly the authority's numbered shot geometry.</summary>
    protected override void PredictTraceFire(int ShotId, float Accuracy, FVector TraceStartLocation = default) =>
        TracePellets(Accuracy, ShotId, TraceStartLocation, prediction: true);

    /// <summary>The barrel smoke of each shot: an SSmokeCanonEmitter at each barrel, 22 uu along the FX bone, 3 uu to each side.</summary>
    [Uc("SBase.SWeaponDefense_Shotgun.CreateSmoke1", Sha1 = "d75ca1e15a97bd9960893ce4b334a2f728cdb4f7")]
    [PortOf("uc:SBase.SWeaponDefense_Shotgun:CreateSmoke1", Spec = "spec/equipment/merc-guns.md#rules")]
    [Provenance(ProvenanceKind.Decoded, "SWeaponDefense_Shotgun.CreateSmoke1: SetRelativeLocation(vect(22, 3, -1)) and vect(22, -3, -1)")]
    public override void CreateSmoke1()
    {
        SpawnBarrelSmoke(new FVector(22f, 3f, -1f));
        SpawnBarrelSmoke(new FVector(22f, -3f, -1f));
    }

    /// <summary>The barrel smoke after a pull is the rifle's; the shotgun has none.</summary>
    [Uc("SBase.SWeaponDefense_Shotgun.CreateSmoke2", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
    [PortOf("uc:SBase.SWeaponDefense_Shotgun:CreateSmoke2", Spec = "spec/equipment/merc-guns.md#rules")]
    public override void CreateSmoke2()
    {
    }

    /// <summary>The centre pellet before spread; matches the shotgun's separate mover and eye rules.</summary>
    public override (FVector Start, FVector End) AimRay(FRotator rotation)
    {
        if (Instigator is not { } shooter) return base.AimRay(rotation);
        bool still = shooter.Base is Mover { Velocity: var velocity } && velocity.IsZero();
        FVector eye = still
            ? shooter.Location + new FVector(0f, 0f, shooter.bIsCrouched ? K.MoverAimCrouched : K.MoverAimStanding)
            : shooter.Location + new FVector(0f, 0f, shooter.EyeHeight) + shooter.WalkBob;
        return (still ? eye : GetFireStart(), eye + rotation.Vector() * ShootingRange);
    }

    // ---- the Reloaded pellet trace -----------------------------------------------------------------------------

    /// <summary>
    /// Reloaded 4.0.4: a centre pellet and eight equally spaced ring pellets. Each actor takes the sum of its
    /// distance- and pill-scaled pellets times TotalShotgunDamageMultipler, with each truncation preserved.
    /// World impacts use those same rays; responsive smoke retains every traversed segment.
    /// </summary>
    [UcNative("ASWeaponDefense_Shotgun::execTraceFire", Exe = 0x10ab9d00, Editor = 0x111d69c0)]
    [Uc("SBase.SWeaponDefense_Shotgun.TraceFire", Sha1 = "205915467dd36e8dcbd313d69a76ff8fee165888")]
    [PortOf("nat:SBase.SWeaponDefense_Shotgun.TraceFire", Spec = "spec/equipment/merc-guns.md#reloaded-404-pellets")]
    [Provenance(ProvenanceKind.Decoded, "Reloaded.Core 4.0.4 0x10145d80: nine circular traces, RadiusScale, sum of truncated pill-scaled pellets, TotalShotgunDamageMultipler; evidence ev_f843119571540e69c44ec9d9b2ad88bb11b2bf0021d1f1800c66bb9bed1ee379")]
    public override void TraceFire(float Accuracy, float YOffset, float ZOffset, FVector TraceStartLocation = default) =>
        TracePellets(Accuracy, ActiveShotId, TraceStartLocation, prediction: false);

    private void TracePellets(float accuracy, int shotId, FVector traceStart, bool prediction)
    {
        if (Instigator is not { Controller: not null } shooter) return;
        using var activity = CombatDeathContext.Push(new(CombatDeathCause.Shotgun));
        Span<FVector> ends = stackalloc FVector[K.ShotgunPellets];
        PelletRays(shooter, accuracy, shotId, traceStart, ends, out FVector start, out FVector aim);
        int nearest = ShootingRange;
        int farthest = 0;
        int count = 0;
        for (int i = 0; i < ends.Length; i++)
            Pellet(start, ends[i], shotId, i, prediction, ref nearest, ref farthest, ref count);
        if (prediction) return;

        if (Dummy is SWeaponDefenseDummy_Shotgun gun)
        {
            gun.MinProjDistance = nearest;
            gun.MaxProjDistance = SvmMath.Min(farthest, nearest + K.ShotgunImpactWindow);
        }

        bool countedHit = false;
        for (int i = 0; i < count; i++)
        {
            Actor victim = _pelletActor[i]!;
            int damage = SvmMath.Trunc(_pelletDamage[i] * TotalShotgunDamageMultipler);
            FVector hit = _pelletHit[i];
            _pelletActor[i] = null;
            if (victim.bDeleteMe) continue;
            if (!countedHit && victim is SBasePawn target && OwnerPC is { } pc &&
                (pc.IsMercDeathMatch || SGameInfo.TeamIndexOf(pc) != SGameInfo.TeamIndexOf(target.Controller)))
            {
                SPlayerController.IncStat(pc, static s => s.ShotsHit++);
                countedHit = true;
            }
            if (damage <= 0) continue;
            FVector momentum = (hit - aim) * K.BulletMomentumScale;
            if (victim is SBasePawn pawn)
            {
                pawn.LastBulletWeapon = this;
                // Pill multipliers were applied to each pellet already. An unspecified pill preserves that
                // damage and lets the normal reaction choose an approximate pill from the first hit position.
                pawn.Controller?.PawnTakeDamage(damage, shooter, hit, momentum, MyDamageType, 0, PercentDamageRefillable);
                if (pawn is SPawnAttaque spy) spy.BloodDropStartTime = Level.TimeSeconds;
            }
            else victim.eventTakeDamage(damage, shooter, hit, momentum, MyDamageType);
        }
    }

    private void PelletRays(Pawn shooter, float accuracy, int shotId, FVector traceStart, Span<FVector> ends,
        out FVector start, out FVector aim)
    {
        AccurateRotation.GetAxes(out FVector fwd, out FVector right, out FVector up);
        bool onStillMover = shooter.Base is Mover { Velocity: var mv } && mv.IsZero();
        aim = onStillMover
            ? shooter.Location + new FVector(0f, 0f, shooter.bIsCrouched ? K.MoverAimCrouched : K.MoverAimStanding)
            : shooter.Location + new FVector(0f, 0f, shooter.EyeHeight) + shooter.WalkBob;
        start = !traceStart.IsZero() ? traceStart : onStillMover ? aim : GetFireStart();
        float spread = ShootingRange * accuracy * K.ShotgunSpread;
        FVector centre = aim + fwd * ShootingRange
            + up * ((SpreadRandom(shotId, AccurateRotation.Yaw, ShotSpread.Merc, 0) - 0.5f) * spread)
            + right * ((SpreadRandom(shotId, AccurateRotation.Yaw, ShotSpread.Merc, 1) - 0.5f) * spread);
        FVector direction = (centre - start).SafeNormal();
        FVector ringUp = up - direction * (up | direction);
        if (ringUp.Size() < 0.0001f)
        {
            FVector axis = MathF.Abs(direction.Z) > 0.99f ? new FVector(0, 1, 0) : new FVector(0, 0, 1);
            ringUp = (axis ^ direction).SafeNormal();
        }
        else ringUp = ringUp.SafeNormal();
        FVector ringRight = (direction ^ ringUp).SafeNormal();
        float radius = ShootingRange * K.ShotgunRing * RadiusScale;
        ends[0] = centre;
        for (int i = 1; i < ends.Length; i++)
        {
            int angle = (i - 1) * 8192; // pi/4 in UE angle units; the deterministic sine table.
            ends[i] = centre + (ringRight * SvmMath.Cos(angle) + ringUp * SvmMath.Sin(angle)) * radius;
        }
    }

    private void Pellet(FVector start, FVector end, int shotId, int pellet, bool prediction,
        ref int nearest, ref int farthest, ref int count)
    {
        IHitZones zones = CombatServices.HitZones(this);
        BoneTraceResult hit = zones.TraceBone(this, end, start, true);
        if (!prediction) ResponsiveSmokeWorld.Bullet(this, start, hit.bHit ? hit.HitLocation : end);
        if (!hit.bHit) return;
        int range = SvmMath.Trunc(FVector.Dist(hit.HitLocation, start));
        nearest = SvmMath.Min(nearest, range);
        farthest = SvmMath.Max(farthest, range);
        PelletImpact(hit, shotId, pellet, prediction);
        if (hit.SurfaceType == ESurfaceType.SURFACE_FakeCeiling)
        {
            FVector continuation = hit.HitLocation - hit.HitNormal * K.FakeCeilingExit;
            hit = zones.TraceBone(this, end, continuation, true);
            if (!prediction) ResponsiveSmokeWorld.Bullet(this, continuation, hit.bHit ? hit.HitLocation : end);
            if (!hit.bHit) return;
            PelletImpact(hit, shotId, pellet + K.ShotgunPellets, prediction);
        }
        if (prediction) return;

        int damage = SvmMath.Trunc(PelletDamage(FVector.Dist(hit.HitLocation, start)));
        if (hit.Actor is SBasePawn && hit.PillTag > 0 && hit.PillTag <= DamageLookupTable.Length)
            damage = SvmMath.Trunc(damage * DamageLookupTable[hit.PillTag - 1] * 0.01f);
        int slot = 0;
        while (slot < count && !ReferenceEquals(_pelletActor[slot], hit.Actor)) slot++;
        if (slot == count)
        {
            _pelletActor[slot] = hit.Actor;
            _pelletHit[slot] = hit.HitLocation;
            _pelletDamage[slot] = damage;
            count++;
        }
        else _pelletDamage[slot] += damage;
    }

    private void PelletImpact(in BoneTraceResult hit, int shotId, int pellet, bool prediction)
    {
        if (hit.bIsAFakeBackdrop || hit.Actor is Pawn) return;
        FRotator rotation = hit.HitNormal.Rotation();
        rotation.Roll = SvmMath.Trunc(ShotSpread.Unit(shotId, AccurateRotation.Yaw, ShotSpread.Merc,
            2 + pellet) * K.RandomRoll);
        Name effect = hit.SurfaceType == ESurfaceType.SURFACE_FakeCeiling
            ? new Name("SImpactEffectHole") : ImpactClass(hit.SurfaceType);
        if (prediction) ImpactManager.AddImpactHere(XLevel, effect, hit.HitLocation, rotation, hit.Actor);
        else ImpactManager.AddImpact(this, effect, hit.HitLocation, rotation, PredictedBy, hit.Actor);
    }

    /// <summary>BaseDamage times the band factor at distance <paramref name="d"/>: flat CMax before DistCac (rule 7.6).</summary>
    private float PelletDamage(float d)
    {
        if (d < DistCac)
        {
            return BaseDamage * CMax;
        }

        if (d < DistMed)
        {
            return ((CoefMed - CoefCaC) / (DistMed - DistCac) * (d - DistCac) + CoefCaC) * BaseDamage;
        }

        if (d < DistLoin)
        {
            return ((CoefMin - CoefMed) / (DistLoin - DistMed) * (d - DistMed) + CoefMed) * BaseDamage;
        }

        return BaseDamage * CoefMin;
    }

    // ---- the reload ---------------------------------------------------------------------------------------------

    /// <summary>The shells a reload loads: the clip's room, at most Ammo.</summary>
    private byte ShellsToLoad()
    {
        int shells = ClipMaxAmmo - ClipAmmo;
        return (byte)(shells > Ammo ? Ammo : shells);
    }

    protected override float AuthoritativeReloadSeconds()
    {
        int shells = ShellsToLoad();
        if (shells is >= 1 and <= 5)
        {
            // The owner skips to frame 39 at the last required shell notify. Authority follows that same
            // shortened clip, without replaying the owner's reload sound or third-person action.
            float tail = (K.FirstReloStFd2S.Frames - 1 - EndOfReloadFrame) / K.FirstReloStFd2S.Fps;
            return K.ShotgunShellNotifySeconds[shells - 1] + System.Math.Max(0, tail);
        }
        return base.AuthoritativeReloadSeconds();
    }

    /// <summary>The third-person reload of n shells (SA_RELOAD_1 to 5, SA_RELOAD for 6), or none.</summary>
    private static int ReloadAction(byte Shells) => Shells switch
    {
        1 => SBasePawn.SA_RELOAD_1,
        2 => SBasePawn.SA_RELOAD_2,
        3 => SBasePawn.SA_RELOAD_3,
        4 => SBasePawn.SA_RELOAD_4,
        5 => SBasePawn.SA_RELOAD_5,
        6 => SBasePawn.SA_RELOAD,
        _ => -1,
    };

    /// <summary>
    /// The view-model notifies of the shotgun's clips (the engine fires them as the clip passes them): the pump sound
    /// of the shot clip, and the shell notifies of the reload clip, before the channels advance.
    /// </summary>
    protected override void NativeTick(float DeltaSeconds)
    {
        if (ViewAnim.IsAnimating(ACTIONCHANNEL))
        {
            Name seq = ViewAnim.Sequence(ACTIONCHANNEL);
            float step = DeltaSeconds * ViewAnim.Rate(ACTIONCHANNEL);
            if (seq == ViewModelClips.FirstShooStFd2S)
            {
                float t = ViewAnim.Elapsed(ACTIONCHANNEL, K.FirstShooStFd2S) * ViewAnim.Rate(ACTIONCHANNEL);
                if (t < K.ShotgunPumpNotifySeconds && K.ShotgunPumpNotifySeconds <= t + step)
                {
                    PlaySound(PumpSound, ESoundSlot.SLOT_None, K.ShotgunPumpVolume, false, K.ShotgunPumpRadius);
                }
            }
            else if (seq == ViewModelClips.FirstReloStFd2S)
            {
                float t = ViewAnim.Elapsed(ACTIONCHANNEL, K.FirstReloStFd2S) * ViewAnim.Rate(ACTIONCHANNEL);
                for (int n = 1; n <= K.ShotgunShellNotifySeconds.Length; n++)
                {
                    float at = K.ShotgunShellNotifySeconds[n - 1];
                    if (t < at && at <= t + step && EventReloadFirst(n))
                    {
                        break;
                    }
                }
            }
        }

        base.NativeTick(DeltaSeconds);
    }

    /// <summary>After shell <paramref name="n"/>: a reload of n shells skips to the clip's end (frame 39). True when it did.</summary>
    private bool EventReloadFirst(int n)
    {
        if (iNbAmmoToReload != n)
        {
            return false;
        }

        ViewAnim.SetFrame(ACTIONCHANNEL, EndOfReloadFrame, K.FirstReloStFd2S);
        return true;
    }

    [Uc("SBase.SWeaponDefense_Shotgun.EventReloadFirst1", Sha1 = "f84db978fad84fee068ff5860a4ab4c16bd49640")]
    [PortOf("uc:SBase.SWeaponDefense_Shotgun:EventReloadFirst1", Spec = "spec/equipment/merc-guns.md#rules")]
    public void EventReloadFirst1() => EventReloadFirst(1);

    [Uc("SBase.SWeaponDefense_Shotgun.EventReloadFirst2", Sha1 = "5693b00fe3e8af25ed5811dc9d04f24145fd8d70")]
    [PortOf("uc:SBase.SWeaponDefense_Shotgun:EventReloadFirst2", Spec = "spec/equipment/merc-guns.md#rules")]
    public void EventReloadFirst2() => EventReloadFirst(2);

    [Uc("SBase.SWeaponDefense_Shotgun.EventReloadFirst3", Sha1 = "fb8ba90180fe0eb1f60ac3017525880f0bd8b98a")]
    [PortOf("uc:SBase.SWeaponDefense_Shotgun:EventReloadFirst3", Spec = "spec/equipment/merc-guns.md#rules")]
    public void EventReloadFirst3() => EventReloadFirst(3);

    [Uc("SBase.SWeaponDefense_Shotgun.EventReloadFirst4", Sha1 = "3ffdcd807b947bb4ccf60fc7e27efca1c65867a4")]
    [PortOf("uc:SBase.SWeaponDefense_Shotgun:EventReloadFirst4", Spec = "spec/equipment/merc-guns.md#rules")]
    public void EventReloadFirst4() => EventReloadFirst(4);

    [Uc("SBase.SWeaponDefense_Shotgun.EventReloadFirst5", Sha1 = "194bd89fcf3e4c817691a5bc0d9475e367595aea")]
    [PortOf("uc:SBase.SWeaponDefense_Shotgun:EventReloadFirst5", Spec = "spec/equipment/merc-guns.md#rules")]
    public void EventReloadFirst5() => EventReloadFirst(5);

    /// <summary>s_Reloading: the rifle's reload with the shell count; nothing to do with a full clip or no ammo.</summary>
    [Uc("SBase.SWeaponDefense_Shotgun.s_Reloading")]
    public new class s_Reloading : SWeaponDefense.s_Reloading
    {
        public static new readonly s_Reloading Instance = new(UcMeta.s_Reloading, SWeaponDefense.s_Reloading.Instance);

        protected s_Reloading(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SWeaponDefense_Shotgun.s_Reloading.BeginState", Sha1 = "d8b41a3ae47fab41dac87fdd6dc39ca0a49260cc")]
        [PortOf("uc:SBase.SWeaponDefense_Shotgun:s_Reloading.BeginState", Spec = "spec/equipment/merc-guns.md#rules")]
        public override void BeginState(Actor self)
        {
            var w = (SWeaponDefense_Shotgun)self;
            if (w.Ammo == 0 || w.ClipAmmo == w.ClipMaxAmmo)
            {
                w.GotoState(CNAME_s_Selected);
                w.PreviousState = CNAME_s_Selected;
                return;
            }

            w.iNbAmmoToReload = w.ShellsToLoad();
            w.ReloadBegin(K.ReloadMessageTime, ReloadAction(w.iNbAmmoToReload));
        }
    }

    /// <summary>s_FullReloading: the ammo crate's reload with the shell count (the low-ammo flash is left as it is).</summary>
    [Uc("SBase.SWeaponDefense_Shotgun.s_FullReloading")]
    public new class s_FullReloading : SWeaponDefense.s_FullReloading
    {
        public static new readonly s_FullReloading Instance = new(UcMeta.s_FullReloading, null);

        protected s_FullReloading(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SWeaponDefense_Shotgun.s_FullReloading.BeginState", Sha1 = "d81c186fbd52c28cd9022d2b19413266316997c8")]
        [PortOf("uc:SBase.SWeaponDefense_Shotgun:s_FullReloading.BeginState", Spec = "spec/equipment/merc-guns.md#rules")]
        public override void BeginState(Actor self)
        {
            var w = (SWeaponDefense_Shotgun)self;
            w.iNbAmmoToReload = w.ShellsToLoad();
            w.ReloadBegin(K.FullReloadMessageTime, ReloadAction(w.iNbAmmoToReload), ClearFlash: false);
        }
    }
}
