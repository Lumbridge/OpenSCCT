using Godot;

namespace OpenScctGame.Render.Rd;

/// <summary>
/// Simple blended geometry in the own frame (SVM_RD_FRAME=1): triangles with a position, a UV and a colour at each
/// vertex, one texture a draw, added to the frame or blended over it by the texture's alpha. The producers hand their
/// vertices over when they rebuild them (the rain sheets: svm_rain.gdshaderinc's panner and fog rules).
/// </summary>
internal static class RdSheets
{
    /// <summary>24 bytes: position, UV, colour.</summary>
    public const int Stride = 24;

    public struct Draw
    {
        public int First, Count;
        public Rid Texture;
        /// <summary>UV scale (xy) and UV speed a second (zw).</summary>
        public Vector4 Pan;
        /// <summary>True: blended by the texture's alpha and fogged towards the fog colour; false: added, fog dims it.</summary>
        public bool Alpha;
    }

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
            vec4 kind;
        } draw;

        """;

    public const string Vertex = Common + """
        layout(location = 0) in vec3 a_pos;
        layout(location = 1) in vec2 a_uv;
        layout(location = 2) in vec4 a_col;
        layout(location = 0) out vec2 v_uv;
        layout(location = 1) out vec4 v_col;
        layout(location = 2) out vec3 v_rel;

        void main()
        {
            v_uv = a_uv * draw.pan.xy + draw.pan.zw * frame.params.y;
            v_col = a_col;
            v_rel = a_pos - frame.eye.xyz;
            gl_Position = frame.world * vec4(a_pos, 1.0);
        }

        """;

    public const string Fragment = Common + """
        layout(set = 1, binding = 0) uniform sampler2D tex;
        layout(location = 0) in vec2 v_uv;
        layout(location = 1) in vec4 v_col;
        layout(location = 2) in vec3 v_rel;
        layout(location = 0) out vec4 frag;

        void main()
        {
            vec4 t = texture(tex, v_uv);
            float cover = 0.0;
            if (frame.fog_color.a > 0.5) {
                cover = clamp((max(length(v_rel) * frame.fog.x, frame.fog.y) - frame.fog.y) * frame.fog.z, 0.0, 1.0);
            }
            vec3 c = t.rgb * v_col.rgb * (1.0 - cover);
            if (draw.kind.x > 0.5) {
                frag = vec4(c + frame.fog_color.rgb * cover, t.a);
            } else {
                frag = vec4(c, 1.0);
            }
        }

        """;
}
