using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Godot;
using OpenScct.Core;
using OpenScct.Game.Lighting;
using OpenScct.Provenance;
using OpenScctGame.Render.Data;

namespace OpenScctGame.Render;

/// <summary>
/// The original's lighting of actors (pawns, weapons, runtime props and static meshes without baked colours), by the
/// shared <see cref="ActorLightRules"/> (which the bots' light field also reads):
/// <c>display = texture x min(2, A + sum C attenuation(d) max(0, N.L))</c> over the four strongest lights.
/// Ordinary point/spot attenuation is the original D3D reciprocal-linear fit to 2 S(d/R) over the actor's sphere;
/// suns use 1.75, C is the light's FGetHSV colour x brightness / 255, A is zone ambient plus AmbientGlow. The light set is chosen on the CPU and handed to the template as
/// instance uniforms (svm_ambient, svm_l0..3_pos/col/spot). docs/RENDERING.md#actor-lighting.
/// </summary>
[PortOf("eng:Render.ActorLighting", Spec = "docs/RENDERING.md#actor-lighting")]
public sealed class ActorLighting
{
    private static readonly StringName AmbientParam = "svm_ambient";
    private static readonly StringName[] PosParams = [new("svm_l0_pos"), new("svm_l1_pos"), new("svm_l2_pos"), new("svm_l3_pos")];
    private static readonly StringName[] ColParams = [new("svm_l0_col"), new("svm_l1_col"), new("svm_l2_col"), new("svm_l3_col")];
    private static readonly StringName[] SpotParams = [new("svm_l0_spot"), new("svm_l1_spot"), new("svm_l2_spot"), new("svm_l3_spot")];
    // This is the sole writer of the actor-light instance uniforms. Retain their values independently of the
    // caller's mutable light set, and let freed/replaced geometry disappear without keeping its node alive.
    private static readonly ConditionalWeakTable<GeometryInstance3D, AppliedLights> Applied = new();
    private sealed class AppliedLights
    {
        public bool Known;
        public Vector4 Ambient;
        public readonly PackedLight[] Lights = new PackedLight[RenderK.MaxActorLights];
    }

    /// <summary>Actual native uniform writes, for the differential upload smoke.</summary>
    internal static long UniformWrites { get; private set; }

    private readonly List<LightEntry> _lights = [];
    private readonly List<(float Score, LightEntry Light)> _ranked = [];
    private readonly Dictionary<string, List<int>> _byName = new(StringComparer.OrdinalIgnoreCase);
    private readonly LightInfluenceIndex? _index;
    private readonly List<int> _candidates = [];
    private readonly List<int> _moved = [];
    private readonly bool[] _hasMoved;

    private readonly LightingDoc _source;
    private ActorLighting? _smokeLighting;
    private readonly Dictionary<string, bool> _switches = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (FVector Location, FRotator Rotation)> _places = new(StringComparer.OrdinalIgnoreCase);

    public ActorLighting(LightingDoc lighting) : this(lighting, false) { }

    private ActorLighting(LightingDoc lighting, bool includeBaked)
    {
        ArgumentNullException.ThrowIfNull(lighting);
        _source = lighting;
        foreach (LightEntry l in lighting.Lights)
        {
            // Lights that reach actors: the ones the game keeps for run time, bApplyToInGameLighting or bDynamicLight
            // (as WorldLights), with a light type, and not the runtime ESpotShadow projectors, which have their own
            // path. A light not initially on (STriggerLight.bInitialyOn) starts off. Dynamic lights without the in-game
            // flag were left out until AquaE's kiosk (SGamePlayObject2126, beside STriggerLight303) drew black.
            if (ActorLightRules.LightsActors(l.ApplyInGame || (includeBaked && l.ApplyStatic), l.Dynamic, l.LightType, l.Effect))
            {
                if (!_byName.TryGetValue(l.Name, out List<int>? sameName))
                {
                    sameName = [];
                    _byName.Add(l.Name, sameName);
                }
                sameName.Add(_lights.Count);
                _lights.Add(l);
                if (!l.InitiallyOn)
                {
                    _off.Add(l.Name);
                }
            }
        }

        _hasMoved = new bool[_lights.Count];
        // Small maps are cheaper to scan. The BVH is immutable; any light that moves joins the always-checked
        // list, while its old index entry is harmless (duplicates are removed before scoring).
        if (_lights.Count >= 32)
        {
            var influences = new LightInfluence[_lights.Count];
            for (int i = 0; i < _lights.Count; i++)
            {
                LightEntry light = _lights[i];
                influences[i] = new LightInfluence(light.Location, WorldRadius(light),
                    light.Effect == RenderK.LE_Sunlight || light.Brightness <= 0f || !float.IsFinite(light.Brightness));
            }
            _index = new LightInfluenceIndex(influences);
        }
    }

    private readonly HashSet<string> _off = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<LightEntry> _spawned = [];

    public int LightCount => _lights.Count;

    /// <summary>Map lights considered by the last selection, before switches, zones and exact scoring.</summary>
    public int LastMapCandidates { get; private set; }

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
        _smokeLighting?.SetSpawned(_spawned);
    }

    private float _time;

    /// <summary>Scene time (Level.TimeSeconds) the light types are evaluated at; the map sets it each frame.</summary>
    public float Time
    {
        get => _time;
        set { _time = value; if (_smokeLighting is { } smoke) smoke.Time = value; }
    }

    /// <summary>Switches a light on or off for actors (a triggered STriggerLight); takes effect at the next <see cref="Select"/>.</summary>
    public void SetOn(string name, bool on)
    {
        _switches[name] = on;
        _smokeLighting?.SetOn(name, on);
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
        _places[name] = (location, rotation);
        _smokeLighting?.SetPlace(name, location, rotation);
        if (!_byName.TryGetValue(name, out List<int>? indices)) return;
        foreach (int i in indices)
        {
            LightEntry light = _lights[i];
            if (light.Location != location || light.Rotation != rotation)
            {
                _lights[i] = light with { Location = location, Rotation = rotation };
                if (!_hasMoved[i])
                {
                    _hasMoved[i] = true;
                    _moved.Add(i);
                }
            }
        }
    }

    /// <summary>Smoke also scatters baked room lights, even when those lights deliberately exclude characters.</summary>
    internal ActorLightSet SelectForSmoke(FVector center, float radius, Vector3 ambient, int zone = -1)
    {
        if (_smokeLighting is null)
        {
            var smoke = new ActorLighting(_source, true) { Time = Time };
            foreach (var (name, on) in _switches) smoke.SetOn(name, on);
            foreach (var (name, place) in _places) smoke.SetPlace(name, place.Location, place.Rotation);
            smoke.SetSpawned(_spawned);
            _smokeLighting = smoke;
        }
        return _smokeLighting.Select(center, radius, ambient, zone);
    }

    /// <summary>
    /// The light set of an actor whose bounding sphere is (<paramref name="center"/>, <paramref name="radius"/>) in
    /// uu. <paramref name="ambient"/> is A in display units (zone ambient plus AmbientGlow / 255); <paramref name="zone"/>
    /// the actor's zone index (-1 unknown).
    /// </summary>
    public ActorLightSet Select(FVector center, float radius, Vector3 ambient, int zone = -1)
    {
        _ranked.Clear();
        LastMapCandidates = 0;
        if (_index is not null && _moved.Count < _lights.Count / 2)
        {
            _index.Query(center, radius, _candidates);
            _candidates.AddRange(_moved);
            // Preserve exactly the full scan's insertion order before the original List.Sort, including its
            // existing tie behavior. No new tie breaker or approximate top-four selection is introduced.
            _candidates.Sort();
            int previous = -1;
            foreach (int i in _candidates)
            {
                if (i == previous) continue;
                previous = i;
                LastMapCandidates++;
                LightEntry l = _lights[i];
                if (_off.Count == 0 || !_off.Contains(l.Name)) Rank(l, center, radius, zone);
            }
        }
        else
        {
            LastMapCandidates = _lights.Count;
            foreach (LightEntry l in _lights)
                if (_off.Count == 0 || !_off.Contains(l.Name)) Rank(l, center, radius, zone);
        }

        foreach (LightEntry l in _spawned)
        {
            Rank(l, center, radius, zone);
        }

        // Suns always first, then by score, retaining the original sort and input order for equal scores.
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
            set.Lights[i] = PackActor(l, WorldLights.Modulation(l, Time), center, radius);
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

    /// <summary>LightBrightness x (1 - |L - C|^2 / (R + r)^2); spots outside their cone score 0 (<see cref="ActorLightRules.Score"/>).</summary>
    public static float Score(LightEntry l, FVector center, float radius)
    {
        ArgumentNullException.ThrowIfNull(l);
        return ActorLightRules.Score(l.Effect, l.Location, l.Rotation, l.Brightness, l.Radius, l.Cone, center, radius);
    }

    /// <summary>R = 25 x (LightRadius + 1).</summary>
    public static float WorldRadius(LightEntry l) => ActorLightRules.WorldRadius(l.Radius);

    public static bool IsSpot(LightEntry l) => ActorLightRules.IsSpot(l.Effect);

    /// <summary>Full cone angle in radians: 1.15 acos((1 - LightCone / 256)^2).</summary>
    public static float SpotPhi(LightEntry l) => ActorLightRules.SpotPhi(l.Cone);

    /// <summary>FGetHSV(hue, saturation, 255) x brightness / 255 (<see cref="ActorLightRules.Colour"/>).</summary>
    public static Vector3 Colour(LightEntry l)
    {
        ArgumentNullException.ThrowIfNull(l);
        LightRgb c = ActorLightRules.Colour(l.Hue, l.Saturation, l.Brightness);
        return new Vector3(c.R, c.G, c.B);
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

    /// <summary>The original D3D actor light fit, in metres. World light packing retains its own layout.</summary>
    public static PackedLight PackActor(LightEntry light, float brightness, FVector center, float radius)
    {
        PackedLight packed = PackWorld(light, brightness);
        if (light.Effect == RenderK.LE_Sunlight) return packed;
        Vector3 colour = Colour(light) * brightness;
        if (light.Effect == RenderK.LE_QuadraticNonIncidence)
            return packed with { Col = new Vector4(colour.X, colour.Y, colour.Z, 0f) };
        var fit = ActorLightRules.FitAttenuation(FVector.Dist(light.Location, center), radius, light.Radius);
        return new PackedLight(new Vector4(packed.Pos.X, packed.Pos.Y, packed.Pos.Z, fit.Linear * UeSpace.UnitsPerMetre),
            new Vector4(colour.X, colour.Y, colour.Z, fit.Constant), packed.Spot);
    }

    private static readonly StringName SphereMeta = "scct_light_sphere";

    public static void ReadSphere(Mesh mesh, JsonElement source)
    {
        if (CookedSphere(source) is { } sphere) mesh.SetMeta(SphereMeta, sphere);
    }

    /// <summary>A static mesh export's cooked bounding sphere (<c>scctSphere</c>: centre and radius in the mesh's space, metres), or null without one.</summary>
    public static Vector4? CookedSphere(JsonElement source) =>
        source.TryGetProperty("extras", out JsonElement extras) &&
        extras.TryGetProperty("scctSphere", out JsonElement sphere) && sphere.GetArrayLength() == 4
            ? new Vector4(sphere[0].GetSingle(), sphere[1].GetSingle(), sphere[2].GetSingle(), sphere[3].GetSingle())
            : null;

    /// <summary>The sphere older content falls back to: the one around a local box.</summary>
    public static Vector4 BoxSphere(Aabb box)
    {
        Vector3 centre = box.GetCenter();
        return new Vector4(centre.X, centre.Y, centre.Z, box.Size.Length() * .5005f);
    }

    /// <summary>The cooked local sphere scaled by the largest axis, rather than a rotation-expanded world AABB.</summary>
    public static (FVector Center, float Radius) SphereOf(Mesh mesh, Transform3D transform)
    {
        Vector4 sphere = mesh.HasMeta(SphereMeta) ? mesh.GetMeta(SphereMeta).AsVector4() : BoxSphere(mesh.GetAabb());
        float scale = MathF.Max(transform.Basis.X.Length(), MathF.Max(transform.Basis.Y.Length(), transform.Basis.Z.Length()));
        return (UeSpace.ToUe(transform * new Vector3(sphere.X, sphere.Y, sphere.Z)),
            sphere.W * scale * UeSpace.UnitsPerMetre);
    }

    private static readonly StringName EchelonMeta = "svm_echelon_ignore";

    /// <summary>
    /// The spot shadow lights an actor skips, as the template's Echelon code (<see cref="RenderMap.EchelonCode"/>).
    /// An LE_ESpotShadow light lights an actor unless the light is static and the actor has bIgnoreEchelonLighting,
    /// the light is dynamic and the actor has bIgnoreDynamicEchelonLighting, the light is in the actor's
    /// IgnoredEchelonLights or the actor's GlassEffect is set.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "game 0x10a37310, called by the actor light gathering 0x10a37610 for LightEffect 22 only")]
    public static int EchelonCode(RenderMap map, GameplayActor actor)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(actor);
        int flags = actor.Int("GlassEffect") != 0
            ? 3
            : (actor.Bool("bIgnoreEchelonLighting") ? 1 : 0) | (actor.Bool("bIgnoreDynamicEchelonLighting") ? 2 : 0);
        List<string>? lights = null;
        if (actor.TryGet("IgnoredEchelonLights", out System.Text.Json.JsonElement list) && list.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            foreach (System.Text.Json.JsonElement item in list.EnumerateArray())
            {
                // {"$actor": "STriggerLight6899"} or {"$asset": "AquaE.Light7159"}: the light's name is the last part.
                string? path = item.ValueKind != System.Text.Json.JsonValueKind.Object ? null
                    : item.TryGetProperty("$actor", out System.Text.Json.JsonElement a) ? a.GetString()
                    : item.TryGetProperty("$asset", out System.Text.Json.JsonElement b) ? b.GetString() : null;
                if (!string.IsNullOrEmpty(path))
                {
                    (lights ??= []).Add(path[(path.LastIndexOf('.') + 1)..]);
                }
            }
        }

        return map.EchelonCode(flags, lights);
    }

    /// <summary>Sets the Echelon code of an actor-lit instance (<see cref="EchelonCode"/>); kept by later <see cref="Apply"/> calls.</summary>
    public static void SetEchelonIgnore(GeometryInstance3D node, int code)
    {
        ArgumentNullException.ThrowIfNull(node);
        node.SetMeta(EchelonMeta, (float)code);
        Vector4 ambient = OpenScctGame.Render.InstanceParams.Get(node, AmbientParam).AsVector4();
        var updated = new Vector4(ambient.X, ambient.Y, ambient.Z, code);
        OpenScctGame.Render.InstanceParams.Set(node, AmbientParam, updated);
        UniformWrites++;
        if (Applied.TryGetValue(node, out AppliedLights? applied)) applied.Ambient = updated;
    }

    /// <summary>
    /// The ambient and lights last written onto an instance by <see cref="Apply"/>, as its instance parameters hold
    /// them, without asking the engine for them: false for an instance <see cref="Apply"/> has not written.
    /// </summary>
    internal static bool TryGetApplied(GeometryInstance3D node, out Vector4 ambient, out PackedLight[] lights)
    {
        if (Applied.TryGetValue(node, out AppliedLights? applied) && applied.Known)
        {
            (ambient, lights) = (applied.Ambient, applied.Lights);
            return true;
        }

        (ambient, lights) = (default, []);
        return false;
    }

    /// <summary>Writes a light set onto a drawn instance (its material must use the actor-lit template).</summary>
    public static void Apply(GeometryInstance3D node, ActorLightSet set)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(set);
        AppliedLights applied = Applied.GetValue(node, static _ => new AppliedLights());
        float echelon = node.HasMeta(EchelonMeta) ? (float)node.GetMeta(EchelonMeta) : 0f;
        var ambient = new Vector4(set.Ambient.X, set.Ambient.Y, set.Ambient.Z, echelon);
        if (!applied.Known || applied.Ambient != ambient)
        {
            OpenScctGame.Render.InstanceParams.Set(node, AmbientParam, ambient);
            UniformWrites++;
            applied.Ambient = ambient;
        }
        for (int i = 0; i < RenderK.MaxActorLights; i++)
        {
            PackedLight p = i < set.Count ? set.Lights[i] : new(default, default, new Vector4(0f, 0f, 0f, -2f));
            PackedLight previous = applied.Lights[i];
            if (!applied.Known || previous.Pos != p.Pos)
            {
                OpenScctGame.Render.InstanceParams.Set(node, PosParams[i], p.Pos);
                UniformWrites++;
            }
            if (!applied.Known || previous.Col != p.Col)
            {
                OpenScctGame.Render.InstanceParams.Set(node, ColParams[i], p.Col);
                UniformWrites++;
            }
            if (!applied.Known || previous.Spot != p.Spot)
            {
                OpenScctGame.Render.InstanceParams.Set(node, SpotParams[i], p.Spot);
                UniformWrites++;
            }
            applied.Lights[i] = p;
        }
        applied.Known = true;
    }
}

/// <summary>A light packed for the template. World lights carry radius/gain/inner cone; actor lights carry
/// fitted linear attenuation/colour/constant attenuation. Both share position/direction and outer cone.</summary>
public readonly record struct PackedLight(Vector4 Pos, Vector4 Col, Vector4 Spot);

/// <summary>The ambient and up to four lights of one actor.</summary>
public sealed class ActorLightSet
{
    public Vector3 Ambient { get; init; }

    public PackedLight[] Lights { get; } = new PackedLight[RenderK.MaxActorLights];

    public string?[] Names { get; } = new string?[RenderK.MaxActorLights];

    public int Count { get; set; }
}
