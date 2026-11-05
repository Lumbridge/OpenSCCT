using System;
using System.Collections.Generic;
using OpenScct.Core.Engine;

namespace OpenScct.Core.Match;

/// <summary>
/// A level's match-size rules bound to its actors (a level service the map loader installs when the map has
/// <c>sim/match.json</c>). The rules name actors by their map ids; the round start works with the spawned actors.
/// </summary>
public sealed class LevelMatchRules
{
    private readonly Dictionary<Actor, string> _ids = new(ReferenceEqualityComparer.Instance);

    /// <param name="rules">The map's rules.</param>
    /// <param name="actors">Every placed actor of the map with its map id, in level order.</param>
    public LevelMatchRules(MatchRuleSet rules, IEnumerable<(Actor Actor, string Id)> actors)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(actors);
        Rules = rules;
        var gated = new List<(Actor, MatchRule)>();
        foreach ((Actor actor, string id) in actors)
        {
            if (!_ids.TryAdd(actor, id))
            {
                continue;
            }

            if (rules.RuleOf(id) is { } rule)
            {
                gated.Add((actor, rule));
            }
        }

        Gated = gated;
    }

    public MatchRuleSet Rules { get; }

    /// <summary>The actors with a rule, in level order.</summary>
    public IReadOnlyList<(Actor Actor, MatchRule Rule)> Gated { get; }

    /// <summary>The actor's map id; actors spawned after the load get a stand-in that no rule names.</summary>
    public string IdOf(Actor actor) => _ids.TryGetValue(actor, out string? id) ? id : "#" + actor.ActorId;
}
