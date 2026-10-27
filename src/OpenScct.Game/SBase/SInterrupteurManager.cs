using OpenScct.Core.Engine;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// Switches the team that may use a set of switches on an event, written from spec/objectives/switch-hack.md rule 6.
public partial class SInterrupteurManager
{
    [Uc("SBase.SInterrupteurManager.Trigger", Sha1 = "2d324eb5b43825044a2579b94cac1fadef766578")]
    [PortOf("uc:SBase.SInterrupteurManager:Trigger", Spec = "spec/objectives/switch-hack.md#rules")]
    protected override void Trigger(Actor? Other, Pawn? EventInstigator)
    {
        foreach (FInterrupteurValue entry in SInterrupteurList)
        {
            if (entry.Interrupteur?.InteractionObj is { } obj)
            {
                obj.UsableBy = entry.TriggeredUsability;
            }
        }
    }

    [Uc("SBase.SInterrupteurManager.UnTrigger", Sha1 = "037ecbd1bcd7d7dc124be81b1f198b86ae67dc18")]
    [PortOf("uc:SBase.SInterrupteurManager:UnTrigger", Spec = "spec/objectives/switch-hack.md#rules")]
    protected override void UnTrigger(Actor? Other, Pawn? EventInstigator)
    {
        foreach (FInterrupteurValue entry in SInterrupteurList)
        {
            if (entry.Interrupteur?.InteractionObj is { } obj)
            {
                obj.UsableBy = entry.UntriggeredUsability;
            }
        }
    }
}
