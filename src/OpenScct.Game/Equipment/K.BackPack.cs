using OpenScct.Core;
using OpenScct.Provenance;

namespace OpenScct.Game.Equipment;

// Constants of the backpack (spec/equipment/backpack.md).
public static partial class K
{
    [Provenance(ProvenanceKind.Decoded, "SGadBackPack.GetBackPackCharge: bBigTeam = Team.Size >= 3")]
    public const int BackPackBigTeam = 3;

    [Provenance(ProvenanceKind.Decoded, "SGadBackPack.GetBackPackCharge: default int(default.Charge x 0.67)")]
    public const float BackPackDefaultShare = 0.67f;

    [Provenance(ProvenanceKind.Decoded, "SGadBackPack.Use: AddMessageToAuxiliaryStack(TextGadgetRefillDone, 2)")]
    public const float BackPackRefillMessageTime = 2f;

    [Provenance(ProvenanceKind.Decoded, "SGadBackPack.PostBeginPlay, SBackPack.Tick: AttachToBone(DummyBackPack, 'B Spine2')")]
    public static readonly Name BackPackBone = new("B Spine2");
}
