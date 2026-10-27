using Godot;
using OpenScct.Core;
using OpenScct.Provenance;

namespace OpenScctGame.Views;

/// <summary>
/// The rotation the pose build gives the aim bone (spec/native/aim-ik.md rule 4), as a Godot basis in world space.
/// </summary>
public static class AimPose
{
    /// <summary>
    /// The world rotation of the AAFULL turn: the pitch about Z x <paramref name="facing"/>, then the yaw about Z, each
    /// as the axis-angle coords of 0x10fdd330 (X axis (x^2(1 - c) + c, xy(1 - c) + s z, xz(1 - c) - s y) and kin)
    /// applied to vectors, in UE space, converted to Godot space.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "0x11102160: Transpose, *= coords(+0x36c, pitch), *= coords(UnitCoords.ZAxis, yaw), Transpose; +0x36c = Z x original direction (BuildPose 0x1110cb5f)")]
    [Provenance(ProvenanceKind.Estimated, "The space base is taken as actor-aligned (vertical Z), so the turn is applied about world axes through the bone")]
    public static Basis WorldBasis(float yaw, float pitch, FVector facing)
    {
        var up = new FVector(0f, 0f, 1f);
        FVector side = (up ^ new FVector(facing.X, facing.Y, 0f)).SafeNormal();
        Vector3 x = UeSpace.DirectionToGodot(Turn(UeSpace.DirectionToUe(Vector3.Right), side, pitch, up, yaw));
        Vector3 y = UeSpace.DirectionToGodot(Turn(UeSpace.DirectionToUe(Vector3.Up), side, pitch, up, yaw));
        Vector3 z = UeSpace.DirectionToGodot(Turn(UeSpace.DirectionToUe(Vector3.Back), side, pitch, up, yaw));
        return new Basis(x, y, z);
    }

    private static FVector Turn(FVector v, FVector pitchAxis, float pitch, FVector yawAxis, float yaw) =>
        Coords(yawAxis, yaw, Coords(pitchAxis, pitch, v));

    /// <summary>A vector transformed by the axis-angle coords (its rows dotted with the vector).</summary>
    public static FVector Coords(FVector n, float angle, FVector v)
    {
        float c = SvmMath.Cos(angle);
        float s = SvmMath.Sin(angle);
        float t = 1f - c;
        var xAxis = new FVector((n.X * n.X * t) + c, (n.X * n.Y * t) + (s * n.Z), (n.X * n.Z * t) - (s * n.Y));
        var yAxis = new FVector((n.X * n.Y * t) - (s * n.Z), (n.Y * n.Y * t) + c, (n.Y * n.Z * t) + (s * n.X));
        var zAxis = new FVector((n.X * n.Z * t) + (s * n.Y), (n.Y * n.Z * t) - (s * n.X), (n.Z * n.Z * t) + c);
        return new FVector(xAxis | v, yAxis | v, zAxis | v);
    }
}
