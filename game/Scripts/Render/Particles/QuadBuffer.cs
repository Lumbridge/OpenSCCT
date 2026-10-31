using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Godot;
using OpenScct.Core;

namespace OpenScctGame.Render.Particles;

/// <summary>
/// A particle layer's quad surface rewritten in place each frame: the vertex (position) and attribute (colour, cell
/// uv, blend uv) streams are filled into byte buffers this object keeps and uploaded with
/// <c>RenderingServer.MeshSurfaceUpdateVertexRegion</c> and <c>MeshSurfaceUpdateAttributeRegion</c>. Building the
/// surface from arrays every frame made Godot arrays, variants and their finalizers per emitter per frame, the main
/// source of the play scene's garbage collections. The surface holds <see cref="Capacity"/> quads; quads past the
/// drawn count keep their corners at the origin, triangles of no area that draw nothing.
/// </summary>
public sealed class QuadBuffer
{
    /// <summary>Bytes of one vertex in the vertex stream: the position as three floats.</summary>
    public const int VertexStride = 12;

    /// <summary>Bytes of one vertex in the attribute stream: RGBA8 colour, uv (two floats), uv2 (two floats).</summary>
    public const int AttributeStride = 20;

    /// <summary>The smallest surface, in quads.</summary>
    public const int MinCapacity = 16;

    private readonly ArrayMesh _mesh;
    private readonly Rid _rid;
    private readonly byte[] _vertex;
    private readonly byte[] _attribute;
    private int _drawn;

    private QuadBuffer(ArrayMesh mesh, int capacity, int drawn)
    {
        _mesh = mesh;
        _rid = mesh.GetRid();
        Capacity = capacity;
        _vertex = new byte[capacity * 4 * VertexStride];
        _attribute = new byte[capacity * 4 * AttributeStride];
        _drawn = drawn;
    }

    /// <summary>Quad surfaces written in place since start (main thread; for the frame-time smoke's report).</summary>
    public static int Built { get; private set; }

    /// <summary>Quad surfaces whose layout was not the expected one, left to rebuild from arrays.</summary>
    public static int Fallbacks { get; private set; }

    /// <summary>Quads the surface holds.</summary>
    public int Capacity { get; }

    /// <summary>The surface size for <paramref name="quads"/> quads: the next power of two, at least <see cref="MinCapacity"/>.</summary>
    public static int CapacityFor(int quads)
    {
        int capacity = MinCapacity;
        while (capacity < quads)
        {
            capacity *= 2;
        }

        return capacity;
    }

    /// <summary>
    /// The buffer for surface 0 of <paramref name="mesh"/>, built from arrays for <paramref name="capacity"/> quads
    /// (<paramref name="drawn"/> of them in use), or null when the surface's streams are not laid out as this class
    /// writes them (then the layer keeps rebuilding from arrays).
    /// </summary>
    public static QuadBuffer? For(ArrayMesh mesh, int capacity, int drawn)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        if (mesh.GetSurfaceCount() != 1 || mesh.SurfaceGetArrayLen(0) != capacity * 4)
        {
            return null;
        }

        var format = (RenderingServer.ArrayFormat)(long)mesh.SurfaceGetFormat(0);
        int vertices = capacity * 4;
        bool layout = (format & RenderingServer.ArrayFormat.FlagCompressAttributes) == 0
            && RenderingServer.MeshSurfaceGetFormatVertexStride(format, vertices) == VertexStride
            && RenderingServer.MeshSurfaceGetFormatAttributeStride(format, vertices) == AttributeStride
            && RenderingServer.MeshSurfaceGetFormatOffset(format, vertices, (int)RenderingServer.ArrayType.Vertex) == 0
            && RenderingServer.MeshSurfaceGetFormatOffset(format, vertices, (int)RenderingServer.ArrayType.Color) == 0
            && RenderingServer.MeshSurfaceGetFormatOffset(format, vertices, (int)RenderingServer.ArrayType.TexUV) == 4
            && RenderingServer.MeshSurfaceGetFormatOffset(format, vertices, (int)RenderingServer.ArrayType.TexUV2) == 12;
        if (!layout)
        {
            Fallbacks++;
            return null;
        }

        Built++;
        return new QuadBuffer(mesh, capacity, drawn);
    }

    /// <summary>Writes and uploads <paramref name="quads"/>; false when they do not fit (the caller rebuilds bigger).</summary>
    public bool Fill(List<SpriteQuad> quads)
    {
        ArgumentNullException.ThrowIfNull(quads);
        int n = quads.Count;
        if (n > Capacity)
        {
            return false;
        }

        Span<byte> vertex = _vertex;
        Span<byte> attribute = _attribute;
        for (int i = 0; i < n; i++)
        {
            SpriteQuad q = quads[i];
            int at = i * 4;
            WriteVertex(vertex, at, q.C0);
            WriteVertex(vertex, at + 1, q.C1);
            WriteVertex(vertex, at + 2, q.C2);
            WriteVertex(vertex, at + 3, q.C3);
            WriteAttribute(attribute, at, q, q.U0, q.V1, q.BU0, q.BV1);
            WriteAttribute(attribute, at + 1, q, q.U1, q.V1, q.BU1, q.BV1);
            WriteAttribute(attribute, at + 2, q, q.U0, q.V0, q.BU0, q.BV0);
            WriteAttribute(attribute, at + 3, q, q.U1, q.V0, q.BU1, q.BV0);
        }

        if (_drawn > n)
        {
            vertex[(n * 4 * VertexStride)..(_drawn * 4 * VertexStride)].Clear();
            attribute[(n * 4 * AttributeStride)..(_drawn * 4 * AttributeStride)].Clear();
        }

        _drawn = n;
        RenderingServer.MeshSurfaceUpdateVertexRegion(_rid, 0, 0, _vertex);
        RenderingServer.MeshSurfaceUpdateAttributeRegion(_rid, 0, 0, _attribute);
        SetBounds(quads);
        return true;
    }

    /// <summary>The mesh's culling box: the corners of <paramref name="quads"/> (the surface's own box is of its first fill).</summary>
    public void SetBounds(List<SpriteQuad> quads)
    {
        ArgumentNullException.ThrowIfNull(quads);
        if (quads.Count > 0) _mesh.CustomAabb = Bounds(quads);
    }

    /// <summary>Exact world bounds of the generated corners, before uploading a layer.</summary>
    public static Aabb Bounds(List<SpriteQuad> quads)
    {
        if (quads.Count == 0) return default;

        Vector3 min = UeSpace.ToGodot(quads[0].C0);
        Vector3 max = min;
        foreach (SpriteQuad q in quads)
        {
            Grow(ref min, ref max, UeSpace.ToGodot(q.C0));
            Grow(ref min, ref max, UeSpace.ToGodot(q.C1));
            Grow(ref min, ref max, UeSpace.ToGodot(q.C2));
            Grow(ref min, ref max, UeSpace.ToGodot(q.C3));
        }

        return new Aabb(min, max - min);
    }

    private static void Grow(ref Vector3 min, ref Vector3 max, Vector3 p)
    {
        min = new Vector3(Math.Min(min.X, p.X), Math.Min(min.Y, p.Y), Math.Min(min.Z, p.Z));
        max = new Vector3(Math.Max(max.X, p.X), Math.Max(max.Y, p.Y), Math.Max(max.Z, p.Z));
    }

    private static void WriteVertex(Span<byte> vertex, int index, FVector corner)
    {
        Vector3 p = UeSpace.ToGodot(corner);
        MemoryMarshal.Write(vertex.Slice(index * VertexStride, VertexStride), in p);
    }

    private static void WriteAttribute(Span<byte> attribute, int index, in SpriteQuad q, float u, float v, float bu, float bv)
    {
        Span<byte> at = attribute.Slice(index * AttributeStride, AttributeStride);
        at[0] = q.Color.R;
        at[1] = q.Color.G;
        at[2] = q.Color.B;
        at[3] = q.Color.A;
        var uv = new Vector2(u, v);
        var uv2 = new Vector2(bu, bv);
        MemoryMarshal.Write(at.Slice(4, 8), in uv);
        MemoryMarshal.Write(at.Slice(12, 8), in uv2);
    }
}
