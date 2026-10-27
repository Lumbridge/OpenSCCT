using OpenScct.Provenance;

namespace OpenScct.Core.Engine;

/// <summary>Engine.AnimNotify_Trigger: fires an event from the animated actor (spec/devices/animated-mesh.md).</summary>
public partial class UAnimNotify_Trigger
{
    /// <summary>
    /// The owner fires EventName with itself as the sender; the instigator is the owner when it is a pawn. The
    /// dispatch is not simulated, so on a client's proxy of the owner it is absorbed (events.md rule 7).
    /// </summary>
    [Uc("Engine.AnimNotify_Trigger.Notify", Sha1 = "15b4bf1c561e1ac94c6a35467f5b646fbeaf8820")]
    [PortOf("uc:Engine.AnimNotify_Trigger:Notify", Spec = "spec/devices/animated-mesh.md#rules")]
    public override void Notify(Actor Owner)
    {
        if (Owner.Role < ENetRole.ROLE_AutonomousProxy)
        {
            return;
        }

        Owner.TriggerEvent(EventName, Owner, Owner as Pawn);
    }
}
