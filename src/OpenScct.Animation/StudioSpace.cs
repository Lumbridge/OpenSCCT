using System.Numerics;
using OpenScct.Core;

namespace OpenScct.Animation;

/// <summary>Matches the game's UeSpace adapter without a Godot dependency.</summary>
public static class StudioSpace
{
    public static Vector3 FromUe(FVector value) => new(value.X / 100, value.Z / 100, value.Y / 100);
    public static FVector ToUe(Vector3 value) => new(value.X * 100, value.Z * 100, value.Y * 100);
    public static Quaternion FromUe(FRotator value)
    {
        value.GetAxes(out FVector x, out FVector y, out FVector z);
        return Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(new(x.X, x.Z, x.Y, 0, z.X, z.Z, z.Y, 0, y.X, y.Z, y.Y, 0, 0, 0, 0, 1)));
    }
    public static FRotator ToUe(Quaternion rotation)
    {
        Vector3 x = Vector3.Transform(Vector3.UnitX, rotation), y = Vector3.Transform(Vector3.UnitZ, rotation), z = Vector3.Transform(Vector3.UnitY, rotation);
        FRotator result = new FVector(x.X, x.Z, x.Y).Rotation();
        int roll = (int)(SvmMath.Atan2(-y.Y, z.Y) * (65536 / (2 * MathF.PI)));
        return new(result.Pitch, result.Yaw, roll);
    }
}
