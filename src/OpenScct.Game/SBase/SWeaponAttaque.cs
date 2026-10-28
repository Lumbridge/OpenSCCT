using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Combat;
using OpenScct.Game.Equipment;
using OpenScct.Physics.Collision;
using OpenScct.Provenance;
using K = OpenScct.Game.Equipment.K;

namespace OpenScct.Game.SBase;

/// <summary>
/// SBase.SWeaponAttaque: the spy's pistol (spec/equipment/spy-shocker.md). The owner draws, holsters and fires; the
/// server traces the shot from the pistol's placement on the owner's machine and gates the laser.
/// </summary>
public partial class SWeaponAttaque
{
    public static new readonly StateTable ClassStates = new(SWeapon.ClassStates, s_Selected.Instance);

    public override StateTable GetStateTable() => ClassStates;

    private static readonly Name CNAME_ServerSwitchLaser = new("ServerSwitchLaser");
    private static readonly Name CNAME_ServerFireWeaponWithLocation = new("ServerFireWeaponWithLocation");
    private static readonly Name BoneRightHand = new("B R Hand");
    private static readonly Name BoneLeftHand = new("B L Hand");
    private static readonly Name ImpactShocker = new("SImpactEffectShocker");
    private static readonly Name ImpactShockerMetal = new("SImpactEffectShockerMetal");

    /// <summary>The server sets the controller and the spy's third-person weapon reference (rule 12).</summary>
    [Uc("SBase.SWeaponAttaque.PostBeginPlay", Sha1 = "7bbc87a2eb46474b07bffbf929e9141c9742ce5d")]
    [PortOf("uc:SBase.SWeaponAttaque:PostBeginPlay", Spec = "spec/equipment/spy-shocker.md#rules")]
    protected override void PostBeginPlay()
    {
        base.PostBeginPlay();
        if (Role == ENetRole.ROLE_Authority && Instigator is SPawnAttaque spy)
        {
            Controller = spy.Controller;
            spy.WeaponDummy = this;
        }
    }

    /// <summary>The idle clip for the replicated stance (rule 12).</summary>
    [Uc("SBase.SWeaponAttaque.PostNetBeginPlay", Sha1 = "222767cc96b8ac05c20ae8cce73dfc84d7bb968a")]
    [PortOf("uc:SBase.SWeaponAttaque:PostNetBeginPlay", Spec = "spec/equipment/spy-shocker.md#rules")]
    protected override void PostNetBeginPlay()
    {
        base.PostNetBeginPlay();
        Name clip = Instigator is SBasePawn { WeaponLoaded: 2 } ? ViewModelClips.SnipeP190 : ViewModelClips.WaitP190;
        ViewAnim.Loop(DEFAULTCHANNEL, clip);
    }

    /// <summary>The laser beam and projector go with the pistol.</summary>
    [Uc("SBase.SWeaponAttaque.Destroyed", Sha1 = "aff0afd2c493f54fecf96ff8e8979a5a24272074")]
    [PortOf("uc:SBase.SWeaponAttaque:Destroyed", Spec = "spec/equipment/spy-shocker.md#rules")]
    protected override void Destroyed()
    {
        LaserBeam?.Destroy();
        LaserBeam = null;
        LaserProjector?.Destroy();
        LaserProjector = null;
        base.Destroyed();
    }

    [Uc("SBase.SWeaponAttaque.Fire", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
    [PortOf("uc:SBase.SWeaponAttaque:Fire", Spec = "spec/equipment/spy-shocker.md#rules")]
    public override void Fire(float Value)
    {
    }

    [Uc("SBase.SWeaponAttaque.AltFire", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
    [PortOf("uc:SBase.SWeaponAttaque:AltFire", Spec = "spec/equipment/spy-shocker.md#rules")]
    public override void AltFire(float Value)
    {
    }

    /// <summary>The laser request (reliable owner to server); the server's tick gates it by the controller state.</summary>
    [Uc("SBase.SWeaponAttaque.ServerSwitchLaser", Sha1 = "961abb207b31680d8861b3c5e5fe527be91ad229")]
    [PortOf("uc:SBase.SWeaponAttaque:ServerSwitchLaser", Spec = "spec/equipment/spy-shocker.md#rules")]
    public void ServerSwitchLaser(bool bOn)
    {
        if (Role < ENetRole.ROLE_Authority && OwnerPC is { } pc)
        {
            CombatServices.Net(this).ServerCall(pc, new CombatCall(CNAME_ServerSwitchLaser) { Target = this, Bool1 = bOn });
            return;
        }

        bLaserShouldBeOn = bOn;
    }

    /// <summary>The native-callable wrapper of <see cref="ServerSwitchLaser"/>.</summary>
    [Uc("SBase.SWeaponAttaque.EventServerSwitchLaser", Sha1 = "7526bd3480ae5a35ac31fee3ee1ea6d0030c887c")]
    [PortOf("uc:SBase.SWeaponAttaque:EventServerSwitchLaser", Spec = "spec/equipment/spy-shocker.md#rules")]
    public void EventServerSwitchLaser(bool bOn) => ServerSwitchLaser(bOn);

    /// <summary>A sound the owner plays for everyone (reliable owner to server).</summary>
    [Uc("SBase.SWeaponAttaque.ServerSound", Sha1 = "13aa0f985072f99970e632f42e0155d6a482c652")]
    [PortOf("uc:SBase.SWeaponAttaque:ServerSound", Spec = "spec/equipment/spy-shocker.md#network")]
    public override void ServerSound(AssetRef MySound, ESoundSlot Slot = ESoundSlot.SLOT_None, int Radius = 0) =>
        base.ServerSound(MySound, Slot, Radius);

    /// <summary>The native-callable wrapper of <see cref="ServerSound"/>.</summary>
    [Uc("SBase.SWeaponAttaque.EventServerSound", Sha1 = "e8efa565fb88422b4a8153344e041072f6b96d87")]
    [PortOf("uc:SBase.SWeaponAttaque:EventServerSound", Spec = "spec/equipment/spy-shocker.md#rules")]
    public void EventServerSound(AssetRef MySound) => ServerSound(MySound, ESoundSlot.SLOT_None, (int)K.WeaponSoundRadius);

    /// <summary>One shot more for the clients' muzzle mesh (server).</summary>
    [Uc("SBase.SWeaponAttaque.AddMuzzleCount", Sha1 = "78ee3d3759628ead56bf33f2b0e1f9822afc1790")]
    [PortOf("uc:SBase.SWeaponAttaque:AddMuzzleCount", Spec = "spec/equipment/spy-shocker.md#rules")]
    public void AddMuzzleCount() => MuzzleCount += 1;

    /// <summary>
    /// The owner's shot (rule 9): the time, the pistol's placement to the server, the rumble. The pistol's world
    /// placement comes from the combat ports (the attached pistol at the hand).
    /// </summary>
    [Uc("SBase.SWeaponAttaque.FireWeapon", Sha1 = "8ba2094d72b53b4a4456884b6816cdc776b2052b")]
    [PortOf("uc:SBase.SWeaponAttaque:FireWeapon", Spec = "spec/equipment/spy-shocker.md#rules")]
    public void FireWeapon()
    {
        LastShot = Level.TimeSeconds;
        FCoords at = CombatServices.Ports(this).BoneCoords(this, SWeaponDefense.FxBone);
        FRotator rot = at.XAxis.Rotation();
        ServerFireWeaponWithLocation(at.Origin, rot.Yaw, rot.Pitch, rot.Roll);
        OwnerPC?.Vibration_Tir();
    }

    /// <summary>
    /// The server's shot (rule 10): the statistic, the trace from the owner's pistol placement, the shot sound at the
    /// left hand with the noise, and the muzzle count.
    /// </summary>
    [Uc("SBase.SWeaponAttaque.ServerFireWeaponWithLocation", Sha1 = "e89968ce1a1f8dab75d7d69343b9a4bc26f2bed7")]
    [PortOf("uc:SBase.SWeaponAttaque:ServerFireWeaponWithLocation", Spec = "spec/equipment/spy-shocker.md#rules")]
    public void ServerFireWeaponWithLocation(FVector Position, int Yaw, int Pitch, int Roll)
    {
        if (Role < ENetRole.ROLE_Authority && OwnerPC is { } client)
        {
            // The shot's number seeds its spread on both ends; with fire prediction the owner draws its impact now.
            int shot = NextShotId();
            bool predicted = FirePrediction.IsOn(this);
            if (predicted)
            {
                PredictTraceFireWithPos(shot, Position, new FRotator(Pitch, Yaw, Roll), DispersionActuelle);
            }

            CombatServices.Net(this).ServerCall(client, new CombatCall(CNAME_ServerFireWeaponWithLocation)
            {
                Target = this,
                Vector1 = Position,
                Rotation = new FRotator(Pitch, Yaw, Roll),
                Int1 = shot,
                Bool1 = predicted,
            });
            return;
        }

        if (OwnerPC is { } pc)
        {
            SPlayerController.IncStat(pc, static s => s.ShotsFired++);
        }

        TraceFireWithPos(Position, new FRotator(Pitch, Yaw, Roll), DispersionActuelle, 0f, 0f);
        if (Instigator is { } pawn)
        {
            pawn.PlaySound(FireSingleShotSound, ESoundSlot.SLOT_None, null, false, K.ShockerSoundRadius);
            CombatFx.Notify(pawn, BoneLeftHand);
            pawn.MakeNoise(1f);
        }

        AddMuzzleCount();
    }

    /// <summary>The ray start of a shot: 30 uu forward, 8 up and 4 left of the pistol (rule 11.1).</summary>
    [Uc("SBase.SWeaponAttaque.GetAxesAndLocation", Sha1 = "6566875906ab8ef7e44855e9770b0f06ff50eaf8")]
    [PortOf("uc:SBase.SWeaponAttaque:GetAxesAndLocation", Spec = "spec/equipment/spy-shocker.md#rules")]
    public static FVector GetAxesAndLocation(FVector Position, FRotator Orientation, out FVector X, out FVector Y, out FVector Z)
    {
        Orientation.GetAxes(out X, out Y, out Z);
        return Position + X * K.ShockerStartForward + Z * K.ShockerStartUp - Y * K.ShockerStartLeft;
    }

    /// <summary>
    /// The server trace (rule 11): world hits shock damageable objects and, through water, every pawn standing in it;
    /// other actors take the shocker damage; pawns get a zero-damage shocker hit with the pill (the shock rules
    /// decide what it does).
    /// </summary>
    [Uc("SBase.SWeaponAttaque.TraceFireWithPos", Sha1 = "23efb192317e86c7163951e303cfe46f4794a263")]
    [PortOf("uc:SBase.SWeaponAttaque:TraceFireWithPos", Spec = "spec/equipment/spy-shocker.md#rules")]
    [PortOf("uc:SBase.SWeaponAttaque:TraceFireWithPos", Spec = "spec/combat/hit-zones-and-weapons.md#rules")]
    public void TraceFireWithPos(FVector Position, FRotator Orientation, float Accuracy, float YOffset, float ZOffset)
    {
        if (Instigator is not { } shooter)
        {
            return;
        }

        FVector start = ShotRay(Position, Orientation, Accuracy, ActiveShotId, out FVector end);
        BoneTraceResult hit = CombatServices.HitZones(this).TraceBone(this, end, start, true);
        if (!hit.bHit)
        {
            return;
        }

        Actor other = hit.Actor;
        FVector impactAt = hit.HitLocation + hit.HitNormal * K.ShockerImpactOffset;
        FRotator impactRot = hit.HitNormal.Rotation();
        impactRot.Roll = SvmMath.Trunc(FRand() * K.RandomRoll);
        if (other.bWorldGeometry || other is Mover || other is LevelInfo)
        {
            if (other is SBasicGamePlayObject gpo)
            {
                gpo.eventTakeDamage(BaseDamage, shooter, hit.HitLocation, -hit.HitNormal * K.BulletMomentumScale, MyDamageType,
                    hit.PillTag);
            }

            if (hit.bIsAFakeBackdrop)
            {
                return;
            }

            if (IsMetal(hit.SurfaceType))
            {
                ImpactManager.AddImpact(this, ImpactShockerMetal, impactAt, impactRot, PredictedBy);
            }
            else if (hit.SurfaceType == ESurfaceType.SURFACE_MediumWater)
            {
                ImpactManager.AddImpact(this, ImpactShockerMetal, impactAt, impactRot, PredictedBy);
                if (other is not LevelInfo)
                {
                    ShockThroughWater(shooter, other, hit);
                }
            }
            else
            {
                ImpactManager.AddImpact(this, ImpactShocker, impactAt, impactRot, PredictedBy);
            }

            return;
        }

        if (other is not SBasePawn victim)
        {
            other.eventTakeDamage(BaseDamage, shooter, hit.HitLocation, -hit.HitNormal * K.BulletMomentumScale, MyDamageType,
                hit.PillTag);
            return;
        }

        victim.Controller?.PawnTakeDamage(0, shooter, hit.HitLocation, (hit.HitLocation - start) * K.BulletMomentumScale,
            MyDamageType, hit.PillTag);
        if (OwnerPC is { } pc && SGameInfo.TeamIndexOf(pc) != SGameInfo.TeamIndexOf(victim.Controller))
        {
            SPlayerController.IncStat(pc, static s => s.ShotsHit++);
        }

        ImpactManager.AddImpact(this, ImpactShocker, impactAt, impactRot, PredictedBy);
    }

    /// <summary>Every pawn whose 100 uu downward trace hits the struck water actor is shocked (rule 11.4).</summary>
    private void ShockThroughWater(Pawn shooter, Actor water, in BoneTraceResult hit)
    {
        if (TraceHitZones.CollisionOf(this) is not { } world)
        {
            return;
        }

        foreach (Pawn pawn in XLevel.DynamicActors<Pawn>())
        {
            FVector down = pawn.Location - new FVector(0f, 0f, K.WaterProbe);
            if (world.LineCheck(out FCheckResult probe, pawn, down, pawn.Location, FVector.Zero, ETraceFlags.TRACE_AllColliding) &&
                ReferenceEquals(probe.Actor, water))
            {
                pawn.Controller?.PawnTakeDamage(0, shooter, hit.HitLocation, default, MyDamageType, hit.PillTag);
            }
        }
    }

    /// <summary>
    /// The ray of one shot (rule 11.1 and 11.2, shared by the server's trace and the owner's prediction): the start
    /// ahead of the pistol and the spread around its orientation, with the shot's hashed numbers when
    /// <paramref name="ShotId"/> is set, else FRand.
    /// </summary>
    private FVector ShotRay(FVector Position, FRotator Orientation, float Accuracy, int ShotId, out FVector End)
    {
        FVector start = GetAxesAndLocation(Position, Orientation, out FVector X, out FVector Y, out FVector Z);
        float u1 = SpreadRandom(ShotId, Orientation.Yaw, ShotSpread.Spy, 0) - 0.5f;
        float u2 = SpreadRandom(ShotId, Orientation.Yaw, ShotSpread.Spy, 1) - 0.5f;
        End = start + Y * (Accuracy * u1) * K.SpreadScale + Z * (Accuracy * u2) * K.SpreadScale + X * ShootingRange;
        return start;
    }

    /// <summary>
    /// Fire prediction on the owner's client (spec/net/lag-compensation.md): the server's ray with the shot's hashed
    /// spread, traced here, and the shocker impact it would add (metal, water or plain; on a pawn too) drawn at once.
    /// Damage, the shock and the water conduction stay with the server, which does not send this impact back.
    /// </summary>
    [Provenance(ProvenanceKind.Estimated, "Our design of rl:net.fireprediction; the impact roll is hashed on the client, not the server's FRand")]
    private void PredictTraceFireWithPos(int ShotId, FVector Position, FRotator Orientation, float Accuracy)
    {
        if (Instigator is null)
        {
            return;
        }

        FVector start = ShotRay(Position, Orientation, Accuracy, ShotId, out FVector end);
        BoneTraceResult hit = CombatServices.HitZones(this).TraceBone(this, end, start, true);
        if (!hit.bHit)
        {
            return;
        }

        Actor other = hit.Actor;
        bool world = other.bWorldGeometry || other is Mover || other is LevelInfo;
        if ((world && hit.bIsAFakeBackdrop) || (!world && other is not SBasePawn))
        {
            return;
        }

        FRotator rot = hit.HitNormal.Rotation();
        rot.Roll = SvmMath.Trunc(ShotSpread.Unit(ShotId, Orientation.Yaw, ShotSpread.Spy, 2) * K.RandomRoll);
        Name type = world && (IsMetal(hit.SurfaceType) || hit.SurfaceType == ESurfaceType.SURFACE_MediumWater)
            ? ImpactShockerMetal
            : ImpactShocker;
        ImpactManager.AddImpactHere(XLevel, type, hit.HitLocation + hit.HitNormal * K.ShockerImpactOffset, rot);
    }

    private static bool IsMetal(ESurfaceType s) =>
        s is ESurfaceType.SURFACE_MetalHard or ESurfaceType.SURFACE_MetalReverb or ESurfaceType.SURFACE_MetalSheet or
            ESurfaceType.SURFACE_FenceMetal or ESurfaceType.SURFACE_SteamPipe or ESurfaceType.SURFACE_WaterPipe;

    /// <summary>The draw (rule 4): the right hand, the draw clip, stance 3, the first shot in 0.1 s.</summary>
    [Uc("SBase.SWeaponAttaque.AnimateDrawWeapon", Sha1 = "886c55bd3dea75ef1caf462bc5f63693c1c0fced")]
    [PortOf("uc:SBase.SWeaponAttaque:AnimateDrawWeapon", Spec = "spec/equipment/spy-shocker.md#rules")]
    public void AnimateDrawWeapon()
    {
        CombatFx.Notify(this, BoneRightHand);
        ViewAnim.Play(DEFAULTCHANNEL, ViewModelClips.DrawP190, K.DrawP190);
        if (Instigator is SBasePawn pawn)
        {
            PawnVision.SetWeaponLoaded(pawn, 3);
        }

        LastShot = Level.TimeSeconds + K.DrawShotDelay;
    }

    /// <summary>The holster (rule 5): the draw clip backward, stance 4.</summary>
    [Uc("SBase.SWeaponAttaque.AnimateUnDrawWeapon", Sha1 = "63aae141a27bbda956b387844e8ab3af23d1ab3a")]
    [PortOf("uc:SBase.SWeaponAttaque:AnimateUnDrawWeapon", Spec = "spec/equipment/spy-shocker.md#rules")]
    public void AnimateUnDrawWeapon()
    {
        ViewAnim.Play(DEFAULTCHANNEL, ViewModelClips.DrawP190, K.DrawP190, 1f, true);
        if (Instigator is SBasePawn pawn)
        {
            PawnVision.SetWeaponLoaded(pawn, 4);
        }
    }

    /// <summary>The quick draw of traversal: stance 1 with the idle clip.</summary>
    [Uc("SBase.SWeaponAttaque.AnimateQuickDrawWeapon", Sha1 = "8920e407e909c814b7f004050f422d480a4a7196")]
    [PortOf("uc:SBase.SWeaponAttaque:AnimateQuickDrawWeapon", Spec = "spec/equipment/spy-shocker.md#rules")]
    public void AnimateQuickDrawWeapon()
    {
        ViewAnim.Loop(DEFAULTCHANNEL, ViewModelClips.WaitP190);
        if (Instigator is SBasePawn pawn)
        {
            PawnVision.SetWeaponLoaded(pawn, 1);
        }
    }

    /// <summary>The quick holster of traversal: stance 0 with the idle clip.</summary>
    [Uc("SBase.SWeaponAttaque.AnimateQuickUnDrawWeapon", Sha1 = "2efc0f35535ec38fd7fe570a7aea740ea63954bb")]
    [PortOf("uc:SBase.SWeaponAttaque:AnimateQuickUnDrawWeapon", Spec = "spec/equipment/spy-shocker.md#rules")]
    public void AnimateQuickUnDrawWeapon()
    {
        ViewAnim.Loop(DEFAULTCHANNEL, ViewModelClips.WaitP190);
        if (Instigator is SBasePawn pawn)
        {
            PawnVision.SetWeaponLoaded(pawn, 0);
        }
    }

    /// <summary>The other-hand position: stance 7 and 2 swap (rule 12).</summary>
    [Uc("SBase.SWeaponAttaque.ChangeHandWeapon", Sha1 = "d1b4ec0fdd09462944e6f43f748455b09bd3fc62")]
    [PortOf("uc:SBase.SWeaponAttaque:ChangeHandWeapon", Spec = "spec/equipment/spy-shocker.md#rules")]
    public void ChangeHandWeapon()
    {
        if (Instigator is SBasePawn pawn)
        {
            pawn.WeaponLoaded = (byte)(pawn.WeaponLoaded == 7 ? 2 : 7);
        }
    }

    /// <summary>
    /// The draw clip ends (rule 4): after a draw the aiming clip and stance 2, after a holster the idle clip and
    /// stance 0; every end allows the next shot at once.
    /// </summary>
    [Uc("SBase.SWeaponAttaque.AnimEnd", Sha1 = "611e21b41bff6d83a213b26035f6bc2ad372498c")]
    [PortOf("uc:SBase.SWeaponAttaque:AnimEnd", Spec = "spec/equipment/spy-shocker.md#rules")]
    protected override void AnimEnd(int Channel)
    {
        if (Channel == DEFAULTCHANNEL && Instigator is SBasePawn pawn)
        {
            if (pawn.WeaponLoaded == 3)
            {
                ViewAnim.Loop(DEFAULTCHANNEL, ViewModelClips.SnipeP190);
                PawnVision.SetWeaponLoaded(pawn, 2);
            }
            else if (pawn.WeaponLoaded == 4)
            {
                ViewAnim.Loop(DEFAULTCHANNEL, ViewModelClips.WaitP190);
                PawnVision.SetWeaponLoaded(pawn, 0);
            }
        }

        LastShot = Level.TimeSeconds - RateOfFire;
    }

    /// <summary>The server's laser gate (rule 6) and the beam drawing elsewhere (a view effect).</summary>
    [Uc("SBase.SWeaponAttaque.Tick", Sha1 = "27a6d68e3eb2fc679719f6916944bfae54e52b0b")]
    [PortOf("uc:SBase.SWeaponAttaque:Tick", Spec = "spec/equipment/spy-shocker.md#rules")]
    protected override void Tick(float DeltaTime)
    {
        if (Role == ENetRole.ROLE_Authority)
        {
            bool allowed = Controller is SPlayerController pc && CombatStateEvents.CanActivateLaser(pc);
            bLaserOn = bLaserShouldBeOn && allowed;
        }

        TickSWeaponAttaque(DeltaTime);
    }

    /// <summary>s_Selected (auto): the held trigger repeats once RateOfFire has passed (rule 7).</summary>
    [Uc("SBase.SWeaponAttaque.s_Selected")]
    public class s_Selected : UcActorState
    {
        public static readonly s_Selected Instance = new(UcMeta.s_Selected, null);

        protected s_Selected(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SWeaponAttaque.s_Selected.Tick", Sha1 = "8dcb462d0c38cb8303b800ff720fb499671933d1")]
        [PortOf("uc:SBase.SWeaponAttaque:s_Selected.Tick", Spec = "spec/equipment/spy-shocker.md#rules")]
        [Provenance(ProvenanceKind.Estimated, "s_Selected.Tick runs the global laser gate before the held-fire repeat")]
        public override void Tick(Actor self, float DeltaTime)
        {
            var w = (SWeaponAttaque)self;
            w.Tick(DeltaTime);
            if (w.OwnerPC is not { } pc || w.Instigator is not SBasePawn pawn)
            {
                return;
            }

            float now = w.Level.TimeSeconds;
            bool notShocked = !pawn.bStickyShocking || now > pc.iTimeToStopStickyShock;
            if (pc.bFire != 0 && now - w.LastShot > w.RateOfFire && notShocked && !pawn.bWeaponTransit)
            {
                w.eventFire(0f);
            }
        }
    }
}
