using OpenScct.Core.Uc;

namespace OpenScct.Core.Engine;

/// <summary>
/// Engine natives that need the collision world, for the actor natives OpenScct.Core declares (SetCollision,
/// SetStaticMesh, SetLocation, SetPhysics, a mover's encroaching move, FastTrace). A level gets them as the service
/// <c>Level.GetService&lt;IActorNatives&gt;()</c>; the physics side installs it (OpenScct.Game's DeviceNatives over the
/// OpenScct.Physics driver). Without the service the natives change only the actor's own fields: there is no collision
/// world to update, nothing to encroach on and nothing that blocks a trace.
/// </summary>
public interface IActorNatives
{
    /// <summary>UnrealScript SetCollision: the actor's flags changed; update touches and the collision world.</summary>
    void SetCollision(Actor Actor, bool NewColActors, bool NewBlockActors, bool NewBlockPlayers);

    /// <summary>UnrealScript SetStaticMesh: the actor's mesh changed; its collision follows the new mesh.</summary>
    void SetStaticMesh(Actor Actor, AssetRef NewStaticMesh);

    /// <summary>UnrealScript SetPhysics.</summary>
    void SetPhysics(Actor Actor, EPhysics NewPhysics);

    /// <summary>UnrealScript SetLocation (a teleport that fails when the new spot is blocked).</summary>
    bool SetLocation(Actor Actor, FVector NewLocation);

    /// <summary>
    /// Moves an encroacher (a mover) by <paramref name="Delta"/>: actors it would overlap are pushed by the step, one
    /// still overlapping is passed to the encroacher's EncroachingOn (true refuses the step, which then leaves
    /// everything where it was), otherwise it gets EncroachedBy. Actors based on the encroacher ride with it.
    /// Returns false when the step was refused.
    /// </summary>
    bool MoveEncroacher(Actor Actor, FVector Delta, FRotator NewRotation);

    /// <summary>UnrealScript FastTrace: true when nothing in the world blocks the line.</summary>
    bool FastTrace(FVector End, FVector Start);
}
