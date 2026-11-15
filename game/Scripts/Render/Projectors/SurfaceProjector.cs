using System;
using System.Collections.Generic;
using Godot;
using OpenScctGame.Render.Geometry;
using OpenScctGame.Render.Materials;
using OpenScctGame.Render.Data;
using OpenScctGame.Render.Post;

namespace OpenScctGame.Render.Projectors;

/// <summary>
/// Static G16 modulation clipped to opaque receiver triangles. Avoids the volume's scene-depth copy and redrawing
/// entire world batches. Moving opaque/masked receivers project in their existing material draw.
/// </summary>
internal static class SurfaceProjector
{
    public const string Define = "SVM_PROJECTOR_DECAL";
    public static ProjectorEntry? Source { get; private set; }
    public static bool Enabled => Source is not null && !s_unsupported;
    public static bool RuntimeFallback { get; private set; }
    // Set only by the opt-in benchmark; all modes share one scene, textures and camera state.
    internal static string? BenchmarkMode { get; set; }
    public static Aabb Bounds { get; private set; }
    private static bool s_unsupported;
    private static bool s_receiverGlobals;
    private static readonly HashSet<Material> s_fixedMaterials = [];
    public static int MovingReceivers { get; private set; }

    internal static void EnsureReceiverGlobals()
    {
        if (s_receiverGlobals) return;
        using var neutral = Image.CreateEmpty(1, 1, false, Image.Format.Rf);
        neutral.Fill(new Color(.5f,0,0));
        ShaderGlobals.Add("svm_g16_active", RenderingServer.GlobalShaderParameterType.Bool, false);
        ShaderGlobals.Add("svm_g16_texture", RenderingServer.GlobalShaderParameterType.Sampler2D, ImageTexture.CreateFromImage(neutral));
        foreach (string name in new[] { "origin", "x", "y", "z", "shape" })
            ShaderGlobals.Add("svm_g16_" + name, RenderingServer.GlobalShaderParameterType.Vec4, Vector4.Zero);
        s_receiverGlobals = true;
    }

    private static void ReceiverGlobals(Texture2D texture, ProjectorEntry source)
    {
        EnsureReceiverGlobals();
        ShaderGlobals.Set("svm_g16_texture", texture);
        ShaderGlobals.Set("svm_g16_origin", new Vector4(s_origin.X, s_origin.Y, s_origin.Z, s_range));
        ShaderGlobals.Set("svm_g16_x", new Vector4(s_x.X, s_x.Y, s_x.Z, source.LuminanceTexture is null ? 0 : 1));
        ShaderGlobals.Set("svm_g16_y", new Vector4(s_y.X, s_y.Y, s_y.Z, 0));
        ShaderGlobals.Set("svm_g16_z", new Vector4(s_z.X, s_z.Y, s_z.Z, 0));
        ShaderGlobals.Set("svm_g16_shape", new Vector4(1 / (2 * s_width), 1 / (2 * s_height), s_apex > 0 ? 1 / s_apex : 0, source.ProjectOnBackfaces ? 1 : 0));
    }
    private static Action? s_refresh;
    private static Action? s_unbind;
    internal static void RefreshFallback() => s_refresh?.Invoke();
    private static Vector3 s_origin, s_x, s_y, s_z;
    private static float s_width, s_height, s_apex, s_range, s_texel;
    private static readonly List<Rect2> s_ink = [];
    private static ArrayMesh? s_full, s_sparse;
    public static bool Sparse { get; private set; }
    public static string SparseReason { get; private set; } = "unconfigured";
    private static readonly List<Vector3>[] s_vertices = [[], []];
    private static readonly List<Color>[] s_facings = [[], []];
    private static readonly bool s_disabled = System.Environment.GetEnvironmentVariable("SVM_PROJECTOR_GEOMETRY") == "0";

    public static void Configure(IReadOnlyList<ProjectorEntry>? projectors, TextureCache textures)
    {
        s_unbind?.Invoke();
        Source = null; BenchmarkMode = null; RuntimeFallback = false; Sparse = false; s_unsupported = false; s_ink.Clear(); s_full = s_sparse = null;
        s_fixedMaterials.Clear(); MovingReceivers = 0;
        if (s_receiverGlobals) ShaderGlobals.Set("svm_g16_active", false);
        foreach (var list in s_vertices) list.Clear();
        foreach (var list in s_facings) list.Clear();
        if (s_disabled || projectors is not { Count: 1 }) return;
        ProjectorEntry p = projectors[0];
        if (p.TextureFormat != 10 || !p.Static || p.FrameBufferBlending is not (0 or 1) ||
            !p.ProjectBsp || !p.ProjectStaticMesh || !p.ProjectActor || p.UvAnim is not null || p.UvMatrix is not null ||
            p.Flipbook is not null || p.MaxTrace <= 0 || textures.Get(p.LuminanceTexture ?? p.Texture) is null) return;
        var shape = MapProjectors.Shape(p);
        if (!float.IsFinite(shape.HalfWidth + shape.HalfHeight + shape.Apex + p.MaxTrace) || shape.HalfWidth <= 0 || shape.HalfHeight <= 0) return;
        Source = p;
        p.Rotation.GetAxes(out var axisX, out var axisY, out var axisZ);
        s_origin = UeSpace.ToGodot(p.Location);
        s_x = UeSpace.DirectionToGodot(axisX);
        s_y = UeSpace.DirectionToGodot(p.Scale.Y < 0 ? axisY * -1 : axisY);
        s_z = UeSpace.DirectionToGodot(axisZ * -1);
        s_width = UeSpace.LengthToGodot(shape.HalfWidth);
        s_height = UeSpace.LengthToGodot(shape.HalfHeight);
        s_apex = UeSpace.LengthToGodot(shape.Apex);
        s_range = UeSpace.LengthToGodot(p.MaxTrace);
        if (p.LuminanceTexture is { } raw && textures.MipDrop == 0 && textures.GetImage(raw) is { } image)
        {
            int w = image.GetWidth(), h = image.GetHeight();
            s_texel = MathF.Min(2 * s_width / w, 2 * s_height / h);
            // Partition into disjoint rectangles. A two-texel halo keeps every bilinear contributor; sparse geometry
            // is used only when every receiver projects at >= one screen pixel per texel (no minification).
            const int tile = 8, padding = 2;
            for (int row = 0; row < h; row += tile)
            {
                int first = -1;
                for (int col = 0; col <= (w + tile - 1) / tile * tile; col += tile)
                {
                    bool ink = false;
                    if (col < w)
                        for (int y = row - padding; y < Math.Min(row + tile, h) + padding && !ink; y++)
                            for (int x = col - padding; x < Math.Min(col + tile, w) + padding; x++)
                                if (MathF.Abs(image.GetPixel((x % w + w) % w, (y % h + h) % h).R * 2 - 1) > .000016f) { ink = true; break; }
                    if (ink && first < 0) first = col;
                    if (!ink && first >= 0) { s_ink.Add(new Rect2((float)first / w, (float)row / h, (float)(Math.Min(col, w) - first) / w, (float)Math.Min(tile, h - row) / h)); first = -1; }
                }
            }
        }
        ReceiverGlobals(textures.Get(p.LuminanceTexture ?? p.Texture)!, p);
        Bounds = new Aabb(s_origin, Vector3.Zero);
        for (int i = 0; i < 8; i++)
        {
            float dx = (i & 4) != 0 ? s_range : 0, scale = s_apex > 0 ? 1 + dx / s_apex : 1;
            Bounds = Bounds.Expand(s_origin + s_x * dx + s_y * ((i & 1) != 0 ? s_width * scale : -s_width * scale)
                + s_z * ((i & 2) != 0 ? s_height * scale : -s_height * scale));
        }
    }

    private static float Distance(Vector3 v, int plane)
    {
        Vector3 d = v - s_origin;
        float dx = d.Dot(s_x), scale = s_apex > 0 ? 1 + dx / s_apex : 1;
        return plane switch { 0 => dx, 1 => s_range - dx, 2 => s_width * scale + d.Dot(s_y),
            3 => s_width * scale - d.Dot(s_y), 4 => s_height * scale + d.Dot(s_z), _ => s_height * scale - d.Dot(s_z) };
    }

    /// <summary>Collect a merged world surface before either renderer uploads it. Original streams stay untouched.</summary>
    public static void Collect(SurfaceBuilder builder, MaterialEntry? entry, Material material, bool animated)
    {
        if (!Enabled) return;
        s_fixedMaterials.Add(material);
        var streams = builder.RdStreams();
        Vector3[] vertices = streams.Positions;
        int[] indices = streams.Indices;
        var polygon = new List<Vector3>(10);
        var clipped = new List<Vector3>(10);
        for (int i = 0; i < indices.Length; i += 3)
        {
            Vector3 a = vertices[indices[i]], b = vertices[indices[i + 1]], c = vertices[indices[i + 2]];
            if (!Bounds.Intersects(new Aabb(a, Vector3.Zero).Expand(b).Expand(c).Grow(.00001f))) continue;
            polygon.Clear(); polygon.Add(a); polygon.Add(b); polygon.Add(c);
            for (int plane = 0; plane < 6 && polygon.Count > 0; plane++)
            {
                clipped.Clear();
                Vector3 from = polygon[^1]; float fd = Distance(from, plane);
                foreach (Vector3 to in polygon)
                {
                    float td = Distance(to, plane);
                    if ((fd >= 0) != (td >= 0)) clipped.Add(from.Lerp(to, fd / (fd - td)));
                    if (td >= 0) clipped.Add(to);
                    from = to; fd = td;
                }
                (polygon, clipped) = (clipped, polygon);
            }
            if (polygon.Count < 3) continue;
            // Transparent surfaces do not populate the volume's depth buffer. Masked/displaced/changing surfaces
            // need the original pass: a solid decal would fill their holes or outlive their movement.
            if (entry?.Blend is MaterialBlend.Translucent or MaterialBlend.Additive or MaterialBlend.Modulate or MaterialBlend.Darken) continue;
            if (entry is null || entry.Blend != MaterialBlend.Opaque || animated || material.NextPass is not null ||
                material.HasMeta("authored_displacement") || (entry.Sequences.TryGetValue("diffuse", out var sequence) && sequence.SwitchMaterial) ||
                (material is ShaderMaterial { Shader: { } receiverShader } && ResponsiveSmokeShaders.BaseCode(receiverShader).Contains("#define BILLBOARD", StringComparison.Ordinal))) { s_unsupported = true; return; }
            Vector3 normal = (c - a).Cross(b - a);
            if (normal.LengthSquared() < 1e-12f) continue;
            if (Source is { ProjectOnBackfaces: false } && !entry.TwoSided && normal.Dot(-s_x) <= 0) continue;
            Color facing = normal.Dot(-s_x) > 0 ? Colors.White : Colors.Black;
            int kind = entry.TwoSided ? 1 : 0;
            for (int t = 1; t < polygon.Count - 1; t++)
                foreach (Vector3 v in new[] { polygon[0], polygon[t], polygon[t + 1] })
                { s_vertices[kind].Add(v); s_facings[kind].Add(facing); }
        }
    }

    internal static MeshInstance3D? CreateNode(Texture2D texture)
    {
        if (!Enabled || Source is not { } source) return null;
        var mesh = new ArrayMesh();
        var sparse = new ArrayMesh();
        for (int kind = 0; kind < 2; kind++)
        {
            if (s_vertices[kind].Count == 0) continue;
            var arrays = new Godot.Collections.Array(); arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = s_vertices[kind].ToArray();
            arrays[(int)Mesh.ArrayType.Color] = s_facings[kind].ToArray();
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
            var material = new ShaderMaterial { Shader = ResponsiveSmokeShaders.Shared(
                "shader_type spatial;\nrender_mode unshaded, fog_disabled, skip_vertex_transform, depth_draw_never, blend_mul, " +
                (kind == 1 ? "cull_disabled" : "cull_back") + ";\n#define " + Define + "\n#define BLEND_MODULATE\n" +
                "#include \"res://Shaders/svm_world.gdshaderinc\"\n", "clipped G16 projector") };
            material.SetShaderParameter("proj_tex", texture);
            material.SetShaderParameter("proj_luminance", source.LuminanceTexture is not null);
            material.SetShaderParameter("proj_origin", s_origin);
            material.SetShaderParameter("proj_x", s_x);
            material.SetShaderParameter("proj_y", s_y / (2 * s_width));
            material.SetShaderParameter("proj_z", s_z / (2 * s_height));
            material.SetShaderParameter("proj_inverse_apex", s_apex > 0 ? 1 / s_apex : 0);
            material.SetShaderParameter("proj_backfaces", source.ProjectOnBackfaces);
            material.RenderPriority = -127;
            mesh.SurfaceSetMaterial(mesh.GetSurfaceCount() - 1, material);
            if (s_ink.Count > 0)
            {
                var positions = new List<Vector3>(); var colors = new List<Color>();
                List<Vector3> poly = new(10), cut = new(10);
                for (int i = 0; i < s_vertices[kind].Count; i += 3)
                    foreach (Rect2 rect in s_ink)
                    {
                        poly.Clear(); poly.Add(s_vertices[kind][i]); poly.Add(s_vertices[kind][i+1]); poly.Add(s_vertices[kind][i+2]);
                        for (int plane = 0; plane < 4 && poly.Count > 0; plane++)
                        {
                            float D(Vector3 vertex)
                            {
                                Vector3 d = vertex - s_origin;
                                float q = 1 + d.Dot(s_x) * (s_apex > 0 ? 1 / s_apex : 0);
                                return plane switch { 0 => d.Dot(s_y) / (2 * s_width) - (rect.Position.X - .5f) * q,
                                    1 => (rect.End.X - .5f) * q - d.Dot(s_y) / (2 * s_width),
                                    2 => d.Dot(s_z) / (2 * s_height) - (rect.Position.Y - .5f) * q,
                                    _ => (rect.End.Y - .5f) * q - d.Dot(s_z) / (2 * s_height) };
                            }
                            cut.Clear(); Vector3 from = poly[^1]; float fd = D(from);
                            foreach (Vector3 to in poly)
                            {
                                float td = D(to);
                                if ((fd >= 0) != (td >= 0)) cut.Add(from.Lerp(to, fd / (fd - td)));
                                if (td >= 0) cut.Add(to);
                                from = to; fd = td;
                            }
                            (poly, cut) = (cut, poly);
                        }
                        for (int t = 1; t < poly.Count - 1; t++)
                            foreach (Vector3 vertex in new[] { poly[0], poly[t], poly[t+1] }) { positions.Add(vertex); colors.Add(s_facings[kind][i]); }
                    }
                if (positions.Count > 0)
                {
                    var clippedArrays = new Godot.Collections.Array(); clippedArrays.Resize((int)Mesh.ArrayType.Max);
                    clippedArrays[(int)Mesh.ArrayType.Vertex] = positions.ToArray(); clippedArrays[(int)Mesh.ArrayType.Color] = colors.ToArray();
                    sparse.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, clippedArrays);
                    sparse.SurfaceSetMaterial(sparse.GetSurfaceCount() - 1, material);
                }
            }
        }
        if (mesh.GetSurfaceCount() == 0) return null;
        s_full = mesh; s_sparse = sparse.GetSurfaceCount() == 0 ? null : sparse;
        return new MeshInstance3D { Name = "ClippedG16_" + source.Name, Mesh = mesh, Layers = RenderLayers.Effects,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
    }

    /// <summary>Use sparse neutral-grey coverage only without fog/smoke and without texture minification in any live view.</summary>
    internal static void SelectGeometry(MeshInstance3D decal, IEnumerable<Camera3D> cameras)
    {
        bool smoke = ResponsiveSmokeShaders.Enabled && ShaderGlobals.TryGet("svm_smoke_active", out Variant active) && active.AsBool();
        bool sparse = s_sparse is not null && !smoke;
        SparseReason = s_sparse is null ? "no neutral tiles" : smoke ? "responsive smoke" : "no live view";
        bool view = false;
        if (sparse) foreach (Camera3D camera in cameras)
        {
            if (!camera.IsInsideTree() || !camera.Current || (camera.CullMask & RenderLayers.Effects) == 0 || camera.GetWorld3D() != decal.GetWorld3D()) continue;
            if (camera.GetViewport() is SubViewport { RenderTargetUpdateMode: SubViewport.UpdateMode.Disabled }) continue;
            view = true;
            if (SurfaceFogRegistry.HasFog(camera)) { SparseReason = "fog"; sparse = false; break; }
            Vector2 size = camera.GetViewport().GetVisibleRect().Size * camera.GetViewport().Scaling3DScale;
            Projection projection = camera.GetCameraProjection();
            float focal = MathF.Min(MathF.Abs(size.X * projection.X.X), MathF.Abs(size.Y * projection.Y.Y)) * .5f;
            foreach (var vertices in s_vertices)
                for (int i = 0; i < vertices.Count; i += 3)
                {
                    Vector3 a = vertices[i], b = vertices[i+1], c = vertices[i+2];
                    Vector3 normal = (b-a).Cross(c-a);
                    if (normal.LengthSquared() < 1e-12f) continue;
                    normal = normal.Normalized();
                    float distance = MathF.Max(camera.GlobalPosition.DistanceTo(a), MathF.Max(camera.GlobalPosition.DistanceTo(b), camera.GlobalPosition.DistanceTo(c)));
                    float cosine = MathF.Min(MathF.Abs(normal.Dot((camera.GlobalPosition-a).Normalized())), MathF.Min(MathF.Abs(normal.Dot((camera.GlobalPosition-b).Normalized())), MathF.Abs(normal.Dot((camera.GlobalPosition-c).Normalized()))));
                    float density = focal * s_texel * cosine / MathF.Max(distance, .001f);
                    if (camera.Projection == Camera3D.ProjectionType.Orthogonal || camera.IsPositionBehind(a) || camera.IsPositionBehind(b) || camera.IsPositionBehind(c) || density < 1.0f)
                    { SparseReason = $"minification/clip: {density:F3} pixels per texel"; sparse = false; break; }
                }
        }
        Sparse = sparse && view;
        if (Sparse) SparseReason = "neutral tiles omitted";
        if (s_full is not null) decal.Mesh = Sparse ? s_sparse : s_full;
    }

    /// <summary>Track dynamic receivers without enabling the full scene projector. Cloaks retain their screen-copy semantics.</summary>
    public static void Bind(Node3D root, MeshInstance3D volume, MeshInstance3D decal)
    {
        SceneTree tree = root.GetTree();
        ProjectorEntry? source = Source;
        var receivers = new List<MeshInstance3D>();
        var cameras = new List<Camera3D>();
        var moving = new MovingProjectorReceivers(s_fixedMaterials);
        void Added(Node node) { if (node is MeshInstance3D m && m != volume && m != decal && !m.HasMeta("svm_fixed_cell")) receivers.Add(m); if (node is Camera3D c) cameras.Add(c); }
        void Scan(Node node) { Added(node); foreach (Node child in node.GetChildren()) Scan(child); }
        Scan(tree.Root);
        void Removed(Node node) { if (node is MeshInstance3D m) receivers.Remove(m); if (node is Camera3D c) cameras.Remove(c); }
        bool first = true;
        void Refresh()
        {
            if (!ReferenceEquals(Source, source)) return;
            bool fallback = BenchmarkMode == "volume" || System.Environment.GetEnvironmentVariable("SVM_PROJECTOR_FALLBACK") == "1";
            bool active = !fallback && BenchmarkMode != "missing";
            ShaderGlobals.Set("svm_g16_active", active);
            moving.Update(receivers, decal.GetWorld3D(), Bounds, active);
            if (moving.UnsupportedReceiver) { fallback = true; ShaderGlobals.Set("svm_g16_active", false); }
            MovingReceivers = moving.Count;
            bool wasSparse = Sparse;
            SelectGeometry(decal, cameras);
            if (first || fallback != RuntimeFallback || wasSparse != Sparse)
                GD.Print($"SVM_PROJECTOR_GEOMETRY fallback={fallback} receivers={MovingReceivers} sparse={Sparse} reason={SparseReason} triangles={(s_vertices[0].Count + s_vertices[1].Count) / 3}");
            first = false;
            RuntimeFallback = fallback;
            volume.Visible = fallback && BenchmarkMode != "missing";
            decal.Visible = !fallback && BenchmarkMode != "missing";
        }
        tree.NodeAdded += Added; tree.NodeRemoved += Removed;
        s_refresh = Refresh;
        RenderingServer.FramePreDraw += Refresh;
        bool disposed = false;
        void Unbind()
        {
            if (disposed) return;
            disposed = true;
            moving.Dispose(); tree.NodeAdded -= Added; tree.NodeRemoved -= Removed;
            RenderingServer.FramePreDraw -= Refresh; root.TreeExiting -= Unbind;
            if (s_refresh == Refresh) s_refresh = null;
            if (s_unbind == Unbind) s_unbind = null;
        }
        s_unbind = Unbind; root.TreeExiting += Unbind;
        Refresh();
    }
}
