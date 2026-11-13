using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using RegexMatch = System.Text.RegularExpressions.Match;

namespace OpenScctGame.Render.Rd;

/// <summary>A global shader parameter as a translated shader declares it (ShaderGlobals gives the places).</summary>
internal readonly record struct RdGlobal(string Name, bool Texture, int Place);

/// <summary>A material value uniform: its GLSL type, its std140 offset and its default (floats, or ints for int types).</summary>
internal sealed record RdMaterialValue(string Name, string Type, int Offset, int Size, double[] Default);

/// <summary>A material texture uniform: binding, filter and repeat from its hints, and the default texture's colour.</summary>
internal sealed record RdMaterialTexture(string Name, int Binding, RdFilter Filter, bool Repeat, bool DefaultBlack);

internal enum RdFilter
{
    Nearest,
    Linear,
    NearestMipmap,
    LinearMipmap,
    LinearMipmapAnisotropic,
}

/// <summary>A Godot spatial shader of the Faithful template as GLSL 450 for the RenderingDevice world pass.</summary>
internal sealed class RdTranslatedShader
{
    public required string Vertex { get; init; }
    public required string Fragment { get; init; }
    public required IReadOnlyList<RdMaterialValue> Values { get; init; }
    public required int ValueBlockSize { get; init; }
    public required IReadOnlyList<RdMaterialTexture> Textures { get; init; }
    public required IReadOnlySet<string> RenderModes { get; init; }
    /// <summary>The stencil reference solid surfaces write (stencil_mode write, compare_always, N), or null.</summary>
    public required int? StencilWrite { get; init; }
    /// <summary>0: merged map geometry. 1: a rigid actor mesh, 2: a skinned one (RdObjects' vertices, object block and bones).</summary>
    public int Actor { get; init; }
    /// <summary>Merged map geometry read with its vision stream (the vertex array that holds it), for thermal and EMF.</summary>
    public bool VisionStream { get; init; }
    public bool TwoSided => RenderModes.Contains("cull_disabled");
}

/// <summary>
/// Turns the code of a Godot spatial shader built on svm_world.gdshaderinc into GLSL 450 vertex and fragment sources,
/// so the world pass on the RenderingDevice (RdWorldRenderer) runs the same source as Godot's renderer: the
/// preprocessor conditionals are resolved here, uniforms become two std140 blocks (global parameters, material
/// values) and combined samplers, instance uniforms their defaults (merged map geometry never sets them), varyings
/// stage inputs and outputs, and vertex() and fragment() run inside a main() that gives them Godot's built-ins.
/// Engine free: plain text in, plain text out. Anything it cannot carry throws <see cref="NotSupportedException"/>,
/// and the surface stays with Godot's renderer.
/// </summary>
internal static partial class GdShaderTranslator
{
    /// <summary>Set 0: binding 0 the frame block, 1 the global value block, 2 + place the global textures.</summary>
    public const int GlobalTextureBinding = 2;

    /// <summary>The frame block's bytes: four mat4, viewport, eye, layers.</summary>
    public const int FrameBlockSize = (4 * 64) + (3 * 16);

    /// <summary>
    /// The depth-only shader of the pass's own pre-pass: the same clip position, by the same expression, as every
    /// translated vertex stage (invariant), so the colour pass finds its own depth.
    /// </summary>
    public const string DepthVertex = "#version 450\n" + FrameBlock + """
        layout(location = 0) in vec3 svm_a_vertex;
        invariant gl_Position;

        void main()
        {
            gl_Position = svm_f.clip * vec4(svm_a_vertex, 1.0);
        }

        """;

    public const string DepthFragment = """
        #version 450
        layout(location = 0) out vec4 svm_frag;

        void main()
        {
            svm_frag = vec4(0.0);
        }

        """;

    /// <summary>The per-draw push constant's bytes (fragment stage): the spot slots and dynamic light rows that reach the draw.</summary>
    public const int DrawBlockSize = 16;

    private const string FrameBlock = """
        layout(set = 0, binding = 0, std140) uniform SvmFrame
        {
            mat4 view;
            mat4 inv_view;
            mat4 projection;
            mat4 clip;
            vec4 viewport;
            vec4 eye;
            uvec4 layers;
        } svm_f;
        #define VIEW_MATRIX svm_f.view
        #define INV_VIEW_MATRIX svm_f.inv_view
        #define PROJECTION_MATRIX svm_f.projection
        #define VIEWPORT_SIZE svm_f.viewport.xy
        #define CAMERA_VISIBLE_LAYERS svm_f.layers.x
        #define CAMERA_POSITION_WORLD svm_f.eye.xyz

        """;

    // Merged map geometry is in world space already.
    private const string WorldModel = """
        #define MODEL_MATRIX mat4(1.0)
        #define MODEL_NORMAL_MATRIX mat3(1.0)
        #define MODELVIEW_MATRIX svm_f.view

        """;

    // An actor: its model matrix (the vertex stage sets it: the object's, or the vertex's blend of bones), its
    // instance uniforms from its block (RdObjects), and the draw's push constant in both stages.
    private const string ActorModel = """
        mat4 svm_model = mat4(1.0);
        #define MODEL_MATRIX svm_model
        #define MODEL_NORMAL_MATRIX mat3(svm_model)
        #define MODELVIEW_MATRIX (svm_f.view * svm_model)
        struct SvmObject
        {
            mat4 model;
            vec4 ambient;
            vec4 light[12];
            vec4 tint;
            vec4 glow_hue;
            vec4 vision;
        };
        layout(set = 2, binding = 0, std430) readonly buffer SvmObjects
        {
            SvmObject svm_objects[];
        };
        layout(push_constant, std430) uniform SvmDraw
        {
            int spot_bits;
            int dyn_bits;
            int object;
            int palette;
        } svm_draw;
        #define svm_ambient svm_objects[svm_draw.object].ambient
        #define svm_l0_pos svm_objects[svm_draw.object].light[0]
        #define svm_l0_col svm_objects[svm_draw.object].light[1]
        #define svm_l0_spot svm_objects[svm_draw.object].light[2]
        #define svm_l1_pos svm_objects[svm_draw.object].light[3]
        #define svm_l1_col svm_objects[svm_draw.object].light[4]
        #define svm_l1_spot svm_objects[svm_draw.object].light[5]
        #define svm_l2_pos svm_objects[svm_draw.object].light[6]
        #define svm_l2_col svm_objects[svm_draw.object].light[7]
        #define svm_l2_spot svm_objects[svm_draw.object].light[8]
        #define svm_l3_pos svm_objects[svm_draw.object].light[9]
        #define svm_l3_col svm_objects[svm_draw.object].light[10]
        #define svm_l3_spot svm_objects[svm_draw.object].light[11]
        #define svm_tint svm_objects[svm_draw.object].tint
        #define svm_glow_hue svm_objects[svm_draw.object].glow_hue
        #define svm_vision_actor svm_objects[svm_draw.object].vision

        """;

    private static readonly HashSet<string> s_actorInstance = new(StringComparer.Ordinal)
    {
        "svm_ambient", "svm_l0_pos", "svm_l0_col", "svm_l0_spot", "svm_l1_pos", "svm_l1_col", "svm_l1_spot", "svm_l2_pos", "svm_l2_col", "svm_l2_spot",
        "svm_l3_pos", "svm_l3_col", "svm_l3_spot", "svm_tint", "svm_glow_hue", "svm_vision_actor",
    };

    [GeneratedRegex(@"^\s*#\s*(\w+)\s*(.*?)\s*$")]
    private static partial Regex Directive();

    [GeneratedRegex("^\\s*#\\s*include\\s+\"([^\"]+)\"")]
    private static partial Regex Include();

    [GeneratedRegex(@"^\s*global\s+uniform\s+(\w+)\s+(\w+)\s*(?::[^;=]*)?(?:=[^;]*)?;")]
    private static partial Regex GlobalUniform();

    [GeneratedRegex(@"^\s*instance\s+uniform\s+(\w+)\s+(\w+)\s*(?::[^;=]*)?(?:=\s*([^;]+?))?\s*;")]
    private static partial Regex InstanceUniform();

    [GeneratedRegex(@"^\s*uniform\s+(\w+)\s+(\w+)\s*(?::\s*([^;=]*?))?\s*(?:=\s*([^;]+?))?\s*;")]
    private static partial Regex Uniform();

    [GeneratedRegex(@"^\s*varying\s+(flat\s+)?(\w+)\s+(\w+)\s*;")]
    private static partial Regex Varying();

    [GeneratedRegex(@"^\s*(shader_type|render_mode|stencil_mode)\s+([^;]*);\s*$")]
    private static partial Regex Statement();

    [GeneratedRegex(@"\bvoid\s+(vertex|fragment)\s*\(\s*\)")]
    private static partial Regex StageFunction();

    [GeneratedRegex(@"-?\d+(?:\.\d*)?(?:[eE][-+]?\d+)?")]
    private static partial Regex Number();

    /// <param name="code">The Godot shader's code.</param>
    /// <param name="include">Reads an included file (res:// path) as text.</param>
    /// <param name="globals">The registered global parameters (name to place).</param>
    /// <param name="define">Names defined before the code's first line.</param>
    /// <param name="suppress">Names whose #define lines in the code are dropped.</param>
    public static RdTranslatedShader Translate(string code, Func<string, string> include, IReadOnlyDictionary<string, RdGlobal> globals,
        IReadOnlyCollection<string>? define = null, IReadOnlyCollection<string>? suppress = null, int actor = 0, bool visionStream = false)
    {
        visionStream &= actor == 0;
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(include);
        ArgumentNullException.ThrowIfNull(globals);
        var flat = new List<string>();
        // The template's own switch for this pass.
        var defined = new HashSet<string>(StringComparer.Ordinal) { "SVM_RD_WORLD" };
        flat.Add("#define SVM_RD_WORLD");
        foreach (string name in define ?? [])
        {
            defined.Add(name);
            flat.Add("#define " + name);
        }

        Flatten(code, include, defined, new HashSet<string>(suppress ?? [], StringComparer.Ordinal), flat, 0);

        var modes = new HashSet<string>(StringComparer.Ordinal);
        int? stencil = null;
        var values = new List<RdMaterialValue>();
        var textures = new List<RdMaterialTexture>();
        var varyings = new List<(bool Flat, string Type, string Name)>();
        var globalValues = new SortedDictionary<int, (string Type, string Name)>();
        var globalTextures = new SortedDictionary<int, (string Type, string Name)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var body = new StringBuilder();
        int blockSize = 0;
        foreach (string raw in flat)
        {
            string line = StripComment(raw);
            RegexMatch m;
            if ((m = Statement().Match(line)).Success)
            {
                string[] parts = m.Groups[2].Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                if (m.Groups[1].Value == "render_mode") modes.UnionWith(parts);
                else if (m.Groups[1].Value == "stencil_mode")
                {
                    if (parts.Length != 3 || parts[0] != "write" || parts[1] != "compare_always") throw new NotSupportedException("stencil_mode " + m.Groups[2].Value);
                    stencil = int.Parse(parts[2], CultureInfo.InvariantCulture);
                }
                else if (m.Groups[2].Value.Trim() != "spatial") throw new NotSupportedException("shader_type " + m.Groups[2].Value);
                continue;
            }

            if ((m = GlobalUniform().Match(line)).Success)
            {
                string type = m.Groups[1].Value, name = m.Groups[2].Value;
                if (!globals.TryGetValue(name, out RdGlobal slot)) throw new NotSupportedException("global uniform " + name + " is not registered");
                if (slot.Texture != IsSampler(type)) throw new NotSupportedException("global uniform " + name + " has another kind here");
                (slot.Texture ? globalTextures : globalValues)[slot.Place] = (type, name);
                continue;
            }

            if ((m = InstanceUniform().Match(line)).Success)
            {
                // Merged map geometry never sets instance uniforms: each reads its default.
                string type = m.Groups[1].Value, name = m.Groups[2].Value;
                // An actor reads its light and cosmetic values from its block.
                if (actor != 0 && s_actorInstance.Contains(name)) continue;
                if (seen.Add("i:" + name))
                {
                    body.Append("const ").Append(type).Append(' ').Append(name).Append(" = ")
                        .Append(m.Groups[3].Success ? m.Groups[3].Value : type + "(0)").Append(";\n");
                }

                continue;
            }

            if ((m = Uniform().Match(line)).Success)
            {
                string type = m.Groups[1].Value, name = m.Groups[2].Value, hints = m.Groups[3].Value;
                if (!seen.Add("u:" + name)) continue;
                if (IsSampler(type))
                {
                    if (type != "sampler2D") throw new NotSupportedException("material uniform " + type + " " + name);
                    textures.Add(TextureOf(name, hints, textures.Count + 1));
                }
                else
                {
                    (int size, int align) = Std140(type);
                    int offset = (blockSize + align - 1) / align * align;
                    blockSize = offset + size;
                    values.Add(new RdMaterialValue(name, type, offset, size, DefaultOf(type, m.Groups[4].Success ? m.Groups[4].Value : null)));
                }

                continue;
            }

            if ((m = Varying().Match(line)).Success)
            {
                if (seen.Add("v:" + m.Groups[3].Value)) varyings.Add((m.Groups[1].Success, m.Groups[2].Value, m.Groups[3].Value));
                continue;
            }

            body.Append(raw).Append('\n');
        }

        string text = body.ToString();
        (int vertexAt, int vertexEnd) = FunctionSpan(text, "vertex");
        (int fragmentAt, int fragmentEnd) = FunctionSpan(text, "fragment");
        if (vertexAt < 0 && fragmentAt >= 0)
        {
            // A shader with no vertex stage of its own (the laser beams' vision shader): an empty one.
            text = text.Insert(fragmentAt, "void vertex() {\n}\n");
            (vertexAt, vertexEnd) = FunctionSpan(text, "vertex");
            (fragmentAt, fragmentEnd) = FunctionSpan(text, "fragment");
        }

        if (vertexAt < 0 || fragmentAt < 0 || fragmentAt < vertexEnd) throw new NotSupportedException("vertex() then fragment() expected");

        var shared = new StringBuilder();
        shared.Append(FrameBlock);
        shared.Append(actor == 0 ? WorldModel : ActorModel);
        if (globalValues.Count > 0)
        {
            // Explicit offsets: the block holds only the parameters this shader names, at their places in the one buffer.
            shared.Append("layout(set = 0, binding = 1, std140) uniform SvmGlobals\n{\n");
            foreach ((int place, (string type, string name)) in globalValues)
            {
                shared.Append("    layout(offset = ").Append(place).Append(") ").Append(BlockType(type)).Append(' ').Append(name).Append(";\n");
            }

            shared.Append("} svm_g;\n");
            foreach ((string type, string name) in globalValues.Values) shared.Append(Macro(name, type, "svm_g"));
        }

        foreach ((int place, (string type, string name)) in globalTextures)
        {
            shared.Append("layout(set = 0, binding = ").Append(GlobalTextureBinding + place).Append(") uniform ").Append(type).Append(' ').Append(name).Append(";\n");
        }

        if (values.Count > 0)
        {
            shared.Append("layout(set = 1, binding = 0, std140) uniform SvmMaterial\n{\n");
            foreach (RdMaterialValue v in values)
            {
                shared.Append("    layout(offset = ").Append(v.Offset).Append(") ").Append(BlockType(v.Type)).Append(' ').Append(v.Name).Append(";\n");
            }

            shared.Append("} svm_m;\n");
            foreach (RdMaterialValue v in values) shared.Append(Macro(v.Name, v.Type, "svm_m"));
        }

        foreach (RdMaterialTexture t in textures)
        {
            shared.Append("layout(set = 1, binding = ").Append(t.Binding).Append(") uniform sampler2D ").Append(t.Name).Append(";\n");
        }

        bool skipTransform = modes.Contains("skip_vertex_transform");
        var vertex = new StringBuilder("#version 450\n");
        // Each instance's heat and EMF values, per vertex (the mesh's CUSTOM0, w + 2).
        vertex.Append(visionStream ? "#define SVM_SET_VISION CUSTOM0 = svm_a_vision;\n" : "#define SVM_SET_VISION\n");
        vertex.Append(shared);
        vertex.Append(actor == 0 ? """
            layout(location = 0) in vec3 svm_a_vertex;
            layout(location = 1) in vec2 svm_a_normal;
            layout(location = 2) in vec2 svm_a_uv;
            layout(location = 3) in vec2 svm_a_uv2;
            layout(location = 4) in vec4 svm_a_color;
            layout(location = 5) in float svm_a_echelon;

            """ + (visionStream ? "layout(location = 6) in vec4 svm_a_vision;\n" : "") : """
            layout(location = 0) in vec3 svm_a_vertex;
            layout(location = 1) in vec3 svm_a_normal;
            layout(location = 2) in vec2 svm_a_uv;
            layout(location = 3) in vec4 svm_a_color;

            """);
        if (actor == 1)
        {
            vertex.Append("""
                layout(location = 6) in vec2 svm_a_uv2;
                #define SVM_A_UV2 svm_a_uv2

                """);
        }

        if (actor == 2)
        {
            vertex.Append("""
                #define SVM_A_UV2 vec2(0.0)
                layout(location = 4) in uvec4 svm_a_bones;
                layout(location = 5) in vec4 svm_a_weights;
                layout(set = 2, binding = 1, std430) readonly buffer SvmPalette
                {
                    mat4 svm_bones[];
                };

                """);
        }

        vertex.Append("""
            layout(location = 0) out vec3 svm_v_vertex;
            layout(location = 1) out vec3 svm_v_normal;
            layout(location = 2) out vec2 svm_v_uv;
            layout(location = 3) out vec2 svm_v_uv2;
            layout(location = 4) out vec4 svm_v_color;
            vec3 VERTEX;
            vec3 NORMAL;
            vec3 TANGENT = vec3(0.0);
            vec3 BINORMAL = vec3(0.0);
            vec2 UV;
            vec2 UV2;
            vec4 COLOR;
            vec4 CUSTOM0 = vec4(0.0);
            vec4 CUSTOM1 = vec4(0.0);
            vec4 CUSTOM2 = vec4(0.0);
            vec4 POSITION;
            vec3 NODE_POSITION_VIEW = vec3(0.0);
            invariant gl_Position;
            int svm_draw_dyn_bits = -1;
            // The normal as Godot stores it in a mesh (octahedral, 16 bits a component) and unpacks it.
            vec3 svm_oct_normal(vec2 e)
            {
                vec3 v = vec3(e.xy, 1.0 - abs(e.x) - abs(e.y));
                float t = max(-v.z, 0.0);
                v.xy += t * -sign(v.xy);
                return normalize(v);
            }

            """);
        for (int i = 0; i < varyings.Count; i++)
        {
            vertex.Append("layout(location = ").Append(5 + i).Append(") ").Append(varyings[i].Flat ? "flat " : "").Append("out ")
                .Append(varyings[i].Type).Append(' ').Append(varyings[i].Name).Append(";\n");
        }

        // Everything up to the end of vertex(): the functions after it are the fragment's.
        vertex.Append(StageFunction().Replace(text[..vertexEnd], "void svm_vertex()"));
        vertex.Append("""

            void main()
            {

            """);
        vertex.Append(actor == 0 ? """
                VERTEX = svm_a_vertex;
                NORMAL = svm_oct_normal(svm_a_normal * 2.0 - 1.0);
                UV = svm_a_uv;
                UV2 = svm_a_uv2;
                COLOR = svm_a_color;
                CUSTOM1.x = svm_a_echelon;
                SVM_SET_VISION
                POSITION = PROJECTION_MATRIX * (VIEW_MATRIX * vec4(VERTEX, 1.0));
                svm_vertex();

            """ : (actor == 2 ? """
                svm_model = svm_bones[svm_draw.palette + int(svm_a_bones.x)] * svm_a_weights.x + svm_bones[svm_draw.palette + int(svm_a_bones.y)] * svm_a_weights.y
                    + svm_bones[svm_draw.palette + int(svm_a_bones.z)] * svm_a_weights.z + svm_bones[svm_draw.palette + int(svm_a_bones.w)] * svm_a_weights.w;

            """ : """
                svm_model = svm_objects[svm_draw.object].model;

            """) + """
                VERTEX = svm_a_vertex;
                NORMAL = svm_a_normal;
                UV = svm_a_uv;
                UV2 = SVM_A_UV2;
                COLOR = svm_a_color;
                NODE_POSITION_VIEW = (VIEW_MATRIX * svm_model[3]).xyz;
                POSITION = PROJECTION_MATRIX * (VIEW_MATRIX * (svm_model * vec4(VERTEX, 1.0)));
                svm_vertex();

            """);
        // skip_vertex_transform: vertex() left VERTEX and NORMAL in view space itself.
        vertex.Append(skipTransform
            ? "    svm_v_vertex = VERTEX;\n    svm_v_normal = normalize(NORMAL);\n"
            : actor != 0
                ? "    svm_v_vertex = (VIEW_MATRIX * (svm_model * vec4(VERTEX, 1.0))).xyz;\n    svm_v_normal = normalize(mat3(VIEW_MATRIX) * (mat3(svm_model) * NORMAL));\n"
                : "    svm_v_vertex = (VIEW_MATRIX * vec4(VERTEX, 1.0)).xyz;\n    svm_v_normal = normalize(mat3(VIEW_MATRIX) * NORMAL);\n");
        // Merged map geometry is placed by the one clip matrix (the template's own POSITION is the same product in
        // another order); the sky zone keeps the position its vertex() wrote.
        vertex.Append("    svm_v_uv = UV;\n    svm_v_uv2 = UV2;\n    svm_v_color = COLOR;\n");
        vertex.Append(skipTransform ? "    gl_Position = POSITION;\n}\n"
            // The local player's arms and gun keep the nearest hundredth of the depth range (svm_glow_hue.w, as the
            // template's own POSITION does under SVM_COSMETIC): over the world and over the beam starting inside the gun.
            : actor != 0 ? "    gl_Position = svm_f.clip * (svm_model * vec4(VERTEX, 1.0));\n"
                + "    if (svm_objects[svm_draw.object].glow_hue.w > 0.5) gl_Position.z = mix(0.99 * gl_Position.w, gl_Position.w, gl_Position.z / gl_Position.w);\n}\n" :"    gl_Position = svm_f.clip * vec4(svm_a_vertex, 1.0);\n}\n");

        var fragment = new StringBuilder("#version 450\n");
        fragment.Append(shared);
        if (actor == 0)
        {
            fragment.Append("""
                layout(push_constant, std430) uniform SvmDraw
                {
                    int spot_bits;
                    int dyn_bits;
                    int pad1;
                    int pad2;
                } svm_draw;

                """);
        }

        fragment.Append("""
            layout(location = 0) in vec3 svm_v_vertex;
            layout(location = 1) in vec3 svm_v_normal;
            layout(location = 2) in vec2 svm_v_uv;
            layout(location = 3) in vec2 svm_v_uv2;
            layout(location = 4) in vec4 svm_v_color;
            layout(location = 0) out vec4 svm_frag;
            vec3 VERTEX;
            vec3 NORMAL;
            vec3 VIEW;
            vec2 UV;
            vec2 UV2;
            vec4 COLOR;
            vec4 FRAGCOORD;
            vec2 SCREEN_UV;
            bool FRONT_FACING;
            vec3 ALBEDO = vec3(0.0);
            float ALPHA = 1.0;
            float ALPHA_SCISSOR_THRESHOLD = 0.0;
            float ALPHA_ANTIALIASING_EDGE = 0.0;
            vec2 ALPHA_TEXTURE_COORDINATE = vec2(0.0);
            int svm_draw_spot_bits;
            int svm_draw_dyn_bits;

            """);
        // The slots that are on, limited to those that can reach this draw (the others add exactly nothing).
        if (globalValues.ContainsKey(globals.TryGetValue("svm_spot_bits", out RdGlobal bits) ? bits.Place : -1))
        {
            fragment.Append("#undef svm_spot_bits\n#define svm_spot_bits (svm_g.svm_spot_bits & svm_draw_spot_bits)\n");
        }

        for (int i = 0; i < varyings.Count; i++)
        {
            fragment.Append("layout(location = ").Append(5 + i).Append(") ").Append(varyings[i].Flat ? "flat " : "").Append("in ")
                .Append(varyings[i].Type).Append(' ').Append(varyings[i].Name).Append(";\n");
        }

        fragment.Append(text[..vertexAt]);
        fragment.Append(StageFunction().Replace(text[vertexEnd..], "void svm_fragment()"));
        fragment.Append("""

            void main()
            {
                svm_draw_spot_bits = svm_draw.spot_bits;
                svm_draw_dyn_bits = svm_draw.dyn_bits;
                VERTEX = svm_v_vertex;
                NORMAL = svm_v_normal;

            """);
        if (modes.Contains("cull_disabled")) fragment.Append("    if (!gl_FrontFacing) NORMAL = -NORMAL;\n");
        fragment.Append("""
                VIEW = normalize(-VERTEX);
                UV = svm_v_uv;
                UV2 = svm_v_uv2;
                COLOR = svm_v_color;
                FRAGCOORD = gl_FragCoord;
                SCREEN_UV = gl_FragCoord.xy / VIEWPORT_SIZE;
                FRONT_FACING = gl_FrontFacing;
                svm_fragment();
                if (ALPHA < ALPHA_SCISSOR_THRESHOLD) discard;
                svm_frag = vec4(ALBEDO, ALPHA);
            }

            """);

        return new RdTranslatedShader
        {
            Vertex = vertex.ToString(), Fragment = fragment.ToString(), Values = values, ValueBlockSize = (blockSize + 15) / 16 * 16,
            Textures = textures, RenderModes = modes, Actor = actor, StencilWrite = stencil, VisionStream = visionStream,
        };
    }

    private static string Macro(string name, string type, string block) => type == "bool"
        ? $"#define {name} ({block}.{name} != 0)\n"
        : $"#define {name} {block}.{name}\n";

    private static string BlockType(string type) => type == "bool" ? "int" : type;

    private static bool IsSampler(string type) => type.StartsWith("sampler", StringComparison.Ordinal);

    private static (int Size, int Align) Std140(string type) => type switch
    {
        "float" or "int" or "uint" or "bool" => (4, 4),
        "vec2" or "ivec2" => (8, 8),
        "vec3" or "ivec3" => (12, 16),
        "vec4" or "ivec4" => (16, 16),
        _ => throw new NotSupportedException("material uniform type " + type),
    };

    private static int Components(string type) => type switch
    {
        "vec2" or "ivec2" => 2,
        "vec3" or "ivec3" => 3,
        "vec4" or "ivec4" => 4,
        _ => 1,
    };

    private static double[] DefaultOf(string type, string? text)
    {
        int n = Components(type);
        var value = new double[n];
        if (text is null) return value;
        text = text.Trim();
        if (text is "true" or "false")
        {
            value[0] = text == "true" ? 1 : 0;
            return value;
        }

        MatchCollection numbers = Number().Matches(text.Contains('(') ? text[text.IndexOf('(')..] : text);
        if (numbers.Count == 0) throw new NotSupportedException("uniform default " + text);
        for (int i = 0; i < n; i++)
        {
            // One argument fills every component, as the constructor does.
            value[i] = double.Parse(numbers[numbers.Count == 1 ? 0 : Math.Min(i, numbers.Count - 1)].Value, CultureInfo.InvariantCulture);
        }

        return value;
    }

    private static RdMaterialTexture TextureOf(string name, string hints, int binding)
    {
        // Godot's defaults for a 3D material sampler: linear with mipmaps, repeating.
        RdFilter filter = RdFilter.LinearMipmap;
        bool repeat = true, black = false;
        foreach (string hint in hints.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (hint)
            {
                case "filter_nearest": filter = RdFilter.Nearest; break;
                case "filter_linear": filter = RdFilter.Linear; break;
                case "filter_nearest_mipmap": filter = RdFilter.NearestMipmap; break;
                case "filter_linear_mipmap": filter = RdFilter.LinearMipmap; break;
                case "filter_linear_mipmap_anisotropic": filter = RdFilter.LinearMipmapAnisotropic; break;
                case "repeat_enable": repeat = true; break;
                case "repeat_disable": repeat = false; break;
                case "hint_default_white": black = false; break;
                case "hint_default_black": black = true; break;
                default: throw new NotSupportedException("sampler hint " + hint + " on " + name);
            }
        }

        return new RdMaterialTexture(name, binding, filter, repeat, black);
    }

    private static string StripComment(string line)
    {
        int at = line.IndexOf("//", StringComparison.Ordinal);
        return at < 0 ? line : line[..at];
    }

    /// <summary>The span of "void name() { ... }" in <paramref name="text"/>: its start and the index after its closing brace.</summary>
    private static (int Start, int End) FunctionSpan(string text, string name)
    {
        foreach (RegexMatch m in StageFunction().Matches(text))
        {
            if (m.Groups[1].Value != name) continue;
            int open = text.IndexOf('{', m.Index + m.Length);
            if (open < 0) break;
            int depth = 0;
            for (int i = open; i < text.Length; i++)
            {
                if (text[i] == '{') depth++;
                else if (text[i] == '}' && --depth == 0) return (m.Index, i + 1);
            }
        }

        return (-1, -1);
    }

    /// <summary>
    /// Resolves #include and the conditional directives: the lines that stay, in order. #define and #undef lines stay
    /// (the GLSL compiler expands them); object-like defines are tracked for defined().
    /// </summary>
    private static void Flatten(string code, Func<string, string> include, HashSet<string> defined, HashSet<string> suppressed, List<string> output, int depth)
    {
        if (depth > 8) throw new NotSupportedException("#include nesting");
        // One entry per open conditional: whether its branch is live, whether any branch was taken, the parent's state.
        var stack = new Stack<(bool Live, bool Taken, bool Parent)>();
        bool live = true;
        foreach (string raw in code.Replace("\r\n", "\n").Split('\n'))
        {
            RegexMatch d = Directive().Match(StripComment(raw));
            if (!d.Success)
            {
                if (live) output.Add(raw);
                continue;
            }

            string keyword = d.Groups[1].Value, rest = d.Groups[2].Value;
            switch (keyword)
            {
                case "ifdef":
                case "ifndef":
                case "if":
                {
                    bool value = live && (keyword == "if" ? Evaluate(rest, defined) : defined.Contains(rest) == (keyword == "ifdef"));
                    stack.Push((value, value, live));
                    live = value;
                    break;
                }

                case "elif":
                case "else":
                {
                    if (stack.Count == 0) throw new NotSupportedException("#" + keyword + " without #if");
                    (bool _, bool taken, bool parent) = stack.Pop();
                    bool value = parent && !taken && (keyword == "else" || Evaluate(rest, defined));
                    stack.Push((value, taken || value, parent));
                    live = value;
                    break;
                }

                case "endif":
                    if (stack.Count == 0) throw new NotSupportedException("#endif without #if");
                    live = stack.Pop().Parent;
                    break;
                case "include":
                    if (live)
                    {
                        RegexMatch path = Include().Match(raw);
                        if (!path.Success) throw new NotSupportedException(raw);
                        Flatten(include(path.Groups[1].Value), include, defined, suppressed, output, depth + 1);
                    }

                    break;
                case "define":
                    if (live)
                    {
                        int end = 0;
                        while (end < rest.Length && (char.IsLetterOrDigit(rest[end]) || rest[end] == '_')) end++;
                        if (suppressed.Contains(rest[..end])) break;
                        defined.Add(rest[..end]);
                        output.Add(raw);
                    }

                    break;
                case "undef":
                    if (live)
                    {
                        defined.Remove(rest);
                        output.Add(raw);
                    }

                    break;
                case "error":
                    if (live) throw new NotSupportedException("#error " + rest);
                    break;
                default:
                    if (live) output.Add(raw);
                    break;
            }
        }

        if (stack.Count != 0) throw new NotSupportedException("unterminated #if");
    }

    /// <summary>A conditional expression of defined(NAME), !, &amp;&amp;, || and parentheses.</summary>
    private static bool Evaluate(string expression, HashSet<string> defined)
    {
        int at = 0;
        bool value = Or(expression, ref at, defined);
        SkipSpace(expression, ref at);
        if (at != expression.Length) throw new NotSupportedException("#if " + expression);
        return value;
    }

    private static bool Or(string s, ref int at, HashSet<string> defined)
    {
        bool value = And(s, ref at, defined);
        while (Take(s, ref at, "||")) value |= And(s, ref at, defined);
        return value;
    }

    private static bool And(string s, ref int at, HashSet<string> defined)
    {
        bool value = Unary(s, ref at, defined);
        while (Take(s, ref at, "&&")) value &= Unary(s, ref at, defined);
        return value;
    }

    private static bool Unary(string s, ref int at, HashSet<string> defined)
    {
        if (Take(s, ref at, "!")) return !Unary(s, ref at, defined);
        if (Take(s, ref at, "("))
        {
            bool inner = Or(s, ref at, defined);
            if (!Take(s, ref at, ")")) throw new NotSupportedException("#if " + s);
            return inner;
        }

        string word = Word(s, ref at);
        if (word == "defined")
        {
            bool parenthesis = Take(s, ref at, "(");
            string name = Word(s, ref at);
            if (parenthesis && !Take(s, ref at, ")")) throw new NotSupportedException("#if " + s);
            return defined.Contains(name);
        }

        if (word is "0" or "1") return word == "1";
        throw new NotSupportedException("#if " + s);
    }

    private static void SkipSpace(string s, ref int at)
    {
        while (at < s.Length && char.IsWhiteSpace(s[at])) at++;
    }

    private static bool Take(string s, ref int at, string token)
    {
        SkipSpace(s, ref at);
        if (string.CompareOrdinal(s, at, token, 0, token.Length) != 0) return false;
        at += token.Length;
        return true;
    }

    private static string Word(string s, ref int at)
    {
        SkipSpace(s, ref at);
        int start = at;
        while (at < s.Length && (char.IsLetterOrDigit(s[at]) || s[at] == '_')) at++;
        if (at == start) throw new NotSupportedException("#if " + s);
        return s[start..at];
    }
}
