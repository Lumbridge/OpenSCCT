using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Provenance;

namespace OpenScct.Game.Objectives;

/// <summary>Which state the stand-spot walk ends in (<c>s_FlagAndDropZone_GoTo.TopSynchro</c>).</summary>
public enum EFlagWalkEnd
{
    /// <summary>The entry is a pickup: s_Pickup with SA_PICKUP_FLAG.</summary>
    Pickup,

    /// <summary>The entry is a drop zone: s_PutFlagInDropZone with SA_DROP_FLAG.</summary>
    PutInDropZone,
}

/// <summary>
/// The controller side of carrying the disk (<c>SPlayerController.DiscardFlag</c>, <c>PickupFlag</c>,
/// <c>SetHandItemFromInteraction</c> and the s_FlagAndDropZone_GoTo, s_Pickup and s_PutFlagInDropZone states,
/// spec/objectives/disk-flag.md rules 9 to 29), objective part. The controller port calls these from its states;
/// animation actions stay there.
/// </summary>
public static class DiskCarryRules
{
    /// <summary>
    /// <c>SPlayerController.DiscardFlag</c> (server, rule 16): the carried disk falls with the pawn's velocity and,
    /// while its objective is open, everyone hears it was dropped.
    /// </summary>
    [Uc("SBase.SPlayerController.DiscardFlag", Sha1 = "85339f5e50a804c7938b67aafb573ad047052934")]
    [PortOf("uc:SBase.SPlayerController:DiscardFlag", Spec = "spec/objectives/disk-flag.md#rules")]
    public static void DiscardFlag(SPlayerController pc)
    {
        if (pc.Pawn is not SBasePawn { CarriedFlag: { } flag } pawn)
        {
            return;
        }

        if (flag.Objective is not { ObjectiveTaken: true })
        {
            ObjectiveMessages.Broadcast(pc, K.MsgDataDropped, flag);
        }

        flag.eventThrow(pc, pawn.Velocity);
        pawn.CarriedFlag = null;
    }

    /// <summary>
    /// <c>SPlayerController.PickupFlag(flag)</c> (server, rule 22): a spy who touches a resting disk takes it at
    /// once, with no menu and no animation; it rides on his thigh.
    /// </summary>
    [Uc("SBase.SPlayerController.PickupFlag", Sha1 = "192acb31778ace15c728b36999e8bf6c124ff5ad")]
    [PortOf("uc:SBase.SPlayerController:PickupFlag", Spec = "spec/objectives/disk-flag.md#rules")]
    public static void PickupFlag(SPlayerController pc, SFlag flag)
    {
        if (pc.Pawn is not SBasePawn pawn)
        {
            return;
        }

        if (pawn.CarriedFlag is not null)
        {
            DiscardFlag(pc);
        }

        pawn.CarriedFlag = flag;
        flag.eventNotifyPickup(pc);
        ObjectiveNatives.DetachFromBone(flag);
        ObjectiveNatives.AttachToBone(pawn, flag, K.BoneThigh);
        flag.RelativeLocation = K.ThighOffset;
        flag.RelativeRotation = K.ThighRotationTouch;
    }

    /// <summary>
    /// <c>SPlayerController.SetHandItemFromInteraction(IntObj)</c> (server, rule 11): the object behind a menu pickup
    /// is taken, unless the pawn's hand is full or someone holds it. A disk goes to <c>CarriedFlag</c> (a second disk
    /// drops the first); other pickables go to the hand.
    /// </summary>
    [Uc("SBase.SPlayerController.SetHandItemFromInteraction", Sha1 = "8aeb893503f573519f72df7564def7e16d843df1")]
    [PortOf("uc:SBase.SPlayerController:SetHandItemFromInteraction", Spec = "spec/objectives/disk-flag.md#rules")]
    public static void SetHandItemFromInteraction(SPlayerController pc, SInteractObject? IntObj)
    {
        if (pc.Pawn is not SBasePawn pawn || pawn.HandItem is not null || IntObj?.Owner is not SGamePlayObject item)
        {
            return;
        }

        if (item.Owner is PlayerController)
        {
            return;
        }

        if (item is SFlag flag)
        {
            if (pawn.CarriedFlag is not null)
            {
                DiscardFlag(pc);
            }

            pawn.CarriedFlag = flag;
            flag.eventNotifyPickup(pc);
            return;
        }

        // Disk Hunt (disk-hunt.md rule 7): the disk joins the front of the pawn's disks and is the hand item until
        // the pickup animation frees the hand; the spy's PRI shows how many he carries.
        if (item is SDisk disk)
        {
            pawn.Disks.Insert(0, disk);
            disk.eventNotifyPickup(pc);
            pawn.HandItem = disk;
            if (pc.PlayerReplicationInfo is { } pri)
            {
                pri.iNbObjectivesTaken = pawn.Disks.Count;
            }

            return;
        }

        pawn.HandItem = item;
        item.eventNotifyPickup(pc);
    }

    /// <summary>
    /// <c>s_FlagAndDropZone_GoTo.BeginState</c>, objective part (rules 9 and 27): the stand spot in front of the
    /// selected disk or zone entry becomes the repositioning target (<c>SBasePawn.vVandammePos</c>).
    /// </summary>
    [Uc("SBase.SPlayerController.s_FlagAndDropZone_GoTo.BeginState", Sha1 = "31ee4c89e68649fccce0ce7534fe42e796807626")]
    [PortOf("uc:SBase.SPlayerController:s_FlagAndDropZone_GoTo.BeginState", Spec = "spec/objectives/disk-flag.md#rules")]
    public static bool GoToBeginState(SPlayerController pc, out FVector Facing)
    {
        Facing = default;
        if (pc.IManager?.GetCurrentInteraction() is not { } entry || pc.Pawn is not { } pawn)
        {
            return false;
        }

        SFlag.GetStandSpot(entry, pawn, out FVector spot, out Facing);
        if (pawn is SBasePawn sp)
        {
            sp.vVandammePos = spot;
        }

        return true;
    }

    /// <summary><c>s_FlagAndDropZone_GoTo.TopSynchro</c> (rules 10 and 27): which action the walk ends in.</summary>
    [Uc("SBase.SPlayerController.s_FlagAndDropZone_GoTo.TopSynchro", Sha1 = "19d8027a0c3c321887d60fcb084ddc3af4aca626")]
    [PortOf("uc:SBase.SPlayerController:s_FlagAndDropZone_GoTo.TopSynchro", Spec = "spec/objectives/disk-flag.md#rules")]
    public static EFlagWalkEnd WalkEnd(SPlayerController pc) =>
        pc.IManager?.GetCurrentInteraction() is SPickupInteraction ? EFlagWalkEnd.Pickup : EFlagWalkEnd.PutInDropZone;

    /// <summary><c>s_Pickup.BeginState</c> on the local player: asks the server for the selected object.</summary>
    [Uc("SBase.SPlayerController.s_Pickup.BeginState", Sha1 = "dbcba74040c53d7ef0faac015aedd5274f50ec41")]
    [PortOf("uc:SBase.SPlayerController:s_Pickup.BeginState", Spec = "spec/objectives/disk-flag.md#rules")]
    public static void PickupBeginState(SPlayerController pc) =>
        SetHandItemFromInteraction(pc, pc.IManager?.GetCurrentInteraction());

    /// <summary><c>SBasePawn.AnimNotifyPickup</c> (server, rule 14): with an empty hand the carried disk goes to the hand.</summary>
    [Uc("SBase.SBasePawn.AnimNotifyPickup", Sha1 = "fe3cdef63349f279d0c093dd65b3a078e4dae5fc")]
    [PortOf("uc:SBase.SBasePawn:AnimNotifyPickup", Spec = "spec/objectives/disk-flag.md#rules")]
    public static void AnimNotifyPickup(SBasePawn pawn)
    {
        if (pawn.Role != ENetRole.ROLE_Authority)
        {
            return;
        }

        // A hand item goes to the hand; a Disk Hunt disk also frees the hand (disk-hunt.md rule 10).
        if (pawn.HandItem is { } item)
        {
            if (item is SDisk)
            {
                pawn.HandItem = null;
            }

            ObjectiveNatives.AttachToBone(pawn, item, K.BoneHand);
            item.RelativeLocation = Modes.K.HandItemOffset;
            item.RelativeRotation = Modes.K.HandItemRotation;
            return;
        }

        if (pawn.CarriedFlag is { } flag)
        {
            ObjectiveNatives.AttachToBone(pawn, flag, K.BoneHand);
            flag.RelativeLocation = K.HandOffset;
            flag.RelativeRotation = K.HandRotationPickup;
        }
    }

    /// <summary><c>s_Pickup.EndState</c> (server, rule 14): the disk moves to the thigh.</summary>
    [Uc("SBase.SPlayerController.s_Pickup.EndState", Sha1 = "974d465f9b303f25b1ba69542ff802acea6ffd03")]
    [PortOf("uc:SBase.SPlayerController:s_Pickup.EndState", Spec = "spec/objectives/disk-flag.md#rules")]
    public static void PickupEndState(SPlayerController pc)
    {
        if (pc.Role != ENetRole.ROLE_Authority || pc.Pawn is not SBasePawn pawn)
        {
            return;
        }

        if (pawn.CarriedFlag is { } flag)
        {
            ObjectiveNatives.AttachToBone(pawn, flag, K.BoneThigh);
            flag.RelativeLocation = K.ThighOffset;
            flag.RelativeRotation = K.ThighRotationMenu;
        }
        else if (pawn.Disks.Count > 0 && pawn.Disks[0] is { } disk)
        {
            // Disk Hunt (disk-hunt.md rule 10): the newest disk rides on the thigh.
            ObjectiveNatives.DetachFromBone(disk);
            ObjectiveNatives.AttachToBone(pawn, disk, K.BoneThigh);
            disk.RelativeLocation = Modes.K.DiskThighOffset;
            disk.RelativeRotation = Modes.K.DiskThighRotation;
        }
    }

    /// <summary><c>s_PutFlagInDropZone.BeginState</c> (server, rule 28): the disk goes to the hand for the drop.</summary>
    [Uc("SBase.SPlayerController.s_PutFlagInDropZone.BeginState", Sha1 = "e1808c056a94193a2fc49a39677329ec1dee81e0")]
    [PortOf("uc:SBase.SPlayerController:s_PutFlagInDropZone.BeginState", Spec = "spec/objectives/disk-flag.md#rules")]
    public static void PutFlagBeginState(SPlayerController pc)
    {
        if (pc.Role == ENetRole.ROLE_Authority && pc.Pawn is SBasePawn { CarriedFlag: { } flag } pawn)
        {
            ObjectiveNatives.AttachToBone(pawn, flag, K.BoneHand);
            flag.RelativeLocation = K.HandOffset;
            flag.RelativeRotation = K.HandRotationDrop;
        }
    }

    /// <summary>
    /// <c>s_PutFlagInDropZone.EndState</c> (server, rule 28): a disk still in the hand is delivered (thrown with no
    /// velocity, then taken). One moved back to the thigh by damage is not.
    /// </summary>
    [Uc("SBase.SPlayerController.s_PutFlagInDropZone.EndState", Sha1 = "e4bd696c055d00b0c585b452ccdbc26d25fded83")]
    [PortOf("uc:SBase.SPlayerController:s_PutFlagInDropZone.EndState", Spec = "spec/objectives/disk-flag.md#rules")]
    public static void PutFlagEndState(SPlayerController pc)
    {
        if (pc.Role != ENetRole.ROLE_Authority || pc.Pawn is not SBasePawn { CarriedFlag: { } flag } pawn)
        {
            return;
        }

        if (flag.AttachmentBone == K.BoneHand)
        {
            flag.eventThrow(pc, FVector.Zero);
            flag.eventTakeObjective();
            pawn.CarriedFlag = null;
        }
    }

    /// <summary>
    /// <c>s_PutFlagInDropZone.PawnTakeDamage</c>, objective part (rule 29): the disk goes back to the thigh (so the
    /// delivery is cancelled) and a living player returns to walking; the standard damage follows.
    /// </summary>
    [Uc("SBase.SPlayerController.s_PutFlagInDropZone.PawnTakeDamage", Sha1 = "ff41057029808fb6dc2380acf2921f5c78aed792")]
    [PortOf("uc:SBase.SPlayerController:s_PutFlagInDropZone.PawnTakeDamage", Spec = "spec/objectives/disk-flag.md#rules")]
    public static void PutFlagTakeDamage(SPlayerController pc)
    {
        if (pc.Pawn is SBasePawn { CarriedFlag: { } flag } pawn)
        {
            ObjectiveNatives.AttachToBone(pawn, flag, K.BoneThigh);
            flag.RelativeLocation = K.ThighOffset;
            flag.RelativeRotation = K.ThighRotationTouch;
        }

        if (ObjectiveNatives.IsAlive(pc))
        {
            ObjectiveNatives.MirrorState(pc, K.s_PlayerWalkingSt0H);
        }
    }
}
