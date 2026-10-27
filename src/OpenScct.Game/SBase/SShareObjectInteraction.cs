using OpenScct.Core.Engine;
using OpenScct.Game.Objectives;
using OpenScct.Provenance;

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

    /// <summary>Offered while a living mate to give to has been found (the mate search belongs to the coop port).</summary>
    [UcNative("ASShareObjectInteraction::execCanUse", Exe = 0x10AB96F0, Editor = 0x111C9E60)]
    [PortOf("nat:SBase.SShareObjectInteraction.CanUse", Spec = "spec/objectives/interaction-system.md#rules")]
    [Provenance(ProvenanceKind.Estimated, "Expected: a living mate set by the native mate search")]
    public override bool CanUse(SPlayerController? TheController) =>
        aActorToInteractWith is { bDeleteMe: false, Health: > 0 } && base.CanUse(TheController);
}
