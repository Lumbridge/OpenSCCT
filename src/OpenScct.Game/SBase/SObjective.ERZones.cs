using OpenScct.Core.Engine;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

public partial class SObjective
{
    /// <summary>
    /// True when a viewer in <paramref name="Viewer"/> sees this objective's triggers directly in Enhanced Reality:
    /// its zone is one of TriggersERVisibilityZones, or its zone number is that of one of the triggers or of the
    /// objective itself (spec/devices/zone-info.md, ER visibility zones).
    /// </summary>
    [UcNative("ASHud::ERDrawObjectives", Exe = 0x10af8c00)]
    [Provenance(ProvenanceKind.Decoded, "game 0x10af92be..0x10af938b: zone list compare, trigger and objective Region.ZoneNumber compare")]
    public bool ERSeesTriggersFrom(FPointRegion Viewer)
    {
        foreach (ZoneInfo? zone in TriggersERVisibilityZones)
        {
            if (zone is not null && ReferenceEquals(zone, Viewer.Zone))
            {
                return true;
            }
        }

        foreach (SObjectiveTrigger? trigger in Triggers)
        {
            if (trigger is not null && trigger.Region.ZoneNumber == Viewer.ZoneNumber)
            {
                return true;
            }
        }

        return Region.ZoneNumber == Viewer.ZoneNumber;
    }

    /// <summary>
    /// The objective's own ER marker target alpha (ObjectiveAndTriggerERState.objtargetvisibility): 255 while not taken
    /// and the viewer does not see the triggers directly, else 0.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "game 0x10af92be: ObjectiveTaken (bit 8 at 0x354) gives 0, else 0xff unless a zone rule matches")]
    public byte ERObjectiveTargetVisibility(FPointRegion Viewer) =>
        !ObjectiveTaken && !ERSeesTriggersFrom(Viewer) ? (byte)255 : (byte)0;

    /// <summary>
    /// A trigger's ER marker target alpha: 255 when the objective marker is off (taken, or the viewer sees the
    /// triggers) or the viewer shares the trigger's zone number, and the trigger is active or locked; else 0.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "game ERDrawObjectives trigger pass: (objective target 0 or same ZoneNumber) and (bActive or bLocked)")]
    public byte ERTriggerTargetVisibility(SObjectiveTrigger Trigger, FPointRegion Viewer)
    {
        System.ArgumentNullException.ThrowIfNull(Trigger);
        bool zoneOk = ERObjectiveTargetVisibility(Viewer) == 0 || Trigger.Region.ZoneNumber == Viewer.ZoneNumber;
        return zoneOk && (Trigger.bActive || Trigger.bLocked) ? (byte)255 : (byte)0;
    }
}
