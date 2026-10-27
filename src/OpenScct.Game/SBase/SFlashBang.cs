using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

/// <summary>
/// SBase.SFlashBang: the flare a flash-bang leaves where it goes off (spec/equipment/flash-bang.md rule 5). The server
/// spawns it and it replicates; every drawing machine lights the spot for an instant with its own SFlashLight.
/// </summary>
public partial class SFlashBang
{
    /// <summary>Off a dedicated server, the short flash light at the flare.</summary>
    [Uc("SBase.SFlashBang.PostNetBeginPlay", Sha1 = "2dfb8e0bbd9d33e318f9699d1fb6aee43b5c0330")]
    [PortOf("uc:SBase.SFlashBang:PostNetBeginPlay", Spec = "spec/equipment/flash-bang.md#rules")]
    protected override void PostNetBeginPlay()
    {
        if (Level.NetMode != ENetMode.NM_DedicatedServer)
        {
            XLevel.Spawn<SFlashLight>(Location, default, null, null);
        }
    }
}
