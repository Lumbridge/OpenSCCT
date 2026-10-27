using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// A loud noise reaching the player: the merc's sound arrows on the HUD.
public partial class SPlayerController
{
    private static readonly Name CNAME_ClientNotifyNoise = new("ClientNotifyNoise");

    /// <summary>
    /// A noise at <paramref name="Actor"/>: the HUD's NotifyHotSound, except for the merc weapon's dummy (its shots give
    /// no arrow). A controller without a HUD on the server is a remote player's: the call is sent to its machine.
    /// </summary>
    [Uc("SBase.SPlayerController.ClientNotifyNoise", Sha1 = "439ce2ed91999919b8cf0b5e3965ff5e4ebb2764")]
    [PortOf("uc:SBase.SPlayerController:ClientNotifyNoise", Spec = "spec/hud/merc-reticle.md#rules")]
    public override void ClientNotifyNoise(Actor? Actor, float Loudness)
    {
        if (myHUD is SHud hud)
        {
            if (Actor is not null && Actor is not SWeaponDefenseDummy)
            {
                hud.NotifyHotSound(Actor, Loudness);
            }

            return;
        }

        if (Role == ENetRole.ROLE_Authority && Actor is not null)
        {
            GameHost.ClientCall(this, new GameCall(CNAME_ClientNotifyNoise) { Actor = Actor, Float1 = Loudness });
        }
    }
}
