using System;
using System.Collections.Generic;
using Godot;
using OpenScct.Core;
using OpenScct.Provenance;
using OpenScctGame.Render.Data;

namespace OpenScctGame.Render;

/// <summary>
/// The map's own LE_ESpotShadow lights (window and sun projectors, searchlight patches: render/lighting.json effect 22),
/// drawn as spot shadow lights. The original keeps them out of the baked light and projects them at run time (the
/// lightmap under FactD's window patches is uniform while the game draws the patch with its bar shadows; bridge
/// docs/handoff/fix_plan.md I8), so every one that is on is drawn, whatever its bApplyToStaticLighting. The template
/// has <see cref="RenderK.SpotShadowSlots"/> slots. Visible light volumes have priority, with stable selection and
/// fading when the map budget is exceeded. Each follows its switch (STriggerLight) and light type.
/// </summary>
[PortOf("eng:Render.SpotShadow", Spec = "docs/RENDERING.md#spot-shadow-lights")]
public sealed class MapSpotLights : IDisposable
{
    private readonly RenderMap _map;
    private readonly Node _parent;
    private readonly List<LightEntry> _lights = [];
    private readonly List<Aabb> _bounds = [];
    private readonly Dictionary<int, SpotShadowLight> _drawn = [];
    private readonly List<MapSpotLightCandidate> _candidates = [];
    private readonly MapSpotLightBudget _budget = new(RenderK.SpotShadowMapLights);
    private readonly List<int> _drop = [];
    private readonly Plane[] _frustum = new Plane[6];

    public MapSpotLights(RenderMap map, Node parent)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(parent);
        _map = map;
        _parent = parent;
        foreach (LightEntry l in map.Lighting.Lights)
        {
            if (l.Echelon is not null && l.Brightness > 0f)
            {
                _lights.Add(l);
                _bounds.Add(SpotShadowShape.Of(l, l.Echelon).InfluenceBounds(l.Location, l.Rotation));
            }
        }
    }

    /// <summary>The map's LE_ESpotShadow lights.</summary>
    public int Count => _lights.Count;

    /// <summary>Lights drawn now.</summary>
    public int DrawnCount => _drawn.Count;

    /// <summary>Picks the lights to draw for a camera at <paramref name="camera"/> (uu) and places them for this frame.</summary>
    [Provenance(ProvenanceKind.Estimated, "visible-first shadow budget with hysteresis and fading; the original's limit is not decoded")]
    public void Update(FVector camera)
    {
        Camera3D? cameraNode = _parent.GetViewport().GetCamera3D();
        if (cameraNode is not null) ViewFrustum.Read(cameraNode, _frustum);
        _candidates.Clear();
        for (int i = 0; i < _lights.Count; i++)
        {
            LightEntry l = _lights[i];
            if (_map.IsLightOn(l.Name, l.InitiallyOn && l.LightType != 0))
            {
                bool visible = cameraNode is null || ViewFrustum.Intersects(_bounds[i], _frustum);
                _candidates.Add(new MapSpotLightCandidate(i, Distance(l, camera), visible));
            }
        }

        _budget.Update(_candidates, (float)_parent.GetProcessDeltaTime());

        _drop.Clear();
        foreach (int index in _drawn.Keys)
        {
            if (!_budget.Active.ContainsKey(index))
            {
                _drop.Add(index);
            }
        }

        foreach (int index in _drop)
        {
            _drawn[index].Dispose();
            _drawn.Remove(index);
        }

        foreach ((int index, float visibility) in _budget.Active)
        {
            LightEntry l = _lights[index];
            if (!_drawn.TryGetValue(index, out SpotShadowLight? light))
            {
                EchelonSpot e = l.Echelon!;
                Texture2D? cookie = e.Texture is null ? null : _map.Textures.Get(e.Texture, mipmaps: false);
                light = new SpotShadowLight(_parent, _map.Pack, SpotShadowShape.Of(l, e), cookie, cacheStatic: !l.Dynamic)
                {
                    Zone = l.OwnZoneOnly ? l.ZoneIndex : -1,
                    LightName = l.Name,
                };
                _drawn[index] = light;
            }

            light.Scale = WorldLights.Modulation(l, _map.Time);
            light.VisibilityGain = visibility;
            light.Place(l.Location, l.Rotation);
            if (cameraNode is not null)
            {
                light.CullShadow(_frustum);
            }
            if (!light.On)
            {
                light.On = true;
            }
        }
    }

    /// <summary>
    /// How far the camera is from the light's frustum: from the nearest point of its axis (light to EchelonRange),
    /// less the frustum's half size there.
    /// </summary>
    private static float Distance(LightEntry l, FVector camera)
    {
        EchelonSpot e = l.Echelon!;
        FVector dir = l.Rotation.Vector();
        float t = Math.Clamp((camera - l.Location) | dir, 0f, e.Range);
        float p = Math.Clamp(e.NearPlaneRatio, 0f, 1f);
        float half = MathF.Max(e.Width, e.Height) * (p + ((1f - p) * t / MathF.Max(1f, e.Range)));
        return MathF.Max(0f, FVector.Dist(camera, l.Location + (dir * t)) - half);
    }

    public void Dispose()
    {
        foreach (SpotShadowLight light in _drawn.Values)
        {
            light.Dispose();
        }

        _drawn.Clear();
    }
}
