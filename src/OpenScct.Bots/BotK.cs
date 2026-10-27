using OpenScct.Provenance;

namespace OpenScct.Bots;

/// <summary>Tuning of the bots. These are choices of the test bots, not values of the original.</summary>
public static class BotK
{
    /// <summary>
    /// Longest drop a spy plans: a little over the spy's fall damage height (Fell_MinHeightForDmg 600, full damage at
    /// Fell_MaxHeightForDmg 1500), so a 700 uu drop costs at most about 11 hp. SteeE's spies leave their roof this way.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "SPawnAttaque defaults Fell_MinHeightForDmg 600, Fell_MaxHeightForDmg 1500")]
    public const float SpyMaxDrop = 700f;

    /// <summary>A spy bot walks crouched (its start stance): the walk graph uses the crouched cylinder.</summary>
    [Provenance(ProvenanceKind.Decoded, "SBasePawn defaults CrouchHeight 50, CollisionRadius 35")]
    public const float SpyHeight = 50f;

    /// <summary>Longest drop a merc plans, under the merc's fall damage height (Fell_MinHeightForDmg 450).</summary>
    [Provenance(ProvenanceKind.Decoded, "SPAWNDEFENSE defaults Fell_MinHeightForDmg 450; the bot keeps 50 uu under it")]
    public const float MercMaxDrop = 400f;

    /// <summary>A waypoint closer than this (horizontally) may be passed when the next one is in a clear line.</summary>
    public const float WaypointReach = 36f;

    /// <summary>A waypoint closer than this (horizontally) is passed in any case.</summary>
    public const float WaypointArrive = 12f;

    /// <summary>Seconds without 24 uu of progress before the bot plans again from where it stands.</summary>
    public const float StuckSeconds = 1.5f;

    /// <summary>A goal closer than this (horizontally) is reached.</summary>
    public const float GoalReach = 20f;

    /// <summary>A walker strafes right past a pawn ahead of it that is this close to touching it.</summary>
    public const float PassingClearance = 50f;

    /// <summary>Seconds a walker held up by another pawn backs away from it (BotNavigator gives way).</summary>
    public const float GiveWaySeconds = 1f;

    /// <summary>Shortest and longest wait after backing away, drawn per bot each time, before it goes on.</summary>
    public const float GiveWayWaitMin = 0.3f;

    public const float GiveWayWaitMax = 1.8f;

    /// <summary>A direction to back away in must be clear of geometry this far (uu).</summary>
    public const float GiveWayProbe = 96f;

    /// <summary>Range at which a merc engages a spy it can see.</summary>
    public const float MercEngageRange = 2000f;

    /// <summary>
    /// A merc fires at a walking spy only this close; farther away it waits for the spy to stop. The rifle's
    /// ammunition (125 rounds and a 25 round clip) lasts only a few minutes of firing at moving targets.
    /// </summary>
    public const float MercMovingTargetRange = 600f;

    /// <summary>A spy slower than this (uu/s, on the merc's machine) counts as standing still.</summary>
    public const float StillSpeed = 40f;

    /// <summary>Seconds a raiding spy stands at each stop before the next leg.</summary>
    public const float RaidLoiterSeconds = 6f;

    /// <summary>
    /// Seconds a merc holds the trigger per burst, then pauses (auto fire): short bursts keep the spread low (it grows
    /// with every bullet in a row).
    /// </summary>
    public const float MercBurstSeconds = 0.3f;

    public const float MercBurstPause = 0.35f;

    /// <summary>A merc holds fire when a teammate stands this close to the line of fire.</summary>
    public const float FriendlyClearance = 60f;

    /// <summary>A merc aims this far below a spy's centre (a crouched spy's chest).</summary>
    public const float MercAimDrop = 20f;

    /// <summary>A merc's guard post near a terminal is this far from its use spot (it must not stand on the spot).</summary>
    public const float GuardMinDistance = 150f;

    public const float GuardMaxDistance = 300f;

    /// <summary>Eye height above the pawn's centre used for the line of sight test.</summary>
    public const float EyeHeight = 60f;
}
