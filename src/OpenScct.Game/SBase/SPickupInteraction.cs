using OpenScct.Core.Engine;
using OpenScct.Game.Objectives;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// The "Pick Up" entry of a pickable object (the Story disk at home), written from spec/objectives/disk-flag.md.
public partial class SPickupInteraction
{
    /// <summary>The generic sizing, then the owner's cylinder plus 50 x 10 (55 x 26 for the disk).</summary>
    [Uc("SBase.SPickupInteraction.PostBeginPlay", Sha1 = "940ff57713db8ea14b9bd9aecff90e0a7e4db3df")]
    [PortOf("uc:SBase.SPickupInteraction:PostBeginPlay", Spec = "spec/objectives/disk-flag.md#rules")]
    protected override void PostBeginPlay()
    {
        base.PostBeginPlay();
        if (Owner is SFlag { bPS2Objective: true })
        {
            // PS2 COM0xD641B: fixed cylinder after the base sizing.
            ObjectiveNatives.SetCollisionSize(this, K.Ps2PickupCylinder, K.Ps2PickupCylinder);
            bCanPickup = true;
            return;
        }
        ItRadius = (Owner?.CollisionRadius ?? 0f) + K.PickupRadiusPadding;
        ItHeight = (Owner?.CollisionHeight ?? 0f) + K.PickupHeightPadding;
        ObjectiveNatives.SetCollisionSize(this, ItRadius, ItHeight);
    }

    protected override void PostNetBeginPlay()
    {
        base.PostNetBeginPlay();
        // The PC base has an additional PostNetBeginPlay sizing pass. PS2
        // SInteractObject declares none; preserve the source pickup cylinder.
        if (Owner is SFlag { bPS2Objective: true }) ObjectiveNatives.SetCollisionSize(this, K.Ps2PickupCylinder, K.Ps2PickupCylinder);
    }

    protected override void NativeTick(float deltaSeconds)
    {
        base.NativeTick(deltaSeconds);
        // A dynamic flag's channel can be acknowledged after this entry's
        // initial bunch. Apply the source cylinder when its Owner resolves.
        if (Owner is SFlag { bPS2Objective: true } && (CollisionRadius != K.Ps2PickupCylinder || CollisionHeight != K.Ps2PickupCylinder))
            ObjectiveNatives.SetCollisionSize(this, K.Ps2PickupCylinder, K.Ps2PickupCylinder);
    }

    /// <summary>A living player walks to the object: the disk's own walk for an SFlag, the generic one otherwise.</summary>
    [Uc("SBase.SPickupInteraction.InitInteract", Sha1 = "7d1d7aa735bcd6f8c1318d61e10d2a4b9db9fcda")]
    [PortOf("uc:SBase.SPickupInteraction:InitInteract", Spec = "spec/objectives/disk-flag.md#rules")]
    protected internal override void InitInteract(SPlayerController Instigator, int SubInteraction)
    {
        if (ObjectiveNatives.IsAlive(Instigator))
        {
            ObjectiveNatives.MirrorState(Instigator, Owner is SFlag { bPS2Objective: false } ? K.s_FlagAndDropZone_GoTo : K.s_Pickup_GoTo);
        }
    }

    /// <summary>The base conditions plus: the object can be picked up from the menu now.</summary>
    [UcNative("ASPickupInteraction::execCanUse", Exe = 0x10AB96F0, Editor = 0x111BDD60)]
    [PortOf("nat:SBase.SPickupInteraction.CanUse", Spec = "spec/objectives/disk-flag.md#rules")]
    [Provenance(ProvenanceKind.Estimated, "Expected: base conditions and bCanPickup (spec open question 5)")]
    public override bool CanUse(SPlayerController? TheController) => bCanPickup && base.CanUse(TheController);
}
