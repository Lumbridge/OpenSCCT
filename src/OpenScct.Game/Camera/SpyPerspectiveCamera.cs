using System;
using OpenScct.Core;
using OpenScct.Game.SBase;
using OpenScct.Physics.Collision;
using OpenScct.Physics.Movement;

namespace OpenScct.Game.Camera;

/// <summary>The optional spy eye view, constrained by the live body's size and the actual collision world.</summary>
public static class SpyPerspectiveCamera
{
    /// <summary>Conservative default for the renderer's 2 uu near plane. Renderers can supply their frustum radius.</summary>
    public const float DefaultClearance = 8f;

    // Include moving doors and blocking props, but not other players. Do not require bBlockCamera: a player-blocking
    // wall remains a visibility barrier even when its ordinary third-person camera flag is disabled. Queries use
    // no source actor so traversal's temporary bCollideWorld/bBlockActors changes cannot disable their barriers.
    // Pawns (including the owner) are excluded by the mask; ordinary triggers do not satisfy TRACE_Blocking.
    private const ETraceFlags TraceFlags = CameraK.CameraTraceFlags;

    /// <summary>A stable eye inside the current collision body, including immediately after entering a low vent.</summary>
    public static FVector EyeLocation(SPawnAttaque pawn, float clearance = DefaultClearance)
    {
        float height = MathF.Max(0f, pawn.CollisionHeight - clearance);
        return pawn.Location + new FVector(0f, 0f, Math.Clamp(pawn.EyeHeight, -height, height));
    }

    /// <summary>
    /// Checks the final camera after interpolation and look-ahead. The clearance box encloses the near plane at any
    /// rotation. First-person placement stays inside the live collision body; a following view sweeps from a safe
    /// eye. False means that no safe anchor exists (or no collision world is installed), so callers must obscure
    /// the world rather than draw a camera inside solid geometry.
    /// </summary>
    public static bool TryConstrain(SPawnAttaque pawn, FVector desired, out FVector safe,
        float clearance = DefaultClearance, bool withinBody = true)
    {
        safe = pawn.Location;
        if (!float.IsFinite(clearance) || clearance <= 0f ||
            !float.IsFinite(desired.X) || !float.IsFinite(desired.Y) || !float.IsFinite(desired.Z) ||
            pawn.XLevel?.PhysicsDriver is not PhysicsDriver driver) return false;

        ICollisionWorld world = driver.Collision;
        var extent = new FVector(clearance, clearance, clearance);
        if (world.PointCheck(out _, null, safe, extent, TraceFlags)) return false;

        if (withinBody)
        {
            // The square sweep footprint must fit inside the pawn's horizontal cylinder even at a corner.
            float radius = MathF.Max(0f, pawn.CollisionRadius - clearance * MathF.Sqrt(2f));
            float height = MathF.Max(0f, pawn.CollisionHeight - clearance);
            FVector offset = desired - pawn.Location;
            float horizontal = MathF.Sqrt(offset.X * offset.X + offset.Y * offset.Y);
            if (horizontal > radius && horizontal > 0f)
            {
                offset.X *= radius / horizontal;
                offset.Y *= radius / horizontal;
            }
            offset.Z = Math.Clamp(offset.Z, -height, height);
            desired = pawn.Location + offset;
        }
        else
        {
            safe = Sweep(world, safe, EyeLocation(pawn, clearance), extent);
        }

        safe = Sweep(world, safe, desired, extent);
        return !world.PointCheck(out _, null, safe, extent, TraceFlags);
    }

    private static FVector Sweep(ICollisionWorld world, FVector start, FVector end, FVector extent)
    {
        if (start == end || !world.LineCheck(out FCheckResult hit, null, end, start, extent, TraceFlags)) return end;
        FVector delta = end - start;
        float distance = delta.Size();
        // Pull back slightly from contact even for collision providers whose hit time has no built-in margin.
        float fraction = Math.Clamp(hit.Time - (distance > 0f ? 0.1f / distance : 0f), 0f, 1f);
        return start + delta * fraction;
    }
}
