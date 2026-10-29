using System;
using System.Numerics;
using System.Text.Json.Serialization;

namespace OpenScctGame.Options;

/// <summary>
/// The merc's custom crosshair (an OpenSCCT addition), modelled on CS2's crosshair settings since its 30/09/2026 update:
/// the same ten styles, sizes in pixels at a reference screen height, outline modes and colours, so a CS2 share code
/// (<c>CS...</c>) imports as it looks in CS2. Off by default: the original reticle draws. Either way the sound arrows,
/// the reticle message and the red hit flash stay.
/// </summary>
public sealed class CrosshairSettings
{
    /// <summary>The CS2 style names, by style number (cl_crosshairstyle).</summary>
    public static readonly string[] StyleNames =
    [
        "Dynamic cross", "Dynamic circle", "Dynamic cross (split)", "Static circle", "Static cross",
        "Static cross (shot feedback)", "Dot only", "Dynamic quad", "Static square", "Static quad",
    ];

    public static readonly string[] OutlineNames = ["None", "Full", "Half"];

    /// <summary>Draws this crosshair instead of the original reticle lines.</summary>
    [JsonPropertyName("custom")]
    public bool Custom { get; set; }

    /// <summary>0 to 9, <see cref="StyleNames"/>.</summary>
    [JsonPropertyName("style")]
    public int Style { get; set; } = 4;

    /// <summary>The crosshair follows the aim point (the weapon's kick) instead of staying at the fixed spot.</summary>
    [JsonPropertyName("follow_recoil")]
    public bool FollowRecoil { get; set; }

    [JsonPropertyName("center_dot")]
    public bool CenterDot { get; set; }

    /// <summary>No top arm.</summary>
    [JsonPropertyName("t_style")]
    public bool TStyle { get; set; }

    /// <summary>0 none, 1 full, 2 half (a shadow to the bottom right).</summary>
    [JsonPropertyName("outline_mode")]
    public int OutlineMode { get; set; } = 1;

    [JsonPropertyName("red")]
    public int Red { get; set; } = 50;

    [JsonPropertyName("green")]
    public int Green { get; set; } = 250;

    [JsonPropertyName("blue")]
    public int Blue { get; set; } = 50;

    [JsonPropertyName("alpha")]
    public int Alpha { get; set; } = 255;

    [JsonPropertyName("outline_red")]
    public int OutlineRed { get; set; }

    [JsonPropertyName("outline_green")]
    public int OutlineGreen { get; set; }

    [JsonPropertyName("outline_blue")]
    public int OutlineBlue { get; set; }

    [JsonPropertyName("outline_alpha")]
    public int OutlineAlpha { get; set; } = 255;

    /// <summary>Pixels (at <see cref="ScreenHeight"/>) between the centre and the arms, -3840 to 3840.</summary>
    [JsonPropertyName("gap")]
    public int Gap { get; set; } = 3;

    /// <summary>Arm length in pixels at <see cref="ScreenHeight"/>, 0 to 255.</summary>
    [JsonPropertyName("length")]
    public int Length { get; set; } = 7;

    /// <summary>Line thickness in pixels at <see cref="ScreenHeight"/>, 0 to 32.</summary>
    [JsonPropertyName("thickness")]
    public int Thickness { get; set; } = 2;

    /// <summary>The most the dynamic styles open, in pixels at <see cref="ScreenHeight"/>, 0 to 255.</summary>
    [JsonPropertyName("dynamic_spread_limit")]
    public int DynamicSpreadLimit { get; set; } = 181;

    /// <summary>Split style: how far the inner pieces travel before they split from the outer ones, 0 to 127.</summary>
    [JsonPropertyName("split_distance")]
    public int SplitDistance { get; set; } = 3;

    [JsonPropertyName("inner_split_alpha")]
    public float InnerSplitAlpha { get; set; } = 1f;

    [JsonPropertyName("outer_split_alpha")]
    public float OuterSplitAlpha { get; set; } = 0.5f;

    /// <summary>Split style: the share of each arm that stays behind as the outer piece, 0 to 1.</summary>
    [JsonPropertyName("split_size_ratio")]
    public float SplitSizeRatio { get; set; } = 0.3f;

    /// <summary>The screen height the pixel sizes are for (CS2 cl_crosshair_screen_height); they scale with the screen.</summary>
    [JsonPropertyName("screen_height")]
    public int ScreenHeight { get; set; } = 1080;

    /// <summary>Kept so a CS2 code round-trips; the merc has no iron-sight dot.</summary>
    [JsonPropertyName("scope_dot_scale")]
    public float ScopeDotScale { get; set; } = 1f;

    [JsonPropertyName("scope_dot_use_crosshair_color")]
    public bool ScopeDotUseCrosshairColor { get; set; }

    /// <summary>The original's red flash for 0.4 s after a hit, on the custom crosshair too.</summary>
    [JsonPropertyName("hit_flash")]
    public bool HitFlash { get; set; } = true;

    public bool IsDynamic => Style is 0 or 1 or 2 or 5 or 7;

    public CrosshairSettings Clone() => (CrosshairSettings)MemberwiseClone();

    /// <summary>Puts every value in the range CS2 allows.</summary>
    public void Clamp()
    {
        Style = Math.Clamp(Style, 0, 9);
        OutlineMode = Math.Clamp(OutlineMode, 0, 2);
        Red = Math.Clamp(Red, 0, 255);
        Green = Math.Clamp(Green, 0, 255);
        Blue = Math.Clamp(Blue, 0, 255);
        Alpha = Math.Clamp(Alpha, 0, 255);
        OutlineRed = Math.Clamp(OutlineRed, 0, 255);
        OutlineGreen = Math.Clamp(OutlineGreen, 0, 255);
        OutlineBlue = Math.Clamp(OutlineBlue, 0, 255);
        OutlineAlpha = Math.Clamp(OutlineAlpha, 0, 255);
        Gap = Math.Clamp(Gap, -3840, 3840);
        Length = Math.Clamp(Length, 0, 255);
        Thickness = Math.Clamp(Thickness, 0, 32);
        DynamicSpreadLimit = Math.Clamp(DynamicSpreadLimit, 0, 255);
        SplitDistance = Math.Clamp(SplitDistance, 0, 127);
        InnerSplitAlpha = Fraction(InnerSplitAlpha, 0f, 1f);
        OuterSplitAlpha = Fraction(OuterSplitAlpha, 0.3f, 1f);
        SplitSizeRatio = Fraction(SplitSizeRatio, 0f, 1f);
        ScreenHeight = Math.Clamp(ScreenHeight, 240, 65535);
        ScopeDotScale = Fraction(ScopeDotScale, 0.1f, 2f);
    }

    private static float Fraction(float v, float min, float max) => float.IsFinite(v) ? Math.Clamp(v, min, max) : min;

    // ---- CS2 share codes (cs2-v1) ----------------------------------------------------------------------------
    // The layout follows the csgo-sharecode library's cs2-v1 decoder (github.com/akiver/csgo-sharecode, src/crosshair.ts):
    // "CS" then 44 base-57 characters, least significant first, holding 32 bytes; byte 0 is the sum of the others.

    private const string Dictionary = "ABCDEFGHJKLMNOPQRSTUVWXYZabcdefhijkmnopqrstuvwxyz23456789";
    private const string Prefix = "CS";
    private const int CharCount = 44;
    private const int ByteLength = 32;

    /// <summary>This crosshair as a CS2 share code.</summary>
    public string ToShareCode()
    {
        CrosshairSettings c = Clone();
        c.Clamp();
        int bits = c.SplitDistance | (Steps(c.InnerSplitAlpha) << 7) | ((Steps(c.OuterSplitAlpha) - 30) << 14) |
            (Steps(c.SplitSizeRatio) << 21) | ((c.ScopeDotUseCrosshairColor ? 1 : 0) << 28);
        var b = new byte[ByteLength];
        b[1] = 1;
        b[2] = (byte)(c.ScreenHeight & 0xff);
        b[3] = (byte)(c.ScreenHeight >> 8);
        b[4] = (byte)(c.Style | (c.FollowRecoil ? 0x20 : 0) | (c.CenterDot ? 0x40 : 0) | (c.TStyle ? 0x80 : 0));
        b[5] = (byte)c.Red;
        b[6] = (byte)c.Green;
        b[7] = (byte)c.Blue;
        b[8] = (byte)c.Alpha;
        b[9] = (byte)c.OutlineRed;
        b[10] = (byte)c.OutlineGreen;
        b[11] = (byte)c.OutlineBlue;
        b[12] = (byte)c.OutlineAlpha;
        b[13] = (byte)(c.Thickness | (c.OutlineMode << 6));
        b[14] = (byte)(c.Gap & 0xff);
        b[15] = (byte)((c.Gap >> 8) & 0xff);
        b[16] = (byte)c.Length;
        b[17] = (byte)c.DynamicSpreadLimit;
        b[18] = (byte)(bits & 0xff);
        b[19] = (byte)((bits >> 8) & 0xff);
        b[20] = (byte)((bits >> 16) & 0xff);
        b[21] = (byte)((bits >> 24) & 0xff);
        b[22] = (byte)(Steps(c.ScopeDotScale) - 10);
        int sum = 0;
        for (int i = 1; i < ByteLength; i++)
        {
            sum += b[i];
        }

        b[0] = (byte)(sum & 0xff);

        BigInteger total = new(b, isUnsigned: true, isBigEndian: true);
        var chars = new char[CharCount];
        for (int i = 0; i < CharCount; i++)
        {
            total = BigInteger.DivRem(total, Dictionary.Length, out BigInteger rem);
            chars[i] = Dictionary[(int)rem];
        }

        return Prefix + new string(chars);
    }

    /// <summary>
    /// Reads a CS2 share code into a new crosshair (custom on, the hit flash kept from <paramref name="keep"/>), or
    /// null when it is not a valid cs2-v1 code. Older CSGO-xxxxx codes are not read: CS2 no longer imports them either.
    /// </summary>
    public static CrosshairSettings? FromShareCode(string code, CrosshairSettings? keep = null)
    {
        code = (code ?? "").Trim();
        if (code.Length != Prefix.Length + CharCount || !code.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return null;
        }

        BigInteger big = BigInteger.Zero;
        for (int i = code.Length - 1; i >= Prefix.Length; i--)
        {
            int d = Dictionary.IndexOf(code[i], StringComparison.Ordinal);
            if (d < 0)
            {
                return null;
            }

            big = big * Dictionary.Length + d;
        }

        byte[] raw = big.ToByteArray(isUnsigned: true, isBigEndian: true);
        if (raw.Length > ByteLength)
        {
            return null;
        }

        var b = new byte[ByteLength];
        raw.CopyTo(b, ByteLength - raw.Length);
        int sum = 0;
        for (int i = 1; i < ByteLength; i++)
        {
            sum += b[i];
        }

        int height = b[2] | (b[3] << 8);
        if (b[0] != (sum & 0xff) || b[1] != 1 || height == 0)
        {
            return null;
        }

        int bits = b[18] | (b[19] << 8) | (b[20] << 16) | (b[21] << 24);
        var c = new CrosshairSettings
        {
            Custom = true,
            HitFlash = keep?.HitFlash ?? true,
            Style = b[4] & 0x1f,
            FollowRecoil = (b[4] & 0x20) != 0,
            CenterDot = (b[4] & 0x40) != 0,
            TStyle = (b[4] & 0x80) != 0,
            OutlineMode = b[13] >> 6,
            Red = b[5],
            Green = b[6],
            Blue = b[7],
            Alpha = b[8],
            OutlineRed = b[9],
            OutlineGreen = b[10],
            OutlineBlue = b[11],
            OutlineAlpha = b[12],
            Gap = (short)(b[14] | (b[15] << 8)),
            Length = b[16],
            Thickness = b[13] & 0x3f,
            DynamicSpreadLimit = b[17],
            SplitDistance = bits & 0x7f,
            InnerSplitAlpha = Math.Min((bits >> 7) & 0x7f, 100) / 100f,
            OuterSplitAlpha = (Math.Min((bits >> 14) & 0x7f, 70) + 30) / 100f,
            SplitSizeRatio = Math.Min((bits >> 21) & 0x7f, 100) / 100f,
            ScreenHeight = height,
            ScopeDotScale = (b[22] + 10) / 100f,
            ScopeDotUseCrosshairColor = ((bits >> 28) & 1) == 1,
        };
        c.Clamp();
        return c;
    }

    private static int Steps(float v) => (int)MathF.Round(v * 100f);
}
