using OpenScct.Provenance;
using K = OpenScct.Game.Equipment.K;

namespace OpenScct.Game.SBase;

/// <summary>
/// SBase.SFlareBurnProjector: the burn mark a flare leaves (spec/equipment/flare.md rule 5). It projects Burn_flare
/// onto the surface, leaves the mark for 30 s and goes at once.
/// </summary>
public partial class SFlareBurnProjector
{
    [Uc("SBase.SFlareBurnProjector.PostBeginPlay", Sha1 = "870ac64566914fef659b4de33b3daa22e0b838bf")]
    [PortOf("uc:SBase.SFlareBurnProjector:PostBeginPlay", Spec = "spec/equipment/flare.md#rules")]
    protected override void PostBeginPlay()
    {
        AttachProjector();
        AbandonProjector(K.FlareBurnLifetime);
        Destroy();
    }
}
