using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using K = OpenScct.Game.GameFlow.K;

namespace OpenScct.Game.SBase;

/// <summary>Where one player's first pawn spawns in a test play from the map editor (spec/additions/test-start.md).</summary>
/// <param name="Player">The player it is for: the local player of an offline play.</param>
/// <param name="Location">The spot, as the editor's camera stood.</param>
/// <param name="Rotation">The view the player starts with (the pawn keeps only the yaw).</param>
public sealed record TestStart(Controller Player, FVector Location, FRotator Rotation);

// OpenSCCT's start from the editor's camera (spec/additions/test-start.md).
public partial class SGameInfo
{
    /// <summary>
    /// A start the offline play session sets for its local player after login, or null. It is used once: the next
    /// spawn of that player happens at it, and later spawns choose a PlayerStart as usual.
    /// </summary>
    public TestStart? PendingTestStart { get; set; }

    /// <summary>
    /// Spawns <paramref name="player"/>'s pawn at its <see cref="PendingTestStart"/>, with the same floor probe as a
    /// PlayerStart. Null when it has none, or when the spot is blocked (then it spawns at a start as usual).
    /// </summary>
    private Pawn? SpawnAtTestStart(Controller player, out FRotator rotation)
    {
        rotation = default;
        if (PendingTestStart is not { } start || !ReferenceEquals(start.Player, player))
        {
            return null;
        }

        PendingTestStart = null;
        FVector spawnAt = start.Location;
        if (TraceFloor(spawnAt, spawnAt - new FVector(0f, 0f, K.FloorProbeLength), out FVector hitLocation))
        {
            spawnAt.Z = hitLocation.Z + K.SpawnHeightAboveHit;
        }

        rotation = start.Rotation;
        var upright = new FRotator(0, start.Rotation.Yaw, 0);
        return UcSpawn.Spawn(this, player.PawnClass, null, spawnAt, upright) as Pawn;
    }
}
