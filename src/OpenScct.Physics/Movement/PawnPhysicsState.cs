namespace OpenScct.Physics.Movement;

/// <summary>
/// Per-pawn physics bookkeeping the driver keeps for pawns whose controller is not an
/// <see cref="ISPlayerControllerPhysics"/>: the fall apex (an SPlayerController keeps it in fZMaxFalling) and the
/// KillZ latch. The walking values the natives read live on the generated Core fields (Pawn.Floor,
/// Pawn.bCanWalkOffLedges); the step height, floor slope and friction are the decoded constants in <see cref="K"/>.
/// Pawn ports may implement <see cref="IPawnPhysicsStateOwner"/> to own it.
/// </summary>
public sealed class PawnPhysicsState
{
    /// <summary>Highest Z of the current airborne phase when the controller has no fZMaxFalling of its own.</summary>
    public float fZMaxFalling;

    /// <summary>Set while below KillZ, so FellOutOfWorld is raised once per fall.</summary>
    public bool bBelowKillZ;
}
