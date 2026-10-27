using System;
using OpenScct.Provenance;

namespace OpenScctGame.Render.SoftBodies;

/// <summary>
/// The level's 2D gradient noise the soft-body wind samples (0x1099e1f0 in the game): Perlin's noise2 over a 256 entry
/// permutation and gradient table, inputs offset by 4096, s-curve 3t^2 - 2t^3. The original fills its tables with
/// random values when the level starts, so these come from a fixed seed. Output is about -0.7 to 0.7.
/// </summary>
[Provenance(ProvenanceKind.Decoded, "0x1099e1f0: noise2 with +4096 offset, & 0xff wrap, t*t*(3 - 2t) blend")]
[Provenance(ProvenanceKind.Estimated, "The permutation and gradient tables (random per level in the original) come from a fixed seed")]
internal static class SoftBodyNoise
{
    private const int B = 256;
    private const float Offset = 4096f;
    private static readonly int[] P = new int[B + B + 2];
    private static readonly float[] G = new float[(B + B + 2) * 2];

    static SoftBodyNoise()
    {
        uint seed = 0x5EED5EEDu;
        uint Next()
        {
            seed = (seed * 1103515245u) + 12345u;
            return (seed >> 8) & 0xFFFFFF;
        }

        for (int i = 0; i < B; i++)
        {
            P[i] = i;
            float x = ((int)(Next() % (B + B)) - B) / (float)B;
            float y = ((int)(Next() % (B + B)) - B) / (float)B;
            float len = MathF.Sqrt((x * x) + (y * y));
            if (len < 1e-4f)
            {
                x = 1f;
                y = 0f;
                len = 1f;
            }

            G[i * 2] = x / len;
            G[(i * 2) + 1] = y / len;
        }

        for (int i = B - 1; i > 0; i--)
        {
            int j = (int)(Next() % (uint)(i + 1));
            (P[i], P[j]) = (P[j], P[i]);
        }

        for (int i = 0; i < B + 2; i++)
        {
            P[B + i] = P[i];
            G[(B + i) * 2] = G[i * 2];
            G[((B + i) * 2) + 1] = G[(i * 2) + 1];
        }
    }

    public static float Noise2(float x, float y)
    {
        float tx = x + Offset;
        int bx0 = (int)tx & (B - 1);
        int bx1 = (bx0 + 1) & (B - 1);
        float rx0 = tx - (int)tx;
        float rx1 = rx0 - 1f;
        float ty = y + Offset;
        int by0 = (int)ty & (B - 1);
        int by1 = (by0 + 1) & (B - 1);
        float ry0 = ty - (int)ty;
        float ry1 = ry0 - 1f;

        int i = P[bx0];
        int j = P[bx1];
        int b00 = P[i + by0];
        int b10 = P[j + by0];
        int b01 = P[i + by1];
        int b11 = P[j + by1];

        float sx = rx0 * rx0 * (3f - (2f * rx0));
        float sy = ry0 * ry0 * (3f - (2f * ry0));

        float u = (rx0 * G[b00 * 2]) + (ry0 * G[(b00 * 2) + 1]);
        float v = (rx1 * G[b10 * 2]) + (ry0 * G[(b10 * 2) + 1]);
        float a = u + (sx * (v - u));
        u = (rx0 * G[b01 * 2]) + (ry1 * G[(b01 * 2) + 1]);
        v = (rx1 * G[b11 * 2]) + (ry1 * G[(b11 * 2) + 1]);
        float b = u + (sx * (v - u));
        return a + (sy * (b - a));
    }
}
