using OpenScct.Core.Engine;
using OpenScct.Game.Objectives;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// The fixed "Exit" entry of every Use menu, written from spec/objectives/interaction-system.md.
public partial class SLeaveInteraction
{
    /// <summary>The generic sizing, then again on the server from the owner's cylinder plus 150.</summary>
    [Uc("SBase.SLeaveInteraction.PostBeginPlay", Sha1 = "286f296aee214ab68fbbf5759f8a286d5990539b")]
    [PortOf("uc:SBase.SLeaveInteraction:PostBeginPlay", Spec = "spec/objectives/interaction-system.md#rules")]
    protected override void PostBeginPlay()
    {
        base.PostBeginPlay();
        ObjectiveNatives.SetCollisionSize(this, (Owner?.CollisionRadius ?? 0f) + K.InteractionPadding,
            (Owner?.CollisionHeight ?? 0f) + K.InteractionPadding);
    }

    /// <summary>Fixed entries never join or leave the menu by touch.</summary>
    [Uc("SBase.SLeaveInteraction.Touch", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
    [PortOf("uc:SBase.SLeaveInteraction:Touch", Spec = "spec/objectives/interaction-system.md#rules")]
    protected override void Touch(Actor Other)
    {
    }

    [Uc("SBase.SLeaveInteraction.UnTouch", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
    [PortOf("uc:SBase.SLeaveInteraction:UnTouch", Spec = "spec/objectives/interaction-system.md#rules")]
    protected override void UnTouch(Actor Other)
    {
    }

    /// <summary>Exit is always offered.</summary>
    [UcNative("ASLeaveInteraction::execCanUse", Exe = 0x10AB96F0, Editor = 0x111BE020)]
    [PortOf("nat:SBase.SLeaveInteraction.CanUse", Spec = "spec/objectives/interaction-system.md#rules")]
    [Provenance(ProvenanceKind.Estimated, "Exit always shown (RE+ saw Use, Exit at a terminal)")]
    public override bool CanUse(SPlayerController? TheController) => true;
}
