using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using OpenScct.Core;
using OpenScct.Provenance;
using OpenScctGame.Render.Data;

namespace OpenScctGame.Render;

/// <summary>
/// The map's rain volumes (ERainVolume, render/fx.json rain[]) as AERainVolume::RenderRain draws them each frame
/// (game 0x10a04d70): vertical sheets across X and across Y, inside the volume's world box, placed around the camera.
/// Along each axis the camera coordinate is clamped to [min - D/2, max + D/2] (D = m_DistanceToCamera); sheets stand at
/// D/2, D/2 + T, D/2 + 2T ... (T = m_RainThickness) either side of it, each spanning the box's other axis and its
/// height, grey vertex colour round(255 x clamp((Falloff - d) / Falloff x FadeScale, 0, 1)) at distance d. A sheet is
/// drawn while it lies in [min, max) of its axis, its grey is not 0 and it is within m_maxDistFromCamera of the camera;
/// the walk stops at the first step that draws neither sheet. One texture repeat per m_RainMatScale uu, from a per-sheet
/// offset, then the material's panner. A negative FadeScale (Stat*) draws nothing. docs/RENDERING.md#rain-volumes.
/// </summary>
[PortOf("eng:Render.RainVolumes", Spec = "docs/RENDERING.md#rain-volumes")]
public sealed class RainVolumes
{
    public const string TemplatePath = "res://Shaders/svm_rain.gdshaderinc";

    // Safety stop for the walk (the fade ends it long before: falloff 100 uu against steps of 80 uu or more).
    private const int MaxSteps = 64;

    // The game offsets each sheet's texture coordinates from a 16-float table it fills at run time (the file holds
    // zeros); a fixed spread stands in for it [estimate].
    private static readonly Vector2[] SheetOffsets =
    [
        new(0.0f, 0.0f), new(0.37f, 0.61f), new(0.74f, 0.23f), new(0.11f, 0.85f),
        new(0.48f, 0.47f), new(0.85f, 0.09f), new(0.22f, 0.71f), new(0.59f, 0.33f),
        new(0.96f, 0.95f),
    ];

    private readonly List<Volume> _volumes = [];
    private readonly ImmediateMesh _mesh = new();
    private FVector? _drawnCamera;

    private sealed record Volume(RainEntry Entry, FVector Min, FVector Max, ShaderMaterial Material);

    public RainVolumes(RenderMap map, Node3D parent)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(parent);
        var shaders = new Dictionary<bool, Shader>();
        foreach (RainEntry r in FxDoc.LoadRain(Path.Combine(map.Folder, "render", "fx.json")))
        {
            if (r.Texture is null || map.Textures.Get(r.Texture) is not { } texture)
            {
                continue;
            }

            // RebuildRainVolume's box: (-X, -Y, 0)..(X, Y, Z) of m_RainSize x DrawScale x DrawScale3D from the volume's
            // Location (the floor) [the floor origin is read from OrphD's BSP; the volume's small rotations are left out].
            var half = new FVector(r.Size.X * r.Scale.X, r.Size.Y * r.Scale.Y, r.Size.Z * r.Scale.Z);
            var min = new FVector(r.Location.X - half.X, r.Location.Y - half.Y, r.Location.Z);
            var max = new FVector(r.Location.X + half.X, r.Location.Y + half.Y, r.Location.Z + half.Z);
            if (!shaders.TryGetValue(r.Additive, out Shader? shader))
            {
                string modes = r.Additive ? "blend_add" : "blend_mix";
                string define = r.Additive ? "" : "#define RAIN_ALPHA\n";
                shader = new Shader
                {
                    Code = "shader_type spatial;\nrender_mode unshaded, fog_disabled, cull_disabled, depth_draw_never, " + modes
                        + ";\n" + define + "#include \"" + TemplatePath + "\"\n",
                    ResourceName = "svm_rain " + modes,
                };
                shaders[r.Additive] = shader;
            }

            var material = new ShaderMaterial { Shader = shader, ResourceName = "Rain_" + r.Name };
            material.SetShaderParameter("rain_tex", texture);
            UvAnimRaw p = r.Panner ?? new UvAnimRaw(0f, 0f, 0f, 1f, 1f);
            // TexFriendlyPanner as u' = scaleU u + PanU x PanRate x t [estimate: GetMatrix is not decoded].
            material.SetShaderParameter("rain_pan", new Vector4(p.ScaleU, p.ScaleV, p.PanU * p.Rate, p.PanV * p.Rate));
            _volumes.Add(new Volume(r, min, max, material));
        }

        Node = new MeshInstance3D
        {
            Name = "RainVolumes",
            Mesh = _mesh,
            Layers = RenderLayers.Effects,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            // The sheets follow the camera: never cull the mesh by its stale bounds.
            ExtraCullMargin = 16384f,
        };
        parent.AddChild(Node);
    }

    public MeshInstance3D Node { get; }

    public int Count => _volumes.Count;

    /// <summary>Sheets drawn by the last <see cref="Update"/>.</summary>
    public int Sheets { get; private set; }

    /// <summary>Rebuilds the sheets for a camera at <paramref name="camera"/> (uu).</summary>
    [Provenance(ProvenanceKind.Decoded, "AERainVolume::RenderRain, game 0x10a04d70 (ERainVolume fields at 0x304..0x328 in reverse declaration order)")]
    public void Update(FVector camera)
    {
        // Only placement and vertex alpha depend on the eye position. The material's panner reads scene time in
        // the shader, so an idle camera needs no new surfaces for the same sheets.
        if (_drawnCamera == camera) return;
        _drawnCamera = camera;
        _mesh.ClearSurfaces();
        Sheets = 0;
        foreach (Volume v in _volumes)
        {
            bool begun = false;
            for (int axis = 0; axis < 2; axis++)
            {
                Walk(v, camera, axis, ref begun);
            }

            if (begun)
            {
                _mesh.SurfaceEnd();
            }
        }
    }

    private void Walk(Volume v, FVector camera, int axis, ref bool begun)
    {
        RainEntry r = v.Entry;
        if (r.Falloff == 0f)
        {
            return;
        }

        float lo = axis == 0 ? v.Min.X : v.Min.Y;
        float hi = axis == 0 ? v.Max.X : v.Max.Y;
        float cam = axis == 0 ? camera.X : camera.Y;
        float h = r.DistanceToCamera * 0.5f;
        float step = r.Thickness == 0f ? 80f : r.Thickness;
        float start = Math.Clamp(cam, lo - h, hi + h);
        int sheet = 0;
        for (int k = 0; k < MaxSteps; k++)
        {
            float d = h + (k * step);
            float alpha = Math.Clamp((r.Falloff - d) / r.Falloff * r.FadeScale, 0f, 1f);
            float grey = MathF.Round(alpha * 255f) / 255f;
            bool any = false;
            foreach (float at in (ReadOnlySpan<float>)[start + d, start - d])
            {
                if (at < lo || at >= hi || grey <= 0f || MathF.Abs(at - cam) > r.MaxDistance)
                {
                    continue;
                }

                any = true;
                if (!begun)
                {
                    _mesh.SurfaceBegin(Mesh.PrimitiveType.Triangles, v.Material);
                    begun = true;
                }

                AddSheet(v, axis, at, grey, SheetOffsets[(axis * 4 + sheet++) % SheetOffsets.Length]);
            }

            if (!any)
            {
                break;
            }
        }
    }

    private void AddSheet(Volume v, int axis, float at, float grey, Vector2 offset)
    {
        RainEntry r = v.Entry;
        // The sheet spans the box's other horizontal axis (U) and its height (V, from the top down).
        float spanLo = axis == 0 ? v.Min.Y : v.Min.X;
        float spanHi = axis == 0 ? v.Max.Y : v.Max.X;
        float u1 = offset.X + ((spanHi - spanLo) / r.MatScale);
        float v1 = offset.Y + ((v.Max.Z - v.Min.Z) / r.MatScale);
        FVector Corner(float span, float z) => axis == 0 ? new FVector(at, span, z) : new FVector(span, at, z);
        Vector3 a = UeSpace.ToGodot(Corner(spanLo, v.Max.Z));
        Vector3 b = UeSpace.ToGodot(Corner(spanHi, v.Max.Z));
        Vector3 c = UeSpace.ToGodot(Corner(spanLo, v.Min.Z));
        Vector3 d = UeSpace.ToGodot(Corner(spanHi, v.Min.Z));
        var colour = new Color(grey, grey, grey, 1f);
        Vertex(a, offset.X, offset.Y, colour);
        Vertex(b, u1, offset.Y, colour);
        Vertex(c, offset.X, v1, colour);
        Vertex(c, offset.X, v1, colour);
        Vertex(b, u1, offset.Y, colour);
        Vertex(d, u1, v1, colour);
        Sheets++;
    }

    private void Vertex(Vector3 position, float u, float v, Color colour)
    {
        _mesh.SurfaceSetColor(colour);
        _mesh.SurfaceSetUV(new Vector2(u, v));
        _mesh.SurfaceAddVertex(position);
    }
}
