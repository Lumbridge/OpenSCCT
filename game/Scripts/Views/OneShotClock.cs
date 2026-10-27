using OpenScct.Core;

namespace OpenScctGame.Views;

/// <summary>
/// The time left in a view model one-shot at the drawn moment between two client ticks (Godot-free, so the logic tests
/// run it). The simulation counts a one-shot down once per tick; the screen draws between the last two ticks, so the
/// clip is drawn at the same point in time as the camera instead of holding a pose for a whole tick.
/// </summary>
public static class OneShotClock
{
    /// <summary>
    /// The seconds left at <paramref name="alpha"/> of the way from the previous tick to the current one.
    /// A clip that started at the current tick (another sequence, or the same one restarted) is drawn from its start
    /// (<paramref name="full"/> seconds left); with no previous sample the current value is used as it is.
    /// </summary>
    public static float Remaining(bool hasPrevious, Name previousSequence, float previousRemaining, Name sequence, float remaining, float full, float alpha)
    {
        float t = alpha < 0f ? 0f : alpha > 1f ? 1f : alpha;
        if (!hasPrevious)
        {
            return remaining;
        }

        if (previousSequence != sequence || remaining > previousRemaining + 1e-4f)
        {
            float start = full > remaining ? full : remaining;
            return start + ((remaining - start) * t);
        }

        return previousRemaining + ((remaining - previousRemaining) * t);
    }
}
