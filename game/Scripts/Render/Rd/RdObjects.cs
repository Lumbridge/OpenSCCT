using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Godot;
using OpenScctGame.Render.Materials;

namespace OpenScctGame.Render.Rd;

/// <summary>
/// Meshes outside the merged world in the own frame (the options' Fast renderer): the map's gameplay meshes and the characters,
/// with solid or masked template materials. Each surface is drawn as the original draws an actor
/// (docs/PERFORMANCE-LOWEND-2026-11.md, "The states it draws with"): its texture times a light worked out at each
/// vertex (the baked vertex colour, or the actor's ambient and up to four lights), doubled. Skinned meshes are posed in
/// the vertex shader from their skeleton's bones. Blended surfaces, cloaks, tints and meshes rewritten in place are
/// not drawn yet.
/// </summary>
internal sealed class RdObjects
{
    /// <summary>
    /// 44 bytes: position, normal, UV, colour, second UV (the lightmap's, for map surfaces left to this pass). Skinned:
    /// 60 bytes: position, normal, UV, colour, four bone indices and weights.
    /// </summary>
    public const int Stride = 44, SkinnedStride = 60;

    /// <summary>Bytes of one object's block: model matrix, ambient, four lights (position, colour, spot).</summary>
    public const int ObjectBytes = 64 + 16 + (4 * 48) + 32 + 16;

    public sealed class Upload
    {
        public required int Surface;
        public required byte[] Vertices;
        public required uint[] Indices;
        public required int VertexCount;
        public required bool Skinned;
    }

    public struct Draw
    {
        public int Surface, Object, Kind;
        /// <summary>The first bone matrix of the draw's skeleton in the frame's palette, or -1 for a rigid mesh.</summary>
        public int Palette;
        public bool Masked, TwoSided;
        public Rid Texture;
        /// <summary>0 solid, 1 blended by the texture's alpha, 2 added, 3 a cloaked body (drawn last, over a copy of the frame).</summary>
        public int Blend;
        /// <summary>The spot slots whose sphere reaches the mesh (0: its shader holds no spot code).</summary>
        public int SpotBits;
        /// <summary>The lit flares (FlareLights) when one reaches the mesh, else 0: a small program then adds them at each pixel.</summary>
        public int Flares;
        /// <summary>
        /// Set for a material with more than a texture and a light (layers, a combiner, an environment, cosmetics, a
        /// multiplied blend): the surface is drawn by the translated template with this material's values.
        /// </summary>
        public RdMaterial? Material;
        /// <summary>A mirrored instance (its model matrix turns the triangles' winding over): the other face is the front.</summary>
        public bool Mirrored;
        /// <summary>The squared distance from the eye to the mesh's box: blended draws go far to near.</summary>
        public float Distance;
    }

    private sealed class SurfaceInfo
    {
        public int Id;
        public bool Uploaded;
        /// <summary>A mesh rewritten in place (cloth): the version of its arrays the GPU copy holds, and its index list.</summary>
        public int Version = -1;
        public uint[]? Indices;
    }

    /// <summary>The current shape of a mesh that is rewritten in place (SoftBodyViews), as it was last written.</summary>
    private sealed class DynamicSource
    {
        public Vector3[] Positions = [], Normals = [];
        public Vector2[] Uvs = [];
        public int[] Indices = [];
        public int Version;
        public ulong SeenFrame = ulong.MaxValue;
    }

    /// <summary>True when the own frame had the rewritten mesh in its view in the last frames (it is then kept moving).</summary>
    public static bool InView(Mesh mesh) =>
        RdWorldRenderer.OwnFrame && s_dynamic.TryGetValue(mesh, out DynamicSource? source) && source.SeenFrame != ulong.MaxValue
        && Engine.GetProcessFrames() - source.SeenFrame <= 3;

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Mesh, DynamicSource> s_dynamic = [];

    /// <summary>
    /// The arrays a mesh was just rewritten from (one surface of triangles, a fixed vertex count and index list): the
    /// own frame draws it from these, uploading them again when they changed and the mesh is in view.
    /// </summary>
    public static void SetDynamic(Mesh mesh, Vector3[] positions, Vector3[] normals, Vector2[] uvs, int[] indices)
    {
        if (!RdWorldRenderer.OwnFrame) return;
        DynamicSource source = s_dynamic.GetValue(mesh, static _ => new DynamicSource());
        (source.Positions, source.Normals, source.Uvs, source.Indices) = (positions, normals, uvs, indices);
        source.Version++;
    }

    private static Upload PackDynamic(DynamicSource source, SurfaceInfo surface)
    {
        Vector3[] positions = source.Positions;
        var bytes = new byte[positions.Length * Stride];
        for (int i = 0; i < positions.Length; i++)
        {
            Span<byte> v = bytes.AsSpan(i * Stride, Stride);
            MemoryMarshal.Write(v, in positions[i]);
            Vector3 n = i < source.Normals.Length ? source.Normals[i] : Vector3.Up;
            MemoryMarshal.Write(v[12..], in n);
            Vector2 uv = i < source.Uvs.Length ? source.Uvs[i] : Vector2.Zero;
            MemoryMarshal.Write(v[24..], in uv);
            v[32] = v[33] = v[34] = v[35] = 255;
        }

        if (surface.Indices is null || surface.Indices.Length != source.Indices.Length)
        {
            surface.Indices = new uint[source.Indices.Length];
            for (int i = 0; i < source.Indices.Length; i++) surface.Indices[i] = (uint)source.Indices[i];
        }

        return new Upload { Surface = surface.Id, Vertices = bytes, Indices = surface.Indices, VertexCount = positions.Length, Skinned = false };
    }

    private sealed class SkinInfo
    {
        public required Skeleton3D Skeleton;
        public required int[] Bones;
        public required Transform3D[] Binds;
    }

    // The include line itself: other shaders name the template in their comments.
    private const string TemplateInclude = "#include \"" + MaterialCompiler.TemplatePath + "\"";
    private static readonly StringName s_diffuse = "tex_diffuse";
    private static readonly StringName s_static = "Static";
    private static readonly StringName s_softBodies = "SoftBodies";
    private static readonly StringName s_ambient = "svm_ambient";
    private static readonly StringName[] s_pos = [new("svm_l0_pos"), new("svm_l1_pos"), new("svm_l2_pos"), new("svm_l3_pos")];
    private static readonly StringName[] s_col = [new("svm_l0_col"), new("svm_l1_col"), new("svm_l2_col"), new("svm_l3_col")];
    private static readonly StringName[] s_spot = [new("svm_l0_spot"), new("svm_l1_spot"), new("svm_l2_spot"), new("svm_l3_spot")];

    private readonly Dictionary<(Mesh, int), SurfaceInfo> _surfaces = [];
    private readonly Dictionary<Material, (int Kind, bool Masked, bool TwoSided, bool Drawn, Rid Texture, int Blend)> _materials = [];
    private readonly Dictionary<MeshInstance3D, SkinInfo?> _skins = [];
    private readonly Dictionary<Material, bool> _wantsTemplate = [];
    private readonly Dictionary<(ShaderMaterial, bool), RdMaterial?> _templates = [];
    private readonly List<(ShaderMaterial Source, RdMaterial Material, bool Live)> _refresh = [];
    private int _refreshAt;
    private static readonly StringName s_tint = "svm_tint";
    private static readonly StringName s_glowHue = "svm_glow_hue";
    private static readonly StringName s_scroll = "svm_scroll";
    private static readonly StringName s_visionActor = "svm_vision_actor";
    private const string LaserInclude = "#include \"res://Shaders/svm_laser.gdshaderinc\"";
    // The materials as thermal and EMF vision draw them: the whole template with its vision outputs.
    private readonly Dictionary<(ShaderMaterial, bool), RdMaterial?> _visionTemplates = [];

    private RdMaterial? VisionTemplateOf(ShaderMaterial material, bool skinned)
    {
        if (_visionTemplates.TryGetValue((material, skinned), out RdMaterial? template)) return template;
        // The template's materials, and the laser beams' own vision shader (svm_laser.gdshaderinc).
        template = material.Shader is { } shader && ResponsiveSmokeShaders.BaseCode(shader) is { } code &&
            (code.Contains(TemplateInclude, StringComparison.Ordinal) || code.Contains(LaserInclude, StringComparison.Ordinal))
            ? RdWorldGeometry.ActorMaterial(material, skinned ? 2 : 1, vision: true) : null;
        _visionTemplates[(material, skinned)] = template;
        if (template is not null) _refresh.Add((material, template, template.Cloak || material.GetShaderParameter(s_scroll).VariantType != Variant.Type.Nil));
        return template;
    }

    private RdMaterial? TemplateOf(ShaderMaterial material, bool skinned)
    {
        if (_templates.TryGetValue((material, skinned), out RdMaterial? template)) return template;
        template = RdWorldGeometry.ActorMaterial(material, skinned ? 2 : 1);
        _templates[(material, skinned)] = template;
        // A conveyor slides its textures every frame; other values change now and then (cosmetics, switched screens).
        // A cloak fades in and out by its glass value.
        if (template is not null) _refresh.Add((material, template, template.Cloak || material.GetShaderParameter(s_scroll).VariantType != Variant.Type.Nil));
        return template;
    }

    /// <summary>Reads again the values of the materials that change every frame, and of two of the others in turn.</summary>
    private void RefreshTemplates()
    {
        for (int i = _refresh.Count - 1; i >= 0; i--)
        {
            (ShaderMaterial source, RdMaterial material, bool live) = _refresh[i];
            if (!GodotObject.IsInstanceValid(source))
            {
                _refresh.RemoveAt(i);
                continue;
            }

            if (live) RdWorldGeometry.Refresh(source, material);
        }

        for (int n = 0; n < 2 && _refresh.Count > 0; n++)
        {
            _refreshAt = (_refreshAt + 1) % _refresh.Count;
            (ShaderMaterial source, RdMaterial material, bool live) = _refresh[_refreshAt];
            if (!live && GodotObject.IsInstanceValid(source)) RdWorldGeometry.Refresh(source, material);
        }
    }
    private readonly Stack<Node> _stack = new();
    private readonly List<MeshInstance3D> _nodes = [];
    /// <summary>The scene's mesh nodes this pass looks at each frame (statistics).</summary>
    public int NodeCount => _nodes.Count;
    // By node, what its last full look found, for leaving it out of a frame at the cost of its layers and its
    // transform alone: its mesh's box, and bit 0 known, bit 1 a sprite turned to the camera, bit 2 skinned. A node is
    // looked at in full again whenever it is in view, and in turn with the others (RecheckPerFrame).
    private readonly List<(Aabb Box, byte State)> _seen = [];
    private readonly Dictionary<MeshInstance3D, (Rid Base, Mesh Mesh)> _meshOf = new(ReferenceEqualityComparer.Instance);
    private Node? _walked;

    /// <summary>Set when a node entered or left the tree: the next <see cref="Gather"/> walks the scene again.</summary>
    public bool Dirty { get; set; } = true;
    private int _nextSurface;

    public void Begin(List<Draw> draws, List<byte> objects, List<float> palette, List<Upload> uploads)
    {
        draws.Clear();
        objects.Clear();
        palette.Clear();
        uploads.Clear();
    }

    /// <summary>Adds the visible meshes under <paramref name="root"/> (main thread, after <see cref="Begin"/>).</summary>
    private readonly HashSet<ulong> _prepared = [];
    private static readonly string? s_dump = System.Environment.GetEnvironmentVariable("SVM_RD_FRAME_DUMP") is { Length: > 0 } path ? path : null;
    private int _dumpFrame;

    /// <summary>SVM_RD_FRAME_DUMP=file (diagnosis only): every geometry node of the scene, 300 frames in, with what this pass makes of it.</summary>
    private void Dump(Node root)
    {
        var text = new System.Text.StringBuilder();
        var stack = new Stack<Node>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            Node at = stack.Pop();
            foreach (Node child in at.GetChildren()) stack.Push(child);
            if (at is not GeometryInstance3D g) continue;
            if (at.GetPath().ToString().Contains("/Static/")) continue;
            text.Append(at.GetPath()).Append(" | ").Append(at.GetClass()).Append(" layers=").Append(g.Layers.ToString("x")).Append(" visible=").Append(g.IsVisibleInTree())
                .Append(" at=").Append(g.GlobalPosition);
            if (at is MeshInstance3D m)
            {
                text.Append(" mesh=").Append(m.Mesh?.GetClass()).Append(" skin=").Append(m.Skin is not null).Append(" known=").Append(_nodes.Contains(m));
                for (int i = 0; m.Mesh is { } mesh && i < mesh.GetSurfaceCount(); i++)
                {
                    Material? material = m.MaterialOverride ?? m.GetSurfaceOverrideMaterial(i) ?? mesh.SurfaceGetMaterial(i);
                    text.Append(" [").Append(material?.GetClass());
                    if (material is ShaderMaterial sm && _materials.TryGetValue(sm, out var d)) text.Append(" kind=").Append(d.Kind).Append(" drawn=").Append(d.Drawn).Append(" blend=").Append(d.Blend).Append(" template=").Append(_wantsTemplate.GetValueOrDefault(sm));
                    else if (material is ShaderMaterial other) text.Append(' ').Append(other.Shader?.ResourcePath);
                    text.Append(']');
                }
            }

            text.AppendLine();
        }

        System.IO.File.WriteAllText(s_dump!, text.ToString());
    }
    /// <summary>The highest bone index a packed vertex holds.</summary>
    internal const int MaxBone = 255;

    private Node? _staticRoot;
    private int _recheckAt;
    private const int RecheckPerFrame = 24;
    private readonly List<MeshInstance3D> _staticLeft = [];

    /// <summary>
    /// Everything a mesh node will need when it is first drawn, made when the node is first met in the scene (at the
    /// map's load, or when a character spawns) instead of the frame it comes into view: its surfaces packed for
    /// upload, and its template materials named for the render thread to compile.
    /// </summary>
    private void Prepare(MeshInstance3D node, List<Upload> uploads, List<RdMaterial> warm)
    {
        bool sky = (node.Layers & (RenderLayers.World | RenderLayers.FirstPerson | RenderLayers.Effects | RenderLayers.StaticWorld)) == 0;
        if (node.Mesh is not { } mesh || (sky && (node.Layers & RenderLayers.Sky) == 0)) return;
        bool skinned = false;
        if (node.Skin is not null)
        {
            if (!_skins.TryGetValue(node, out SkinInfo? skin))
            {
                skin = SkinOf(node);
                _skins[node] = skin;
            }

            if (skin is null) return;
            skinned = true;
        }

        bool dynamic = s_dynamic.TryGetValue(mesh, out _);
        for (int s = 0; s < mesh.GetSurfaceCount(); s++)
        {
            if ((node.MaterialOverride ?? node.GetSurfaceOverrideMaterial(s) ?? mesh.SurfaceGetMaterial(s)) is not ShaderMaterial material) continue;
            if (material is EffectMaterial effect)
            {
                if (!skinned && !sky && EffectBlend(effect) >= 0) SurfaceReady(mesh, s, uploads);
                // A weapon beam's thermal and EMF pass (the next pass of its additive look).
                for (ShaderMaterial? pass = effect.NextPass as ShaderMaterial; pass is not null && !sky; pass = pass.NextPass as ShaderMaterial)
                {
                    if (pass is not EffectMaterial && !_visionTemplates.ContainsKey((pass, skinned)) && VisionTemplateOf(pass, skinned) is { } seen) warm.Add(seen);
                }

                continue;
            }

            Known(material);
            if (!_materials.TryGetValue(material, out (int Kind, bool Masked, bool TwoSided, bool Drawn, Rid Texture, int Blend) m))
            {
                m = Describe(material);
                _materials[material] = m;
                _wantsTemplate[material] = WantsTemplate(material);
            }

            bool fresh = !sky && _wantsTemplate.GetValueOrDefault(material) && !_templates.ContainsKey((material, skinned));
            RdMaterial? template = !sky && _wantsTemplate.GetValueOrDefault(material) ? TemplateOf(material, skinned) : null;
            if (fresh && RdWorldRenderer.Stats) GD.Print($"SVM_RD_FRAME template for {node.GetPath()} surface {s} at process frame {Engine.GetProcessFrames()}");
            if (!sky)
            {
                // Thermal and EMF draw every template material by the template: made ready with the others, and the
                // see-through pass of a partly see-through one.
                for (ShaderMaterial? pass = material; pass is not null; pass = pass.NextPass as ShaderMaterial)
                {
                    if (!_visionTemplates.ContainsKey((pass, skinned)) && VisionTemplateOf(pass, skinned) is { } seen) warm.Add(seen);
                }
            }

            if (template is null && !m.Drawn && !_visionTemplates.ContainsKey((material, skinned))) continue;
            if (template is not null && fresh) warm.Add(template);
            // Cloth is uploaded as it is rewritten.
            if (dynamic && !skinned && s == 0) continue;
            if (!_surfaces.TryGetValue((mesh, s), out SurfaceInfo? surface))
            {
                surface = new SurfaceInfo { Id = _nextSurface++ };
                _surfaces[(mesh, s)] = surface;
            }

            if (surface.Uploaded) continue;
            surface.Uploaded = true;
            if (Pack(mesh, s, surface.Id, skinned) is { } upload) uploads.Add(upload);
            else surface.Id = -1;
        }
    }

    /// <param name="vision">Thermal or EMF: every template material is drawn by the template with its vision outputs.</param>
    public void Gather(Node root, Plane[] planes, Func<Plane[], Aabb, bool> inside, Func<Aabb, int> spots, List<Draw> draws, List<byte> objects, List<float> palette, List<Upload> uploads,
        List<RdMaterial> warm, Basis view, Vector3 eye, bool vision = false)
    {
        Span<float> block = stackalloc float[ObjectBytes / 4];
        RefreshTemplates();
        if (Dirty || !ReferenceEquals(root, _walked))
        {
            // The scene's mesh nodes, walked again only after a node entered or left the tree.
            Dirty = false;
            _walked = root;
            _nodes.Clear();
            _seen.Clear();
            _meshOf.Clear();
            _stack.Clear();
            _stack.Push(root);
            while (_stack.Count > 0)
            {
                Node at = _stack.Pop();
                if (at.Name == s_static)
                {
                    // The map's merged meshes do not change after the map is built: found once. Only those the world
                    // pass left are this pass's (its own are on its own layers).
                    if (!ReferenceEquals(at, _staticRoot))
                    {
                        _staticRoot = at;
                        _staticLeft.Clear();
                        var within = new Stack<Node>();
                        within.Push(at);
                        while (within.Count > 0)
                        {
                            Node part = within.Pop();
                            foreach (Node child in part.GetChildren()) within.Push(child);
                            if (part is MeshInstance3D left && (left.Layers & (RenderLayers.StaticWorld | RenderLayers.World | RenderLayers.Sky)) != 0) _staticLeft.Add(left);
                        }
                    }

                    foreach (MeshInstance3D left in _staticLeft)
                    {
                        _nodes.Add(left);
                        if (_prepared.Add(left.GetInstanceId())) Prepare(left, uploads, warm);
                    }

                    continue;
                }

                foreach (Node child in at.GetChildren()) _stack.Push(child);
                if (at is MeshInstance3D found)
                {
                    _nodes.Add(found);
                    if (_prepared.Add(found.GetInstanceId())) Prepare(found, uploads, warm);
                }
            }
        }

        // A material given to a node after it was first met (an objective's marker, a screen switched on, a cosmetic)
        // would otherwise be made ready in the frame it first comes into view: a few nodes a frame are looked at
        // again, in turn, in view or not, so its shaders and geometry are ready before that.
        for (int n = 0; n < RecheckPerFrame && _nodes.Count > 0; n++)
        {
            _recheckAt = (_recheckAt + 1) % _nodes.Count;
            MeshInstance3D again = _nodes[_recheckAt];
            if (GodotObject.IsInstanceValid(again)) Prepare(again, uploads, warm);
            if (_recheckAt < _seen.Count) _seen[_recheckAt] = default;
        }

        while (_seen.Count < _nodes.Count) _seen.Add(default);

        if (s_dump is not null && ++_dumpFrame == 300) Dump(root);
        for (int at = 0; at < _nodes.Count; at++)
        {
            MeshInstance3D node = _nodes[at];
            if (!GodotObject.IsInstanceValid(node)) continue;
            (Aabb known, byte state) = _seen[at];
            if ((state & 1) != 0)
            {
                // Most of the scene's meshes are out of view in any frame: those are left with two reads.
                uint layers = node.Layers;
                if ((layers & (RenderLayers.World | RenderLayers.FirstPerson | RenderLayers.Effects | RenderLayers.StaticWorld)) != 0)
                {
                    Transform3D placed = node.GlobalTransform;
                    if ((state & 2) != 0) placed = new Transform3D(view, placed.Origin);
                    Aabb seen = placed * known;
                    if (!inside(planes, (state & 4) != 0 ? seen.Grow(1.5f) : seen)) continue;
                }
                else if ((layers & RenderLayers.Sky) == 0)
                {
                    continue;
                }
            }

            // The node's mesh object from the last look, while the node still shows the same mesh resource (its RID is
            // one plain read; the object is a lookup). Its surfaces and its box are read every time: a mesh can be
            // rebuilt in place.
            Rid shown = node.GetBase();
            if (!_meshOf.TryGetValue(node, out (Rid Base, Mesh Mesh) held) || held.Base != shown || !shown.IsValid)
            {
                if (node.Mesh is not { } fresh) continue;
                held = (shown, fresh);
                _meshOf[node] = held;
            }

            Mesh mesh = held.Mesh;
            if (!node.IsVisibleInTree()) continue;

            // Actors, characters and the first-person model. Effects (their own shaders) and a body hidden from its
            // own eyes (the shadow-only layer) are not this pass's.
            // Template materials on the effects layer too (light sprites); the effects' own shaders are not described.
            // Of the map's merged meshes, those the world pass could not take (its own are on its own layers).
            bool sky = (node.Layers & (RenderLayers.World | RenderLayers.FirstPerson | RenderLayers.Effects | RenderLayers.StaticWorld)) == 0;
            if (sky && (node.Layers & RenderLayers.Sky) == 0) continue;
            SkinInfo? skin = null;
            if (node.Skin is not null)
            {
                if (!_skins.TryGetValue(node, out skin))
                {
                    skin = SkinOf(node);
                    _skins[node] = skin;
                }

                if (skin is null || !GodotObject.IsInstanceValid(skin.Skeleton)) continue;
            }

            Transform3D model = node.GlobalTransform;
            // A sprite (the template's BILLBOARD) faces the camera whatever its node's own basis: turned here.
            bool turned = node.MaterialOverride is ShaderMaterial facing && IsBillboard(facing);
            if (turned) model = new Transform3D(view, model.Origin);
            // A posed mesh leaves its rest box: it is culled by a generous one.
            s_dynamic.TryGetValue(mesh, out DynamicSource? dynamic);
            Aabb local = mesh is ArrayMesh { CustomAabb.Size: var custom } own && custom != Vector3.Zero ? own.CustomAabb : mesh.GetAabb();
            // A mesh rewritten in place (cloth) has a new box every time: it is looked at in full every frame.
            _seen[at] = dynamic is null && !RdShadowPass.IsMutable(mesh) ? (local, (byte)(1 | (turned ? 2 : 0) | (skin is not null ? 4 : 0))) : default;
            Aabb box = model * local;
            if (!sky && !inside(planes, skin is null ? box : box.Grow(1.5f))) continue;
            if (dynamic is not null) dynamic.SeenFrame = Engine.GetProcessFrames();
            int objectIndex = -1, paletteBase = -1;
            int spotBits = spots(skin is null ? box : box.Grow(1.5f));
            int flares = FlaresReaching(skin is null ? box : box.Grow(1.5f));
            for (int s = 0; s < mesh.GetSurfaceCount(); s++)
            {
                if ((node.MaterialOverride ?? node.GetSurfaceOverrideMaterial(s) ?? mesh.SurfaceGetMaterial(s)) is not ShaderMaterial material) continue;
                if (material is EffectMaterial effect)
                {
                    // A surface mark has neither heat nor field strength (svm_effect.gdshaderinc).
                    if (skin is null && !sky && !(vision && effect.HiddenInVision)) GatherEffect(mesh, s, effect, node.GlobalTransform, box, view, eye, draws, objects, uploads);
                    // A weapon beam's thermal and EMF pass follows its additive look.
                    if (!vision || sky || effect.NextPass is not ShaderMaterial after || after is EffectMaterial) continue;
                    material = after;
                }

                Known(material);
                if (!_materials.TryGetValue(material, out (int Kind, bool Masked, bool TwoSided, bool Drawn, Rid Texture, int Blend) m))
                {
                    m = Describe(material);
                    _materials[material] = m;
                    _wantsTemplate[material] = WantsTemplate(material);
                }

                // The sky zone's meshes are drawn by the small programs (the template's sky code is for Godot's sky camera).
                if (RdWorldRenderer.Stats && !sky && _wantsTemplate.GetValueOrDefault(material) && !_templates.ContainsKey((material, skin is not null)))
                {
                    GD.Print($"SVM_RD_FRAME template in a draw for {node.GetPath()} surface {s} at process frame {Engine.GetProcessFrames()}");
                }

                RdMaterial? template = vision ? (sky ? null : VisionTemplateOf(material, skin is not null))
                    : !sky && _wantsTemplate.GetValueOrDefault(material) ? TemplateOf(material, skin is not null) : null;
                // Thermal and EMF have no small program: what the template cannot draw is left out.
                if (template is null && (vision || !m.Drawn)) continue;
                if (!_surfaces.TryGetValue((mesh, s), out SurfaceInfo? surface))
                {
                    surface = new SurfaceInfo { Id = _nextSurface++ };
                    _surfaces[(mesh, s)] = surface;
                }

                if (dynamic is not null && skin is null && s == 0)
                {
                    // Cloth: its vertices again whenever they were rewritten since the last upload.
                    if (dynamic.Positions.Length == 0 || dynamic.Indices.Length < 3) continue;
                    if (surface.Version != dynamic.Version)
                    {
                        surface.Version = dynamic.Version;
                        surface.Uploaded = true;
                        uploads.Add(PackDynamic(dynamic, surface));
                    }
                }
                else if (!surface.Uploaded)
                {
                    surface.Uploaded = true;
                    if (Pack(mesh, s, surface.Id, skin is not null) is { } upload) uploads.Add(upload);
                    else surface.Id = -1;
                }

                if (surface.Id < 0) continue;
                if (objectIndex < 0)
                {
                    objectIndex = objects.Count / ObjectBytes;
                    block.Clear();
                    if (skin is null)
                    {
                        Basis b = model.Basis;
                        (block[0], block[1], block[2]) = (b.X.X, b.X.Y, b.X.Z);
                        (block[4], block[5], block[6]) = (b.Y.X, b.Y.Y, b.Y.Z);
                        (block[8], block[9], block[10]) = (b.Z.X, b.Z.Y, b.Z.Z);
                        (block[12], block[13], block[14], block[15]) = (model.Origin.X, model.Origin.Y, model.Origin.Z, 1f);
                    }
                    else
                    {
                        // The bone matrices place the vertices in the world themselves.
                        block[0] = block[5] = block[10] = block[15] = 1f;
                        paletteBase = palette.Count / 16;
                        Transform3D skeleton = skin.Skeleton.GlobalTransform;
                        Transform3D[] poses = RdSkeletonPoses.Of(skin.Skeleton);
                        // Written in place: a list of sixteen floats a bone would be an array a bone, every frame.
                        int first = palette.Count;
                        CollectionsMarshal.SetCount(palette, first + (skin.Bones.Length * 16));
                        Span<float> rows = CollectionsMarshal.AsSpan(palette)[first..];
                        for (int i = 0; i < skin.Bones.Length; i++)
                        {
                            Transform3D bone = skin.Bones[i] >= 0 && skin.Bones[i] < poses.Length ? skeleton * poses[skin.Bones[i]] * skin.Binds[i] : model;
                            Basis b = bone.Basis;
                            Span<float> r = rows.Slice(i * 16, 16);
                            (r[0], r[1], r[2], r[3]) = (b.X.X, b.X.Y, b.X.Z, 0f);
                            (r[4], r[5], r[6], r[7]) = (b.Y.X, b.Y.Y, b.Y.Z, 0f);
                            (r[8], r[9], r[10], r[11]) = (b.Z.X, b.Z.Y, b.Z.Z, 0f);
                            (r[12], r[13], r[14], r[15]) = (bone.Origin.X, bone.Origin.Y, bone.Origin.Z, 1f);
                        }
                    }

                    // Every surface of the node shares the block: the lights and the cosmetics are the node's.
                    {
                        (block[76], block[77], block[78], block[79]) = (0f, 1f, 0f, 1f);
                        Put(block, 76, OpenScctGame.Render.InstanceParams.Get(node, s_visionActor));
                        Variant tint = OpenScctGame.Render.InstanceParams.Get(node, s_tint);
                        Put(block, 68, tint);
                        Put(block, 72, OpenScctGame.Render.InstanceParams.Get(node, s_glowHue));
                        if (tint.VariantType == Variant.Type.Nil) (block[68], block[69], block[70]) = (1f, 1f, 1f);
                        if (ActorLighting.TryGetApplied(node, out Vector4 ambient, out PackedLight[] lights) && lights.Length >= 4)
                        {
                            // What ActorLighting last wrote onto the node, from its own record: thirteen reads fewer.
                            (block[16], block[17], block[18], block[19]) = (ambient.X, ambient.Y, ambient.Z, ambient.W);
                            for (int i = 0; i < 4; i++)
                            {
                                PackedLight l = lights[i];
                                int to = 20 + (i * 12);
                                (block[to], block[to + 1], block[to + 2], block[to + 3]) = (l.Pos.X, l.Pos.Y, l.Pos.Z, l.Pos.W);
                                (block[to + 4], block[to + 5], block[to + 6], block[to + 7]) = (l.Col.X, l.Col.Y, l.Col.Z, l.Col.W);
                                (block[to + 8], block[to + 9], block[to + 10], block[to + 11]) = (l.Spot.X, l.Spot.Y, l.Spot.Z, l.Spot.W);
                            }
                        }
                        else
                        {
                            Put(block, 16, OpenScctGame.Render.InstanceParams.Get(node, s_ambient));
                            for (int i = 0; i < 4; i++)
                            {
                                Put(block, 20 + (i * 12), OpenScctGame.Render.InstanceParams.Get(node, s_pos[i]));
                                Put(block, 24 + (i * 12), OpenScctGame.Render.InstanceParams.Get(node, s_col[i]));
                                Put(block, 28 + (i * 12), OpenScctGame.Render.InstanceParams.Get(node, s_spot[i]));
                            }
                        }
                    }

                    objects.AddRange(MemoryMarshal.AsBytes(block));
                }

                draws.Add(new Draw
                {
                    Surface = surface.Id, Object = objectIndex, Kind = m.Kind | (sky ? 128 : 0), Palette = paletteBase, Mirrored = skin is null && model.Basis.Determinant() < 0f, Distance = eye.DistanceSquaredTo(box.GetCenter()), Masked = m.Masked, TwoSided = m.TwoSided, Texture = m.Texture,
                    Blend = template is null ? m.Blend : template.Cloak ? 3 : template.Blended ? 1 : 0, SpotBits = vision || sky || (m.Kind == 0 && template is null) ? 0 : spotBits, Material = template,
                    Flares = vision || sky || m.Kind == 0 || template is not null ? 0 : flares,
                });
                // The blended pass of a partly see-through surface, over what lies behind it.
                if (vision && material.NextPass is ShaderMaterial next && VisionTemplateOf(next, skin is not null) is { } over)
                {
                    draws.Add(draws[^1] with { Blend = 1, Material = over });
                }
            }
        }
    }

    /// <summary>The kind of an unlit effect surface (<see cref="EffectMaterial"/>): its colour and UV placement ride in the object's block.</summary>
    public const int KindEffect = 3;

    private readonly Dictionary<EffectMaterial, (Texture2D? Source, Rid Texture)> _effectTextures = [];

    /// <summary>How an effect material blends here: 0 solid, 1 by its alpha, 2 added; -1 when the own frame does not draw it.</summary>
    private static int EffectBlend(EffectMaterial effect)
    {
        // Multiplied and subtracted effects, those drawn over everything and inside-out ones are left out.
        if (effect.NoDepthTest || effect.CullMode == BaseMaterial3D.CullModeEnum.Front) return -1;
        return effect.BlendMode switch
        {
            BaseMaterial3D.BlendModeEnum.Add => 2,
            BaseMaterial3D.BlendModeEnum.Mix => effect.Transparency == BaseMaterial3D.TransparencyEnum.Alpha ? 1 : 0,
            _ => -1,
        };
    }

    private SurfaceInfo SurfaceReady(Mesh mesh, int s, List<Upload> uploads)
    {
        if (!_surfaces.TryGetValue((mesh, s), out SurfaceInfo? surface))
        {
            surface = new SurfaceInfo { Id = _nextSurface++ };
            _surfaces[(mesh, s)] = surface;
        }

        if (!surface.Uploaded)
        {
            surface.Uploaded = true;
            if (Pack(mesh, s, surface.Id, false) is { } upload) uploads.Add(upload);
            else surface.Id = -1;
        }

        return surface;
    }

    /// <summary>
    /// One surface with an <see cref="EffectMaterial"/> (a weapon laser's beam, a muzzle glow, a tracer), as
    /// svm_effect.gdshaderinc draws it: its texture times its colour, unlit, blended as the material says. The
    /// material's values are read every frame (a beam's texture slides), into a block of the surface's own.
    /// </summary>
    private void GatherEffect(Mesh mesh, int s, EffectMaterial effect, Transform3D model, Aabb box, Basis view, Vector3 eye, List<Draw> draws, List<byte> objects, List<Upload> uploads)
    {
        int blend = EffectBlend(effect);
        Color colour = effect.AlbedoColor;
        if (blend < 0 || (blend != 0 && colour.A <= 0f)) return;
        SurfaceInfo surface = SurfaceReady(mesh, s, uploads);
        if (surface.Id < 0) return;
        if (effect.BillboardMode == BaseMaterial3D.BillboardModeEnum.Enabled)
        {
            // Turned to the camera; a fixed size grows with its depth, as the shader's vertex stage does it.
            float size = effect.FixedSize ? (model.Origin - eye).Dot(-view.Z) : 1f;
            model = new Transform3D(new Basis(view.X * size, view.Y * size, view.Z * size), model.Origin);
        }

        Texture2D? source = effect.AlbedoTexture;
        if (!_effectTextures.TryGetValue(effect, out (Texture2D? Source, Rid Texture) texture) || !ReferenceEquals(texture.Source, source))
        {
            texture = (source, source is null ? default : RenderingServer.TextureGetRdTexture(source.GetRid(), false));
            _effectTextures[effect] = texture;
        }

        Span<float> block = stackalloc float[ObjectBytes / 4];
        block.Clear();
        Basis b = model.Basis;
        (block[0], block[1], block[2]) = (b.X.X, b.X.Y, b.X.Z);
        (block[4], block[5], block[6]) = (b.Y.X, b.Y.Y, b.Y.Z);
        (block[8], block[9], block[10]) = (b.Z.X, b.Z.Y, b.Z.Z);
        (block[12], block[13], block[14], block[15]) = (model.Origin.X, model.Origin.Y, model.Origin.Z, 1f);
        (block[16], block[17], block[18], block[19]) = (colour.R, colour.G, colour.B, colour.A);
        (block[20], block[21], block[22], block[23]) = (effect.Uv1Scale.X, effect.Uv1Scale.Y, effect.Uv1Offset.X, effect.Uv1Offset.Y);
        block[24] = effect.VertexColorUseAsAlbedo ? 1f : 0f;
        (block[68], block[69], block[70]) = (1f, 1f, 1f);
        int objectIndex = objects.Count / ObjectBytes;
        objects.AddRange(MemoryMarshal.AsBytes(block));
        draws.Add(new Draw
        {
            Surface = surface.Id, Object = objectIndex, Kind = KindEffect, Palette = -1, Mirrored = b.Determinant() < 0f, Distance = eye.DistanceSquaredTo(box.GetCenter()),
            TwoSided = effect.CullMode == BaseMaterial3D.CullModeEnum.Disabled, Texture = texture.Texture, Blend = blend,
        });
    }

    /// <summary>The bones of a skinned mesh's binds in its skeleton, or null when it has no skeleton to read.</summary>
    private static SkinInfo? SkinOf(MeshInstance3D node)
    {
        if (node.Skin is not { } skin || node.GetNodeOrNull(node.Skeleton) is not Skeleton3D skeleton) return null;
        int count = skin.GetBindCount();
        var bones = new int[count];
        var binds = new Transform3D[count];
        for (int i = 0; i < count; i++)
        {
            int bone = skin.GetBindBone(i);
            if (bone < 0 && skin.GetBindName(i) is { } name && !name.IsEmpty) bone = skeleton.FindBone(name);
            bones[i] = bone;
            binds[i] = skin.GetBindPose(i);
        }

        return new SkinInfo { Skeleton = skeleton, Bones = bones, Binds = binds };
    }

    private static void Put(Span<float> block, int at, Variant value)
    {
        if (value.VariantType == Variant.Type.Nil) return;
        Vector4 v = value.VariantType == Variant.Type.Color
            ? new Vector4(value.AsColor().R, value.AsColor().G, value.AsColor().B, value.AsColor().A) : value.AsVector4();
        (block[at], block[at + 1], block[at + 2], block[at + 3]) = (v.X, v.Y, v.Z, v.W);
    }

    /// <summary>How a template material draws here: 0 fullbright, 1 baked vertex colour, 2 actor light; not drawn when blended.</summary>
    private static readonly string[] s_layers =
    [
        "AUTHORED_MATERIAL", "SELF_ILLUM", "SI_LAYER", "SPEC_LAYER", "OPACITY_LAYER", "NORMAL_MAP", "SEQUENCE_D", "SEQUENCE_SI", "SEQUENCE_SIM",
        "COMBINE", "UV_ANIM", "FLIPBOOK", "ENV_COLOR", "BILLBOARD", "SVM_COSMETIC",
    ];

    /// <summary>The template features of a material's code that the own frame's small shaders leave out ("": none).</summary>
    public static string LayersOf(string code)
    {
        string found = "";
        foreach (string name in s_layers)
        {
            int at = code.IndexOf("#define " + name, StringComparison.Ordinal);
            if (at < 0) continue;
            int end = at + 8 + name.Length;
            if (end < code.Length && (char.IsLetterOrDigit(code[end]) || code[end] == '_')) continue;
            found += (found.Length > 0 ? "+" : "") + name;
        }

        return found;
    }

    /// <summary>
    /// A template material the small actor shaders cannot draw as it is: layers, cosmetics, a multiplied blend or a
    /// cloak (it reads the frame behind it). A surface only thermal and EMF show is not drawn here.
    /// </summary>
    private static bool WantsTemplate(ShaderMaterial material)
    {
        if (material.Shader is not { } shader) return false;
        string code = ResponsiveSmokeShaders.BaseCode(shader);
        if (!code.Contains(TemplateInclude, StringComparison.Ordinal) || code.Contains("#define VISION_SEE_THROUGH", StringComparison.Ordinal)) return false;
        // A sprite with nothing else is turned to the camera by its model matrix and drawn by the small programs.
        string layers = LayersOf(code);
        // A lightmapped map surface left to this pass needs the template's second UV and light term.
        return (layers.Length > 0 && layers != "BILLBOARD") || code.Contains("#define SVM_CLOAK", StringComparison.Ordinal) || code.Contains("#define LIGHT_LIGHTMAP", StringComparison.Ordinal) ||
            // Every blended surface: the template's own alpha, gain and fog rules for each blend.
            code.Contains("#define BLEND_TRANSLUCENT", StringComparison.Ordinal) || code.Contains("#define BLEND_ADDITIVE", StringComparison.Ordinal) || code.Contains("#define BLEND_MODULATE", StringComparison.Ordinal) || code.Contains("#define BLEND_DARKEN", StringComparison.Ordinal);
    }

    private readonly Dictionary<ShaderMaterial, bool> _billboards = [];
    private readonly Dictionary<ShaderMaterial, Shader?> _shaders = [];

    /// <summary>
    /// A material whose shader was replaced since it was described (a material sequence switching its blend mode, a
    /// cloak put on or taken off in place) is described again.
    /// </summary>
    private void Known(ShaderMaterial material)
    {
        Shader? now = material.Shader;
        if (!_shaders.TryGetValue(material, out Shader? was))
        {
            _shaders[material] = now;
            return;
        }

        if (ReferenceEquals(was, now)) return;
        _shaders[material] = now;
        _materials.Remove(material);
        _wantsTemplate.Remove(material);
        _billboards.Remove(material);
        _templates.Remove((material, false));
        _templates.Remove((material, true));
        _visionTemplates.Remove((material, false));
        _visionTemplates.Remove((material, true));
        _refresh.RemoveAll(entry => ReferenceEquals(entry.Source, material));
    }

    private bool IsBillboard(ShaderMaterial material)
    {
        if (_billboards.TryGetValue(material, out bool billboard)) return billboard;
        billboard = material.Shader is { } shader && ResponsiveSmokeShaders.BaseCode(shader).Contains("#define BILLBOARD", StringComparison.Ordinal);
        _billboards[material] = billboard;
        return billboard;
    }

    /// <summary>Statistics: the actor materials met so far, by what they hold (materials and whether they are drawn).</summary>
    public static readonly Dictionary<string, int> Met = [];

    private static (int Kind, bool Masked, bool TwoSided, bool Drawn, Rid Texture, int Blend) Describe(ShaderMaterial material)
    {
        if (material.Shader is not { } shader) return default;
        string code = ResponsiveSmokeShaders.BaseCode(shader);
        string met = (code.Contains(TemplateInclude, StringComparison.Ordinal) ? "world" : shader.ResourcePath) + " " + LayersOf(code)
            + (code.Contains("#define SVM_CLOAK", StringComparison.Ordinal) ? " CLOAK" : "") + (code.Contains("#define BLEND_MODULATE", StringComparison.Ordinal) ? " MODULATE" : "")
            + (code.Contains("#define BLEND_DARKEN", StringComparison.Ordinal) ? " DARKEN" : "") + (code.Contains("#define BLEND_ADDITIVE", StringComparison.Ordinal) ? " ADD" : "")
            + (code.Contains("#define BLEND_TRANSLUCENT", StringComparison.Ordinal) ? " TRANSLUCENT" : "") + (code.Contains("#define LIGHT_ACTOR", StringComparison.Ordinal) ? " actor" : "");
        Met[met] = Met.GetValueOrDefault(met) + 1;
        // Translucent and additive surfaces are blended by the texture's alpha; multiplied ones are not drawn yet.
        bool blended = code.Contains("#define BLEND_MODULATE", StringComparison.Ordinal) || code.Contains("#define BLEND_DARKEN", StringComparison.Ordinal);
        int blend = code.Contains("#define BLEND_ADDITIVE", StringComparison.Ordinal) ? 2 : code.Contains("#define BLEND_TRANSLUCENT", StringComparison.Ordinal) ? 1 : 0;
        // A cloaked body is not drawn at all for now: unseen is nearer the truth than plainly visible.
        if (blended || code.Contains("#define SVM_CLOAK", StringComparison.Ordinal) || code.Contains("#define VISION_SEE_THROUGH", StringComparison.Ordinal)
            || !code.Contains(TemplateInclude, StringComparison.Ordinal)) return default;
        int kind = code.Contains("#define LIGHT_ACTOR", StringComparison.Ordinal) ? 2
            : code.Contains("#define LIGHT_VERTEX", StringComparison.Ordinal) || code.Contains("#define LIGHT_LIGHTMAP", StringComparison.Ordinal) ? 1 : 0;
        Rid texture = material.GetShaderParameter(s_diffuse).AsGodotObject() is Texture2D t ? RenderingServer.TextureGetRdTexture(t.GetRid(), false) : default;
        return (kind, code.Contains("#define BLEND_MASKED", StringComparison.Ordinal), code.Contains("cull_disabled", StringComparison.Ordinal), true, texture, blend);
    }

    private static Upload? Pack(Mesh mesh, int surface, int id, bool skinned)
    {
        Godot.Collections.Array arrays = mesh.SurfaceGetArrays(surface);
        Vector3[] positions = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        if (positions.Length == 0) return null;
        Vector3[] normals = arrays[(int)Mesh.ArrayType.Normal].VariantType == Variant.Type.Nil ? [] : arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
        Vector2[] uvs = arrays[(int)Mesh.ArrayType.TexUV].VariantType == Variant.Type.Nil ? [] : arrays[(int)Mesh.ArrayType.TexUV].AsVector2Array();
        Color[] colors = arrays[(int)Mesh.ArrayType.Color].VariantType == Variant.Type.Nil ? [] : arrays[(int)Mesh.ArrayType.Color].AsColorArray();
        Vector2[] uv2s = skinned || arrays[(int)Mesh.ArrayType.TexUV2].VariantType == Variant.Type.Nil ? [] : arrays[(int)Mesh.ArrayType.TexUV2].AsVector2Array();
        int[] indices = arrays[(int)Mesh.ArrayType.Index].VariantType == Variant.Type.Nil ? [] : arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
        int[] bones = [];
        float[] weights = [];
        int perVertex = 4;
        if (skinned)
        {
            if (arrays[(int)Mesh.ArrayType.Bones].VariantType == Variant.Type.Nil || arrays[(int)Mesh.ArrayType.Weights].VariantType == Variant.Type.Nil) return null;
            bones = arrays[(int)Mesh.ArrayType.Bones].AsInt32Array();
            weights = arrays[(int)Mesh.ArrayType.Weights].AsFloat32Array();
            perVertex = bones.Length / positions.Length;
            if (perVertex is not (4 or 8) || weights.Length != bones.Length) return null;
        }

        int stride = skinned ? SkinnedStride : Stride;
        var bytes = new byte[positions.Length * stride];
        for (int i = 0; i < positions.Length; i++)
        {
            Span<byte> v = bytes.AsSpan(i * stride, stride);
            MemoryMarshal.Write(v, in positions[i]);
            Vector3 n = i < normals.Length ? normals[i] : Vector3.Up;
            MemoryMarshal.Write(v[12..], in n);
            Vector2 uv = i < uvs.Length ? uvs[i] : Vector2.Zero;
            MemoryMarshal.Write(v[24..], in uv);
            Color c = i < colors.Length ? colors[i] : Colors.White;
            v[32] = (byte)Math.Clamp((int)MathF.Round(c.R * 255f), 0, 255);
            v[33] = (byte)Math.Clamp((int)MathF.Round(c.G * 255f), 0, 255);
            v[34] = (byte)Math.Clamp((int)MathF.Round(c.B * 255f), 0, 255);
            // The alpha of a map surface's colour is its zone (255 where there is none).
            v[35] = (byte)Math.Clamp((int)MathF.Round(c.A * 255f), 0, 255);
            if (!skinned)
            {
                Vector2 uv2 = i < uv2s.Length ? uv2s[i] : Vector2.Zero;
                MemoryMarshal.Write(v[36..], in uv2);
                continue;
            }

            // The first four influences (the heaviest come first in a Godot mesh), renormalised.
            Span<ushort> b = MemoryMarshal.Cast<byte, ushort>(v.Slice(36, 8));
            Span<float> w = MemoryMarshal.Cast<byte, float>(v.Slice(44, 16));
            float sum = 0f;
            for (int k = 0; k < 4; k++)
            {
                // Within the slack the palette buffer keeps past its last matrix (RdWorldGpu.PaletteSlack): an index a
                // mesh holds beyond its skin's binds then reads inside the buffer, never past its end.
                b[k] = (ushort)Math.Clamp(bones[(i * perVertex) + k], 0, MaxBone);
                w[k] = weights[(i * perVertex) + k];
                sum += w[k];
            }

            if (sum > 1e-6f)
            {
                for (int k = 0; k < 4; k++) w[k] /= sum;
            }
            else
            {
                w[0] = 1f;
            }
        }

        uint[] index;
        if (indices.Length > 0)
        {
            index = new uint[indices.Length];
            for (int i = 0; i < indices.Length; i++) index[i] = (uint)indices[i];
        }
        else
        {
            index = new uint[positions.Length];
            for (int i = 0; i < index.Length; i++) index[i] = (uint)i;
        }

        return new Upload { Surface = id, Vertices = bytes, Indices = index, VertexCount = positions.Length, Skinned = skinned };
    }

    // ---- Render thread ----

    /// <summary>The frame's linear distance fog on a colour c, as the original's fog state: one colour, start and end.</summary>
    public const string Fog = """
            if (frame.fog_color.a > 0.5) {
                float cover = clamp((max(length(v_rel) * frame.fog.x, frame.fog.y) - frame.fog.y) * frame.fog.z, 0.0, 1.0);
                c = mix(c, frame.fog_color.rgb, cover);
            }

        """;

    private const string Common = """
        layout(set = 0, binding = 0, std140) uniform Frame
        {
            mat4 world;
            mat4 sky;
            vec4 params;
            // The eye (world, metres); the frame's distance fog: colour (a = 1 when on), then the distance scale to
            // the fog's units, its start and its rate.
            vec4 eye;
            vec4 fog_color;
            vec4 fog;
            // x: the light the vision mode adds to every lit surface (svm_vision_ambient).
            vec4 vision;
        } frame;
        struct Object
        {
            mat4 model;
            vec4 ambient;
            vec4 light[12];
            vec4 tint;
            vec4 glow_hue;
            // x HeatIntensity, y HeatOpacity, z EMF glow, w EMFOpacity (svm_vision_actor): read by the template in thermal and EMF.
            vec4 vision;
        };
        layout(set = 0, binding = 1, std430) readonly buffer Objects
        {
            Object objects[];
        };
        layout(push_constant, std430) uniform Draw
        {
            int object;
            int kind;
            int palette;
            // Bits 0-15: the spot slots that are on and reach the mesh; bits 16-31: those of them that are dynamic lights.
            int spot_bits;
        } draw;

        """;


    /// <summary>
    /// The spot shadow lights at a point of an actor: svm_spot_lookup, svm_spot_shadow and svm_spot_slot of
    /// svm_world.gdshaderinc, for the slots the draw names (those that are on and whose sphere reaches the mesh).
    /// </summary>
    /// <summary>The number of lit flares when the sphere of one reaches the box (they are few: a pixel then walks them all), else 0.</summary>
    private static int FlaresReaching(Aabb box)
    {
        IReadOnlyList<Vector4> flares = FlareLights.Reach;
        for (int i = 0; i < flares.Count; i++)
        {
            Vector4 f = flares[i];
            var centre = new Vector3(f.X, f.Y, f.Z);
            if (!(f.W >= 0f) || centre.DistanceSquaredTo(centre.Clamp(box.Position, box.End)) < f.W * f.W) return Math.Min(flares.Count, 15);
        }

        return 0;
    }

    private const string Spot = """
        layout(set = 2, binding = 0) uniform sampler2D svm_spots;
        layout(set = 2, binding = 1) uniform sampler2D svm_spot_depth;
        layout(set = 2, binding = 2) uniform sampler2DArray svm_spot_cookies;
        layout(set = 2, binding = 3) uniform sampler2D svm_spot_atten;
        layout(set = 2, binding = 4) uniform sampler2D svm_spot_ignore;
        layout(location = 3) in vec3 v_wpos;
        layout(location = 4) in vec3 v_wn;

        float spot_lookup(vec4 tile, vec2 suv, float zref)
        {
            vec2 atlas = vec2(textureSize(svm_spot_depth, 0));
            vec2 lo = tile.xy * atlas + 0.5;
            vec2 hi = (tile.xy + tile.zw) * atlas - 1.5;
            vec2 p = clamp((tile.xy + suv * tile.zw) * atlas - 0.5, lo, max(lo, hi));
            vec2 base = floor(p);
            vec2 f = p - base;
            vec4 d = textureGather(svm_spot_depth, (base + 1.0) / atlas, 0);
            vec4 lit = step(vec4(zref), d);
            return mix(mix(lit.w, lit.z, f.x), mix(lit.x, lit.y, f.x), f.y);
        }

        float spot_shadow(vec4 tile, vec2 suv, float zref, float soft)
        {
            if (soft <= 0.0) {
                return spot_lookup(tile, suv, zref);
            }
            vec2 step_uv = soft / vec2(textureSize(svm_spot_depth, 0)) / tile.zw;
            float sum = 0.0;
            for (int k = 0; k < 8; k++) {
                float a = float(k) * 0.78539816 + 0.39269908;
                sum += spot_lookup(tile, suv + vec2(cos(a), sin(a)) * step_uv, zref);
            }
            return sum * 0.125;
        }

        vec3 spot_slot(int i, vec3 wp, vec3 wn, int ignore)
        {
            vec4 s = texelFetch(svm_spots, ivec2(8, i), 0);
            vec3 ds = wp - s.xyz;
            if (dot(ds, ds) >= s.w) {
                return vec3(0.0);
            }
            vec4 a = texelFetch(svm_spots, ivec2(3, i), 0);
            if (a.x < 0.5 || (ignore & (a.x > 1.5 ? 2 : 1)) != 0) {
                return vec3(0.0);
            }
            int group = ignore >> 2;
            if (group > 0 && group < textureSize(svm_spot_ignore, 0).y && texelFetch(svm_spot_ignore, ivec2(i, group), 0).r > 0.5) {
                return vec3(0.0);
            }
            vec4 e = texelFetch(svm_spots, ivec2(7, i), 0);
            vec4 r0 = texelFetch(svm_spots, ivec2(0, i), 0);
            vec4 r1 = texelFetch(svm_spots, ivec2(1, i), 0);
            vec4 r2 = texelFetch(svm_spots, ivec2(2, i), 0);
            vec4 w = vec4(wp, 1.0);
            vec3 v = vec3(dot(r0, w), dot(r1, w), dot(r2, w));
            float z = -v.z;
            vec4 c = texelFetch(svm_spots, ivec2(4, i), 0);
            if (z <= c.a || z >= c.a + a.y) {
                return vec3(0.0);
            }
            vec4 f = texelFetch(svm_spots, ivec2(5, i), 0);
            vec2 ndc = f.z > 0.5 ? v.xy * f.xy : v.xy * f.xy / z;
            if (abs(ndc.x) >= 1.0 || abs(ndc.y) >= 1.0) {
                return vec3(0.0);
            }
            vec2 suv = vec2(ndc.x, -ndc.y) * 0.5 + 0.5;
            float shadow = spot_shadow(texelFetch(svm_spots, ivec2(6, i), 0), suv, z - e.y - e.z * z, e.w);
            if (shadow <= 0.0) {
                return vec3(0.0);
            }
            vec3 cookie = texture(svm_spot_cookies, vec3(suv, f.w)).rgb;
            vec2 aw = vec2(textureSize(svm_spot_atten, 0));
            float at = (z - c.a) / a.y;
            float au = clamp(at, 0.5 / aw.x, 1.0 - 0.5 / aw.x);
            float atten = a.w < 0.5 ? 1.0
                : texture(svm_spot_atten, vec2(au, a.w < 1.5 ? 0.25 : 0.75)).r * clamp((1.0 - at) * aw.x, 0.0, 1.0);
            vec3 nv = vec3(dot(r0.xyz, wn), dot(r1.xyz, wn), dot(r2.xyz, wn));
            vec3 l = f.z > 0.5 ? vec3(0.0, 0.0, 1.0) : normalize(-v);
            float ndl = max(0.0, dot(nv, l));
            return c.rgb * cookie * atten * mix(1.0, ndl, a.z) * shadow;
        }

        // The burning flares (svm_one_light with falloff 0, svm_flare_lookup, svm_flare_shadow and svm_flare_light of
        // svm_world.gdshaderinc, as the template's LIGHT_ACTOR adds them): the draw's kind holds their count.
        layout(set = 2, binding = 5) uniform sampler2D svm_flares;
        layout(set = 2, binding = 6) uniform sampler2D svm_flare_depth;

        vec3 one_light(vec4 pos, vec4 col, vec4 spot, vec3 wp, vec3 wn)
        {
            if (dot(col.rgb, col.rgb) <= 0.0) {
                return vec3(0.0);
            }
            vec3 l;
            float att = 1.0;
            float ambient = 0.0;
            if (pos.w < 0.0) {
                l = normalize(pos.xyz);
            } else {
                vec3 d = pos.xyz - wp;
                float dist = length(d);
                l = d / max(dist, 1e-5);
                float x = dist / max(pos.w, 1e-5);
                if (x >= 1.0) {
                    att = 0.0;
                } else if (spot.w < -2.5 && spot.w > -3.5) {
                    att = 0.5 / max(8.0 * x * x, 1e-3);
                } else if (spot.w < -3.5) {
                    att = 1.0 - 3.0 * x * x + 2.0 * x * x * x;
                    ambient = att;
                } else {
                    att = 1.0 - 3.0 * x * x + 2.0 * x * x * x;
                }
                if (spot.w > -1.5) {
                    float c = dot(-l, spot.xyz);
                    att *= clamp((c - spot.w) / max(col.a - spot.w, 1e-5), 0.0, 1.0);
                }
            }
            return col.rgb * (att * max(0.0, dot(wn, l)) + ambient);
        }

        float flare_lookup(vec2 t0, vec2 ts, vec2 suv, float zref)
        {
            vec2 atlas = vec2(textureSize(svm_flare_depth, 0));
            vec2 lo = t0 * atlas + 0.5;
            vec2 hi = (t0 + ts) * atlas - 1.5;
            vec2 p = clamp((t0 + suv * ts) * atlas - 0.5, lo, max(lo, hi));
            vec2 base = floor(p);
            vec2 f = p - base;
            vec4 d = textureGather(svm_flare_depth, (base + 1.0) / atlas, 0);
            vec4 lit = step(vec4(zref), d);
            return mix(mix(lit.w, lit.z, f.x), mix(lit.x, lit.y, f.x), f.y);
        }

        float flare_shadow(vec3 d, vec4 sh, vec4 ex)
        {
            vec3 a = abs(d);
            float face;
            vec3 fwd;
            vec3 up;
            if (a.x >= a.y && a.x >= a.z) {
                face = d.x > 0.0 ? 0.0 : 1.0;
                fwd = vec3(d.x > 0.0 ? 1.0 : -1.0, 0.0, 0.0);
                up = vec3(0.0, 1.0, 0.0);
            } else if (a.y >= a.z) {
                face = d.y > 0.0 ? 2.0 : 3.0;
                fwd = vec3(0.0, d.y > 0.0 ? 1.0 : -1.0, 0.0);
                up = vec3(0.0, 0.0, d.y > 0.0 ? -1.0 : 1.0);
            } else {
                face = d.z > 0.0 ? 4.0 : 5.0;
                fwd = vec3(0.0, 0.0, d.z > 0.0 ? 1.0 : -1.0);
                up = vec3(0.0, 1.0, 0.0);
            }
            float z = dot(d, fwd);
            if (z <= 1e-4) {
                return 1.0;
            }
            vec3 right = cross(fwd, up);
            vec2 ndc = vec2(dot(d, right), dot(d, up)) / z * ex.x;
            vec2 suv = vec2(ndc.x, -ndc.y) * 0.5 + 0.5;
            vec2 atlas = vec2(textureSize(svm_flare_depth, 0));
            vec2 ts = vec2(ex.y) / atlas;
            vec2 t0 = vec2(face, sh.x) * ts;
            float zref = z - sh.y - ex.z * z;
            vec2 step_uv = sh.w / ex.y * vec2(1.0);
            float sum = flare_lookup(t0, ts, suv, zref);
            for (int k = 0; k < 8; k++) {
                float an = float(k) * 0.78539816 + 0.39269908;
                sum += flare_lookup(t0, ts, suv + vec2(cos(an), sin(an)) * step_uv, zref);
            }
            return sum / 9.0;
        }

        vec3 flare_light(vec3 wp, vec3 wn)
        {
            int count = (draw.kind >> 16) & 15;
            vec3 sum = vec3(0.0);
            for (int i = 0; i < count; i++) {
                vec4 p = texelFetch(svm_flares, ivec2(0, i), 0);
                vec3 l = one_light(p, texelFetch(svm_flares, ivec2(1, i), 0), texelFetch(svm_flares, ivec2(2, i), 0), wp, wn);
                if (dot(l, l) <= 0.0) {
                    continue;
                }
                vec4 sh = texelFetch(svm_flares, ivec2(3, i), 0);
                if (sh.x >= 0.0) {
                    vec4 ex = texelFetch(svm_flares, ivec2(4, i), 0);
                    float offset = sh.z + 3.0 * 2.0 / ex.y * distance(wp, p.xyz);
                    l *= flare_shadow(wp + wn * offset - p.xyz, sh, ex);
                }
                sum += l;
            }
            return sum;
        }

        // An actor's zone is not known to its surface (the template passes -1: own-zone lights still reach it).
        vec3 spot_light(vec3 wp, vec3 wn, int ignore)
        {
            int on = draw.spot_bits & 65535;
            int dynamic_slots = (draw.spot_bits >> 16) & 65535;
            if ((ignore & 1) != 0) {
                on &= dynamic_slots;
            }
            if ((ignore & 2) != 0) {
                on &= ~dynamic_slots;
            }
            vec3 sum = vec3(0.0);
            while (on != 0) {
                int i = findLSB(on);
                on &= on - 1;
                sum += spot_slot(i, wp, wn, ignore);
            }
            return sum;
        }

        """;

    // svm_actor_light of svm_world.gdshaderinc, at the vertex as the template's LIGHT_ACTOR does it.
    public static string Vertex(bool skinned, bool spot) => "#version 450\n" + (skinned ? "#define SKINNED\n" : "") + (spot ? "#define SPOT\n" : "") + Common + """
        layout(location = 0) in vec3 a_pos;
        layout(location = 1) in vec3 a_normal;
        layout(location = 2) in vec2 a_uv;
        layout(location = 3) in vec4 a_col;
        #ifdef SKINNED
        layout(location = 4) in uvec4 a_bones;
        layout(location = 5) in vec4 a_weights;
        layout(set = 0, binding = 2, std430) readonly buffer Palette
        {
            mat4 bones[];
        };
        #endif
        layout(location = 0) out vec2 v_uv;
        layout(location = 1) out vec3 v_light;
        layout(location = 2) out vec3 v_rel;
        #ifdef SPOT
        layout(location = 3) out vec3 v_wpos;
        layout(location = 4) out vec3 v_wn;
        #endif
        layout(location = 5) out float v_alpha;

        vec3 actor_light(vec4 pos, vec4 col, vec4 spot, vec3 wp, vec3 wn)
        {
            if (dot(col.rgb, col.rgb) <= 0.0) return vec3(0.0);
            if (pos.w < 0.0) return col.rgb * max(0.0, dot(wn, normalize(pos.xyz)));
            vec3 d = pos.xyz - wp;
            float dist = length(d);
            vec3 l = d / max(dist, 1e-5);
            float att;
            if (spot.w < -2.5 && spot.w > -3.5) {
                float x = dist / max(pos.w, 1e-5);
                att = x >= 1.0 ? 0.0 : 1.0 / max(8.0 * x * x, 1e-3);
            } else {
                float denominator = col.w + pos.w * dist;
                float range = (256.0 - col.w) / max(1.0, pos.w);
                att = dist > range ? 0.0 : 1.0 / max(denominator, .01);
            }
            if (spot.w > -1.5) {
                float inner = cos(acos(clamp(spot.w, -1.0, 1.0)) * 0.05);
                att *= clamp((dot(-l, spot.xyz) - spot.w) / max(inner - spot.w, 1e-5), 0.0, 1.0);
            }
            return col.rgb * att * (max(0.0, dot(wn, l)) + (spot.w < -3.5 ? 1.0 : 0.0));
        }

        void main()
        {
        #ifdef SKINNED
            mat4 model = bones[draw.palette + int(a_bones.x)] * a_weights.x + bones[draw.palette + int(a_bones.y)] * a_weights.y
                + bones[draw.palette + int(a_bones.z)] * a_weights.z + bones[draw.palette + int(a_bones.w)] * a_weights.w;
        #else
            mat4 model = objects[draw.object].model;
        #endif
            vec4 wp = model * vec4(a_pos, 1.0);
            v_uv = a_uv;
            int kind = draw.kind & 127;
            vec3 wn = normalize(mat3(model) * a_normal);
        #ifdef SPOT
            v_wpos = wp.xyz;
            v_wn = wn;
        #endif
            if (kind == 2) {
                vec3 sum = objects[draw.object].ambient.rgb;
                for (int i = 0; i < 4; i++) {
                    sum += actor_light(objects[draw.object].light[i * 3], objects[draw.object].light[i * 3 + 1], objects[draw.object].light[i * 3 + 2], wp.xyz, wn);
                }
                v_light = min(sum + vec3(frame.vision.x), vec3(2.0));
            } else if (kind == 1) {
                v_light = min(a_col.rgb * 2.0 + vec3(frame.vision.x), vec3(2.0));
            } else {
                v_light = vec3(1.0);
            }
            v_alpha = 1.0;
            if (kind == 3) {
                // An unlit effect: its colour in the ambient's place, its UV scale and offset in the first light's.
                vec4 colour = objects[draw.object].ambient;
                if (objects[draw.object].light[1].x > 0.5) colour *= a_col;
                vec4 place = objects[draw.object].light[0];
                v_uv = a_uv * place.xy + place.zw;
                v_light = colour.rgb;
                v_alpha = colour.a;
            }
            // A mesh of the sky zone is seen from the sky origin, behind the world, and is not fogged here.
            bool sky = (draw.kind & 128) != 0;
            v_rel = sky ? vec3(0.0) : wp.xyz - frame.eye.xyz;
            gl_Position = (sky ? frame.sky : frame.world) * wp;
        }

        """;

    public static string Fragment(bool masked, bool spot) => "#version 450\n" + Common + (spot ? Spot : "") + """
        layout(set = 1, binding = 0) uniform sampler2D tex_diffuse;
        layout(location = 0) in vec2 v_uv;
        layout(location = 1) in vec3 v_light;
        layout(location = 2) in vec3 v_rel;
        layout(location = 5) in float v_alpha;
        layout(location = 0) out vec4 frag;

        void main()
        {
            vec4 t = texture(tex_diffuse, v_uv);

        """ + (masked ? "    if (t.a < 0.5) discard;\n" : "") + "    t.a *= v_alpha;\n"
        // The template: texture x min(actor light + spot lights, 2); the Echelon code rides in the ambient's w.
        // The flares add past the 2x cap, as in the template.
        + (spot ? "    vec3 wn = normalize(v_wn);\n    vec3 c = t.rgb * (min(v_light + spot_light(v_wpos, wn, int(objects[draw.object].ambient.w + 0.5)), vec3(2.0)) + flare_light(v_wpos, wn));\n"
            : "    vec3 c = t.rgb * v_light;\n") + """
            int blend = (draw.kind >> 8) & 255;
            if (blend == 2) {
                float cover = frame.fog_color.a > 0.5 ? clamp((max(length(v_rel) * frame.fog.x, frame.fog.y) - frame.fog.y) * frame.fog.z, 0.0, 1.0) : 0.0;
                frag = vec4(min(c * (1.0 - cover), vec3(1.0)), t.a);
                return;
            }

        """ + Fog + """
            frag = vec4(min(c, vec3(1.0)), blend == 0 ? 1.0 : t.a);
        }

        """;
}
