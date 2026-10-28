using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Equipment;
using OpenScct.Game.Objectives;
using OpenScct.Physics.Collision;
using OpenScct.Provenance;
using K = OpenScct.Game.Objectives.K;

namespace OpenScct.Game.SBase;

// The fixed "Give" entry (share a gadget with a team mate), written from spec/objectives/interaction-system.md.
public partial class SShareObjectInteraction
{
    protected override void CleanupDestroyedReferences()
    {
        base.CleanupDestroyedReferences();
        aActorToInteractWith = Live(aActorToInteractWith);
    }

    [Uc("SBase.SShareObjectInteraction.PostBeginPlay", Sha1 = "286f296aee214ab68fbbf5759f8a286d5990539b")]
    [PortOf("uc:SBase.SShareObjectInteraction:PostBeginPlay", Spec = "spec/objectives/interaction-system.md#rules")]
    protected override void PostBeginPlay()
    {
        base.PostBeginPlay();
        ObjectiveNatives.SetCollisionSize(this, (Owner?.CollisionRadius ?? 0f) + K.InteractionPadding,
            (Owner?.CollisionHeight ?? 0f) + K.InteractionPadding);
    }

    [Uc("SBase.SShareObjectInteraction.Touch", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
    [PortOf("uc:SBase.SShareObjectInteraction:Touch", Spec = "spec/objectives/interaction-system.md#rules")]
    protected override void Touch(Actor Other)
    {
    }

    [Uc("SBase.SShareObjectInteraction.UnTouch", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
    [PortOf("uc:SBase.SShareObjectInteraction:UnTouch", Spec = "spec/objectives/interaction-system.md#rules")]
    protected override void UnTouch(Actor Other)
    {
    }

    /// <summary>
    /// Offered while a team mate could take one charge of the selected gadget: the level's pawns in order; the first
    /// that is alive, not lying on the ground, not the player's own pawn, of the player's side, within the mate reach
    /// (closer than 200 uu, in front, facing the player), in a clear line (or a line that stops on him) and who holds
    /// the same gadget with fewer charges than its full load, while the selected gadget has a charge to give, becomes
    /// <see cref="aActorToInteractWith"/>. With none it is cleared.
    /// </summary>
    [UcNative("ASShareObjectInteraction::execCanUse", Exe = 0x10AB96F0, Editor = 0x111C9E60)]
    [PortOf("nat:SBase.SShareObjectInteraction.CanUse", Spec = "spec/objectives/interaction-system.md#rules")]
    [Provenance(ProvenanceKind.Decoded, "Editor ASShareObjectInteraction CanUse virtual 0x111c9fe0 (vtable +0x14c): mate reach 200 / 0.75, line 0x86 hitting nothing or the mate, SelectedGadget Charge > 0, aGadgetType[i] == id and aNbChargesGad<i> < default Charge")]
    public override bool CanUse(SPlayerController? TheController)
    {
        if (TheController?.Pawn is not SBasePawn me)
        {
            return false;
        }

        foreach (SBasePawn mate in GadgetWorld.Pawns(XLevel))
        {
            if (mate.bDeleteMe || mate.Health <= 0 || mate.bLyingOnGround || ReferenceEquals(mate, me) || mate.SPawnType != me.SPawnType)
            {
                continue;
            }

            if (me.SelectedGadget is not { Charge: > 0 } gadget || !SMateInteraction.MateInReach(me, mate))
            {
                continue;
            }

            if (GadgetWorld.Line(this, me.Location, mate.Location, SMateInteraction.MateLineFlags, out FCheckResult hit) &&
                hit.Actor is not null && !ReferenceEquals(hit.Actor, mate))
            {
                continue;
            }

            if (CanTake(mate, gadget))
            {
                aActorToInteractWith = mate;
                return true;
            }
        }

        aActorToInteractWith = null;
        return false;
    }

    /// <summary>The mate holds the gadget in one of his four slots with fewer charges than its full load.</summary>
    private static bool CanTake(SBasePawn mate, SGadget gadget)
    {
        int id = SGadget.GetUniqueIDByClass(new ClassRef(gadget.GetUcClass().Path));
        for (int slot = 0; slot < 4; slot++)
        {
            if (mate.aGadgetType[slot] != id)
            {
                continue;
            }

            int held = slot switch
            {
                0 => mate.aNbChargesGad0,
                1 => mate.aNbChargesGad1,
                2 => mate.aNbChargesGad2,
                _ => mate.aNbChargesGad3,
            };
            if (held < gadget.ClassDefaults.Charge)
            {
                return true;
            }
        }

        return false;
    }
}
