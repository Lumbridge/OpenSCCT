using System;
using System.Collections.Generic;
using Godot;
using OpenScct.Content.Loading;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Provenance;

namespace OpenScctGame.Render.LightBeams;

/// <summary>
/// The light beams of the map's lights (UsesLightBeam with a LightBeamMesh, docs/RENDERING.md#light-beams): the
/// beam slice mesh stretched along the light, each slice adding the light that its light's shadow map and projected
/// texture let through (additive). A beam shows while its light is on and keeps its
/// beam: lights the simulation holds (STriggerLight, light movers) are read live each tick, so a switched-off or
/// alarm-switched light drops its beam (STriggerLight.LightOff clears UsesLightBeam) and a moving light carries it;
/// the other lights follow <see cref="RenderMap.IsLightOn"/>.
/// </summary>
[PortOf("eng:Render.LightBeams", Spec = "docs/RENDERING.md#light-beams")]
public sealed class MapLightBeams
{
    public const string ShaderPath = "res://Shaders/svm_light_beam.gdshader";

    /// <summary>Light each slice adds per unit of LightBrightness / 255 x sqrt(LightBeamInitialAlpha), x the light colour and cookie.</summary>
    [Provenance(ProvenanceKind.Measured, "MissP far-180 golden: STriggerLight1096's beam (brightness 100, alpha 5) over the black sky reads 85 to 92 of 255 in the game; 0.046 gives 87 to 104. Linear in brightness and sqrt(alpha): bridge FactD sweeps")]
    public const float BeamGain = 0.046f;

    /// <summary>NearPlaneRatio outside (0, 1] (MissE's Light315 stores 500) is taken as this.</summary>
    [Provenance(ProvenanceKind.Estimated, "bridge SCCTLightBeams.cpp fallback; the native's handling of such values is not decoded")]
    public const float DefaultNearRatio = 0.1f;

    private readonly RenderMap _map;
    private readonly List<Beam> _beams = [];
    private bool _bound;

    private MapLightBeams(RenderMap map)
    {
        _map = map;
    }

    public int Count => _beams.Count;

    /// <summary>The beams of render/lightbeams.json under <paramref name="parent"/>, or null when the map has none.</summary>
    public static MapLightBeams? Build(RenderMap map, Node3D parent)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(parent);
        if (LightBeamsDoc.Load(map.Folder) is not { Beams.Count: > 0 } doc)
        {
            return null;
        }

        var beams = new MapLightBeams(map);
        var root = new Node3D { Name = "LightBeams" };
        parent.AddChild(root);
        var shader = GD.Load<Shader>(ShaderPath);
        var meshes = new Dictionary<string, ArrayMesh>(StringComparer.OrdinalIgnoreCase);
        var spots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Data.LightEntry l in map.Lighting.Lights)
        {
            if (l.Echelon is not null)
            {
                spots.Add(l.Name);
            }
        }

        foreach (LightBeamEntry e in doc.Beams)
        {
            if (!doc.Meshes.TryGetValue(e.Mesh, out (Vector3[] Positions, int[] Indices) raw))
            {
                continue;
            }

            if (!meshes.TryGetValue(e.Mesh, out ArrayMesh? mesh))
            {
                var arrays = new Godot.Collections.Array();
                arrays.Resize((int)Mesh.ArrayType.Max);
                arrays[(int)Mesh.ArrayType.Vertex] = raw.Positions;
                arrays[(int)Mesh.ArrayType.Index] = raw.Indices;
                mesh = new ArrayMesh { ResourceName = e.Mesh };
                mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
                meshes[e.Mesh] = mesh;
            }

            var node = new MeshInstance3D
            {
                Name = "LightBeam_" + e.Name,
                Mesh = mesh,
                MaterialOverride = new ShaderMaterial { Shader = shader },
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                Layers = RenderLayers.World,
            };
            root.AddChild(node);
            var beam = new Beam(e, node) { Spot = spots.Contains(e.Name) };
            beam.Apply(e.LightOn && e.InitiallyOn, e.Location, e.Rotation, e.EchelonRange, e.SpotWidth, e.SpotHeight,
                e.NearPlaneRatio, e.FalloffScale, e.Alpha, e.Brightness);
            beams._beams.Add(beam);
        }

        beams.UpdateSlots();
        return beams;
    }

    /// <summary>
    /// Points each LE_ESpotShadow light's beam at the spot shadow slot its light holds this frame (its frustum and shadow
    /// map cut the beam); a beam whose light holds no slot is not drawn. Call after the spot lights are placed
    /// (MapSpotLights.Update).
    /// </summary>
    [Provenance(ProvenanceKind.Estimated, "a light without a shadow map this frame draws no beam (the game gives every drawn Echelon light one; our slots go to the nearest)")]
    public void UpdateSlots()
    {
        foreach (Beam b in _beams)
        {
            b.SetSlot(b.Spot ? SpotShadowLight.SlotOf(b.Entry.Name) : -1);
        }
    }

    /// <summary>
    /// Follows the lights after a simulation tick: a light the client's map load holds (<paramref name="loaded"/>) by its
    /// live LightType, UsesLightBeam, beam values, location and rotation; any other by the map's light switches.
    /// </summary>
    public void OnTick(MapLoadResult? loaded)
    {
        if (!_bound && loaded is not null)
        {
            _bound = true;
            foreach (Beam b in _beams)
            {
                b.Actor = loaded.FindActor(b.Entry.Name);
            }
        }

        foreach (Beam b in _beams)
        {
            LightBeamEntry e = b.Entry;
            if (b.Actor is { } a)
            {
                bool on = !a.bDeleteMe && a.LightType != ELightType.LT_None && a.UsesLightBeam && !a.LightBeamMesh.IsNone;
                b.Apply(on, a.Location, a.Rotation, a.EchelonRange, a.SpotWidth, a.SpotHeight, a.NearPlaneRatio,
                    a.LightBeamFalloffScale, a.LightBeamInitialAlpha, a.LightBrightness);
            }
            else
            {
                b.Apply(_map.IsLightOn(e.Name, e.LightOn && e.InitiallyOn), e.Location, e.Rotation, e.EchelonRange,
                    e.SpotWidth, e.SpotHeight, e.NearPlaneRatio, e.FalloffScale, e.Alpha, e.Brightness);
            }
        }
    }

    private sealed class Beam(LightBeamEntry entry, MeshInstance3D node)
    {
        private (bool, FVector, FRotator, float, float, float, float, float, int, float)? _last;
        private bool _on;
        private int _slot = -2;

        /// <summary>The light is an LE_ESpotShadow light: its shadow map cuts the beam.</summary>
        public bool Spot { get; init; }

        /// <summary>Uses the light's spot shadow slot (-1: none) and shows the beam when it is on and, for a spot light, has one.</summary>
        public void SetSlot(int slot)
        {
            if (slot == _slot)
            {
                return;
            }

            _slot = slot;
            ((ShaderMaterial)node.MaterialOverride).SetShaderParameter("beam_slot", slot);
            node.Visible = _on && (!Spot || slot >= 0);
        }

        public LightBeamEntry Entry { get; } = entry;

        public Actor? Actor { get; set; }

        /// <summary>
        /// Shapes and shows the beam: length EchelonRange x LightBeamFalloffScale, the section at the light
        /// NearPlaneRatio x the spot, growing to the spot at EchelonRange; each slice adds
        /// <see cref="BeamGain"/> x LightBrightness / 255 x sqrt(LightBeamInitialAlpha) x the light colour (x the cookie and
        /// shadow term of a spot shadow light, in the shader).
        /// </summary>
        [Provenance(ProvenanceKind.Decoded, "beam setup 0x1097ba8a..0x1097bbb3 (bridge SCCTReaderBeams.cpp): unit mesh stretched to EchelonRange x FalloffScale, near section NearPlaneRatio x spot")]
        [Provenance(ProvenanceKind.Estimated, "the slice fade 1 - x along the beam (bridge LightBeamFade 1)")]
        [Provenance(ProvenanceKind.Decoded, "beam setup 0x1097b3d0..0x1097bc12: stage flags from the light's shadow buffer (light flags bit) and its EchelonLightMaterial textures (+0x50, +0x54), the light's projection to c8: shadow map and projected texture cut and tint the slices")]
        [Provenance(ProvenanceKind.Measured, "additive: MissP's lamp beam shows over the black sky (a multiply draws nothing there); tinted by the cookie: RiveE's Mall_tibu.lightECHL beams read teal (spy-90 SSIM 0.69 to 0.95)")]
        public void Apply(bool on, FVector location, FRotator rotation, float range, float width, float height, float nearRatio,
            float falloff, int alpha, float brightness)
        {
            float length = range * falloff;
            on &= alpha > 0 && length > 1f;
            var key = (on, location, rotation, range, width, height, nearRatio, falloff, alpha, brightness);
            if (_last == key)
            {
                return;
            }

            _last = key;
            _on = on;
            node.Visible = on && (!Spot || _slot >= 0);
            if (!on)
            {
                return;
            }

            rotation.GetAxes(out FVector x, out FVector y, out FVector z);
            float nr = nearRatio > 0f && nearRatio <= 1f ? nearRatio : DefaultNearRatio;
            float strength = BeamGain * brightness / 255f * MathF.Sqrt(alpha);
            Color c = Entry.Colour * strength;
            var material = (ShaderMaterial)node.MaterialOverride;
            material.SetShaderParameter("beam_origin", UeSpace.ToGodot(location));
            material.SetShaderParameter("beam_x", UeSpace.DirectionToGodot(x));
            material.SetShaderParameter("beam_y", UeSpace.DirectionToGodot(y));
            material.SetShaderParameter("beam_z", UeSpace.DirectionToGodot(z));
            material.SetShaderParameter("beam_shape", new Vector4(UeSpace.LengthToGodot(length), UeSpace.LengthToGodot(width),
                UeSpace.LengthToGodot(height), nr));
            material.SetShaderParameter("beam_boost", new Vector4(c.R, c.G, c.B, falloff));

            // The mesh is drawn in world space from the shader: its box is the beam's, around the light.
            float grow = MathF.Max(1f, nr + ((1f - nr) * falloff));
            float reach = UeSpace.LengthToGodot(MathF.Max(length, MathF.Max(width, height) * grow));
            Vector3 at = UeSpace.ToGodot(location);
            node.CustomAabb = new Aabb(at - new Vector3(reach, reach, reach), new Vector3(reach, reach, reach) * 2f);
        }
    }
}
