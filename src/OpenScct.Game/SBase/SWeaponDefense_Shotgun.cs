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
/// stops after the shells it needs, and the native nine-pellet trace on the server (natives2-damage.md rule 7).
/// </summary>
public partial class SWeaponDefense_Shotgun
{
    public static new readonly StateTable ClassStates = new(SWeaponDefense.ClassStates, s_Reloading.Instance, s_FullReloading.Instance);

    public override StateTable GetStateTable() => ClassStates;

    private static readonly Name CNAME_s_Selected = new("s_Selected");
    private static readonly AssetRef PumpSound = new("Armes_impacts.Armes.Reload_Def_Pump");
    private static readonly ClassRef ShotGunProjectorClass = new("SBase.SShotGunProjector");

    // The actors the pellets of one shot hit, each with its largest pellet (TraceFire scratch, cleared after use).
    private readonly Actor?[] _pelletActor = new Actor?[K.ShotgunPellets];
    private readonly int[] _pelletDamage = new int[K.ShotgunPellets];
    private readonly int[] _pelletPill = new int[K.ShotgunPellets];
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
    /// The first-person muzzle effects; a client also draws the pellet marks of its own shot at once (the host draws
    /// them from the third-person gun).
    /// </summary>
    [Uc("SBase.SWeaponDefense_Shotgun.SpawnSFX", Sha1 = "6de5a8775bdf7117f4777993cc928031d2dbcc24")]
    [PortOf("uc:SBase.SWeaponDefense_Shotgun:SpawnSFX", Spec = "spec/equipment/merc-guns.md#rules")]
    public override void SpawnSFX()
    {
        base.SpawnSFX();
        if (Role != ENetRole.ROLE_Authority)
        {
            SpawnImpacts();
        }
    }

    /// <summary>
    /// The owner's pellet marks: two traces along the gun at the pattern's sides give the near and far range, then
    /// eight marks are thrown at it. Drawing only; no damage.
    /// </summary>
    [Uc("SBase.SWeaponDefense_Shotgun.SpawnImpacts", Sha1 = "23634eafdb43f0533b798bdb8b6f95b04d88e4ca")]
    [PortOf("uc:SBase.SWeaponDefense_Shotgun:SpawnImpacts", Spec = "spec/equipment/merc-guns.md#rules")]
    public void SpawnImpacts()
    {
        if (Instigator is not SBasePawn { MyWeapon: { } gun })
        {
            return;
        }

        FCoords fx = CombatServices.Ports(this).BoneCoords(gun, FxBone);
        FVector orig = fx.Origin - fx.XAxis * K.ShotgunDecalPullBack;
        FVector start = gun.GetFireStart();
        FVector end = start + fx.XAxis * gun.ShootingRange;
        IHitZones zones = CombatServices.HitZones(this);

        // The first side trace still has the range at 0, so it runs straight along the gun.
        int near = 0;
        BoneTraceResult hit = zones.TraceBone(this, end + fx.YAxis * (near * K.ShotgunDecalSpread), start, true);
        if (!hit.bHit)
        {
            return;
        }

        near = SvmMath.Trunc(FVector.Dist(hit.HitLocation, orig));
        int far = near;
        hit = zones.TraceBone(this, end - fx.YAxis * (near * K.ShotgunDecalSpread), start, true);
        if (!hit.bHit)
        {
            return;
        }

        int d = SvmMath.Trunc(FVector.Dist(hit.HitLocation, orig));
        near = SvmMath.Min(near, d) - K.ShotgunDecalMargin;
        far = SvmMath.Max(far, d) + K.ShotgunDecalMargin;
        SpawnPelletMarks(this, fx, near, far);
    }

    /// <summary>
    /// The eight pellet marks (SShotGunProjector) thrown from 50 behind the gun's FX bone towards the near range,
    /// spread by it, each left for 10 s. Shared by the owner's gun and the third-person gun.
    /// </summary>
    internal static void SpawnPelletMarks(Actor Spawner, FCoords Fx, int MinProjDistance, int MaxProjDistance)
    {
        SimRandom rng = Spawner.XLevel.World.Random;
        FVector x = Fx.XAxis;
        FVector y = Fx.YAxis;
        FVector z = Fx.ZAxis;
        FVector orig = Fx.Origin - x * K.ShotgunDecalPullBack;
        FRotator rot = x.Rotation();
        float near = MinProjDistance;
        for (int i = 0; i < K.ShotgunDecals; i++)
        {
            rot.Roll = rng.Rand(K.FullRoll);
            FVector at = orig + x * SvmMath.Max(0f, near - K.ShotgunDecalMargin) + z * K.ShotgunDecalOffset - y * K.ShotgunDecalOffset;
            at += y * ((rng.FRand() - 0.5f) * near * K.ShotgunDecalSpread);
            at += z * ((rng.FRand() - 0.5f) * near * K.ShotgunDecalSpread);
            if (Devices.DeviceClasses.Spawn(Spawner, ShotGunProjectorClass, Spawner, default, at, rot) is not SShotGunProjector mark)
            {
                continue;
            }

            mark.MaxTraceDistance = K.ShotgunDecalTraceExtra + MaxProjDistance - MinProjDistance;
            mark.DrawScale = near * K.ShotgunDecalScale;
            mark.AttachProjector();
            mark.AbandonProjector(K.ShotgunDecalLifetime);
            mark.Destroy();
        }
    }

    /// <summary>The owner already draws its pellet marks at once (<see cref="SpawnImpacts"/>), so nothing is predicted.</summary>
    protected override bool PredictsImpacts => false;

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

    // ---- the native pellet trace ------------------------------------------------------------------------------

    /// <summary>
    /// TraceFire (native 1799, server): nine pellets around the aim, each a bone trace through fake ceilings; each
    /// hit actor takes only its largest pellet, with the distance damage of the merc gun bands (flat CMax up close)
    /// times 1, 0.3 (ring) or 0.5 (diagonals). The near and far hit ranges go to the third-person gun for the marks.
    /// </summary>
    [UcNative("ASWeaponDefense_Shotgun::execTraceFire", Exe = 0x10ab9d00, Editor = 0x111d69c0)]
    [Uc("SBase.SWeaponDefense_Shotgun.TraceFire", Sha1 = "205915467dd36e8dcbd313d69a76ff8fee165888")]
    [PortOf("nat:SBase.SWeaponDefense_Shotgun.TraceFire", Spec = "spec/native/natives2-damage.md#rules")]
    [Provenance(ProvenanceKind.Estimated, "The eye and mover aim offsets are taken along world Z; the fire start is the rifle's GetFireStart (the native 0x111d66e0 is not ported)")]
    public override void TraceFire(float Accuracy, float YOffset, float ZOffset, FVector TraceStartLocation = default)
    {
        if (Instigator is not { Controller: not null } shooter)
        {
            return;
        }

        AccurateRotation.GetAxes(out FVector fwd, out FVector right, out FVector up);
        bool onStillMover = shooter.Base is Mover { Velocity: var mv } && mv.IsZero();
        FVector aim = onStillMover
            ? shooter.Location + new FVector(0f, 0f, shooter.bIsCrouched ? K.MoverAimCrouched : K.MoverAimStanding)
            : shooter.Location + new FVector(0f, 0f, shooter.EyeHeight) + shooter.WalkBob;
        FVector start = !TraceStartLocation.IsZero() ? TraceStartLocation : onStillMover ? aim : GetFireStart();
        float range = ShootingRange;
        float f1 = (FRand() - 0.5f) * Accuracy;
        float f2 = (FRand() - 0.5f) * Accuracy;
        FVector end = aim + fwd * range + up * (f1 * range * K.ShotgunSpread) + right * (f2 * range * K.ShotgunSpread);
        FVector ring = range * K.ShotgunRing * up;
        FVector side = range * K.ShotgunRing * right;
        FVector diagonal = range * K.ShotgunDiagonal * (up + right);

        int nearest = ShootingRange;
        int farthest = 0;
        int count = 0;
        for (int i = 0; i < K.ShotgunPellets; i++)
        {
            // Each pellet's end steps from the last: centre, up, down, right, left, then the four diagonals.
            end += i switch
            {
                1 => ring,
                2 => -2f * ring,
                3 => ring + side,
                4 => -2f * side,
                5 => diagonal,
                6 => side,
                7 => -1f * ring,
                8 => -1f * side,
                _ => default,
            };

            float factor = i == 0 ? 1f : i <= 4 ? K.ShotgunRingFactor : K.ShotgunDiagonalFactor;
            Pellet(start, end, factor, ref nearest, ref farthest, ref count);
        }

        if (Dummy is SWeaponDefenseDummy_Shotgun gun)
        {
            gun.MinProjDistance = nearest;
            gun.MaxProjDistance = SvmMath.Min(farthest, nearest + K.ShotgunImpactWindow);
        }

        for (int i = 0; i < count; i++)
        {
            Actor victim = _pelletActor[i]!;
            int damage = _pelletDamage[i];
            FVector momentum = (_pelletHit[i] - aim) * K.BulletMomentumScale;
            _pelletActor[i] = null;
            if (damage == 0 || victim.bDeleteMe)
            {
                continue;
            }

            if (victim is SBasePawn pawn)
            {
                pawn.LastBulletWeapon = this;
                pawn.Controller?.PawnTakeDamage(damage, shooter, _pelletHit[i], momentum, MyDamageType, _pelletPill[i], PercentDamageRefillable);
            }
            else
            {
                victim.eventTakeDamage(damage, shooter, _pelletHit[i], momentum, MyDamageType);
            }
        }
    }

    /// <summary>One pellet: the trace (again from behind a fake ceiling), the ranges, the actor's largest pellet.</summary>
    private void Pellet(FVector start, FVector end, float factor, ref int nearest, ref int farthest, ref int count)
    {
        IHitZones zones = CombatServices.HitZones(this);
        BoneTraceResult hit = zones.TraceBone(this, end, start, true);
        if (!hit.bHit)
        {
            return;
        }

        int range = SvmMath.Trunc(FVector.Dist(hit.HitLocation, start));
        if (range <= nearest)
        {
            nearest = range;
        }

        if (farthest <= range)
        {
            farthest = range;
        }

        if (hit.SurfaceType == ESurfaceType.SURFACE_FakeCeiling)
        {
            hit = zones.TraceBone(this, end, hit.HitLocation - hit.HitNormal * K.FakeCeilingExit, true);
            if (!hit.bHit)
            {
                return;
            }
        }

        int damage = SvmMath.Trunc(SvmMath.Trunc(PelletDamage(FVector.Dist(hit.HitLocation, start))) * factor);
        int slot = 0;
        while (slot < count && !ReferenceEquals(_pelletActor[slot], hit.Actor))
        {
            slot++;
        }

        if (slot < count && damage <= _pelletDamage[slot])
        {
            return;
        }

        if (slot == count)
        {
            _pelletActor[slot] = hit.Actor;
            count++;
        }

        _pelletDamage[slot] = damage;
        _pelletPill[slot] = hit.PillTag;
        _pelletHit[slot] = hit.HitLocation;
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
