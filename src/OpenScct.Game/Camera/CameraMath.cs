using OpenScct.Core;
using OpenScct.Provenance;

namespace OpenScct.Game.Camera;

/// <summary>The small maths the camera natives share: approach at a speed, rotate into an actor's axes, the pitch curve bias.</summary>
public static class CameraMath
{
    /// <summary>Moves <paramref name="from"/> toward <paramref name="to"/> by at most <paramref name="step"/>.</summary>
    public static float Approach(float from, float to, float step)
    {
        if (!(step > 0f))
        {
            return from;
        }

        float d = to - from;
        return d > step ? from + step : d < -step ? from - step : to;
    }

    /// <summary>Moves <paramref name="from"/> toward <paramref name="to"/> along the line by at most <paramref name="step"/>.</summary>
    public static FVector Approach(FVector from, FVector to, float step)
    {
        if (!(step > 0f))
        {
            return from;
        }

        FVector d = to - from;
        float length = d.Size();
        return length <= step || length == 0f ? to : from + d * (step / length);
    }

    /// <summary>
    /// An integer effect value moved toward its target: from + (to - from) clamped to the step, truncated toward zero
    /// (MoveTowardScalar then ftol, as UpdateView does). A sub-unit step still moves a value toward zero by one unit and
    /// leaves a value moving away from zero where it is.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "UpdateView game 0x10a96280: MoveTowardScalar 0x109532c0 (step = speed x Level.m_dT) then ftol 0x10b83aa0")]
    public static int ApproachInt(int from, int to, float step)
    {
        float d = to - (float)from;
        float s = step;
        float moved = d < -s ? from - s : d < s ? from + d : from + s;
        return SvmMath.Trunc(moved);
    }

    /// <summary>
    /// The first-person yaw smoothing (game 0x10a98720): both yaws as 16 bit angles, <paramref name="from"/> turned the
    /// short way toward <paramref name="to"/> by the step truncated toward zero (never past it); the result in [0, 65535].
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "game 0x10a98720: 16 bit yaws, short way, min(gap, step) truncated, result masked to 0xffff")]
    public static int ApproachYaw16(int from, int to, float step)
    {
        int a = from & 0xffff;
        int b = to & 0xffff;
        if (a == b)
        {
            return b;
        }

        bool up;
        int gap;
        if (b > a)
        {
            gap = b - a;
            up = gap <= 0x7fff;
            if (!up)
            {
                gap = a - b + 0x10000;
            }
        }
        else
        {
            gap = a - b;
            up = gap > 0x7fff;
            if (up)
            {
                gap = b - a + 0x10000;
            }
        }

        int s = System.Math.Abs(SvmMath.Trunc(SvmMath.Min(gap, step)));
        return (up ? a + s : a - s) & 0xffff;
    }

    /// <summary>The rotator of an orthonormal frame (UE2 FCoords::OrthoRotation, game 0x10950770).</summary>
    [Provenance(ProvenanceKind.Decoded, "game 0x10950770: pitch and yaw from the X axis, roll from the Z and Y axes against the unrolled frame's Y axis")]
    public static FRotator OrthoRotation(FVector x, FVector y, FVector z)
    {
        const float ToUnits = 32768f / 3.14159265358979f;
        int pitch = SvmMath.Trunc(SvmMath.Atan2(x.Z, SvmMath.Sqrt((x.X * x.X) + (x.Y * x.Y))) * ToUnits);
        int yaw = SvmMath.Trunc(SvmMath.Atan2(x.Y, x.X) * ToUnits);
        new FRotator(pitch, yaw, 0).GetAxes(out _, out FVector sy, out _);
        int roll = SvmMath.Trunc(SvmMath.Atan2(z | sy, y | sy) * ToUnits);
        return new FRotator(pitch, yaw, roll);
    }

    /// <summary><paramref name="v"/> expressed in the axes of <paramref name="rotation"/>.</summary>
    public static FVector Rotate(FVector v, FRotator rotation)
    {
        rotation.GetAxes(out FVector x, out FVector y, out FVector z);
        return x * v.X + y * v.Y + z * v.Z;
    }

    /// <summary>The pitch curve's bias: x ^ (ln b / ln 0.5) (editor 0x10fea400).</summary>
    [Provenance(ProvenanceKind.Decoded, "camera.md rule 3.9: Bias(x, b) = x ^ (ln b / ln 0.5)")]
    public static float Bias(float x, float b)
    {
        if (!(x > 0f))
        {
            return 0f;
        }

        if (!(b > 0f))
        {
            return 1f;
        }

        return SvmMath.Pow(x, SvmMath.Loge(b) / SvmMath.Loge(0.5f));
    }
}
