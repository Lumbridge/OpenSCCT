using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Physics.Collision;
using OpenScct.Physics.Movement;
using OpenScct.Provenance;

namespace OpenScct.Game.Movement;

/// <summary>
/// The engine natives the movement script calls, over the level's OpenScct.Physics driver (docs/PHYSICS.md, pawn movement
/// API): UnrealScript Trace, collision size changes and placements. Without a driver (a level with no collision
/// world) traces hit nothing and placements go straight through.
/// </summary>
public static class MovementNatives
{
    /// <summary>The level's physics driver, or null.</summary>
    public static PhysicsDriver? Driver(Actor actor) => actor.XLevel is null ? null : PhysicsDriver.Of(actor);

    /// <summary>
    /// UnrealScript <c>Trace(HitLocation, HitNormal, End, Start, bTraceActors, Extent)</c>: the first blocking
    /// hit of a line or box from Start to End, or null. A world hit returns the LevelInfo.
    /// </summary>
    [UcNative("AActor::execTrace", Exe = 0x10a27450, Editor = 0x110acac0)]
    [PortOf("nat:Engine.Actor.Trace", Spec = "spec/movement/native-contracts.md#per-native-contracts")]
    public static Actor? Trace(Actor self, out FVector HitLocation, out FVector HitNormal, FVector End, FVector Start,
        bool bTraceActors, FVector Extent = default)
    {
        uint flags = bTraceActors ? MovementK.TraceScriptActors : MovementK.TraceScriptWorld;
        if (Driver(self) is { } driver && driver.Collision.LineCheck(out FCheckResult hit, self, End, Start, Extent, (ETraceFlags)flags))
        {
            HitLocation = hit.Location;
            HitNormal = hit.Normal;
            return hit.Actor ?? self.Level;
        }

        HitLocation = End;
        HitNormal = default;
        return null;
    }

    /// <summary>True when the actor's box at <paramref name="Location"/> with the given extent overlaps nothing that blocks it.</summary>
    public static bool FitsAt(Actor self, FVector Location, FVector Extent)
    {
        if (Driver(self) is not { } driver)
        {
            return true;
        }

        return !driver.Collision.PointCheck(out _, self, Location, Extent, PhysicsDriver.MoveTraceFlags(self));
    }

    /// <summary>UnrealScript SetCollisionSize.</summary>
    public static void SetCollisionSize(Actor self, float Radius, float Height)
    {
        if (Driver(self) is { } driver)
        {
            driver.SetCollisionSize(self, Radius, Height);
        }
        else
        {
            self.CollisionRadius = Radius;
            self.CollisionHeight = Height;
        }
    }

    /// <summary>The engine's FarMoveActor without the collision test (a placement the caller checked).</summary>
    public static void FarMoveActor(Actor self, FVector NewLocation)
    {
        if (Driver(self) is { } driver)
        {
            driver.FarMoveActor(self, NewLocation, bTest: false, bNoCheck: true, bAttachedMove: true);
        }
        else
        {
            self.Location = NewLocation;
        }
    }

    /// <summary>The engine's setPhysics, or the field when the level has no driver.</summary>
    public static void SetPhysics(Actor self, EPhysics mode)
    {
        if (self.Physics == mode)
        {
            return;
        }

        if (Driver(self) is { } driver)
        {
            driver.setPhysics(self, mode);
        }
        else
        {
            self.Physics = mode;
        }
    }
}
