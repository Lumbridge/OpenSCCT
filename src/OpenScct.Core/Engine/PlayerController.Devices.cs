using OpenScct.Provenance;

namespace OpenScct.Core.Engine;

// Engine.PlayerController functions the map devices call.
public partial class PlayerController
{
    /// <summary>
    /// A loud noise happened at <paramref name="Actor"/> (sent reliably to the owning client). Empty on
    /// PlayerController; SPlayerController shows it on the HUD.
    /// </summary>
    [Uc("Engine.PlayerController.ClientNotifyNoise", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
    [PortOf("uc:Engine.PlayerController:ClientNotifyNoise", Spec = "spec/devices/events.md#rules")]
    public virtual void ClientNotifyNoise(Actor? Actor, float Loudness)
    {
    }
}
