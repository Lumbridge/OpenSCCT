using System;
using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Provenance;

namespace OpenScct.Game.AI;

/// <summary>A light of the map as the actor lighting rule reads it (render/lighting.json, docs/RENDERING.md).</summary>
public readonly record struct AiLight(
    FVector Location,
    FRotator Rotation,
    float Brightness,
    byte Hue,
    byte Saturation,
    float Radius,
    byte Cone,
    int Effect,
    int ZoneIndex,
    bool OwnZoneOnly);

/// <summary>An RGB light term in display units.</summary>
public readonly record struct AiRgb(float R, float G, float B)
{
    public static AiRgb operator +(AiRgb a, AiRgb b) => new(a.R + b.R, a.G + b.G, a.B + b.B);

    public static AiRgb operator *(AiRgb a, float k) => new(a.R * k, a.G * k, a.B * k);

    /// <summary>Display luminance (0.3, 0.59, 0.11), as the night vision post pass weighs it.</summary>
    public float Luminance => 0.3f * R + 0.59f * G + 0.11f * B;

    public AiRgb Capped(float cap) => new(SvmMath.Min(R, cap), SvmMath.Min(G, cap), SvmMath.Min(B, cap));
}

/// <summary>
/// How lit a point is, by the rule the renderer lights pawns with (docs/RENDERING.md, "Actor lighting"): the zone
/// ambient plus the four strongest lights, <c>min(2, A + sum k C S(d / R) max(0, N.L))</c>, with S(x) = 1 - 3x^2 + 2x^3,
/// k 2 for point and spot lights and 1.75 for suns, C the light's FGetHSV colour x brightness / 255, R = 25
/// (LightRadius + 1). Lights are ranked by brightness x (1 - d^2 / (R + r)^2), suns first, spots outside their cone
/// score 0. The merc torch (an LE_ESpotShadow light) adds its spot with its shadow test. The level is read on the side
/// of the target that faces the viewer, so a spy lit from behind stays dark to a merc in front of it.
/// <para>Godot-free and deterministic; the renderer's <c>ActorLighting</c> is the same rule on the drawing side.</para>
/// </summary>
[PortOf("eng:Render.ActorLighting", Spec = "docs/RENDERING.md#actor-lighting")]
public sealed class AiLightField
{
    private readonly AiLight[] _lights;
    private readonly Dictionary<int, AiRgb> _ambient;
    private readonly Func<FVector, int>? _zoneOf;
    private readonly (float Score, int Index)[] _ranked;

    /// <summary>
    /// A field over <paramref name="lights"/> (lights that reach actors: on, applied in game, not ESpotShadow) and the
    /// zone ambients by zone index (display units, 0 to 1). <paramref name="zoneOf"/> gives a point's zone index (the
    /// collision BSP), or null when unknown.
    /// </summary>
    public AiLightField(IReadOnlyList<AiLight> lights, IReadOnlyDictionary<int, AiRgb>? zoneAmbient = null, Func<FVector, int>? zoneOf = null)
    {
        ArgumentNullException.ThrowIfNull(lights);
        _lights = new AiLight[lights.Count];
        for (int i = 0; i < lights.Count; i++)
        {
            _lights[i] = lights[i];
        }

        _ambient = new Dictionary<int, AiRgb>();
        if (zoneAmbient is not null)
        {
            foreach (KeyValuePair<int, AiRgb> kv in zoneAmbient)
            {
                _ambient[kv.Key] = kv.Value;
            }
        }

        _zoneOf = zoneOf;
        _ranked = new (float, int)[_lights.Length];
        HasData = _lights.Length > 0 || _ambient.Count > 0;
    }

    /// <summary>A field without light data: every point reads <see cref="UnknownLevel"/>.</summary>
    public static AiLightField Unknown { get; } = new([]);

    /// <summary>The level of a point when the map has no light data (a server with only the sim part).</summary>
    public const float UnknownLevel = 0.6f;

    /// <summary>True when the map's lights or zone ambients were read.</summary>
    public bool HasData { get; }

    public int LightCount => _lights.Length;

    /// <summary>The zone ambient at <paramref name="p"/> (black when unknown).</summary>
    public AiRgb AmbientAt(FVector p)
    {
        int zone = _zoneOf?.Invoke(p) ?? -1;
        return zone >= 0 && _ambient.TryGetValue(zone, out AiRgb a) ? a : default;
    }

    /// <summary>
    /// The light level (luminance of the capped term) on the side of a target at <paramref name="center"/> (bounding
    /// radius <paramref name="radius"/>) that faces <paramref name="viewer"/>.
    /// </summary>
    public float LevelAt(FVector center, float radius, FVector viewer)
    {
        if (!HasData)
        {
            return UnknownLevel;
        }

        FVector toViewer = viewer - center;
        float len = toViewer.Size();
        FVector normal = len > 1e-3f ? toViewer * (1f / len) : new FVector(0f, 0f, 1f);
        return Term(center, radius, normal).Luminance;
    }

    /// <summary>The light level a point receives from every side at once (the brightest side), for route costs.</summary>
    public float LevelAround(FVector center, float radius)
    {
        if (!HasData)
        {
            return UnknownLevel;
        }

        return Term(center, radius, null).Luminance;
    }

    /// <summary>The capped display term at the point; <paramref name="normal"/> null counts every light as facing.</summary>
    public AiRgb Term(FVector center, float radius, FVector? normal)
    {
        int zone = _zoneOf?.Invoke(center) ?? -1;
        AiRgb sum = zone >= 0 && _ambient.TryGetValue(zone, out AiRgb a) ? a : default;
        int n = 0;
        for (int i = 0; i < _lights.Length; i++)
        {
            ref readonly AiLight l = ref _lights[i];
            if (l.OwnZoneOnly && zone >= 0 && l.ZoneIndex >= 0 && l.ZoneIndex != zone)
            {
                continue;
            }

            float score = Score(in l, center, radius);
            if (score > 0f)
            {
                _ranked[n++] = (score, i);
            }
        }

        // Suns first, then by score; ties keep the map order (insertion sort: stable and allocation free).
        for (int i = 1; i < n; i++)
        {
            (float Score, int Index) item = _ranked[i];
            int j = i - 1;
            while (j >= 0 && Before(item, _ranked[j]))
            {
                _ranked[j + 1] = _ranked[j];
                j--;
            }

            _ranked[j + 1] = item;
        }

        int count = Math.Min(AiK.MaxActorLights, n);
        for (int i = 0; i < count; i++)
        {
            sum += Contribution(in _lights[_ranked[i].Index], center, normal);
        }

        return sum.Capped(AiK.LightCap);
    }

    private bool Before((float Score, int Index) a, (float Score, int Index) b)
    {
        bool sa = _lights[a.Index].Effect == AiK.LE_Sunlight;
        bool sb = _lights[b.Index].Effect == AiK.LE_Sunlight;
        if (sa != sb)
        {
            return sa;
        }

        return a.Score > b.Score;
    }

    /// <summary>LightBrightness x (1 - |L - C|^2 / (R + r)^2); spots outside their cone score 0.</summary>
    [Provenance(ProvenanceKind.Decoded, "actor light cache ranking, editor 0x111a3f60")]
    public static float Score(in AiLight l, FVector center, float radius)
    {
        if (l.Effect == AiK.LE_Sunlight)
        {
            return float.MaxValue;
        }

        float r = WorldRadius(in l);
        float d2 = FVector.DistSquared(l.Location, center);
        float reach = r + radius;
        float score = l.Brightness * (1f - (d2 / (reach * reach)));
        if (score <= 0f)
        {
            return 0f;
        }

        if (IsSpot(in l))
        {
            FVector to = center - l.Location;
            float len = to.Size();
            if (len > radius)
            {
                float c = (to | l.Rotation.Vector()) / len;
                float slack = SvmMath.Asin(SvmMath.Clamp(radius / len, 0f, 1f));
                if (SvmMath.Acos(SvmMath.Clamp(c, -1f, 1f)) - slack > SpotPhi(in l) * 0.5f)
                {
                    return 0f;
                }
            }
        }

        return score;
    }

    /// <summary>One light's term at the point: k C S(d / R) max(0, N.L), with the spot falloff from phi to theta.</summary>
    public static AiRgb Contribution(in AiLight l, FVector center, FVector? normal)
    {
        AiRgb c = Colour(l.Hue, l.Saturation, l.Brightness);
        if (l.Effect == AiK.LE_Sunlight)
        {
            FVector toLight = -l.Rotation.Vector();
            float nl = normal is { } ns ? SvmMath.Max(0f, ns | toLight) : 1f;
            return c * (AiK.SunLightGain * nl);
        }

        FVector d = l.Location - center;
        float dist = d.Size();
        float r = WorldRadius(in l);
        if (dist >= r)
        {
            return default;
        }

        float x = dist / r;
        float falloff = 1f - 3f * x * x + 2f * x * x * x;
        float facing = 1f;
        if (normal is { } n && dist > 1e-3f)
        {
            facing = SvmMath.Max(0f, (n | d) / dist);
        }

        float spot = 1f;
        if (IsSpot(in l) && dist > 1e-3f)
        {
            float phi = SpotPhi(in l);
            float cosOuter = SvmMath.Cos(phi * 0.5f);
            float cosInner = SvmMath.Cos(phi * 0.05f * 0.5f);
            float cosAngle = ((center - l.Location) | l.Rotation.Vector()) / dist;
            spot = cosInner > cosOuter ? SvmMath.Clamp((cosAngle - cosOuter) / (cosInner - cosOuter), 0f, 1f) : (cosAngle >= cosOuter ? 1f : 0f);
        }

        return c * (AiK.PointLightGain * falloff * facing * spot);
    }

    /// <summary>
    /// The merc torch's light on a target: colour x 2 x the linear attenuation (1 - d / range) inside the 400 x 400
    /// spot at range 1500, when <paramref name="clear"/> (the shadow test) says the line is open.
    /// </summary>
    public static float TorchLevel(FVector torch, FRotator aim, FVector target, bool clear)
    {
        if (!clear)
        {
            return 0f;
        }

        FVector d = target - torch;
        float along = d | aim.Vector();
        if (along <= 0f || along >= AiK.TorchRange)
        {
            return 0f;
        }

        // The spot's half size grows linearly to SpotSize / 2 at the range (a square frustum, read here as a cone).
        float lateral2 = d.SizeSquared() - along * along;
        float half = AiK.TorchSpotSize * 0.5f * (along / AiK.TorchRange);
        if (lateral2 > half * half)
        {
            return 0f;
        }

        AiRgb c = Colour(AiK.TorchHue, AiK.TorchSaturation, AiK.TorchBrightness);
        return (c * (AiK.TorchGain * (1f - along / AiK.TorchRange))).Luminance;
    }

    /// <summary>R = 25 x (LightRadius + 1).</summary>
    public static float WorldRadius(in AiLight l) => AiK.LightRadiusUnits * (l.Radius + 1f);

    public static bool IsSpot(in AiLight l) => l.Effect is AiK.LE_StaticSpot or AiK.LE_Spotlight;

    /// <summary>Full cone angle in radians: 1.15 acos((1 - LightCone / 256)^2).</summary>
    public static float SpotPhi(in AiLight l)
    {
        float k = 1f - (l.Cone / 256f);
        return AiK.SpotPhiScale * SvmMath.Acos(SvmMath.Clamp(k * k, -1f, 1f));
    }

    /// <summary>
    /// FGetHSV(hue, saturation, 255) x brightness / 255: a hue triangle pulled toward white by saturation / 255, at the
    /// V 255 scale 0.8213.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "FGetHSV, editor 0x11096ab0; FDynamicLight::Update colour, editor 0x111af860")]
    public static AiRgb Colour(byte hue, byte saturation, float brightness)
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
        float k = AiK.HsvFullBrightness * (brightness / 255f);
        return new AiRgb((r + s * (1f - r)) * k, (g + s * (1f - g)) * k, (b + s * (1f - b)) * k);
    }
}
