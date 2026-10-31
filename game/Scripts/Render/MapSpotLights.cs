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
/// has <see cref="RenderK.SpotShadowSlots"/> slots, so the <see cref="RenderK.SpotShadowMapLights"/> nearest the
/// camera take them. Each follows its switch (STriggerLight) and light type.
/// </summary>
[PortOf("eng:Render.SpotShadow", Spec = "docs/RENDERING.md#spot-shadow-lights")]
public sealed class MapSpotLights : IDisposable
{
    private readonly RenderMap _map;
    private readonly Node _parent;
    private readonly List<LightEntry> _lights = [];
    private readonly Dictionary<LightEntry, SpotShadowLight> _drawn = new(ReferenceEqualityComparer.Instance);
    private readonly List<(float Distance, LightEntry Light)> _ranked = [];
    private readonly HashSet<LightEntry> _keep = new(ReferenceEqualityComparer.Instance);
    private readonly List<LightEntry> _drop = [];
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
            }
        }
    }

    /// <summary>The map's LE_ESpotShadow lights.</summary>
    public int Count => _lights.Count;

    /// <summary>Lights drawn now.</summary>
    public int DrawnCount => _drawn.Count;

    /// <summary>Picks the lights to draw for a camera at <paramref name="camera"/> (uu) and places them for this frame.</summary>
    [Provenance(ProvenanceKind.Estimated, "nearest-first choice of the lights that get a slot; the original's limit is not decoded")]
    public void Update(FVector camera)
    {
        _ranked.Clear();
        foreach (LightEntry l in _lights)
        {
            if (_map.IsLightOn(l.Name, l.InitiallyOn && l.LightType != 0))
            {
                _ranked.Add((Distance(l, camera), l));
            }
        }

        _ranked.Sort((a, b) => a.Distance.CompareTo(b.Distance));
        _keep.Clear();
        for (int i = 0; i < _ranked.Count && i < RenderK.SpotShadowMapLights; i++)
        {
            _keep.Add(_ranked[i].Light);
        }

        _drop.Clear();
        foreach (LightEntry l in _drawn.Keys)
        {
            if (!_keep.Contains(l))
            {
                _drop.Add(l);
            }
        }

        foreach (LightEntry l in _drop)
        {
            _drawn[l].Dispose();
            _drawn.Remove(l);
        }

        if (_keep.Count == 0)
        {
            return;
        }

        Camera3D? cameraNode = _parent.GetViewport().GetCamera3D();
        if (cameraNode is not null) ViewFrustum.Read(cameraNode, _frustum);
        foreach (LightEntry l in _keep)
        {
            if (!_drawn.TryGetValue(l, out SpotShadowLight? light))
            {
                EchelonSpot e = l.Echelon!;
                Texture2D? cookie = e.Texture is null ? null : _map.Textures.Get(e.Texture, mipmaps: false);
                light = new SpotShadowLight(_parent, _map.Pack, SpotShadowShape.Of(l, e), cookie, cacheStatic: !l.Dynamic)
                {
                    Zone = l.OwnZoneOnly ? l.ZoneIndex : -1,
                    LightName = l.Name,
                };
                _drawn[l] = light;
            }

            light.Scale = WorldLights.Modulation(l, _map.Time);
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
