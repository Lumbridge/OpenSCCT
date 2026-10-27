using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

/// <summary>
/// SBase.SPresenceTrigger: fires its Event once per entry of a pawn of DetectedClass (the SteeE merc doors), or of
/// a spy carrying a grabbed DetectedClass pawn (spec/devices/triggers.md).
/// </summary>
public partial class SPresenceTrigger
{
    private static readonly Name NAME_s_GrabbingPlayer = new("s_GrabbingPlayer");

    /// <summary>
    /// A DetectedClass toucher fires (UnTrigger when bUntrigger); otherwise, with bDetectGrab, a spy whose controller
    /// is exactly in s_GrabbingPlayer and whose grab target is a DetectedClass pawn fires a Trigger.
    /// </summary>
    [Uc("SBase.SPresenceTrigger.Touch", Sha1 = "a28272d02a47d0ca07d48587b0d01e2024575147")]
    [PortOf("uc:SBase.SPresenceTrigger:Touch", Spec = "spec/devices/triggers.md#spresencetrigger")]
    protected override void Touch(Actor Other)
    {
        if (ClassIsChildOf(Other.GetUcClass(), DetectedClass))
        {
            if (bUntrigger)
            {
                UntriggerEvent(Event, this, Other as Pawn);
            }
            else
            {
                TriggerEvent(Event, this, Other as Pawn);
            }
        }
        else if (bDetectGrab && Other is SPawnAttaque spy && spy.Controller is { } controller &&
                 controller.GetStateName() == NAME_s_GrabbingPlayer &&
                 ClassIsChildOf(spy.m_AttackTarget?.GetUcClass(), DetectedClass))
        {
            TriggerEvent(Event, this, spy);
        }
    }
}
