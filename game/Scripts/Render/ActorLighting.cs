using System;
using System.Collections.Generic;
using Godot;
using OpenScct.Core;
using OpenScct.Provenance;
using OpenScctGame.Render.Data;

namespace OpenScctGame.Render;

/// <summary>
/// The original's lighting of actors (pawns, weapons, runtime props and static meshes without baked colours):
/// <c>display = texture x min(2, A + sum k C S(d / R) max(0, N.L))</c> over the four strongest lights, with
/// S(x) = 1 - 3x^2 + 2x^3, k = 2 for point lights and 1.75 for suns, C the light's FGetHSV colour x brightness / 255
/// and A the zone ambient plus AmbientGlow. The light set is chosen on the CPU and handed to the template as
/// instance uniforms (svm_ambient, svm_l0..3_pos/col/spot). docs/RENDERING.md#actor-lighting.
/// </summary>
[PortOf("eng:Render.ActorLighting", Spec = "docs/RENDERING.md#actor-lighting")]
public sealed class ActorLighting
{
    private static readonly StringName AmbientParam = "svm_ambient";
    private static readonly StringName[] PosParams = [new("svm_l0_pos"), new("svm_l1_pos"), new("svm_l2_pos"), new("svm_l3_pos")];
    private static readonly StringName[] ColParams = [new("svm_l0_col"), new("svm_l1_col"), new("svm_l2_col"), new("svm_l3_col")];
    private static readonly StringName[] SpotParams = [new("svm_l0_spot"), new("svm_l1_spot"), new("svm_l2_spot"), new("svm_l3_spot")];

    private readonly List<LightEntry> _lights = [];
    private readonly List<(float Score, LightEntry Light)> _ranked = [];

    public ActorLighting(LightingDoc lighting)
    {
        ArgumentNullException.ThrowIfNull(lighting);
        foreach (LightEntry l in lighting.Lights)
        {
            // Lights that reach actors: bApplyToInGameLighting, with a light type, and not the runtime ESpotShadow
            // projectors, which have their own path. A light not initially on (STriggerLight.bInitialyOn) starts off.
            if (l.ApplyInGame && l.LightType != 0 && l.Effect != RenderK.LE_ESpotShadow)
            {
                _lights.Add(l);
                if (!l.InitiallyOn)
                {
                    _off.Add(l.Name);
                }
            }
        }
    }

    private readonly HashSet<string> _off = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<LightEntry> _spawned = [];

    public int LightCount => _lights.Count;

    /// <summary>Lights spawned during play that light actors now (see <see cref="SetSpawned"/>).</summary>
    public int SpawnedCount => _spawned.Count;

    /// <summary>
    /// The dynamic lights spawned during play this frame (the effect lights: SFlashLight and kin, muzzle flashes, the
    /// flare), each at the brightness it has now. They are ranked with the map's lights for every actor, as UE2's
    /// dynamic lights reach actors as well as surfaces. Replaces the previous frame's set.
    /// </summary>
    public void SetSpawned(IEnumerable<LightEntry> lights)
    {
        ArgumentNullException.ThrowIfNull(lights);
        _spawned.Clear();
        foreach (LightEntry l in lights)
        {
            if (l.LightType != 0 && l.Effect != RenderK.LE_ESpotShadow)
            {
                _spawned.Add(l);
            }
        }
    }

    /// <summary>Scene time (Level.TimeSeconds) the light types are evaluated at; the map sets it each frame.</summary>
    public float Time { get; set; }

    /// <summary>Switches a light on or off for actors (a triggered STriggerLight); takes effect at the next <see cref="Select"/>.</summary>
    public void SetOn(string name, bool on)
    {
        if (on)
        {
            _off.Remove(name);
        }
        else
        {
            _off.Add(name);
        }
    }

    /// <summary>Moves a light (one riding a mover or a bone); takes effect at the next <see cref="Select"/>.</summary>
    public void SetPlace(string name, FVector location, FRotator rotation)
    {
        for (int i = 0; i < _lights.Count; i++)
        {
            if (string.Equals(_lights[i].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                _lights[i] = _lights[i] with { Location = location, Rotation = rotation };
            }
        }
    }

    /// <summary>
    /// The light set of an actor whose bounding sphere is (<paramref name="center"/>, <paramref name="radius"/>) in
    /// uu. <paramref name="ambient"/> is A in display units (zone ambient plus AmbientGlow / 255); <paramref name="zone"/>
    /// the actor's zone index (-1 unknown).
    /// </summary>
    public ActorLightSet Select(FVector center, float radius, Vector3 ambient, int zone = -1)
    {
        _ranked.Clear();
        foreach (LightEntry l in _lights)
        {
            if (_off.Count > 0 && _off.Contains(l.Name))
            {
                continue;
            }

            Rank(l, center, radius, zone);
        }

        foreach (LightEntry l in _spawned)
        {
            Rank(l, center, radius, zone);
        }

        // Suns always first, then by score; ties keep the map order.
        _ranked.Sort((a, b) =>
        {
            bool sa = a.Light.Effect == RenderK.LE_Sunlight;
            bool sb = b.Light.Effect == RenderK.LE_Sunlight;
            return sa != sb ? (sa ? -1 : 1) : b.Score.CompareTo(a.Score);
        });

        var set = new ActorLightSet { Ambient = ambient };
        int n = Math.Min(RenderK.MaxActorLights, _ranked.Count);
        for (int i = 0; i < n; i++)
        {
            // Ranked by LightBrightness alone; the light type scales the colour (FDynamicLight::Update).
            LightEntry l = _ranked[i].Light;
            set.Lights[i] = PackWorld(l, WorldLights.Modulation(l, Time));
            set.Names[i] = _ranked[i].Light.Name;
        }

        set.Count = n;
        return set;
    }

    private void Rank(LightEntry l, FVector center, float radius, int zone)
    {
        // bAffectOwnZoneOnly lights reach only actors in their zone.
        if (l.OwnZoneOnly && zone >= 0 && l.ZoneIndex >= 0 && l.ZoneIndex != zone)
        {
            return;
        }

        float score = Score(l, center, radius);
        if (score > 0f)
        {
            _ranked.Add((score, l));
        }
    }

    /// <summary>LightBrightness x (1 - |L - C|^2 / (R + r)^2); spots outside their cone score 0.</summary>
    [Provenance(ProvenanceKind.Decoded, "actor light cache ranking, editor 0x111a3f60")]
    public static float Score(LightEntry l, FVector center, float radius)
    {
        if (l.Effect == RenderK.LE_Sunlight)
        {
            return float.MaxValue;
        }

        float r = WorldRadius(l);
        float d2 = FVector.DistSquared(l.Location, center);
        float reach = r + radius;
        float score = l.Brightness * (1f - (d2 / (reach * reach)));
        if (score <= 0f)
        {
            return 0f;
        }

        if (IsSpot(l))
        {
            FVector dir = l.Rotation.Vector();
            FVector to = center - l.Location;
            float len = to.Size();
            if (len > radius)
            {
                float cosHalfPhi = MathF.Cos(SpotPhi(l) * 0.5f);
                float c = (to | dir) / len;
                // Widen the cone by the actor's angular size so a sphere half in the cone still counts.
                float slack = MathF.Asin(Math.Clamp(radius / len, 0f, 1f));
                if (MathF.Acos(Math.Clamp(c, -1f, 1f)) - slack > MathF.Acos(cosHalfPhi))
                {
                    return 0f;
                }
            }
        }

        return score;
    }

    /// <summary>R = 25 x (LightRadius + 1).</summary>
    public static float WorldRadius(LightEntry l) => RenderK.LightRadiusUnits * (l.Radius + 1f);

    public static bool IsSpot(LightEntry l) => l.Effect is RenderK.LE_StaticSpot or RenderK.LE_Spotlight;

    /// <summary>Full cone angle in radians: 1.15 acos((1 - LightCone / 256)^2).</summary>
    public static float SpotPhi(LightEntry l)
    {
        float k = 1f - (l.Cone / 256f);
        return RenderK.SpotPhiScale * MathF.Acos(Math.Clamp(k * k, -1f, 1f));
    }

    /// <summary>
    /// FGetHSV(hue, saturation, 255) x brightness / 255: a hue triangle (red to green to blue, 85 steps a ramp) pulled
    /// toward white by saturation / 255, at the V 255 scale 0.8213.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "FGetHSV, editor 0x11096ab0; FDynamicLight::Update colour, editor 0x111af860")]
    public static Vector3 Colour(LightEntry l)
    {
        int h = l.Hue;
        Vector3 hue = h < 86 ? new Vector3((85 - h) / 85f, h / 85f, 0f)
            : h < 171 ? new Vector3(0f, (170 - h) / 85f, (h - 85) / 85f)
            : new Vector3((h - 170) / 85f, 0f, (255 - h) / 84f);
        float s = l.Saturation / 255f;
        Vector3 c = hue + (s * (Vector3.One - hue));
        return c * RenderK.HsvFullBrightness * (l.Brightness / 255f);
    }

    /// <summary>
    /// A light in the template's layout, its colour scaled by <paramref name="brightness"/> (light type modulation).
    /// A point light's spot.w carries its effect for the actor term: -2 plain, -3 LE_QuadraticNonIncidence (D3D
    /// attenuation 1 / (8 d^2 / R^2)), -4 LE_PointAmbient (the light's colour also set as its D3D ambient, which adds
    /// C x attenuation whatever the normal).
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "RenderingModeNormal::SetLight effects 20 and 21, editor 0x10f29940 (bridge docs/LIGHTING_BALANCE.md)")]
    public static PackedLight PackWorld(LightEntry l, float brightness)
    {
        ArgumentNullException.ThrowIfNull(l);
        Vector3 colour = Colour(l) * brightness;
        if (l.Effect == RenderK.LE_Sunlight)
        {
            // Directional along the light's rotation: the vector to the light is the reverse of its facing.
            Vector3 toLight = -UeSpace.DirectionToGodot(l.Rotation.Vector());
            return new PackedLight(new Vector4(toLight.X, toLight.Y, toLight.Z, -1f),
                new Vector4(colour.X * RenderK.SunLightGain, colour.Y * RenderK.SunLightGain, colour.Z * RenderK.SunLightGain, 1f),
                new Vector4(0f, 0f, 0f, -2f));
        }

        Vector3 p = UeSpace.ToGodot(l.Location);
        float radius = UeSpace.LengthToGodot(WorldRadius(l));
        Vector4 spot = new(0f, 0f, 0f, l.Effect switch
        {
            RenderK.LE_QuadraticNonIncidence => -3f,
            RenderK.LE_PointAmbient => -4f,
            _ => -2f,
        });
        float cosTheta = 1f;
        if (IsSpot(l))
        {
            Vector3 dir = UeSpace.DirectionToGodot(l.Rotation.Vector());
            float phi = SpotPhi(l);
            spot = new Vector4(dir.X, dir.Y, dir.Z, MathF.Cos(phi * 0.5f));
            cosTheta = MathF.Cos(phi * RenderK.SpotThetaRatio * 0.5f);
        }

        return new PackedLight(new Vector4(p.X, p.Y, p.Z, radius),
            new Vector4(colour.X * RenderK.PointLightGain, colour.Y * RenderK.PointLightGain, colour.Z * RenderK.PointLightGain, cosTheta),
            spot);
    }

    /// <summary>Writes a light set onto a drawn instance (its material must use the actor-lit template).</summary>
    public static void Apply(GeometryInstance3D node, ActorLightSet set)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(set);
        node.SetInstanceShaderParameter(AmbientParam, new Vector4(set.Ambient.X, set.Ambient.Y, set.Ambient.Z, 0f));
        for (int i = 0; i < RenderK.MaxActorLights; i++)
        {
            PackedLight p = i < set.Count ? set.Lights[i] : default;
            node.SetInstanceShaderParameter(PosParams[i], p.Pos);
            node.SetInstanceShaderParameter(ColParams[i], p.Col);
            node.SetInstanceShaderParameter(SpotParams[i], i < set.Count ? p.Spot : new Vector4(0f, 0f, 0f, -2f));
        }
    }
}

/// <summary>A light packed for the template: position or direction, gain x colour, spot cone.</summary>
public readonly record struct PackedLight(Vector4 Pos, Vector4 Col, Vector4 Spot);

/// <summary>The ambient and up to four lights of one actor.</summary>
public sealed class ActorLightSet
{
    public Vector3 Ambient { get; init; }

    public PackedLight[] Lights { get; } = new PackedLight[RenderK.MaxActorLights];

    public string?[] Names { get; } = new string?[RenderK.MaxActorLights];

    public int Count { get; set; }
}
