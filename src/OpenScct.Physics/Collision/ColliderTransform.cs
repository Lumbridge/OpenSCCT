using System;
using System.Collections.Generic;
using OpenScct.Core;

namespace OpenScct.Physics.Collision;

/// <summary>
/// An actor transform as in spec/svmap/1.0/conventions.md: <c>world = ((local - PrePivot) * Scale) * M(Rotation) +
/// Location</c>, with M the UE2 rotation matrix from the sim's sine table (rows are the rotator's X, Y, Z axes).
/// </summary>
internal readonly struct ColliderTransform
{
    public readonly FVector Location;
    public readonly FRotator Rotation;
    public readonly FVector Scale;
    public readonly FVector PrePivot;
    private readonly FVector _x;
    private readonly FVector _y;
    private readonly FVector _z;

    public ColliderTransform(FVector location, FRotator rotation, FVector scale, FVector prePivot)
    {
        Location = location;
        Rotation = rotation;
        Scale = scale;
        PrePivot = prePivot;
        FCoords axes = FCoords.FromRotator(rotation);
        _x = axes.XAxis;
        _y = axes.YAxis;
        _z = axes.ZAxis;
        IsIdentity = location.IsZero() && rotation == default && scale == new FVector(1f, 1f, 1f) && prePivot.IsZero();
        Valid = SvmMath.Abs(scale.X) > 1e-6f && SvmMath.Abs(scale.Y) > 1e-6f && SvmMath.Abs(scale.Z) > 1e-6f;
        Mirrored = scale.X * scale.Y * scale.Z < 0f;
    }

    /// <summary>Location 0, rotation 0, scale 1, prePivot 0.</summary>
    public bool IsIdentity { get; }

    /// <summary>False when a scale component is (nearly) zero, which flattens the geometry.</summary>
    public bool Valid { get; }

    /// <summary>True when the scale mirrors the geometry (odd number of negative components): winding flips.</summary>
    public bool Mirrored { get; }

    public FVector TransformPoint(FVector local)
    {
        if (IsIdentity)
        {
            return local;
        }

        FVector p = (local - PrePivot) * Scale;
        return new FVector(
            p.X * _x.X + p.Y * _y.X + p.Z * _z.X + Location.X,
            p.X * _x.Y + p.Y * _y.Y + p.Z * _z.Y + Location.Y,
            p.X * _x.Z + p.Y * _y.Z + p.Z * _z.Z + Location.Z);
    }

    /// <summary>Transforms a plane with the inverse transpose, renormalising the normal.</summary>
    public HullPlane TransformPlane(FVector n, float w)
    {
        if (IsIdentity)
        {
            return new HullPlane(n, w);
        }

        var ns = new FVector(n.X / Scale.X, n.Y / Scale.Y, n.Z / Scale.Z);
        var nw = new FVector(
            ns.X * _x.X + ns.Y * _y.X + ns.Z * _z.X,
            ns.X * _x.Y + ns.Y * _y.Y + ns.Z * _z.Y,
            ns.X * _x.Z + ns.Y * _y.Z + ns.Z * _z.Z);
        float k = nw.Size();
        if (k <= 0f)
        {
            return new HullPlane(n, w);
        }

        float wr = w - (n | PrePivot) + (nw | Location);
        float inv = 1f / k;
        return new HullPlane(nw * inv, wr * inv);
    }
}

/// <summary>Corners of a convex hull given only by its planes (planes pointing out).</summary>
internal static class HullCorners
{
    private const int MaxPlanes = 64;

    /// <summary>
    /// Adds the hull's corners to <paramref name="output"/>: every point where three planes meet that lies inside
    /// all planes, without near duplicates. False (nothing added) for hulls with too many planes.
    /// </summary>
    public static bool Compute(ReadOnlySpan<HullPlane> planes, List<FVector> output)
    {
        int n = planes.Length;
        if (n < 4 || n > MaxPlanes)
        {
            return false;
        }

        int first = output.Count;
        for (int i = 0; i < n; i++)
        {
            for (int j = i + 1; j < n; j++)
            {
                FVector cij = planes[i].N ^ planes[j].N;
                for (int k = j + 1; k < n; k++)
                {
                    float det = cij | planes[k].N;
                    if (SvmMath.Abs(det) < 1e-6f)
                    {
                        continue;
                    }

                    // Cramer's rule in vector form: p = (wi (nj x nk) + wj (nk x ni) + wk (ni x nj)) / det.
                    FVector p = ((planes[j].N ^ planes[k].N) * planes[i].W + (planes[k].N ^ planes[i].N) * planes[j].W +
                                 cij * planes[k].W) * (1f / det);
                    float tol = 0.01f + 1e-5f * (SvmMath.Abs(p.X) + SvmMath.Abs(p.Y) + SvmMath.Abs(p.Z));
                    bool inside = true;
                    for (int m = 0; m < n; m++)
                    {
                        if ((planes[m].N | p) - planes[m].W > tol)
                        {
                            inside = false;
                            break;
                        }
                    }

                    if (!inside)
                    {
                        continue;
                    }

                    bool duplicate = false;
                    for (int q = first; q < output.Count; q++)
                    {
                        if (FVector.DistSquared(output[q], p) < 1e-4f)
                        {
                            duplicate = true;
                            break;
                        }
                    }

                    if (!duplicate)
                    {
                        output.Add(p);
                    }
                }
            }
        }

        return output.Count > first;
    }
}
