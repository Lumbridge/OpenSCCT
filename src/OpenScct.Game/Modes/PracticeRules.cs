using System.Collections.Generic;
using OpenScct.Core.Engine;

namespace OpenScct.Game.Modes;

/// <summary>
/// The per-player switches of a practice server (spec/reloaded/FEATURES.md rl:net.practice, an OpenSCCT port of a
/// Reloaded server mode, not part of the original game): the players who turned <c>!tank</c> on take no damage. A
/// level service, installed on the server only (damage is decided there); a level without it plays normally.
/// </summary>
public sealed class PracticeRules
{
    private readonly HashSet<Controller> _tanks = new();

    /// <summary>Turns <c>!tank</c> on or off for a player.</summary>
    public void SetTank(Controller controller, bool on)
    {
        if (on)
        {
            _tanks.Add(controller);
        }
        else
        {
            _tanks.Remove(controller);
        }
    }

    /// <summary>Whether a player has <c>!tank</c> on.</summary>
    public bool IsTank(Controller controller) => _tanks.Contains(controller);

    /// <summary>Turns every switch off (practice turned off, or a new round).</summary>
    public void Clear() => _tanks.Clear();

    /// <summary>
    /// True drops a damage call on <paramref name="victim"/>: a tank takes no damage, except his own suicide (so
    /// <c>!kill</c> and resigning still work).
    /// </summary>
    public bool IgnoresDamage(Controller victim, SDamageType damageType) =>
        damageType != SDamageType.Suicided && _tanks.Contains(victim);

    /// <summary>The practice rules of <paramref name="actor"/>'s level, or null when it has none.</summary>
    public static PracticeRules? Of(Actor actor) => actor.XLevel?.GetService<PracticeRules>();
}
