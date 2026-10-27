using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// A numbered HUD text for this player (the alarm's "detected" line, the noise emitter, the mines).
public partial class SPlayerController
{
    private static readonly Name CNAME_AddMessageOnHud = new("AddMessageOnHud");

    /// <summary>Seconds a numbered HUD text stays.</summary>
    [Provenance(ProvenanceKind.Decoded, "SPlayerController.AddMessageOnHud: AddHudNumberMessageToStack(Message, 4)")]
    public const int HudNumberMessageTime = 4;

    /// <summary>
    /// The HUD's AddHudNumberMessageToStack(Message, 4). A reliable server-to-owner event: on the server a controller
    /// without a HUD is a remote player's, and the call is sent to its machine, where this method runs again.
    /// </summary>
    [Uc("SBase.SPlayerController.AddMessageOnHud", Sha1 = "c722d6ac3b189b9c5cedf44a3fc054c3a311631c")]
    [PortOf("uc:SBase.SPlayerController:AddMessageOnHud", Spec = "spec/devices/alarm.md#network")]
    public void AddMessageOnHud(int Message)
    {
        if (myHUD is SHud hud)
        {
            hud.AddHudNumberMessageToStack(Message, HudNumberMessageTime);
            return;
        }

        if (Role == ENetRole.ROLE_Authority)
        {
            GameHost.ClientCall(this, new GameCall(CNAME_AddMessageOnHud) { Int = Message });
        }
    }
}
