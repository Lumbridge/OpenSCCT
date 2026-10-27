using System;

namespace OpenScct.DiffTest.Model;

/// <summary>A location or velocity in Unreal units (Z up). Doubles for analysis; the values come from float32.</summary>
public readonly record struct Vec3(double X, double Y, double Z)
{
    public static readonly Vec3 Zero = new(0, 0, 0);

    public double Length => Math.Sqrt((X * X) + (Y * Y) + (Z * Z));

    /// <summary>Horizontal length (X and Y only): the speed across the ground.</summary>
    public double Length2D => Math.Sqrt((X * X) + (Y * Y));

    public static Vec3 operator +(Vec3 a, Vec3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

    public static Vec3 operator -(Vec3 a, Vec3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    public static Vec3 operator *(Vec3 a, double s) => new(a.X * s, a.Y * s, a.Z * s);

    public static double Distance(Vec3 a, Vec3 b) => (a - b).Length;

    public static Vec3 Lerp(Vec3 a, Vec3 b, double w) => a + ((b - a) * w);
}

/// <summary>A rotator in 65536ths of a turn, as the game stores it (not masked: a yaw can be 81919).</summary>
public readonly record struct Rot3(int Pitch, int Yaw, int Roll)
{
    /// <summary>The yaw masked to 0..65535.</summary>
    public int YawMasked => Yaw & 0xFFFF;
}
