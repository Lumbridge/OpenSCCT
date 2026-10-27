using OpenScct.Core.Engine;

namespace OpenScct.Game.Modes;

/// <summary>
/// Rules a host can change for one level, which the ported code asks at a few gates. An OpenSCCT addition, not part
/// of the original: the hub mode (spec/additions/hub.md) turns damage and pawn-on-pawn attacks off. A level without
/// the service plays by <see cref="Original"/>, which changes nothing. Install the same rules on the server and on
/// every client of the level, so a client does not start an attack its server refuses.
/// </summary>
public sealed record PlayRules
{
    /// <summary>The original game's rules.</summary>
    public static readonly PlayRules Original = new();

    /// <summary>
    /// The hub's rules: no damage, no grabs, no melee knockdowns, no drop attacks; the aim range's pop-up targets
    /// count the rifle's hits.
    /// </summary>
    public static readonly PlayRules Hub = new() { PawnsTakeDamage = false, PawnsAttackPawns = false, RangeTargetsTakeHits = true };

    /// <summary>False drops every damage call on the server (PawnTakeDamage) before any reaction.</summary>
    public bool PawnsTakeDamage { get; init; } = true;

    /// <summary>False makes every pawn refuse a grab, an elbow or melee shock and a drop attack.</summary>
    public bool PawnsAttackPawns { get; init; } = true;

    /// <summary>
    /// True lets the hub's aim range targets (not pawns, not level actors) register a bullet as a hit. A hit on a
    /// target never reaches a pawn, so it leaves <see cref="PawnsTakeDamage"/> as it is.
    /// </summary>
    public bool RangeTargetsTakeHits { get; init; }

    /// <summary>The rules of <paramref name="actor"/>'s level.</summary>
    public static PlayRules Of(Actor actor) => actor.XLevel?.GetService<PlayRules>() ?? Original;
}
