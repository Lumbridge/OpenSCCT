using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Godot;
using OpenScctGame.Render.Materials;
using RD = Godot.RenderingDevice;

namespace OpenScctGame.Render.Rd;

/// <summary>
/// The map's gameplay meshes in the own frame (SVM_RD_FRAME=1): rigid objects with solid or masked materials, each
/// surface drawn as the original draws an actor (docs/PERFORMANCE-LOWEND-2026-11.md, "The states it draws with"): its
/// texture times a light worked out at each vertex (the baked vertex colour, or the actor's ambient and up to four
/// lights), doubled. Blended surfaces, skinned meshes and meshes rewritten in place are not drawn yet.
/// </summary>
internal sealed class RdObjects
{
    /// <summary>36 bytes: position, normal, UV, colour.</summary>
    public const int Stride = 36;

    /// <summary>Bytes of one object's block: model matrix, ambient, four lights (position, colour, spot).</summary>
    public const int ObjectBytes = 64 + 16 + (4 * 48);

    public sealed class Upload
    {
        public required int Surface;
        public required byte[] Vertices;
        public required uint[] Indices;
        public required int VertexCount;
    }

    public struct Draw
    {
        public int Surface, Object, Kind;
        public bool Masked, TwoSided;
        public Rid Texture;
    }

    private sealed class SurfaceInfo
    {
        public int Id;
        public bool Uploaded;
    }

    private static readonly StringName s_diffuse = "tex_diffuse";
    private static readonly StringName s_ambient = "svm_ambient";
    private static readonly StringName[] s_pos = [new("svm_l0_pos"), new("svm_l1_pos"), new("svm_l2_pos"), new("svm_l3_pos")];
    private static readonly StringName[] s_col = [new("svm_l0_col"), new("svm_l1_col"), new("svm_l2_col"), new("svm_l3_col")];
    private static readonly StringName[] s_spot = [new("svm_l0_spot"), new("svm_l1_spot"), new("svm_l2_spot"), new("svm_l3_spot")];

    private readonly Dictionary<(Mesh, int), SurfaceInfo> _surfaces = [];
    private readonly Dictionary<Material, (int Kind, bool Masked, bool TwoSided, bool Drawn, Rid Texture)> _materials = [];
    private int _nextSurface;

    /// <summary>
    /// Fills <paramref name="draws"/>, <paramref name="objects"/> and <paramref name="uploads"/> with the visible meshes
    /// under <paramref name="root"/> (main thread).
    /// </summary>
    public void Gather(Node root, Plane[] planes, Func<Plane[], Aabb, bool> inside, List<Draw> draws, List<byte> objects, List<Upload> uploads)
    {
        draws.Clear();
        objects.Clear();
        uploads.Clear();
        Span<float> block = stackalloc float[ObjectBytes / 4];
        foreach (Node child in root.GetChildren())
        {
            if (child is not MeshInstance3D { Mesh: ArrayMesh mesh } node || !node.IsVisibleInTree() || node.Skin is not null) continue;
            Transform3D model = node.GlobalTransform;
            if (!inside(planes, model * mesh.GetAabb())) continue;
            int objectIndex = -1;
            for (int s = 0; s < mesh.GetSurfaceCount(); s++)
            {
                if ((node.GetSurfaceOverrideMaterial(s) ?? mesh.SurfaceGetMaterial(s)) is not ShaderMaterial material) continue;
                if (!_materials.TryGetValue(material, out (int Kind, bool Masked, bool TwoSided, bool Drawn, Rid Texture) m))
                {
                    m = Describe(material);
                    _materials[material] = m;
                }

                if (!m.Drawn) continue;
                if (!_surfaces.TryGetValue((mesh, s), out SurfaceInfo? surface))
                {
                    surface = new SurfaceInfo { Id = _nextSurface++ };
                    _surfaces[(mesh, s)] = surface;
                }

                if (!surface.Uploaded)
                {
                    surface.Uploaded = true;
                    if (Pack(mesh, s, surface.Id) is { } upload) uploads.Add(upload);
                    else surface.Id = -1;
                }

                if (surface.Id < 0) continue;
                if (objectIndex < 0)
                {
                    objectIndex = objects.Count / ObjectBytes;
                    block.Clear();
                    Basis b = model.Basis;
                    (block[0], block[1], block[2]) = (b.X.X, b.X.Y, b.X.Z);
                    (block[4], block[5], block[6]) = (b.Y.X, b.Y.Y, b.Y.Z);
                    (block[8], block[9], block[10]) = (b.Z.X, b.Z.Y, b.Z.Z);
                    (block[12], block[13], block[14], block[15]) = (model.Origin.X, model.Origin.Y, model.Origin.Z, 1f);
                    if (m.Kind == 2)
                    {
                        Put(block, 16, node.GetInstanceShaderParameter(s_ambient));
                        for (int i = 0; i < 4; i++)
                        {
                            Put(block, 20 + (i * 12), node.GetInstanceShaderParameter(s_pos[i]));
                            Put(block, 24 + (i * 12), node.GetInstanceShaderParameter(s_col[i]));
                            Put(block, 28 + (i * 12), node.GetInstanceShaderParameter(s_spot[i]));
                        }
                    }

                    objects.AddRange(MemoryMarshal.AsBytes(block));
                }

                draws.Add(new Draw { Surface = surface.Id, Object = objectIndex, Kind = m.Kind, Masked = m.Masked, TwoSided = m.TwoSided, Texture = m.Texture });
            }
        }
    }

    private static void Put(Span<float> block, int at, Variant value)
    {
        if (value.VariantType == Variant.Type.Nil) return;
        Vector4 v = value.VariantType == Variant.Type.Color
            ? new Vector4(value.AsColor().R, value.AsColor().G, value.AsColor().B, value.AsColor().A) : value.AsVector4();
        (block[at], block[at + 1], block[at + 2], block[at + 3]) = (v.X, v.Y, v.Z, v.W);
    }

    /// <summary>How a template material draws here: 0 fullbright, 1 baked vertex colour, 2 actor light; not drawn when blended.</summary>
    private static (int Kind, bool Masked, bool TwoSided, bool Drawn, Rid Texture) Describe(ShaderMaterial material)
    {
        if (material.Shader is not { } shader) return default;
        string code = ResponsiveSmokeShaders.BaseCode(shader);
        bool blended = code.Contains("#define BLEND_TRANSLUCENT", StringComparison.Ordinal) || code.Contains("#define BLEND_ADDITIVE", StringComparison.Ordinal)
            || code.Contains("#define BLEND_MODULATE", StringComparison.Ordinal) || code.Contains("#define BLEND_DARKEN", StringComparison.Ordinal);
        if (blended || !code.Contains("svm_world.gdshaderinc", StringComparison.Ordinal)) return default;
        int kind = code.Contains("#define LIGHT_ACTOR", StringComparison.Ordinal) ? 2
            : code.Contains("#define LIGHT_VERTEX", StringComparison.Ordinal) || code.Contains("#define LIGHT_LIGHTMAP", StringComparison.Ordinal) ? 1 : 0;
        Rid texture = material.GetShaderParameter(s_diffuse).AsGodotObject() is Texture2D t ? RenderingServer.TextureGetRdTexture(t.GetRid(), false) : default;
        return (kind, code.Contains("#define BLEND_MASKED", StringComparison.Ordinal), code.Contains("cull_disabled", StringComparison.Ordinal), true, texture);
    }

    private static Upload? Pack(ArrayMesh mesh, int surface, int id)
    {
        Godot.Collections.Array arrays = mesh.SurfaceGetArrays(surface);
        Vector3[] positions = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        if (positions.Length == 0) return null;
        Vector3[] normals = arrays[(int)Mesh.ArrayType.Normal].VariantType == Variant.Type.Nil ? [] : arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
        Vector2[] uvs = arrays[(int)Mesh.ArrayType.TexUV].VariantType == Variant.Type.Nil ? [] : arrays[(int)Mesh.ArrayType.TexUV].AsVector2Array();
        Color[] colors = arrays[(int)Mesh.ArrayType.Color].VariantType == Variant.Type.Nil ? [] : arrays[(int)Mesh.ArrayType.Color].AsColorArray();
        int[] indices = arrays[(int)Mesh.ArrayType.Index].VariantType == Variant.Type.Nil ? [] : arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
        var bytes = new byte[positions.Length * Stride];
        for (int i = 0; i < positions.Length; i++)
        {
            Span<byte> v = bytes.AsSpan(i * Stride, Stride);
            MemoryMarshal.Write(v, in positions[i]);
            Vector3 n = i < normals.Length ? normals[i] : Vector3.Up;
            MemoryMarshal.Write(v[12..], in n);
            Vector2 uv = i < uvs.Length ? uvs[i] : Vector2.Zero;
            MemoryMarshal.Write(v[24..], in uv);
            Color c = i < colors.Length ? colors[i] : Colors.White;
            v[32] = (byte)Math.Clamp((int)MathF.Round(c.R * 255f), 0, 255);
            v[33] = (byte)Math.Clamp((int)MathF.Round(c.G * 255f), 0, 255);
            v[34] = (byte)Math.Clamp((int)MathF.Round(c.B * 255f), 0, 255);
            v[35] = 255;
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

        return new Upload { Surface = id, Vertices = bytes, Indices = index, VertexCount = positions.Length };
    }

    // ---- Render thread ----

    public const string Common = """
        #version 450
        layout(set = 0, binding = 0, std140) uniform Frame
        {
            mat4 world;
            mat4 sky;
            vec4 params;
        } frame;
        struct Object
        {
            mat4 model;
            vec4 ambient;
            vec4 light[12];
        };
        layout(set = 0, binding = 1, std430) readonly buffer Objects
        {
            Object objects[];
        };
        layout(push_constant, std430) uniform Draw
        {
            int object;
            int kind;
            int pad0;
            int pad1;
        } draw;

        """;

    // svm_actor_light of svm_world.gdshaderinc, at the vertex as the template's LIGHT_ACTOR does it.
    public const string Vertex = Common + """
        layout(location = 0) in vec3 a_pos;
        layout(location = 1) in vec3 a_normal;
        layout(location = 2) in vec2 a_uv;
        layout(location = 3) in vec4 a_col;
        layout(location = 0) out vec2 v_uv;
        layout(location = 1) out vec3 v_light;

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
            mat4 model = objects[draw.object].model;
            vec4 wp = model * vec4(a_pos, 1.0);
            v_uv = a_uv;
            if (draw.kind == 2) {
                vec3 wn = normalize(mat3(model) * a_normal);
                vec3 sum = objects[draw.object].ambient.rgb;
                for (int i = 0; i < 4; i++) {
                    sum += actor_light(objects[draw.object].light[i * 3], objects[draw.object].light[i * 3 + 1], objects[draw.object].light[i * 3 + 2], wp.xyz, wn);
                }
                v_light = min(sum, vec3(2.0));
            } else if (draw.kind == 1) {
                v_light = a_col.rgb * 2.0;
            } else {
                v_light = vec3(1.0);
            }
            gl_Position = frame.world * wp;
        }

        """;

    public static string Fragment(bool masked) => Common + """
        layout(set = 1, binding = 0) uniform sampler2D tex_diffuse;
        layout(location = 0) in vec2 v_uv;
        layout(location = 1) in vec3 v_light;
        layout(location = 0) out vec4 frag;

        void main()
        {
            vec4 t = texture(tex_diffuse, v_uv);

        """ + (masked ? "    if (t.a < 0.5) discard;\n" : "") + """
            frag = vec4(min(t.rgb * v_light, vec3(1.0)), 1.0);
        }

        """;
}
