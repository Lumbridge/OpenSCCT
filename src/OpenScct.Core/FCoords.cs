using OpenScct.Provenance;

namespace OpenScct.Core;

/// <summary>
/// UE2 FCoords: an origin and three axes. <see cref="FromRotator"/> gives the world-space forward, right and up
/// axes of a rotation; <see cref="FVector.TransformVectorBy"/> projects a world vector onto those axes.
/// </summary>
public struct FCoords
{
    public FVector Origin;
    public FVector XAxis;
    public FVector YAxis;
    public FVector ZAxis;

    public FCoords(FVector origin, FVector xAxis, FVector yAxis, FVector zAxis)
    {
        Origin = origin;
        XAxis = xAxis;
        YAxis = yAxis;
        ZAxis = zAxis;
    }

    /// <summary>Identity coordinates (GMath.UnitCoords).</summary>
    public static FCoords UnitCoords => new(default, new FVector(1f, 0f, 0f), new FVector(0f, 1f, 0f), new FVector(0f, 0f, 1f));

    /// <summary>Axes of a rotation using the UE2 sine table (UnitCoords / Rot).</summary>
    [Provenance(ProvenanceKind.Estimated, "Standard UE rotation axes; float product order of the Y and Z axes to be confirmed")]
    public static FCoords FromRotator(FRotator r)
    {
        float sp = SvmMath.Sin(r.Pitch);
        float cp = SvmMath.Cos(r.Pitch);
        float sy = SvmMath.Sin(r.Yaw);
        float cy = SvmMath.Cos(r.Yaw);
        float sr = SvmMath.Sin(r.Roll);
        float cr = SvmMath.Cos(r.Roll);

        return new FCoords(
            default,
            new FVector(cp * cy, cp * sy, sp),
            new FVector(sr * sp * cy - cr * sy, sr * sp * sy + cr * cy, -sr * cp),
            new FVector(-(cr * sp * cy + sr * sy), cy * sr - cr * sp * sy, cr * cp));
    }

    /// <summary>Swaps rows and columns of the axis matrix (the inverse rotation for orthonormal axes).</summary>
    public readonly FCoords Transpose()
    {
        return new FCoords(
            -Origin.TransformVectorBy(this),
            new FVector(XAxis.X, YAxis.X, ZAxis.X),
            new FVector(XAxis.Y, YAxis.Y, ZAxis.Y),
            new FVector(XAxis.Z, YAxis.Z, ZAxis.Z));
    }
}
