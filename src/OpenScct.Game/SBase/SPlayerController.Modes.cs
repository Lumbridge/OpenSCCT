using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Objectives;
using OpenScct.Provenance;
using MK = OpenScct.Game.Modes.K;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController: the mode-specific server functions. Disk Hunt's disk drop (spec/objectives/disk-hunt.md),
// the pickup entry points the pickup states call (spec/objectives/disk-flag.md, disk-hunt.md) and the bomb plant
// request (spec/objectives/bomb-target.md). Each one is a reliable client-to-server function: on a client it is
// sent to the server's copy of the controller, which runs it.
public partial class SPlayerController
{
    private static readonly Name CNAME_SetHandItemFromInteraction = new("SetHandItemFromInteraction");
    private static readonly Name CNAME_DiscardPickup = new("DiscardPickup");
    private static readonly Name CNAME_DiscardFlag = new("DiscardFlag");
    private static readonly Name CNAME_ServerPlaceBomb = new("ServerPlaceBomb");
    private static readonly Name CNAME_s_PlaceBomb = new("s_PlaceBomb");

    /// <summary>True when this machine sent the call to the server instead of running it.</summary>
    private bool SentToServer(Name function, Actor? actor = null)
    {
        if (Role >= ENetRole.ROLE_Authority)
        {
            return false;
        }

        GameHost.ServerCall(this, new GameCall(function) { Actor = actor });
        return true;
    }

    /// <summary>
    /// Disk Hunt (rule 11): a dying carrier throws every disk he holds, the next one a little faster than the last,
    /// and everyone hears that disks were dropped (message 30 for a merc carrier, 31 for a spy).
    /// </summary>
    [Uc("SBase.SPlayerController.DropDisks", Sha1 = "3f61e23aababd918cfb400a0d04434fb5ea4cada")]
    [PortOf("uc:SBase.SPlayerController:DropDisks", Spec = "spec/objectives/disk-hunt.md#rules")]
    public void DropDisks()
    {
        if (Role != ENetRole.ROLE_Authority || SBPawn is not { } pawn)
        {
            return;
        }

        if (pawn.Disks.Count > 0)
        {
            ObjectiveMessages.Broadcast(this, pawn.SPawnType == PawnType.SPAWNDEFENSE ? MK.MsgDisksDroppedMerc : MK.MsgDisksDroppedSpy);
        }

        for (int i = 0; i < pawn.Disks.Count; i++)
        {
            float speed = i * MK.DiskDropVelocityStep;
            pawn.Disks[i]?.eventThrow(this, new FVector(speed, speed, speed));
        }

        pawn.Disks.Clear();
        if (PlayerReplicationInfo is { } pri)
        {
            pri.iNbObjectivesTaken = 0;
        }
    }

    /// <summary>
    /// The server takes the object behind a menu pickup for this player (disk-flag.md rule 11, disk-hunt.md rule 7).
    /// </summary>
    [Uc("SBase.SPlayerController.SetHandItemFromInteraction", Sha1 = "8aeb893503f573519f72df7564def7e16d843df1")]
    [PortOf("rep:SBase.SPlayerController.SetHandItemFromInteraction", Spec = "spec/objectives/disk-hunt.md#network")]
    public void SetHandItemFromInteraction(SInteractObject? IntObj)
    {
        if (!SentToServer(CNAME_SetHandItemFromInteraction, IntObj))
        {
            DiskCarryRules.SetHandItemFromInteraction(this, IntObj);
        }
    }

    /// <summary>The hand item falls with the pawn's velocity (disk-flag.md rule 9; the death entry).</summary>
    [Uc("SBase.SPlayerController.DiscardPickup", Sha1 = "5833d34316acb21f6a3da81b4cc682a54620db0c")]
    [PortOf("uc:SBase.SPlayerController:DiscardPickup", Spec = "spec/objectives/disk-flag.md#rules")]
    [PortOf("rep:SBase.SPlayerController.DiscardPickup", Spec = "spec/objectives/disk-hunt.md#network")]
    public void DiscardPickup()
    {
        if (SentToServer(CNAME_DiscardPickup) || SBPawn is not { HandItem: { } item } pawn)
        {
            return;
        }

        item.eventThrow(this, pawn.Velocity);
        pawn.HandItem = null;
    }

    /// <summary>The carried flag falls where the carrier is (disk-flag.md rule 16).</summary>
    [Uc("SBase.SPlayerController.DiscardFlag", Sha1 = "85339f5e50a804c7938b67aafb573ad047052934")]
    [PortOf("rep:SBase.SPlayerController.DiscardFlag", Spec = "spec/objectives/disk-hunt.md#network")]
    public void DiscardFlag()
    {
        if (!SentToServer(CNAME_DiscardFlag))
        {
            DiskCarryRules.DiscardFlag(this);
        }
    }

    /// <summary>A living spy at a bomb spot starts the plant (bomb-target.md rule 4).</summary>
    [Uc("SBase.SPlayerController.ServerPlaceBomb", Sha1 = "305d9639cf247d1ed70681cde98b2c09324b28da")]
    [PortOf("uc:SBase.SPlayerController:ServerPlaceBomb", Spec = "spec/objectives/bomb-target.md#rules")]
    [PortOf("rep:SBase.SPlayerController.ServerPlaceBomb", Spec = "spec/objectives/bomb-target.md#network")]
    public void ServerPlaceBomb()
    {
        if (SentToServer(CNAME_ServerPlaceBomb))
        {
            return;
        }

        if (SBPawn is { Health: > 0 })
        {
            eventMirrorState(CNAME_s_PlaceBomb);
        }
    }
}
