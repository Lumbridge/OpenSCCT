using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Combat;
using OpenScct.Game.Equipment;
using OpenScct.Game.Movement;
using OpenScct.Provenance;
using K = OpenScct.Game.Equipment.K;

namespace OpenScct.Game.SBase;

/// <summary>
/// SBase.SStickyCam: a spy's camera stuck on a wall (spec/equipment/sticky-cam.md rules 7, 13 to 21). The network
/// cameras (SCamNetwork) inherit it; with no owner pawn its owner-side reactions do nothing for them.
/// </summary>
public partial class SStickyCam
{
    private static readonly Name CNAME_SpawnGaz = new("SpawnGaz");

    private SStickyCam? _camDefaults;

    /// <summary>
    /// Reloaded's class defaults (rl:fix.stickycam_east_sound): the replicated angles start at 12345678 instead of 0,
    /// so the first bunch to the other players always carries them, a cam facing east (yaw 0) or level (pitch 0)
    /// included. A value equal to the class default is left out of the first bunch (docs/NETCODE.md), so without this
    /// such a cam reached the others without its replicated angles (they then had only the cam's rotation, which
    /// PostNetBeginPlay copies). Only SStickyCam's own defaults change, as in Reloaded: the
    /// network cameras that extend it keep 0.
    /// </summary>
    [PortOf("rl:fix.stickycam_east_sound", Spec = "spec/equipment/sticky-cam.md#open-questions")]
    [Provenance(ProvenanceKind.Decoded, "Reloaded.Core 0x1000f790: SStickyCam default iReplicatedPitch and iReplicatedYaw = 12345678 at start-up")]
    public SStickyCam()
    {
        if (GetType() == typeof(SStickyCam))
        {
            iReplicatedPitch = K.ReloadedStickyCamAngleDefault;
            iReplicatedYaw = K.ReloadedStickyCamAngleDefault;
        }
    }

    /// <summary>The class default object (UnrealScript <c>default.X</c>).</summary>
    protected SStickyCam CamDefaults => _camDefaults ??= GetUcClass().Create() as SStickyCam ?? this;

    /// <summary>The owner pawn's player, or null (a network camera has none).</summary>
    public SPlayerController? OwnerController => (Owner as Pawn)?.Controller as SPlayerController;

    /// <summary>
    /// On the server, the interaction goes; on the owner's machine a used object pointing at this cam is cleared.
    /// The orientable and gameplay object versions do not run (the original does not call them).
    /// </summary>
    [Uc("SBase.SStickyCam.Destroyed", Sha1 = "2c01ff4228d8c56b80b575e5731522d247e83393")]
    [PortOf("uc:SBase.SStickyCam:Destroyed", Spec = "spec/equipment/sticky-cam.md#the-cam-actor")]
    protected override void Destroyed()
    {
        if (Role == ENetRole.ROLE_Authority && InteractionObj is not null)
        {
            InteractionObj.Destroy();
        }

        if (OwnerController is { } pc && pc.IAmOnClient() && pc.SBPawn is { } pawn && ReferenceEquals(pawn.UsedObject, this))
        {
            pawn.UsedObject = null;
        }
    }

    /// <summary>
    /// On the local player's machine the cam is drawn small while that player looks through it, then the orientable
    /// tick.
    /// </summary>
    [Uc("SBase.SStickyCam.Tick", Sha1 = "cd22a066f5a5711c86ed942041857b2a4849d920")]
    [PortOf("uc:SBase.SStickyCam:Tick", Spec = "spec/equipment/sticky-cam.md#the-cam-actor")]
    protected override void Tick(float DeltaTime)
    {
        if (Level.GetLocalPlayerController() is SPlayerController local && local.IAmOnClient())
        {
            SetSelfViewScale(GadgetStates.InStickyCamViewState(local) && ReferenceEquals(local.SBPawn?.UsedObject, this));
        }

        base.Tick(DeltaTime);
    }

    /// <summary>A moving mover touching the cam destroys it (server); the owner loses it with its doses.</summary>
    [Uc("SBase.SStickyCam.Touch", Sha1 = "3f9f6d9944e83392207dd3faad7d03acdc6c31e2")]
    [PortOf("uc:SBase.SStickyCam:Touch", Spec = "spec/equipment/sticky-cam.md#the-cam-actor")]
    protected override void Touch(Actor Other)
    {
        if (Role == ENetRole.ROLE_Authority && Other is Mover && !Other.Velocity.IsZero())
        {
            OwnerController?.StickyCamWasDestroyed(null, Smoke_NbDoses);
            Destroy();
        }
    }

    /// <summary>On the server the owner enters the cam view at once (unless falling); the optional dose refill timer.</summary>
    [Uc("SBase.SStickyCam.PreBeginPlay", Sha1 = "b76632f7d0548b774e9ea84b855dad7395dfe6f4")]
    [PortOf("uc:SBase.SStickyCam:PreBeginPlay", Spec = "spec/equipment/sticky-cam.md#firing-a-cam")]
    protected override void PreBeginPlay()
    {
        base.PreBeginPlay();
        if (Role == ENetRole.ROLE_Authority && Owner is Pawn { Physics: not EPhysics.PHYS_Falling } owner &&
            owner.Controller is SPlayerController pc)
        {
            pc.eventUseStickyCam(this);
        }

        if (Smoke_RefillRate != 0f)
        {
            SetTimer(Smoke_RefillRate, true);
        }
    }

    /// <summary>A client's own cam arriving while its predicted cam lives does not play the activation sound again.</summary>
    [Uc("SBase.SStickyCam.PostNetBeginPlay", Sha1 = "b8b4a8c62f5b06dd39f0dd40fbd5d148a88b444f")]
    [PortOf("uc:SBase.SStickyCam:PostNetBeginPlay", Spec = "spec/equipment/sticky-cam.md#firing-a-cam")]
    protected override void PostNetBeginPlay()
    {
        if (Role != ENetRole.ROLE_Authority && Level.GetLocalPlayerController() is SPlayerController pc &&
            ReferenceEquals(Owner, pc.SBPawn) && Level.TimeSeconds < pc.PredictedStickyCamDeadline)
        {
            ActivationSound = AssetRef.None;
        }

        base.PostNetBeginPlay();
    }

    /// <summary>The cam's collision is 20 x 20.</summary>
    [Uc("SBase.SStickyCam.PostBeginPlay", Sha1 = "e33e448f7b4c2ac7bb52d3ec697ea697263d6bc8")]
    [PortOf("uc:SBase.SStickyCam:PostBeginPlay", Spec = "spec/equipment/sticky-cam.md#firing-a-cam")]
    protected override void PostBeginPlay()
    {
        MovementNatives.SetCollisionSize(this, K.StickyCamCollision, K.StickyCamCollision);
        base.PostBeginPlay();
    }

    /// <summary>
    /// The dose refill timer: the original's post-increment inside Clamp leaves the doses unchanged, so this only
    /// keeps them within [0, default].
    /// </summary>
    [Uc("SBase.SStickyCam.Timer", Sha1 = "30a187e9a212f217394ceacac5112289bb47e315")]
    [PortOf("uc:SBase.SStickyCam:Timer", Spec = "spec/equipment/sticky-cam.md#the-cam-actor")]
    protected override void Timer() =>
        Smoke_NbDoses = SvmMath.Clamp(Smoke_NbDoses, 0, CamDefaults.Smoke_NbDoses);

    /// <summary>
    /// SpawnGaz (client to server): with a dose and the reuse delay over, one dose of gas in front of the cam, its
    /// sound and the gasser's stat.
    /// </summary>
    [Uc("SBase.SStickyCam.SpawnGaz", Sha1 = "c092332db4a2ac163b75dd40cafab63aa865002c")]
    [PortOf("uc:SBase.SStickyCam:SpawnGaz", Spec = "spec/equipment/sticky-cam.md#gas")]
    [PortOf("rep:SBase.SStickyCam.SpawnGaz", Spec = "spec/equipment/sticky-cam.md#network")]
    public void SpawnGaz()
    {
        if (GadgetNet.ToServer(this, OwnerController, new CombatCall(CNAME_SpawnGaz) { Target = this }))
        {
            return;
        }

        if (Smoke_NbDoses <= 0 || Level.TimeSeconds < NextUseTime)
        {
            return;
        }

        NextUseTime = SvmMath.Trunc(Level.TimeSeconds + Smoke_ReusabilityPeriod);
        Smoke_NbDoses--;
        XLevel.SpawnActor(new SStickyCamEmitter(), Location, Rotation);
        PlaySound(SpawnGasSound, ESoundSlot.SLOT_None, 1f, false, K.StickyGasSoundRadius);
        GadgetFx.Notify(this, GadgetFx.StickyCamGas);
        if (OwnerController?.PlayerReplicationInfo?.Stats is { } stats)
        {
            stats.Spy_StickyCamUsed++;
        }
    }

    /// <summary>
    /// Damage: ignored when the owner hits his own cam while looking through it; on the server the owner loses the
    /// cam (a pick-up, marked by the hit location (-1, -2, -3), reloads the picker with its doses); then it is gone.
    /// </summary>
    [Uc("SBase.SStickyCam.TakeDamage", Sha1 = "6acbf582ad1ebdb88ec3ac6cb7963568ee11e226")]
    [PortOf("uc:SBase.SStickyCam:TakeDamage", Spec = "spec/equipment/sticky-cam.md#the-cam-actor")]
    protected override void TakeDamage(int Damage, Pawn? EventInstigator, FVector HitLocation, FVector Momentum,
        SDamageType DamageType, int PillTag = 0)
    {
        SPlayerController? owner = OwnerController;
        if (Role == ENetRole.ROLE_Authority && owner is not null && ReferenceEquals(Owner, EventInstigator) &&
            (owner.IsInState(GadgetStates.s_UseObject_StickyCam) || owner.IsInState(GadgetStates.s_UseObject_StickyCam_SmoothToPosition)))
        {
            return;
        }

        if (Role == ENetRole.ROLE_Authority)
        {
            if (HitLocation == K.StickyPickupMarker)
            {
                owner?.StickyCamWasDestroyed(EventInstigator?.Controller as SPlayerController, Smoke_NbDoses);
            }
            else
            {
                owner?.StickyCamWasDestroyed(null, CamDefaults.Smoke_NbDoses);
            }
        }

        Destroy();
    }

    /// <summary>An encroaching mover destroys the cam (server); the owner loses it with its doses.</summary>
    [Uc("SBase.SStickyCam.EncroachedBy", Sha1 = "a081f45f08a7f04977e195e587b4121689a7a0c7")]
    [PortOf("uc:SBase.SStickyCam:EncroachedBy", Spec = "spec/equipment/sticky-cam.md#the-cam-actor")]
    public override void EncroachedBy(Actor? Other)
    {
        if (Role == ENetRole.ROLE_Authority)
        {
            OwnerController?.StickyCamWasDestroyed(null, Smoke_NbDoses);
            Destroy();
        }
    }
}
