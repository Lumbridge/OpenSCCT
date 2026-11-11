using System;
using System.Collections.Generic;
using Godot;
using OpenScctGame.Render.Materials;

namespace OpenScctGame.Render.Rd;

/// <summary>
/// Map projectors in the own frame (the options' Fast renderer), as the original draws them (docs/PERFORMANCE-LOWEND-2026-11.md,
/// "The states it draws with"): the world surfaces a projector's box reaches are drawn a second time, at the depth
/// they already have, with the projector's texture blended into the frame. The projection itself is that of
/// svm_projector.gdshaderinc, with the surface's own position in place of one rebuilt from the depth buffer.
/// Objects and characters do not receive projections yet.
/// </summary>
internal sealed class RdProjectors
{
    /// <summary>Floats of one projector's block: origin, x, y, z, shape, flip, uv rows, uv animation, kind.</summary>
    public const int BlockFloats = 44;

    public struct Draw
    {
        /// <summary>The projector's index among the frame's blocks, and the world draw (index into the frame's draws).</summary>
        public int Projector, WorldDraw;
    }

    private sealed class Info
    {
        public required MeshInstance3D Node;
        public required Aabb Box;
        public required float[] Block;
        public required Rid Texture;
        /// <summary>0 add, 1 multiply by twice the texture, 2 alpha blend.</summary>
        public required int Kind;
        public required bool SkipStaticMeshes;
        /// <summary>The frustum box's six faces, outward (xyz normal, w = -dot(normal, point)); empty when unknown.</summary>
        public required Vector4[] Planes;
    }

    private static readonly StringName[] s_vec4 = ["proj_origin", "proj_x", "proj_y", "proj_z", "proj_shape", "proj_flip", "uv_row_u", "uv_row_v", "uv_anim_a", "uv_anim_b"];
    private readonly List<Info> _projectors = [];
    private Node? _root;

    /// <summary>
    /// The projectors under <paramref name="root"/> whose box is in view, each with the frame's world draws its box
    /// reaches (main thread).
    /// </summary>
    public void Gather(Node? root, SceneTree? tree, Plane[] planes, Func<Plane[], Aabb, bool> inside, List<RdWorldGeometry.Batch> batches, int[] worldDraws, int worldDrawCount,
        List<Draw> draws, List<float> blocks, List<Rid> textures, List<int> kinds)
    {
        draws.Clear();
        blocks.Clear();
        textures.Clear();
        kinds.Clear();
        // The spy pistols' red dots: each is a small projector that moves every frame, so it is read again each time.
        if (tree is not null)
        {
            foreach (Node dot in tree.GetNodesInGroup(s_laserDots))
            {
                if (dot is MeshInstance3D { Mesh: { } mesh } node && node.IsVisibleInTree() && Describe(node, mesh) is { } info) Reach(info, planes, inside, batches, worldDraws, worldDrawCount, draws, blocks, textures, kinds);
            }
        }

        if (root is null) return;
        if (!ReferenceEquals(root, _root))
        {
            _root = root;
            _projectors.Clear();
            foreach (Node child in root.GetChildren())
            {
                if (child is MeshInstance3D { Mesh: { } mesh } node && Describe(node, mesh) is { } info) _projectors.Add(info);
            }
        }

        foreach (Info p in _projectors)
        {
            if (!GodotObject.IsInstanceValid(p.Node) || !p.Node.IsVisibleInTree()) continue;
            Reach(p, planes, inside, batches, worldDraws, worldDrawCount, draws, blocks, textures, kinds);
        }
    }

    private static readonly StringName s_laserDots = OpenScctGame.Fx.SpyLaserView.GroupDot;

    /// <summary>Adds the frame's world draws the projector's box reaches.</summary>
    private static void Reach(Info p, Plane[] planes, Func<Plane[], Aabb, bool> inside, List<RdWorldGeometry.Batch> batches, int[] worldDraws, int worldDrawCount,
        List<Draw> draws, List<float> blocks, List<Rid> textures, List<int> kinds)
    {
        if (!inside(planes, p.Box)) return;
        int index = -1;
        for (int k = 0; k < worldDrawCount; k++)
        {
            RdWorldGeometry.Batch b = batches[worldDraws[k]];
            if (b.Sky || b.Material.Blended || !b.Bounds.Intersects(p.Box) || Outside(p.Planes, b.Bounds)) continue;
            // bProjectStaticMesh off: baked static meshes are left alone.
            if (p.SkipStaticMeshes && b.Material.Code.Contains("#define LIGHT_VERTEX", StringComparison.Ordinal)) continue;
            if (index < 0)
            {
                index = kinds.Count;
                blocks.AddRange(p.Block);
                textures.Add(p.Texture);
                kinds.Add(p.Kind);
            }

            draws.Add(new Draw { Projector = index, WorldDraw = k });
        }
    }

    /// <summary>True when the box lies wholly beyond one face of the projector's frustum box: nothing of it can receive.</summary>
    private static bool Outside(Vector4[] planes, Aabb box)
    {
        Vector3 low = box.Position, high = box.End;
        foreach (Vector4 plane in planes)
        {
            // The corner of the box deepest inside this face.
            float d = (plane.X * (plane.X > 0f ? low.X : high.X)) + (plane.Y * (plane.Y > 0f ? low.Y : high.Y)) + (plane.Z * (plane.Z > 0f ? low.Z : high.Z)) + plane.W;
            if (d > 0f) return true;
        }

        return false;
    }

    private static Info? Describe(MeshInstance3D node, Mesh mesh)
    {
        if ((node.GetSurfaceOverrideMaterial(0) ?? node.MaterialOverride) is not ShaderMaterial { Shader: { } shader } material) return null;
        string code = ResponsiveSmokeShaders.BaseCode(shader);
        // The laser dot adds its texture times the texture's alpha: the added blend, marked 3 for the shader.
        bool laser = code.Contains("#define PROJ_LASER", StringComparison.Ordinal);
        int kind = laser || code.Contains("#define PROJ_ADD", StringComparison.Ordinal) ? 0 : code.Contains("#define PROJ_MODULATE", StringComparison.Ordinal) ? 1
            : code.Contains("#define PROJ_ALPHA", StringComparison.Ordinal) ? 2 : -1;
        if (kind < 0) return null;
        var block = new float[BlockFloats];
        for (int i = 0; i < s_vec4.Length; i++)
        {
            Variant value = material.GetShaderParameter(s_vec4[i]);
            Vector4 v = value.VariantType switch
            {
                Variant.Type.Vector4 => value.AsVector4(),
                Variant.Type.Vector3 => new Vector4(value.AsVector3().X, value.AsVector3().Y, value.AsVector3().Z, 0f),
                // The shader's own defaults.
                _ => i switch { 1 => new Vector4(1, 0, 0, 0), 2 => new Vector4(0, 0, 1, 0), 3 => new Vector4(0, 1, 0, 0), 4 => new Vector4(1, 1, 0, 0), 5 => new Vector4(1, 1, 1, 0),
                    6 => new Vector4(1, 0, 0, 0), 7 => new Vector4(0, 1, 0, 0), _ => Vector4.Zero },
            };
            (block[i * 4], block[(i * 4) + 1], block[(i * 4) + 2], block[(i * 4) + 3]) = (v.X, v.Y, v.Z, v.W);
        }

        Variant anim = material.GetShaderParameter("uv_anim_type");
        block[40] = anim.VariantType == Variant.Type.Nil ? 0f : anim.AsInt32();
        block[41] = laser ? 3 : kind;
        Rid texture = material.GetShaderParameter("proj_tex").AsGodotObject() is Texture2D t ? RenderingServer.TextureGetRdTexture(t.GetRid(), false) : default;
        return new Info
        {
            Node = node, Box = node.GlobalTransform * mesh.GetAabb(), Block = block, Texture = texture, Kind = kind,
            SkipStaticMeshes = code.Contains("stencil_mode read", StringComparison.Ordinal),
            Planes = material.GetShaderParameter("proj_planes") is { VariantType: Variant.Type.PackedVector4Array } planes ? planes.AsVector4Array() : [],
        };
    }

    // ---- Render thread ----

    private const string Common = """
        #version 450
        layout(set = 0, binding = 0, std140) uniform Frame
        {
            mat4 world;
            mat4 sky;
            vec4 params;
            vec4 eye;
            vec4 fog_color;
            vec4 fog;
            // x: the light the vision mode adds to every lit surface (svm_vision_ambient).
            vec4 vision;
        } frame;
        struct Projector
        {
            vec4 origin;
            vec4 x;
            vec4 y;
            vec4 z;
            vec4 shape;
            vec4 flip;
            vec4 row_u;
            vec4 row_v;
            vec4 anim_a;
            vec4 anim_b;
            vec4 kind;
        };
        layout(set = 0, binding = 1, std430) readonly buffer Projectors
        {
            Projector projectors[];
        };
        layout(push_constant, std430) uniform Draw
        {
            int projector;
            int pad0;
            int pad1;
            int pad2;
        } draw;

        """;

    public const string Vertex = Common + """
        layout(location = 0) in vec3 a_pos;
        layout(location = 0) out vec3 v_world;
        invariant gl_Position;

        void main()
        {
            v_world = a_pos;
            gl_Position = frame.world * vec4(a_pos, 1.0);
        }

        """;

    public const string Fragment = Common + """
        layout(set = 1, binding = 0) uniform sampler2D proj_tex;
        layout(location = 0) in vec3 v_world;
        layout(location = 0) out vec4 frag;

        void main()
        {
            Projector p = projectors[draw.projector];
            vec3 world = v_world;
            // The surface's normal from its own positions, turned towards the camera.
            vec3 n = cross(dFdx(world), dFdy(world));
            if (dot(n, frame.eye.xyz - world) < 0.0) {
                n = -n;
            }
            vec3 d = world - p.origin.xyz;
            float dx = dot(d, p.x.xyz);
            if (dx < 0.0 || dx > p.origin.w) {
                discard;
            }
            float s = p.shape.z > 0.0 ? (dx + p.shape.z) / p.shape.z : 1.0;
            vec2 uv = vec2(dot(d, p.y.xyz) / (2.0 * p.shape.x * s), dot(d, p.z.xyz) / (2.0 * p.shape.y * s)) + 0.5;
            if (uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0) {
                discard;
            }
            if (p.shape.w < 0.5 && dot(n, -p.x.xyz) <= 0.0) {
                discard;
            }
            uv = vec2(dot(p.row_u.xyz, vec3(uv, 1.0)), dot(p.row_v.xyz, vec3(uv, 1.0)));
            float t = frame.params.y;
            if (p.kind.x > 0.5 && p.kind.x < 1.5) {
                uv += p.anim_a.xy * p.anim_a.z * t;
            } else if (p.kind.x > 1.5) {
                float su = p.anim_a.x * sin(6.2831853 * p.anim_a.y * t);
                float sv = p.anim_b.x * sin(6.2831853 * p.anim_b.y * t);
                uv.x = p.anim_a.z >= 1.0 ? (uv.x - 0.5) * (1.0 + su) + 0.5 : uv.x + su;
                uv.y = p.anim_b.z >= 1.0 ? (uv.y - 0.5) * (1.0 + sv) + 0.5 : uv.y + sv;
            }
            if (p.flip.z >= 2.0) {
                float cell_frame = mod(floor(t * p.flip.w), p.flip.z);
                vec2 cell = vec2(mod(cell_frame, p.flip.x), floor(cell_frame / p.flip.x));
                uv = (cell + clamp(fract(uv), 0.001, 0.999)) / p.flip.xy;
            }
            vec4 c = texture(proj_tex, uv);
            float cover = 0.0;
            if (frame.fog_color.a > 0.5) {
                cover = clamp((max(length(world - frame.eye.xyz) * frame.fog.x, frame.fog.y) - frame.fog.y) * frame.fog.z, 0.0, 1.0);
            }
            if (p.kind.y > 2.5) {
                // The spy pistol's dot (PROJ_LASER): added, weighted by the texture's alpha.
                frag = vec4(c.rgb * c.a * (1.0 - cover), 1.0);
            } else if (p.kind.y < 0.5) {
                frag = vec4(c.rgb * (1.0 - cover), 1.0);
            } else if (p.kind.y < 1.5) {
                // The original's multiplied draw: fog pulls the factor to one.
                frag = vec4(mix(vec3(254.0 / 255.0), c.rgb * 2.0, 1.0 - cover), 1.0);
            } else {
                frag = vec4(c.rgb * (1.0 - cover) + frame.fog_color.rgb * cover, c.a);
            }
        }

        """;
}
