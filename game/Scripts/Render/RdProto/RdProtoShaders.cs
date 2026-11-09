namespace OpenScctGame.Render.RdProto;

/// <summary>
/// The prototype's one world shader (GLSL 450 for RenderingDevice), in two variants: solid and alpha-tested (MASKED).
/// Both declare the same sets, so a uniform set made against one is valid with the other (Godot checks the set
/// format, which comes from SPIR-V reflection: a binding a variant never reads would be stripped and change it).
/// </summary>
internal static class RdProtoShaders
{
    // set 0: per frame (both view-projections, already in Vulkan clip space with reverse Z; params.x lightmap scale).
    // set 1: per texture pair.
    private const string Common = """
        layout(set = 0, binding = 0, std140) uniform Frame
        {
            mat4 world;
            mat4 sky;
            vec4 params;
        } frame;
        """;

    public const string Vertex = """
        #version 450
        """ + "\n" + Common + """

        layout(location = 0) in vec3 a_pos;
        layout(location = 1) in vec2 a_uv;
        layout(location = 2) in vec2 a_uv2;
        layout(location = 3) in vec4 a_col;

        layout(location = 0) out vec2 v_uv;
        layout(location = 1) out vec2 v_uv2;
        layout(location = 2) out vec3 v_light;
        layout(location = 3) flat out uint v_flags;

        void main()
        {
            uint flags = uint(a_col.a * 255.0 + 0.5);
            v_uv = a_uv;
            v_uv2 = a_uv2;
            // The vertex light holds half the light term (Godot COLOR), as the faithful template's LIGHT_VERTEX.
            v_light = a_col.rgb * 2.0;
            v_flags = flags;
            // Flag 1: a sky zone surface, seen from the SkyZoneInfo with the camera's rotation, depth squeezed behind.
            gl_Position = ((flags & 1u) != 0u ? frame.sky : frame.world) * vec4(a_pos, 1.0);
        }
        """;

    public static string Fragment(bool masked) => "#version 450\n" + (masked ? "#define MASKED\n" : "") + Common + """

        layout(set = 1, binding = 0) uniform sampler2D tex_diffuse;
        layout(set = 1, binding = 1) uniform sampler2D tex_lightmap;

        layout(location = 0) in vec2 v_uv;
        layout(location = 1) in vec2 v_uv2;
        layout(location = 2) in vec3 v_light;
        layout(location = 3) flat in uint v_flags;

        layout(location = 0) out vec4 frag;

        void main()
        {
            vec4 t = texture(tex_diffuse, v_uv);
        #ifdef MASKED
            if (t.a < 0.5) {
                discard;
            }
        #endif
            // texture x min(lightmap x 2 + vertex light x 2, 2): a lightmapped batch has black vertex light, a
            // vertex-lit one a black lightmap. Flag 2: fullbright.
            vec3 light = (v_flags & 2u) != 0u ? vec3(1.0)
                : min(texture(tex_lightmap, v_uv2).rgb * frame.params.x + v_light, vec3(2.0));
            // Raw display values into the RGBA8 target, as the original's back buffer. Blended passes keep the
            // target's alpha (write mask), solid ones write 1 so the canvas shows the picture opaque.
            frag = vec4(t.rgb * light, t.a);
        }
        """;
}
