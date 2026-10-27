using OpenScct.Core.Engine;

namespace OpenScct.Core;

/// <summary>
/// Physics hook called by the level tick for every actor whose Physics is not PHYS_None (UE2
/// AActor::performPhysics). OpenScct.Physics provides the implementation; set it on <see cref="Level.PhysicsDriver"/>.
/// </summary>
public interface IActorPhysics
{
    void performPhysics(Actor Actor, float DeltaSeconds);
}
