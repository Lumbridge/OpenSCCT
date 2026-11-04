using System;
using System.Collections.Generic;
using Godot;
using OpenScct.Core;
using OpenScct.Game.Lighting;
using OpenScct.Physics.Collision;
using OpenScct.Provenance;
using OpenScctGame.Render.Data;

namespace OpenScctGame.Render;

/// <summary>
/// The original's lighting of actors (pawns, weapons, runtime props and static meshes without baked colours), by the
/// shared <see cref="ActorLightRules"/> (which the bots' light field also reads):
/// <c>display = texture x min(2, A + sum k C S(d / R) max(0, N.L))</c> over the four strongest lights, with
/// S(x) = 1 - 3x^2 + 2x^3, k = 2 for point lights and 1.75 for suns, C the light's FGetHSV colour x brightness / 255
/// and A the zone ambient plus AmbientGlow. The light set is chosen on the CPU and handed to the template as
/// instance uniforms (svm_ambient, svm_l0..3_pos/col/spot). docs/RENDERING.md#actor-lighting.
/// <para>The candidates follow the game's gather (0x10a37610): with the map's leaf light lists
/// (<see cref="LeafLights"/>) a static light must be listed by a leaf the actor touches, wherever it has moved to,
/// other lights must reach it; a light whose bSpecialLit differs from the actor's or that the actor's
/// IgnoredEchelonLights names is skipped; own-zone lights and suns stay in their zone; at most 256 candidates are
/// ranked.</para>
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
    private readonly Dictionary<string, List<int>> _byName = new(StringComparer.OrdinalIgnoreCase);
    private readonly LightInfluenceIndex? _index;
    private readonly List<int> _candidates = [];
    private readonly List<int> _moved = [];
    private readonly bool[] _hasMoved;

    public ActorLighting(LightingDoc lighting, LeafLights? leaves = null)
    {
        ArgumentNullException.ThrowIfNull(lighting);
        foreach (LightEntry l in lighting.Lights)
        {
            // Lights that reach actors: the ones the game keeps for run time, bApplyToInGameLighting or bDynamicLight
            // (as WorldLights), with a light type, and not the runtime ESpotShadow projectors, which have their own
            // path. A light not initially on (STriggerLight.bInitialyOn) starts off. Dynamic lights without the in-game
            // flag were left out until AquaE's kiosk (SGamePlayObject2126, beside STriggerLight303) drew black.
            if (ActorLightRules.LightsActors(l.ApplyInGame, l.Dynamic, l.LightType, l.Effect))
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
        _leafId = new int[_lights.Count];
        Array.Fill(_leafId, -1);
        if (leaves is not null)
        {
            _leaves = leaves;
            _leafSet = leaves.NewSet();
            for (int i = 0; i < _lights.Count; i++)
            {
                // Static lights with a light type are in the leaf lists (pass 0 of the gather); the others are reached
                // by distance (the runtime list, pass 1, which the game fills per frame with non-static lights only:
                // 0x10a3d3b0 skips bStatic at 0x10a3d66b). A static light no leaf lists lights no actor.
                LightEntry light = _lights[i];
                if (light.Static && light.LightType != 0)
                {
                    int id = leaves.IdOf(light.Name);
                    _leafId[i] = id >= 0 ? id : NotListed;
                }
            }
        }

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

    // Per map light: its id in the leaf lists when it is a static light the lists decide (NotListed when no leaf lists
    // it), else -1 (chosen by distance).
    private const int NotListed = int.MaxValue;
    private readonly int[] _leafId;
    private readonly LeafLights? _leaves;
    private readonly LeafLights.LeafLightSet? _leafSet;
    private (bool? SpecialLit, IReadOnlySet<string>? Ignored) _filter;

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

    /// <summary>
    /// The light set of an actor whose bounding sphere is (<paramref name="center"/>, <paramref name="radius"/>) in
    /// uu. <paramref name="ambient"/> is A in display units (zone ambient plus AmbientGlow / 255); <paramref name="zone"/>
    /// the actor's zone index (-1 unknown), tested at the bounds' centre (the original takes every leaf the actor
    /// touches); <paramref name="specialLit"/> the actor's bSpecialLit (null when the map's content does not say, as
    /// conversions made before it was exported: no light is then skipped for it) and <paramref name="ignored"/> the
    /// light names of its IgnoredEchelonLights (<see cref="ActorLightRules.LightsActor"/>). <paramref name="maxLights"/>
    /// is its Actor.MaxLights: it keeps min(8, MaxLights) lights (none at 0), of which the template draws the first
    /// <see cref="RenderK.ActorLightSlots"/>.
    /// </summary>
    public ActorLightSet Select(FVector center, float radius, Vector3 ambient, int zone = -1, bool? specialLit = false,
        IReadOnlySet<string>? ignored = null, int maxLights = RenderK.MaxActorLights)
    {
        _ranked.Clear();
        LastMapCandidates = 0;
        _filter = (specialLit, ignored);
        _leafSet?.Gather(center, radius);
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
                if ((_off.Count == 0 || !_off.Contains(l.Name)) && LeafAdmits(i)) Rank(l, center, radius, zone);
            }
        }
        else
        {
            LastMapCandidates = _lights.Count;
            for (int i = 0; i < _lights.Count; i++)
            {
                LightEntry l = _lights[i];
                if ((_off.Count == 0 || !_off.Contains(l.Name)) && LeafAdmits(i)) Rank(l, center, radius, zone);
            }
        }

        foreach (LightEntry l in _spawned)
        {
            Rank(l, center, radius, zone);
        }

        _filter = default;

        // Suns always first, then by score, retaining the original sort and input order for equal scores.
        _ranked.Sort((a, b) =>
        {
            bool sa = a.Light.Effect == RenderK.LE_Sunlight;
            bool sb = b.Light.Effect == RenderK.LE_Sunlight;
            return sa != sb ? (sa ? -1 : 1) : b.Score.CompareTo(a.Score);
        });

        var set = new ActorLightSet { Ambient = ambient };
        int n = Math.Min(Math.Min(RenderK.ActorLightCap(maxLights), RenderK.ActorLightSlots), _ranked.Count);
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

    // Pass 0 of the gather: a static light the leaf lists decide must be listed by a leaf the actor touches. A static
    // light that has moved (bone-attached flares) is still taken only from the lists Build made, then tested at its
    // current place, as the game's runtime list holds no bStatic light.
    private bool LeafAdmits(int i)
    {
        int id = _leafId[i];
        return id < 0 || (id != NotListed && _leafSet!.Contains(id));
    }

    private void Rank(LightEntry l, FVector center, float radius, int zone)
    {
        // The gather's candidate array holds 256 lights, filled in the order it meets them.
        if (_ranked.Count >= ActorLightRules.MaxCandidates)
        {
            return;
        }

        // bSpecialLit must match (unless the content does not say the actor's) and IgnoredEchelonLights must not name
        // the light (game 0x10a37d86); bAffectOwnZoneOnly lights and suns reach only actors in their zone.
        if (!ActorLightRules.LightsActor(l.SpecialLit, l.Name, _filter.SpecialLit ?? l.SpecialLit, _filter.Ignored) ||
            !ActorLightRules.ZoneAdmits(l.Effect, l.OwnZoneOnly, l.ZoneIndex, zone))
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

    private static readonly StringName EchelonMeta = "svm_echelon_ignore";

    /// <summary>
    /// The spot shadow lights an actor skips, as the template's Echelon code (<see cref="RenderMap.EchelonCode"/>).
    /// An LE_ESpotShadow light lights an actor unless the light is static and the actor has bIgnoreEchelonLighting,
    /// the light is dynamic and the actor has bIgnoreDynamicEchelonLighting, the light is in the actor's
    /// IgnoredEchelonLights or the actor's GlassEffect is set. Only the two bIgnore*EchelonLighting flags and
    /// GlassEffect are particular to spot shadow lights: IgnoredEchelonLights skips every kind of light
    /// (<see cref="ActorLightRules.LightsActor"/>, which <see cref="Select"/> applies to the ordinary ones).
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "game 0x10a37310 for LightEffect 22; the ordinary branch 0x10a37d86 also tests IgnoredEchelonLights")]
    public static int EchelonCode(RenderMap map, GameplayActor actor)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(actor);
        int flags = actor.Int("GlassEffect") != 0
            ? 3
            : (actor.Bool("bIgnoreEchelonLighting") ? 1 : 0) | (actor.Bool("bIgnoreDynamicEchelonLighting") ? 2 : 0);
        return map.EchelonCode(flags, IgnoredLightNames(actor));
    }

    /// <summary>The light actor names of a placed actor's IgnoredEchelonLights (sim/gameplay.json), or null when it has none.</summary>
    public static IReadOnlySet<string>? IgnoredLightNames(GameplayActor actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        HashSet<string>? lights = null;
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
                    (lights ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase)).Add(path[(path.LastIndexOf('.') + 1)..]);
                }
            }
        }

        return lights;
    }

    /// <summary>Sets the Echelon code of an actor-lit instance (<see cref="EchelonCode"/>); kept by later <see cref="Apply"/> calls.</summary>
    public static void SetEchelonIgnore(GeometryInstance3D node, int code)
    {
        ArgumentNullException.ThrowIfNull(node);
        node.SetMeta(EchelonMeta, (float)code);
        Vector4 ambient = node.GetInstanceShaderParameter(AmbientParam).AsVector4();
        node.SetInstanceShaderParameter(AmbientParam, new Vector4(ambient.X, ambient.Y, ambient.Z, code));
    }

    /// <summary>Writes a light set onto a drawn instance (its material must use the actor-lit template).</summary>
    public static void Apply(GeometryInstance3D node, ActorLightSet set)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(set);
        float echelon = node.HasMeta(EchelonMeta) ? (float)node.GetMeta(EchelonMeta) : 0f;
        node.SetInstanceShaderParameter(AmbientParam, new Vector4(set.Ambient.X, set.Ambient.Y, set.Ambient.Z, echelon));
        for (int i = 0; i < RenderK.ActorLightSlots; i++)
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

    public PackedLight[] Lights { get; } = new PackedLight[RenderK.ActorLightSlots];

    public string?[] Names { get; } = new string?[RenderK.ActorLightSlots];

    public int Count { get; set; }
}
