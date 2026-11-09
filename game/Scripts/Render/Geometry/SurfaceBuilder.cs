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
    private readonly List<float> _echelon = [];
    private bool _anyEchelon;
    private readonly List<float> _bakedDirections = [];
    private bool _anyBakedDirection;
    private readonly List<int> _indices = [];
    private readonly List<Pending> _pending = [];
    private Streams? _streams;

    /// <summary>A primitive queued by <see cref="Queue"/>, with the Vision and Echelon current when it was queued.</summary>
    private readonly record struct Pending(Transform3D Transform, MeshPrimitive Prim, byte Zone, byte[]? Zones, Vector4 Vision, int Echelon);

    /// <summary>The streams as the arrays a mesh surface takes (<see cref="Pack"/>).</summary>
    private sealed record Streams(Vector3[] Positions, Vector3[] Normals, Vector2[] Uvs, Vector2[]? Uv2s, Color[]? Colours,
        int[] Indices, float[]? Vision, float[]? Echelon, float[]? BakedDirections);

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

    /// <summary>
    /// The Echelon code the next <see cref="Add"/> writes (RenderMap.EchelonCode: the spot shadow lights the surface
    /// skips), carried in CUSTOM1 when any vertex has one.
    /// </summary>
    public int Echelon { get; set; }

    public bool HasUv2 { get; }

    public bool HasColour { get; }

    public int VertexCount
    {
        get
        {
            Merge();
            return _positions.Count;
        }
    }

    public int TriangleCount
    {
        get
        {
            Merge();
            return _indices.Count / 3;
        }
    }

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
        Merge();
        _streams = null;
        Write(transform, prim, zone, triangleZones, Vision, Echelon);
    }

    /// <summary>
    /// <see cref="Add"/> later: queues the primitive with the current <see cref="Vision"/> and <see cref="Echelon"/>
    /// until <see cref="Prepare"/> (or anything that reads the surface) writes the queue in order. The arrays it is
    /// given must not change meanwhile.
    /// </summary>
    public void Queue(in Transform3D transform, MeshPrimitive prim, byte zone, byte[]? triangleZones = null)
    {
        _streams = null;
        _pending.Add(new Pending(transform, prim, zone, triangleZones, Vision, Echelon));
    }

    /// <summary>
    /// Writes the queued primitives in order and packs the streams for <see cref="AddTo"/>. Plain data work: a worker
    /// thread may run it, one thread per builder, with the same result as on the main thread.
    /// </summary>
    public void Prepare()
    {
        Merge();
        _streams ??= Pack();
    }

    private void Merge()
    {
        if (_pending.Count == 0)
        {
            return;
        }

        int indices = 0;
        int vertices = 0;
        foreach (Pending p in _pending)
        {
            indices += p.Prim.Indices.Length;
            vertices += Math.Min(p.Prim.VertexCount, p.Prim.Indices.Length);
        }

        Reserve(_positions.Count + vertices, _indices.Count + indices);
        foreach (Pending p in _pending)
        {
            Write(p.Transform, p.Prim, p.Zone, p.Zones, p.Vision, p.Echelon);
        }

        _pending.Clear();
    }

    private void Reserve(int vertices, int indices)
    {
        _positions.EnsureCapacity(vertices);
        _normals.EnsureCapacity(vertices);
        _uvs.EnsureCapacity(vertices);
        if (HasUv2) _uv2s.EnsureCapacity(vertices);
        if (HasColour) _colours.EnsureCapacity(vertices);
        if (HasVision) _vision.EnsureCapacity(vertices * 4);
        _echelon.EnsureCapacity(vertices);
        _bakedDirections.EnsureCapacity(vertices * 4);
        _indices.EnsureCapacity(indices);
    }

    private void Write(in Transform3D transform, MeshPrimitive prim, byte zone, byte[]? triangleZones, Vector4 vision, int echelon)
    {
        Basis normalBasis = transform.Basis.Inverse().Transposed();
        bool mirrored = transform.Basis.Determinant() < 0f;
        int[] idx = prim.Indices;

        // Where each source vertex was first written, and with which zone. The same vertex in a triangle of another
        // zone (BSP surfaces) is looked up by vertex and zone: one copy per vertex and zone, in first-use order.
        var first = new int[prim.VertexCount];
        Array.Fill(first, -1);
        var firstZone = new byte[first.Length];
        Dictionary<long, int>? other = null;
        Span<int> tri = stackalloc int[3];
        for (int t = 0; t + 2 < idx.Length; t += 3)
        {
            byte z = triangleZones is null ? zone : triangleZones[t / 3];
            for (int k = 0; k < 3; k++)
            {
                int v = idx[t + k];
                int at = first[v];
                if (at < 0)
                {
                    at = _positions.Count;
                    WriteVertex(transform, normalBasis, prim, v, z, vision, echelon);
                    first[v] = at;
                    firstZone[v] = z;
                }
                else if (firstZone[v] != z)
                {
                    other ??= [];
                    long key = ((long)v << 8) | z;
                    if (!other.TryGetValue(key, out at))
                    {
                        at = _positions.Count;
                        WriteVertex(transform, normalBasis, prim, v, z, vision, echelon);
                        other[key] = at;
                    }
                }

                tri[k] = at;
            }

            _indices.Add(tri[0]);
            _indices.Add(mirrored ? tri[1] : tri[2]);
            _indices.Add(mirrored ? tri[2] : tri[1]);
        }
    }

    private void WriteVertex(in Transform3D transform, in Basis normalBasis, MeshPrimitive prim, int i, byte zone, Vector4 vision, int echelon)
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

        _echelon.Add(echelon);
        _anyEchelon |= echelon != 0;
        if (prim.BakedDirections is { } data)
        {
            _anyBakedDirection = true;
            for (int k = 0; k < 4; k++) _bakedDirections.Add(data[i * 4 + k]);
        }
        else
        {
            _bakedDirections.Add(.5f);
            _bakedDirections.Add(.5f);
            _bakedDirections.Add(.5f);
            _bakedDirections.Add(0f);
        }

        if (HasVision)
        {
            // w + 2 marks the stream as present: a mesh without CUSTOM0 reads w below 1.5.
            _vision.Add(vision.X);
            _vision.Add(vision.Y);
            _vision.Add(vision.Z);
            _vision.Add(vision.W + 2f);
        }
    }

    private Streams Pack() => new(
        _positions.ToArray(),
        _normals.ToArray(),
        _uvs.ToArray(),
        HasUv2 ? _uv2s.ToArray() : null,
        HasColour ? _colours.ToArray() : null,
        _indices.ToArray(),
        HasVision ? _vision.ToArray() : null,
        _anyEchelon ? _echelon.ToArray() : null,
        _anyBakedDirection ? _bakedDirections.ToArray() : null);

    /// <summary>The packed streams (SVM_RD_PROTO copies them into its own buffers, RdProtoGeometry).</summary>
    internal (Vector3[] Positions, Vector2[] Uvs, Vector2[]? Uv2s, Color[]? Colours, int[] Indices) RawStreams()
    {
        Prepare();
        Streams s = _streams!;
        return (s.Positions, s.Uvs, s.Uv2s, s.Colours, s.Indices);
    }

    /// <summary>The packed streams the world pass on the RenderingDevice copies into its own buffers (Rd/RdWorldGeometry).</summary>
    internal (Vector3[] Positions, Vector3[] Normals, Vector2[] Uvs, Vector2[]? Uv2s, Color[]? Colours, float[]? Echelon, int[] Indices) RdStreams()
    {
        Prepare();
        Streams s = _streams!;
        return (s.Positions, s.Normals, s.Uvs, s.Uv2s, s.Colours, s.Echelon, s.Indices);
    }

    /// <summary>Appends this surface to <paramref name="mesh"/>.</summary>
    public void AddTo(ArrayMesh mesh, Material? material)
    {
        Prepare();
        Streams s = _streams!;
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = s.Positions;
        arrays[(int)Mesh.ArrayType.Normal] = s.Normals;
        arrays[(int)Mesh.ArrayType.TexUV] = s.Uvs;
        if (s.Uv2s is not null)
        {
            arrays[(int)Mesh.ArrayType.TexUV2] = s.Uv2s;
        }

        if (s.Colours is not null)
        {
            arrays[(int)Mesh.ArrayType.Color] = s.Colours;
        }

        arrays[(int)Mesh.ArrayType.Index] = s.Indices;
        Mesh.ArrayFormat flags = 0;
        if (s.Vision is not null)
        {
            arrays[(int)Mesh.ArrayType.Custom0] = s.Vision;
            flags = (Mesh.ArrayFormat)((long)Mesh.ArrayCustomFormat.RgbaFloat << (int)Mesh.ArrayFormat.FormatCustom0Shift);
        }

        if (s.Echelon is not null)
        {
            arrays[(int)Mesh.ArrayType.Custom1] = s.Echelon;
            flags |= (Mesh.ArrayFormat)((long)Mesh.ArrayCustomFormat.RFloat << (int)Mesh.ArrayFormat.FormatCustom1Shift);
        }
        if (s.BakedDirections is not null)
        {
            arrays[(int)Mesh.ArrayType.Custom2] = s.BakedDirections;
            flags |= (Mesh.ArrayFormat)((long)Mesh.ArrayCustomFormat.RgbaFloat << (int)Mesh.ArrayFormat.FormatCustom2Shift);
        }

        using var lods = material?.HasMeta("authored_displacement") == true && material.GetMeta("authored_displacement").AsBool()
            ? Materials.AuthoredMeshLods.Create(arrays, (ulong)flags) : new Godot.Collections.Dictionary();
        var blendShapes = new Godot.Collections.Array<Godot.Collections.Array>();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays, blendShapes, lods, flags);
        GC.KeepAlive(blendShapes);
        if (material is not null)
        {
            mesh.SurfaceSetMaterial(mesh.GetSurfaceCount() - 1, material);
        }
    }
}

/// <summary>A glTF primitive read into arrays, in render space.</summary>
internal sealed class MeshPrimitive
{
    public int Section { get; init; } = -1;

    public required string Material { get; init; }

    public required float[] Positions { get; init; }

    public float[]? Normals { get; init; }

    public float[]? Uvs { get; init; }

    public float[]? Uv2s { get; init; }

    /// <summary>COLOR_0 as normalized RGBA bytes.</summary>
    public byte[]? Colours { get; init; }
    /// <summary>COLOR_1: encoded world-space dominant light direction and coherence for authored materials.</summary>
    public float[]? BakedDirections { get; init; }

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
