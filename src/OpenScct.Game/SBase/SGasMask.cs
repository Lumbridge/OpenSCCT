using OpenScct.Core.Uc;
using OpenScct.Game.Objectives;
using OpenScct.Provenance;
using K = OpenScct.Game.Equipment.K;

namespace OpenScct.Game.SBase;

/// <summary>
/// SBase.SGasMask: the gas mask a merc carrying the gadget wears on his head (DEF_Gasmask, spec/equipment/gas-mask.md
/// rule 6). A simulated proxy whose Instigator is the merc; each machine keeps it on his head bone, where the view draws
/// it.
/// </summary>
public partial class SGasMask
{
    /// <summary>On every machine: attached to the Instigator's 'B Head'.</summary>
    [Uc("SBase.SGasMask.Tick", Sha1 = "9f10c4d302d942bf449cf0df8a3d6896cfbdd716")]
    [PortOf("uc:SBase.SGasMask:Tick", Spec = "spec/equipment/gas-mask.md#rules")]
    [Provenance(ProvenanceKind.Estimated, "Attached again only when the base or bone differs (the script attaches every tick, with the same result)")]
    protected override void Tick(float DeltaTime)
    {
        if (Instigator is SBasePawn pawn && (!ReferenceEquals(Base, pawn) || AttachmentBone != K.GasMaskBone))
        {
            ObjectiveNatives.AttachToBone(pawn, this, K.GasMaskBone);
        }
    }
}
