using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using OpenScct.Content;
using OpenScct.Content.Collision;
using OpenScct.Core;
using OpenScct.Provenance;

namespace OpenScct.Physics.Collision;

/// <summary>
/// The read-only part of a map's collision, built once from <see cref="CollisionData"/> and shared by every
/// <see cref="CollisionWorld"/> of that map (server, client, render zones, bot navigation scratch world): the static
/// BVH, the BSP, zones, materials, cylinders and each collider's starting state. Nothing writes it after the build,
/// so worlds on different threads can query it at once. Each world copies the collider states and the moving
/// colliders' primitive sets, which it changes.
/// </summary>
internal sealed class CollisionModel
{
    private const int TflxStride = 8;
    private const int TflxOcclusionOffset = 6;

    // SVMap 1.1 CSRC rows (spec/svmap/1.1/collision.md): u32 source, u32 asset, u8 set, u8, u16 variants, u16 traceUse, u16.
    private const int CsrcStride = 16;
    private const byte CsrcSetPerPoly = 2;
    private const byte CsrcSetSimple = 3;

    // Models live as long as their CollisionData (an SvMap) or a world that uses them.
    private static readonly object s_gate = new();
    private static readonly ConditionalWeakTable<CollisionData, CollisionModel> s_byData = new();
    private static readonly Dictionary<string, WeakReference<CollisionModel>> s_byContent = new(StringComparer.Ordinal);

    /// <summary>Each collider's state before any world binds or moves it. Worlds copy these; never written after the build.</summary>
    public readonly ColliderState[] Colliders;
    public readonly int[] DynamicColliders;
    public readonly ETraceFlags DynamicTraceBits;
    public readonly PrimitiveSet Static;
    public readonly CollisionMaterial[] Materials;
    public readonly uint[] MaterialPolyFlags;
    public readonly byte[] MaterialOcclusion;
    public readonly CollisionZone[] Zones;
    public readonly string?[] ZoneActorIds;
    public readonly CollisionBspNode[] Bsp;
    public readonly int BspRoot = -1;
    public readonly int BspCollider = -1;
    public readonly CollisionTriangle[] BspTris = [];
    public readonly FVector[] BspVerts = [];
    public readonly CollisionCylinder[] Cylinders;
    public readonly string?[] CylinderActorIds;
    public readonly FVector WorldMin;
    public readonly FVector WorldMax;
    public readonly float KillZ;
    public readonly int DegenerateTriangles;
    public readonly int TriangleCount;
    public readonly int HullCount;

    /// <summary>
    /// The shared model of <paramref name="data"/>, built on first use. <paramref name="contentKey"/>
    /// (<see cref="ContentKey"/> of the collision.bin bytes) also finds a model built from another read of the same file.
    /// </summary>
    public static CollisionModel Of(CollisionData data, string? contentKey = null)
    {
        ArgumentNullException.ThrowIfNull(data);

        // One lock: a second world of the same map waits for the first build instead of repeating it.
        lock (s_gate)
        {
            if (s_byData.TryGetValue(data, out CollisionModel? model))
            {
                Remember(contentKey, model);
                return model;
            }

            if (contentKey is null || !TryFind(contentKey, out model))
            {
                model = new CollisionModel(data);
                Remember(contentKey, model);
            }

            s_byData.AddOrUpdate(data, model);
            return model;
        }
    }

    /// <summary>The shared model of a collision.bin file's bytes; they are parsed only when no model of the same bytes exists.</summary>
    public static CollisionModel OfFile(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        string key = ContentKey(SvMapHash.Sha256Hex(bytes), bytes.LongLength);
        lock (s_gate)
        {
            if (!TryFind(key, out CollisionModel? model))
            {
                model = new CollisionModel(CollisionReader.Read(bytes));
                Remember(key, model);
            }

            return model;
        }
    }

    /// <summary>The cache key of collision.bin bytes: their SHA-256 (as in the SVMap file digests) and length.</summary>
    public static string ContentKey(string sha256, long size) => sha256 + "/" + size.ToString(CultureInfo.InvariantCulture);

    /// <summary>The content key of an SVMap's collision.bin from its sim file digests, or null when it is not listed.</summary>
    public static string? ContentKeyOf(SvMap map)
    {
        foreach (SvMapFileDigest file in map.SimFiles)
        {
            if (file.Path == CollisionReader.RelativePath)
            {
                return ContentKey(file.Sha256, file.Size);
            }
        }

        return null;
    }

    private static bool TryFind(string key, [NotNullWhen(true)] out CollisionModel? model)
    {
        model = null;
        return s_byContent.TryGetValue(key, out WeakReference<CollisionModel>? weak) && weak.TryGetTarget(out model);
    }

    private static void Remember(string? key, CollisionModel model)
    {
        if (key is null)
        {
            return;
        }

        // Drop entries of maps whose worlds are gone.
        List<string>? dead = null;
        foreach (KeyValuePair<string, WeakReference<CollisionModel>> entry in s_byContent)
        {
            if (!entry.Value.TryGetTarget(out _))
            {
                (dead ??= new List<string>()).Add(entry.Key);
            }
        }

        if (dead is not null)
        {
            foreach (string k in dead)
            {
                s_byContent.Remove(k);
            }
        }

        s_byContent[key] = new WeakReference<CollisionModel>(model);
    }

    private CollisionModel(CollisionData data)
    {
        int nc = data.Colliders.Length;
        Colliders = new ColliderState[nc];
        var staticBuilder = new PrimitiveSetBuilder(dynamic: false);
        var dynamic = new List<int>();
        var localPlanes = new List<HullPlane>();
        bool hasWorld = false;
        FVector worldMin = default;
        FVector worldMax = default;
        int degenerate = 0;
        int triangles = 0;
        int hulls = 0;

        for (int ci = 0; ci < nc; ci++)
        {
            Collider c = data.Colliders[ci];
            var state = new ColliderState
            {
                Index = ci,
                Kind = c.Kind,
                Flags = c.Flags,
                TraceBits = CollisionWorld.KindBits(c.Kind, c.Flags),
                ActorId = data.GetString(c.Actor),
                Enabled = (c.Flags & ColliderFlags.StartsDisabled) == 0,
                IsDynamic = CollisionWorld.IsDynamicKind(c.Kind),
                Location = c.Location,
                Rotation = c.Rotation,
                Scale = c.Scale,
                PrePivot = c.PrePivot,
            };

            var xf = new ColliderTransform(c.Location, c.Rotation, c.Scale, c.PrePivot);
            PrimitiveSetBuilder target = state.IsDynamic ? new PrimitiveSetBuilder(dynamic: true) : staticBuilder;
            FVector cmin = default;
            FVector cmax = default;
            bool has = false;
            if (xf.Valid)
            {
                if (c.HullCount > 0)
                {
                    for (int h = (int)c.HullStart; h < (int)(c.HullStart + c.HullCount); h++)
                    {
                        CollisionHull hull = data.Hulls[h];
                        localPlanes.Clear();
                        foreach (CollisionPlane p in data.PlanesOf(hull))
                        {
                            localPlanes.Add(new HullPlane(p.Normal, p.W));
                        }

                        target.AddHull(ci, hull.Material, h, CollectionsMarshal.AsSpan(localPlanes), data.CornersOf(hull), hull.Min,
                            hull.Max, xf, ref cmin, ref cmax, ref has);
                        hulls++;
                    }
                }
                else
                {
                    // Colliders with BSP nodes collide by the triangles of their polygons (BSPN is provisional).
                    for (int t = (int)c.TriStart; t < (int)(c.TriStart + c.TriCount); t++)
                    {
                        CollisionTriangle tri = data.Triangles[t];
                        target.AddTriangle(ci, tri.Material, t, data.Vertices[(int)tri.I0], data.Vertices[(int)tri.I1],
                            data.Vertices[(int)tri.I2], xf, ref cmin, ref cmax, ref has);
                        triangles++;
                    }
                }
            }
            else
            {
                state.Enabled = false;
            }

            if (state.IsDynamic)
            {
                state.Set = target.Build();
                degenerate += target.DegenerateTriangles;
                dynamic.Add(ci);
                DynamicTraceBits |= state.TraceBits;
            }

            state.Min = has ? cmin : c.AabbMin;
            state.Max = has ? cmax : c.AabbMax;
            if (has)
            {
                worldMin = hasWorld ? CollisionMath.MinV(worldMin, cmin) : cmin;
                worldMax = hasWorld ? CollisionMath.MaxV(worldMax, cmax) : cmax;
                hasWorld = true;
            }

            if (BspRoot < 0 && c.Kind == ColliderKind.WorldBsp && c.BspCount > 0)
            {
                BspRoot = (int)c.BspStart;

                // The level model is untransformed; its tree answers zero-extent traces (UModel::LineCheck).
                if (!state.IsDynamic && c.Location.IsZero() && c.Rotation == default && c.PrePivot.IsZero() &&
                    c.Scale == new FVector(1f, 1f, 1f))
                {
                    BspCollider = ci;
                }
            }

            Colliders[ci] = state;
        }

        PairMeshSets(data);
        Static = staticBuilder.Build();
        degenerate += staticBuilder.DegenerateTriangles;
        DynamicColliders = dynamic.ToArray();
        Materials = data.Materials.AsSpan().ToArray();
        MaterialPolyFlags = ReadPolyFlags(data, Materials.Length);
        MaterialOcclusion = ReadOcclusionTypes(data, Materials.Length);
        Bsp = data.BspNodes.AsSpan().ToArray();
        if (BspCollider >= 0)
        {
            BspTris = data.Triangles.AsSpan().ToArray();
            BspVerts = data.Vertices.AsSpan().ToArray();
        }

        Zones = data.Zones.AsSpan().ToArray();
        ZoneActorIds = new string?[Zones.Length];
        for (int i = 0; i < Zones.Length; i++)
        {
            ZoneActorIds[i] = data.GetString(Zones[i].Actor);
        }

        Cylinders = data.Cylinders.AsSpan().ToArray();
        CylinderActorIds = new string?[Cylinders.Length];
        for (int i = 0; i < Cylinders.Length; i++)
        {
            CylinderActorIds[i] = data.GetString(Cylinders[i].Actor);
        }

        WorldMin = worldMin;
        WorldMax = worldMax;
        KillZ = hasWorld ? worldMin.Z - K.KillZBelowWorld : -K.KillZBelowWorld;
        DegenerateTriangles = degenerate;
        TriangleCount = triangles - degenerate;
        HullCount = hulls;
    }

    /// <summary>
    /// Pairs each static mesh's per-poly collider with its simple-collision collider. Traces use the simple set when a
    /// mesh has one and the per-poly set only with TRACE_ShadowCast (UStaticMesh::LineCheck tree selection). The
    /// converter writes the two sets of one mesh one after the other (per-poly then simple) with the same owner and
    /// transform. Plain meshes tell them apart by kind (meshPoly, meshHull); movers, glass and gameplay objects use one
    /// kind for both, so their sets come from the SVMap 1.1 CSRC chunk (set 2 per-poly, 3 simple, same source, asset
    /// and variants). Maps without CSRC keep both sets of those actors colliding.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "UStaticMesh::LineCheck tree selector editor 0x111b47a0 (spec/native/collision-primitives.md)")]
    [Provenance(ProvenanceKind.Decoded, "Terminal mode_neutralisation: per-poly reaches X 33.2, simple hulls 28.2; the 64 uu use spot only fits a 35 uu spy against the simple set (spec/objectives/terminal-hack.md)")]
    private void PairMeshSets(CollisionData data)
    {
        ReadOnlySpan<byte> sources = default;
        foreach (CollisionUnknownChunk chunk in data.UnknownChunks)
        {
            if (chunk.FourCC == "CSRC" && chunk.Count == (uint)Colliders.Length && chunk.Payload.Length >= Colliders.Length * CsrcStride)
            {
                sources = chunk.Payload;
                break;
            }
        }

        if (!sources.IsEmpty)
        {
            for (int ci = 0; ci < Colliders.Length; ci++)
            {
                Colliders[ci].Variants = BinaryPrimitives.ReadUInt16LittleEndian(sources.Slice(ci * CsrcStride + 10, 2));
            }
        }

        for (int ci = 0; ci + 1 < Colliders.Length; ci++)
        {
            Collider a = data.Colliders[ci];
            Collider b = data.Colliders[ci + 1];
            if (a.Actor != b.Actor || a.Location != b.Location || a.Rotation != b.Rotation || a.Scale != b.Scale ||
                a.PrePivot != b.PrePivot)
            {
                continue;
            }

            bool pair = a.Kind == ColliderKind.MeshPoly && b.Kind == ColliderKind.MeshHull;
            if (!pair && !sources.IsEmpty && a.Kind == b.Kind)
            {
                ReadOnlySpan<byte> sa = sources.Slice(ci * CsrcStride, CsrcStride);
                ReadOnlySpan<byte> sb = sources.Slice((ci + 1) * CsrcStride, CsrcStride);
                pair = sa[8] == CsrcSetPerPoly && sb[8] == CsrcSetSimple && sa[..8].SequenceEqual(sb[..8]) &&
                       sa.Slice(10, 2).SequenceEqual(sb.Slice(10, 2));
            }

            if (!pair)
            {
                continue;
            }

            Colliders[ci].SimpleTwin = ci + 1;
            Colliders[ci + 1].PerPolyTwin = ci;
        }
    }

    /// <summary>The poly flags of the SVMap 1.1 TFLX rows (u32 polyFlags, u16 materialBits, u8 occlusion, u8), one per material.</summary>
    private static uint[] ReadPolyFlags(CollisionData data, int count)
    {
        var flags = new uint[count];
        foreach (CollisionUnknownChunk chunk in data.UnknownChunks)
        {
            if (chunk.FourCC == "TFLX" && chunk.Count == (uint)count && chunk.Payload.Length >= count * TflxStride)
            {
                ReadOnlySpan<byte> rows = chunk.Payload;
                for (int i = 0; i < count; i++)
                {
                    flags[i] = BinaryPrimitives.ReadUInt32LittleEndian(rows.Slice(i * TflxStride, 4));
                }

                break;
            }
        }

        return flags;
    }

    /// <summary>The occlusion type byte of the TFLX rows, one per material (the sound system's occlusion, spec/native/audio.md rule 18).</summary>
    private static byte[] ReadOcclusionTypes(CollisionData data, int count)
    {
        var types = new byte[count];
        foreach (CollisionUnknownChunk chunk in data.UnknownChunks)
        {
            if (chunk.FourCC == "TFLX" && chunk.Count == (uint)count && chunk.Payload.Length >= count * TflxStride)
            {
                for (int i = 0; i < count; i++)
                {
                    types[i] = chunk.Payload[(i * TflxStride) + TflxOcclusionOffset];
                }

                break;
            }
        }

        return types;
    }
}
