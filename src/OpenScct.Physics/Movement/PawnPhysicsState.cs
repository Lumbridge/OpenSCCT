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
    /// <summary>The mover that carries the GE this pawn holds (a GE attached to a mover). Null for every GE that does not move.</summary>
    public OpenScct.Core.Engine.Actor? TraversalBase;

    /// <summary>
    /// OpenSCCT: the horizontal speed of the mover this pawn stood on or hung from when it began to fall. physFalling
    /// moves the pawn by it on top of its own motion, so a jump on a moving car comes down where it left and a drop
    /// from the car's side stays beside the car. Zero once the fall ends or the carried move is blocked.
    /// </summary>
    public OpenScct.Core.FVector AirCarry;

    /// <summary>Server, a client's pawn: true once a move of the client has said what its positions are told in.</summary>
    public bool ClientFrameTold;

    /// <summary>Server, a client's pawn: the mover the client's last move was told in (PhysicsDriver.SetClientFrame), or null for world positions.</summary>
    public OpenScct.Core.Engine.Actor? ClientFrame;
    /// <summary>Highest Z of the current airborne phase when the controller has no fZMaxFalling of its own.</summary>
    public float fZMaxFalling;

    /// <summary>Set while below KillZ, so FellOutOfWorld is raised once per fall.</summary>
    public bool bBelowKillZ;
}
