using System;
using System.Collections.Generic;
using Godot;

namespace OpenScctGame.Render.Geometry;

/// <summary>One surface being merged: vertices of many primitives that share a material, in one space.</summary>
internal sealed class SurfaceBuilder
{
    private readonly List<Vector3> _positions = [];
    private readonly List<Vector3> _normals = [];
    private readonly List<Vector2> _uvs = [];
    private readonly List<Vector2> _uv2s = [];
    private readonly List<Color> _colours = [];
    private readonly List<float> _vision = [];
    private readonly List<int> _indices = [];

    public SurfaceBuilder(bool hasUv2, bool hasColour, bool hasVision = false)
    {
        HasUv2 = hasUv2;
        HasColour = hasColour;
        HasVision = hasVision;
    }

    /// <summary>
    /// The default vision values of a surface with no actor values of its own (Actor class defaults: HeatIntensity 0,
    /// HeatOpacity 1, no EMF glow, EMFOpacity 1).
    /// </summary>
    public static readonly Vector4 DefaultVision = new(0f, 1f, 0f, 1f);

    /// <summary>
    /// Whether the surface carries a per-vertex vision stream (CUSTOM0: HeatIntensity, HeatOpacity, EMF glow, EMFOpacity
    /// + 2): merged geometry, whose instances cannot carry their own instance values.
    /// </summary>
    public bool HasVision { get; }

    /// <summary>The vision values the next <see cref="Add"/> writes (merged surfaces only).</summary>
    public Vector4 Vision { get; set; } = DefaultVision;

    public bool HasUv2 { get; }

    public bool HasColour { get; }

    public int VertexCount => _positions.Count;

    public int TriangleCount => _indices.Count / 3;

    /// <summary>
    /// Appends a primitive in render space (glTF), transformed by <paramref name="transform"/>. Only the vertices its
    /// triangles use are written. glTF triangles are counter-clockwise; Godot's front faces are clockwise, so each
    /// triangle's second and third index swap, unless the transform mirrors (negative determinant), which flips the
    /// winding back. The colour stream carries the primitive's baked colour (white without one) and, in alpha, the
    /// zone index / 255 of the triangle (<paramref name="triangleZones"/>, one per triangle, else
    /// <paramref name="zone"/>) for zone-limited dynamic lights; a vertex shared by triangles of different zones is
    /// written once per zone, so no triangle blends two zones.
    /// </summary>
    public void Add(in Transform3D transform, MeshPrimitive prim, byte zone, byte[]? triangleZones = null)
    {
        Basis normalBasis = transform.Basis.Inverse().Transposed();
        bool mirrored = transform.Basis.Determinant() < 0f;
        var written = new Dictionary<long, int>();
        int[] idx = prim.Indices;
        Span<int> tri = stackalloc int[3];
        for (int t = 0; t + 2 < idx.Length; t += 3)
        {
            byte z = triangleZones is null ? zone : triangleZones[t / 3];
            for (int k = 0; k < 3; k++)
            {
                int v = idx[t + k];
                long key = ((long)v << 8) | z;
                if (!written.TryGetValue(key, out int at))
                {
                    at = _positions.Count;
                    Write(transform, normalBasis, prim, v, z);
                    written[key] = at;
                }

                tri[k] = at;
            }

            _indices.Add(tri[0]);
            _indices.Add(mirrored ? tri[1] : tri[2]);
            _indices.Add(mirrored ? tri[2] : tri[1]);
        }
    }

    private void Write(in Transform3D transform, in Basis normalBasis, MeshPrimitive prim, int i, byte zone)
    {
        var p = new Vector3(prim.Positions[i * 3], prim.Positions[(i * 3) + 1], prim.Positions[(i * 3) + 2]);
        _positions.Add(transform * p);
        Vector3 n = prim.Normals is null
            ? Vector3.Up
            : new Vector3(prim.Normals[i * 3], prim.Normals[(i * 3) + 1], prim.Normals[(i * 3) + 2]);
        _normals.Add((normalBasis * n).Normalized());
        _uvs.Add(prim.Uvs is null ? Vector2.Zero : new Vector2(prim.Uvs[i * 2], prim.Uvs[(i * 2) + 1]));
        if (HasUv2)
        {
            _uv2s.Add(prim.Uv2s is null ? Vector2.Zero : new Vector2(prim.Uv2s[i * 2], prim.Uv2s[(i * 2) + 1]));
        }

        if (HasColour)
        {
            Color c = prim.Colours is null ? new Color(1f, 1f, 1f) : prim.ColourAt(i);
            c.A = zone / 255f;
            _colours.Add(c);
        }

        if (HasVision)
        {
            // w + 2 marks the stream as present: a mesh without CUSTOM0 reads w below 1.5.
            _vision.Add(Vision.X);
            _vision.Add(Vision.Y);
            _vision.Add(Vision.Z);
            _vision.Add(Vision.W + 2f);
        }
    }

    /// <summary>Appends this surface to <paramref name="mesh"/>.</summary>
    public void AddTo(ArrayMesh mesh, Material? material)
    {
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = _positions.ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = _normals.ToArray();
        arrays[(int)Mesh.ArrayType.TexUV] = _uvs.ToArray();
        if (HasUv2)
        {
            arrays[(int)Mesh.ArrayType.TexUV2] = _uv2s.ToArray();
        }

        if (HasColour)
        {
            arrays[(int)Mesh.ArrayType.Color] = _colours.ToArray();
        }

        arrays[(int)Mesh.ArrayType.Index] = _indices.ToArray();
        Mesh.ArrayFormat flags = 0;
        if (HasVision)
        {
            arrays[(int)Mesh.ArrayType.Custom0] = _vision.ToArray();
            flags = (Mesh.ArrayFormat)((long)Mesh.ArrayCustomFormat.RgbaFloat << (int)Mesh.ArrayFormat.FormatCustom0Shift);
        }

        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays, null, null, flags);
        if (material is not null)
        {
            mesh.SurfaceSetMaterial(mesh.GetSurfaceCount() - 1, material);
        }
    }
}

/// <summary>A glTF primitive read into arrays, in render space.</summary>
internal sealed class MeshPrimitive
{
    public required string Material { get; init; }

    public required float[] Positions { get; init; }

    public float[]? Normals { get; init; }

    public float[]? Uvs { get; init; }

    public float[]? Uv2s { get; init; }

    /// <summary>COLOR_0 as normalized RGBA bytes.</summary>
    public byte[]? Colours { get; init; }

    public required int[] Indices { get; init; }

    public int VertexCount => Positions.Length / 3;

    public Color ColourAt(int i) => Colours is null
        ? new Color(1f, 1f, 1f, 1f)
        : new Color(Colours[i * 4] / 255f, Colours[(i * 4) + 1] / 255f, Colours[(i * 4) + 2] / 255f, Colours[(i * 4) + 3] / 255f);

    public Aabb Bounds(in Transform3D transform)
    {
        var box = new Aabb();
        for (int i = 0; i < VertexCount; i++)
        {
            Vector3 p = transform * new Vector3(Positions[i * 3], Positions[(i * 3) + 1], Positions[(i * 3) + 2]);
            box = i == 0 ? new Aabb(p, Vector3.Zero) : box.Expand(p);
        }

        return box;
    }
}
