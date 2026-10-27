using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Devices;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

/// <summary>
/// SBase.SBasicGamePlayObject: the base of interactive props (switches, stations, gameplay objects, cameras),
/// written from spec/devices/gameplay-object.md. It spawns the prop's interaction, keeps hit points for the classes
/// that do not override TakeDamage, and handles the chaff lock (state s_LockedByChaff).
/// </summary>
public partial class SBasicGamePlayObject
{
    public static new readonly StateTable ClassStates = new(Actor.ClassStates, s_LockedByChaff.Instance);

    public override StateTable GetStateTable() => ClassStates;

    /// <summary>Copies the description to the interaction, or retries by timer until the interaction exists.</summary>
    [Uc("SBase.SBasicGamePlayObject.PreBeginPlay", Sha1 = "e2db39d0843852b0879269c2fec1e8c203d1e6cb")]
    [PortOf("uc:SBase.SBasicGamePlayObject:PreBeginPlay", Spec = "spec/devices/gameplay-object.md#start-up")]
    protected override void PreBeginPlay()
    {
        if (string.IsNullOrEmpty(sDescriptionText))
        {
            return;
        }

        if (InteractionObj is not null)
        {
            InteractionObj.Description = sDescriptionText;
        }
        else
        {
            SetTimer(1f + 2f * FRand(), false);
        }
    }

    /// <summary>
    /// Spawns the interaction at the object's pose (a hidden object becomes an empty visible mesh), applies the
    /// initial usability, and gives a damageable object at least one hit point.
    /// </summary>
    [Uc("SBase.SBasicGamePlayObject.PostBeginPlay", Sha1 = "dd090dc167ae48b1598eb37b05dfa61d4b916b8e")]
    [PortOf("uc:SBase.SBasicGamePlayObject:PostBeginPlay", Spec = "spec/devices/gameplay-object.md#start-up")]
    protected override void PostBeginPlay()
    {
        // The script function is not simulated, so a client never runs this (net-replication.md rule 19.2). A client
        // copy of the interaction would join that client's Use menu, and picking it sends the server an object it
        // does not know: the menu then hangs. Clients get the server's interaction by replication (InteractionObj).
        if (Role < ENetRole.ROLE_Authority)
        {
            base.PostBeginPlay();
            return;
        }

        if (!InteractionClass.IsNone)
        {
            if (bHidden)
            {
                bHidden = false;
                SetDrawType(EDrawType.DT_StaticMesh);
                SetStaticMesh(AssetRef.None);
            }

            InteractionObj = DeviceClasses.Spawn(this, InteractionClass, this, Name.None, Location, Rotation) as SInteractObject;
            if (InteractionObj is not null)
            {
                if (iPriority != -1)
                {
                    InteractionObj.iPriority = iPriority;
                }

                InteractionObj.bInteractInFront = bInteractInFront;
            }
        }

        // The original also touches InteractionObj when there is none (an access warning only).
        if (!bInitialyUsable && InteractionObj is not null)
        {
            InteractionObj.bUsable = false;
        }

        if (bDamageable && HitPoints == 0)
        {
            HitPoints = 1;
        }

        base.PostBeginPlay();
    }

    /// <summary>Retries the description copy: 3 s later while there is no interaction, and every 3 s on clients.</summary>
    [Uc("SBase.SBasicGamePlayObject.Timer", Sha1 = "eb03fb225ff3f7beee1fb005854699fda7a8158d")]
    [PortOf("uc:SBase.SBasicGamePlayObject:Timer", Spec = "spec/devices/gameplay-object.md#start-up")]
    protected override void Timer()
    {
        if (string.IsNullOrEmpty(sDescriptionText))
        {
            return;
        }

        if (InteractionObj is not null)
        {
            InteractionObj.Description = sDescriptionText;
            if (Role != ENetRole.ROLE_Authority)
            {
                SetTimer(DeviceK.DescriptionRetry, false);
            }
        }
        else
        {
            SetTimer(DeviceK.DescriptionRetry, false);
        }
    }

    /// <summary>The authority destroys the interaction with the object.</summary>
    [Uc("SBase.SBasicGamePlayObject.Destroyed", Sha1 = "796d9cf36d22107a3b0cd1d866cdc7727509898f")]
    [PortOf("uc:SBase.SBasicGamePlayObject:Destroyed", Spec = "spec/devices/gameplay-object.md#effects-and-helpers")]
    protected override void Destroyed()
    {
        if (Role == ENetRole.ROLE_Authority && InteractionObj is not null)
        {
            InteractionObj.Destroy();
        }

        base.Destroyed();
    }

    /// <summary>Sticky cameras and mines may attach to the object.</summary>
    [Uc("SBase.SBasicGamePlayObject.AcceptStickableGPO", Sha1 = "a40381ccfdf342092554b33966344721f68fd6c2")]
    [PortOf("uc:SBase.SBasicGamePlayObject:AcceptStickableGPO", Spec = "spec/devices/gameplay-object.md#effects-and-helpers")]
    public virtual bool AcceptStickableGPO() => true;

    /// <summary>For priority 5 and above, every pawn's controller shows a malfunction marker at the object.</summary>
    [Uc("SBase.SBasicGamePlayObject.AddOnHud", Sha1 = "787a842655aff916a513a6384a3e267265fb12b7")]
    [PortOf("uc:SBase.SBasicGamePlayObject:AddOnHud", Spec = "spec/devices/gameplay-object.md#effects-and-helpers")]
    public virtual void AddOnHud()
    {
        if (iPriority < DeviceK.HudPriority || XLevel.GetService<IDeviceHooks>() is not { } hooks)
        {
            return;
        }

        foreach (SBasePawn p in XLevel.AllActors<SBasePawn>())
        {
            if (p.Controller is SPlayerController pc)
            {
                hooks.AddGPOOnHud(pc, Location);
            }
        }
    }

    /// <summary>Every pawn's controller removes the marker at the object.</summary>
    [Uc("SBase.SBasicGamePlayObject.RemoveFromHud", Sha1 = "92dd4cbd7b85e9e2a7ae4fd52b0a83bbe4ba625f")]
    [PortOf("uc:SBase.SBasicGamePlayObject:RemoveFromHud", Spec = "spec/devices/gameplay-object.md#effects-and-helpers")]
    public virtual void RemoveFromHud()
    {
        if (XLevel.GetService<IDeviceHooks>() is not { } hooks)
        {
            return;
        }

        foreach (SBasePawn p in XLevel.AllActors<SBasePawn>())
        {
            if (p.Controller is SPlayerController pc)
            {
                hooks.RemoveGPOFromHud(pc, Location);
            }
        }
    }

    /// <summary>
    /// Locks a chaffable object for <paramref name="Length"/> seconds: remembers its state once, enters
    /// s_LockedByChaff and passes the lock on to its control relay.
    /// </summary>
    [Uc("SBase.SBasicGamePlayObject.SetLockedByChaff", Sha1 = "a20c73d371ccb8f54efa8177b781e657f62ae580")]
    [PortOf("uc:SBase.SBasicGamePlayObject:SetLockedByChaff", Spec = "spec/devices/gameplay-object.md#chaff-lock")]
    public virtual void SetLockedByChaff(float Length)
    {
        if (!bCanBeChaffed)
        {
            return;
        }

        SetTimer(Length, false);
        if (ChaffLockPreviousState.IsNone)
        {
            ChaffLockPreviousState = GetStateName();
        }

        GotoState(nameof(s_LockedByChaff));
        ERControl?.LockChaff(Length);
    }

    /// <summary>Dispatches SetLockedByChaff through the current state.</summary>
    public void eventSetLockedByChaff(float Length)
    {
        if (StateNode is ISetLockedByChaff s)
        {
            s.SetLockedByChaff(this, Length);
        }
        else
        {
            SetLockedByChaff(Length);
        }
    }

    /// <summary>Tells the merc team about the malfunction (message 21 of SGameMessages).</summary>
    [Uc("SBase.SBasicGamePlayObject.Malfunction", Sha1 = "3349f2e10a30174c6433b42b263c002be63548d5")]
    [PortOf("uc:SBase.SBasicGamePlayObject:Malfunction", Spec = "spec/devices/gameplay-object.md#chaff-lock")]
    public virtual void Malfunction() =>
        XLevel.GetService<IDeviceHooks>()?.BroadcastTeamLocalizedMessage(DeviceK.MercTeam, DeviceK.GameMessages, DeviceK.MalfunctionMessage, ERControl);

    /// <summary>
    /// Hit points for classes that do not override it: ignored unless damageable (or a sticky shot on a
    /// sticky-as-chaff object), the sticky lock, then at 0 hit points the destroy sound and Destroy (which a
    /// bNoDelete object survives, staying at 0).
    /// </summary>
    [Uc("SBase.SBasicGamePlayObject.TakeDamage", Sha1 = "0e047980c60aea43984616675488dfb798427074")]
    [PortOf("uc:SBase.SBasicGamePlayObject:TakeDamage", Spec = "spec/devices/gameplay-object.md#damage")]
    protected override void TakeDamage(int Damage, Pawn? EventInstigator, FVector HitLocation, FVector Momentum,
        SDamageType DamageType, int PillTag = 0)
    {
        bool stickyLock = bStickyDamageIsLikeChaff && DamageType == SDamageType.SDTStickyShocker;
        if (!bDamageable && !stickyLock)
        {
            return;
        }

        PlaySound(TakeDamageSound, ESoundSlot.SLOT_Misc, 1f, false, DeviceK.DamageSoundRadius, 1f);
        if (stickyLock)
        {
            DeviceClasses.Spawn(this, DeviceK.SparkShockerClass, this, Name.None, HitLocation, Rotation);
            eventSetLockedByChaff(StickyNeutralizationTime);
            return;
        }

        HitPoints = SvmMath.Max(HitPoints - Damage, 0);
        if (HitPoints <= 0)
        {
            PlaySound(DestroySound, ESoundSlot.SLOT_Misc, 1f, false, DeviceK.DamageSoundRadius, 1f);
            Destroy();
        }
    }

    protected override void CleanupDestroyedReferences()
    {
        base.CleanupDestroyedReferences();
        InteractionObj = Live(InteractionObj);
    }

    /// <summary>A state's version of <see cref="SetLockedByChaff"/>.</summary>
    public interface ISetLockedByChaff
    {
        void SetLockedByChaff(SBasicGamePlayObject self, float Length);
    }
}
