using System;
using System.Collections.Generic;
using OpenScct.Game.SBase;

namespace OpenScct.Bots;

/// <summary>
/// The SteeE Story 2v2 bot scripts and the round end each one leads to:
/// <list type="bullet">
/// <item><c>spies</c>: the spies split the terminals and hack them while the mercs patrol the terminals without
/// firing: the spies win on objectives (END_STORY).</item>
/// <item><c>mercs</c>: the spies walk to the mercs' starts again and again while the mercs hold their ground and shoot
/// on sight: the mercs win when the spies are out of lives (END_TEAMDEATH).</item>
/// <item><c>timeout</c>: both sides stay home: the mercs win when the clock runs out (END_TIMEOUT).</item>
/// </list>
/// </summary>
public static class StoryScripts
{
    public static readonly string[] Names = ["spies", "mercs", "timeout"];

    /// <summary>The four bots of <paramref name="script"/>.</summary>
    public static IReadOnlyList<BotSpec> Bots(string script) => script switch
    {
        "spies" => BotMatchSettings.TwoVersusTwo(i => new SpyBrain(SpyScript.HackShare(i)), i => new MercBrain(MercScript.Guard(i, holdFire: true))),
        "mercs" => BotMatchSettings.TwoVersusTwo(i => new SpyRaidBrain(i), _ => new MercBrain(MercScript.HoldGround)),
        "timeout" => BotMatchSettings.TwoVersusTwo(_ => new SpyBrain(SpyScript.StayHome), _ => new MercBrain(MercScript.StayHome)),
        _ => throw new ArgumentOutOfRangeException(nameof(script), script, "Unknown bot script."),
    };

    /// <summary>The winning team and reason <paramref name="script"/> leads to.</summary>
    public static (int Team, EEndReason Reason) Expected(string script) => script switch
    {
        "spies" => (0, EEndReason.END_STORY),
        "mercs" => (1, EEndReason.END_TEAMDEATH),
        "timeout" => (1, EEndReason.END_TIMEOUT),
        _ => throw new ArgumentOutOfRangeException(nameof(script), script, "Unknown bot script."),
    };
}
