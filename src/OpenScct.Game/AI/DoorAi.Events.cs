using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;

namespace OpenScct.Game.AI;

public sealed partial class DoorAi
{
    private bool BlocksCrossing(SBasePawn pawn) =>
        _bot.World.Collision.LineCheck(out _, pawn, _crossTo, _crossFrom, default, MoverFlags);

    private bool SwitchReachesDoor(SInterrupteur button, Mover door)
    {
        bool trigger = button.TriggerMethode switch
        {
            TriggerType.UnTriggerOnly => false,
            TriggerType.TriggerControl => button.TriggerInteraction is not { bIsActive: true },
            _ => true,
        };
        return EventReachesDoor(button.Event, trigger, button, door, new HashSet<(Name, bool)>());
    }

    /// <summary>
    /// Follow only events the relay would emit on this activation. Exhausted groups, later sequence steps and
    /// untrigger-only inputs must not make an unrelated button look like an escape. Cycles terminate here.
    /// </summary>
    private bool EventReachesDoor(Name name, bool trigger, Actor? sender, Mover door, HashSet<(Name, bool)> visited)
    {
        if (name == Name.None || !visited.Add((name, trigger)))
        {
            return false;
        }

        if (name == door.Tag && trigger)
        {
            return true;
        }

        foreach (SMagicEvent relay in _relays)
        {
            if (relay.bDeleteMe || relay.Tag != name || ReferenceEquals(sender, relay.StopActor))
            {
                continue;
            }

            foreach (FEventGroup group in relay.Groups)
            {
                if (group.Repeat < 0)
                {
                    continue;
                }

                for (int i = 0; i < group.EventGroup.Count; i++)
                {
                    if (group.Sequence && i != group.SequenceIndex)
                    {
                        continue;
                    }

                    FSuperEvent entry = group.EventGroup[i];
                    if (entry.ValidOn == EEventType.EVT_Trigger && !trigger ||
                        entry.ValidOn == EEventType.EVT_Untrigger && trigger)
                    {
                        continue;
                    }

                    if (EventReachesDoor(entry.Event, SendsTrigger(entry), sender, door, visited))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static bool SendsTrigger(FSuperEvent entry) =>
        entry.Type is EEventType.EVT_Trigger or EEventType.EVT_TriggerUntrigger;

    /// <summary>
    /// An accepted press may still be in a switch timer, a delayed relay queue, or mover motion. Read the pending
    /// work instead of guessing a retry delay: pressing Aquarius's button twice schedules two opposite toggles.
    /// Also sees work queued by teammates before this bot arrived.
    /// </summary>
    private bool PendingOpening(Mover door) => door.bInterpolating || door.bDelaying || PendingDoorEvent(door);

    private bool PendingDoorEvent(Mover door)
    {
        Gather();
        foreach (SInterrupteur button in _switches)
        {
            if (!button.bDeleteMe && button.TriggerInteraction is { } entry &&
                entry.IsInState(new Name("WaitingBeforeActivation")) && SwitchReachesDoor(button, door))
            {
                return true;
            }
        }

        foreach (SMagicEvent relay in _relays)
        {
            if (relay.bDeleteMe)
            {
                continue;
            }

            foreach (FWaitingEvent waiting in relay.EventQueue)
            {
                if (EventReachesDoor(waiting.Event.Event, SendsTrigger(waiting.Event), waiting.Other, door,
                    new HashSet<(Name, bool)>()))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
