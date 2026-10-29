using System;
using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Lighting;
using OpenScct.Game.SBase;
using OpenScct.Provenance;

namespace OpenScct.Game.AI;

/// <summary>
/// A map light of the actor light set (render/lighting.json, docs/RENDERING.md#actor-lighting): kept for run time
/// (bApplyToInGameLighting or bDynamicLight), with a light type, not LE_ESpotShadow.
/// </summary>
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
    bool OwnZoneOnly,
    int LightType = 1,
    float Period = 32f,
    float Phase = 0f,
    string? Name = null,
    bool InitiallyOn = true);

/// <summary>
/// A map LE_ESpotShadow light (window and sun projectors, searchlight patches: docs/RENDERING.md#spot-shadow-lights).
/// It lights a pawn inside its frustum where the line from the light is open; own-zone limits do not apply to actors.
/// </summary>
public readonly record struct AiSpotLight(
    FVector Location,
    FRotator Rotation,
    float Brightness,
    byte Hue,
    byte Saturation,
    SpotShadowFrustum Frustum,
    int LightType = 1,
    float Period = 32f,
    float Phase = 0f,
    string? Name = null,
    bool InitiallyOn = true);

/// <summary>An RGB light term in display units.</summary>
public readonly record struct AiRgb(float R, float G, float B)
{
    public static AiRgb operator +(AiRgb a, AiRgb b) => new(a.R + b.R, a.G + b.G, a.B + b.B);

    public static AiRgb operator *(AiRgb a, float k) => new(a.R * k, a.G * k, a.B * k);

    public static implicit operator AiRgb(LightRgb c) => new(c.R, c.G, c.B);

    public static implicit operator LightRgb(AiRgb c) => new(c.R, c.G, c.B);

    /// <summary>Display luminance (0.3, 0.59, 0.11), as the night vision post pass weighs it.</summary>
    public float Luminance => 0.3f * R + 0.59f * G + 0.11f * B;

    public AiRgb Capped(float cap) => new(SvmMath.Min(R, cap), SvmMath.Min(G, cap), SvmMath.Min(B, cap));
}

/// <summary>
/// How lit a pawn is, by the rules the renderer lights pawns with (<see cref="ActorLightRules"/>, docs/RENDERING.md
/// "Actor lighting" and "Spot shadow lights"): the zone ambient plus the four strongest map lights (in-game or
/// dynamic, on, own-zone lights only in their zone, scaled by their light type), saturating at 2, plus every spot
/// shadow light whose frustum holds the pawn and whose line to it is open (the map's projectors and the merc torches),
/// saturating at 2 again. The level is the luminance of that term on the side of the target that faces the viewer, so
/// a spy lit from behind stays dark to a merc in front of it. Map lights follow their STriggerLight switches.
/// <para>Godot-free and deterministic: the renderer's <c>ActorLighting</c> and <c>SpotShadowLight</c> use the same
/// <see cref="ActorLightRules"/>.</para>
/// </summary>
[PortOf("eng:Render.ActorLighting", Spec = "docs/RENDERING.md#actor-lighting")]
public sealed class AiLightField
{
    private readonly AiLight[] _lights;
    private readonly AiSpotLight[] _spots;
    private readonly bool[] _lightOn;
    private readonly bool[] _spotOn;
    private readonly Dictionary<int, AiRgb> _ambient;
    private readonly Func<FVector, int>? _zoneOf;
    private readonly Func<FVector, FVector, bool>? _lineOpen;
    private readonly (float Score, int Index)[] _ranked;
    private readonly List<(STriggerLight Actor, string Name, bool? Shown)> _switches = [];

    /// <summary>
    /// A field over <paramref name="lights"/> (the actor light set's lights) and the zone ambients by zone index
    /// (display units, 0 to 1). <paramref name="zoneOf"/> gives a point's zone index (the collision BSP), or null when
    /// unknown. <paramref name="spots"/> are the map's spot shadow lights, <paramref name="lineOpen"/> their shadow test
    /// (from, to: true when no geometry blocks the line); without it spot shadow lights are not shadowed.
    /// </summary>
    public AiLightField(IReadOnlyList<AiLight> lights, IReadOnlyDictionary<int, AiRgb>? zoneAmbient = null, Func<FVector, int>? zoneOf = null,
        IReadOnlyList<AiSpotLight>? spots = null, Func<FVector, FVector, bool>? lineOpen = null)
    {
        ArgumentNullException.ThrowIfNull(lights);
        _lights = new AiLight[lights.Count];
        _lightOn = new bool[lights.Count];
        for (int i = 0; i < lights.Count; i++)
        {
            _lights[i] = lights[i];
            _lightOn[i] = lights[i].InitiallyOn && lights[i].LightType != 0;
        }

        int spotCount = spots?.Count ?? 0;
        _spots = new AiSpotLight[spotCount];
        _spotOn = new bool[spotCount];
        for (int i = 0; i < spotCount; i++)
        {
            _spots[i] = spots![i];
            _spotOn[i] = _spots[i].InitiallyOn && _spots[i].LightType != 0;
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
        _lineOpen = lineOpen;
        _ranked = new (float, int)[_lights.Length];
        HasData = _lights.Length > 0 || _spots.Length > 0 || _ambient.Count > 0;
    }

    /// <summary>A field without light data: every point reads <see cref="UnknownLevel"/>.</summary>
    public static AiLightField Unknown { get; } = new([]);

    /// <summary>The level of a point when the map has no light data (a server with only the sim part).</summary>
    public const float UnknownLevel = 0.6f;

    /// <summary>True when the map's lights or zone ambients were read.</summary>
    public bool HasData { get; }

    public int LightCount => _lights.Length;

    public int SpotLightCount => _spots.Length;

    /// <summary>Scene time (Level.TimeSeconds) the light types are evaluated at by <see cref="LevelAt"/>.</summary>
    public float Time { get; set; }

    /// <summary>Switches the map light(s) named <paramref name="name"/> (an STriggerLight) on or off.</summary>
    public void SetOn(string name, bool on)
    {
        for (int i = 0; i < _lights.Length; i++)
        {
            if (string.Equals(_lights[i].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                _lightOn[i] = on;
            }
        }

        for (int i = 0; i < _spots.Length; i++)
        {
            if (string.Equals(_spots[i].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                _spotOn[i] = on;
            }
        }
    }

    /// <summary>
    /// Follows the trigger light <paramref name="actor"/> (map light name <paramref name="name"/>, its actor id): on
    /// while it lives and its LightType is not LT_None, as the client's DeviceViews switches the drawn light.
    /// </summary>
    public void AddSwitch(STriggerLight actor, string name)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(name);
        _switches.Add((actor, name, null));
    }

    /// <summary>Applies the followed trigger lights' state (once a tick).</summary>
    public void FollowSwitches()
    {
        for (int i = 0; i < _switches.Count; i++)
        {
            (STriggerLight actor, string name, bool? shown) = _switches[i];
            bool on = !actor.bDeleteMe && actor.LightType != ELightType.LT_None;
            if (shown != on)
            {
                _switches[i] = (actor, name, on);
                SetOn(name, on);
            }
        }
    }

    /// <summary>The zone ambient at <paramref name="p"/> (black when unknown).</summary>
    public AiRgb AmbientAt(FVector p)
    {
        int zone = _zoneOf?.Invoke(p) ?? -1;
        return zone >= 0 && _ambient.TryGetValue(zone, out AiRgb a) ? a : default;
    }

    /// <summary>
    /// The light level (luminance of the term) on the side of a target at <paramref name="center"/> (bounding radius
    /// <paramref name="radius"/>) that faces <paramref name="viewer"/>, now. <paramref name="torches"/> are merc torches
    /// that are on (the light's place and aim, and whether the line from it to the target is open; the caller leaves out the
    /// target's own).
    /// </summary>
    public float LevelAt(FVector center, float radius, FVector viewer, IReadOnlyList<(FVector Location, FRotator Aim, bool Clear)>? torches = null)
    {
        FVector toViewer = viewer - center;
        float len = toViewer.Size();
        FVector normal = len > 1e-3f ? toViewer * (1f / len) : new FVector(0f, 0f, 1f);
        if (!HasData)
        {
            float torch = 0f;
            if (torches is not null)
            {
                foreach ((FVector at, FRotator aim, bool clear) in torches)
                {
                    torch += TorchTerm(at, aim, center, normal, clear).Luminance;
                }
            }

            return SvmMath.Min(ActorLightRules.LightCap, UnknownLevel + torch);
        }

        return Evaluate(center, radius, normal, mean: false, torches).Luminance;
    }

    /// <summary>
    /// The light level a point receives from every side at once (the brightest side), its animated lights at their
    /// mean, for route costs and hiding spots.
    /// </summary>
    public float LevelAround(FVector center, float radius)
    {
        if (!HasData)
        {
            return UnknownLevel;
        }

        return Evaluate(center, radius, null, mean: true, null).Luminance;
    }

    /// <summary>The term at the point now; <paramref name="normal"/> null counts every light as facing.</summary>
    public AiRgb Term(FVector center, float radius, FVector? normal) => Evaluate(center, radius, normal, mean: false, null);

    private LightRgb Evaluate(FVector center, float radius, FVector? normal, bool mean, IReadOnlyList<(FVector Location, FRotator Aim, bool Clear)>? torches)
    {
        int zone = _zoneOf?.Invoke(center) ?? -1;
        LightRgb set = zone >= 0 && _ambient.TryGetValue(zone, out AiRgb a) ? a : default;
        int n = 0;
        for (int i = 0; i < _lights.Length; i++)
        {
            if (!_lightOn[i])
            {
                continue;
            }

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

        int count = Math.Min(ActorLightRules.MaxActorLights, n);
        for (int i = 0; i < count; i++)
        {
            ref readonly AiLight l = ref _lights[_ranked[i].Index];
            float scale = mean ? ActorLightRules.MeanModulation(l.LightType) : Modulation(l.LightType, l.Period, l.Phase);
            set += (LightRgb)Contribution(in l, center, normal) * scale;
        }

        LightRgb spot = default;
        for (int i = 0; i < _spots.Length; i++)
        {
            if (!_spotOn[i])
            {
                continue;
            }

            ref readonly AiSpotLight s = ref _spots[i];
            float scale = mean ? ActorLightRules.MeanModulation(s.LightType) : Modulation(s.LightType, s.Period, s.Phase);
            if (scale <= 0f)
            {
                continue;
            }

            LightRgb c = ActorLightRules.SpotShadowColour(s.Hue, s.Saturation, s.Brightness) * scale;
            LightRgb term = ActorLightRules.SpotShadowTerm(s.Frustum, s.Location, s.Rotation, c, center, normal);
            if (term.Max > 0f && Open(s.Location, center))
            {
                spot += term;
            }
        }

        if (torches is not null)
        {
            foreach ((FVector at, FRotator aim, bool clear) in torches)
            {
                spot += (LightRgb)TorchTerm(at, aim, center, normal, clear);
            }
        }

        return ActorLightRules.Combine(set, spot);
    }

    private bool Open(FVector from, FVector to) => _lineOpen is null || _lineOpen(from, to);

    // Flicker and strobe are per-frame noise on screen; the bots take their steady part (flicker at its mean, strobe on).
    private float Modulation(int lightType, float period, float phase) => lightType == 4
        ? ActorLightRules.MeanModulation(4)
        : ActorLightRules.Modulation(lightType, period, phase, Time);

    private bool Before((float Score, int Index) a, (float Score, int Index) b)
    {
        bool sa = _lights[a.Index].Effect == ActorLightRules.LE_Sunlight;
        bool sb = _lights[b.Index].Effect == ActorLightRules.LE_Sunlight;
        if (sa != sb)
        {
            return sa;
        }

        return a.Score > b.Score;
    }

    /// <summary>LightBrightness x (1 - |L - C|^2 / (R + r)^2); spots outside their cone score 0.</summary>
    public static float Score(in AiLight l, FVector center, float radius) =>
        ActorLightRules.Score(l.Effect, l.Location, l.Rotation, l.Brightness, l.Radius, l.Cone, center, radius);

    /// <summary>One light's term at the point at full brightness (<see cref="ActorLightRules.Term"/>).</summary>
    public static AiRgb Contribution(in AiLight l, FVector center, FVector? normal) =>
        ActorLightRules.Term(l.Effect, l.Location, l.Rotation, ActorLightRules.Colour(l.Hue, l.Saturation, l.Brightness), l.Radius, l.Cone,
            center, normal);

    /// <summary>The merc torch's frustum (SGadTorche, spec/equipment/merc-flashlight.md): linear, no normal influence.</summary>
    public static SpotShadowFrustum TorchFrustum { get; } =
        new(AiK.TorchRange, AiK.TorchSpotSize, AiK.TorchSpotSize, AiK.TorchNearPlaneRatio, 0f, 1);

    /// <summary>
    /// The merc torch's light on a target as the spot shadow light draws it: its capped colour x 2 x the linear
    /// attenuation inside its frustum (+-400 uu at 1500 uu), when <paramref name="clear"/> (the shadow test) says the
    /// line is open.
    /// </summary>
    public static AiRgb TorchTerm(FVector torch, FRotator aim, FVector target, FVector? normal, bool clear)
    {
        if (!clear)
        {
            return default;
        }

        LightRgb c = ActorLightRules.SpotShadowColour(AiK.TorchHue, AiK.TorchSaturation, AiK.TorchBrightness);
        return ActorLightRules.SpotShadowTerm(TorchFrustum, torch, aim, c, target, normal);
    }

    /// <summary>The luminance of <see cref="TorchTerm"/> facing the torch.</summary>
    public static float TorchLevel(FVector torch, FRotator aim, FVector target, bool clear) =>
        TorchTerm(torch, aim, target, null, clear).Luminance;

    /// <summary>R = 25 x (LightRadius + 1).</summary>
    public static float WorldRadius(in AiLight l) => ActorLightRules.WorldRadius(l.Radius);

    public static bool IsSpot(in AiLight l) => ActorLightRules.IsSpot(l.Effect);

    /// <summary>Full cone angle in radians: 1.15 acos((1 - LightCone / 256)^2).</summary>
    public static float SpotPhi(in AiLight l) => ActorLightRules.SpotPhi(l.Cone);

    /// <summary>FGetHSV(hue, saturation, 255) x brightness / 255 (<see cref="ActorLightRules.Colour"/>).</summary>
    public static AiRgb Colour(byte hue, byte saturation, float brightness) => ActorLightRules.Colour(hue, saturation, brightness);
}
