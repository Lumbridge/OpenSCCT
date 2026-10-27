using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// The fixed "Unspy Mate" entry: removes a merc's spy mark from a team mate. Written from
// spec/objectives/interaction-system.md and spec/objectives/team-interactions.md.
public partial class SRemoveSpyBulletInteraction
{
    /// <summary>Offered while the mate search finds a team mate carrying a spy mark (bSpyBulletOn).</summary>
    [UcNative("ASRemoveSpyBulletInteraction::CanUse", Exe = 0x10AE8050, Editor = 0x111CADF0)]
    [PortOf("nat:SBase.SMateInteraction.CanUse", Spec = "spec/objectives/team-interactions.md#unspy-mate")]
    [Provenance(ProvenanceKind.Decoded, "Game CanUse virtual 0x10ae8050: mate.bSpyBulletOn, then the mate search")]
    public override bool CanUse(SPlayerController? TheController) =>
        FindMate(TheController, static (_, mate) => mate.bSpyBulletOn);

    /// <summary>The mate's mark is removed (DetectedByMarqueurSpy(0, false)); then the default sends the player back.</summary>
    [Uc("SBase.SRemoveSpyBulletInteraction.InitInteract", Sha1 = "110359f5d61d7843f1383eacc1e48d3ebf8c4798")]
    [PortOf("uc:SBase.SRemoveSpyBulletInteraction:InitInteract", Spec = "spec/objectives/team-interactions.md#unspy-mate")]
    protected internal override void InitInteract(SPlayerController Instigator, int SubInteraction)
    {
        Mate?.DetectedByMarqueurSpy(0, false);
        base.InitInteract(Instigator, SubInteraction);
    }
}
