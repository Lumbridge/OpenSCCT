using OpenScct.Core.Uc;
using OpenScct.Game.Equipment;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SPAWNDEFENSE.PostBeginPlay: the merc's goggle glow (spec/equipment/goggle-glow.md rule 1).
public partial class SPAWNDEFENSE
{
    /// <summary>The goggle glow is spawned on clients, standalone and listen servers, before the parent's PostBeginPlay.</summary>
    [Uc("SBase.SPAWNDEFENSE.PostBeginPlay", Sha1 = "3d68dd5a5adbb25ad04082d6ab930e68bd0dc602")]
    [PortOf("uc:SBase.SPAWNDEFENSE:PostBeginPlay", Spec = "spec/equipment/goggle-glow.md#rules")]
    protected override void PostBeginPlay()
    {
        PawnVision.SpawnGoggleBeam(this);
        base.PostBeginPlay();
    }
}
