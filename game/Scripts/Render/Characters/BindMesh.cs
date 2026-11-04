using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text.Json;
using OpenScctGame.Render.Gltf;

namespace OpenScctGame.Render.Characters;

/// <summary>
/// A skinned glb in its bind pose, for moving textures from one mesh onto another of the same rig
/// (<see cref="SurfaceTransfer"/>): the triangles of every material with their corners' positions, normals, UVs and
/// bone weights, and the skin's joints with their bind matrices. Values stay in the file's own space.
/// </summary>
internal sealed class BindMesh
{
    private BindMesh(string[] materials, string?[] images, Vector3[] positions, Vector3[] normals, Vector2[] uvs, int[] joints,
        float[] weights, int[] corners, int[] triangleMaterials, Joint[] skeleton)
    {
        Materials = materials;
        Images = images;
        Positions = positions;
        Normals = normals;
        Uvs = uvs;
        Joints = joints;
        Weights = weights;
        Corners = corners;
        TriangleMaterials = triangleMaterials;
        Skeleton = skeleton;
    }

    /// <summary>A joint of the skin: its name, parent joint (-1 for a root) and bind matrices (row-vector convention).</summary>
    internal readonly record struct Joint(string Name, int Parent, Matrix4x4 InverseBind, Matrix4x4 Bind);

    /// <summary>Material names by glTF material index.</summary>
    public string[] Materials { get; }

    /// <summary>The base colour image of each material (a URI relative to the file), or null.</summary>
    public string?[] Images { get; }

    public Vector3[] Positions { get; }

    public Vector3[] Normals { get; }

    public Vector2[] Uvs { get; }

    /// <summary>Four joints per vertex (indices into <see cref="Skeleton"/>).</summary>
    public int[] Joints { get; }

    /// <summary>Four weights per vertex, beside <see cref="Joints"/>.</summary>
    public float[] Weights { get; }

    /// <summary>Three vertex indices per triangle.</summary>
    public int[] Corners { get; }

    /// <summary>The material index of each triangle.</summary>
    public int[] TriangleMaterials { get; }

    public Joint[] Skeleton { get; }

    public int TriangleCount => TriangleMaterials.Length;

    public int MaterialIndex(string name) => Array.FindIndex(Materials, m => string.Equals(m, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Reads the skinned meshes of <paramref name="path"/>; throws <see cref="InvalidDataException"/> when it has none.</summary>
    public static BindMesh Load(string path)
    {
        using GlbDocument glb = GlbDocument.Load(path);
        JsonElement root = glb.Root;
        if (!root.TryGetProperty("skins", out JsonElement skins) || skins.GetArrayLength() == 0)
        {
            throw new InvalidDataException($"{path}: no skin");
        }

        JsonElement nodes = root.GetProperty("nodes");
        Joint[] skeleton = ReadSkeleton(glb, nodes, skins[0]);
        (string[] materials, string?[] images) = ReadMaterials(root);

        var positions = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var joints = new List<int>();
        var weights = new List<float>();
        var corners = new List<int>();
        var triangleMaterials = new List<int>();
        // Primitives of one mesh normally share their vertex accessors and differ by indices: read each set once.
        var bases = new Dictionary<int, int>();
        foreach (JsonElement node in nodes.EnumerateArray())
        {
            if (!node.TryGetProperty("mesh", out JsonElement meshIndex) || !node.TryGetProperty("skin", out _))
            {
                continue;
            }

            JsonElement mesh = root.GetProperty("meshes")[meshIndex.GetInt32()];
            foreach (JsonElement primitive in mesh.GetProperty("primitives").EnumerateArray())
            {
                JsonElement attributes = primitive.GetProperty("attributes");
                if (!attributes.TryGetProperty("POSITION", out JsonElement position) || !attributes.TryGetProperty("TEXCOORD_0", out JsonElement uv) ||
                    !attributes.TryGetProperty("JOINTS_0", out JsonElement joint) || !attributes.TryGetProperty("WEIGHTS_0", out JsonElement weight) ||
                    !primitive.TryGetProperty("indices", out JsonElement indices) || !primitive.TryGetProperty("material", out JsonElement material))
                {
                    continue;
                }

                if (primitive.TryGetProperty("mode", out JsonElement mode) && mode.GetInt32() != 4)
                {
                    continue;
                }

                if (!bases.TryGetValue(position.GetInt32(), out int first))
                {
                    first = positions.Count;
                    bases[position.GetInt32()] = first;
                    float[] p = glb.ReadFloats(position.GetInt32());
                    float[] t = glb.ReadFloats(uv.GetInt32());
                    float[]? n = attributes.TryGetProperty("NORMAL", out JsonElement normal) ? glb.ReadFloats(normal.GetInt32()) : null;
                    for (int v = 0; v < p.Length / 3; v++)
                    {
                        positions.Add(new Vector3(p[v * 3], p[(v * 3) + 1], p[(v * 3) + 2]));
                        normals.Add(n is null ? Vector3.Zero : new Vector3(n[v * 3], n[(v * 3) + 1], n[(v * 3) + 2]));
                        uvs.Add(new Vector2(t[v * 2], t[(v * 2) + 1]));
                    }

                    joints.AddRange(glb.ReadIntegers(joint.GetInt32()));
                    weights.AddRange(glb.ReadFloats(weight.GetInt32()));
                }

                int[] index = glb.ReadIndices(indices.GetInt32());
                for (int i = 0; i + 2 < index.Length; i += 3)
                {
                    corners.Add(first + index[i]);
                    corners.Add(first + index[i + 1]);
                    corners.Add(first + index[i + 2]);
                    triangleMaterials.Add(material.GetInt32());
                }
            }
        }

        if (triangleMaterials.Count == 0)
        {
            throw new InvalidDataException($"{path}: no skinned triangles");
        }

        return new BindMesh(materials, images, positions.ToArray(), normals.ToArray(), uvs.ToArray(), joints.ToArray(),
            weights.ToArray(), corners.ToArray(), triangleMaterials.ToArray(), skeleton);
    }

    /// <summary>
    /// The vertices of this mesh moved into <paramref name="other"/>'s bind pose: each one rides its bones from this
    /// rig's bind onto the bone of the same name in the other (UE2 "B L ForeArm" and the view models' "Bip01 L
    /// Forearm" are one bone). A bone the other rig lacks moves with its nearest ancestor that it has.
    /// </summary>
    public (Vector3[] Positions, Vector3[] Normals) PosedLike(BindMesh other)
    {
        var byName = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int j = 0; j < other.Skeleton.Length; j++)
        {
            byName.TryAdd(BoneKey(other.Skeleton[j].Name), j);
        }

        var moves = new Matrix4x4[Skeleton.Length];
        for (int j = 0; j < Skeleton.Length; j++)
        {
            moves[j] = Matrix4x4.Identity;
            for (int a = j; a >= 0; a = Skeleton[a].Parent)
            {
                if (byName.TryGetValue(BoneKey(Skeleton[a].Name), out int match))
                {
                    moves[j] = Skeleton[a].InverseBind * other.Skeleton[match].Bind;
                    break;
                }
            }
        }

        var positions = new Vector3[Positions.Length];
        var normals = new Vector3[Positions.Length];
        for (int v = 0; v < Positions.Length; v++)
        {
            Vector3 p = Vector3.Zero;
            Vector3 n = Vector3.Zero;
            float total = 0f;
            for (int k = 0; k < 4; k++)
            {
                float w = Weights[(v * 4) + k];
                int j = Joints[(v * 4) + k];
                if (w <= 0f || j < 0 || j >= moves.Length)
                {
                    continue;
                }

                p += w * Vector3.Transform(Positions[v], moves[j]);
                n += w * Vector3.TransformNormal(Normals[v], moves[j]);
                total += w;
            }

            positions[v] = total > 0f ? p / total : Positions[v];
            normals[v] = total > 0f && n != Vector3.Zero ? Vector3.Normalize(n) : Normals[v];
        }

        return (positions, normals);
    }

    /// <summary>A bone name without the rig's prefix ("B ", "Bip01 "), ignoring case.</summary>
    internal static string BoneKey(string name)
    {
        string key = name.Trim().ToLowerInvariant();
        foreach (string prefix in new[] { "bip01", "b" })
        {
            if (key == prefix)
            {
                return "";
            }

            if (key.StartsWith(prefix + " ", StringComparison.Ordinal))
            {
                return key[(prefix.Length + 1)..];
            }
        }

        return key;
    }

    private static Joint[] ReadSkeleton(GlbDocument glb, JsonElement nodes, JsonElement skin)
    {
        var jointNodes = new List<int>();
        foreach (JsonElement j in skin.GetProperty("joints").EnumerateArray())
        {
            jointNodes.Add(j.GetInt32());
        }

        var parentNode = new Dictionary<int, int>();
        for (int n = 0; n < nodes.GetArrayLength(); n++)
        {
            if (nodes[n].TryGetProperty("children", out JsonElement children))
            {
                foreach (JsonElement c in children.EnumerateArray())
                {
                    parentNode[c.GetInt32()] = n;
                }
            }
        }

        if (!skin.TryGetProperty("inverseBindMatrices", out JsonElement ibm))
        {
            throw new InvalidDataException("the skin has no inverse bind matrices");
        }

        float[] m = glb.ReadFloats(ibm.GetInt32());
        var joints = new Joint[jointNodes.Count];
        for (int j = 0; j < joints.Length; j++)
        {
            // glTF stores column-major matrices for column vectors; read in order they are the row-vector matrices
            // System.Numerics uses.
            int o = j * 16;
            var inverse = new Matrix4x4(m[o], m[o + 1], m[o + 2], m[o + 3], m[o + 4], m[o + 5], m[o + 6], m[o + 7],
                m[o + 8], m[o + 9], m[o + 10], m[o + 11], m[o + 12], m[o + 13], m[o + 14], m[o + 15]);
            Matrix4x4.Invert(inverse, out Matrix4x4 bind);
            int parent = -1;
            for (int n = jointNodes[j]; parentNode.TryGetValue(n, out int up); n = up)
            {
                if (jointNodes.IndexOf(up) is var p and >= 0)
                {
                    parent = p;
                    break;
                }
            }

            string name = nodes[jointNodes[j]].TryGetProperty("name", out JsonElement nm) ? nm.GetString() ?? "" : "";
            joints[j] = new Joint(name, parent, inverse, bind);
        }

        return joints;
    }

    private static (string[] Materials, string?[] Images) ReadMaterials(JsonElement root)
    {
        var images = new List<string?>();
        if (root.TryGetProperty("images", out JsonElement imgs))
        {
            foreach (JsonElement i in imgs.EnumerateArray())
            {
                images.Add(i.TryGetProperty("uri", out JsonElement u) ? u.GetString() : null);
            }
        }

        var textures = new List<int>();
        if (root.TryGetProperty("textures", out JsonElement texs))
        {
            foreach (JsonElement t in texs.EnumerateArray())
            {
                textures.Add(t.TryGetProperty("source", out JsonElement s) ? s.GetInt32() : -1);
            }
        }

        var names = new List<string>();
        var diffuse = new List<string?>();
        if (root.TryGetProperty("materials", out JsonElement mats))
        {
            foreach (JsonElement m in mats.EnumerateArray())
            {
                names.Add(m.TryGetProperty("name", out JsonElement n) ? n.GetString() ?? "" : "");
                string? image = null;
                if (m.TryGetProperty("pbrMetallicRoughness", out JsonElement pbr) && pbr.TryGetProperty("baseColorTexture", out JsonElement bct) &&
                    bct.GetProperty("index").GetInt32() is var ti && ti >= 0 && ti < textures.Count && textures[ti] is var ii && ii >= 0 && ii < images.Count)
                {
                    image = images[ii];
                }

                diffuse.Add(image);
            }
        }

        return (names.ToArray(), diffuse.ToArray());
    }
}
