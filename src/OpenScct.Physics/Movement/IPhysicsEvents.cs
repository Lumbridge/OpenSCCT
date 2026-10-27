using OpenScct.Core;
using OpenScct.Core.Engine;

namespace OpenScct.Physics.Movement;

/// <summary>
/// Script events the native movement raises on a pawn's controller (SPlayerController in SCCT). The physics looks
/// for this interface on the pawn's Controller, then on the pawn itself; without it the stock Actor events are
/// used (Falling, Landed, HitWall, Bump). Events run immediately, so a state or physics change made in one takes
/// effect for the rest of the frame (spec/movement/native-contracts.md, "Events the natives raise").
/// </summary>
public interface IPhysicsEvents
{
    /// <summary>The walking pawn lost its floor. <paramref name="LeftTime"/> is the frame time left.</summary>
    void eventMyFalling(float LeftTime);

    /// <summary>The falling pawn touched a walkable floor. True when handled.</summary>
    bool eventMyNotifyLanded(FVector HitNormal, float fTimeLeft, Actor? LandedOn);

    /// <summary>The pawn ran into a wall. True stops the default handling (the pawn's HitWall event).</summary>
    bool eventNotifyHitWall(FVector HitNormal, Actor? Wall);

    /// <summary>The pawn touched a blocking actor. True stops the default handling (the pawn's Bump event).</summary>
    bool eventNotifyBump(Actor Other);

    /// <summary>The pawn passed below the world (KillZ).</summary>
    void eventFellOutOfWorld();

    /// <summary>The falling pawn came down on another pawn.</summary>
    void eventDropAttack(Pawn Target);
}

/// <summary>Implemented by pawn ports that keep their own <see cref="PawnPhysicsState"/> (no lookup).</summary>
public interface IPawnPhysicsStateOwner
{
    PawnPhysicsState PhysicsState { get; }
}

/// <summary>
/// Optional actor-side encroachment events (UE2 EncroachingOn / EncroachedBy). A mover port implements
/// <see cref="eventEncroachingOn"/> with its MoverEncroachType rules; without it a blocked mover stops.
/// </summary>
public interface IEncroacher
{
    /// <summary>The encroacher's step would leave it overlapping <paramref name="Other"/>. True refuses the step.</summary>
    bool eventEncroachingOn(Actor Other);
}

/// <summary>Optional: told when an encroacher's step went ahead while overlapping it.</summary>
public interface IEncroachable
{
    void eventEncroachedBy(Actor Other);
}

/// <summary>A physics mode implementation that replaces or adds to the built-in ones (traversal modes, decoded natives).</summary>
public interface IPhysicsModeHandler
{
    void performPhysics(PhysicsDriver Driver, Actor Actor, float DeltaSeconds);
}
