using OpenScct.Core;
using OpenScct.Provenance;

namespace OpenScct.Game.Movement;

/// <summary>Small maths the movement port shares: UnrealScript conversions and the engine turn helpers.</summary>
public static class MovementMath
{
    /// <summary>UnrealScript <c>int(f)</c>: truncation toward zero (x87 indefinite for out-of-range values).</summary>
    public static int Int(float f) => SvmMath.Trunc(f);

    /// <summary>
    /// UnrealScript <c>rotator(v)</c> as the game computes it. The game truncates the scaled angle where stock UE2
    /// rounds: a direction 16383.75 units round gives yaw 16383, one of 12287.81 gives 12287, which is why a spy
    /// standing still at yaw 16384 turns to 81919 (16384 + 65535) on the first frame of a run.
    /// </summary>
    [UcNative("FVector::Rotation")]
    [Provenance(ProvenanceKind.Measured, "goldens movement.spy-run-forward and spy-fall-ledge: pawn yaw 16384 -> 81919, 12288 -> 77823 on the first Mario frame")]
    public static FRotator Rotator(FVector v)
    {
        const double scale = 65535.0 / (2.0 * 3.1415926535897932);
        float pitch = SvmMath.Atan2(v.Z, SvmMath.Sqrt(v.X * v.X + v.Y * v.Y));
        float yaw = SvmMath.Atan2(v.Y, v.X);
        return new FRotator(SvmMath.Trunc((float)(pitch * scale)), SvmMath.Trunc((float)(yaw * scale)), 0);
    }

    /// <summary>The yaw of <c>rotator(v)</c>.</summary>
    public static int Yaw(FVector v) => Rotator(v).Yaw;

    /// <summary>The horizontal part of a vector.</summary>
    public static FVector Flat(FVector v) => new(v.X, v.Y, 0f);

    /// <summary>
    /// The X and Y axes of a rotation flattened to the ground (Z = 0, not renormalised), as the pawn-rotation
    /// helpers use them.
    /// </summary>
    public static void FlatAxes(FRotator r, out FVector X, out FVector Y)
    {
        r.GetAxes(out X, out Y, out _);
        X.Z = 0f;
        Y.Z = 0f;
    }

    /// <summary>A point moved into world space by the actor's location and rotation (UE2 local-to-world).</summary>
    public static FVector ToWorld(FVector location, FRotator rotation, FVector local)
    {
        rotation.GetAxes(out FVector X, out FVector Y, out FVector Z);
        return location + X * local.X + Y * local.Y + Z * local.Z;
    }

    /// <summary>The projection of <paramref name="p"/> on the infinite line through <paramref name="a"/> and <paramref name="b"/>.</summary>
    public static FVector ProjectOnLine(FVector p, FVector a, FVector b)
    {
        FVector d = b - a;
        float len2 = d.SizeSquared();
        if (len2 <= 0f)
        {
            return a;
        }

        return a + d * (((p - a) | d) / len2);
    }
}
