using System;
using Godot;
using OpenScct.Core;

namespace OpenScctGame.Smoke;

/// <summary>
/// Checks of <see cref="UeSpace"/>: axis mapping, handedness (UE right stays right), rotations, the full actor
/// transform and the triangle winding rule. Returns the first failure, or null.
/// </summary>
public static class UeSpaceChecks
{
    private const float Epsilon = 1e-4f;

    private static readonly FRotator[] Rotators =
    [
        new(0, 0, 0),
        new(0, 16384, 0),
        new(1000, 20000, -3000),
        new(-12000, 45000, 7000),
        new(30000, -5000, 16000),
        new(8192, 70000, -40000),
    ];

    public static string? Run()
    {
        // Axes: UE forward, right and up.
        var forward = new FVector(1f, 0f, 0f);
        var right = new FVector(0f, 1f, 0f);
        var up = new FVector(0f, 0f, 1f);
        if (UeSpace.ToGodot(forward * 100f) != new Vector3(1f, 0f, 0f))
        {
            return "UE +X 100 uu must be Godot +X 1 m";
        }

        if (UeSpace.ToGodot(right * 100f) != new Vector3(0f, 0f, 1f))
        {
            return "UE +Y 100 uu must be Godot +Z 1 m";
        }

        if (UeSpace.ToGodot(up * 100f) != new Vector3(0f, 1f, 0f))
        {
            return "UE +Z 100 uu must be Godot +Y 1 m";
        }

        // Handedness: in right-handed Godot space, right = forward x up. UE right must land there.
        Vector3 godotRight = UeSpace.DirectionToGodot(forward).Cross(UeSpace.DirectionToGodot(up));
        if (!godotRight.IsEqualApprox(UeSpace.DirectionToGodot(right)))
        {
            return $"handedness: forward x up is {godotRight} in Godot, but UE right maps to {UeSpace.DirectionToGodot(right)}";
        }

        // Yaw 16384 turns local +X into world +Y; pitch 16384 turns it up.
        if (!Near(UeSpace.RotationToGodot(new FRotator(0, 16384, 0)) * Vector3.Right, UeSpace.DirectionToGodot(right)))
        {
            return "yaw 16384 must turn forward into UE +Y";
        }

        if (!Near(UeSpace.RotationToGodot(new FRotator(16384, 0, 0)) * Vector3.Right, UeSpace.DirectionToGodot(up)))
        {
            return "pitch 16384 must turn forward into UE +Z";
        }

        var local = new FVector(37f, -12.5f, 81f);
        var location = new FVector(-640f, 1200f, 96f);
        var scale = new FVector(1.5f, 0.75f, 2f);
        var prePivot = new FVector(8f, -4f, 16f);
        foreach (FRotator rotator in Rotators)
        {
            Basis basis = UeSpace.RotationToGodot(rotator);
            if (Math.Abs(basis.Determinant() - 1f) > 1e-3f || !basis.IsEqualApprox(basis.Orthonormalized()))
            {
                return $"rotation {Describe(rotator)} is not a proper rotation in Godot (det {basis.Determinant()})";
            }

            rotator.GetAxes(out FVector x, out FVector y, out FVector z);

            // UE row-vector form: v * M = v.X * X + v.Y * Y + v.Z * Z.
            FVector rotated = x * local.X + y * local.Y + z * local.Z;
            if (!Near(basis * UeSpace.DirectionToGodot(local), UeSpace.DirectionToGodot(rotated), 1e-3f))
            {
                return $"rotation {Describe(rotator)} does not match the UE rotation matrix";
            }

            // UE2 LocalToWorld: ((local - PrePivot) * Scale) * M + Location.
            FVector d = (local - prePivot) * scale;
            FVector world = x * d.X + y * d.Y + z * d.Z + location;
            Transform3D transform = UeSpace.TransformToGodot(location, rotator, scale, prePivot);
            if (!Near(transform * UeSpace.ToGodot(local), UeSpace.ToGodot(world), 1e-4f))
            {
                return $"actor transform with rotation {Describe(rotator)} does not match UE2 LocalToWorld";
            }
        }

        // Triangles: the UE front normal (b - a) x (c - a) must be the Godot front normal of the mapped triangle
        // kept in UE order. Godot front faces are clockwise, so that normal is (c - a) x (b - a).
        var a = new FVector(0f, 0f, 0f);
        var b = new FVector(100f, 0f, 0f);
        var c = new FVector(0f, 100f, 0f);
        FVector ueNormal = ((b - a) ^ (c - a)).SafeNormal();
        Vector3 ga = UeSpace.ToGodot(a);
        Vector3 gb = UeSpace.ToGodot(b);
        Vector3 gc = UeSpace.ToGodot(c);
        Vector3 godotFront = (gc - ga).Cross(gb - ga).Normalized();
        if (!Near(godotFront, UeSpace.DirectionToGodot(ueNormal)))
        {
            return $"winding: UE front normal {UeSpace.DirectionToGodot(ueNormal)} but Godot front normal {godotFront}";
        }

        // Round trip.
        var p = new FVector(1234.5f, -987.25f, 64.125f);
        FVector back = UeSpace.ToUe(UeSpace.ToGodot(p));
        if ((back - p).Size() > 1e-3f)
        {
            return "UE to Godot and back must return the point";
        }

        return null;
    }

    private static bool Near(Vector3 a, Vector3 b, float tolerance = Epsilon) => (a - b).Length() <= tolerance;

    private static string Describe(FRotator r) => $"(P {r.Pitch}, Y {r.Yaw}, R {r.Roll})";
}
