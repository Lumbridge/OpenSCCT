using System;
using System.Collections.Generic;
using Godot;
using OpenScct.Core;
using OpenScct.Provenance;

namespace OpenScctGame.Render.Projectors;

/// <summary>
/// The map's texture projectors (Projector, DynamicProjector; docs/RENDERING.md#projectors): each a box over its
/// frustum whose shader projects the texture onto whatever the depth buffer holds inside it (world BSP, static
/// meshes and actors alike), blended into the frame by its FrameBufferBlendingOp as the original's
/// SetProjectorMaterial does. It replaces the PROJ_ decal meshes the converter clips onto the BSP.
/// </summary>
[PortOf("eng:Render.Projectors", Spec = "docs/RENDERING.md#projectors")]
public sealed class MapProjectors
{
    public const string ShaderInclude = "res://Shaders/svm_projector.gdshaderinc";

    private static readonly Dictionary<string, Shader> Shaders = new(StringComparer.Ordinal);

    private MapProjectors(Node3D root, int count)
    {
        Root = root;
        Count = count;
    }

    public Node3D Root { get; }

    public int Count { get; }

    /// <summary>The projectors of <paramref name="projectors"/>, drawn under <paramref name="parent"/>.</summary>
    public static MapProjectors Build(RenderMap map, IReadOnlyList<ProjectorEntry> projectors, Node3D parent)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(projectors);
        ArgumentNullException.ThrowIfNull(parent);
        var root = new Node3D { Name = "Projectors" };
        parent.AddChild(root);
        int count = 0;
        foreach (ProjectorEntry p in projectors)
        {
            string uri = p.Flipbook?.Texture ?? p.Texture;
            if (map.Textures.Get(uri) is not { } texture || p.MaxTrace <= 0f)
            {
                continue;
            }

            root.AddChild(CreateNode(p, texture));
            count++;
        }

        return new MapProjectors(root, count);
    }

    /// <summary>
    /// The shape of a projector in UE units: half the texture's width and height at the projector (texture size x
    /// DrawScale x DrawScale3D), and for FOV &gt; 0 the distance behind it where the frustum's sides meet.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "ortho mapping u = dot(P-L, Y) / (USize x DrawScale x DrawScale3D.Y) + 0.5, checked against the live Projector.Matrix (bridge LIGHTMAPS.md)")]
    [Provenance(ProvenanceKind.Estimated, "perspective: the frustum holds the texture's size at the projector and widens by FOV from an apex behind it, so FOV 1 (ShadD's signs, the spy's laser dot) stays the texture's size")]
    public static (float HalfWidth, float HalfHeight, float Apex) Shape(ProjectorEntry p)
    {
        float halfW = MathF.Abs(p.Size.X * p.Scale.Y) * 0.5f;
        float halfH = MathF.Abs(p.Size.Y * p.Scale.Z) * 0.5f;
        float fov = Math.Clamp(p.Fov, 0f, 170f);
        float apex = fov > 0f ? halfW / Mathf.Tan(Mathf.DegToRad(fov) * 0.5f) : 0f;
        return (halfW, halfH, apex);
    }

    private static MeshInstance3D CreateNode(ProjectorEntry p, Texture2D texture)
    {
        p.Rotation.GetAxes(out FVector x, out FVector y, out FVector z);
        (float halfW, float halfH, float apex) = Shape(p);
        float farScale = apex > 0f ? (p.MaxTrace + apex) / apex : 1f;
        var material = new ShaderMaterial { Shader = ShaderFor(p.FrameBufferBlending, p.ProjectStaticMesh) };
        material.SetShaderParameter("proj_tex", texture);
        material.SetShaderParameter("proj_origin", new Vector4(0f, 0f, 0f, UeSpace.LengthToGodot(p.MaxTrace)) + Pos4(p.Location));
        material.SetShaderParameter("proj_x", UeSpace.DirectionToGodot(x));
        // The texture axes carry DrawScale3D's sign (u = dot(P-L, Y) / (USize x DrawScale3D.Y), signed): the Mission
        // bunker signs (DrawScale3D.Z -1, pitch and roll 180) read upside down and mirrored with the size alone.
        material.SetShaderParameter("proj_y", UeSpace.DirectionToGodot(p.Scale.Y < 0f ? y * -1f : y));
        material.SetShaderParameter("proj_z", UeSpace.DirectionToGodot(p.Scale.Z < 0f ? z * -1f : z));
        material.SetShaderParameter("proj_shape", new Vector4(UeSpace.LengthToGodot(halfW), UeSpace.LengthToGodot(halfH),
            UeSpace.LengthToGodot(apex), p.ProjectOnBackfaces ? 1f : 0f));
        if (p.Flipbook is { } fb)
        {
            material.SetShaderParameter("proj_flip", new Vector4(fb.Cols, fb.Rows, fb.Frames, fb.Fps));
        }

        if (p.UvMatrix is { } m)
        {
            material.SetShaderParameter("uv_row_u", m.RowU);
            material.SetShaderParameter("uv_row_v", m.RowV);
        }

        if (p.UvAnim is { Code: 1 or 2 } anim)
        {
            material.SetShaderParameter("uv_anim_type", anim.Code);
            material.SetShaderParameter("uv_anim_a", anim.A);
            material.SetShaderParameter("uv_anim_b", anim.B);
        }

        // The frustum's eight corners: the near face at the projector, the far face at MaxTraceDistance.
        var corners = new Vector3[8];
        for (int i = 0; i < 8; i++)
        {
            bool far = (i & 4) != 0;
            float scale = far ? farScale : 1f;
            FVector c = p.Location + (x * (far ? p.MaxTrace : 0f))
                + (y * (((i & 1) != 0 ? 1f : -1f) * halfW * scale)) + (z * (((i & 2) != 0 ? 1f : -1f) * halfH * scale));
            corners[i] = UeSpace.ToGodot(c);
        }

        return new MeshInstance3D
        {
            Name = "Projector_" + p.Name,
            Mesh = BoxMesh(corners),
            MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Layers = RenderLayers.World,
        };
    }

    private static Vector4 Pos4(FVector location)
    {
        Vector3 g = UeSpace.ToGodot(location);
        return new Vector4(g.X, g.Y, g.Z, 0f);
    }

    /// <summary>The six faces of the frustum box, each with its outward normal (the shader keeps the far side).</summary>
    private static ArrayMesh BoxMesh(Vector3[] c)
    {
        int[][] faces = [[0, 1, 3, 2], [4, 5, 7, 6], [0, 1, 5, 4], [2, 3, 7, 6], [0, 2, 6, 4], [1, 3, 7, 5]];
        Vector3 centre = Vector3.Zero;
        foreach (Vector3 v in c)
        {
            centre += v / 8f;
        }

        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        foreach (int[] f in faces)
        {
            Vector3 a = c[f[0]], b = c[f[1]], d = c[f[2]], e = c[f[3]];
            Vector3 n = (b - a).Cross(e - a);
            if (n.LengthSquared() < 1e-12f)
            {
                n = (d - b).Cross(e - b);
            }

            n = n.Normalized();
            if (n.Dot(((a + b + d + e) / 4f) - centre) < 0f)
            {
                n = -n;
            }

            foreach (Vector3 v in new[] { a, b, d, a, d, e })
            {
                st.SetNormal(n);
                st.AddVertex(v);
            }
        }

        return st.Commit();
    }

    /// <param name="frameBufferBlending">EProjectorBlending of the frame buffer.</param>
    /// <param name="staticMeshes">bProjectStaticMesh: false skips pixels whose visible surface is a baked static mesh
    /// (the world template writes <see cref="Materials.MaterialCompiler.StencilStaticMesh"/> there).</param>
    [Provenance(ProvenanceKind.Estimated, "bProjectStaticMesh by the stencil of the visible surface: vertex-lit static meshes are skipped, actor-lit ones (no baked colours) still receive")]
    private static Shader ShaderFor(int frameBufferBlending, bool staticMeshes)
    {
        // PB_AlphaBlend and PB_Add blend as named; PB_None and PB_Modulate both modulate 2x (SetProjectorMaterial).
        (string define, string blend) = frameBufferBlending switch
        {
            2 => ("PROJ_ALPHA", "blend_mix"),
            3 => ("PROJ_ADD", "blend_add"),
            _ => ("PROJ_MODULATE", "blend_mul"),
        };
        string key = staticMeshes ? define : define + " NO_STATIC_MESH";
        if (!Shaders.TryGetValue(key, out Shader? shader))
        {
            string stencil = staticMeshes
                ? ""
                : $"stencil_mode read, compare_not_equal, {Materials.MaterialCompiler.StencilStaticMesh};\n";
            shader = new Shader
            {
                Code = "shader_type spatial;\nrender_mode unshaded, fog_disabled, cull_disabled, depth_test_disabled, depth_draw_never, "
                    + blend + ";\n" + stencil + "#define " + define + "\n#include \"" + ShaderInclude + "\"\n",
                ResourceName = "svm_projector " + key,
            };
            Shaders[key] = shader;
        }

        return shader;
    }
}
