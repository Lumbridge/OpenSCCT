using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Godot;
using OpenScctGame.Render.Data;
using OpenScctGame.Render.Geometry;
using OpenScctGame.Render.Materials;

namespace OpenScctGame.Render.RdProto;

/// <summary>
/// SVM_RD_PROTO: the merged cell geometry MapBuilder made, copied out as it flushes each cell and regrouped per
/// (cell, diffuse, lightmap, blend, sidedness) into one interleaved vertex stream and one index stream for
/// <see cref="RdProtoRenderer"/>. A measurement prototype: gameplay meshes, scenery, particles and characters are not
/// captured.
/// </summary>
internal sealed class RdProtoGeometry
{
    public static bool Enabled { get; } = System.Environment.GetEnvironmentVariable("SVM_RD_PROTO") == "1";

    /// <summary>Vertex flags in the colour's alpha byte (the shader reads them back as an integer).</summary>
    public const byte FlagSky = 1, FlagFullbright = 2;

    /// <summary>One vertex: 32 bytes, matching <see cref="RdProtoRenderer"/>'s vertex format.</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct Vertex
    {
        public Vector3 Position;
        public Vector2 Uv;
        public Vector2 Uv2;
        /// <summary>RGBA8: the vertex light (half the light term, as Godot's COLOR) and the flags in alpha.</summary>
        public uint Rgba;
    }

    /// <summary>A draw: a range of the index stream with one texture pair and one pipeline state.</summary>
    public sealed class Batch
    {
        public required bool Sky { get; init; }
        public required MaterialBlend Blend { get; init; }
        public required bool TwoSided { get; init; }
        /// <summary>Godot texture RIDs (the RD textures are looked up on the render thread); default = none.</summary>
        public required Rid Diffuse { get; init; }
        public required Rid Lightmap { get; init; }
        public uint IndexOffset { get; set; }
        public uint IndexCount { get; set; }
        public Aabb Bounds { get; set; }
    }

    private readonly record struct Key(int X, int Y, bool Sky, Texture2D? Diffuse, Texture2D? Lightmap, MaterialBlend Blend, bool TwoSided);

    private sealed class Builder
    {
        public readonly List<Vertex> Vertices = [];
        public readonly List<uint> Indices = [];
        public Aabb Bounds;
        public bool Any;
    }

    private readonly Dictionary<Key, Builder> _builders = [];
    // Keeps the textures referenced while the RIDs are in use.
    private readonly HashSet<Texture2D> _textures = [];

    public Vertex[] Vertices { get; private set; } = [];
    public uint[] Indices { get; private set; } = [];
    public List<Batch> Batches { get; } = [];
    public int Skipped { get; private set; }

    /// <summary>
    /// One flushed cell bucket (MapBuilder.FlushCells): its material as compiled for Godot (the textures are read
    /// from its tex_diffuse / tex_lightmap parameters), the material entry (blend, sidedness, unlit) and the surface.
    /// </summary>
    public void Add((int X, int Y, int Sky) cell, LightSource light, Material material, MaterialEntry? entry, SurfaceBuilder surface, TextureCache textures)
    {
        if (entry is null)
        {
            Skipped++;
            return;
        }

        var shader = material as ShaderMaterial;
        Texture2D? diffuse = shader?.GetShaderParameter("tex_diffuse").Obj as Texture2D ?? textures.Get(entry.Texture);
        Texture2D? lightmap = shader?.GetShaderParameter("tex_lightmap").Obj as Texture2D;
        bool unlit = entry.Unlit && entry.SelfIllum is null;
        // MaterialCompiler.LitBy: a lightmapped surface without a lightmap, and unlit materials, draw fullbright.
        bool vertexLit = lightmap is null && light == LightSource.Vertex && !unlit;
        bool fullbright = lightmap is null && !vertexLit;
        var key = new Key(cell.X, cell.Y, cell.Sky != 0, diffuse, lightmap, entry.Blend, entry.TwoSided);
        if (!_builders.TryGetValue(key, out Builder? b))
        {
            b = new Builder();
            _builders[key] = b;
        }

        if (diffuse is not null) _textures.Add(diffuse);
        if (lightmap is not null) _textures.Add(lightmap);
        (Vector3[] positions, Vector2[] uvs, Vector2[]? uv2s, Color[]? colours, int[] indices) = surface.RawStreams();
        uint baseVertex = (uint)b.Vertices.Count;
        byte flags = (byte)((cell.Sky != 0 ? FlagSky : 0) | (fullbright ? FlagFullbright : 0));
        b.Vertices.EnsureCapacity(b.Vertices.Count + positions.Length);
        for (int i = 0; i < positions.Length; i++)
        {
            uint rgb = 0;
            if (vertexLit)
            {
                Color c = colours is null ? new Color(1f, 1f, 1f) : colours[i];
                rgb = Byte(c.R) | (Byte(c.G) << 8) | (Byte(c.B) << 16);
            }

            b.Vertices.Add(new Vertex
            {
                Position = positions[i],
                Uv = uvs[i],
                Uv2 = uv2s is null ? Vector2.Zero : uv2s[i],
                Rgba = rgb | ((uint)flags << 24),
            });
            b.Bounds = b.Any ? b.Bounds.Expand(positions[i]) : new Aabb(positions[i], Vector3.Zero);
            b.Any = true;
        }

        b.Indices.EnsureCapacity(b.Indices.Count + indices.Length);
        foreach (int index in indices)
        {
            b.Indices.Add(baseVertex + (uint)index);
        }
    }

    private static uint Byte(float v) => (uint)Math.Clamp((int)MathF.Round(v * 255f), 0, 255);

    /// <summary>Concatenates the batches into one vertex and one index stream (indices absolute in the stream).</summary>
    public void Finish()
    {
        int vertices = 0, indices = 0;
        foreach (Builder b in _builders.Values)
        {
            vertices += b.Vertices.Count;
            indices += b.Indices.Count;
        }

        var vs = new Vertex[vertices];
        var ix = new uint[indices];
        int v = 0, n = 0;
        foreach ((Key key, Builder b) in _builders)
        {
            if (b.Indices.Count == 0) continue;
            CollectionsMarshal.AsSpan(b.Vertices).CopyTo(vs.AsSpan(v));
            var batch = new Batch
            {
                Sky = key.Sky, Blend = key.Blend, TwoSided = key.TwoSided,
                Diffuse = key.Diffuse?.GetRid() ?? default, Lightmap = key.Lightmap?.GetRid() ?? default,
                IndexOffset = (uint)n, IndexCount = (uint)b.Indices.Count, Bounds = b.Bounds,
            };
            foreach (uint index in b.Indices)
            {
                ix[n++] = index + (uint)v;
            }

            v += b.Vertices.Count;
            Batches.Add(batch);
        }

        Vertices = vs;
        Indices = ix;
        _builders.Clear();
    }
}
