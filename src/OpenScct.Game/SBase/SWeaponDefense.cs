using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Combat;
using OpenScct.Game.Equipment;
using OpenScct.Game.GameFlow;
using OpenScct.Provenance;
using K = OpenScct.Game.Equipment.K;

namespace OpenScct.Game.SBase;

/// <summary>
/// SBase.SWeaponDefense: the merc rifle (spec/equipment/merc-rifle.md). The owner runs the trigger, the rate of fire,
/// the clip and reserve, the reload and the recoil; the server traces each shot with the spread of the moment and
/// deals the distance-banded damage; the victim scales it by the hit zone.
/// </summary>
public partial class SWeaponDefense
{
    /// <summary>The muzzle bone of the first-person and third-person guns.</summary>
    public static readonly Name FxBone = new("FX");

    private static readonly Name CNAME_s_Selected = new("s_Selected");
    private static readonly Name CNAME_s_Firing = new("s_Firing");
    private static readonly Name CNAME_s_Reloading = new("s_Reloading");
    private static readonly Name CNAME_s_FullReloading = new("s_FullReloading");
    private static readonly Name CNAME_s_IdleInHolster = new("s_IdleInHolster");
    private static readonly Name CNAME_s_IdleGrabbed = new("s_IdleGrabbed");
    private static readonly Name CNAME_s_Binoculars_Def = new("s_Binoculars_Def");
    private static readonly Name CNAME_ServerSetAccurateRotation = new("ServerSetAccurateRotation");
    private static readonly Name CNAME_ServerFireWeapon = new("ServerFireWeapon");
    private static readonly Name CNAME_ServerSetROF = new("ServerSetROF");
    private static readonly Name CNAME_ImpactHole = new("SImpactEffectHole");
    private static readonly Name CNAME_ImpactHoleBack = new("SImpactEffectHoleBack");
    private static readonly Name CNAME_SpawnSFX = new("SpawnSFX");
    private static readonly Name CNAME_CreateSmoke2 = new("CreateSmoke2");
    private static readonly Name CNAME_FirstPainStBk2 = new("FirstPainStBk2");
    private static readonly Name CNAME_FirstPainStFt2 = new("FirstPainStFt2");
    private static readonly Name CNAME_FirstPainStLt2 = new("FirstPainStLt2");
    private static readonly Name CNAME_FirstPainStRt2 = new("FirstPainStRt2");
    private static readonly AssetRef SkinSingle = new("SWeaponTex.Armes.SI_rof_single");
    private static readonly AssetRef SkinBurst = new("SWeaponTex.Armes.SI_rof_burst");
    private static readonly AssetRef SkinAuto = new("SWeaponTex.Armes.SI_rof_auto");
    private static readonly ClassRef FireTraceClass = new("SBase.SFireTrace");

    /// <summary>A rifle state's version of <see cref="SwitchROF"/>.</summary>
    public interface ISwitchROF
    {
        void SwitchROF(SWeaponDefense self);
    }

    /// <summary>A rifle state's version of <see cref="AnimateDraw"/>.</summary>
    public interface IAnimateDraw
    {
        void AnimateDraw(SWeaponDefense self);
    }

    /// <summary>A rifle state's version of <see cref="AnimateUnDraw"/>.</summary>
    public interface IAnimateUnDraw
    {
        void AnimateUnDraw(SWeaponDefense self);
    }

    /// <summary>A rifle state's version of <see cref="AnimateUnDraw_GRAB"/>.</summary>
    public interface IAnimateUnDraw_GRAB
    {
        void AnimateUnDraw_GRAB(SWeaponDefense self);
    }

    public void eventSwitchROF()
    {
        if (StateNode is ISwitchROF s)
        {
            s.SwitchROF(this);
        }
        else
        {
            SwitchROF();
        }
    }

    public void eventAnimateDraw()
    {
        if (StateNode is IAnimateDraw s)
        {
            s.AnimateDraw(this);
        }
        else
        {
            AnimateDraw();
        }
    }

    public void eventAnimateUnDraw()
    {
        if (StateNode is IAnimateUnDraw s)
        {
            s.AnimateUnDraw(this);
        }
        else
        {
            AnimateUnDraw();
        }
    }

    public void eventAnimateUnDraw_GRAB()
    {
        if (StateNode is IAnimateUnDraw_GRAB s)
        {
            s.AnimateUnDraw_GRAB(this);
        }
        else
        {
            AnimateUnDraw_GRAB();
        }
    }

    /// <summary>True when the owner holds the fire button.</summary>
    public bool FireHeld => (Controller ?? Instigator?.Controller) is { bFire: not 0 };

    // ---- setup ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The rifle's setup (rule 24): on the server the controller and the third-person dummy in the right hand; on
    /// every machine the walk loop.
    /// </summary>
    [Uc("SBase.SWeaponDefense.PostBeginPlay", Sha1 = "236fd9d823a7b5d38643a1eff33197e479f001ac")]
    [PortOf("uc:SBase.SWeaponDefense:PostBeginPlay", Spec = "spec/equipment/merc-rifle.md#rules")]
    protected override void PostBeginPlay()
    {
        base.PostBeginPlay();
        if (Owner is not null)
        {
            SetBase(Owner);
        }

        if (Role == ENetRole.ROLE_Authority && Instigator is not null)
        {
            Controller = Instigator.Controller;
            SpawnDummy();
        }

        ViewAnim.Loop(MVTCHANNEL, ViewModelClips.FirstWalkStFd2);
    }

    /// <summary>The weapon given to its pawn also gets its controller and dummy when the spawn had no instigator.</summary>
    public override void GiveTo(Pawn Other)
    {
        base.GiveTo(Other);
        SetBase(Other);
        if (Role == ENetRole.ROLE_Authority)
        {
            Controller = Other.Controller;
            SpawnDummy();
        }
    }

    private void SpawnDummy()
    {
        if (Dummy is not null || Instigator is not SPAWNDEFENSE merc)
        {
            return;
        }

        if (Devices.DeviceClasses.Spawn(this, DummyClass, merc, default, merc.Location, merc.Rotation) is SWeaponDefenseDummy dummy)
        {
            dummy.Instigator = merc;
            dummy.SetBase(merc);
            Dummy = dummy;
            merc.WeaponDummy = dummy;
        }
    }

    // ---- trigger --------------------------------------------------------------------------------------------

    /// <summary>
    /// The trigger (rule 2): an empty clip clicks (and reloads in single shot when there is a reserve); otherwise,
    /// with a clear muzzle, the rifle fires.
    /// </summary>
    [Uc("SBase.SWeaponDefense.Fire", Sha1 = "29f42078cbdfbd22d97d4292be9a94fdd62530fd")]
    [PortOf("uc:SBase.SWeaponDefense:Fire", Spec = "spec/equipment/merc-rifle.md#rules")]
    public override void Fire(float Value)
    {
        if (ClipAmmo == 0)
        {
            ServerSound(EmptySound, ESoundSlot.SLOT_None, (int)K.WeaponSoundRadius);
            if (ROFMode == SRateOfFireMode.ROF_Single && Ammo > 0)
            {
                eventReload();
            }

            return;
        }

        if (OwnerPC?.CheckIfCanFire() ?? true)
        {
            GotoState(CNAME_s_Firing);
        }
    }

    [Uc("SBase.SWeaponDefense.AltFire", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
    [PortOf("uc:SBase.SWeaponDefense:AltFire", Spec = "spec/equipment/merc-rifle.md#rules")]
    public override void AltFire(float Value)
    {
    }

    /// <summary>
    /// One bullet on the owner (rule 6): the aim and the shot go to the server in that order, the clip and the reserve
    /// lose a round, the HUD flashes on the last rounds, the recoil moves the next shot, the shot clip plays.
    /// </summary>
    [Uc("SBase.SWeaponDefense.FireWeapon", Sha1 = "18c3e79b883a41e2a868d56d1c9ab8f6156b3769")]
    [PortOf("uc:SBase.SWeaponDefense:FireWeapon", Spec = "spec/equipment/merc-rifle.md#rules")]
    public void FireWeapon()
    {
        BulletsToFire -= 1;
        FRotator aim = OwnerPC?.MyDesiredRotation ?? Instigator?.Rotation ?? Rotation;
        ServerSetAccurateRotation(aim.Yaw, aim.Pitch, aim.Roll);
        ServerFireWeapon(BulletInARow);
        if (ClipAmmo > 0)
        {
            ClipAmmo -= 1;
            Ammo -= 1;
            if (OwnerPC?.myHUD is SHud hud)
            {
                hud.bReticuleFlash = ClipAmmo < MinimumAmmoFlashReticule;
                if (hud.bReticuleFlash)
                {
                    hud.ReticuleFlashTime = Level.TimeSeconds;
                }
            }
        }

        SpawnSFX();
        ApplyWeaponJump();
        ViewAnim.SetAlpha(ACTIONCHANNEL, K.ShotClipAlpha);
        ViewAnim.Play(ACTIONCHANNEL, AFirstShooSt, K.FirstShooStFd2);
    }

    /// <summary>
    /// The recoil (rule 7, owner): the camera pitch and yaw and the aim pitch and yaw each kicked by fresh random
    /// numbers in the original order; pitches clamped to +-13000.
    /// </summary>
    [Uc("SBase.SWeaponDefense.ApplyWeaponJump", Sha1 = "870b787fe8c59b579c3e419e6dac405081bfbe68")]
    [PortOf("uc:SBase.SWeaponDefense:ApplyWeaponJump", Spec = "spec/equipment/merc-rifle.md#rules")]
    public void ApplyWeaponJump()
    {
        if (OwnerPC is not { } pc)
        {
            return;
        }

        FRotator view = pc.Rotation;
        view.Pitch = WeaponRules.KickPitch(view.Pitch, ValeurReculVer, FRand());
        float r2 = FRand();
        float r3 = FRand();
        view.Yaw += WeaponRules.CameraYawKick(ValeurReculHor, r2, r3);
        if (RecoilCaptured(pc))
        {
            // Reloaded's SetRotation wrapper: the controller keeps its rotation and the kick waits for the view step.
            RecoilPendingPitch += (short)(view.Pitch - pc.Rotation.Pitch);
            RecoilPendingYaw += (short)(view.Yaw - pc.Rotation.Yaw);
        }
        else
        {
            pc.Rotation = view;
        }

        FRotator desired = pc.MyDesiredRotation;
        desired.Pitch = WeaponRules.KickPitch(desired.Pitch, ValeurReculVer, FRand());
        float r5 = FRand();
        float r6 = FRand();
        desired.Yaw += WeaponRules.AimYawKick(ValeurReculHor, r5, r6);
        pc.MyDesiredRotation = desired;
    }

    /// <summary>The owner's aim for the next shot (reliable owner to server).</summary>
    [Uc("SBase.SWeaponDefense.ServerSetAccurateRotation", Sha1 = "426defaf3803e021ec78e5689fd8f9b57b004f60")]
    [PortOf("uc:SBase.SWeaponDefense:ServerSetAccurateRotation", Spec = "spec/equipment/merc-rifle.md#rules")]
    [PortOf("rep:SBase.SWeaponDefense.ServerSetAccurateRotation", Spec = "spec/combat/hit-zones-and-weapons.md#network")]
    public void ServerSetAccurateRotation(int Yaw, int Pitch, int Roll)
    {
        if (Role < ENetRole.ROLE_Authority && OwnerPC is { } pc)
        {
            CombatServices.Net(this).ServerCall(pc, new CombatCall(CNAME_ServerSetAccurateRotation)
            {
                Target = this,
                Rotation = new FRotator(Pitch, Yaw, Roll),
            });
            return;
        }

        AccurateRotation = new FRotator(Pitch, Yaw, Roll);
    }

    /// <summary>
    /// The server's shot (rule 9): the owner's burst count, the shot statistic, the spread of the moment (none in the
    /// scope), the trace, the shot sound for everyone, the third-person muzzle and the noise.
    /// </summary>
    [Uc("SBase.SWeaponDefense.ServerFireWeapon", Sha1 = "80fb83c65df93efef1b340c597a389a97cb823fc")]
    [PortOf("uc:SBase.SWeaponDefense:ServerFireWeapon", Spec = "spec/equipment/merc-rifle.md#rules")]
    public override void ServerFireWeapon(int iBulletInARow = 0)
    {
        if (Role < ENetRole.ROLE_Authority && OwnerPC is { } client)
        {
            CombatServices.Net(this).ServerCall(client, new CombatCall(CNAME_ServerFireWeapon) { Target = this, Int1 = iBulletInARow });
            return;
        }

        BulletInARow = iBulletInARow;
        if (OwnerPC is { } pc)
        {
            SPlayerController.IncStat(pc, static s => s.ShotsFired++);
        }

        DispersionActuelle = InScope ? 0f : GetAccuracy();
        TraceFire(DispersionActuelle, 0f, 0f);
        Instigator?.PlaySound(FireSingleShotSound, ESoundSlot.SLOT_None, null, false, K.ShotSoundRadius);
        if (Dummy is { } dummy)
        {
            dummy.AddMuzzleCount();
            dummy.NotifyNoise(1f);
        }
    }

    /// <summary>True when the server's copy of the owner's controller is in the scope (exactly s_Binoculars_Def).</summary>
    public bool InScope => OwnerPC?.GetStateName() == CNAME_s_Binoculars_Def;

    /// <summary>GetAccuracy (native 1286): the spread of the shot (rule 13).</summary>
    [UcNative("ASWeaponDefense::execGetAccuracy", Exe = 0x10abcfc0)]
    [Uc("SBase.SWeaponDefense.GetAccuracy", Sha1 = "067d5096f219c64b53bb1c7d5e3754285b565a47")]
    [PortOf("nat:SBase.SWeaponDefense.GetAccuracy", Spec = "spec/equipment/merc-rifle.md#rules")]
    public float GetAccuracy() =>
        WeaponRules.Accuracy(Instigator?.Velocity.Size() ?? 0f, MinDispersion, MaxDispersion, BulletInARow);

    /// <summary>
    /// The server trace of one bullet (rule 10): spread in a square around the aim, the ray from the gun or the eye,
    /// impacts and the fake ceiling pass-through on world hits, flat damage on actors, distance-banded damage (or the
    /// scope's flat snipe) on pawns, then the random tracer.
    /// </summary>
    [Uc("SBase.SWeaponDefense.TraceFire", Sha1 = "d314de74e13035858380e9a6cf85ff4ee728d52f")]
    [PortOf("uc:SBase.SWeaponDefense:TraceFire", Spec = "spec/equipment/merc-rifle.md#rules")]
    [PortOf("uc:SBase.SWeaponDefense:TraceFire", Spec = "spec/combat/hit-zones-and-weapons.md#merc-rifle-and-uzi-script-tracefire-server")]
    public void TraceFire(float Accuracy, float YOffset, float ZOffset, FVector TraceStartLocation = default)
    {
        if (Instigator is not { } shooter || Controller is null)
        {
            return;
        }

        bool onMoverMoving = shooter.Base is Mover { Velocity: var mv } && !mv.IsZero();
        AccurateRotation.GetAxes(out FVector X, out FVector Y, out FVector Z);
        FVector startTrace = onMoverMoving
            ? shooter.Location + new FVector(0f, 0f, shooter.bIsCrouched ? K.MoverAimCrouched : K.MoverAimStanding)
            : shooter.Location + CombatServices.Ports(this).EyePosition(shooter);
        float u1 = FRand() - 0.5f;
        float u2 = FRand() - 0.5f;
        FVector endTrace = startTrace + Y * (Accuracy * u1) * K.SpreadScale + Z * (Accuracy * u2) * K.SpreadScale + X * K.TraceLength;
        FVector wStart = !TraceStartLocation.IsZero() ? TraceStartLocation : onMoverMoving ? startTrace : GetFireStart();
        BoneTraceResult hit = CombatServices.HitZones(this).TraceBone(this, endTrace, wStart, true);
        int roll = SvmMath.Trunc(FRand() * K.RandomRoll);
        Actor other = hit.Actor;
        if (hit.bHit && (other.bWorldGeometry || other is Mover || other is LevelInfo))
        {
            if (other is SBasicGamePlayObject gpo)
            {
                gpo.eventTakeDamage(BaseDamage, shooter, hit.HitLocation, -hit.HitNormal * K.BulletMomentumScale, MyDamageType, hit.PillTag);
            }

            if (!hit.bIsAFakeBackdrop && hit.SurfaceType == ESurfaceType.SURFACE_FakeCeiling)
            {
                // A fake ceiling: the bullet hole on each side and the trace goes on from behind the panel (no
                // second pass-through).
                if (!TraceStartLocation.IsZero())
                {
                    return;
                }

                FRotator front = hit.HitNormal.Rotation();
                front.Roll = roll;
                ImpactManager.AddImpact(this, CNAME_ImpactHole, hit.HitLocation + hit.HitNormal * 0.25f, front);
                FVector back = hit.HitLocation - hit.HitNormal * K.FakeCeilingExit;
                FRotator backRot = (-hit.HitNormal).Rotation();
                backRot.Roll = SvmMath.Trunc(FRand() * K.RandomRoll);
                ImpactManager.AddImpact(this, CNAME_ImpactHoleBack, back, backRot);
                TraceFire(Accuracy, YOffset, ZOffset, back);
                return;
            }

            if (!hit.bIsAFakeBackdrop)
            {
                ImpactEffect(hit, roll);
            }
        }
        else if (hit.bHit && other is not SBasePawn)
        {
            other.eventTakeDamage(BaseDamage, shooter, hit.HitLocation, -hit.HitNormal * K.BulletMomentumScale, MyDamageType, hit.PillTag);
        }
        else if (hit.bHit && other is SBasePawn victim)
        {
            HitPawn(shooter, victim, hit, startTrace, wStart);
        }

        if (!InScope)
        {
            SpawnTracer(hit.HitLocation);
        }
    }

    /// <summary>A bullet on a pawn (rule 10.10): the hit statistic, the snipe or the distance damage, the blood trail.</summary>
    private void HitPawn(Pawn shooter, SBasePawn victim, in BoneTraceResult hit, FVector startTrace, FVector wStart)
    {
        if (OwnerPC is { } pc && (SGameInfo.TeamIndexOf(pc) != SGameInfo.TeamIndexOf(victim.Controller) || pc.IsMercDeathMatch))
        {
            SPlayerController.IncStat(pc, static s => s.ShotsHit++);
        }

        FVector momentum = (hit.HitLocation - startTrace) * K.BulletMomentumScale;
        if (InScope)
        {
            victim.Controller?.PawnTakeDamage(BaseDamage, shooter, hit.HitLocation, momentum, SDamageType.SDTSnipeShot, hit.PillTag,
                PercentDamageRefillable);
        }
        else
        {
            int dmg = WeaponRules.DistanceDamage(this, FVector.Dist(hit.HitLocation, wStart));
            victim.Controller?.PawnTakeDamage(dmg, shooter, hit.HitLocation, momentum, MyDamageType, hit.PillTag, PercentDamageRefillable);
        }

        if (victim is SPawnAttaque spy)
        {
            spy.BloodDropStartTime = Level.TimeSeconds;
        }
    }

    /// <summary>The impact of a world hit, chosen by the surface type, through the level's impact manager.</summary>
    private void ImpactEffect(in BoneTraceResult hit, int roll)
    {
        Name effect = ImpactClass(hit.SurfaceType);
        FRotator r = hit.HitNormal.Rotation();
        r.Roll = roll;
        ImpactManager.AddImpact(this, effect, hit.HitLocation, r);
    }

    private static readonly Name ImpactBeton = new("SImpactEffectBeton");
    private static readonly Name ImpactMetal = new("SImpactEffectMetal");
    private static readonly Name ImpactSoft = new("SImpactEffectSoft");
    private static readonly Name ImpactGlass = new("SImpactEffectGlass");
    private static readonly Name ImpactWood = new("SImpactEffectWood");
    private static readonly Name ImpactFence = new("SImpactEffectFence");
    private static readonly Name ImpactWater = new("SImpactEffectWater");
    private static readonly Name ImpactSteamPipe = new("SImpactSteamPipe");
    private static readonly Name ImpactWaterPipe = new("SImpactWaterPipe");
    private static readonly Name ImpactSand = new("SImpactEffectSand");
    private static readonly Name ImpactBase = new("Engine.SImpactEffectBase");

    /// <summary>The impact class of a surface type (rule 10.8).</summary>
    public static Name ImpactClass(ESurfaceType Surface) => Surface switch
    {
        ESurfaceType.SURFACE_Generic or ESurfaceType.SURFACE_ConcreteHard or ESurfaceType.SURFACE_ConcreteDirt or
            ESurfaceType.SURFACE_Gravel => ImpactBeton,
        ESurfaceType.SURFACE_MetalHard or ESurfaceType.SURFACE_MetalReverb or ESurfaceType.SURFACE_MetalSheet => ImpactMetal,
        ESurfaceType.SURFACE_Carpet => ImpactSoft,
        ESurfaceType.SURFACE_BreakingGlass => ImpactGlass,
        ESurfaceType.SURFACE_WoodHard or ESurfaceType.SURFACE_WoodBoomy => ImpactWood,
        ESurfaceType.SURFACE_FenceMetal => ImpactFence,
        ESurfaceType.SURFACE_MediumWater => ImpactWater,
        ESurfaceType.SURFACE_SteamPipe => ImpactSteamPipe,
        ESurfaceType.SURFACE_WaterPipe => ImpactWaterPipe,
        ESurfaceType.SURFACE_Sand => ImpactSand,
        _ => ImpactBase,
    };

    /// <summary>The tracer (rule 10.11): its chance by fire mode, tested with one more random number.</summary>
    private void SpawnTracer(FVector impact)
    {
        float percent = ROFMode switch
        {
            SRateOfFireMode.ROF_Single => PourcBTSingle,
            SRateOfFireMode.ROF_Burst => PourcBTBurst,
            _ => PourcBTFullAuto,
        };

        if (!(FRand() < percent / K.TracerPercent))
        {
            return;
        }

        FCoords fx = CombatServices.Ports(this).BoneCoords(this, FxBone);
        if (Devices.DeviceClasses.Spawn(this, FireTraceClass, Instigator, default, fx.Origin, AccurateRotation) is SFireTrace trace)
        {
            trace.ImpactLoc = impact;
            trace.Instigator = Instigator;
        }
    }

    /// <summary>
    /// The ray start (rule 10.6): with the server-side laser on, the gun muzzle pulled back 50 uu; else the eye point.
    /// </summary>
    [Uc("SBase.SWeaponDefense.GetFireStart", Sha1 = "dff3dd8d4f679b00dfb623bf138b177deb92c296")]
    [PortOf("uc:SBase.SWeaponDefense:GetFireStart", Spec = "spec/equipment/merc-rifle.md#rules")]
    public override FVector GetFireStart()
    {
        if (Instigator is SPAWNDEFENSE { bLaserOn: true })
        {
            FCoords fx = CombatServices.Ports(this).BoneCoords(this, FxBone);
            return fx.Origin - fx.XAxis * K.LaserFireStartPullBack;
        }

        return base.GetFireStart();
    }

    // ---- rate of fire ---------------------------------------------------------------------------------------

    /// <summary>The shots of one trigger pull.</summary>
    [Uc("SBase.SWeaponDefense.GetNbBulletsFromROF", Sha1 = "b2255164cf812413b1f582a576a3a74bb8e6bfef")]
    [PortOf("uc:SBase.SWeaponDefense:GetNbBulletsFromROF", Spec = "spec/equipment/merc-rifle.md#rules")]
    public int GetNbBulletsFromROF() => WeaponRules.BulletsForMode(ROFMode, ClipAmmo);

    /// <summary>The owner's rate of fire on the server (reliable owner to server).</summary>
    [Uc("SBase.SWeaponDefense.ServerSetROF", Sha1 = "1deec70343687193c452a617b95e68abda610ed7")]
    [PortOf("uc:SBase.SWeaponDefense:ServerSetROF", Spec = "spec/equipment/merc-rifle.md#rules")]
    public void ServerSetROF(SRateOfFireMode newROFMode)
    {
        if (Role < ENetRole.ROLE_Authority && OwnerPC is { } pc)
        {
            CombatServices.Net(this).ServerCall(pc, new CombatCall(CNAME_ServerSetROF) { Target = this, Int1 = (int)newROFMode });
            return;
        }

        ROFMode = newROFMode;
    }

    /// <summary>The ROF button (rule 16): Auto and Single to Burst, Burst to Auto, with the sound, skin and server copy.</summary>
    [Uc("SBase.SWeaponDefense.SwitchROF", Sha1 = "63b06ca88f96c5c10ed1ef13fd0c2bf4b74f32af")]
    [PortOf("uc:SBase.SWeaponDefense:SwitchROF", Spec = "spec/equipment/merc-rifle.md#rules")]
    public void SwitchROF() => SetMode(WeaponRules.NextMode(ROFMode));

    /// <summary>Single shot for the scope, remembering the mode (when not single yet, or on entry).</summary>
    [Uc("SBase.SWeaponDefense.ROF_Single", Sha1 = "bd5e771871aab1ce31c8c2d3a3c67128d6119ee4")]
    [PortOf("uc:SBase.SWeaponDefense:ROF_Single", Spec = "spec/equipment/merc-rifle.md#rules")]
    public void ROF_Single(bool bFirst)
    {
        if (ROFMode != SRateOfFireMode.ROF_Single || bFirst)
        {
            PreviousROFMode = ROFMode;
            SetMode(SRateOfFireMode.ROF_Single);
        }
    }

    /// <summary>The mode remembered by ROF_Single, back after the scope.</summary>
    [Uc("SBase.SWeaponDefense.PreviousMode", Sha1 = "1c7afa2894e97c755a2c02a121d8a150cb8cbb0c")]
    [PortOf("uc:SBase.SWeaponDefense:PreviousMode", Spec = "spec/equipment/merc-rifle.md#rules")]
    public void PreviousMode()
    {
        if (ROFMode != PreviousROFMode)
        {
            SetMode(PreviousROFMode);
        }
    }

    /// <summary>The HUD name of the mode.</summary>
    [Uc("SBase.SWeaponDefense.GetROFName", Sha1 = "9d1e9cd3c3b597b0e6c332818ddd00bf0906436d")]
    [PortOf("uc:SBase.SWeaponDefense:GetROFName", Spec = "spec/equipment/merc-rifle.md#rules")]
    public string GetROFName() => WeaponRules.ModeName(ROFMode);

    private void SetMode(SRateOfFireMode mode)
    {
        ServerSound(SwitchROFSound, ESoundSlot.SLOT_None, (int)K.WeaponSoundRadius);
        ROFMode = mode;
        if (Skins.Count > ROF_MaterialID)
        {
            Skins[ROF_MaterialID] = mode switch
            {
                SRateOfFireMode.ROF_Single => SkinSingle,
                SRateOfFireMode.ROF_Burst => SkinBurst,
                _ => SkinAuto,
            };
        }

        ServerSetROF(mode);
    }

    // ---- reload ---------------------------------------------------------------------------------------------

    /// <summary>The reload (rule 17): refused while the HUD is perturbed; only with room in the clip and a reserve.</summary>
    [Uc("SBase.SWeaponDefense.Reload", Sha1 = "78d14d7dcef9dfb2f3dddb40ce938447cfbc4385")]
    [PortOf("uc:SBase.SWeaponDefense:Reload", Spec = "spec/equipment/merc-rifle.md#rules")]
    public override bool Reload()
    {
        if (OwnerPC?.IsHudPerturbed ?? false)
        {
            return false;
        }

        if (ClipAmmo < ClipMaxAmmo && Ammo - ClipAmmo > 0)
        {
            GotoState(CNAME_s_Reloading);
            return true;
        }

        return false;
    }

    /// <summary>The ammo crate's full reload (rule 18): refused while perturbed; nothing when the reserve is full.</summary>
    [Uc("SBase.SWeaponDefense.FullReload", Sha1 = "23ac0a9557adb387cdb52179780857790836e01b")]
    [PortOf("uc:SBase.SWeaponDefense:FullReload", Spec = "spec/equipment/merc-rifle.md#rules")]
    [PortOf("uc:SBase.SWeaponDefense:FullReload", Spec = "spec/equipment/ammo-stock.md#rules")]
    public override bool FullReload()
    {
        if (OwnerPC?.IsHudPerturbed ?? false)
        {
            return false;
        }

        if (Ammo < MaxAmmo)
        {
            GotoState(CNAME_s_FullReloading);
            return true;
        }

        return false;
    }

    /// <summary>The server plays an owner sound on the pawn (reliable owner to server).</summary>
    [Uc("SBase.SWeaponDefense.ServerSound", Sha1 = "02e49fc938c0e540b1fa74b9d1a577ba2172618a")]
    [PortOf("uc:SBase.SWeaponDefense:ServerSound", Spec = "spec/equipment/merc-rifle.md#network")]
    public override void ServerSound(AssetRef MySound, ESoundSlot Slot = ESoundSlot.SLOT_None, int Radius = 0) =>
        base.ServerSound(MySound, Slot, Radius);

    // ---- tick ---------------------------------------------------------------------------------------------

    /// <summary>The global tick: the native spread decay.</summary>
    [Uc("SBase.SWeaponDefense.Tick", Sha1 = "f722d00d54cc6605a6a8eb31656fa13351c1e4ee")]
    [PortOf("uc:SBase.SWeaponDefense:Tick", Spec = "spec/equipment/merc-rifle.md#rules")]
    protected override void Tick(float DeltaTime) => NativeTickSWeaponDefense(DeltaTime);

    /// <summary>
    /// NativeTickSWeaponDefense (native 1285, decoded part, rule 12): the spread stays open one RateOfFire after the
    /// last shot, then BulletInARow drops by one every 0.02 s.
    /// </summary>
    [UcNative("ASWeaponDefense::execNativeTickSWeaponDefense", Exe = 0x10abcdb0)]
    [Uc("SBase.SWeaponDefense.NativeTickSWeaponDefense", Sha1 = "d37def7e4d9a81ab95253a74558109081344f7b1")]
    [PortOf("nat:SBase.SWeaponDefense.NativeTickSWeaponDefense", Spec = "spec/equipment/merc-rifle.md#rules")]
    public void NativeTickSWeaponDefense(float dt)
    {
        float now = Level.TimeSeconds;
        if (BulletInARow != oldBulletInARow)
        {
            oldBulletInARow = BulletInARow;
            TimeAccReset = now + RateOfFire;
        }
        else if (now > TimeAccReset)
        {
            if (BulletInARow > 0)
            {
                BulletInARow -= 1;
                oldBulletInARow = BulletInARow;
            }

            TimeAccReset = now + K.BulletInARowDecay;
        }

        ReloadedRecoilStep(dt);
    }

    // ---- view model ------------------------------------------------------------------------------------------

    /// <summary>The draw: the view model shown, the draw clip backward at rate 2.</summary>
    [Uc("SBase.SWeaponDefense.AnimateDraw", Sha1 = "05b49f886c23badae67203ceb0bb789fe08308d5")]
    [PortOf("uc:SBase.SWeaponDefense:AnimateDraw", Spec = "spec/equipment/merc-rifle.md#rules")]
    public void AnimateDraw()
    {
        bDrawWeapon = true;
        ViewAnim.Play(MVTCHANNEL, ViewModelClips.FirstDrawStEd2, K.FirstDrawStEd2, K.DrawClipRate, true);
    }

    /// <summary>The holster: the holster state plays the draw clip forward.</summary>
    [Uc("SBase.SWeaponDefense.AnimateUnDraw", Sha1 = "f5085ccdb7d6864d7d8aba63752564a2110b5777")]
    [PortOf("uc:SBase.SWeaponDefense:AnimateUnDraw", Spec = "spec/equipment/merc-rifle.md#rules")]
    [Provenance(ProvenanceKind.Estimated, "AnimateUnDraw body not specced: the holster state and its clip")]
    public void AnimateUnDraw() => GotoState(CNAME_s_IdleInHolster);

    /// <summary>The weapon lowered for a grab: the grab clip.</summary>
    [Uc("SBase.SWeaponDefense.AnimateUnDraw_GRAB", Sha1 = "b960ea6e60ec28dc00e6dc55cfe1551f2a758622")]
    [PortOf("uc:SBase.SWeaponDefense:AnimateUnDraw_GRAB", Spec = "spec/equipment/merc-rifle.md#rules")]
    public void AnimateUnDraw_GRAB() => ViewAnim.Play(MVTCHANNEL, ViewModelClips.FirstGrbdStBg2, K.FirstGrbdStBg2);

    /// <summary>The idle or walk loop by speed.</summary>
    [Uc("SBase.SWeaponDefense.AnimateWaiting", Sha1 = "a64e0b84d08ee3b04450b079bd517adb236f7723")]
    [PortOf("uc:SBase.SWeaponDefense:AnimateWaiting", Spec = "spec/equipment/merc-rifle.md#rules")]
    public void AnimateWaiting()
    {
        float speed = Instigator?.Velocity.Size() ?? 0f;
        if (speed > K.WalkClipMinSpeed)
        {
            // The walk loop's rate follows the speed: 2.08 at the merc's 346.5 uu/s (measured live in the original).
            ViewAnim.Loop(MVTCHANNEL, ViewModelClips.FirstWalkStFd2, SvmMath.Clamp(speed * K.WalkClipRatePerSpeed, 0f, K.WalkClipMaxRate));
        }
        else if (ViewAnim.Sequence(MVTCHANNEL) != ViewModelClips.FirstWaitStFd2)
        {
            ViewAnim.Loop(MVTCHANNEL, ViewModelClips.FirstWaitStFd2);
        }
    }

    /// <summary>The revive clip on the action channel.</summary>
    [Uc("SBase.SWeaponDefense.AnimateReanimate", Sha1 = "af352c5b906c74fc129ad1be7eb5f4eaf7aff065")]
    [PortOf("uc:SBase.SWeaponDefense:AnimateReanimate", Spec = "spec/equipment/merc-rifle.md#rules")]
    public void AnimateReanimate()
    {
        ViewAnim.SetAlpha(ACTIONCHANNEL, 1f);
        ViewAnim.Play(ACTIONCHANNEL, ViewModelClips.FirstReanStFd2, ViewModelClips.DataOf(ViewModelClips.FirstReanStFd2));
    }

    [Uc("SBase.SWeaponDefense.AnimateJump", Sha1 = "f6a6206d53bc0185133ad3f436fd8787032f0826")]
    [PortOf("uc:SBase.SWeaponDefense:AnimateJump", Spec = "spec/equipment/merc-rifle.md#rules")]
    public void AnimateJump() => ViewAnim.Loop(MVTCHANNEL, ViewModelClips.FirstJumpStFd2);

    [Uc("SBase.SWeaponDefense.AnimateLand", Sha1 = "60a24758dadf25c221eadfad16246e9b93bc857c")]
    [PortOf("uc:SBase.SWeaponDefense:AnimateLand", Spec = "spec/equipment/merc-rifle.md#rules")]
    public void AnimateLand(float TweenTime = 0f) =>
        ViewAnim.Play(MVTCHANNEL, ViewModelClips.FirstLandStFd2, ViewModelClips.DataOf(ViewModelClips.FirstLandStFd2));

    [Uc("SBase.SWeaponDefense.AnimateLandHigh", Sha1 = "cdae3cbaedbeb783a6ae59a385fc8268857a2afc")]
    [PortOf("uc:SBase.SWeaponDefense:AnimateLandHigh", Spec = "spec/equipment/merc-rifle.md#rules")]
    public void AnimateLandHigh(float TweenTime = 0f) =>
        ViewAnim.Play(MVTCHANNEL, ViewModelClips.FirstLandStHi2, ViewModelClips.DataOf(ViewModelClips.FirstLandStHi2));

    [Uc("SBase.SWeaponDefense.AnimateFall", Sha1 = "7f26bba6433dfd00dfddec4ec0b69dfe6a5b540c")]
    [PortOf("uc:SBase.SWeaponDefense:AnimateFall", Spec = "spec/equipment/merc-rifle.md#rules")]
    public void AnimateFall() => ViewAnim.Loop(MVTCHANNEL, ViewModelClips.FirstFallStFd2);

    /// <summary>The knock-back clip on the pain channel.</summary>
    [Uc("SBase.SWeaponDefense.AnimateBlast", Sha1 = "b8ecd8adcbad4b1ae7aa09f7d63865f5cef63586")]
    [PortOf("uc:SBase.SWeaponDefense:AnimateBlast", Spec = "spec/equipment/merc-rifle.md#rules")]
    public void AnimateBlast()
    {
        ViewAnim.SetAlpha(PAINCHANNEL, 1f);
        ViewAnim.Play(PAINCHANNEL, ViewModelClips.FirstStrkStFd2, ViewModelClips.DataOf(ViewModelClips.FirstStrkStFd2));
    }

    /// <summary>The pain clip of a hit side (0 back, 1 front, 2 left, 3 right) on the pain channel.</summary>
    [Uc("SBase.SWeaponDefense.AnimatePain", Sha1 = "383db524cf4dc8266c8b08033dd0b4593b8437db")]
    [PortOf("uc:SBase.SWeaponDefense:AnimatePain", Spec = "spec/equipment/merc-rifle.md#rules")]
    public void AnimatePain(int Side = 0)
    {
        Name clip = Side switch
        {
            1 => CNAME_FirstPainStFt2,
            2 => CNAME_FirstPainStLt2,
            3 => CNAME_FirstPainStRt2,
            _ => CNAME_FirstPainStBk2,
        };

        ViewAnim.SetAlpha(PAINCHANNEL, 1f);
        ViewAnim.Play(PAINCHANNEL, clip, ViewModelClips.DataOf(clip));
    }

    /// <summary>The scope view model lowered back: the snipe clip backward, the view model shown.</summary>
    [Uc("SBase.SWeaponDefense.AnimateUndrawSniping", Sha1 = "fa3f8e05ec0d244a869e45e5eea81fa48e0d0950")]
    [PortOf("uc:SBase.SWeaponDefense:AnimateUndrawSniping", Spec = "spec/equipment/merc-scope.md#rules")]
    public void AnimateUndrawSniping()
    {
        bDrawWeapon = true;
        ViewAnim.Play(SNIPECHANNEL, ViewModelClips.FirstSnipStBg2, K.FirstSnipStBg2, K.SnipeClipRate, true);
    }

    /// <summary>
    /// The global AnimEnd (rule 24): after the jump clip the fall loop when falling, else the idle; after a landing
    /// or draw clip the idle; the pain channel blends out unless the electric clip plays; the snipe channel stops.
    /// </summary>
    [Uc("SBase.SWeaponDefense.AnimEnd", Sha1 = "91be1971a52e80107f61513ecc6a02edddd60e8b")]
    [PortOf("uc:SBase.SWeaponDefense:AnimEnd", Spec = "spec/equipment/merc-rifle.md#rules")]
    protected override void AnimEnd(int Channel)
    {
        switch (Channel)
        {
            case MVTCHANNEL:
                if (Instigator is { Physics: EPhysics.PHYS_Falling })
                {
                    AnimateFall();
                }
                else
                {
                    AnimateWaiting();
                }

                break;
            case PAINCHANNEL:
                if (ViewAnim.Sequence(PAINCHANNEL) != ViewModelClips.FirstElecStFd2)
                {
                    ViewAnim.SetAlpha(PAINCHANNEL, 0f);
                }

                break;
            case SNIPECHANNEL:
                ViewAnim.SetAlpha(SNIPECHANNEL, 0f);
                break;
        }
    }

    /// <summary>The first-person muzzle effects (a view effect).</summary>
    [Uc("SBase.SWeaponDefense.SpawnSFX", Sha1 = "1a99da366a6976dcc1a814919a71bbaff447fac1")]
    [PortOf("uc:SBase.SWeaponDefense:SpawnSFX", Spec = "spec/equipment/merc-rifle.md#rules")]
    public void SpawnSFX() => CombatFx.Notify(this, CNAME_SpawnSFX);

    /// <summary>The barrel smoke after a burst (a view effect).</summary>
    [Uc("SBase.SWeaponDefense.CreateSmoke2", Sha1 = "9116098e3a572b5ba37edd478f6424542f1d6f3a")]
    [PortOf("uc:SBase.SWeaponDefense:CreateSmoke2", Spec = "spec/equipment/merc-rifle.md#rules")]
    public void CreateSmoke2() => CombatFx.Notify(this, CNAME_CreateSmoke2);

    /// <summary>A reticle message on the owner's HUD (the server-to-client function always runs locally).</summary>
    [Uc("SBase.SWeaponDefense.LocalMessage", Sha1 = "f2493b837e571ebcffa102a61299a768380d8066")]
    [PortOf("uc:SBase.SWeaponDefense:LocalMessage", Spec = "spec/equipment/merc-rifle.md#network")]
    public void LocalMessage(string Message, float Time, byte prio) =>
        CombatFx.Notify(OwnerPC ?? (Actor)this, CombatFx.LocalMessage, Time, prio);
}
