using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Godot;
using RD = Godot.RenderingDevice;

namespace OpenScctGame.Render.Rd;

/// <summary>
/// SVM_RD_SHADOWS=1: the moving casters of a spot shadow light drawn by our own code on the RenderingDevice, instead of
/// a Godot SubViewport per light per frame. A light whose static world is cached (SpotShadowLight's static tile) gets
/// its tile of the shadow atlas as a copy of the cached tile with the other casters in its frustum drawn over it: each
/// writes its distance along the light's axis, and the nearest wins (minimum blending), the same value the viewport
/// pass and its depth copy produce. Skinned meshes are skinned in the vertex shader from the skeleton's pose.
/// <para>
/// What it can draw: MeshInstance3D nodes with an ArrayMesh of triangles whose surfaces are never rewritten (see
/// <see cref="MarkMutable"/>), up to four bone weights a vertex, with Faithful template materials that are solid, cut
/// out by their diffuse texture's alpha, or cast nothing (blended, cloaked). Anything else in a light's frustum leaves
/// that light to its viewport pass for the frame.
/// </para>
/// </summary>
internal static class RdShadowPass
{
    public static bool Enabled { get; } = System.Environment.GetEnvironmentVariable("SVM_RD_SHADOWS") == "1";

    /// <summary>
    /// SVM_RD_SHADOWS=verify: the viewport passes keep writing the atlas, and now and then this pass draws the same
    /// tiles of the same frame into a scratch texture; both are read back and compared, one "SVM_RD_SHADOW verify" line
    /// a tile. A check of this pass against Godot's own drawing, not a way to play.
    /// </summary>
    public static bool Verify { get; } = System.Environment.GetEnvironmentVariable("SVM_RD_SHADOWS") == "verify";

    private static readonly List<(Vector2I Origin, Vector2I Size, string Label, int Items)> s_verify = [];
    private static readonly List<string> s_labels = [];

    /// <summary>As <see cref="Commit"/>, for a tile the viewport pass also draws this frame (<see cref="Verify"/>).</summary>
    public static void CommitVerify(object tile, string label)
    {
        List<Rect2I>? whole = null;
        Commit(tile, ref whole);
        s_labels.Add(label);
    }

    /// <summary>Set when the pass failed on the render thread: every light goes back to its viewport pass.</summary>
    public static bool Failed => s_failed;

    private static volatile bool s_failed;

    /// <summary>How a surface casts: nothing, its whole triangles, or cut out by a texture's alpha at 0.5.</summary>
    private enum Cast : byte
    {
        Unsupported,
        Nothing,
        Solid,
        Masked,
    }

    private sealed record MaterialCast(Cast Kind, bool TwoSided, Texture2D? Texture);

    private sealed class SurfaceData
    {
        public required byte[] Vertices;
        public required int VertexCount;
        public required uint[] Indices;
        public required bool Skinned;
        // Render thread.
        public Rid VertexBuffer, VertexArray, IndexBuffer, IndexArray;
    }

    private sealed class MeshData
    {
        public SurfaceData?[] Surfaces = [];
        public bool Supported;
        public bool Uploaded;
        /// <summary>The mesh's own box (a rewritten mesh: of its current shape).</summary>
        public Aabb Bounds;
        /// <summary>A rewritten mesh (<see cref="SetDynamic"/>): the version its vertex bytes hold, and whether the GPU copy is behind.</summary>
        public int Version = -1;
        public bool Stale;

        ~MeshData()
        {
            if (!Uploaded) return;
            lock (s_dead)
            {
                foreach (SurfaceData? s in Surfaces)
                {
                    if (s is null) continue;
                    if (s.VertexBuffer.IsValid) s_dead.Add(s.VertexBuffer);
                    if (s.IndexBuffer.IsValid) s_dead.Add(s.IndexBuffer);
                }
            }
        }
    }

    /// <summary>A node's skinning: its skeleton and, per bind of its skin, the bone and the bind pose.</summary>
    private sealed class SkinInfo
    {
        public required Skeleton3D Skeleton;
        public required int[] Bones;
        public required Transform3D[] BindPoses;
        public ulong Frame = ulong.MaxValue;
        public int PaletteBase;
        /// <summary>The posed bones' origins this frame (skeleton space), as a box.</summary>
        public Aabb Bones3D;
        /// <summary>The furthest a vertex lies from the bind origin of a bone that moves it (below 0: not measured yet).</summary>
        public float Reach = -1f;
    }

    private readonly record struct Item(MeshData Mesh, int Surface, Transform3D Model, int PaletteBase, bool TwoSided, Rid Texture);

    /// <summary>One light's tile to write this frame.</summary>
    private sealed class Job
    {
        // The tile's pixels the casters can cover, a box each, or everything when a caster's box cannot be placed.
        public readonly List<Rect2I> Covered = [];
        public bool CoversAll;
        // The pixels copied from the cached static tile: what the casters cover now and covered at the last write.
        public readonly List<Rect2I> Copy = [];
        public Vector2I Origin, Size;
        public Projection Clip;
        public Transform3D View;
        public readonly List<Item> Items = [];
    }

    private const int VertexStride = 44;
    private static readonly ConditionalWeakTable<Mesh, MeshData> s_meshes = new();
    private static readonly ConditionalWeakTable<Mesh, object> s_mutable = new();
    private static readonly ConditionalWeakTable<Material, MaterialCast> s_materials = new();
    private static readonly ConditionalWeakTable<Shader, MaterialCast> s_shaders = new();
    private static readonly ConditionalWeakTable<MeshInstance3D, SkinInfo> s_skins = new();
    private static readonly MaterialCast s_solid = new(Cast.Solid, false, null);
    private static readonly MaterialCast s_unsupported = new(Cast.Unsupported, false, null);
    private static readonly object s_mark = new();
    private static readonly List<Rid> s_dead = [];
    private static readonly StringName s_diffuse = "tex_diffuse";
    private static readonly StringName s_opacity = "svm_fragment_opacity";

    // Main thread, one frame: the jobs and the bone palette (three rows a bone) they index.
    private static readonly List<Job> s_jobs = [];
    private static readonly Stack<Job> s_freeJobs = new();
    private static float[] s_palette = new float[12 * 256];
    private static int s_paletteRows;
    private static ulong s_frame;

    /// <summary>
    /// Marks a mesh whose surfaces are rewritten after it is built (a soft body, steps rebuilt each frame): this pass
    /// keeps a copy of a mesh's geometry from its first use, so it leaves such a mesh to the viewport pass.
    /// </summary>
    public static void MarkMutable(Mesh mesh)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        s_mutable.AddOrUpdate(mesh, s_mark);
    }

    /// <summary>The current shape of a mesh that is rewritten in place, handed over by whoever rewrites it.</summary>
    private sealed class DynamicSource
    {
        public Vector3[] Positions = [];
        public Vector2[] Uvs = [];
        public int[] Indices = [];
        public int Version;
    }

    private static readonly ConditionalWeakTable<Mesh, DynamicSource> s_dynamic = new();

    /// <summary>
    /// The current shape of a mesh marked <see cref="MarkMutable"/> (one surface of triangles with a fixed vertex count
    /// and index list): called each time it is rewritten, with the arrays it was rewritten from. The pass then draws it
    /// from these instead of leaving it to the viewport pass.
    /// </summary>
    public static void SetDynamic(Mesh mesh, Vector3[] positions, Vector2[] uvs, int[] indices)
    {
        DynamicSource source = s_dynamic.GetValue(mesh, static _ => new DynamicSource());
        source.Positions = positions;
        source.Uvs = uvs;
        source.Indices = indices;
        source.Version++;
    }

    private static MeshData? Dynamic(Mesh mesh)
    {
        if (!s_dynamic.TryGetValue(mesh, out DynamicSource? source) || source.Positions.Length == 0 || source.Indices.Length < 3) return null;
        MeshData data = s_meshes.GetValue(mesh, static _ => new MeshData { Supported = true, Surfaces = new SurfaceData?[1] });
        SurfaceData? surface = data.Surfaces[0];
        if (surface is not null && (surface.VertexCount != source.Positions.Length || surface.Indices.Length != source.Indices.Length)) return null;
        if (surface is null)
        {
            var indices = new uint[source.Indices.Length];
            for (int i = 0; i < indices.Length; i++) indices[i] = (uint)source.Indices[i];
            surface = new SurfaceData { Vertices = new byte[source.Positions.Length * VertexStride], VertexCount = source.Positions.Length, Indices = indices, Skinned = false };
            data.Surfaces[0] = surface;
        }

        if (data.Version != source.Version)
        {
            data.Version = source.Version;
            data.Stale = true;
            data.Bounds = new Aabb(source.Positions[0], Vector3.Zero);
            for (int i = 0; i < source.Positions.Length; i++)
            {
                Span<byte> v = surface.Vertices.AsSpan(i * VertexStride, VertexStride);
                data.Bounds = data.Bounds.Expand(source.Positions[i]);
                MemoryMarshal.Write(v, in source.Positions[i]);
                if (i < source.Uvs.Length) MemoryMarshal.Write(v[12..], in source.Uvs[i]);
            }
        }

        return data;
    }

    /// <summary>Frees every RenderingDevice object of the pass (the last spot shadow light is gone).</summary>
    public static void Release() => RenderingServer.CallOnRenderThread(Callable.From(Gpu.Free));

    /// <summary>Starts a light's tile: the casters are added with <see cref="TryAdd"/>, then <see cref="Commit"/> or <see cref="Discard"/>.</summary>
    public static object Begin(Vector2I origin, Vector2I size, Camera3D camera)
    {
        ulong frame = Engine.GetProcessFrames();
        if (frame != s_frame)
        {
            s_frame = frame;
            s_paletteRows = 0;
        }

        Job job = s_freeJobs.Count > 0 ? s_freeJobs.Pop() : new Job();
        job.Items.Clear();
        job.Covered.Clear();
        job.Copy.Clear();
        job.CoversAll = false;
        job.Origin = origin;
        job.Size = size;
        Transform3D eye = camera.GlobalTransform;
        job.View = eye.AffineInverse();
        // The camera's own projection, corrected as Godot corrects it for its buffers (y flipped, depth 1 near to 0 far).
        Projection p = camera.GetCameraProjection() * new Projection(job.View);
        static Vector4 Fix(Vector4 c) => new(c.X, -c.Y, 0.5f * (c.W - c.Z), c.W);
        job.Clip = new Projection(Fix(p.X), Fix(p.Y), Fix(p.Z), Fix(p.W));
        return job;
    }

    /// <summary>
    /// Adds a caster to the tile. False when the pass cannot draw it as Godot would: the light then keeps its viewport
    /// pass this frame. <paramref name="moves"/> is set when its shape can change without its node moving (a skeleton).
    /// </summary>
    public static bool TryAdd(object tile, MeshInstance3D node, ref bool moves)
    {
        var job = (Job)tile;
        if (node.Mesh is not ArrayMesh mesh) return false;
        MeshData? data;
        if (s_mutable.TryGetValue(mesh, out _))
        {
            // A rewritten mesh is drawn from the shape its owner last handed over, every frame.
            data = Dynamic(mesh);
            moves = true;
        }
        else
        {
            data = s_meshes.GetValue(mesh, Read);
        }

        if (data is not { Supported: true } || data.Surfaces.Length != mesh.GetSurfaceCount()) return false;
        int paletteBase = -1;
        bool skinned = false;
        foreach (SurfaceData? s in data.Surfaces) skinned |= s is { Skinned: true };
        if (skinned)
        {
            SkinInfo? skin = SkinOf(node);
            // Bone weights without a skeleton: Godot draws the mesh unskinned.
            if (skin is not null)
            {
                if (skin.Frame != s_frame && !Pose(skin)) return false;
                paletteBase = skin.PaletteBase;
                moves = true;
            }
        }

        Transform3D model = node.GlobalTransform;
        // Where the caster can fall in the tile: its mesh's box, or for a skinned one its posed bones' box widened by
        // the furthest a vertex lies from a bone that moves it (a skinned vertex stays within that of its bones).
        Aabb box = data.Bounds;
        if (paletteBase >= 0 && s_skins.TryGetValue(node, out SkinInfo? posed))
        {
            if (posed.Reach < 0f) posed.Reach = Reach(data, posed);
            box = posed.Bones3D.Grow((posed.Reach * 1.1f) + 0.1f);
        }

        int before = job.Items.Count;
        for (int s = 0; s < data.Surfaces.Length; s++)
        {
            MaterialCast cast = CastOf(node.GetActiveMaterial(s));
            switch (cast.Kind)
            {
                case Cast.Unsupported:
                    return false;
                case Cast.Nothing:
                    continue;
            }

            if (data.Surfaces[s] is not { } surface) return false;
            Rid texture = default;
            if (cast.Kind == Cast.Masked)
            {
                if (cast.Texture is null || !GodotObject.IsInstanceValid(cast.Texture)) return false;
                texture = cast.Texture.GetRid();
            }

            job.Items.Add(new Item(data, s, model, surface.Skinned ? paletteBase : -1, cast.TwoSided, texture));
        }

        if (job.Items.Count > before) Cover(job, model * box);
        return true;
    }

    /// <summary>Widens the job's covered pixels by a world box's projection (all of the tile when it reaches the light's plane).</summary>
    private static void Cover(Job job, Aabb box)
    {
        if (job.CoversAll) return;
        Vector3 low = box.Position, high = box.End;
        float minX = float.PositiveInfinity, minY = float.PositiveInfinity, maxX = float.NegativeInfinity, maxY = float.NegativeInfinity;
        // The box's corners in clip space; only the part in front of the near plane (depth z up to w) is drawn, so
        // the corners behind it are replaced by where the box's edges cross it.
        Span<Vector4> clip = stackalloc Vector4[8];
        for (int corner = 0; corner < 8; corner++)
        {
            var p = new Vector3((corner & 1) != 0 ? high.X : low.X, (corner & 2) != 0 ? high.Y : low.Y, (corner & 4) != 0 ? high.Z : low.Z);
            clip[corner] = job.Clip * new Vector4(p.X, p.Y, p.Z, 1f);
            if (!float.IsFinite(clip[corner].X + clip[corner].Y + clip[corner].Z + clip[corner].W))
            {
                job.CoversAll = true;
                return;
            }
        }

        bool any = false;
        void Take(Vector4 c, Job job)
        {
            if (!(c.W > 1e-6f))
            {
                job.CoversAll = true;
                return;
            }

            float x = ((c.X / c.W * 0.5f) + 0.5f) * job.Size.X, y = ((c.Y / c.W * 0.5f) + 0.5f) * job.Size.Y;
            minX = MathF.Min(minX, x);
            maxX = MathF.Max(maxX, x);
            minY = MathF.Min(minY, y);
            maxY = MathF.Max(maxY, y);
            any = true;
        }

        for (int corner = 0; corner < 8; corner++)
        {
            Vector4 a = clip[corner];
            float inside = a.W - a.Z;
            if (inside >= 0f) Take(a, job);
            for (int axis = 1; axis < 8; axis <<= 1)
            {
                // Each edge once, from its lower corner.
                if ((corner & axis) != 0) continue;
                Vector4 b = clip[corner | axis];
                float other = b.W - b.Z;
                if ((inside >= 0f) == (other >= 0f)) continue;
                Take(a.Lerp(b, inside / (inside - other)), job);
            }
        }

        if (job.CoversAll || !any) return;
        int x0 = Math.Clamp((int)MathF.Floor(minX) - 2, 0, job.Size.X), x1 = Math.Clamp((int)MathF.Ceiling(maxX) + 2, 0, job.Size.X);
        int y0 = Math.Clamp((int)MathF.Floor(minY) - 2, 0, job.Size.Y), y1 = Math.Clamp((int)MathF.Ceiling(maxY) + 2, 0, job.Size.Y);
        if (x1 <= x0 || y1 <= y0) return;
        var rect = new Rect2I(x0, y0, x1 - x0, y1 - y0);
        // Boxes that overlap become one, so no pixel is copied twice; many boxes are not worth their copies.
        for (int i = job.Covered.Count - 1; i >= 0; i--)
        {
            if (!job.Covered[i].Intersects(rect)) continue;
            rect = rect.Merge(job.Covered[i]);
            job.Covered.RemoveAt(i);
            i = job.Covered.Count;
        }

        job.Covered.Add(rect);
        if (job.Covered.Count > MaxBoxes) job.CoversAll = true;
    }

    private const int MaxBoxes = 24;

    /// <summary>The furthest a vertex of the mesh lies from the bind origin of a bone that moves it.</summary>
    private static float Reach(MeshData data, SkinInfo skin)
    {
        var origins = new Vector3[skin.BindPoses.Length];
        for (int i = 0; i < origins.Length; i++) origins[i] = skin.BindPoses[i].AffineInverse().Origin;
        float reach = 0f;
        foreach (SurfaceData? s in data.Surfaces)
        {
            if (s is not { Skinned: true }) continue;
            for (int v = 0; v < s.VertexCount; v++)
            {
                ReadOnlySpan<byte> vertex = s.Vertices.AsSpan(v * VertexStride, VertexStride);
                Vector3 position = MemoryMarshal.Read<Vector3>(vertex);
                ReadOnlySpan<ushort> bones = MemoryMarshal.Cast<byte, ushort>(vertex.Slice(20, 8));
                ReadOnlySpan<float> weights = MemoryMarshal.Cast<byte, float>(vertex.Slice(28, 16));
                for (int k = 0; k < 4; k++)
                {
                    if (weights[k] > 0f && bones[k] < origins.Length) reach = MathF.Max(reach, position.DistanceTo(origins[bones[k]]));
                }
            }
        }

        return reach;
    }

    /// <summary>
    /// The tile is written this frame (before anything draws). <paramref name="written"/> is what the casters covered
    /// at the tile's last write by this pass (null: the whole tile, it was written by something else), and takes what
    /// they cover now: only those pixels are copied from the cached static tile.
    /// </summary>
    public static void Commit(object tile, ref List<Rect2I>? written)
    {
        var job = (Job)tile;
        var all = new Rect2I(Vector2I.Zero, job.Size);
        if (job.CoversAll)
        {
            job.Covered.Clear();
            job.Covered.Add(all);
        }

        if (written is null || written.Count > MaxBoxes || (written.Count == 1 && written[0] == all) || job.CoversAll)
        {
            job.Copy.Add(all);
        }
        else
        {
            // The boxes of now and of the last write, overlapping ones as one.
            job.Copy.AddRange(job.Covered);
            foreach (Rect2I last in written)
            {
                Rect2I rect = last;
                for (int i = job.Copy.Count - 1; i >= 0; i--)
                {
                    if (!job.Copy[i].Intersects(rect)) continue;
                    rect = rect.Merge(job.Copy[i]);
                    job.Copy.RemoveAt(i);
                    i = job.Copy.Count;
                }

                job.Copy.Add(rect);
            }
        }

        written ??= [];
        written.Clear();
        written.AddRange(job.Covered);
        s_jobs.Add(job);
    }

    public static void Discard(object tile) => s_freeJobs.Push((Job)tile);

    /// <summary>Writes the committed tiles (main thread, at pre-draw: the render thread runs it at once).</summary>
    public static void Flush(Rid atlas, Rid staticAtlas)
    {
        if (Verify && s_verify.Count > 0)
        {
            // Last frame's tiles: the viewport passes wrote the atlas while that frame drew.
            var tiles = new List<(Vector2I, Vector2I, string, int)>(s_verify);
            s_verify.Clear();
            RenderingServer.CallOnRenderThread(Callable.From(() => Gpu.Compare(tiles, atlas)));
        }

        if (s_jobs.Count == 0) return;
        var jobs = new List<Job>(s_jobs);
        s_jobs.Clear();
        if (Verify)
        {
            for (int i = 0; i < jobs.Count; i++) s_verify.Add((jobs[i].Origin, jobs[i].Size, i < s_labels.Count ? s_labels[i] : "?", jobs[i].Items.Count));
            s_labels.Clear();
        }

        float[] palette = s_palette;
        int rows = s_paletteRows;
        RenderingServer.CallOnRenderThread(Callable.From(() =>
        {
            try
            {
                if (!s_failed) Gpu.Draw(jobs, palette, rows, Verify ? Gpu.Scratch(atlas) : atlas, staticAtlas);
            }
            catch (Exception error)
            {
                s_failed = true;
                GD.PushError("RD shadow pass failed: " + error);
            }
        }));
        // Under the default thread model the callable has run: the jobs are free again.
        foreach (Job job in jobs) s_freeJobs.Push(job);
    }

    private static SkinInfo? SkinOf(MeshInstance3D node)
    {
        if (s_skins.TryGetValue(node, out SkinInfo? info)) return info.Bones.Length == 0 ? null : info;
        Skeleton3D? skeleton = node.Skeleton.IsEmpty ? null : node.GetNodeOrNull<Skeleton3D>(node.Skeleton);
        if (skeleton is null)
        {
            // Remembered as "no skeleton" (the node keeps its path for its lifetime here).
            s_skins.Add(node, new SkinInfo { Skeleton = null!, Bones = [], BindPoses = [] });
            return null;
        }

        int[] bones;
        Transform3D[] poses;
        if (node.Skin is { } skin)
        {
            int count = skin.GetBindCount();
            bones = new int[count];
            poses = new Transform3D[count];
            for (int i = 0; i < count; i++)
            {
                StringName name = skin.GetBindName(i);
                bones[i] = name is { IsEmpty: false } ? skeleton.FindBone(name) : skin.GetBindBone(i);
                poses[i] = skin.GetBindPose(i);
            }
        }
        else
        {
            // Without a skin Godot binds bone i to bind i by the skeleton's rest pose.
            int count = skeleton.GetBoneCount();
            bones = new int[count];
            poses = new Transform3D[count];
            for (int i = 0; i < count; i++)
            {
                bones[i] = i;
                poses[i] = skeleton.GetBoneGlobalRest(i).AffineInverse();
            }
        }

        info = new SkinInfo { Skeleton = skeleton, Bones = bones, BindPoses = poses };
        s_skins.Add(node, info);
        return info.Bones.Length == 0 ? null : info;
    }

    /// <summary>This frame's palette of a skin: per bind, the bone's global pose x the bind pose (as Skeleton3D hands Godot's renderer).</summary>
    private static bool Pose(SkinInfo skin)
    {
        if (!GodotObject.IsInstanceValid(skin.Skeleton)) return false;
        int count = skin.Bones.Length;
        if ((s_paletteRows + (count * 3)) * 4 > s_palette.Length) Array.Resize(ref s_palette, Math.Max(s_palette.Length * 2, (s_paletteRows + (count * 3)) * 4));
        skin.PaletteBase = s_paletteRows;
        Span<float> to = s_palette.AsSpan(s_paletteRows * 4, count * 12);
        Aabb box = default;
        bool any = false;
        for (int i = 0; i < count; i++)
        {
            int bone = skin.Bones[i];
            Transform3D pose = bone >= 0 ? skin.Skeleton.GetBoneGlobalPose(bone) : Transform3D.Identity;
            if (bone >= 0)
            {
                box = any ? box.Expand(pose.Origin) : new Aabb(pose.Origin, Vector3.Zero);
                any = true;
            }

            Transform3D m = bone >= 0 ? pose * skin.BindPoses[i] : Transform3D.Identity;
            Basis b = m.Basis;
            Span<float> r = to.Slice(i * 12, 12);
            (r[0], r[1], r[2], r[3]) = (b.Row0.X, b.Row0.Y, b.Row0.Z, m.Origin.X);
            (r[4], r[5], r[6], r[7]) = (b.Row1.X, b.Row1.Y, b.Row1.Z, m.Origin.Y);
            (r[8], r[9], r[10], r[11]) = (b.Row2.X, b.Row2.Y, b.Row2.Z, m.Origin.Z);
        }

        s_paletteRows += count * 3;
        skin.Frame = s_frame;
        skin.Bones3D = box;
        return true;
    }

    /// <summary>How a material casts in a shadow camera (svm_world.gdshaderinc, the SVM_SHADOW_PASS branches of fragment()).</summary>
    private static MaterialCast CastOf(Material? material)
    {
        if (material is null) return s_solid;
        if (s_materials.TryGetValue(material, out MaterialCast? cast)) return cast;
        cast = s_unsupported;
        if (material is ShaderMaterial { Shader: { } shader } shaderMaterial)
        {
            MaterialCast kind = s_shaders.GetValue(shader, static s => CastOf(Materials.ResponsiveSmokeShaders.BaseCode(s)));
            cast = kind;
            if (kind.Kind == Cast.Masked)
            {
                // The cut is the diffuse texture's alpha x the fragment opacity at 0.5: only a full opacity is carried.
                Variant opacity = shaderMaterial.GetShaderParameter(s_opacity);
                bool faded = opacity.VariantType != Variant.Type.Nil && opacity.AsSingle() != 1f;
                cast = faded ? s_unsupported : kind with { Texture = shaderMaterial.GetShaderParameter(s_diffuse).Obj as Texture2D };
                if (cast.Kind == Cast.Masked && cast.Texture is null) cast = s_unsupported;
            }
        }

        s_materials.Add(material, cast);
        return cast;
    }

    private static MaterialCast CastOf(string code)
    {
        static bool Has(string code, string define) => code.Contains("#define " + define + "\n", StringComparison.Ordinal);
        if (!code.Contains(Materials.MaterialCompiler.TemplatePath, StringComparison.Ordinal)) return s_unsupported;
        bool twoSided = code.Contains("cull_disabled", StringComparison.Ordinal);
        // Blended surfaces, cloaked bodies and the see-through pass discard in a shadow camera.
        if (Has(code, "SVM_CLOAK") || Has(code, "BLEND_TRANSLUCENT") || Has(code, "BLEND_ADDITIVE") || Has(code, "BLEND_MODULATE")
            || Has(code, "BLEND_DARKEN") || Has(code, "VISION_SEE_THROUGH")) return new MaterialCast(Cast.Nothing, twoSided, null);
        if (Has(code, "BILLBOARD") || Has(code, "SVM_SKY_ZONE")) return s_unsupported;
        if (!Has(code, "BLEND_MASKED")) return new MaterialCast(Cast.Solid, twoSided, null);
        // A cut that moves or comes from another layer is left to the template itself.
        if (Has(code, "UV_ANIM") || Has(code, "OPACITY_LAYER") || Has(code, "COMBINE") || Has(code, "FLIPBOOK") || Has(code, "SEQUENCE_D")
            || Has(code, "AUTHORED_MATERIAL")) return s_unsupported;
        return new MaterialCast(Cast.Masked, twoSided, null);
    }

    /// <summary>A mesh's surfaces as this pass draws them (positions, UVs, four bones and weights), read once.</summary>
    private static MeshData Read(Mesh source)
    {
        var data = new MeshData();
        var mesh = (ArrayMesh)source;
        int count = mesh.GetSurfaceCount();
        var surfaces = new SurfaceData?[count];
        for (int s = 0; s < count; s++)
        {
            if (mesh.SurfaceGetPrimitiveType(s) != Mesh.PrimitiveType.Triangles) return data;
            if ((mesh.SurfaceGetFormat(s) & Mesh.ArrayFormat.FlagUse8BoneWeights) != 0) return data;
            Godot.Collections.Array arrays = mesh.SurfaceGetArrays(s);
            Vector3[] positions = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            if (positions.Length == 0) continue;
            Vector2[]? uvs = arrays[(int)Mesh.ArrayType.TexUV].VariantType == Variant.Type.PackedVector2Array ? arrays[(int)Mesh.ArrayType.TexUV].AsVector2Array() : null;
            int[]? bones = arrays[(int)Mesh.ArrayType.Bones].VariantType == Variant.Type.PackedInt32Array ? arrays[(int)Mesh.ArrayType.Bones].AsInt32Array() : null;
            float[]? weights = arrays[(int)Mesh.ArrayType.Weights].VariantType switch
            {
                Variant.Type.PackedFloat32Array => arrays[(int)Mesh.ArrayType.Weights].AsFloat32Array(),
                Variant.Type.PackedFloat64Array => Array.ConvertAll(arrays[(int)Mesh.ArrayType.Weights].AsFloat64Array(), x => (float)x),
                _ => null,
            };
            bool skinned = bones is not null && weights is not null && bones.Length == positions.Length * 4 && weights.Length == positions.Length * 4;
            if ((bones is not null || weights is not null) && !skinned) return data;
            var bytes = new byte[positions.Length * VertexStride];
            for (int i = 0; i < positions.Length; i++)
            {
                Span<byte> v = bytes.AsSpan(i * VertexStride, VertexStride);
                MemoryMarshal.Write(v, in positions[i]);
                if (uvs is not null && i < uvs.Length) MemoryMarshal.Write(v[12..], in uvs[i]);
                if (!skinned) continue;
                Span<ushort> b = MemoryMarshal.Cast<byte, ushort>(v.Slice(20, 8));
                Span<float> w = MemoryMarshal.Cast<byte, float>(v.Slice(28, 16));
                for (int k = 0; k < 4; k++)
                {
                    b[k] = (ushort)Math.Clamp(bones![(i * 4) + k], 0, ushort.MaxValue);
                    w[k] = weights![(i * 4) + k];
                }
            }

            uint[] indices;
            if (arrays[(int)Mesh.ArrayType.Index].VariantType == Variant.Type.PackedInt32Array)
            {
                int[] source32 = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
                indices = new uint[source32.Length];
                for (int i = 0; i < indices.Length; i++) indices[i] = (uint)source32[i];
            }
            else
            {
                indices = new uint[positions.Length];
                for (int i = 0; i < indices.Length; i++) indices[i] = (uint)i;
            }

            if (indices.Length < 3) continue;
            surfaces[s] = new SurfaceData { Vertices = bytes, VertexCount = positions.Length, Indices = indices, Skinned = skinned };
        }

        data.Bounds = mesh.GetAabb();
        data.Surfaces = surfaces;
        data.Supported = true;
        return data;
    }

    /// <summary>The render-thread half.</summary>
    private static class Gpu
    {
        private const string Common = """
            #version 450
            layout(set = 0, binding = 0, std140) uniform Tile
            {
                mat4 clip;
                mat4 view;
            } tile;
            layout(push_constant, std430) uniform Draw
            {
                mat4 model;
                int palette_base;
                int pad0;
                int pad1;
                int pad2;
            } draw;

            """;

        private const string Vertex = Common + """
            layout(set = 0, binding = 1, std430) readonly buffer Palette
            {
                vec4 rows[];
            } palette;
            layout(location = 0) in vec3 a_position;
            layout(location = 1) in vec2 a_uv;
            layout(location = 2) in uvec4 a_bones;
            layout(location = 3) in vec4 a_weights;
            layout(location = 0) out float v_distance;
            layout(location = 1) out vec2 v_uv;

            void main()
            {
                vec4 p = vec4(a_position, 1.0);
                if (draw.palette_base >= 0)
                {
                    // Godot's skinning: the bones' transforms summed by weight.
                    vec3 skinned = vec3(0.0);
                    for (int i = 0; i < 4; i++)
                    {
                        int row = draw.palette_base + int(a_bones[i]) * 3;
                        skinned += a_weights[i] * vec3(dot(palette.rows[row], p), dot(palette.rows[row + 1], p), dot(palette.rows[row + 2], p));
                    }

                    p = vec4(skinned, 1.0);
                }

                vec4 world = draw.model * p;
                // The distance along the light's axis, as the viewport pass's depth copy gives it (svm_spot_depth.glsl).
                v_distance = -(tile.view * world).z;
                v_uv = a_uv;
                gl_Position = tile.clip * world;
            }

            """;

        private static string Fragment(bool masked) => Common + (masked ? "layout(set = 1, binding = 0) uniform sampler2D tex_diffuse;\n" : "") + """
            layout(location = 0) in float v_distance;
            layout(location = 1) in vec2 v_uv;
            layout(location = 0) out vec4 frag;

            void main()
            {

            """ + (masked ? "    if (texture(tex_diffuse, v_uv).a < 0.5) discard;\n" : "") + """
                frag = vec4(v_distance, 0.0, 0.0, 0.0);
            }

            """;

        // SVM_SHADOW_STATS=1 prints, now and then, what a tile drew and copied.
        private static readonly bool s_stats = System.Environment.GetEnvironmentVariable("SVM_SHADOW_STATS") == "1";
        private static int s_statFrames;
        private static RenderingDevice? s_rd;
        private static Rid s_solid, s_masked, s_sampler, s_paletteBuffer, s_framebuffer, s_framebufferAtlas;
        private static uint s_paletteBytes;
        private static long s_vertexFormat;
        private static readonly Rid[] s_pipelines = new Rid[4];
        private static readonly List<(Rid Buffer, Rid Set, Rid Palette)> s_tiles = [];
        private static readonly Dictionary<Rid, Rid> s_textureSets = [];
        private static readonly byte[] s_tileBlock = new byte[128];
        private static readonly byte[] s_drawBlock = new byte[80];
        private static readonly Color[] s_noClear = [];

        public static void Draw(List<Job> jobs, float[] palette, int paletteRows, Rid atlas, Rid staticAtlas)
        {
            RenderingDevice? rd = s_rd ??= RenderingServer.GetRenderingDevice();
            if (rd is null || !atlas.IsValid || !staticAtlas.IsValid) throw new InvalidOperationException("no device or atlas");
            if (!s_solid.IsValid) Init(rd);
            lock (s_dead)
            {
                foreach (Rid rid in s_dead) rd.FreeRid(rid);
                s_dead.Clear();
            }

            if (s_framebufferAtlas != atlas || !rd.FramebufferIsValid(s_framebuffer))
            {
                s_framebuffer = rd.FramebufferCreate([atlas], -1, 1);
                s_framebufferAtlas = atlas;
                long format = rd.FramebufferGetFormat(s_framebuffer);
                for (int i = 0; i < 4; i++)
                {
                    if (s_pipelines[i].IsValid) rd.FreeRid(s_pipelines[i]);
                    s_pipelines[i] = Pipeline(rd, format, masked: (i & 1) != 0, twoSided: (i & 2) != 0);
                }
            }

            // Everything a draw list needs is made before any list opens.
            uint paletteBytes = (uint)Math.Max(48, paletteRows * 16);
            if (paletteBytes > s_paletteBytes)
            {
                if (s_paletteBuffer.IsValid) rd.FreeRid(s_paletteBuffer);
                s_paletteBytes = Math.Max(paletteBytes, 12 * 16 * 256);
                s_paletteBuffer = rd.StorageBufferCreate(s_paletteBytes, ReadOnlySpan<byte>.Empty, 0, 0);
            }

            if (paletteRows > 0) rd.BufferUpdate(s_paletteBuffer, 0, (uint)(paletteRows * 16), MemoryMarshal.AsBytes(palette.AsSpan(0, paletteRows * 4)));
            for (int j = 0; j < jobs.Count; j++)
            {
                if (j >= s_tiles.Count) s_tiles.Add((rd.UniformBufferCreate((uint)s_tileBlock.Length, ReadOnlySpan<byte>.Empty, 0), default, default));
                (Rid buffer, Rid set, Rid setPalette) = s_tiles[j];
                if (setPalette != s_paletteBuffer || !rd.UniformSetIsValid(set))
                {
                    var block = new RDUniform { UniformType = RD.UniformType.UniformBuffer, Binding = 0 };
                    block.AddId(buffer);
                    var bones = new RDUniform { UniformType = RD.UniformType.StorageBuffer, Binding = 1 };
                    bones.AddId(s_paletteBuffer);
                    set = rd.UniformSetCreate([block, bones], s_solid, 0);
                    s_tiles[j] = (buffer, set, s_paletteBuffer);
                }

                Job job = jobs[j];
                Span<float> f = MemoryMarshal.Cast<byte, float>(s_tileBlock.AsSpan());
                Write(job.Clip, f[..16]);
                Write(new Projection(job.View), f[16..32]);
                rd.BufferUpdate(buffer, 0, (uint)s_tileBlock.Length, s_tileBlock);
                foreach (Item item in job.Items)
                {
                    if (!item.Mesh.Uploaded) Upload(rd, item.Mesh);
                    else if (item.Mesh.Stale)
                    {
                        SurfaceData rewritten = item.Mesh.Surfaces[0]!;
                        rd.BufferUpdate(rewritten.VertexBuffer, 0, (uint)rewritten.Vertices.Length, rewritten.Vertices);
                        item.Mesh.Stale = false;
                    }

                    if (item.Texture.IsValid && (!s_textureSets.TryGetValue(item.Texture, out Rid textureSet) || !rd.UniformSetIsValid(textureSet)))
                    {
                        var texture = new RDUniform { UniformType = RD.UniformType.SamplerWithTexture, Binding = 0 };
                        texture.AddId(s_sampler);
                        texture.AddId(RenderingServer.TextureGetRdTexture(item.Texture, false));
                        s_textureSets[item.Texture] = rd.UniformSetCreate([texture], s_masked, 1);
                    }
                }
            }

            s_statFrames++;
            rd.CaptureTimestamp("RD Shadow Pass");
            for (int j = 0; j < jobs.Count; j++)
            {
                Job job = jobs[j];
                // The cached static world first, where the casters are or were; they then keep the nearer distance.
                long copied = 0;
                foreach (Rect2I copy in job.Copy)
                {
                    var origin = new Vector3(job.Origin.X + copy.Position.X, job.Origin.Y + copy.Position.Y, 0f);
                    rd.TextureCopy(staticAtlas, atlas, origin, origin, new Vector3(copy.Size.X, copy.Size.Y, 1f), 0, 0, 0, 0);
                    copied += copy.Area;
                }

                if (s_stats && (s_statFrames % 240) == 0)
                {
                    GD.Print($"SVM_RD_SHADOW tile {j}: {job.Items.Count} surfaces, {job.Copy.Count} boxes copied, {100.0 * copied / Math.Max(1, job.Size.X * job.Size.Y):F1}% of {job.Size}");
                }

                if (job.Items.Count == 0) continue;
                long dl = rd.DrawListBegin(s_framebuffer, default, s_noClear, 1f, 0, new Rect2(job.Origin, job.Size), 0);
                try
                {
                    int bound = -1;
                    foreach (Item item in job.Items)
                    {
                        SurfaceData surface = item.Mesh.Surfaces[item.Surface]!;
                        int pipeline = (item.Texture.IsValid ? 1 : 0) | (item.TwoSided ? 2 : 0);
                        if (pipeline != bound)
                        {
                            bound = pipeline;
                            rd.DrawListBindRenderPipeline(dl, s_pipelines[pipeline]);
                            rd.DrawListBindUniformSet(dl, s_tiles[j].Set, 0);
                        }

                        if (item.Texture.IsValid) rd.DrawListBindUniformSet(dl, s_textureSets[item.Texture], 1);
                        Span<float> d = MemoryMarshal.Cast<byte, float>(s_drawBlock.AsSpan());
                        Write(new Projection(item.Model), d[..16]);
                        MemoryMarshal.Cast<byte, int>(s_drawBlock.AsSpan(64))[0] = item.PaletteBase;
                        rd.DrawListSetPushConstant(dl, s_drawBlock, (uint)s_drawBlock.Length);
                        rd.DrawListBindVertexArray(dl, surface.VertexArray);
                        rd.DrawListBindIndexArray(dl, surface.IndexArray);
                        rd.DrawListDraw(dl, true, 1, 0);
                    }
                }
                finally
                {
                    rd.DrawListEnd();
                }
            }

            rd.CaptureTimestamp("RD Shadow Pass End");
        }

        private static Rid s_scratch;

        /// <summary>A texture like the atlas for <see cref="Verify"/>.</summary>
        public static Rid Scratch(Rid atlas)
        {
            RenderingDevice rd = s_rd ??= RenderingServer.GetRenderingDevice();
            if (s_scratch.IsValid || !atlas.IsValid) return s_scratch;
            using RDTextureFormat format = rd.TextureGetFormat(atlas);
            s_scratch = rd.TextureCreate(new RDTextureFormat
            {
                Format = format.Format, Width = format.Width, Height = format.Height,
                UsageBits = RD.TextureUsageBits.SamplingBit | RD.TextureUsageBits.ColorAttachmentBit | RD.TextureUsageBits.CanCopyToBit | RD.TextureUsageBits.CanCopyFromBit,
            }, new RDTextureView());
            return s_scratch;
        }

        /// <summary>Reads the atlas and the scratch texture back and prints, per tile, how far the two passes are apart.</summary>
        public static void Compare(List<(Vector2I Origin, Vector2I Size, string Label, int Items)> tiles, Rid atlas)
        {
            if (s_rd is not { } rd || !s_scratch.IsValid || !atlas.IsValid) return;
            using RDTextureFormat format = rd.TextureGetFormat(atlas);
            int width = (int)format.Width;
            ReadOnlySpan<float> godot = MemoryMarshal.Cast<byte, float>(rd.TextureGetData(atlas, 0));
            ReadOnlySpan<float> ours = MemoryMarshal.Cast<byte, float>(rd.TextureGetData(s_scratch, 0));
            foreach ((Vector2I origin, Vector2I size, string label, int items) in tiles)
            {
                long apart = 0, missing = 0, extra = 0, drawn = 0;
                float worst = 0f;
                for (int y = 0; y < size.Y; y++)
                {
                    int row = ((origin.Y + y) * width) + origin.X;
                    for (int x = 0; x < size.X; x++)
                    {
                        float a = godot[row + x], b = ours[row + x];
                        float d = MathF.Abs(a - b);
                        worst = MathF.Max(worst, d);
                        // 2 uu, the template's own shadow bias.
                        if (d <= 0.038f) continue;
                        apart++;
                        if (b > a) missing++;
                        else extra++;
                    }
                }

                _ = drawn;
                GD.Print(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                    $"SVM_RD_SHADOW verify {label}: {items} surfaces, {size.X}x{size.Y} texels, {apart} apart by more than the bias ({100.0 * apart / (size.X * (double)size.Y):F3}%: {missing} where ours is further, {extra} where ours is nearer), worst {worst:F3} m"));
            }
        }

        private static void Init(RenderingDevice rd)
        {
            s_solid = Compile(rd, masked: false);
            s_masked = Compile(rd, masked: true);
            s_sampler = rd.SamplerCreate(new RDSamplerState
            {
                MagFilter = RD.SamplerFilter.Linear, MinFilter = RD.SamplerFilter.Linear, MipFilter = RD.SamplerFilter.Linear,
                RepeatU = RD.SamplerRepeatMode.Repeat, RepeatV = RD.SamplerRepeatMode.Repeat,
            });
            s_vertexFormat = rd.VertexFormatCreate(
            [
                new RDVertexAttribute { Location = 0, Offset = 0, Format = RD.DataFormat.R32G32B32Sfloat, Stride = VertexStride, Binding = 0 },
                new RDVertexAttribute { Location = 1, Offset = 12, Format = RD.DataFormat.R32G32Sfloat, Stride = VertexStride, Binding = 0 },
                new RDVertexAttribute { Location = 2, Offset = 20, Format = RD.DataFormat.R16G16B16A16Uint, Stride = VertexStride, Binding = 0 },
                new RDVertexAttribute { Location = 3, Offset = 28, Format = RD.DataFormat.R32G32B32A32Sfloat, Stride = VertexStride, Binding = 0 },
            ]);
        }

        private static Rid Compile(RenderingDevice rd, bool masked)
        {
            RDShaderSpirV spirv = rd.ShaderCompileSpirVFromSource(new RDShaderSource { Language = RD.ShaderLanguage.Glsl, SourceVertex = Vertex, SourceFragment = Fragment(masked) }, false);
            if (!string.IsNullOrEmpty(spirv.CompileErrorVertex) || !string.IsNullOrEmpty(spirv.CompileErrorFragment))
            {
                throw new InvalidOperationException("shader: " + spirv.CompileErrorVertex + spirv.CompileErrorFragment);
            }

            Rid shader = rd.ShaderCreateFromSpirV(spirv, masked ? "svm_rd_shadow_masked" : "svm_rd_shadow");
            return shader.IsValid ? shader : throw new InvalidOperationException("shader create failed");
        }

        private static Rid Pipeline(RenderingDevice rd, long format, bool masked, bool twoSided)
        {
            var raster = new RDPipelineRasterizationState
            {
                CullMode = twoSided ? RD.PolygonCullMode.Disabled : RD.PolygonCullMode.Back, FrontFace = RD.PolygonFrontFace.Clockwise,
            };
            // The nearest distance wins, whatever the order: no depth buffer.
            var attachment = new RDPipelineColorBlendStateAttachment
            {
                EnableBlend = true, ColorBlendOp = RD.BlendOperation.Minimum, AlphaBlendOp = RD.BlendOperation.Minimum,
                SrcColorBlendFactor = RD.BlendFactor.One, DstColorBlendFactor = RD.BlendFactor.One,
                SrcAlphaBlendFactor = RD.BlendFactor.One, DstAlphaBlendFactor = RD.BlendFactor.One,
            };
            Rid pipeline = rd.RenderPipelineCreate(masked ? s_masked : s_solid, format, s_vertexFormat, RD.RenderPrimitive.Triangles, raster,
                new RDPipelineMultisampleState(), new RDPipelineDepthStencilState(), new RDPipelineColorBlendState { Attachments = [attachment] }, 0, 0, []);
            return pipeline.IsValid ? pipeline : throw new InvalidOperationException("pipeline failed");
        }

        private static void Upload(RenderingDevice rd, MeshData mesh)
        {
            foreach (SurfaceData? s in mesh.Surfaces)
            {
                if (s is null) continue;
                s.VertexBuffer = rd.VertexBufferCreate((uint)s.Vertices.Length, s.Vertices, 0);
                s.VertexArray = rd.VertexArrayCreate((uint)s.VertexCount, s_vertexFormat, [s.VertexBuffer], ReadOnlySpan<long>.Empty);
                s.IndexBuffer = rd.IndexBufferCreate((uint)s.Indices.Length, RD.IndexBufferFormat.Uint32, MemoryMarshal.AsBytes(s.Indices.AsSpan()), false, 0);
                s.IndexArray = rd.IndexArrayCreate(s.IndexBuffer, 0, (uint)s.Indices.Length);
            }

            mesh.Uploaded = true;
            mesh.Stale = false;
            s_uploaded.Add(new WeakReference<MeshData>(mesh));
        }

        private static readonly List<WeakReference<MeshData>> s_uploaded = [];

        public static void Free()
        {
            if (s_rd is not { } rd) return;
            foreach (WeakReference<MeshData> reference in s_uploaded)
            {
                if (!reference.TryGetTarget(out MeshData? mesh) || !mesh.Uploaded) continue;
                foreach (SurfaceData? s in mesh.Surfaces)
                {
                    if (s is null) continue;
                    // The arrays go with their buffers.
                    if (s.VertexBuffer.IsValid) rd.FreeRid(s.VertexBuffer);
                    if (s.IndexBuffer.IsValid) rd.FreeRid(s.IndexBuffer);
                    s.VertexBuffer = s.VertexArray = s.IndexBuffer = s.IndexArray = default;
                }

                mesh.Uploaded = false;
            }

            s_uploaded.Clear();
            lock (s_dead)
            {
                foreach (Rid rid in s_dead) rd.FreeRid(rid);
                s_dead.Clear();
            }

            foreach (Rid set in s_textureSets.Values)
            {
                if (rd.UniformSetIsValid(set)) rd.FreeRid(set);
            }

            s_textureSets.Clear();
            foreach ((Rid buffer, Rid set, Rid _) in s_tiles)
            {
                if (rd.UniformSetIsValid(set)) rd.FreeRid(set);
                rd.FreeRid(buffer);
            }

            s_tiles.Clear();
            for (int i = 0; i < s_pipelines.Length; i++)
            {
                if (s_pipelines[i].IsValid) rd.FreeRid(s_pipelines[i]);
                s_pipelines[i] = default;
            }

            if (rd.FramebufferIsValid(s_framebuffer)) rd.FreeRid(s_framebuffer);
            foreach (Rid rid in new[] { s_paletteBuffer, s_sampler, s_solid, s_masked, s_scratch })
            {
                if (rid.IsValid) rd.FreeRid(rid);
            }

            s_framebuffer = s_framebufferAtlas = s_paletteBuffer = s_sampler = s_solid = s_masked = s_scratch = default;
            s_paletteBytes = 0;
        }

        /// <summary>Column-major, as GLSL reads a mat4.</summary>
        private static void Write(in Projection m, Span<float> to)
        {
            ReadOnlySpan<Vector4> columns = [m.X, m.Y, m.Z, m.W];
            for (int c = 0; c < 4; c++)
            {
                to[(c * 4) + 0] = columns[c].X;
                to[(c * 4) + 1] = columns[c].Y;
                to[(c * 4) + 2] = columns[c].Z;
                to[(c * 4) + 3] = columns[c].W;
            }
        }
    }
}
