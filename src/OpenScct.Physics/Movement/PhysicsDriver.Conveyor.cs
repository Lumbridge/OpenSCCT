using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Physics.Collision;
using OpenScct.Physics.Conveyors;
using OpenScct.Provenance;

namespace OpenScct.Physics.Movement;

public sealed partial class PhysicsDriver
{
    /// <summary>
    /// A walking pawn standing on a running conveyor moves with its surface for the time it walked
    /// (spec/additions/conveyors.md#carrying-pawns). Run at the end of physWalking, after the pawn's own move and
    /// floor check, on every machine that simulates the pawn: the server and the owner's prediction and replay take
    /// the same step. The carry is not part of Velocity, as a mover's carry is not, and never turns the pawn.
    /// </summary>
    [Provenance(ProvenanceKind.Estimated, "OpenSCCT addition: spec/additions/conveyors.md#carrying-pawns")]
    private void ConveyorCarry(Pawn p, float deltaTime)
    {
        if (p.XLevel.GetService<ConveyorField>() is not { } field || p.Physics != EPhysics.PHYS_Walking || p.bDeleteMe)
        {
            return;
        }

        FVector feet = p.Location - new FVector(0f, 0f, p.CollisionHeight);
        if (field.FindCarrier(feet, p.CollisionRadius, ConveyorCargo.Pawns, out FVector velocity) is not null)
        {
            CarryOnConveyor(p, velocity * deltaTime);
        }
    }

    /// <summary>
    /// Moves a body standing on a conveyor by <paramref name="delta"/>: up by <see cref="ConveyorK.CarryStep"/>, along
    /// (sliding once along a wall it meets), then back down by what it rose, so joints between conveyors and small
    /// steps do not stop it.
    /// </summary>
    public void CarryOnConveyor(Actor body, FVector delta)
    {
        NotNull(body);
        FVector start = body.Location;
        MoveActor(body, new FVector(0f, 0f, ConveyorK.CarryStep), body.Rotation, out _);
        float rose = body.Location.Z - start.Z;
        if (body.bDeleteMe)
        {
            return;
        }

        MoveActor(body, delta, body.Rotation, out FCheckResult hit);
        if (hit.bHit && !body.bDeleteMe)
        {
            FVector n = new FVector(hit.Normal.X, hit.Normal.Y, 0f).SafeNormal();
            FVector slide = (delta - n * (delta | n)) * (1f - hit.Time);
            if (slide.SizeSquared() > K.Walk.TinyMoveSq)
            {
                MoveActor(body, slide, body.Rotation, out _);
            }
        }

        if (!body.bDeleteMe)
        {
            MoveActor(body, new FVector(0f, 0f, -rose), body.Rotation, out _);
        }
    }

    /// <summary>True when a floor is within <paramref name="depth"/> below the body's collision cylinder.</summary>
    public bool HasFloorBelow(Actor body, float depth)
    {
        NotNull(body);
        Collision.LineCheck(out FCheckResult floor, body, body.Location - new FVector(0f, 0f, depth), body.Location,
            GetCylinderExtent(body), K.Flags.Floor);
        return floor.bHit;
    }
}
