using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Godot;

namespace OpenScctGame.Render.Particles;

/// <summary>
/// Rewrites a spark's line surface without rebuilding its Godot arrays. The segment count is exact: unlike
/// zero-area triangles, padded zero-length lines are not guaranteed to produce no fragments on every renderer.
/// </summary>
public sealed class LineBuffer
{
    private readonly ArrayMesh _mesh;
    private readonly Rid _rid;
    private readonly byte[] _vertex;
    private readonly byte[] _attribute;

    private LineBuffer(ArrayMesh mesh, int segments)
    {
        _mesh = mesh;
        _rid = mesh.GetRid();
        Count = segments;
        _vertex = new byte[segments * 2 * QuadBuffer.VertexStride];
        _attribute = new byte[segments * 2 * QuadBuffer.AttributeStride];
    }

    public int Count { get; }

    public static int Built { get; private set; }

    public static int Fallbacks { get; private set; }

    /// <summary>Retains only the known uncompressed position/RGBA8/UV/UV2 layout; other layouts rebuild normally.</summary>
    public static LineBuffer? For(ArrayMesh mesh, int segments)
    {
        int vertices = segments * 2;
        if (mesh.GetSurfaceCount() != 1 || mesh.SurfaceGetArrayLen(0) != vertices) return null;
        var format = (RenderingServer.ArrayFormat)(long)mesh.SurfaceGetFormat(0);
        bool layout = (format & RenderingServer.ArrayFormat.FlagCompressAttributes) == 0
            && RenderingServer.MeshSurfaceGetFormatVertexStride(format, vertices) == QuadBuffer.VertexStride
            && RenderingServer.MeshSurfaceGetFormatAttributeStride(format, vertices) == QuadBuffer.AttributeStride
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
        return new LineBuffer(mesh, segments);
    }

    /// <summary>Updates the same line vertices, UVs and byte colours; false when the primitive count changed.</summary>
    public bool Fill(List<SparkSegment> segments)
    {
        if (segments.Count != Count || Count == 0) return false;
        Vector3 min = UeSpace.ToGodot(segments[0].From), max = min;
        for (int i = 0; i < Count; i++)
        {
            SparkSegment segment = segments[i];
            Write(i * 2, UeSpace.ToGodot(segment.From), new Vector2(0f, segment.V0), segment.Color, ref min, ref max);
            Write(i * 2 + 1, UeSpace.ToGodot(segment.To), new Vector2(1f, segment.V1), segment.Color, ref min, ref max);
        }

        RenderingServer.MeshSurfaceUpdateVertexRegion(_rid, 0, 0, _vertex);
        RenderingServer.MeshSurfaceUpdateAttributeRegion(_rid, 0, 0, _attribute);
        _mesh.CustomAabb = new Aabb(min, max - min);
        return true;
    }

    private void Write(int index, Vector3 position, Vector2 uv, FxColor colour, ref Vector3 min, ref Vector3 max)
    {
        MemoryMarshal.Write(_vertex.AsSpan(index * QuadBuffer.VertexStride, QuadBuffer.VertexStride), in position);
        Span<byte> attribute = _attribute.AsSpan(index * QuadBuffer.AttributeStride, QuadBuffer.AttributeStride);
        attribute[0] = colour.R;
        attribute[1] = colour.G;
        attribute[2] = colour.B;
        attribute[3] = colour.A;
        MemoryMarshal.Write(attribute.Slice(4, 8), in uv);
        MemoryMarshal.Write(attribute.Slice(12, 8), in uv);
        min = min.Min(position);
        max = max.Max(position);
    }
}
