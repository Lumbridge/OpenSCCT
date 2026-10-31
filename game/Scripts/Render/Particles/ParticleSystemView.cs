using System;
using System.Collections.Generic;
using System.Text;
using Godot;
using OpenScct.Core;
using OpenScct.Provenance;

namespace OpenScctGame.Render.Particles;

/// <summary>Textures and meshes a particle view draws with, by the paths the converted content names.</summary>
public interface IParticleAssets
{
    /// <summary>The texture of a sub-emitter (its PNG path in the content), or null.</summary>
    Texture2D? Texture(string? png);

    /// <summary>The static mesh of a mesh emitter (its object path), or null.</summary>
    Mesh? Mesh(string? objectPath);
}

/// <summary>
/// Draws one <see cref="ParticleSystemSim"/>: per sub-emitter a mesh filled each frame from
/// <see cref="ParticleGeometry"/> (sprite and beam quads with their cells and vertex colours, spark lines) or a
/// MultiMesh of the emitter's static mesh, with the draw style's blending (svm_particle.gdshaderinc, spec/native/emitters.md
/// rule 21). Sub-emitters draw in array order. Sprite and beam quads are written into the surface in place
/// (<see cref="QuadBuffer"/>); the surface is rebuilt only when the quads outgrow it.
/// </summary>
[PortOf("eng:USpriteEmitter::RenderParticles", Spec = "spec/native/emitters.md#rules")]
public sealed class ParticleSystemView
{
    public const string ShaderPath = "res://Shaders/svm_particle.gdshaderinc";

    private static readonly Dictionary<string, Shader> s_shaders = [];

    private readonly List<Layer> _layers = [];
    private readonly List<SpriteQuad> _quads = [];
    private readonly List<SparkSegment> _segments = [];
    private readonly List<MeshInstance> _meshes = [];

    public ParticleSystemView(ParticleSystemSim sim, IParticleAssets assets, Node3D parent, string name, float heatOpacity = 1f)
    {
        Sim = sim ?? throw new ArgumentNullException(nameof(sim));
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(parent);
        Root = new Node3D { Name = name };
        parent.AddChild(Root);
        for (int i = 0; i < sim.Emitters.Count; i++)
        {
            ParticleEmitterSim e = sim.Emitters[i];
            ParticleParams p = e.Params;
            if (p.Kind == ParticleKind.Mesh)
            {
                Mesh? mesh = assets.Mesh(p.StaticMesh);
                var mm = new MultiMesh
                {
                    TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                    UseColors = true,
                    Mesh = mesh,
                    InstanceCount = Math.Max(1, p.MaxParticles),
                    VisibleInstanceCount = 0,
                };
                var mmNode = new MultiMeshInstance3D
                {
                    Name = $"{i}_{p.Name}",
                    Multimesh = mm,
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                    Layers = RenderLayers.Effects,
                    ExtraCullMargin = 64f,
                };
                if (!p.UseMeshBlendMode)
                {
                    mmNode.MaterialOverride = MaterialFor(p, assets, heatOpacity, i, true);
                }

                Root.AddChild(mmNode);
                _layers.Add(new Layer(e, null, null, mmNode, mm));
                MissingMeshes += mesh is null ? 1 : 0;
                HasLitMeshes |= mesh is not null && p.UseMeshBlendMode;
                continue;
            }

            var arrayMesh = new ArrayMesh();
            var node = new MeshInstance3D
            {
                Name = $"{i}_{p.Name}",
                Mesh = arrayMesh,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                Layers = RenderLayers.Effects,
                ExtraCullMargin = 64f,
                MaterialOverride = MaterialFor(p, assets, heatOpacity, i, false),
            };
            Root.AddChild(node);
            _layers.Add(new Layer(e, node, arrayMesh, null, null));
        }
    }

    public ParticleSystemSim Sim { get; }

    public Node3D Root { get; }

    /// <summary>Mesh emitters whose mesh the content does not have.</summary>
    public int MissingMeshes { get; }

    /// <summary>True when a mesh emitter draws its mesh's own materials (UseMeshBlendMode), which the owner's lights light.</summary>
    public bool HasLitMeshes { get; }

    /// <summary>
    /// Lights the meshes drawn with their own materials by the owner's light set (their mesh must be actor-lit, as
    /// the map's particle meshes are). The particle-style meshes (UseMeshBlendMode off) are unlit.
    /// </summary>
    public void LightMeshes(ActorLightSet set)
    {
        ArgumentNullException.ThrowIfNull(set);
        foreach (Layer layer in _layers)
        {
            if (layer.MultiNode is { } node && layer.Emitter.Params.UseMeshBlendMode)
            {
                ActorLighting.Apply(node, set);
            }
        }
    }

    /// <summary>Quads, segments and mesh instances drawn by the last <see cref="Draw"/>.</summary>
    public int Drawn { get; private set; }

    /// <summary>Sprite quads drawn by the last <see cref="Draw"/>, per sub-emitter.</summary>
    public IReadOnlyList<int> DrawnPerEmitter => _drawnPer;

    private readonly List<int> _drawnPer = [];

    /// <summary>Rebuilds every sub-emitter's geometry for the owner and camera of this frame.</summary>
    public void Draw(in ParticleOwner owner, in ParticleCamera camera, ReadOnlySpan<Plane> frustum = default)
    {
        int total = 0;
        _drawnPer.Clear();
        foreach (Layer layer in _layers)
        {
            ParticleEmitterSim e = layer.Emitter;
            int n = 0;
            if (e.Disabled)
            {
                Clear(layer);
                _drawnPer.Add(0);
                continue;
            }

            if (e.Params.Kind == ParticleKind.Sprite && !frustum.IsEmpty && !SpriteMayBeVisible(e, owner, frustum))
            {
                // Reject before computing camera-facing axes, spin and UV cells. The box contains every possible
                // orientation of every live sprite, so long streaks and particles away from their owner stay visible.
                layer.Show(false);
                _drawnPer.Add(0);
                continue;
            }

            switch (e.Params.Kind)
            {
                case ParticleKind.Mesh:
                    _meshes.Clear();
                    ParticleGeometry.Meshes(e, owner, _meshes);
                    n = FillMeshes(layer, _meshes);
                    break;
                case ParticleKind.Spark:
                    _segments.Clear();
                    ParticleGeometry.Sparks(e, owner, _segments);
                    n = FillLines(layer, _segments);
                    break;
                case ParticleKind.Beam:
                    _quads.Clear();
                    ParticleGeometry.Beams(e, camera, owner, _quads);
                    n = DrawQuads(layer, _quads, frustum);
                    break;
                default:
                    _quads.Clear();
                    ParticleGeometry.Sprites(e, camera, owner, _quads);
                    n = DrawQuads(layer, _quads, frustum);
                    break;
            }

            _drawnPer.Add(n);
            total += n;
        }

        Drawn = total;
    }

    private static bool SpriteMayBeVisible(ParticleEmitterSim emitter, in ParticleOwner owner, ReadOnlySpan<Plane> frustum)
    {
        Vector3 min = Vector3.One * float.PositiveInfinity, max = Vector3.One * float.NegativeInfinity;
        bool any = false;
        ReadOnlySpan<Particle> particles = emitter.Particles;
        int used = Math.Min(emitter.ActiveParticles, particles.Length);
        for (int i = 0; i < used; i++)
        {
            ref readonly Particle particle = ref particles[i];
            if (!particle.Alive) continue;
            Vector3 center = UeSpace.ToGodot(ParticleGeometry.World(emitter.Params, particle.Location, owner));
            float sx = MathF.Abs(particle.Size.X), sy = MathF.Abs(particle.Size.Y);
            Vector3 extent = Vector3.One * UeSpace.LengthToGodot(sx + Math.Max(sx, sy));
            min = min.Min(center - extent);
            max = max.Max(center + extent);
            any = true;
        }
        return any && ViewFrustum.Intersects(new Aabb(min, max - min), frustum);
    }

    public void Free()
    {
        Root.QueueFree();
    }

    private static void Clear(Layer layer)
    {
        if (layer.Quads is not null)
        {
            // The surface is kept for the next quads; hidden, it draws nothing.
            layer.Show(false);
        }
        else
        {
            layer.Mesh?.ClearSurfaces();
        }

        if (layer.Multi is { } mm)
        {
            mm.VisibleInstanceCount = 0;
        }
    }

    private static int DrawQuads(Layer layer, List<SpriteQuad> quads, ReadOnlySpan<Plane> frustum)
    {
        if (quads.Count > 0 && !frustum.IsEmpty && !ViewFrustum.Intersects(QuadBuffer.Bounds(quads), frustum))
        {
            // Simulation keeps advancing; only building and uploading invisible geometry is skipped. Re-entering
            // the view draws the current particles at once, including beams spanning several rooms.
            layer.Show(false);
            return 0;
        }

        return FillQuads(layer, quads);
    }

    private static int FillQuads(Layer layer, List<SpriteQuad> quads)
    {
        int n = quads.Count;
        if (n == 0)
        {
            Clear(layer);
            return 0;
        }

        if (layer.Quads is { } buffer && buffer.Fill(quads))
        {
            layer.Show(true);
            return n;
        }

        return FillQuadsRebuild(layer, quads);
    }

    /// <summary>
    /// Rebuilds the layer's surface from arrays: when the quads outgrow the surface, and as the fallback when the
    /// surface's layout is not the one <see cref="QuadBuffer"/> writes.
    /// </summary>
    private static int FillQuadsRebuild(Layer layer, List<SpriteQuad> quads)
    {
        ArrayMesh mesh = layer.Mesh!;
        mesh.ClearSurfaces();
        int n = quads.Count;
        int capacity = QuadBuffer.CapacityFor(n);
        layer.Ensure(capacity * 4, capacity * 6);
        Vector3[] v = layer.Vertices!;
        Vector2[] uv = layer.Uv!;
        Vector2[] uv2 = layer.Uv2!;
        Color[] c = layer.Colors!;
        int[] idx = layer.Indices!;
        Array.Clear(v);
        Array.Clear(uv);
        Array.Clear(uv2);
        Array.Clear(c);
        for (int i = 0; i < n; i++)
        {
            SpriteQuad q = quads[i];
            int at = i * 4;
            v[at] = UeSpace.ToGodot(q.C0);
            v[at + 1] = UeSpace.ToGodot(q.C1);
            v[at + 2] = UeSpace.ToGodot(q.C2);
            v[at + 3] = UeSpace.ToGodot(q.C3);
            uv[at] = new Vector2(q.U0, q.V1);
            uv[at + 1] = new Vector2(q.U1, q.V1);
            uv[at + 2] = new Vector2(q.U0, q.V0);
            uv[at + 3] = new Vector2(q.U1, q.V0);
            uv2[at] = new Vector2(q.BU0, q.BV1);
            uv2[at + 1] = new Vector2(q.BU1, q.BV1);
            uv2[at + 2] = new Vector2(q.BU0, q.BV0);
            uv2[at + 3] = new Vector2(q.BU1, q.BV0);
            var colour = new Color(q.Color.R / 255f, q.Color.G / 255f, q.Color.B / 255f, q.Color.A / 255f);
            c[at] = colour;
            c[at + 1] = colour;
            c[at + 2] = colour;
            c[at + 3] = colour;
        }

        // The index pattern covers the whole capacity; unused quads keep all four corners at one point, a triangle of
        // no area that draws nothing, so later frames only rewrite the vertex and attribute regions.
        for (int i = 0; i < capacity; i++)
        {
            int at = i * 4;
            int k = i * 6;
            idx[k] = at;
            idx[k + 1] = at + 1;
            idx[k + 2] = at + 2;
            idx[k + 3] = at + 2;
            idx[k + 4] = at + 1;
            idx[k + 5] = at + 3;
        }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = v.AsSpan(0, capacity * 4).ToArray();
        arrays[(int)Mesh.ArrayType.TexUV] = uv.AsSpan(0, capacity * 4).ToArray();
        arrays[(int)Mesh.ArrayType.TexUV2] = uv2.AsSpan(0, capacity * 4).ToArray();
        arrays[(int)Mesh.ArrayType.Color] = c.AsSpan(0, capacity * 4).ToArray();
        arrays[(int)Mesh.ArrayType.Index] = idx.AsSpan(0, capacity * 6).ToArray();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays, flags: Mesh.ArrayFormat.FlagUseDynamicUpdate);
        arrays.Dispose();
        layer.Quads = QuadBuffer.For(mesh, capacity, n);
        layer.Quads?.SetBounds(quads);
        layer.Show(true);
        return n;
    }

    private static int FillLines(Layer layer, List<SparkSegment> segments)
    {
        ArrayMesh mesh = layer.Mesh!;
        mesh.ClearSurfaces();
        int n = segments.Count;
        if (n == 0)
        {
            return 0;
        }

        var v = new Vector3[n * 2];
        var uv = new Vector2[n * 2];
        var c = new Color[n * 2];
        for (int i = 0; i < n; i++)
        {
            SparkSegment s = segments[i];
            var colour = new Color(s.Color.R / 255f, s.Color.G / 255f, s.Color.B / 255f, s.Color.A / 255f);
            v[i * 2] = UeSpace.ToGodot(s.From);
            v[(i * 2) + 1] = UeSpace.ToGodot(s.To);
            uv[i * 2] = new Vector2(0f, s.V0);
            uv[(i * 2) + 1] = new Vector2(1f, s.V1);
            c[i * 2] = colour;
            c[(i * 2) + 1] = colour;
        }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = v;
        arrays[(int)Mesh.ArrayType.TexUV] = uv;
        arrays[(int)Mesh.ArrayType.TexUV2] = uv;
        arrays[(int)Mesh.ArrayType.Color] = c;
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Lines, arrays);
        return n;
    }

    private static int FillMeshes(Layer layer, List<MeshInstance> meshes)
    {
        MultiMesh mm = layer.Multi!;
        int n = Math.Min(meshes.Count, mm.InstanceCount);
        for (int i = 0; i < n; i++)
        {
            MeshInstance m = meshes[i];
            mm.SetInstanceTransform(i, UeSpace.TransformToGodot(m.Location, m.Rotation, m.Scale, default));
            mm.SetInstanceColor(i, new Color(m.Color.R / 255f, m.Color.G / 255f, m.Color.B / 255f, m.Color.A / 255f));
        }

        mm.VisibleInstanceCount = n;
        return n;
    }

    private static ShaderMaterial MaterialFor(ParticleParams p, IParticleAssets assets, float heatOpacity, int order, bool mesh)
    {
        var material = new ShaderMaterial { Shader = ShaderFor(p.DrawStyle, p.ZWrite, p.ZTest, mesh) };
        if (assets.Texture(p.Texture) is { } texture)
        {
            material.SetShaderParameter("tex_sprite", texture);
        }

        material.SetShaderParameter("blend_cells", p.BlendBetweenSubdivisions && !p.UseRandomSubdivision);
        material.SetShaderParameter("alpha_ref", p.AlphaTest ? p.AlphaRef / 255f : -1f);
        material.SetShaderParameter("heat_opacity", heatOpacity);
        material.RenderPriority = Math.Clamp(order, -100, 100);
        return material;
    }

    /// <summary>The shader of a UE2 EParticleDrawStyle (0 Regular .. 6 Brighten).</summary>
    [Provenance(ProvenanceKind.Decoded, "FD3DRenderInterface::SetParticleMaterial 0x10f34260: blend factors per draw style")]
    [Provenance(ProvenanceKind.Estimated, "PTDS_Brighten (ONE, INVSRCCOLOR) has no Godot blend: drawn premultiplied with the brightest channel as coverage")]
    public static Shader ShaderFor(int drawStyle, bool zWrite, bool zTest, bool mesh)
    {
        string key = $"{drawStyle}{zWrite}{zTest}{mesh}";
        if (s_shaders.TryGetValue(key, out Shader? cached))
        {
            return cached;
        }

        Materials.MaterialCompiler.EnsureGlobals();
        (string define, string blend) = drawStyle switch
        {
            0 => ("STYLE_REGULAR", "blend_mix"),
            1 => ("STYLE_ALPHA", "blend_mix"),
            2 => ("STYLE_MODULATE", "blend_mul"),
            4 => ("STYLE_PREMUL", "blend_premul_alpha"),
            5 => ("STYLE_DARKEN", "blend_mul"),
            6 => ("STYLE_BRIGHTEN", "blend_premul_alpha"),
            _ => ("STYLE_ADD", "blend_add"),
        };
        var code = new StringBuilder();
        code.Append("shader_type spatial;\nrender_mode unshaded, fog_disabled, cull_disabled, ")
            .Append(blend).Append(zWrite || drawStyle == 0 ? ", depth_draw_opaque" : ", depth_draw_never")
            .Append(zTest ? "" : ", depth_test_disabled")
            .Append(";\n#define ").Append(define).Append('\n');
        if (mesh)
        {
            code.Append("#define PARTICLE_MESH\n");
        }

        code.Append("#include \"").Append(ShaderPath).Append("\"\n");
        var shader = new Shader { Code = code.ToString(), ResourceName = "svm_particle " + define };
        s_shaders[key] = shader;
        return shader;
    }

    private sealed class Layer(ParticleEmitterSim emitter, MeshInstance3D? node, ArrayMesh? mesh, MultiMeshInstance3D? multiNode, MultiMesh? multi)
    {
        public ParticleEmitterSim Emitter { get; } = emitter;

        public MeshInstance3D? Node { get; } = node;

        public ArrayMesh? Mesh { get; } = mesh;

        public MultiMeshInstance3D? MultiNode { get; } = multiNode;

        public MultiMesh? Multi { get; } = multi;

        public Vector3[]? Vertices { get; private set; }

        public Vector2[]? Uv { get; private set; }

        public Vector2[]? Uv2 { get; private set; }

        public Color[]? Colors { get; private set; }

        public int[]? Indices { get; private set; }

        /// <summary>The quad surface written in place each frame, once it is built (sprites and beams).</summary>
        public QuadBuffer? Quads { get; set; }

        private bool _shown = true;

        public void Show(bool shown)
        {
            if (_shown == shown) return;
            _shown = shown;
            Node!.Visible = shown;
        }

        public void Ensure(int vertices, int indices)
        {
            if (Vertices is null || Vertices.Length < vertices)
            {
                int size = Math.Max(vertices, (Vertices?.Length ?? 0) * 2);
                Vertices = new Vector3[size];
                Uv = new Vector2[size];
                Uv2 = new Vector2[size];
                Colors = new Color[size];
            }

            if (Indices is null || Indices.Length < indices)
            {
                Indices = new int[Math.Max(indices, (Indices?.Length ?? 0) * 2)];
            }
        }
    }
}
