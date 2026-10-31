using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Equipment;
using OpenScct.Game.Objectives;
using K = OpenScct.Game.Objectives.K;
using OpenScct.Provenance;
using MK = OpenScct.Game.Modes.K;

namespace OpenScct.Game.SBase;

// The "Place bomb" / "Remove" entry of a bomb spot, written from spec/objectives/bomb-target.md.
public partial class SBombTargetInteraction
{
    private SBombTargetInteraction? _classDefaults;

    private SBombTargetInteraction ClassDefaults => _classDefaults ??= GetUcClass().Create() as SBombTargetInteraction ?? this;

    /// <summary>The label follows the charge: "Remove" while armed, else the class default ("Place bomb").</summary>
    [Uc("SBase.SBombTargetInteraction.Touch", Sha1 = "61fce62f0196162ba3be290667a7c09f1df74561")]
    [PortOf("uc:SBase.SBombTargetInteraction:Touch", Spec = "spec/objectives/bomb-target.md#rules")]
    protected override void Touch(Actor Other)
    {
        Description = bRemove ? DescriptionRemove : ClassDefaults.Description;
        base.Touch(Other);
    }

    /// <summary>
    /// Multicast (rule 15): the label flag changes and the local player's pawn leaves and re-enters the entry so its
    /// menu refreshes; the authority does the same for every other touching SBase pawn.
    /// </summary>
    [Uc("SBase.SBombTargetInteraction.Update", Sha1 = "3c49e6be57f2c43c464bdbbc931dd08857da52ec")]
    [PortOf("uc:SBase.SBombTargetInteraction:Update", Spec = "spec/objectives/bomb-target.md#rules")]
    [PortOf("rep:SBase.SBombTargetInteraction.Update", Spec = "spec/objectives/bomb-target.md#network")]
    public void Update(bool Remove)
    {
        bRemove = Remove;
        // UsableBy is not replicated; the multicast must refresh the client's side filter as well as its label.
        UsableBy = Remove ? Usable.U_SPAWNDEFENSE : Usable.U_SPAWNOFFENSE;
        Description = Remove ? DescriptionRemove : ClassDefaults.Description;
        Pawn? local = Level.GetLocalPlayerController()?.Pawn;
        if (local is not null)
        {
            UnTouch(local);
        }

        var touching = new List<Actor>(Touching);
        foreach (Actor a in touching)
        {
            if (a is not SBasePawn pawn)
            {
                continue;
            }

            if (ReferenceEquals(pawn, local))
            {
                Touch(pawn);
                continue;
            }

            if (Role == ENetRole.ROLE_Authority)
            {
                UnTouch(pawn);
                Touch(pawn);
            }
        }

        if (Role == ENetRole.ROLE_Authority)
        {
            ObjectiveNatives.Net(this).UpdateBombInteraction(this, Remove);
        }
    }

    /// <summary>
    /// Server side of a use (rules 4 and 10): a spy with the modem starts the plant at this spot; a merc removes an
    /// armed charge (secured, credited) and goes back.
    /// </summary>
    [Uc("SBase.SBombTargetInteraction.InitInteract", Sha1 = "ea6c1ad3f4db05a2c2590ee501e55d59ac6215e6")]
    [PortOf("uc:SBase.SBombTargetInteraction:InitInteract", Spec = "spec/objectives/bomb-target.md#rules")]
    protected internal override void InitInteract(SPlayerController Instigator, int SubInteraction)
    {
        if (Instigator.Pawn is SPawnAttaque)
        {
            if (FindBomb(Instigator) is { } bomb)
            {
                bomb.BombTrigger = Owner as SBombTargetObjectiveTrigger;
                Instigator.ServerPlaceBomb();
            }
            else if (Instigator.IsInState(K.s_MenuInteraction) && ObjectiveNatives.IsAlive(Instigator))
            {
                ObjectiveNatives.MirrorState(Instigator, Instigator.PreviousState, Name.None, true);
            }

            return;
        }

        if (Owner is SBombTargetObjectiveTrigger spot)
        {
            spot.eventTriggering(false);
            spot.SetTimer(0f, false);
            ObjectiveMessages.Broadcast(spot, MK.MsgBombSecured, spot);
        }

        if (Instigator.PlayerReplicationInfo?.Stats is { } stats)
        {
            stats.Merc_SecuredBombs++;
        }

        base.InitInteract(Instigator, SubInteraction);
    }

    private static readonly ClassRef GadBombClass = new("SBase.SGadBomb");

    /// <summary>The pawn's modem (FindGadgetType(class'SGadBomb')).</summary>
    private static SGadBomb? FindBomb(SPlayerController pc) =>
        pc.SBPawn is { } pawn ? PawnInventory.FindGadgetType(pawn, GadBombClass) as SGadBomb : null;
}
