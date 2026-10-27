using OpenScct.Core.Engine;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.STriggerPlayerStart: a start that events switch on and off. Written from spec/game/spawn-selection.md.
public partial class STriggerPlayerStart
{
    [Uc("SBase.STriggerPlayerStart.Trigger", Sha1 = "9927f5a6b68b2db75615d04cf33b7ec128d3710e")]
    [PortOf("uc:SBase.STriggerPlayerStart:Trigger", Spec = "spec/game/spawn-selection.md#data")]
    protected override void Trigger(Actor? Other, Pawn? EventInstigator)
    {
        bEnabled = true;
    }

    [Uc("SBase.STriggerPlayerStart.UnTrigger", Sha1 = "9cb3dc52f49d78d0d15d1564918de5bc416ce021")]
    [PortOf("uc:SBase.STriggerPlayerStart:UnTrigger", Spec = "spec/game/spawn-selection.md#data")]
    protected override void UnTrigger(Actor? Other, Pawn? EventInstigator)
    {
        bEnabled = false;
    }
}
