using OpenScct.Provenance;

namespace OpenScct.Game.Equipment;

// View model clip data of the merc's melee moves (spec/combat/melee-elbow-charge-berserk.md rule 30).
public static partial class K
{
    [Provenance(ProvenanceKind.Decoded, "SWeaponFirst.ukx FirstAtakCrFd2 17 frames at 15 fps (melee rule 30)")]
    [Provenance(ProvenanceKind.Measured, "weapon s_ElbowShocking 1.082 s: the view model clip 1.067 s plus one tick (melee rule 34)")]
    public static readonly ClipData FirstAtakCrFd2 = new(17, 15f);
}
