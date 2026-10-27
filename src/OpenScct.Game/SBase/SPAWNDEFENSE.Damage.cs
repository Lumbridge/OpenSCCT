using OpenScct.Core;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SPAWNDEFENSE engine events of the equipment port. Written from spec/equipment/merc-flashlight.md.
public partial class SPAWNDEFENSE
{
    /// <summary>A destroyed merc destroys his torch and laser (merc-flashlight.md rule 7), then his inventory.</summary>
    [Uc("SBase.SPAWNDEFENSE.Destroyed", Sha1 = "5df59a70809f31bb3c51de9cedfcbe44b529a5ce")]
    [PortOf("uc:SBase.SPAWNDEFENSE:Destroyed", Spec = "spec/equipment/merc-flashlight.md#rules")]
    protected override void Destroyed()
    {
        Torche?.Destroy();
        Torche = null;
        TorcheLaser?.Destroy();
        TorcheLaser = null;
        base.Destroyed();
    }
}
