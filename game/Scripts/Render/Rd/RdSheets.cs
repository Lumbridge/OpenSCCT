using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Godot;
using OpenScctGame.Render.Materials;
using OpenScctGame.Render.Particles;

namespace OpenScctGame.Render.Rd;

/// <summary>
/// Simple blended geometry in the own frame (the options' Fast renderer): triangles with a position, two UVs and a colour at each
/// vertex, one texture a draw, blended by one of the particle draw styles. The rain sheets and the sprite particles
/// are drawn this way, with the rules of svm_rain.gdshaderinc and svm_particle.gdshaderinc; the frame's vertices are
/// gathered on the main thread and handed over whole every frame.
/// </summary>
internal static class RdSheets
{
    /// <summary>32 bytes: position, UV, second UV, colour.</summary>
    public const int Stride = 32;

    /// <summary>The draw styles of svm_particle.gdshaderinc, in SetParticleMaterial's order (3 is the additive one).</summary>
    public const int StyleRegular = 0, StyleAlpha = 1, StyleModulate = 2, StyleAdd = 3, StylePremul = 4, StyleDarken = 5, StyleBrighten = 6, Styles = 7;

    public struct Draw
    {
        public int First, Count, Style;
        public Rid Texture;
        /// <summary>UV scale (xy) and UV speed a second (zw).</summary>
        public Vector4 Pan;
        /// <summary>Blend to the second UV's texel by the vertex alpha (particles between texture cells).</summary>
        public bool BlendCells;
        /// <summary>Texels at or under this alpha are cut; negative: no alpha test.</summary>
        public float AlphaRef;
        public bool DepthTest, Repeat;
        /// <summary>The emitter's HeatOpacity: how much of its alpha a particle keeps in thermal vision (1 for everything else).</summary>
        public float HeatOpacity;
        /// <summary>
        /// A light beam's slices (svm_light_beam.gdshader): Pan holds what a slice adds (rgb) and the light's spot
        /// shadow slot (w, negative: no cut); the first UV's x runs along the beam.
        /// </summary>
        public bool Beam;
    }

    // ---- Main thread: this frame's vertices and draws ----

    private static readonly List<byte> s_vertices = [];
    private static readonly List<Draw> s_draws = [];
    private static readonly Dictionary<ShaderMaterial, Draw> s_particleMaterials = [];

    /// <summary>Starts a frame's gathering; the particles collected since the last frame stay (they were built this frame).</summary>
    public static (List<byte> Vertices, List<Draw> Draws) Frame => (s_vertices, s_draws);

    /// <summary>A new map: what was found in the last one and kept by material or mesh is let go.</summary>
    public static void ResetScene()
    {
        Clear();
        s_particleMaterials.Clear();
        s_emitterMeshes.Clear();
        s_beamMeshes.Clear();
        s_beamRoot = null;
        s_beamSearched = false;
    }

    public static void Clear()
    {
        s_vertices.Clear();
        s_draws.Clear();
    }

    /// <summary>A run of vertices already in the list becomes a draw.</summary>
    public static void Add(Draw draw) => s_draws.Add(draw);

    public static void Vertex(Vector3 position, Vector2 uv, Vector2 uv2, byte r, byte g, byte b, byte a)
    {
        Span<float> floats = [position.X, position.Y, position.Z, uv.X, uv.Y, uv2.X, uv2.Y];
        s_vertices.AddRange(MemoryMarshal.AsBytes(floats));
        s_vertices.AddRange([r, g, b, a]);
    }

    /// <summary>
    /// A particle layer's quads of this frame (ParticleSystemView, as it fills its own mesh): two triangles a quad,
    /// drawn with the layer's material rules.
    /// </summary>
    /// <summary>
    /// Set while a renderer takes the frame's sheets (the own frame drawing the normal view): nothing is collected
    /// otherwise (menus, a map Godot's renderer draws, a vision mode), so nothing piles up unread.
    /// </summary>
    public static bool Active { get; set; }

    public static void CollectParticles(ShaderMaterial? material, List<SpriteQuad> quads)
    {
        if (!Active || material is null || quads.Count == 0) return;
        Draw draw = DrawOf(material);
        draw.First = s_vertices.Count / Stride;
        foreach (SpriteQuad q in quads)
        {
            Vector3 c0 = UeSpace.ToGodot(q.C0), c1 = UeSpace.ToGodot(q.C1), c2 = UeSpace.ToGodot(q.C2), c3 = UeSpace.ToGodot(q.C3);
            Vector2 u0 = new(q.U0, q.V1), u1 = new(q.U1, q.V1), u2 = new(q.U0, q.V0), u3 = new(q.U1, q.V0);
            Vector2 b0 = new(q.BU0, q.BV1), b1 = new(q.BU1, q.BV1), b2 = new(q.BU0, q.BV0), b3 = new(q.BU1, q.BV0);
            (byte r, byte g, byte b, byte a) = (q.Color.R, q.Color.G, q.Color.B, q.Color.A);
            Vertex(c0, u0, b0, r, g, b, a);
            Vertex(c1, u1, b1, r, g, b, a);
            Vertex(c2, u2, b2, r, g, b, a);
            Vertex(c2, u2, b2, r, g, b, a);
            Vertex(c1, u1, b1, r, g, b, a);
            Vertex(c3, u3, b3, r, g, b, a);
        }

        draw.Count = (s_vertices.Count / Stride) - draw.First;
        s_draws.Add(draw);
    }

    private static readonly Dictionary<Mesh, (Vector3[] Positions, Vector2[] Uvs)> s_emitterMeshes = [];

    /// <summary>
    /// A mesh emitter's particles of this frame (ParticleSystemView.FillMeshes): the emitter's mesh placed by each
    /// particle here, in the particle's colour, drawn with the layer's material rules.
    /// </summary>
    public static void CollectMeshes(ShaderMaterial? material, Mesh? mesh, List<MeshInstance> meshes, int count)
    {
        if (!Active || material is null || mesh is null || count <= 0) return;
        if (!s_emitterMeshes.TryGetValue(mesh, out (Vector3[] Positions, Vector2[] Uvs) triangles))
        {
            var positions = new List<Vector3>();
            var uvs = new List<Vector2>();
            for (int s = 0; s < mesh.GetSurfaceCount(); s++)
            {
                Godot.Collections.Array arrays = mesh.SurfaceGetArrays(s);
                Vector3[] p = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                Vector2[] t = arrays[(int)Mesh.ArrayType.TexUV].VariantType == Variant.Type.Nil ? [] : arrays[(int)Mesh.ArrayType.TexUV].AsVector2Array();
                int[] index = arrays[(int)Mesh.ArrayType.Index].VariantType == Variant.Type.Nil ? [] : arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
                int n = index.Length > 0 ? index.Length : p.Length;
                for (int i = 0; i < n; i++)
                {
                    int v = index.Length > 0 ? index[i] : i;
                    positions.Add(p[v]);
                    uvs.Add(v < t.Length ? t[v] : Vector2.Zero);
                }
            }

            triangles = (positions.ToArray(), uvs.ToArray());
            s_emitterMeshes[mesh] = triangles;
        }

        if (triangles.Positions.Length == 0) return;
        Draw draw = DrawOf(material);
        draw.BlendCells = false;
        draw.Repeat = true;
        draw.First = s_vertices.Count / Stride;
        for (int i = 0; i < count && i < meshes.Count; i++)
        {
            MeshInstance m = meshes[i];
            Transform3D place = UeSpace.TransformToGodot(m.Location, m.Rotation, m.Scale, default);
            for (int v = 0; v < triangles.Positions.Length; v++)
            {
                Vertex(place * triangles.Positions[v], triangles.Uvs[v], Vector2.Zero, m.Color.R, m.Color.G, m.Color.B, m.Color.A);
            }
        }

        draw.Count = (s_vertices.Count / Stride) - draw.First;
        s_draws.Add(draw);
    }

    private static Draw DrawOf(ShaderMaterial material)
    {
        if (!s_particleMaterials.TryGetValue(material, out Draw draw))
        {
            string code = material.Shader is { } shader ? ResponsiveSmokeShaders.BaseCode(shader) : "";
            int style = code.Contains("#define STYLE_REGULAR", StringComparison.Ordinal) ? StyleRegular
                : code.Contains("#define STYLE_ALPHA", StringComparison.Ordinal) ? StyleAlpha
                : code.Contains("#define STYLE_MODULATE", StringComparison.Ordinal) ? StyleModulate
                : code.Contains("#define STYLE_PREMUL", StringComparison.Ordinal) ? StylePremul
                : code.Contains("#define STYLE_DARKEN", StringComparison.Ordinal) ? StyleDarken
                : code.Contains("#define STYLE_BRIGHTEN", StringComparison.Ordinal) ? StyleBrighten : StyleAdd;
            Variant cells = material.GetShaderParameter("blend_cells"), alphaRef = material.GetShaderParameter("alpha_ref");
            Variant heat = material.GetShaderParameter("heat_opacity");
            draw = new Draw
            {
                Style = style, Pan = new Vector4(1f, 1f, 0f, 0f), HeatOpacity = heat.VariantType == Variant.Type.Nil ? 1f : heat.AsSingle(),
                Texture = material.GetShaderParameter("tex_sprite").AsGodotObject() is Texture2D t ? RenderingServer.TextureGetRdTexture(t.GetRid(), false) : default,
                BlendCells = cells.VariantType == Variant.Type.Bool && cells.AsBool(),
                AlphaRef = alphaRef.VariantType == Variant.Type.Nil ? -1f : alphaRef.AsSingle(),
                DepthTest = !code.Contains("depth_test_disabled", StringComparison.Ordinal), Repeat = false,
            };
            s_particleMaterials[material] = draw;
        }

        return draw;
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
        layout(push_constant, std430) uniform Draw
        {
            vec4 pan;
            // x style, y 1 to blend between texture cells, z alpha test reference (negative: none), w HeatOpacity
            vec4 kind;
        } draw;

        """;

    public const string VertexShader = Common + """
        layout(location = 0) in vec3 a_pos;
        layout(location = 1) in vec2 a_uv;
        layout(location = 2) in vec2 a_uv2;
        layout(location = 3) in vec4 a_col;
        layout(location = 0) out vec2 v_uv;
        layout(location = 1) out vec2 v_uv2;
        layout(location = 2) out vec4 v_col;
        layout(location = 3) out vec3 v_rel;

        void main()
        {
            v_uv = a_uv * draw.pan.xy + draw.pan.zw * frame.params.y;
            v_uv2 = a_uv2;
            v_col = a_col;
            v_rel = a_pos - frame.eye.xyz;
            gl_Position = frame.world * vec4(a_pos, 1.0);
        }

        """;

    public const string FragmentShader = Common + """
        layout(set = 1, binding = 0) uniform sampler2D tex;
        layout(location = 0) in vec2 v_uv;
        layout(location = 1) in vec2 v_uv2;
        layout(location = 2) in vec4 v_col;
        layout(location = 3) in vec3 v_rel;
        layout(location = 0) out vec4 frag;

        void main()
        {
            vec4 t = texture(tex, v_uv);
            vec4 v = v_col;
            if (draw.kind.y > 0.5) {
                t = mix(t, texture(tex, v_uv2), v.a);
                v.a = 1.0;
            }
            int style = int(draw.kind.x + 0.5);
            vec4 c = style == 2 ? mix(v, t, v.a) : t * v;
            if (draw.kind.z >= 0.0 && c.a <= draw.kind.z) {
                discard;
            }
            if (frame.vision.y > 35.5) {
                // Thermal (svm_particle.gdshaderinc): the alpha styles draw the translucent rule, the depth fade by
                // their alpha, over the heat behind them; the other styles draw nothing. The view depth is 1 / w.
                if (style != 0 && style != 1 && style != 4) {
                    discard;
                }
                float fade = 1.0 - clamp(100.0 / (gl_FragCoord.w * 5000.0), 0.0, 1.0);
                frag = vec4(0.0, 0.0, fade, style == 0 ? 1.0 : c.a * draw.kind.w);
                return;
            }
            float cover = 0.0;
            if (frame.fog_color.a > 0.5) {
                cover = clamp((max(length(v_rel) * frame.fog.x, frame.fog.y) - frame.fog.y) * frame.fog.z, 0.0, 1.0);
            }
            float through = 1.0 - cover;
            vec3 fogged = c.rgb * through + frame.fog_color.rgb * cover;
            if (style == 3) {
                frag = vec4(c.rgb * through, 1.0);
            } else if (style == 2) {
                frag = vec4(mix(vec3(254.0 / 255.0), c.rgb * 2.0, through), 1.0);
            } else if (style == 5) {
                frag = vec4(vec3(1.0) - c.rgb * through, 1.0);
            } else if (style == 4) {
                frag = vec4(c.rgb * through, c.a);
            } else if (style == 6) {
                frag = vec4(c.rgb * through, max(c.r, max(c.g, c.b)) * through);
            } else if (style == 0) {
                frag = vec4(fogged, 1.0);
            } else {
                frag = vec4(fogged, c.a);
            }
        }

        """;

    /// <summary>
    /// A light beam's slices: each adds boost x (1 - along) x what its light throws there (the cookie where the light
    /// reaches, nothing outside its frustum or behind an occluder), dimmed by the fog. svm_light_beam.gdshader.
    /// </summary>
    public const string BeamFragmentShader = Common + """
        layout(set = 2, binding = 0) uniform sampler2D svm_spots;
        layout(set = 2, binding = 1) uniform sampler2D svm_spot_depth;
        layout(set = 2, binding = 2) uniform sampler2DArray svm_spot_cookies;
        layout(location = 0) in vec2 v_uv;
        layout(location = 1) in vec2 v_uv2;
        layout(location = 2) in vec4 v_col;
        layout(location = 3) in vec3 v_rel;
        layout(location = 0) out vec4 frag;

        vec3 beam_lit(int i, vec3 wp)
        {
            vec4 a = texelFetch(svm_spots, ivec2(3, i), 0);
            if (a.x < 0.5) {
                return vec3(0.0);
            }
            vec4 w = vec4(wp, 1.0);
            vec3 v = vec3(dot(texelFetch(svm_spots, ivec2(0, i), 0), w), dot(texelFetch(svm_spots, ivec2(1, i), 0), w),
                dot(texelFetch(svm_spots, ivec2(2, i), 0), w));
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
            vec4 tile = texelFetch(svm_spots, ivec2(6, i), 0);
            vec4 e = texelFetch(svm_spots, ivec2(7, i), 0);
            float zref = z - e.y - e.z * z;
            vec2 atlas = vec2(textureSize(svm_spot_depth, 0));
            vec2 lo = tile.xy * atlas + 0.5;
            vec2 hi = (tile.xy + tile.zw) * atlas - 1.5;
            vec2 p = clamp((tile.xy + suv * tile.zw) * atlas - 0.5, lo, max(lo, hi));
            vec2 base = floor(p);
            vec2 fr = p - base;
            vec4 d = textureGather(svm_spot_depth, (base + 1.0) / atlas, 0);
            vec4 lit = step(vec4(zref), d);
            float shadow = mix(mix(lit.w, lit.z, fr.x), mix(lit.x, lit.y, fr.x), fr.y);
            return texture(svm_spot_cookies, vec3(suv, f.w)).rgb * shadow;
        }

        void main()
        {
            vec3 lit = draw.pan.w >= 0.0 ? beam_lit(int(draw.pan.w + 0.5), v_rel + frame.eye.xyz) : vec3(1.0);
            float cover = 0.0;
            if (frame.fog_color.a > 0.5) {
                cover = clamp((max(length(v_rel) * frame.fog.x, frame.fog.y) - frame.fog.y) * frame.fog.z, 0.0, 1.0);
            }
            frag = vec4(max(draw.pan.rgb, vec3(0.0)) * clamp(1.0 - v_uv2.x, 0.0, 1.0) * lit * (1.0 - cover), 1.0);
        }

        """;

    private static readonly Dictionary<Mesh, Vector3[]> s_beamMeshes = [];
    private static Node? s_beamRoot;
    private static bool s_beamSearched;
    private static int s_beamWait;

    /// <summary>
    /// The light beams shown this frame (the LightBeams node's meshes, MapLightBeams): each slice mesh stretched along
    /// its light here as the beam shader's vertex stage does it, and added as one draw.
    /// </summary>
    public static void CollectBeams(Node scene)
    {
        // Looked for again now and then while there is none: a map builds its beams once, before or after this starts.
        if (!s_beamSearched || (s_beamRoot is null && ++s_beamWait >= 120) || (s_beamRoot is not null && !GodotObject.IsInstanceValid(s_beamRoot)))
        {
            s_beamWait = 0;
            s_beamSearched = true;
            s_beamRoot = scene.FindChild("LightBeams", true, false);
        }

        if (s_beamRoot is not Node3D root || !root.IsVisibleInTree()) return;
        foreach (Node child in root.GetChildren())
        {
            if (child is not MeshInstance3D { Visible: true, Mesh: { } mesh, MaterialOverride: ShaderMaterial material }) continue;
            if (!s_beamMeshes.TryGetValue(mesh, out Vector3[]? triangles))
            {
                Godot.Collections.Array arrays = mesh.SurfaceGetArrays(0);
                Vector3[] positions = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                int[] indices = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
                triangles = new Vector3[indices.Length];
                for (int i = 0; i < indices.Length; i++) triangles[i] = positions[indices[i]];
                s_beamMeshes[mesh] = triangles;
            }

            Vector3 origin = material.GetShaderParameter("beam_origin").AsVector3();
            Vector3 x = material.GetShaderParameter("beam_x").AsVector3(), y = material.GetShaderParameter("beam_y").AsVector3(), z = material.GetShaderParameter("beam_z").AsVector3();
            Vector4 shape = material.GetShaderParameter("beam_shape").AsVector4(), boost = material.GetShaderParameter("beam_boost").AsVector4();
            Variant slot = material.GetShaderParameter("beam_slot"), visibility = material.GetShaderParameter("beam_visibility");
            float shown = visibility.VariantType == Variant.Type.Nil ? 1f : visibility.AsSingle();
            int first = s_vertices.Count / Stride;
            foreach (Vector3 m in triangles)
            {
                float grow = shape.W + ((1f - shape.W) * boost.W * m.X);
                Vertex(origin + (x * (m.X * shape.X)) + (y * (m.Y * shape.Y * grow)) + (z * (m.Z * shape.Z * grow)), Vector2.Zero, new Vector2(m.X, 0f), 255, 255, 255, 255);
            }

            s_draws.Add(new Draw
            {
                First = first, Count = triangles.Length, Style = StyleAdd, Beam = true, DepthTest = true, AlphaRef = -1f,
                Pan = new Vector4(boost.X * shown, boost.Y * shown, boost.Z * shown, slot.VariantType == Variant.Type.Nil ? -1f : slot.AsInt32()),
            });
        }
    }

    /// <summary>The blend factors of a style (source, destination), as Godot's blend modes for svm_particle's render modes.</summary>
    public static (RenderingDevice.BlendFactor Source, RenderingDevice.BlendFactor Destination, bool Blend) BlendOf(int style) => style switch
    {
        StyleRegular => (RenderingDevice.BlendFactor.One, RenderingDevice.BlendFactor.Zero, false),
        StyleAlpha => (RenderingDevice.BlendFactor.SrcAlpha, RenderingDevice.BlendFactor.OneMinusSrcAlpha, true),
        StyleModulate or StyleDarken => (RenderingDevice.BlendFactor.DstColor, RenderingDevice.BlendFactor.Zero, true),
        StylePremul or StyleBrighten => (RenderingDevice.BlendFactor.One, RenderingDevice.BlendFactor.OneMinusSrcAlpha, true),
        _ => (RenderingDevice.BlendFactor.One, RenderingDevice.BlendFactor.One, true),
    };
}
