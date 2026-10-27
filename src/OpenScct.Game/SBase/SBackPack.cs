using OpenScct.Core.Uc;
using OpenScct.Game.Objectives;
using OpenScct.Provenance;
using K = OpenScct.Game.Equipment.K;

namespace OpenScct.Game.SBase;

/// <summary>
/// SBase.SBackPack (and SBackPack_Att): the backpack mesh a backpack carrier wears (spec/equipment/backpack.md). A
/// simulated proxy whose Instigator is the carrier; each machine keeps it on his spine bone, where the view draws it.
/// </summary>
public partial class SBackPack
{
    /// <summary>On every machine: attached to the Instigator's 'B Spine2'.</summary>
    [Uc("SBase.SBackPack.Tick", Sha1 = "5ebf5f26be65ebe66e077ed3508113797bca4bbf")]
    [PortOf("uc:SBase.SBackPack:Tick", Spec = "spec/equipment/backpack.md#rules")]
    [Provenance(ProvenanceKind.Estimated, "Attached again only when the base or bone differs (the script attaches every tick, with the same result)")]
    protected override void Tick(float DeltaTime)
    {
        if (Instigator is SBasePawn pawn && (!ReferenceEquals(Base, pawn) || AttachmentBone != K.BackPackBone))
        {
            ObjectiveNatives.AttachToBone(pawn, this, K.BackPackBone);
        }
    }
}
