using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

/// <summary>SBase.SProjoInteraction: the searchlight's "Use Mirador" entry for mercs (spec/devices/projector.md).</summary>
public partial class SProjoInteraction
{
    /// <summary>Marks the interaction in use (the projector lights from it) and puts the merc behind the light.</summary>
    [Uc("SBase.SProjoInteraction.InitInteract", Sha1 = "6284034c61d9d5624e7b2f08f5954bc314c322dd")]
    [PortOf("uc:SBase.SProjoInteraction:InitInteract", Spec = "spec/devices/projector.md#manning-it")]
    protected internal override void InitInteract(SPlayerController Instigator, int SubInteraction)
    {
        bInUse = true;
        Instigator.UseMirador();
    }
}
