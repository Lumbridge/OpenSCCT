using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;

namespace OpenScctGame.Render.Characters;

/// <summary>
/// Where each texel of a mesh's material lies on another mesh of the same rig: both are taken to the second mesh's
/// bind pose (<see cref="BindMesh.PosedLike"/>), and every texel of the first mesh's UV layout finds the nearest
/// point of the second's surface facing the same way. A texture laid out for the second mesh can then be redrawn for
/// the first (<see cref="TexelMap.Bake"/>): a map outfit's model gets the stock suit's heat, EMF and wet masks, and the
/// first-person arms get the outfit's sleeves.
/// </summary>
internal sealed class SurfaceTransfer
{
    // Texels left without a source point (between UV islands) take a neighbour's, this many texels out, so filtering and
    // mipmaps do not pull in the empty colour at island edges.
    private const int Bleed = 4;

    private readonly BindMesh _target;
    private readonly Vector3[] _positions;
    private readonly Vector3[] _normals;
    private readonly bool[] _placed;
    private readonly BindMesh _source;
    private readonly Dictionary<(int X, int Y, int Z), List<int>> _grid = new();
    private float _cell;

    public SurfaceTransfer(BindMesh target, BindMesh source)
    {
        _target = target;
        _source = source;
        (_positions, _normals, _placed) = target.PosedLike(source);
    }

    public BindMesh Target => _target;

    public BindMesh Source => _source;

    // The source triangles by grid cell, made for the first map that has texels to place.
    private void BuildGrid()
    {
        if (_grid.Count > 0)
        {
            return;
        }

        // Cells about the size of an average triangle, so a query tests a handful of them.
        Vector3 min = new(float.MaxValue), max = new(float.MinValue);
        float extent = 0f;
        for (int t = 0; t < _source.TriangleCount; t++)
        {
            (Vector3 lo, Vector3 hi) = Bounds(t);
            min = Vector3.Min(min, lo);
            max = Vector3.Max(max, hi);
            Vector3 size = hi - lo;
            extent += MathF.Max(size.X, MathF.Max(size.Y, size.Z));
        }

        float diagonal = (max - min).Length();
        _cell = MathF.Max(extent / _source.TriangleCount, MathF.Max(diagonal / 256f, 1e-4f));
        for (int t = 0; t < _source.TriangleCount; t++)
        {
            (Vector3 lo, Vector3 hi) = Bounds(t);
            (int X, int Y, int Z) a = Cell(lo), b = Cell(hi);
            for (int x = a.X; x <= b.X; x++)
            {
                for (int y = a.Y; y <= b.Y; y++)
                {
                    for (int z = a.Z; z <= b.Z; z++)
                    {
                        if (!_grid.TryGetValue((x, y, z), out List<int>? list))
                        {
                            _grid[(x, y, z)] = list = [];
                        }

                        list.Add(t);
                    }
                }
            }
        }
    }

    /// <summary>
    /// The source point of every texel of the target material <paramref name="material"/>, at <paramref name="size"/>
    /// squared. Triangles with a corner the bones could not place (<see cref="BindMesh.PosedLike"/>) give their texels
    /// none.
    /// </summary>
    public TexelMap Map(int material, int size)
    {
        var map = new TexelMap(size);
        var texels = new List<(int Texel, Vector3 Position, Vector3 Normal)>();
        var taken = new bool[size * size];
        int[] corners = _target.Corners;
        for (int t = 0; t < _target.TriangleCount; t++)
        {
            if (_target.TriangleMaterials[t] != material)
            {
                continue;
            }

            int i0 = corners[t * 3], i1 = corners[(t * 3) + 1], i2 = corners[(t * 3) + 2];
            if (!_placed[i0] || !_placed[i1] || !_placed[i2])
            {
                continue;
            }

            Vector2 a = _target.Uvs[i0] * size, b = _target.Uvs[i1] * size, c = _target.Uvs[i2] * size;
            float area = Cross(b - a, c - a);
            if (MathF.Abs(area) < 1e-9f)
            {
                continue;
            }

            int x0 = (int)MathF.Floor(MathF.Min(a.X, MathF.Min(b.X, c.X))), x1 = (int)MathF.Ceiling(MathF.Max(a.X, MathF.Max(b.X, c.X)));
            int y0 = (int)MathF.Floor(MathF.Min(a.Y, MathF.Min(b.Y, c.Y))), y1 = (int)MathF.Ceiling(MathF.Max(a.Y, MathF.Max(b.Y, c.Y)));
            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    float w1 = Cross(p - a, c - a) / area;
                    float w2 = Cross(b - a, p - a) / area;
                    float w0 = 1f - w1 - w2;
                    const float edge = -1e-4f;
                    if (w0 < edge || w1 < edge || w2 < edge)
                    {
                        continue;
                    }

                    // Where UV islands overlap (mirrored halves) the first triangle keeps the texel.
                    int texel = (Wrap(y, size) * size) + Wrap(x, size);
                    if (taken[texel])
                    {
                        continue;
                    }

                    taken[texel] = true;
                    Vector3 normal = (w0 * _normals[i0]) + (w1 * _normals[i1]) + (w2 * _normals[i2]);
                    texels.Add((texel, (w0 * _positions[i0]) + (w1 * _positions[i1]) + (w2 * _positions[i2]),
                        normal == Vector3.Zero ? normal : Vector3.Normalize(normal)));
                }
            }
        }

        if (texels.Count == 0)
        {
            return map;
        }

        BuildGrid();

        // The searches only read the source, so they run in parallel, each worker with its own visited marks.
        Parallel.For(0, texels.Count, () => new Probe(_source.TriangleCount), (i, _, probe) =>
        {
            (int texel, Vector3 position, Vector3 normal) = texels[i];
            if (Nearest(position, normal, probe) is { } hit)
            {
                map.Material[texel] = _source.TriangleMaterials[hit.Triangle];
                map.Uv[texel] = hit.Uv;
            }

            return probe;
        }, _ => { });
        map.Spread(Bleed);
        return map;
    }

    /// <summary>One worker's marks of the triangles a search has tested.</summary>
    private sealed class Probe(int triangles)
    {
        public int[] Stamp { get; } = new int[triangles];

        public int Query { get; set; }
    }

    // The nearest source point, by distance stretched up to twice where the surfaces face apart (a sleeve's inside is
    // not the chest it touches).
    private (int Triangle, Vector2 Uv)? Nearest(Vector3 p, Vector3 normal, Probe probe)
    {
        int query = ++probe.Query;
        int[] stamp = probe.Stamp;
        (int X, int Y, int Z) home = Cell(p);
        float best = float.MaxValue;
        (int Triangle, Vector2 Uv)? found = null;
        for (int ring = 0; ring <= 64; ring++)
        {
            for (int x = home.X - ring; x <= home.X + ring; x++)
            {
                for (int y = home.Y - ring; y <= home.Y + ring; y++)
                {
                    // Only the ring's shell: inside it, just its two faces along z.
                    bool side = Math.Abs(x - home.X) == ring || Math.Abs(y - home.Y) == ring;
                    int zStep = side || ring == 0 ? 1 : 2 * ring;
                    for (int z = home.Z - ring; z <= home.Z + ring; z += zStep)
                    {
                        if (!_grid.TryGetValue((x, y, z), out List<int>? list))
                        {
                            continue;
                        }

                        foreach (int t in list)
                        {
                            if (stamp[t] == query)
                            {
                                continue;
                            }

                            stamp[t] = query;
                            int i0 = _source.Corners[t * 3], i1 = _source.Corners[(t * 3) + 1], i2 = _source.Corners[(t * 3) + 2];
                            Vector3 w = ClosestOnTriangle(p, _source.Positions[i0], _source.Positions[i1], _source.Positions[i2]);
                            Vector3 q = (w.X * _source.Positions[i0]) + (w.Y * _source.Positions[i1]) + (w.Z * _source.Positions[i2]);
                            Vector3 n = (w.X * _source.Normals[i0]) + (w.Y * _source.Normals[i1]) + (w.Z * _source.Normals[i2]);
                            float facing = normal == Vector3.Zero || n == Vector3.Zero ? 1f : Vector3.Dot(normal, Vector3.Normalize(n));
                            float score = Vector3.Distance(p, q) * (1.5f - (0.5f * facing));
                            if (score < best)
                            {
                                best = score;
                                found = (t, (w.X * _source.Uvs[i0]) + (w.Y * _source.Uvs[i1]) + (w.Z * _source.Uvs[i2]));
                            }
                        }
                    }
                }
            }

            // Every triangle beyond this ring is at least ring cells away, and the score is never below the distance.
            if (found is not null && best <= ring * _cell)
            {
                break;
            }
        }

        return found;
    }

    private (Vector3 Lo, Vector3 Hi) Bounds(int t)
    {
        Vector3 a = _source.Positions[_source.Corners[t * 3]];
        Vector3 b = _source.Positions[_source.Corners[(t * 3) + 1]];
        Vector3 c = _source.Positions[_source.Corners[(t * 3) + 2]];
        return (Vector3.Min(a, Vector3.Min(b, c)), Vector3.Max(a, Vector3.Max(b, c)));
    }

    private (int X, int Y, int Z) Cell(Vector3 p) =>
        ((int)MathF.Floor(p.X / _cell), (int)MathF.Floor(p.Y / _cell), (int)MathF.Floor(p.Z / _cell));

    private static float Cross(Vector2 a, Vector2 b) => (a.X * b.Y) - (a.Y * b.X);

    private static int Wrap(int i, int size) => ((i % size) + size) % size;

    /// <summary>The barycentric weights of the point of triangle abc nearest <paramref name="p"/> (Ericson, Real-Time Collision Detection 5.1.5).</summary>
    private static Vector3 ClosestOnTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 ab = b - a, ac = c - a, ap = p - a;
        float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
        if (d1 <= 0f && d2 <= 0f)
        {
            return new Vector3(1f, 0f, 0f);
        }

        Vector3 bp = p - b;
        float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
        if (d3 >= 0f && d4 <= d3)
        {
            return new Vector3(0f, 1f, 0f);
        }

        float vc = (d1 * d4) - (d3 * d2);
        if (vc <= 0f && d1 >= 0f && d3 <= 0f)
        {
            float v = d1 / (d1 - d3);
            return new Vector3(1f - v, v, 0f);
        }

        Vector3 cp = p - c;
        float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
        if (d6 >= 0f && d5 <= d6)
        {
            return new Vector3(0f, 0f, 1f);
        }

        float vb = (d5 * d2) - (d1 * d6);
        if (vb <= 0f && d2 >= 0f && d6 <= 0f)
        {
            float w = d2 / (d2 - d6);
            return new Vector3(1f - w, 0f, w);
        }

        float va = (d3 * d6) - (d5 * d4);
        if (va <= 0f && (d4 - d3) >= 0f && (d5 - d6) >= 0f)
        {
            float w = (d4 - d3) / ((d4 - d3) + (d5 - d6));
            return new Vector3(0f, 1f - w, w);
        }

        float denom = 1f / (va + vb + vc);
        float v2 = vb * denom, w2 = vc * denom;
        return new Vector3(1f - v2 - w2, v2, w2);
    }
}

/// <summary>For each texel of a square texture: the source material (-1 for none) and the UV on it.</summary>
internal sealed class TexelMap
{
    public TexelMap(int size)
    {
        Size = size;
        Material = new int[size * size];
        Uv = new Vector2[size * size];
        Array.Fill(Material, -1);
    }

    public int Size { get; }

    public int[] Material { get; }

    public Vector2[] Uv { get; }

    /// <summary>True when no texel has a source point (the meshes share no bones).</summary>
    public bool IsEmpty => Array.TrueForAll(Material, m => m < 0);

    /// <summary>How many texels take each source material.</summary>
    public int[] Counts(int materials)
    {
        var counts = new int[materials];
        foreach (int m in Material)
        {
            if (m >= 0 && m < counts.Length)
            {
                counts[m]++;
            }
        }

        return counts;
    }

    /// <summary>
    /// RGBA8 texels: <paramref name="sample"/> of each texel's source point, or <paramref name="empty"/>'s for a texel
    /// with none.
    /// </summary>
    public byte[] Bake(Func<int, Vector2, Rgba> sample, Func<int, Rgba> empty)
    {
        var data = new byte[Size * Size * 4];
        for (int i = 0; i < Material.Length; i++)
        {
            Rgba c = Material[i] >= 0 ? sample(Material[i], Uv[i]) : empty(i);
            data[i * 4] = c.R;
            data[(i * 4) + 1] = c.G;
            data[(i * 4) + 2] = c.B;
            data[(i * 4) + 3] = c.A;
        }

        return data;
    }

    /// <summary>Gives empty texels a set neighbour's source point, <paramref name="steps"/> texels out.</summary>
    internal void Spread(int steps)
    {
        for (int step = 0; step < steps; step++)
        {
            int[] before = (int[])Material.Clone();
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    int i = (y * Size) + x;
                    if (before[i] >= 0)
                    {
                        continue;
                    }

                    int from = x > 0 && before[i - 1] >= 0 ? i - 1
                        : x + 1 < Size && before[i + 1] >= 0 ? i + 1
                        : y > 0 && before[i - Size] >= 0 ? i - Size
                        : y + 1 < Size && before[i + Size] >= 0 ? i + Size
                        : -1;
                    if (from >= 0)
                    {
                        Material[i] = before[from];
                        Uv[i] = Uv[from];
                    }
                }
            }
        }
    }
}

/// <summary>One RGBA8 texel.</summary>
internal readonly record struct Rgba(byte R, byte G, byte B, byte A)
{
    public static Rgba Grey(float value)
    {
        byte v = ToByte(value);
        return new Rgba(v, v, v, 255);
    }

    public static byte ToByte(float value) => (byte)Math.Clamp((int)MathF.Round(value * 255f), 0, 255);
}
