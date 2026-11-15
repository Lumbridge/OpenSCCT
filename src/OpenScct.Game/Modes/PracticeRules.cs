using System.Collections.Generic;
using OpenScct.Core.Engine;

namespace OpenScct.Game.Modes;

/// <summary>
/// The per-player switches of a practice server (spec/reloaded/FEATURES.md rl:net.practice, an OpenSCCT port of a
/// Reloaded server mode, not part of the original game): the players who turned <c>!tank</c> on take no damage, and
/// while <see cref="On"/> a gear change goes on at once. Infinite ammunition, gadget charges and mask air are enabled
/// initially and can be switched with <c>!infinite</c>. A level
/// service: the server installs it on each round's level, and a client on its own level from the lobby setup (the
/// ammunition and charges are spent on the player's own machine). A level without it plays normally.
/// </summary>
public sealed class PracticeRules
{
    private readonly HashSet<Controller> _tanks = new();
    private GameReplicationInfo? _resourceInfo;

    /// <summary>
    /// A practice round (OpenSCCT additions): a living player's gear change goes on at once.
    /// </summary>
    public bool On { get; set; }

    /// <summary>The initial resource rule, before the round's replicated game rules exist.</summary>
    public bool InfiniteResources { get; set; } = true;

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

    /// <summary>Clears player switches and restores the new-round resource default.</summary>
    public void Clear()
    {
        _tanks.Clear();
        InfiniteResources = true;
        _resourceInfo = null;
    }

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

    /// <summary>Ammo, gadget charges and mask air are free only while this practice switch is enabled.</summary>
    public static bool ResourcesInfinite(Actor actor)
    {
        if (Of(actor) is not { On: true } rules) return false;
        // A client can receive the GRI actor before LevelInfo's reference to it.
        // Use that live replica as soon as it arrives, including late joiners.
        if (actor.XLevel?.Info.GRI is { bDeleteMe: false } linked) rules._resourceInfo = linked;
        if (rules._resourceInfo is null || rules._resourceInfo.bDeleteMe)
        {
            rules._resourceInfo = null;
            if (actor.XLevel is { } level)
                foreach (GameReplicationInfo info in level.AllActors<GameReplicationInfo>())
                    if (!info.bDeleteMe) { rules._resourceInfo = info; break; }
        }
        return rules._resourceInfo is { } gri ? !gri.PracticeFiniteResources : rules.InfiniteResources;
    }
}
