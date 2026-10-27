using System;
using OpenScct.Provenance;

namespace OpenScct.Core;

/// <summary>
/// UE2 FRotator: Pitch, Yaw and Roll as ints with 65536 units per full turn. Arithmetic does not wrap values;
/// use <see cref="Clamp"/> (0..65535) or <see cref="Normalize"/> (-32768..32767) when a canonical form is needed.
/// </summary>
public struct FRotator : IEquatable<FRotator>
{
    public int Pitch;
    public int Yaw;
    public int Roll;

    public FRotator(int pitch, int yaw, int roll)
    {
        Pitch = pitch;
        Yaw = yaw;
        Roll = roll;
    }

    public static FRotator Zero => default;

    public static FRotator operator +(FRotator a, FRotator b) => new(a.Pitch + b.Pitch, a.Yaw + b.Yaw, a.Roll + b.Roll);

    public static FRotator operator -(FRotator a, FRotator b) => new(a.Pitch - b.Pitch, a.Yaw - b.Yaw, a.Roll - b.Roll);

    public static FRotator operator -(FRotator r) => new(-r.Pitch, -r.Yaw, -r.Roll);

    /// <summary>Scales each axis and rounds to nearest (UE2 operator* uses appRound).</summary>
    [Provenance(ProvenanceKind.Estimated, "UE2 UnMath.h FRotator::operator*(FLOAT); rounding mode to be confirmed")]
    public static FRotator operator *(FRotator r, float scale) =>
        new(SvmMath.Round(r.Pitch * scale), SvmMath.Round(r.Yaw * scale), SvmMath.Round(r.Roll * scale));

    public static FRotator operator *(float scale, FRotator r) => r * scale;

    public static bool operator ==(FRotator a, FRotator b) => a.Pitch == b.Pitch && a.Yaw == b.Yaw && a.Roll == b.Roll;

    public static bool operator !=(FRotator a, FRotator b) => !(a == b);

    /// <summary>True when every axis is a whole number of turns.</summary>
    public readonly bool IsZero() => ((Pitch & 65535) == 0) && ((Yaw & 65535) == 0) && ((Roll & 65535) == 0);

    public readonly FRotator Add(int deltaPitch, int deltaYaw, int deltaRoll) =>
        new(Pitch + deltaPitch, Yaw + deltaYaw, Roll + deltaRoll);

    /// <summary>Wraps an axis into 0..65535.</summary>
    public static int ClampAxis(int angle) => angle & 65535;

    /// <summary>Wraps an axis into -32768..32767.</summary>
    public static int NormalizeAxis(int angle)
    {
        angle &= 0xFFFF;
        if (angle > 32767)
        {
            angle -= 0x10000;
        }

        return angle;
    }

    /// <summary>Every axis wrapped into 0..65535.</summary>
    public readonly FRotator Clamp() => new(Pitch & 65535, Yaw & 65535, Roll & 65535);

    /// <summary>Every axis wrapped into -32768..32767 (UnrealScript Normalize(rotator)).</summary>
    public readonly FRotator Normalize() => new(NormalizeAxis(Pitch), NormalizeAxis(Yaw), NormalizeAxis(Roll));

    /// <summary>Every axis wrapped into 0..65535.</summary>
    public readonly FRotator Denormalize() => Clamp();

    /// <summary>Forward direction (UnrealScript vector(rotator)), built from the UE2 sine table.</summary>
    [UcNative("FRotator::Vector")]
    public readonly FVector Vector()
    {
        float cp = SvmMath.Cos(Pitch);
        return new FVector(cp * SvmMath.Cos(Yaw), cp * SvmMath.Sin(Yaw), SvmMath.Sin(Pitch));
    }

    /// <summary>Forward, right and up axes of this rotation (UnrealScript GetAxes).</summary>
    [Provenance(ProvenanceKind.Estimated, "UE2 GetAxes via UnitCoords / R; Y and Z product order to be confirmed in the game exe")]
    public readonly void GetAxes(out FVector X, out FVector Y, out FVector Z)
    {
        FCoords c = FCoords.FromRotator(this);
        X = c.XAxis;
        Y = c.YAxis;
        Z = c.ZAxis;
    }

    /// <summary>Equivalent rotation with all axes wrapped, compared for the same orientation.</summary>
    public readonly bool EqualsWrapped(FRotator other) => Clamp() == other.Clamp();

    public readonly bool Equals(FRotator other) => this == other;

    public override readonly bool Equals(object? obj) => obj is FRotator other && this == other;

    public override readonly int GetHashCode()
    {
        unchecked
        {
            return (Pitch * 31 + Yaw) * 31 + Roll;
        }
    }

    public override readonly string ToString() =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"({Pitch},{Yaw},{Roll})");
}
