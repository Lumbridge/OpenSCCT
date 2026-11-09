using System;
using System.Collections.Generic;
using Godot;
using OpenScctGame.Render.Data;

namespace OpenScctGame.Render;

/// <summary>
/// Finds a sky zone whose fog hides everything in it (AquaE, GhosS, OrphD: a small box fogged from 0 to 16 uu). Its
/// sky view then holds the zone's full fog in every pixel whatever way the camera turns, so the view can draw it once
/// and keep the picture (docs/RENDERING.md#sky-zone).
/// </summary>
internal static class FoggedSkyZone
{
    // Fog covers fully from max(FogStart, FogEnd); every sky triangle must stay this far beyond it (uu).
    private const float FogMarginUnits = 4f;

    // Keeps every sky triangle clear of the near plane up to a field of view of about 176 degrees (uu).
    private const float NearMarginUnits = 32f * RenderK.NearClipUnits;

    // A triangle faces the eye only when the eye is this far in front of its plane (m).
    private const float FacingMargin = 0.005f;

    /// <summary>
    /// True when every pixel the sky view draws from <paramref name="eye"/> (Godot space) is the sky zone's full fog,
    /// for every view direction. The zone has fog. The sky layer holds only visible, opaque world meshes that share
    /// one transform. Every triangle lies beyond the fog's full distance and well inside the far plane, so the
    /// surface writes the fog colour alone. Every triangle faces the eye and every edge is shared with an opposite
    /// one, so the triangles close around the eye and every view ray meets one of them: no pixel keeps the clear
    /// colour.
    /// </summary>
    public static bool IsSolid(RenderMap map, Vector3 eye)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (map.Lighting.SkyZone < 0 || map.Lighting.ZoneByIndex(map.Lighting.SkyZone) is not { Fog: true } zone)
        {
            return false;
        }

        float near = UeSpace.LengthToGodot(Math.Max(Math.Max(zone.FogStart, zone.FogEnd) + FogMarginUnits, NearMarginUnits));
        float far = UeSpace.LengthToGodot(RenderK.FarClipUnits) * 0.5f;
        var meshes = new List<MeshInstance3D>();
        if (!CollectSkyMeshes(map, map.Root, meshes) || meshes.Count == 0)
        {
            return false;
        }

        // One transform for all: shared edges then reach the GPU as the same positions, with no crack between them.
        Transform3D transform = WorldTransform(meshes[0]);
        if (transform.Basis.Determinant() <= 0f)
        {
            return false;
        }

        var edges = new Dictionary<(Vector3 From, Vector3 To), int>();
        bool facesEye = false;
        foreach (MeshInstance3D node in meshes)
        {
            if (WorldTransform(node) != transform)
            {
                return false;
            }

            var mesh = (ArrayMesh)node.Mesh;
            for (int s = 0; s < mesh.GetSurfaceCount(); s++)
            {
                using Godot.Collections.Array arrays = mesh.SurfaceGetArrays(s);
                Vector3[] vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                int[] indices = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
                int count = indices.Length > 0 ? indices.Length : vertices.Length;
                for (int t = 0; t + 2 < count; t += 3)
                {
                    Vector3 la = vertices[indices.Length > 0 ? indices[t] : t];
                    Vector3 lb = vertices[indices.Length > 0 ? indices[t + 1] : t + 1];
                    Vector3 lc = vertices[indices.Length > 0 ? indices[t + 2] : t + 2];
                    AddEdge(edges, la, lb);
                    AddEdge(edges, lb, lc);
                    AddEdge(edges, lc, la);

                    // Relative to the eye, so float error stays far below the margins.
                    Vector3 a = (transform * la) - eye, b = (transform * lb) - eye, c = (transform * lc) - eye;
                    if (a.Length() > far || b.Length() > far || c.Length() > far || ClosestToOrigin(a, b, c) < near)
                    {
                        return false;
                    }

                    // Godot fronts are clockwise: in index order (a, b, c) the front normal is (c - a) x (b - a). Every
                    // face must front the eye, two-sided or not: that keeps the winding number checked below nonzero.
                    Vector3 normal = (c - a).Cross(b - a);
                    if (normal.LengthSquared() < 1e-12f)
                    {
                        continue;
                    }

                    if (normal.Normalized().Dot(-a) <= FacingMargin)
                    {
                        return false;
                    }

                    facesEye = true;
                }
            }
        }

        // Closed: each directed edge is matched by as many in the opposite direction. With every face in front of the
        // eye the surface's winding number around it is not zero, so no ray from the eye leaves without a hit.
        foreach (((Vector3 from, Vector3 to), int n) in edges)
        {
            if (edges.GetValueOrDefault((to, from)) != n)
            {
                return false;
            }
        }

        return facesEye;
    }

    /// <summary>The sky-layer meshes under <paramref name="node"/>; false when the sky layer holds anything else.</summary>
    private static bool CollectSkyMeshes(RenderMap map, Node node, List<MeshInstance3D> meshes)
    {
        if (node is VisualInstance3D visual && (visual.Layers & RenderLayers.Sky) != 0)
        {
            if (visual is not MeshInstance3D mesh || !IsPlainOpaque(map, mesh))
            {
                return false;
            }

            meshes.Add(mesh);
        }

        for (int i = 0; i < node.GetChildCount(); i++)
        {
            if (!CollectSkyMeshes(map, node.GetChild(i), meshes))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// A shown mesh of triangles in opaque map materials that stay opaque (no material swap, no authored LODs), drawn
    /// as built: nothing overrides its materials, fades it or culls it by a custom box or distance.
    /// </summary>
    private static bool IsPlainOpaque(RenderMap map, MeshInstance3D node)
    {
        if (node.Mesh is not ArrayMesh mesh || mesh.GetBlendShapeCount() != 0 || mesh.CustomAabb.Size != Vector3.Zero ||
            node.MaterialOverride is not null || node.MaterialOverlay is not null || node.Skin is not null ||
            node.Transparency != 0f || node.VisibilityRangeBegin != 0f || node.VisibilityRangeEnd != 0f)
        {
            return false;
        }

        for (Node? n = node; n is not null; n = n.GetParent())
        {
            if (n is Node3D { Visible: false })
            {
                return false;
            }
        }

        for (int s = 0; s < mesh.GetSurfaceCount(); s++)
        {
            if (mesh.SurfaceGetPrimitiveType(s) != Mesh.PrimitiveType.Triangles || node.GetSurfaceOverrideMaterial(s) is not null ||
                mesh.SurfaceGetMaterial(s) is not ShaderMaterial { NextPass: null } material ||
                map.Materials.Find(material.ResourceName) is not { Blend: MaterialBlend.Opaque, Authored: null } entry)
            {
                return false;
            }

            foreach (MaterialSequenceEntry sequence in entry.Sequences.Values)
            {
                if (sequence.SwitchMaterial)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static void AddEdge(Dictionary<(Vector3 From, Vector3 To), int> edges, Vector3 from, Vector3 to) =>
        edges[(from, to)] = edges.GetValueOrDefault((from, to)) + 1;

    /// <summary>The node's global transform, composed from its Node3D parents so it holds outside the tree too.</summary>
    private static Transform3D WorldTransform(Node3D node)
    {
        Transform3D transform = Transform3D.Identity;
        for (Node? n = node; n is Node3D n3; n = n3.GetParent())
        {
            transform = n3.Transform * transform;
            if (n3.TopLevel)
            {
                break;
            }
        }

        return transform;
    }

    /// <summary>The distance from the origin to the nearest point of triangle (a, b, c).</summary>
    private static float ClosestToOrigin(Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 normal = (b - a).Cross(c - a);
        float edges = Math.Min(Math.Min(ToSegment(a, b), ToSegment(b, c)), ToSegment(c, a));
        if (normal.LengthSquared() < 1e-12f)
        {
            return edges;
        }

        // The origin's foot on the plane: inside the triangle it is the nearest point, else an edge holds it.
        Vector3 n = normal.Normalized();
        Vector3 foot = n * n.Dot(a);
        bool inside = (b - a).Cross(foot - a).Dot(normal) >= 0f && (c - b).Cross(foot - b).Dot(normal) >= 0f &&
            (a - c).Cross(foot - c).Dot(normal) >= 0f;
        return inside ? Math.Min(edges, MathF.Abs(n.Dot(a))) : edges;
    }

    private static float ToSegment(Vector3 a, Vector3 b)
    {
        Vector3 ab = b - a;
        float lengthSquared = ab.LengthSquared();
        float t = lengthSquared > 0f ? Math.Clamp(-a.Dot(ab) / lengthSquared, 0f, 1f) : 0f;
        return (a + (ab * t)).Length();
    }
}
