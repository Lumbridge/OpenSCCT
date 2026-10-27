using OpenScct.Core.Engine;

namespace OpenScct.Core;

/// <summary>
/// The simulated tick of a class that has its own (UE2 AActor::TickSimulated, virtual): on a client, an actor with
/// <c>ROLE_SimulatedProxy</c> asks the level's service before its physics, and a service that handles the actor
/// replaces the stock physics for that tick (as ASBasePawn::TickSimulated does for other players' pawns,
/// spec/native/net-proxy.md rules 10 to 15). Set it with <see cref="Level.SetService{T}"/>.
/// </summary>
public interface ISimulatedTick
{
    /// <summary>True when the actor's movement for this tick was done here (the stock physics is skipped).</summary>
    bool TickSimulated(Actor Actor, float DeltaSeconds);
}
