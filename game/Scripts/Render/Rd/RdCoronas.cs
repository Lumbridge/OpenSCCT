using System.Collections.Generic;
using Godot;

namespace OpenScctGame.Render.Rd;

/// <summary>
/// Light coronas in the own frame (the default; SVM_RD_FRAME=0 turns it off): the camera-facing quads the Coronas node shows this frame, drawn
/// after the scene with the original's blend (ONE, INVSRCCOLOR: the quad adds to what the frame leaves of white) and
/// svm_corona.gdshaderinc's visibility rule, tested once a corona at its centre against the frame's depth.
/// </summary>
internal sealed class RdCoronas
{
    /// <summary>Floats of one corona: centre and half size, colour and roll.</summary>
    public const int BlockFloats = 8;

    public struct Group
    {
        public Rid Texture;
        public int First, Count;
    }

    private static readonly StringName s_corona = "svm_corona";
    private static readonly StringName s_colour = "svm_corona_colour";
    private static readonly StringName s_texture = "corona_tex";
    private readonly Dictionary<Material, Rid> _textures = [];
    private readonly List<(Rid Texture, Vector3 Centre, Vector4 Shape, Vector4 Colour)> _visible = [];
    private Node? _root;
    private bool _searched;

    /// <summary>The coronas shown this frame, grouped by texture (main thread).</summary>
    public void Gather(Node scene, List<Group> groups, List<float> blocks)
    {
        groups.Clear();
        blocks.Clear();
        if (!_searched || (_root is not null && !GodotObject.IsInstanceValid(_root)))
        {
            _searched = true;
            _root = scene.FindChild("Coronas", true, false);
        }

        if (_root is null) return;
        _visible.Clear();
        foreach (Node child in _root.GetChildren())
        {
            if (child is not MeshInstance3D { Visible: true, MaterialOverride: ShaderMaterial material } node) continue;
            if (!_textures.TryGetValue(material, out Rid texture))
            {
                texture = material.GetShaderParameter(s_texture).AsGodotObject() is Texture2D t ? RenderingServer.TextureGetRdTexture(t.GetRid(), false) : default;
                _textures[material] = texture;
            }

            Variant shape = node.GetInstanceShaderParameter(s_corona), colour = node.GetInstanceShaderParameter(s_colour);
            if (shape.VariantType == Variant.Type.Nil || colour.VariantType == Variant.Type.Nil) continue;
            _visible.Add((texture, node.GlobalPosition, shape.AsVector4(), colour.AsVector4()));
        }

        _visible.Sort(static (a, b) => a.Texture.Id.CompareTo(b.Texture.Id));
        for (int i = 0; i < _visible.Count; i++)
        {
            (Rid texture, Vector3 centre, Vector4 shape, Vector4 colour) = _visible[i];
            blocks.AddRange([centre.X, centre.Y, centre.Z, shape.X, colour.X, colour.Y, colour.Z, shape.Y]);
            if (groups.Count > 0 && groups[^1].Texture == texture) groups[^1] = groups[^1] with { Count = groups[^1].Count + 1 };
            else groups.Add(new Group { Texture = texture, First = i, Count = 1 });
        }
    }

    // ---- Render thread ----

    private const string Common = """
        #version 450
        layout(set = 0, binding = 0, std140) uniform Frame
        {
            mat4 world;
            mat4 sky;
            // x lightmap scale, y time, z and w the projection's depth terms (view z = -w / (ndc + z)).
            vec4 params;
            vec4 eye;
            vec4 fog_color;
            vec4 fog;
        } frame;
        struct Corona
        {
            vec4 centre;  // xyz world, w half size in metres
            vec4 colour;  // rgb display colour x 0.5 x fade, w roll in radians
        };
        layout(set = 0, binding = 1, std430) readonly buffer Coronas
        {
            Corona coronas[];
        };
        layout(push_constant, std430) uniform Draw
        {
            int first;
            int pad0;
            // The share of the depth range the world is drawn into, and where it starts.
            float depth_scale;
            float depth_bias;
        } draw;

        """;

    public const string Vertex = Common + """
        layout(set = 0, binding = 2) uniform sampler2D scene_depth;
        layout(location = 0) out vec2 v_uv;
        layout(location = 1) out vec3 v_colour;

        float view_depth(float stored)
        {
            float ndc = 1.0 - 2.0 * (stored - draw.depth_bias) / draw.depth_scale;
            return frame.params.w / (ndc + frame.params.z);
        }

        void main()
        {
            Corona c = coronas[draw.first + gl_InstanceIndex];
            vec2 corner = vec2(float(gl_VertexIndex == 1 || gl_VertexIndex == 2 || gl_VertexIndex == 4), float(gl_VertexIndex == 2 || gl_VertexIndex == 4 || gl_VertexIndex == 5)) * 2.0 - 1.0;
            vec4 clip = frame.world * vec4(c.centre.xyz, 1.0);
            v_uv = corner * vec2(0.5, -0.5) + 0.5;
            float cover = 0.0;
            if (frame.fog_color.a > 0.5) {
                cover = clamp((max(length(c.centre.xyz - frame.eye.xyz) * frame.fog.x, frame.fog.y) - frame.fog.y) * frame.fog.z, 0.0, 1.0);
            }
            v_colour = c.colour.rgb * (1.0 - cover);
            bool hidden = clip.w <= 0.0;
            if (!hidden) {
                vec2 uv = clip.xy / clip.w * 0.5 + 0.5;
                if (uv.x >= 0.0 && uv.x <= 1.0 && uv.y >= 0.0 && uv.y <= 1.0) {
                    // The frame's depth at the light's centre must not lie in front of the light by more than
                    // max(16 uu, 8% of the distance).
                    float scene = view_depth(textureLod(scene_depth, uv, 0.0).r);
                    float light = view_depth(clip.z / clip.w);
                    hidden = scene < light - max(0.16, 0.08 * light);
                }
            }
            if (hidden) {
                gl_Position = vec4(2.0, 2.0, 2.0, 1.0);
                return;
            }
            // A quad in the screen's plane: half size in metres at the centre's depth, rolled.
            float cs = cos(c.colour.w), sn = sin(c.colour.w);
            vec2 r = vec2(corner.x * cs - corner.y * sn, corner.x * sn + corner.y * cs) * c.centre.w;
            // The clip transform's x and y scales are the projection's (its view part is a rotation).
            float sx = length(vec3(frame.world[0][0], frame.world[1][0], frame.world[2][0]));
            float sy = length(vec3(frame.world[0][1], frame.world[1][1], frame.world[2][1]));
            gl_Position = clip + vec4(r.x * sx, -r.y * sy, 0.0, 0.0);
        }

        """;

    public const string Fragment = Common + """
        layout(set = 1, binding = 0) uniform sampler2D corona_tex;
        layout(location = 0) in vec2 v_uv;
        layout(location = 1) in vec3 v_colour;
        layout(location = 0) out vec4 frag;

        void main()
        {
            frag = vec4(texture(corona_tex, v_uv).rgb * v_colour, 1.0);
        }

        """;
}
