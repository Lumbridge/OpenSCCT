using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Godot;
using OpenScctGame.Render.Materials;
using OpenScctGame.Render.Particles;

namespace OpenScctGame.Render.Rd;

/// <summary>
/// Simple blended geometry in the own frame (SVM_RD_FRAME=1): triangles with a position, two UVs and a colour at each
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
    }

    // ---- Main thread: this frame's vertices and draws ----

    private static readonly List<byte> s_vertices = [];
    private static readonly List<Draw> s_draws = [];
    private static readonly Dictionary<ShaderMaterial, Draw> s_particleMaterials = [];

    /// <summary>Starts a frame's gathering; the particles collected since the last frame stay (they were built this frame).</summary>
    public static (List<byte> Vertices, List<Draw> Draws) Frame => (s_vertices, s_draws);

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
    public static void CollectParticles(ShaderMaterial? material, List<SpriteQuad> quads)
    {
        if (material is null || quads.Count == 0) return;
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
            draw = new Draw
            {
                Style = style, Pan = new Vector4(1f, 1f, 0f, 0f),
                Texture = material.GetShaderParameter("tex_sprite").AsGodotObject() is Texture2D t ? RenderingServer.TextureGetRdTexture(t.GetRid(), false) : default,
                BlendCells = cells.VariantType == Variant.Type.Bool && cells.AsBool(),
                AlphaRef = alphaRef.VariantType == Variant.Type.Nil ? -1f : alphaRef.AsSingle(),
                DepthTest = !code.Contains("depth_test_disabled", StringComparison.Ordinal), Repeat = false,
            };
            s_particleMaterials[material] = draw;
        }

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
        } frame;
        layout(push_constant, std430) uniform Draw
        {
            vec4 pan;
            // x style, y 1 to blend between texture cells, z alpha test reference (negative: none)
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
