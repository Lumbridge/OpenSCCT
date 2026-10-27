using System;
using OpenScct.Provenance;

namespace OpenScct.Core;

/// <summary>
/// Deterministic UE2-style math for the simulation. Every function here gives bit-identical results on every
/// platform: only IEEE basic operations, sqrt and exact helpers are used, never the C runtime transcendentals.
/// <para>
/// Angles: <see cref="Sin(int)"/> and <see cref="Cos(int)"/> take UE2 rotator units (65536 per turn) and read the
/// 16384-entry table like GMath.SinTab/CosTab. The float overloads take radians and match the C runtime functions
/// UE2 called (appSin, appAtan2...) to the nearest float.
/// </para>
/// </summary>
public static class SvmMath
{
    public const float PI = 3.1415926535897932f;
    public const float SMALL_NUMBER = 1.0e-8f;
    public const float KINDA_SMALL_NUMBER = 1.0e-4f;
    public const float BIG_NUMBER = 3.4e+38f;
    public const float DELTA = 0.00001f;

    /// <summary>Entries in the UE2 sine table (one full turn).</summary>
    public const int NUM_ANGLES = 16384;

    /// <summary>Rotator units dropped when indexing the sine table.</summary>
    public const int ANGLE_SHIFT = 2;

    public const int ANGLE_MASK = NUM_ANGLES - 1;

    /// <summary>Rotator units per radian (32768 / pi).</summary>
    public const float RadToUnr = 10430.378350470452724949566316381f;

    /// <summary>Radians per rotator unit (pi / 32768).</summary>
    public const float UnrToRad = 0.000095873799242852576857380474343257f;

    private const double PiD = 3.14159265358979311600e+00;
    private const double PiLoD = 1.22464679914735317720e-16;
    private const double PiO2D = 1.57079632679489655800e+00;
    private const double PiO2LoD = 6.12323399573676588613e-17;
    private const double InvPiO2D = 6.36619772367581382433e-01;

    // pi/2 split into 33-bit pieces so k * piece is exact for |k| < 2^20 (the classic Cody-Waite reduction).
    private const double PiO2_1 = 1.57079632673412561417e+00;
    private const double PiO2_1T = 6.07710050650619224932e-11;
    private const double PiO2_2 = 6.07710050630396597660e-11;
    private const double PiO2_2T = 2.02226624879595063154e-21;

    private const double Ln2HiD = 6.93147180369123816490e-01;
    private const double Ln2LoD = 1.90821492927058770002e-10;
    private const double InvLn2D = 1.44269504088896338700e+00;
    private const double Sqrt2D = 1.41421356237309514547e+00;

    private static readonly float[] s_trigFloat = BuildSinTable();

    /// <summary>The UE2 sine table: entry i is sin(i * 2pi / 16384) rounded to float.</summary>
    public static ReadOnlySpan<float> SinTable => s_trigFloat;

    // ---- UE2 table trig (rotator units) -------------------------------------------------------------------

    /// <summary>UE2 GMath.SinTab: sine of a rotator angle (65536 units per turn).</summary>
    [UcNative("FGlobalMath::SinTab")]
    public static float Sin(int ueAngle) => s_trigFloat[(ueAngle >> ANGLE_SHIFT) & ANGLE_MASK];

    /// <summary>UE2 GMath.CosTab: cosine of a rotator angle (65536 units per turn).</summary>
    [UcNative("FGlobalMath::CosTab")]
    public static float Cos(int ueAngle) => s_trigFloat[((ueAngle + 16384) >> ANGLE_SHIFT) & ANGLE_MASK];

    [UcNative("FGlobalMath::FGlobalMath")]
    [Provenance(ProvenanceKind.Estimated, "UE2 builds TrigFLOAT[i] = appSin((FLOAT)i * 2.f * PI / NUM_ANGLES) at startup; to be checked against a live read of the game table")]
    private static float[] BuildSinTable()
    {
        var table = new float[NUM_ANGLES];
        for (int i = 0; i < NUM_ANGLES; i++)
        {
            table[i] = (float)SinD(SinTableArgument(i));
        }

        return table;
    }

    /// <summary>The double argument UE2 passes to sin for table entry <paramref name="i"/>.</summary>
    internal static double SinTableArgument(int i) => (double)((float)i * 2f) * PiD / NUM_ANGLES;

    // ---- float radian trig --------------------------------------------------------------------------------

    /// <summary>Sine of an angle in radians (UE2 appSin).</summary>
    public static float Sin(float radians) => (float)SinD(radians);

    /// <summary>Cosine of an angle in radians (UE2 appCos).</summary>
    public static float Cos(float radians) => (float)CosD(radians);

    /// <summary>Tangent of an angle in radians (UE2 appTan).</summary>
    public static float Tan(float radians) => (float)TanD(radians);

    /// <summary>Arc tangent in radians (UE2 appAtan).</summary>
    public static float Atan(float value) => (float)AtanD(value);

    /// <summary>Arc tangent of Y/X in radians, C atan2 semantics (UE2 appAtan2).</summary>
    public static float Atan2(float Y, float X) => (float)Atan2D(Y, X);

    /// <summary>Arc cosine in radians; NaN outside [-1, 1] like the C runtime (UE2 appAcos).</summary>
    public static float Acos(float value)
    {
        double x = value;
        if (!(x >= -1.0 && x <= 1.0))
        {
            return float.NaN;
        }

        return (float)Atan2D(Math.Sqrt((1.0 - x) * (1.0 + x)), x);
    }

    /// <summary>Arc sine in radians; NaN outside [-1, 1] like the C runtime (UE2 appAsin).</summary>
    public static float Asin(float value)
    {
        double x = value;
        if (!(x >= -1.0 && x <= 1.0))
        {
            return float.NaN;
        }

        return (float)Atan2D(x, Math.Sqrt((1.0 - x) * (1.0 + x)));
    }

    /// <summary>e to the power <paramref name="value"/> (UE2 appExp).</summary>
    public static float Exp(float value) => (float)ExpD(value);

    /// <summary>Natural logarithm (UE2 appLoge, UnrealScript Loge).</summary>
    public static float Loge(float value) => (float)LogD(value);

    /// <summary><paramref name="A"/> to the power <paramref name="B"/>, C pow semantics (UE2 appPow, UnrealScript **).</summary>
    public static float Pow(float A, float B) => (float)PowD(A, B);

    /// <summary>Square root (UE2 appSqrt). IEEE sqrt is exact-rounded, so this is deterministic.</summary>
    public static float Sqrt(float value) => MathF.Sqrt(value);

    /// <summary>1 / sqrt(value) (UE2 appInvSqrt on x87 builds).</summary>
    [Provenance(ProvenanceKind.Estimated, "UE2 x87 appInvSqrt computes 1/sqrt; confirm the game build does not use an rsqrt estimate")]
    public static float InvSqrt(float value) => 1f / MathF.Sqrt(value);

    // ---- UE2 integer conversions (x87 behaviour) ----------------------------------------------------------

    /// <summary>
    /// UE2 appRound: round to nearest, ties to even (x87 fistp in the default mode).
    /// Out of range and NaN give int.MinValue (the x87 integer indefinite value).
    /// </summary>
    public static int Round(float F) => ToIntOrIndefinite(Math.Round((double)F, MidpointRounding.ToEven));

    /// <summary>UE2 appTrunc / UnrealScript int(float): truncate toward zero; out of range gives int.MinValue.</summary>
    public static int Trunc(float F) => ToIntOrIndefinite(Math.Truncate((double)F));

    /// <summary>UE2 appFloor: largest integer not above F; out of range gives int.MinValue.</summary>
    public static int Floor(float F) => ToIntOrIndefinite(Math.Floor((double)F));

    /// <summary>UE2 appCeil: smallest integer not below F; out of range gives int.MinValue.</summary>
    public static int Ceil(float F) => ToIntOrIndefinite(Math.Ceiling((double)F));

    private static int ToIntOrIndefinite(double integral)
    {
        if (!(integral >= -2147483648.0 && integral <= 2147483647.0))
        {
            return int.MinValue;
        }

        return (int)integral;
    }

    // ---- UE2 helpers --------------------------------------------------------------------------------------

    /// <summary>UE2 Abs.</summary>
    public static float Abs(float A) => A >= 0f ? A : -A;

    /// <summary>UE2 Max template: (A &gt;= B) ? A : B. Differs from Math.Max for NaN and signed zero.</summary>
    public static float Max(float A, float B) => A >= B ? A : B;

    /// <summary>UE2 Min template: (A &lt;= B) ? A : B.</summary>
    public static float Min(float A, float B) => A <= B ? A : B;

    public static int Max(int A, int B) => A >= B ? A : B;

    public static int Min(int A, int B) => A <= B ? A : B;

    /// <summary>UE2 Clamp template: X &lt; Min ? Min : X &lt; Max ? X : Max.</summary>
    public static float Clamp(float X, float Min, float Max) => X < Min ? Min : X < Max ? X : Max;

    public static int Clamp(int X, int Min, int Max) => X < Min ? Min : X < Max ? X : Max;

    /// <summary>UE2 Square.</summary>
    public static float Square(float A) => A * A;

    /// <summary>UE2 Lerp: A + Alpha * (B - A).</summary>
    public static float Lerp(float A, float B, float Alpha) => A + Alpha * (B - A);

    /// <summary>UnrealScript FClamp.</summary>
    public static float FClamp(float V, float A, float B) => Clamp(V, A, B);

    /// <summary>UE2 appFractional: F - appTrunc(F) for values in int range.</summary>
    public static float Fractional(float F) => F - (float)Math.Truncate((double)F);

    /// <summary>Converts radians to rotator units the way UE2 code usually writes it (Radians * 32768 / PI).</summary>
    public static int RadiansToRotator(float radians) => Round(radians * RadToUnr);

    // ---- double kernels (deterministic) -------------------------------------------------------------------

    /// <summary>
    /// Reduces <paramref name="x"/> to r in about [-pi/4, pi/4] with x = r + k * pi/2, returning k mod 4.
    /// Accurate for |x| below about 1e5; larger arguments still give deterministic (if coarse) results.
    /// </summary>
    private static int ReducePiO2(double x, out double r)
    {
        double fn = Math.Round(x * InvPiO2D, MidpointRounding.ToEven);
        double t = x - fn * PiO2_1;
        double w = fn * PiO2_1T;
        double y = t - w;

        // Near a multiple of pi/2 the first step cancels too many bits; take a second, finer step.
        if (Math.Abs(y) < 1.0e-5)
        {
            double t2 = t;
            w = fn * PiO2_2;
            t = t2 - w;
            w = fn * PiO2_2T - ((t2 - t) - w);
            y = t - w;
        }

        r = y;
        return (int)((long)fn & 3);
    }

    // Taylor coefficients 1/n! with alternating signs, folded at compile time.
    private const double S1 = -1.0 / 6.0;
    private const double S2 = 1.0 / 120.0;
    private const double S3 = -1.0 / 5040.0;
    private const double S4 = 1.0 / 362880.0;
    private const double S5 = -1.0 / 39916800.0;
    private const double S6 = 1.0 / 6227020800.0;
    private const double S7 = -1.0 / 1307674368000.0;
    private const double S8 = 1.0 / 355687428096000.0;
    private const double S9 = -1.0 / 121645100408832000.0;
    private const double S10 = 1.0 / 51090942171709440000.0;

    private const double C1 = -1.0 / 2.0;
    private const double C2 = 1.0 / 24.0;
    private const double C3 = -1.0 / 720.0;
    private const double C4 = 1.0 / 40320.0;
    private const double C5 = -1.0 / 3628800.0;
    private const double C6 = 1.0 / 479001600.0;
    private const double C7 = -1.0 / 87178291200.0;
    private const double C8 = 1.0 / 20922789888000.0;
    private const double C9 = -1.0 / 6402373705728000.0;
    private const double C10 = 1.0 / 2432902008176640000.0;

    private static double SinKernel(double r)
    {
        double z = r * r;
        double p = S10;
        p = p * z + S9;
        p = p * z + S8;
        p = p * z + S7;
        p = p * z + S6;
        p = p * z + S5;
        p = p * z + S4;
        p = p * z + S3;
        p = p * z + S2;
        p = p * z + S1;
        return r + r * z * p;
    }

    private static double CosKernel(double r)
    {
        double z = r * r;
        double p = C10;
        p = p * z + C9;
        p = p * z + C8;
        p = p * z + C7;
        p = p * z + C6;
        p = p * z + C5;
        p = p * z + C4;
        p = p * z + C3;
        p = p * z + C2;
        p = p * z + C1;
        return 1.0 + z * p;
    }

    /// <summary>Deterministic double sine.</summary>
    internal static double SinD(double x)
    {
        if (!double.IsFinite(x))
        {
            return double.NaN;
        }

        if (Math.Abs(x) < 1.0e-9)
        {
            return x;
        }

        int q = ReducePiO2(x, out double r);
        return q switch
        {
            0 => SinKernel(r),
            1 => CosKernel(r),
            2 => -SinKernel(r),
            _ => -CosKernel(r),
        };
    }

    /// <summary>Deterministic double cosine.</summary>
    internal static double CosD(double x)
    {
        if (!double.IsFinite(x))
        {
            return double.NaN;
        }

        int q = ReducePiO2(x, out double r);
        return q switch
        {
            0 => CosKernel(r),
            1 => -SinKernel(r),
            2 => -CosKernel(r),
            _ => SinKernel(r),
        };
    }

    /// <summary>Deterministic double tangent.</summary>
    internal static double TanD(double x)
    {
        if (!double.IsFinite(x))
        {
            return double.NaN;
        }

        if (Math.Abs(x) < 1.0e-9)
        {
            return x;
        }

        int q = ReducePiO2(x, out double r);
        double s = SinKernel(r);
        double c = CosKernel(r);
        return (q & 1) == 0 ? s / c : -c / s;
    }

    // Odd series coefficients 1/(2n+1) with alternating signs, for atan on |a| <= tan(pi/16).
    private const double A1 = -1.0 / 3.0;
    private const double A2 = 1.0 / 5.0;
    private const double A3 = -1.0 / 7.0;
    private const double A4 = 1.0 / 9.0;
    private const double A5 = -1.0 / 11.0;
    private const double A6 = 1.0 / 13.0;
    private const double A7 = -1.0 / 15.0;
    private const double A8 = 1.0 / 17.0;
    private const double A9 = -1.0 / 19.0;
    private const double A10 = 1.0 / 21.0;
    private const double A11 = -1.0 / 23.0;
    private const double A12 = 1.0 / 25.0;
    private const double A13 = -1.0 / 27.0;

    /// <summary>atan for 0 &lt;= a &lt;= 1.</summary>
    private static double AtanUnit(double a)
    {
        // Two half-angle steps: atan(a) = 2 atan(a / (1 + sqrt(1 + a^2))), leaving |a| <= tan(pi/16).
        a /= 1.0 + Math.Sqrt(1.0 + a * a);
        a /= 1.0 + Math.Sqrt(1.0 + a * a);
        double z = a * a;
        double p = A13;
        p = p * z + A12;
        p = p * z + A11;
        p = p * z + A10;
        p = p * z + A9;
        p = p * z + A8;
        p = p * z + A7;
        p = p * z + A6;
        p = p * z + A5;
        p = p * z + A4;
        p = p * z + A3;
        p = p * z + A2;
        p = p * z + A1;
        return 4.0 * (a + a * z * p);
    }

    /// <summary>Deterministic double arc tangent.</summary>
    internal static double AtanD(double x)
    {
        if (double.IsNaN(x))
        {
            return double.NaN;
        }

        double a = Math.Abs(x);
        double t;
        if (a <= 1.0)
        {
            t = AtanUnit(a);
        }
        else if (double.IsPositiveInfinity(a))
        {
            t = PiO2D;
        }
        else
        {
            t = (PiO2D - AtanUnit(1.0 / a)) + PiO2LoD;
        }

        return x < 0 ? -t : t;
    }

    /// <summary>Deterministic double atan2 with C semantics for zeros, infinities and NaN.</summary>
    internal static double Atan2D(double y, double x)
    {
        if (double.IsNaN(x) || double.IsNaN(y))
        {
            return double.NaN;
        }

        if (y == 0.0)
        {
            if (x > 0.0 || (x == 0.0 && !double.IsNegative(x)))
            {
                return y;
            }

            return double.IsNegative(y) ? -PiD : PiD;
        }

        if (x == 0.0)
        {
            return y > 0.0 ? PiO2D : -PiO2D;
        }

        double ax = Math.Abs(x);
        double ay = Math.Abs(y);
        double t;
        if (double.IsInfinity(ax))
        {
            if (double.IsInfinity(ay))
            {
                t = x > 0.0 ? PiO2D * 0.5 : PiD * 0.75;
            }
            else
            {
                t = x > 0.0 ? 0.0 : PiD;
            }
        }
        else if (double.IsInfinity(ay))
        {
            t = PiO2D;
        }
        else
        {
            if (ay <= ax)
            {
                t = AtanUnit(ay / ax);
            }
            else
            {
                t = (PiO2D - AtanUnit(ax / ay)) + PiO2LoD;
            }

            if (x < 0.0)
            {
                t = (PiD - t) + PiLoD;
            }
        }

        return y < 0.0 ? -t : t;
    }

    private const double E2 = 1.0 / 2.0;
    private const double E3 = 1.0 / 6.0;
    private const double E4 = 1.0 / 24.0;
    private const double E5 = 1.0 / 120.0;
    private const double E6 = 1.0 / 720.0;
    private const double E7 = 1.0 / 5040.0;
    private const double E8 = 1.0 / 40320.0;
    private const double E9 = 1.0 / 362880.0;
    private const double E10 = 1.0 / 3628800.0;
    private const double E11 = 1.0 / 39916800.0;
    private const double E12 = 1.0 / 479001600.0;
    private const double E13 = 1.0 / 6227020800.0;
    private const double E14 = 1.0 / 87178291200.0;
    private const double E15 = 1.0 / 1307674368000.0;
    private const double E16 = 1.0 / 20922789888000.0;
    private const double E17 = 1.0 / 355687428096000.0;

    /// <summary>Deterministic double exp.</summary>
    internal static double ExpD(double x)
    {
        if (double.IsNaN(x))
        {
            return double.NaN;
        }

        if (x > 709.79)
        {
            return double.PositiveInfinity;
        }

        if (x < -745.2)
        {
            return 0.0;
        }

        double k = Math.Round(x * InvLn2D, MidpointRounding.ToEven);
        double r = (x - k * Ln2HiD) - k * Ln2LoD;
        double p = E17;
        p = p * r + E16;
        p = p * r + E15;
        p = p * r + E14;
        p = p * r + E13;
        p = p * r + E12;
        p = p * r + E11;
        p = p * r + E10;
        p = p * r + E9;
        p = p * r + E8;
        p = p * r + E7;
        p = p * r + E6;
        p = p * r + E5;
        p = p * r + E4;
        p = p * r + E3;
        p = p * r + E2;
        p = p * r + 1.0;
        p = p * r + 1.0;
        return Math.ScaleB(p, (int)k);
    }

    private const double L1 = 1.0 / 3.0;
    private const double L2 = 1.0 / 5.0;
    private const double L3 = 1.0 / 7.0;
    private const double L4 = 1.0 / 9.0;
    private const double L5 = 1.0 / 11.0;
    private const double L6 = 1.0 / 13.0;
    private const double L7 = 1.0 / 15.0;
    private const double L8 = 1.0 / 17.0;
    private const double L9 = 1.0 / 19.0;
    private const double L10 = 1.0 / 21.0;
    private const double L11 = 1.0 / 23.0;
    private const double L12 = 1.0 / 25.0;

    /// <summary>Deterministic double natural log.</summary>
    internal static double LogD(double x)
    {
        if (double.IsNaN(x) || x < 0.0)
        {
            return double.NaN;
        }

        if (x == 0.0)
        {
            return double.NegativeInfinity;
        }

        if (double.IsPositiveInfinity(x))
        {
            return double.PositiveInfinity;
        }

        int e = Math.ILogB(x);
        double m = Math.ScaleB(x, -e);
        if (m > Sqrt2D)
        {
            m *= 0.5;
            e++;
        }

        // log(m) = 2 atanh(s) with s = (m - 1) / (m + 1), |s| <= 0.1716.
        double s = (m - 1.0) / (m + 1.0);
        double z = s * s;
        double p = L12;
        p = p * z + L11;
        p = p * z + L10;
        p = p * z + L9;
        p = p * z + L8;
        p = p * z + L7;
        p = p * z + L6;
        p = p * z + L5;
        p = p * z + L4;
        p = p * z + L3;
        p = p * z + L2;
        p = p * z + L1;
        double logm = 2.0 * s + 2.0 * s * z * p;
        return e * Ln2HiD + (logm + e * Ln2LoD);
    }

    /// <summary>Deterministic double pow with C semantics for the common special cases.</summary>
    internal static double PowD(double x, double y)
    {
        if (y == 0.0 || x == 1.0)
        {
            return 1.0;
        }

        if (double.IsNaN(x) || double.IsNaN(y))
        {
            return double.NaN;
        }

        bool yIsInt = Math.Floor(y) == y && double.IsFinite(y);
        bool yIsOdd = yIsInt && Math.Abs(y) < 9007199254740992.0 && Math.IEEERemainder(y, 2.0) != 0.0;

        if (x == 0.0)
        {
            if (y > 0.0)
            {
                return yIsOdd ? x : 0.0;
            }

            return yIsOdd && double.IsNegative(x) ? double.NegativeInfinity : double.PositiveInfinity;
        }

        if (double.IsInfinity(y))
        {
            double ax = Math.Abs(x);
            if (ax == 1.0)
            {
                return 1.0;
            }

            return (ax > 1.0) == (y > 0.0) ? double.PositiveInfinity : 0.0;
        }

        if (x < 0.0)
        {
            if (!yIsInt)
            {
                return double.NaN;
            }

            double r = ExpD(y * LogD(-x));
            return yIsOdd ? -r : r;
        }

        return ExpD(y * LogD(x));
    }
}
