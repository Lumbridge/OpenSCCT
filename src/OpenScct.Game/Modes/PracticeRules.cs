using System.Collections.Generic;
using OpenScct.Core.Engine;

namespace OpenScct.Game.Modes;

/// <summary>
/// The per-player switches of a practice server (spec/reloaded/FEATURES.md rl:net.practice, an OpenSCCT port of a
/// Reloaded server mode, not part of the original game): the players who turned <c>!tank</c> on take no damage, and
/// while <see cref="On"/> nobody runs out of ammunition, gadget charges or gas mask air, and a gear change goes on at
/// once. A level
/// service: the server installs it on each round's level, and a client on its own level from the lobby setup (the
/// ammunition and charges are spent on the player's own machine). A level without it plays normally.
/// </summary>
public sealed class PracticeRules
{
    private readonly HashSet<Controller> _tanks = new();

    /// <summary>
    /// A practice round (OpenSCCT additions): ammunition, gadget charges and gas mask air are not used up, and a
    /// living player's gear change goes on at once.
    /// </summary>
    public bool On { get; set; }

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

    /// <summary>Whether <paramref name="actor"/>'s level is a practice round (<see cref="On"/>).</summary>
    public static bool IsOn(Actor actor) => Of(actor)?.On == true;
}
