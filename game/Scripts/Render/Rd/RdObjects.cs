using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Godot;
using OpenScctGame.Render.Materials;

namespace OpenScctGame.Render.Rd;

/// <summary>
/// Meshes outside the merged world in the own frame (SVM_RD_FRAME=1): the map's gameplay meshes and the characters,
/// with solid or masked template materials. Each surface is drawn as the original draws an actor
/// (docs/PERFORMANCE-LOWEND-2026-11.md, "The states it draws with"): its texture times a light worked out at each
/// vertex (the baked vertex colour, or the actor's ambient and up to four lights), doubled. Skinned meshes are posed in
/// the vertex shader from their skeleton's bones. Blended surfaces, cloaks, tints and meshes rewritten in place are
/// not drawn yet.
/// </summary>
internal sealed class RdObjects
{
    /// <summary>36 bytes: position, normal, UV, colour. Skinned: 60 bytes, with four bone indices and weights.</summary>
    public const int Stride = 36, SkinnedStride = 60;

    /// <summary>Bytes of one object's block: model matrix, ambient, four lights (position, colour, spot).</summary>
    public const int ObjectBytes = 64 + 16 + (4 * 48);

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
        /// <summary>0 solid, 1 blended by the texture's alpha, 2 added.</summary>
        public int Blend;
        /// <summary>The spot slots whose sphere reaches the mesh (0: its shader holds no spot code).</summary>
        public int SpotBits;
    }

    private sealed class SurfaceInfo
    {
        public int Id;
        public bool Uploaded;
    }

    private sealed class SkinInfo
    {
        public required Skeleton3D Skeleton;
        public required int[] Bones;
        public required Transform3D[] Binds;
    }

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
    private readonly Stack<Node> _stack = new();
    private readonly List<MeshInstance3D> _nodes = [];
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
    public void Gather(Node root, Plane[] planes, Func<Plane[], Aabb, bool> inside, Func<Aabb, int> spots, List<Draw> draws, List<byte> objects, List<float> palette, List<Upload> uploads)
    {
        Span<float> block = stackalloc float[ObjectBytes / 4];
        if (Dirty || !ReferenceEquals(root, _walked))
        {
            // The scene's mesh nodes, walked again only after a node entered or left the tree.
            Dirty = false;
            _walked = root;
            _nodes.Clear();
            _stack.Clear();
            _stack.Push(root);
            while (_stack.Count > 0)
            {
                Node at = _stack.Pop();
                // The merged world is the world pass's own; cloth is rewritten in place every frame and not handled yet.
                if (at.Name == s_static || at.Name == s_softBodies) continue;
                foreach (Node child in at.GetChildren()) _stack.Push(child);
                if (at is MeshInstance3D found) _nodes.Add(found);
            }
        }

        foreach (MeshInstance3D node in _nodes)
        {
            if (!GodotObject.IsInstanceValid(node) || node.Mesh is not ArrayMesh mesh || !node.IsVisibleInTree()) continue;
            // Actors, characters and the first-person model. Effects (their own shaders) and a body hidden from its
            // own eyes (the shadow-only layer) are not this pass's.
            if ((node.Layers & (RenderLayers.World | RenderLayers.FirstPerson)) == 0) continue;
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
            // A posed mesh leaves its rest box: it is culled by a generous one.
            Aabb box = model * mesh.GetAabb();
            if (!inside(planes, skin is null ? box : box.Grow(1.5f))) continue;
            int objectIndex = -1, paletteBase = -1;
            int spotBits = spots(skin is null ? box : box.Grow(1.5f));
            for (int s = 0; s < mesh.GetSurfaceCount(); s++)
            {
                if ((node.MaterialOverride ?? node.GetSurfaceOverrideMaterial(s) ?? mesh.SurfaceGetMaterial(s)) is not ShaderMaterial material) continue;
                if (!_materials.TryGetValue(material, out (int Kind, bool Masked, bool TwoSided, bool Drawn, Rid Texture, int Blend) m))
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
                        for (int i = 0; i < skin.Bones.Length; i++)
                        {
                            Transform3D bone = skin.Bones[i] >= 0 ? skeleton * skin.Skeleton.GetBoneGlobalPose(skin.Bones[i]) * skin.Binds[i] : model;
                            Basis b = bone.Basis;
                            palette.AddRange([b.X.X, b.X.Y, b.X.Z, 0f, b.Y.X, b.Y.Y, b.Y.Z, 0f, b.Z.X, b.Z.Y, b.Z.Z, 0f, bone.Origin.X, bone.Origin.Y, bone.Origin.Z, 1f]);
                        }
                    }

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

                draws.Add(new Draw
                {
                    Surface = surface.Id, Object = objectIndex, Kind = m.Kind, Palette = paletteBase, Masked = m.Masked, TwoSided = m.TwoSided, Texture = m.Texture,
                    Blend = m.Blend, SpotBits = m.Kind == 0 ? 0 : spotBits,
                });
            }
        }
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
    private static (int Kind, bool Masked, bool TwoSided, bool Drawn, Rid Texture, int Blend) Describe(ShaderMaterial material)
    {
        if (material.Shader is not { } shader) return default;
        string code = ResponsiveSmokeShaders.BaseCode(shader);
        // Translucent and additive surfaces are blended by the texture's alpha; multiplied ones are not drawn yet.
        bool blended = code.Contains("#define BLEND_MODULATE", StringComparison.Ordinal) || code.Contains("#define BLEND_DARKEN", StringComparison.Ordinal);
        int blend = code.Contains("#define BLEND_ADDITIVE", StringComparison.Ordinal) ? 2 : code.Contains("#define BLEND_TRANSLUCENT", StringComparison.Ordinal) ? 1 : 0;
        // A cloaked body is not drawn at all for now: unseen is nearer the truth than plainly visible.
        if (blended || code.Contains("#define SVM_CLOAK", StringComparison.Ordinal) || !code.Contains("svm_world.gdshaderinc", StringComparison.Ordinal)) return default;
        int kind = code.Contains("#define LIGHT_ACTOR", StringComparison.Ordinal) ? 2
            : code.Contains("#define LIGHT_VERTEX", StringComparison.Ordinal) || code.Contains("#define LIGHT_LIGHTMAP", StringComparison.Ordinal) ? 1 : 0;
        Rid texture = material.GetShaderParameter(s_diffuse).AsGodotObject() is Texture2D t ? RenderingServer.TextureGetRdTexture(t.GetRid(), false) : default;
        return (kind, code.Contains("#define BLEND_MASKED", StringComparison.Ordinal), code.Contains("cull_disabled", StringComparison.Ordinal), true, texture, blend);
    }

    private static Upload? Pack(ArrayMesh mesh, int surface, int id, bool skinned)
    {
        Godot.Collections.Array arrays = mesh.SurfaceGetArrays(surface);
        Vector3[] positions = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        if (positions.Length == 0) return null;
        Vector3[] normals = arrays[(int)Mesh.ArrayType.Normal].VariantType == Variant.Type.Nil ? [] : arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
        Vector2[] uvs = arrays[(int)Mesh.ArrayType.TexUV].VariantType == Variant.Type.Nil ? [] : arrays[(int)Mesh.ArrayType.TexUV].AsVector2Array();
        Color[] colors = arrays[(int)Mesh.ArrayType.Color].VariantType == Variant.Type.Nil ? [] : arrays[(int)Mesh.ArrayType.Color].AsColorArray();
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
            v[35] = 255;
            if (!skinned) continue;
            // The first four influences (the heaviest come first in a Godot mesh), renormalised.
            Span<ushort> b = MemoryMarshal.Cast<byte, ushort>(v.Slice(36, 8));
            Span<float> w = MemoryMarshal.Cast<byte, float>(v.Slice(44, 16));
            float sum = 0f;
            for (int k = 0; k < 4; k++)
            {
                b[k] = (ushort)Math.Max(0, bones[(i * perVertex) + k]);
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
            int palette;
            // Bits 0-15: the spot slots that are on and reach the mesh; bits 16-31: those of them that are dynamic lights.
            int spot_bits;
        } draw;

        """;


    /// <summary>
    /// The spot shadow lights at a point of an actor: svm_spot_lookup, svm_spot_shadow and svm_spot_slot of
    /// svm_world.gdshaderinc, for the slots the draw names (those that are on and whose sphere reaches the mesh).
    /// </summary>
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
            int kind = draw.kind & 255;
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
                v_light = min(sum, vec3(2.0));
            } else if (kind == 1) {
                v_light = a_col.rgb * 2.0;
            } else {
                v_light = vec3(1.0);
            }
            v_rel = wp.xyz - frame.eye.xyz;
            gl_Position = frame.world * wp;
        }

        """;

    public static string Fragment(bool masked, bool spot) => "#version 450\n" + Common + (spot ? Spot : "") + """
        layout(set = 1, binding = 0) uniform sampler2D tex_diffuse;
        layout(location = 0) in vec2 v_uv;
        layout(location = 1) in vec3 v_light;
        layout(location = 2) in vec3 v_rel;
        layout(location = 0) out vec4 frag;

        void main()
        {
            vec4 t = texture(tex_diffuse, v_uv);

        """ + (masked ? "    if (t.a < 0.5) discard;\n" : "")
        // The template: texture x min(actor light + spot lights, 2); the Echelon code rides in the ambient's w.
        + (spot ? "    vec3 c = t.rgb * min(v_light + spot_light(v_wpos, normalize(v_wn), int(objects[draw.object].ambient.w + 0.5)), vec3(2.0));\n"
            : "    vec3 c = t.rgb * v_light;\n") + """
            int blend = draw.kind >> 8;
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
