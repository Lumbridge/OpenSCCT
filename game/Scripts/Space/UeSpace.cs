using Godot;
using OpenScct.Core;

namespace OpenScctGame;

/// <summary>
/// The one place that converts between the simulation's UE space and Godot space
/// (spec/svmap/1.0/conventions.md, render space).
/// <list type="bullet">
/// <item>UE: unreal units (uu), left-handed, X forward, Y right, Z up.</item>
/// <item>Godot: metres, right-handed, Y up. One uu is one centimetre.</item>
/// </list>
/// Positions: UE (x, y, z) becomes Godot (x, z, y) / 100. Directions and normals: (x, z, y), not scaled.
/// The swap of Y and Z is a reflection, which is what turns the left-handed space into a right-handed one,
/// so UE forward, right and up stay forward, right and up on screen.
/// <para>Triangles: Godot front faces are clockwise, the reverse of glTF. Mapped UE triangles therefore keep
/// their UE order (a, b, c) in a Godot mesh, while glTF files store (a, c, b) and Godot's glTF importer flips
/// them back.</para>
/// </summary>
public static class UeSpace
{
    /// <summary>Unreal units per Godot metre.</summary>
    public const float UnitsPerMetre = 100f;

    /// <summary>Position in uu to Godot metres. Divides like the content pipeline does, so values match the glTF exactly.</summary>
    public static Vector3 ToGodot(FVector location) =>
        new(location.X / UnitsPerMetre, location.Z / UnitsPerMetre, location.Y / UnitsPerMetre);

    /// <summary>Direction or normal, not scaled.</summary>
    public static Vector3 DirectionToGodot(FVector direction) => new(direction.X, direction.Z, direction.Y);

    /// <summary>A length or radius in uu to metres.</summary>
    public static float LengthToGodot(float units) => units / UnitsPerMetre;

    /// <summary>Godot position in metres back to uu.</summary>
    public static FVector ToUe(Vector3 position) =>
        new(position.X * UnitsPerMetre, position.Z * UnitsPerMetre, position.Y * UnitsPerMetre);

    public static FVector DirectionToUe(Vector3 direction) => new(direction.X, direction.Z, direction.Y);

    /// <summary>
    /// The Godot basis of a UE rotation. Applied to a mapped local vector it gives the mapped world vector, so its
    /// columns are the rotated UE X, Z and Y axes (Godot local X, Y and Z), each mapped. It is a proper rotation.
    /// </summary>
    public static Basis RotationToGodot(FRotator rotation)
    {
        rotation.GetAxes(out FVector x, out FVector y, out FVector z);
        return new Basis(DirectionToGodot(x), DirectionToGodot(z), DirectionToGodot(y));
    }

    /// <summary>Location and rotation of an actor with no scale or pivot.</summary>
    public static Transform3D TransformToGodot(FVector location, FRotator rotation) =>
        new(RotationToGodot(rotation), ToGodot(location));

    /// <summary>
    /// The full UE2 actor transform, <c>world = ((local - PrePivot) * Scale) * M(Rotation) + Location</c>, with
    /// <paramref name="scale"/> = DrawScale * DrawScale3D per axis.
    /// </summary>
    public static Transform3D TransformToGodot(FVector location, FRotator rotation, FVector scale, FVector prePivot)
    {
        Basis basis = RotationToGodot(rotation) * Basis.FromScale(DirectionToGodot(scale));
        Vector3 origin = ToGodot(location) - basis * ToGodot(prePivot);
        return new Transform3D(basis, origin);
    }
}
