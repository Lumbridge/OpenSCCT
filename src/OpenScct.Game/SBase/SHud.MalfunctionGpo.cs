using OpenScct.Core;
using OpenScct.Core.Uc;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// The list of gameplay object markers the HUD draws (MalfunctionGamePlayObjects: the spy's GPO markers, the merc's
// radar alarm blips).
public partial class SHud
{
    /// <summary>Adds the place once, at the front of the list.</summary>
    [UcNative("ASHud::execAddMalfunctionGPO", Exe = 0x10a91d60)]
    [PortOf("nat:SBase.SHud.AddMalfunctionGPO", Spec = "spec/devices/gameplay-object.md#effects-and-helpers")]
    [Provenance(ProvenanceKind.Decoded, "game 0x10a91d60: when the vector is not in MalfunctionGamePlayObjects, Insert(0, 1) and store it")]
    public void AddMalfunctionGPO(FVector GPOLocation)
    {
        if (!MalfunctionGamePlayObjects.Contains(GPOLocation))
        {
            MalfunctionGamePlayObjects.Insert(0, GPOLocation);
        }
    }

    /// <summary>Removes the first entry at the place, if any.</summary>
    [UcNative("ASHud::execRemoveMalfunctionGPO", Exe = 0x10a91dd0)]
    [PortOf("nat:SBase.SHud.RemoveMalfunctionGPO", Spec = "spec/devices/gameplay-object.md#effects-and-helpers")]
    [Provenance(ProvenanceKind.Decoded, "game 0x10a91dd0: index of the vector, Remove(index, 1) when found")]
    public void RemoveMalfunctionGPO(FVector GPOLocation)
    {
        int i = MalfunctionGamePlayObjects.IndexOf(GPOLocation);
        if (i != -1)
        {
            MalfunctionGamePlayObjects.RemoveAt(i);
        }
    }
}
