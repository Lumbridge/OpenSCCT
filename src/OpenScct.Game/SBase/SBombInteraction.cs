using OpenScct.Game.Objectives;

namespace OpenScct.Game.SBase;

public partial class SBombInteraction
{
    // Original PS2 InitInteract ref9060: Owner.Destroy(), then the generic exit.
    protected internal override void InitInteract(SPlayerController instigator, int subInteraction)
    {
        if (Owner is Ps2Modem modem && ObjectiveNatives.IsAlive(instigator) && instigator.Pawn is SPAWNDEFENSE)
            modem.Destroy();
        base.InitInteract(instigator, subInteraction);
    }
}
