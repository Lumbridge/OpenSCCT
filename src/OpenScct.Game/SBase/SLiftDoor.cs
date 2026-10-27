using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

/// <summary>
/// SBase.SLiftDoor: a cabin door of an SLift, written from spec/devices/lift.md. A TriggerToggle mover the lift
/// bases on itself and toggles; its native replication also sends BasePos and Base so clients carry it with the cabin.
/// </summary>
public partial class SLiftDoor
{
    /// <summary>
    /// At rest the door ignores what it overlaps. While moving it runs the Mover reaction (damage, the encroach type)
    /// but, as the script returns nothing, the step is never refused.
    /// </summary>
    [Uc("SBase.SLiftDoor.EncroachingOn", Sha1 = "c4c841c35c3e9b5eec844b61a53492d9586b5291")]
    [PortOf("uc:SBase.SLiftDoor:EncroachingOn", Spec = "spec/devices/lift.md#rules")]
    protected override bool EncroachingOn(Actor? Other)
    {
        // Not simulated: on a client the engine's call returns false (mover.md, Network).
        if (Role < ENetRole.ROLE_Authority || KeyNum == PrevKeyNum)
        {
            return false;
        }

        base.EncroachingOn(Other);
        return false;
    }
}
