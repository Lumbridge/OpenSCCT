using OpenScct.Core;
using OpenScct.Provenance;

namespace OpenScct.Game.Lighting;

/// <summary>An RGB light term in display units (1 is full white on a white texture before the 2x stage saturates).</summary>
public readonly record struct LightRgb(float R, float G, float B)
{
    public static LightRgb operator +(LightRgb a, LightRgb b) => new(a.R + b.R, a.G + b.G, a.B + b.B);

    public static LightRgb operator *(LightRgb a, float k) => new(a.R * k, a.G * k, a.B * k);

    /// <summary>Display luminance (0.3, 0.59, 0.11), as the night vision post pass weighs it.</summary>
    public float Luminance => 0.3f * R + 0.59f * G + 0.11f * B;

    /// <summary>Each channel at most <paramref name="cap"/>.</summary>
    public LightRgb Capped(float cap) => new(SvmMath.Min(R, cap), SvmMath.Min(G, cap), SvmMath.Min(B, cap));

    /// <summary>The brightest channel.</summary>
    public float Max => SvmMath.Max(R, SvmMath.Max(G, B));
}

/// <summary>
/// The shape of an LE_ESpotShadow light (Echelon spot: the merc torch, window and sun projectors). SpotWidth and
/// SpotHeight are half extents at <see cref="Range"/> (editor 0x111a5480 maps +-SpotWidth to the cookie's edges).
/// </summary>
/// <param name="Range">EchelonRange in uu: the light reaches this far along its axis.</param>
/// <param name="SpotWidth">Half the lit width at the range (along the light's Y axis).</param>
/// <param name="SpotHeight">Half the lit height at the range (along the light's Z axis).</param>
/// <param name="NearPlaneRatio">p: the frustum's apex sits p x Range / (1 - p) behind the light; from 0.999 the rays are parallel.</param>
/// <param name="NormalInfluence">EchelonNormalInfluence: 0 lights every surface fully, 1 by N.L.</param>
/// <param name="Attenuation">EchelonLightingMaterial.AttenuationType: 0 none, 1 linear, 2 late.</param>
public readonly record struct SpotShadowFrustum(float Range, float SpotWidth, float SpotHeight, float NearPlaneRatio, float NormalInfluence,
    int Attenuation);

/// <summary>
/// The rules the original lights actors (pawns, weapons, runtime props) with, Godot-free, shared by the renderer's
/// <c>ActorLighting</c> / <c>SpotShadowLight</c> / <c>WorldLights</c> and the bots' light field (docs/RENDERING.md,
/// "Actor lighting", "Dynamic lights", "Spot shadow lights"). An actor's light term is
/// <c>min(2, min(2, A + sum of the four strongest lights) + sum of the spot shadow lights)</c>: A the zone ambient plus
/// AmbientGlow (each channel at most 1), each light <c>k C S(d / R) max(0, N.L)</c>, each spot shadow light
/// <c>2 C' x cookie x attenuation x shadow x mix(1, N.L, influence)</c> inside its frustum.
/// </summary>
[PortOf("eng:Render.ActorLighting", Spec = "docs/RENDERING.md#actor-lighting")]
public static class ActorLightRules
{
    /// <summary>Lights that light one actor (Actor.MaxLights default).</summary>
    [Provenance(ProvenanceKind.Decoded, "Engine.u Actor.MaxLights default 4")]
    public const int MaxActorLights = 4;

    /// <summary>Point light gain: the D3D vertex light falloff f(d) = 2 S(d / R).</summary>
    [Provenance(ProvenanceKind.Decoded, "RenderingModeNormal::SetLight falloff f(d) = 2 S(d/R), editor 0x10f29790")]
    public const float PointLightGain = 2f;

    /// <summary>LE_Sunlight gain, unattenuated.</summary>
    [Provenance(ProvenanceKind.Decoded, "RenderingModeNormal::SetLight sun branch 1.75 x C")]
    public const float SunLightGain = 1.75f;

    /// <summary>FGetHSV at V 255: 0.8213.</summary>
    [Provenance(ProvenanceKind.Decoded, "FGetHSV, editor 0x11096ab0")]
    public const float HsvFullBrightness = 0.8213f;

    /// <summary>World light radius in uu per LightRadius unit: R = 25 x (LightRadius + 1).</summary>
    [Provenance(ProvenanceKind.Decoded, "AActor::WorldLightRadius, editor 0x10f57a20")]
    public const float LightRadiusUnits = 25f;

    /// <summary>Spot cone: phi = 1.15 acos((1 - LightCone / 256)^2).</summary>
    [Provenance(ProvenanceKind.Decoded, "RenderingModeNormal::SetLight spot branch")]
    public const float SpotPhiScale = 1.15f;

    /// <summary>Spot inner cone: theta = 0.05 phi.</summary>
    [Provenance(ProvenanceKind.Decoded, "RenderingModeNormal::SetLight spot branch")]
    public const float SpotThetaRatio = 0.05f;

    /// <summary>The actor light term saturates at 2 (the 2x modulation stage).</summary>
    [Provenance(ProvenanceKind.Decoded, "docs/RENDERING.md#actor-lighting: min(2, A + sum)")]
    public const float LightCap = 2f;

    /// <summary>The spot shadow light's gain (the Echelon light pass is not decoded).</summary>
    [Provenance(ProvenanceKind.Estimated, "docs/RENDERING.md#spot-shadow-lights: colour x 2 x cookie x attenuation")]
    public const float SpotShadowGain = 2f;

    /// <summary>Cap of a spot shadow light's brightest colour channel before the gain (the hue is kept).</summary>
    [Provenance(ProvenanceKind.Estimated, "docs/RENDERING.md#spot-shadow-lights: fixed-function colours clamp to [0, 1]; FactD hue match")]
    public const float SpotShadowColourCap = 1f;

    /// <summary>
    /// FDynamicLight's colour scale: FDynamicLight::Update (game 0x10a1de70, also run by the LE_ESpotShadow light's
    /// FDynamicLight made at 0x10992980) sets Color = FGetHSV x LightBrightness x 0.0019607844 (1 / 510) x the light
    /// type x LevelInfo.Brightness, half the brightness / 255 of the other light paths here.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "FDynamicLight::Update, game 0x10a1de70: Color = HSV x LightBrightness / 510")]
    public const float SpotShadowColourScale = 0.5f;

    /// <summary>NearPlaneRatio from which a spot shadow light's rays are parallel (an orthographic box).</summary>
    [Provenance(ProvenanceKind.Estimated, "FactD's Light17985 has 1.0; p / (1 - p) diverges")]
    public const float SpotParallelRatio = 0.999f;

    [Provenance(ProvenanceKind.Decoded, "Engine.u ELightEffect: 8 LE_StaticSpot")]
    public const int LE_StaticSpot = 8;

    [Provenance(ProvenanceKind.Decoded, "Engine.u ELightEffect: 12 LE_Spotlight")]
    public const int LE_Spotlight = 12;

    [Provenance(ProvenanceKind.Decoded, "Engine.u ELightEffect: 19 LE_Sunlight")]
    public const int LE_Sunlight = 19;

    [Provenance(ProvenanceKind.Decoded, "Engine.u ELightEffect: 20 LE_QuadraticNonIncidence")]
    public const int LE_QuadraticNonIncidence = 20;

    [Provenance(ProvenanceKind.Decoded, "Engine.u ELightEffect: 21 LE_PointAmbient")]
    public const int LE_PointAmbient = 21;

    [Provenance(ProvenanceKind.Decoded, "Engine.u ELightEffect: 22 LE_ESpotShadow")]
    public const int LE_ESpotShadow = 22;

    // ---- which lights ----------------------------------------------------------------------------------------

    /// <summary>
    /// True for a light that takes part in the actor light set: kept for run time (bApplyToInGameLighting or
    /// bDynamicLight), with a light type, not an LE_ESpotShadow light (those light actors through their own pass).
    /// </summary>
    public static bool LightsActors(bool applyInGame, bool dynamic, int lightType, int effect) =>
        (applyInGame || dynamic) && lightType != 0 && effect != LE_ESpotShadow;

    /// <summary>R = 25 x (LightRadius + 1).</summary>
    public static float WorldRadius(float lightRadius) => LightRadiusUnits * (lightRadius + 1f);

    public static bool IsSpot(int effect) => effect is LE_StaticSpot or LE_Spotlight;

    /// <summary>Full cone angle in radians: 1.15 acos((1 - LightCone / 256)^2).</summary>
    public static float SpotPhi(byte cone)
    {
        float k = 1f - (cone / 256f);
        return SpotPhiScale * SvmMath.Acos(SvmMath.Clamp(k * k, -1f, 1f));
    }

    /// <summary>
    /// FGetHSV(hue, saturation, 255) x brightness / 255: a hue triangle (red to green to blue, 85 steps a ramp) pulled
    /// toward white by saturation / 255, at the V 255 scale 0.8213.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "FGetHSV, editor 0x11096ab0; FDynamicLight::Update colour, editor 0x111af860")]
    public static LightRgb Colour(byte hue, byte saturation, float brightness)
    {
        int h = hue;
        float r, g, b;
        if (h < 86)
        {
            r = (85 - h) / 85f;
            g = h / 85f;
            b = 0f;
        }
        else if (h < 171)
        {
            r = 0f;
            g = (170 - h) / 85f;
            b = (h - 85) / 85f;
        }
        else
        {
            r = (h - 170) / 85f;
            g = 0f;
            b = (255 - h) / 84f;
        }

        float s = saturation / 255f;
        float k = HsvFullBrightness * (brightness / 255f);
        return new LightRgb((r + (s * (1f - r))) * k, (g + (s * (1f - g))) * k, (b + (s * (1f - b))) * k);
    }

    // ---- light types -----------------------------------------------------------------------------------------

    /// <summary>
    /// The light type's scale of a light's colour at scene time <paramref name="time"/> (FDynamicLight::Update, once a
    /// frame): pulse 0.6 + 0.39 sin(a), subtle pulse 0.9 + 0.09 sin(a) with a = t x 35 x 2 pi / max(1, period) +
    /// (phase &lt;&lt; 8); blink off while the integer part of t x 35 x 65536 / (period + 1) + (phase &lt;&lt; 8) is odd;
    /// flicker <paramref name="random"/> (0 below 0.5); strobe off while <paramref name="strobeOff"/>. The caller
    /// keeps the flicker's random value and the shared strobe switch.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "FDynamicLight::Update light types, game 0x10a1de70")]
    public static float Modulation(int lightType, float period, float phase, float time, float random = 1f, bool strobeOff = false)
    {
        switch (lightType)
        {
            case 2:
                return 0.6f + (0.39f * SvmMath.Sin(PulseAngle(period, phase, time)));
            case 3:
                double index = (time * 2293760.0 / ((int)period + 1)) + ((int)phase << 8);
                return ((long)index & 1) != 0 ? 0f : 1f;
            case 4:
                return random < 0.5f ? 0f : random;
            case 5:
                return strobeOff ? 0f : 1f;
            case 7:
                return 0.9f + (0.09f * SvmMath.Sin(PulseAngle(period, phase, time)));
            default:
                return 1f;
        }
    }

    /// <summary>
    /// The light type's scale averaged over time (pulse 0.6, subtle pulse 0.9, blink and strobe 0.5, flicker 0.375),
    /// for judgements that outlast a frame (the bots' route costs and hiding spots).
    /// </summary>
    [Provenance(ProvenanceKind.Estimated, "time means of the decoded light type curves (Modulation)")]
    public static float MeanModulation(int lightType) => lightType switch
    {
        2 => 0.6f,
        3 => 0.5f,
        4 => 0.375f,
        5 => 0.5f,
        7 => 0.9f,
        _ => 1f,
    };

    // time x 219.9115 (35 x 2 pi) / max(1, period) + (phase << 8), in double as the x87 code keeps it.
    private static float PulseAngle(float period, float phase, float time) =>
        (float)(((time * 219.9115 / System.Math.Max(1, (int)period)) + ((int)phase << 8)) % (2.0 * System.Math.PI));

    // ---- the four strongest lights -------------------------------------------------------------------------

    /// <summary>
    /// The rank of a light for an actor whose bounding sphere is (<paramref name="center"/>, <paramref name="radius"/>):
    /// LightBrightness x (1 - |L - C|^2 / (R + r)^2); suns rank above every other light; spots outside their cone
    /// (widened by the actor's angular size) score 0.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "actor light cache ranking, editor 0x111a3f60")]
    public static float Score(int effect, FVector location, FRotator rotation, float brightness, float lightRadius, byte cone,
        FVector center, float radius)
    {
        if (effect == LE_Sunlight)
        {
            return float.MaxValue;
        }

        float r = WorldRadius(lightRadius);
        float d2 = FVector.DistSquared(location, center);
        float reach = r + radius;
        float score = brightness * (1f - (d2 / (reach * reach)));
        if (score <= 0f)
        {
            return 0f;
        }

        if (IsSpot(effect))
        {
            FVector to = center - location;
            float len = to.Size();
            if (len > radius)
            {
                float c = (to | rotation.Vector()) / len;
                float slack = SvmMath.Asin(SvmMath.Clamp(radius / len, 0f, 1f));
                if (SvmMath.Acos(SvmMath.Clamp(c, -1f, 1f)) - slack > SpotPhi(cone) * 0.5f)
                {
                    return 0f;
                }
            }
        }

        return score;
    }

    /// <summary>
    /// One chosen light's term at <paramref name="point"/>: <paramref name="colour"/> (C x the light type's scale) x
    /// k S(d / R) max(0, N.L), k 2 for points and spots (S = 1 - 3x^2 + 2x^3, the spot ramp from phi to theta) and
    /// 1.75 for suns (no falloff). LE_QuadraticNonIncidence takes the D3D attenuation 1 / (8 d^2 / R^2) without the 2;
    /// LE_PointAmbient also adds its attenuated colour whatever the normal. <paramref name="normal"/> null counts the
    /// light as facing.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "RenderingModeNormal::SetLight, editor 0x10f29790 / 0x10f29940 (effects 20 and 21)")]
    public static LightRgb Term(int effect, FVector location, FRotator rotation, LightRgb colour, float lightRadius, byte cone,
        FVector point, FVector? normal)
    {
        if (effect == LE_Sunlight)
        {
            FVector toLight = -rotation.Vector();
            float nl = normal is { } ns ? SvmMath.Max(0f, ns | toLight) : 1f;
            return colour * (SunLightGain * nl);
        }

        FVector d = location - point;
        float dist = d.Size();
        float r = WorldRadius(lightRadius);
        if (dist >= r)
        {
            return default;
        }

        float x = dist / r;
        float att;
        float ambient = 0f;
        if (effect == LE_QuadraticNonIncidence)
        {
            att = 0.5f / SvmMath.Max(8f * x * x, 1e-3f);
        }
        else
        {
            att = 1f - (3f * x * x) + (2f * x * x * x);
            if (effect == LE_PointAmbient)
            {
                ambient = att;
            }
        }

        if (IsSpot(effect) && dist > 1e-3f)
        {
            float phi = SpotPhi(cone);
            float cosOuter = SvmMath.Cos(phi * 0.5f);
            float cosInner = SvmMath.Cos(phi * SpotThetaRatio * 0.5f);
            float cosAngle = ((point - location) | rotation.Vector()) / dist;
            att *= cosInner > cosOuter
                ? SvmMath.Clamp((cosAngle - cosOuter) / (cosInner - cosOuter), 0f, 1f)
                : (cosAngle >= cosOuter ? 1f : 0f);
        }

        float facing = 1f;
        if (normal is { } n && dist > 1e-3f)
        {
            facing = SvmMath.Max(0f, (n | d) / dist);
        }

        return colour * (PointLightGain * ((att * facing) + ambient));
    }

    // ---- spot shadow lights ----------------------------------------------------------------------------------

    /// <summary>
    /// A spot shadow light's colour before the gain: its FDynamicLight colour, FGetHSV(hue, saturation) x brightness / 510
    /// (<see cref="SpotShadowColourScale"/>), scaled down so its brightest channel is at most
    /// <see cref="SpotShadowColourCap"/> (the hue is kept).
    /// </summary>
    [Provenance(ProvenanceKind.Estimated, "colour decoded (game 0x10a1de70, / 510); the hue-keeping cap is docs/RENDERING.md#spot-shadow-lights (FactD window patches)")]
    public static LightRgb SpotShadowColour(byte hue, byte saturation, float brightness)
    {
        LightRgb c = Colour(hue, saturation, brightness) * SpotShadowColourScale;
        float top = c.Max;
        return top > SpotShadowColourCap ? c * (SpotShadowColourCap / top) : c;
    }

    /// <summary>The apex distance behind the light in uu (p x Range / (1 - p)), or null for parallel rays.</summary>
    [Provenance(ProvenanceKind.Decoded, "ESpotShadow light-space frustum, editor 0x111a5480")]
    public static float? ApexDistance(in SpotShadowFrustum f)
    {
        float p = SvmMath.Clamp(f.NearPlaneRatio, 0f, 1f);
        return p >= SpotParallelRatio ? null : p * f.Range / (1f - p);
    }

    /// <summary>
    /// The analytic attenuation ramps over t = depth past the light / Range: 0 none (1), 1 linear (1 - t), 2 late
    /// (1 until 0.9). The renderer reads Engine.linear_attenuation / late_attenuation from the pack when it has them.
    /// </summary>
    [Provenance(ProvenanceKind.Estimated, "SpotShadowLight.Ramps stand-ins for Engine.linear_attenuation / late_attenuation")]
    public static float SpotAttenuation(int type, float t) => type switch
    {
        0 => 1f,
        2 => t < 0.9f ? 1f : 0f,
        _ => SvmMath.Clamp(1f - t, 0f, 1f),
    };

    /// <summary>
    /// A spot shadow light's light on <paramref name="point"/> without its shadow test: zero outside the frustum
    /// (between the light and Range along its axis, inside +-SpotWidth x +-SpotHeight scaled to the depth from the
    /// apex), else <paramref name="colour"/> (the capped colour x the light type's scale) x 2 x attenuation x
    /// mix(1, N.L towards the apex, influence). The cookie is taken as 1 (its texture is not on the server).
    /// <paramref name="normal"/> null counts the light as facing. The caller tests the shadow (a line from the light).
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "frustum: editor 0x111a5480; the pass itself (gain, cookie, ramp) as docs/RENDERING.md#spot-shadow-lights")]
    public static LightRgb SpotShadowTerm(in SpotShadowFrustum f, FVector location, FRotator rotation, LightRgb colour, FVector point,
        FVector? normal)
    {
        rotation.GetAxes(out FVector ax, out FVector ay, out FVector az);
        FVector rel = point - location;
        float along = rel | ax;
        float range = SvmMath.Max(f.Range, 1f);
        if (along <= 0f || along >= range)
        {
            return default;
        }

        float? apex = ApexDistance(in f);
        float sx = rel | ay;
        float sy = rel | az;
        float w = SvmMath.Max(f.SpotWidth, 1f);
        float h = SvmMath.Max(f.SpotHeight, 1f);
        float nx, ny;
        if (apex is { } a)
        {
            float depth = a + along;
            float far = a + range;
            nx = sx * far / (depth * w);
            ny = sy * far / (depth * h);
        }
        else
        {
            nx = sx / w;
            ny = sy / h;
        }

        if (SvmMath.Abs(nx) >= 1f || SvmMath.Abs(ny) >= 1f)
        {
            return default;
        }

        float atten = SpotAttenuation(f.Attenuation, along / range);
        if (atten <= 0f)
        {
            return default;
        }

        float facing = 1f;
        if (normal is { } n)
        {
            FVector toLight;
            if (apex is { } ap)
            {
                FVector v = (location - (ax * ap)) - point;
                float len = v.Size();
                toLight = len > 1e-3f ? v * (1f / len) : -ax;
            }
            else
            {
                toLight = -ax;
            }

            float ndl = SvmMath.Max(0f, n | toLight);
            facing = SvmMath.Lerp(1f, ndl, f.NormalInfluence);
        }

        return colour * (SpotShadowGain * atten * facing);
    }

    /// <summary>
    /// The actor's light term: the light set (ambient plus the chosen lights) saturates at 2 first, the spot shadow
    /// lights add per pixel and the sum saturates again (svm_world: min(v_light + spot, 2)).
    /// </summary>
    public static LightRgb Combine(LightRgb lightSet, LightRgb spotShadow) => (lightSet.Capped(LightCap) + spotShadow).Capped(LightCap);
}
