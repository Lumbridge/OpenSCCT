using OpenScct.Core;
using OpenScct.Game.Equipment;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

/// <summary>
/// SBase.SMineProxInteraction: "Remove" on a placed mine (spec/equipment/mine-launcher.md rule 24). The sticky cam's
/// pick-up interaction derives from it.
/// </summary>
public partial class SMineProxInteraction
{
    /// <summary>The user gets the mine back as a charge and the mine is gone.</summary>
    [Uc("SBase.SMineProxInteraction.InitInteract", Sha1 = "64f4db8b38d31fec88b99cfd5bc7991b57940dbb")]
    [PortOf("uc:SBase.SMineProxInteraction:InitInteract", Spec = "spec/equipment/mine-launcher.md#removing-and-refilling")]
    protected internal override void InitInteract(SPlayerController Instigator, int SubInteraction)
    {
        if (Owner is null)
        {
            return;
        }

        Instigator.ReloadMine(Owner);
        Owner.Destroy();
    }

    /// <summary>A message on the user's HUD (server to the user).</summary>
    [Uc("SBase.SMineProxInteraction.LocalMessage", Sha1 = "08dae8574c3748ba6a420c771b841f3f18519b4e")]
    [PortOf("uc:SBase.SMineProxInteraction:LocalMessage", Spec = "spec/equipment/mine-launcher.md#removing-and-refilling")]
    public void LocalMessage(SPlayerController Instigator, string Message, float Time) =>
        GadgetHud.AddMessageToAuxiliaryStack(Instigator, Message, Time);
}
