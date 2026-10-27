using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Devices;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

/// <summary>
/// SBase.SMagicEvent: a relay that fires groups of named events, each after its own delay and with its own
/// Trigger/UnTrigger type (spec/devices/magic-event.md). The group logic and the delay queue are native.
/// </summary>
public partial class SMagicEvent
{
    /// <summary>Resets the sequence steps, disables empty groups and starts the 0.05 s queue update.</summary>
    [Uc("SBase.SMagicEvent.BeginPlay", Sha1 = "7f134de81f17ae8334eeb3d8628bd7f0bfc98bec")]
    [PortOf("uc:SBase.SMagicEvent:BeginPlay", Spec = "spec/devices/magic-event.md#rules")]
    protected override void BeginPlay()
    {
        for (i = 0; i < Groups.Count; i++)
        {
            FEventGroup group = Groups[i];
            group.SequenceIndex = 0;
            if (group.EventGroup.Count == 0)
            {
                group.Repeat = -1;
            }

            Groups[i] = group;
        }

        SetTimer(DeviceK.MagicEventTimer, true);
    }

    [Uc("SBase.SMagicEvent.Trigger", Sha1 = "a48cd06250d4c7470df73a20c12d6ab941ff3316")]
    [PortOf("uc:SBase.SMagicEvent:Trigger", Spec = "spec/devices/magic-event.md#rules")]
    protected override void Trigger(Actor? Other, Pawn? EventInstigator) => NativeTrigger(Other, EventInstigator, true);

    [Uc("SBase.SMagicEvent.UnTrigger", Sha1 = "2436396cd3c025d6918e89437b788618da6e0cdb")]
    [PortOf("uc:SBase.SMagicEvent:UnTrigger", Spec = "spec/devices/magic-event.md#rules")]
    protected override void UnTrigger(Actor? Other, Pawn? EventInstigator) => NativeTrigger(Other, EventInstigator, false);

    /// <summary>
    /// One activation: a trigger from StopActor clears the queue; then every enabled group answers (all its entries,
    /// or the current step of a sequence) with each entry whose ValidOn matches, at once or queued at its delay
    /// from now; cycles count down Repeat.
    /// </summary>
    [UcNative("ASMagicEvent::execNativeTrigger", Exe = 0x10ae9cf0, Editor = 0x111d30f0)]
    [PortOf("nat:SBase.SMagicEvent.NativeTrigger", Spec = "spec/devices/magic-event.md#rules")]
    [Provenance(ProvenanceKind.Decoded, "Editor twin NativeTrigger semantics (magic-event.md rule 3), game build to confirm")]
    public void NativeTrigger(Actor? Other, Pawn? EventInstigator, bool bTrigger)
    {
        if (StopActor is not null && ReferenceEquals(Other, StopActor))
        {
            EventQueue.Clear();
        }

        for (int g = 0; g < Groups.Count; g++)
        {
            FEventGroup group = Groups[g];
            if (group.Repeat <= -1)
            {
                continue;
            }

            bool cycleDone;
            if (!group.Sequence)
            {
                for (int e = 0; e < group.EventGroup.Count; e++)
                {
                    FSuperEvent entry = group.EventGroup[e];
                    if (Answers(entry.ValidOn, bTrigger))
                    {
                        Activate(ref entry, Other, EventInstigator);
                        group.EventGroup[e] = entry;
                    }
                }

                cycleDone = true;
            }
            else
            {
                cycleDone = false;
                if (group.SequenceIndex < group.EventGroup.Count)
                {
                    FSuperEvent entry = group.EventGroup[group.SequenceIndex];
                    if (Answers(entry.ValidOn, bTrigger))
                    {
                        Activate(ref entry, Other, EventInstigator);
                        group.EventGroup[group.SequenceIndex] = entry;
                        group.SequenceIndex++;
                        if (group.SequenceIndex >= group.EventGroup.Count)
                        {
                            group.SequenceIndex = 0;
                            cycleDone = true;
                        }
                    }
                }
            }

            if (cycleDone && group.Repeat > 0)
            {
                group.Repeat--;
                if (group.Repeat == 0)
                {
                    group.Repeat = -1;
                }
            }

            Groups[g] = group;
        }
    }

    /// <summary>EVT_Trigger answers triggers, EVT_Untrigger untriggers, the combined types both.</summary>
    private static bool Answers(EEventType validOn, bool bTrigger) => validOn switch
    {
        EEventType.EVT_Trigger => bTrigger,
        EEventType.EVT_Untrigger => !bTrigger,
        _ => true,
    };

    /// <summary>Dispatches an entry now (Delay 0) or queues a copy at the front, due Delay seconds from now.</summary>
    private void Activate(ref FSuperEvent entry, Actor? other, Pawn? instigator)
    {
        if (entry.Delay == 0f)
        {
            Dispatch(ref entry, other, instigator);
            return;
        }

        EventQueue.Insert(0, new FWaitingEvent
        {
            Event = entry,
            Other = other,
            EventInstigator = instigator,
            Time = Level.TimeSeconds + entry.Delay,
        });
    }

    /// <summary>Fires an entry by its type; the alternating types flip on the entry they are fired from.</summary>
    private void Dispatch(ref FSuperEvent entry, Actor? other, Pawn? instigator)
    {
        switch (entry.Type)
        {
            case EEventType.EVT_Trigger:
                TriggerEvent(entry.Event, other, instigator);
                break;
            case EEventType.EVT_Untrigger:
                UntriggerEvent(entry.Event, other, instigator);
                break;
            case EEventType.EVT_TriggerUntrigger:
                TriggerEvent(entry.Event, other, instigator);
                entry.Type = EEventType.EVT_UntriggerTrigger;
                break;
            case EEventType.EVT_UntriggerTrigger:
                UntriggerEvent(entry.Event, other, instigator);
                entry.Type = EEventType.EVT_TriggerUntrigger;
                break;
        }
    }

    /// <summary>
    /// The native queue update (editor twin 0x111d2ff0): every queued entry that is due, front to back, is removed
    /// and dispatched (the copy's type change is discarded with it). Entries queued by those dispatches wait for a
    /// later update.
    /// </summary>
    [PortOf("nat:SBase.SMagicEvent.NativeTrigger", Spec = "spec/devices/magic-event.md#rules")]
    [Provenance(ProvenanceKind.Estimated, "Queue runs on the 0.05 s timer (magic-event.md open question 1)")]
    protected override void Timer()
    {
        if (EventQueue.Count == 0)
        {
            return;
        }

        float now = Level.TimeSeconds;
        int due = 0;
        for (int q = 0; q < EventQueue.Count; q++)
        {
            if (EventQueue[q].Time <= now)
            {
                due++;
            }
        }

        if (due == 0)
        {
            return;
        }

        var ready = new FWaitingEvent[due];
        int n = 0;
        for (int q = 0; q < EventQueue.Count;)
        {
            if (EventQueue[q].Time <= now)
            {
                ready[n++] = EventQueue[q];
                EventQueue.RemoveAt(q);
            }
            else
            {
                q++;
            }
        }

        for (int r = 0; r < ready.Length; r++)
        {
            FSuperEvent entry = ready[r].Event;
            Dispatch(ref entry, ready[r].Other, ready[r].EventInstigator);
        }
    }

    protected override void CleanupDestroyedReferences()
    {
        base.CleanupDestroyedReferences();
        StopActor = Live(StopActor);
    }
}
