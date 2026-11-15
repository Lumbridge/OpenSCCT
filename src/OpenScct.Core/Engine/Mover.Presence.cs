using System.Collections.Generic;

namespace OpenScct.Core.Engine;

public partial class Mover
{
    private List<Actor>? _presenceTriggers;

    private void RememberPresenceTrigger(Actor? source)
    {
        if (Role != ENetRole.ROLE_Authority || source is not IPresenceTrigger { IsPresenceActive: true }) return;
        _presenceTriggers ??= new List<Actor>();
        if (!_presenceTriggers.Contains(source)) _presenceTriggers.Add(source);
    }

    /// <summary>
    /// Authority only: every detector that has triggered this door participates, including entries received
    /// while opening. Clients only interpolate the server's legs; they never decide whether a door may close.
    /// </summary>
    private bool HasActivePresence()
    {
        if (Role != ENetRole.ROLE_Authority || _presenceTriggers is null) return false;
        bool active = false;
        for (int i = _presenceTriggers.Count - 1; i >= 0; i--)
        {
            Actor source = _presenceTriggers[i];
            if (source.bDeleteMe)
            {
                _presenceTriggers.RemoveAt(i);
            }
            else if (source is IPresenceTrigger { IsPresenceActive: true })
            {
                active = true;
            }
        }

        return active;
    }

    private bool PresenceCanReverse(Actor? source)
    {
        if (Role != ENetRole.ROLE_Authority || CurrentKey >= PreviousKey ||
            source is not IPresenceTrigger { IsPresenceActive: true }) return false;

        // Some maps use Toggle for switches but Timed for this detector on the very same door.
        Name state = InitialState;
        foreach (FSpecialBehavior behavior in SpecialBehaviors)
        {
            if (!ReferenceEquals(source, behavior.EventFromActor)) continue;
            state = behavior.InitialState;
            break;
        }

        return state == nameof(TriggerOpenTimed) || state == nameof(TriggerControl);
    }

    private bool WaitForPresenceBeforeControlClose()
    {
        if (!HasActivePresence()) return false;
        if (CurrentKey < PreviousKey)
        {
            // A relay release may arrive after re-entry but before the new opening code has run.
            GotoState(nameof(TriggerControl), NAME_Open);
        }
        else
        {
            Sleep(0f);
        }

        return true;
    }
}
