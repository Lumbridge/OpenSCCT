using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Objectives;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// The Story disk ("Secret Files"): taken from its rack, carried, dropped, returned and delivered. Written from
// spec/objectives/disk-flag.md.
public partial class SFlag
{
    /// <summary>
    /// Takes the flag out of play: a carrier drops it (LeaveCarrier), it hides and is no longer pickable, then the
    /// trigger version.
    /// </summary>
    [UcNative("ASFlag::execDesactivate", Exe = 0x10AE9C60)]
    [PortOf("nat:SBase.SFlag.Desactivate", Spec = "spec/objectives/disk-flag.md#rules")]
    [Provenance(ProvenanceKind.Decoded, "game 0x10AE9C80 (vtable +0x138): FlagCarrier ? LeaveCarrier; bHidden = true; with a PickupInteraction bPickable = false and its bCanPickup = false; ASObjectiveTrigger::Desactivate")]
    public override void Desactivate()
    {
        if (FlagCarrier is not null)
        {
            LeaveCarrier();
        }

        bHidden = true;
        if (PickupInteraction is { } pickup)
        {
            bPickable = false;
            pickup.bCanPickup = false;
        }

        DesactivateTrigger();
    }

    [Provenance(ProvenanceKind.Decoded, "disk-flag.md rule 1: the disk's own mesh, set in PostBeginPlay")]
    public static readonly AssetRef DiskMesh = new("SGameplayObjects.Extraction_SM.HardDisk_T");

    public static new readonly StateTable ClassStates = new(SObjectiveTrigger.ClassStates,
        s_Initial.Instance, s_Inactive.Instance, s_TemporaryInactive.Instance);

    public override StateTable GetStateTable() => ClassStates;

    protected override void CleanupDestroyedReferences()
    {
        base.CleanupDestroyedReferences();
        FlagCarrier = Live(FlagCarrier);
        Server = Live(Server);
        DefDropZone = Live(DefDropZone);
        CarrierKiller = Live(CarrierKiller);
        for (int k = 0; k < DropZone.Length; k++)
        {
            DropZone[k] = Live(DropZone[k]);
        }
    }

    // ---- setup (server) ---------------------------------------------------------------------------------------

    [Uc("SBase.SFlag.PostBeginPlay", Sha1 = "4f7f8158b940b1232042835c6e3030d46d6d68b3")]
    [PortOf("uc:SBase.SFlag:PostBeginPlay", Spec = "spec/objectives/disk-flag.md#rules")]
    protected override void PostBeginPlay()
    {
        Server = ObjectiveNatives.Spawn<SBasicGamePlayObject>(this, this, Location, Rotation);
        if (Server is not null)
        {
            Server.SetStaticMesh(StaticMesh);
            Server.SetDrawType(EDrawType.DT_StaticMesh);
            Server.bHidden = false;
            Server.RemoteRole = ENetRole.ROLE_SimulatedProxy;
            Server.HeatIntensity = HeatIntensity;
            Server.SetCollision(true, true, true);
        }

        SetStaticMesh(DiskMesh);
        InitialLocation = Location;
        InitialRotation = Rotation;
        DefDropZone = ObjectiveNatives.Spawn<SFlagDropZone>(this, null, Location, Rotation);
        bShouldLock = false;
        base.PostBeginPlay();
        if (PickupInteraction is not null)
        {
            PickupInteraction.bInteractInFront = true;
        }

        SetCollision(false, false, false);
        bResetZeroValues = false;
    }

    [Uc("SBase.SFlag.Destroyed", Sha1 = "cfdd32888fd395d862d30f127dd9ab8fba5bbc9a")]
    [PortOf("uc:SBase.SFlag:Destroyed", Spec = "spec/objectives/disk-flag.md#rules")]
    protected override void Destroyed()
    {
        Server?.Destroy();
        base.Destroyed();
    }

    [Uc("SBase.SFlag.Prepare", Sha1 = "f1e69d42e8d136f05406dc261d187f1e47bdb24a")]
    [PortOf("uc:SBase.SFlag:Prepare", Spec = "spec/objectives/disk-flag.md#rules")]
    public override void Prepare() => bHidden = false;

    /// <summary>
    /// Sends the disk home (rule 6): back on its rack, touchable, takeable through the menu again, unless a lock is
    /// pending. No message and no drop zone change.
    /// </summary>
    [Uc("SBase.SFlag.Reset", Sha1 = "09c18f14524a04df65abc980d125f2a226b8d11d")]
    [PortOf("uc:SBase.SFlag:Reset", Spec = "spec/objectives/disk-flag.md#rules")]
    public override void Reset()
    {
        base.Reset();
        CarrierKiller = null;
        bPickable = true;
        if (PickupInteraction is not null)
        {
            PickupInteraction.bCanPickup = true;
        }

        SetCollision(false, false, false);
        bCollideWorld = false;
        SetLocation(InitialLocation);
        ObjectiveNatives.SetRotation(this, InitialRotation);
        DesiredRotation = InitialRotation;
        bCollideWorld = true;
        bHidden = false;
        bTriggering = false;
        SetCollision(true, false, false);
        if (PickupInteraction is { } pickup)
        {
            pickup.SetBase(null);
            pickup.SetLocation(Location);
            ObjectiveNatives.SetRotation(pickup, Rotation);
            pickup.SetBase(this);
        }

        if (bShouldLock)
        {
            bPickable = false;
            if (PickupInteraction is not null)
            {
                PickupInteraction.bCanPickup = false;
            }

            GotoState(K.s_TemporaryInactive);
        }
        else
        {
            GotoState(K.s_Initial);
        }
    }

    // ---- carry ------------------------------------------------------------------------------------------------

    /// <summary>
    /// A player takes the disk (rule 12): the carrier is recorded and told where to go, everyone hears it was
    /// stolen, the listed drop zones open for a spy, and the mercs are told to get it back.
    /// </summary>
    [Uc("SBase.SFlag.NotifyPickup", Sha1 = "e3bc10d776996d2388950ad99cea695fa38f4d6b")]
    [PortOf("uc:SBase.SFlag:NotifyPickup", Spec = "spec/objectives/disk-flag.md#rules")]
    protected internal override bool NotifyPickup(Controller Instigator)
    {
        base.NotifyPickup(Instigator);
        CarrierKiller = null;
        bActive = false;
        FlagCarrier = Instigator.Pawn;
        FlagTaken = true;
        SetFlagCarrier(true);
        bResetZeroValues = false;
        bool spy = Instigator.Pawn is SBasePawn { SPawnType: PawnType.SPAWNOFFENSE };
        if (spy)
        {
            ObjectiveMessages.Broadcast(this, K.MsgDataStolen, this);
            if (Instigator is PlayerController pc)
            {
                ObjectiveMessages.Send(pc, K.MsgGoToDropZone);
            }

            bTriggering = true;
            foreach (Actor? a in DropZone)
            {
                if (a is SFlagDropZone zone)
                {
                    zone.bTriggering = true;
                    if (zone.InteractionObj is not null)
                    {
                        zone.InteractionObj.bUsable = true;
                    }

                    zone.Open();
                }
            }
        }

        ObjectiveMessages.BroadcastTeam(this, K.TeamMercs, K.MsgRetrieveHardDrive);
        if (!spy && Instigator.Pawn is { } carrier)
        {
            ObjectiveNatives.AttachToBone(carrier, this, K.BoneThigh);
            RelativeLocation = K.ThighOffset;
            RelativeRotation = K.ThighRotationTouch;
        }

        return false;
    }

    /// <summary>
    /// The carrier lets go (rule 17): the disk falls from his hand, nobody carries it, and the zones no carried disk
    /// lists close their entries (their lids stay open).
    /// </summary>
    [Uc("SBase.SFlag.Throw", Sha1 = "b43920bc270aa553dd0912d5689870e96f768f23")]
    [PortOf("uc:SBase.SFlag:Throw", Spec = "spec/objectives/disk-flag.md#rules")]
    protected internal override void Throw(Controller? Thrower, FVector ThrowVelocity)
    {
        base.Throw(FlagCarrier?.Controller, ThrowVelocity);
        bHidden = false;
        FlagCarrier = null;
        FlagTaken = false;
        SetFlagCarrier(false);
        bTriggering = false;
        bResetZeroValues = true;
        foreach (Actor? a in DropZone)
        {
            if (a is not SFlagDropZone zone)
            {
                continue;
            }

            if (!zone.IsWaitingAFlag())
            {
                zone.bTriggering = false;
                if (zone.InteractionObj is not null)
                {
                    zone.InteractionObj.bUsable = false;
                }
            }
            else
            {
                zone.Open();
            }
        }
    }

    /// <summary>The disk came to rest (rule 19): it can be touched, never taken through the menu, until it goes home.</summary>
    [Uc("SBase.SFlag.StoppedMoving", Sha1 = "0f18bd754eb092b724af43ad949d947324c70f81")]
    [PortOf("uc:SBase.SFlag:StoppedMoving", Spec = "spec/objectives/disk-flag.md#rules")]
    protected internal override void StoppedMoving()
    {
        if (Objective is not { ObjectiveTaken: true })
        {
            bActive = true;
        }

        bPickable = false;
        base.StoppedMoving();
        GotoState(K.s_Initial);
    }

    /// <summary>Multicast (flag 0x01000000): every machine learns whether the disk is carried.</summary>
    [Uc("SBase.SFlag.SetFlagCarrier", Sha1 = "24ca4e1b90e8a862b051858b7f0eed06f9c895b3")]
    [PortOf("uc:SBase.SFlag:SetFlagCarrier", Spec = "spec/objectives/disk-flag.md#rules")]
    [PortOf("rep:SBase.SFlag.SetFlagCarrier", Spec = "spec/objectives/messages.md#network")]
    public void SetFlagCarrier(bool taken)
    {
        FlagTaken = taken;
        if (Role == ENetRole.ROLE_Authority)
        {
            ObjectiveNatives.Net(this).SetFlagCarrier(this, taken);
        }
    }

    /// <summary>Event raised by native code: the carrier drops the disk.</summary>
    [Uc("SBase.SFlag.LeaveCarrier", Sha1 = "5d2a7e68f398cf5a34587adb7e584824fa7e29ca")]
    [PortOf("uc:SBase.SFlag:LeaveCarrier", Spec = "spec/objectives/disk-flag.md#rules")]
    public void LeaveCarrier()
    {
        if (FlagCarrier?.Controller is SPlayerController pc)
        {
            DiskCarryRules.DiscardFlag(pc);
        }
    }

    // ---- delivery ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Whether <paramref name="A"/> takes this disk (rule 26): a listed zone while the objective is open (the
    /// carrier becomes the taker), or the default zone for a merc carrier (the disk goes home).
    /// </summary>
    [Uc("SBase.SFlag.CheckDropZone", Sha1 = "cefc66933afcae1bdb74b5dd8a46984470286352")]
    [PortOf("uc:SBase.SFlag:CheckDropZone", Spec = "spec/objectives/disk-flag.md#rules")]
    public bool CheckDropZone(Actor? A)
    {
        if (A is not null && ReferenceEquals(A, DefDropZone) && FlagCarrier is SBasePawn { SPawnType: PawnType.SPAWNDEFENSE })
        {
            eventReset();
            return true;
        }

        if (Objective is not { ObjectiveTaken: true } && A is not null)
        {
            foreach (Actor? zone in DropZone)
            {
                if (ReferenceEquals(zone, A))
                {
                    ControllerWhoTakesIt = FlagCarrier?.Controller as PlayerController;
                    return true;
                }
            }
        }

        return false;
    }

    [Uc("SBase.SFlag.TakeObjective", Sha1 = "56a6813a611ad3f58e866832d97d5697df136327")]
    [PortOf("uc:SBase.SFlag:TakeObjective", Spec = "spec/objectives/disk-flag.md#rules")]
    protected internal override void TakeObjective()
    {
        ObjectiveMessages.Broadcast(this, K.MsgDataCaptured, this);
        float best = K.NearestZoneStart;
        for (int k = 0; k < DropZone.Length; k++)
        {
            if (DropZone[k] is { } zone)
            {
                float d = (zone.Location - Location).Size();
                if (d < best)
                {
                    best = d;
                    finalDropZone = k + 1;
                }
            }
        }

        base.TakeObjective();
        SetTimer(0f, false);
    }

    // ---- alarm locks (not reachable in SteeE) -------------------------------------------------------------------

    [Uc("SBase.SFlag.Lock", Sha1 = "069593263d5f2a8c6ebe8dceca7c4a573f75d8a4")]
    [PortOf("uc:SBase.SFlag:Lock", Spec = "spec/objectives/disk-flag.md#rules")]
    protected internal override void Lock()
    {
        if (PickupInteraction is { bCanPickup: true } pickup)
        {
            bPickable = false;
            pickup.bCanPickup = false;
            GotoState(K.s_TemporaryInactive);
        }
        else
        {
            bShouldLock = true;
        }
    }

    [Uc("SBase.SFlag.UnLock", Sha1 = "5d8b7a4b5c992c663910819b538a690a1b7aceef")]
    [PortOf("uc:SBase.SFlag:UnLock", Spec = "spec/objectives/disk-flag.md#rules")]
    protected internal override void UnLock() => bShouldLock = false;

    // ---- positioning ------------------------------------------------------------------------------------------

    /// <summary>
    /// The carrier's stand spot in front of a disk or zone entry (<c>s_FlagAndDropZone_GoTo</c> rule 9): 67 uu
    /// along the entry's facing at the pawn's height, looking back at it.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "SPlayerController.s_FlagAndDropZone_GoTo.BeginState stand spot")]
    public static void GetStandSpot(SInteractObject entry, Pawn pawn, out FVector Spot, out FVector Facing)
    {
        FVector f = entry.Rotation.Vector();
        (-f).Rotation().GetAxes(out FVector x, out FVector y, out FVector z);
        FVector off = entry.vRelativePositionForInteraction;
        Spot = entry.Location + x * off.X + y * off.Y + z * off.Z;
        Spot.Z = pawn.Location.Z;
        Spot -= x * K.FlagStandOff;
        Facing = -f;
        Facing.Z = 0f;
    }
}
