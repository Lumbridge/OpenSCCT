using System;
using OpenScct.Provenance;

namespace OpenScct.Core;

/// <summary>
/// UE2 FVector: three floats in Unreal units, Z up. Operators follow UnMath.h: <c>|</c> is the dot product,
/// <c>^</c> the cross product, <c>*</c> and <c>/</c> with a vector are component-wise, and dividing by a float
/// multiplies by its reciprocal like UE2 does.
/// <para>C# gives <c>|</c> and <c>^</c> low precedence, so bracket them: <c>(A | B) &gt; 0</c>.</para>
/// </summary>
public struct FVector : IEquatable<FVector>
{
    public float X;
    public float Y;
    public float Z;

    public FVector(float x, float y, float z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public static FVector Zero => default;

    public static FVector operator +(FVector a, FVector b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

    public static FVector operator -(FVector a, FVector b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    public static FVector operator -(FVector v) => new(-v.X, -v.Y, -v.Z);

    public static FVector operator *(FVector v, float scale) => new(v.X * scale, v.Y * scale, v.Z * scale);

    public static FVector operator *(float scale, FVector v) => new(v.X * scale, v.Y * scale, v.Z * scale);

    /// <summary>UE2 divides by multiplying with 1/Scale, which can differ from true division in the last bit.</summary>
    public static FVector operator /(FVector v, float scale)
    {
        float rscale = 1f / scale;
        return new FVector(v.X * rscale, v.Y * rscale, v.Z * rscale);
    }

    /// <summary>Component-wise product.</summary>
    public static FVector operator *(FVector a, FVector b) => new(a.X * b.X, a.Y * b.Y, a.Z * b.Z);

    /// <summary>Component-wise quotient.</summary>
    public static FVector operator /(FVector a, FVector b) => new(a.X / b.X, a.Y / b.Y, a.Z / b.Z);

    /// <summary>Dot product (UnrealScript Dot).</summary>
    public static float operator |(FVector a, FVector b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    /// <summary>Cross product (UnrealScript Cross).</summary>
    public static FVector operator ^(FVector a, FVector b) =>
        new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);

    /// <summary>Exact component comparison, like UE2 operator==.</summary>
    public static bool operator ==(FVector a, FVector b) => a.X == b.X && a.Y == b.Y && a.Z == b.Z;

    public static bool operator !=(FVector a, FVector b) => a.X != b.X || a.Y != b.Y || a.Z != b.Z;

    public readonly float Dot(FVector other) => this | other;

    public readonly FVector Cross(FVector other) => this ^ other;

    /// <summary>Length (UnrealScript VSize).</summary>
    public readonly float Size() => MathF.Sqrt(X * X + Y * Y + Z * Z);

    public readonly float SizeSquared() => X * X + Y * Y + Z * Z;

    public readonly float Size2D() => MathF.Sqrt(X * X + Y * Y);

    public readonly float SizeSquared2D() => X * X + Y * Y;

    public readonly bool IsZero() => X == 0f && Y == 0f && Z == 0f;

    public readonly bool IsNearlyZero() =>
        SvmMath.Abs(X) < SvmMath.KINDA_SMALL_NUMBER &&
        SvmMath.Abs(Y) < SvmMath.KINDA_SMALL_NUMBER &&
        SvmMath.Abs(Z) < SvmMath.KINDA_SMALL_NUMBER;

    /// <summary>Normalizes in place. Returns false (and leaves the vector alone) when it is too short.</summary>
    public bool Normalize()
    {
        float squareSum = X * X + Y * Y + Z * Z;
        if (squareSum >= SvmMath.SMALL_NUMBER)
        {
            float scale = 1f / MathF.Sqrt(squareSum);
            X *= scale;
            Y *= scale;
            Z *= scale;
            return true;
        }

        return false;
    }

    /// <summary>Unit vector, or zero when the vector is too short (UnrealScript Normal).</summary>
    [Provenance(ProvenanceKind.Estimated, "UE2 UnMath.h SafeNormal; 1/sqrt form to be confirmed in the game exe")]
    public readonly FVector SafeNormal()
    {
        float squareSum = X * X + Y * Y + Z * Z;
        if (squareSum < SvmMath.SMALL_NUMBER)
        {
            return default;
        }

        float scale = 1f / MathF.Sqrt(squareSum);
        return new FVector(X * scale, Y * scale, Z * scale);
    }

    /// <summary>Unit vector without the length check.</summary>
    public readonly FVector UnsafeNormal()
    {
        float scale = 1f / MathF.Sqrt(X * X + Y * Y + Z * Z);
        return new FVector(X * scale, Y * scale, Z * scale);
    }

    /// <summary>Divides by Z (perspective projection).</summary>
    public readonly FVector Projection()
    {
        float rz = 1f / Z;
        return new FVector(X * rz, Y * rz, 1f);
    }

    /// <summary>Reflects this vector about the plane with the given normal.</summary>
    public readonly FVector MirrorByVector(FVector mirrorNormal) => this - mirrorNormal * (2f * (this | mirrorNormal));

    /// <summary>Clamps every component to [-radius, radius].</summary>
    public readonly FVector BoundToCube(float radius) =>
        new(SvmMath.Clamp(X, -radius, radius), SvmMath.Clamp(Y, -radius, radius), SvmMath.Clamp(Z, -radius, radius));

    /// <summary>
    /// Direction to rotator (UnrealScript rotator(vector)). UE2 scales by MAXWORD (65535) / 2pi and rounds.
    /// </summary>
    [UcNative("FVector::Rotation")]
    [Provenance(ProvenanceKind.Estimated, "UE2 UnMath.cpp FVector::Rotation; scale and rounding to be confirmed in the game exe")]
    public readonly FRotator Rotation()
    {
        var r = new FRotator(
            pitch: SvmMath.Round((float)((double)(SvmMath.Atan2(Z, MathF.Sqrt(X * X + Y * Y)) * 65535f) / (2.0 * 3.1415926535897932))),
            yaw: SvmMath.Round((float)((double)(SvmMath.Atan2(Y, X) * 65535f) / (2.0 * 3.1415926535897932))),
            roll: 0);
        return r;
    }

    /// <summary>Projects into a coordinate system (UE2 FVector::TransformVectorBy).</summary>
    public readonly FVector TransformVectorBy(in FCoords coords) =>
        new(this | coords.XAxis, this | coords.YAxis, this | coords.ZAxis);

    /// <summary>Moves into a coordinate system, origin included (UE2 FVector::TransformPointBy).</summary>
    public readonly FVector TransformPointBy(in FCoords coords)
    {
        FVector temp = this - coords.Origin;
        return new FVector(temp | coords.XAxis, temp | coords.YAxis, temp | coords.ZAxis);
    }

    public static float Dist(FVector a, FVector b) => (a - b).Size();

    public static float DistSquared(FVector a, FVector b) => (a - b).SizeSquared();

    /// <summary>Bitwise-style equality (NaN equals NaN), for collections. Use == for UE2 comparisons.</summary>
    public readonly bool Equals(FVector other) => X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);

    public override readonly bool Equals(object? obj) => obj is FVector other && Equals(other);

    /// <summary>Deterministic hash (no per-process seed).</summary>
    public override readonly int GetHashCode()
    {
        unchecked
        {
            int h = BitConverter.SingleToInt32Bits(X == 0f ? 0f : X);
            h = h * 31 + BitConverter.SingleToInt32Bits(Y == 0f ? 0f : Y);
            h = h * 31 + BitConverter.SingleToInt32Bits(Z == 0f ? 0f : Z);
            return h;
        }
    }

    public override readonly string ToString() =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"({X:R},{Y:R},{Z:R})");
}
