using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Godot;
using OpenScct.Core;

namespace OpenScctGame.Render;

/// <summary>Soft grass and fern fronds. Spatial MultiMeshes avoid a scene node per plant.</summary>
public static class OutdoorGroundcover
{
    public static void Build(RenderMap map)
    {
        string path = Path.Combine(map.Folder, "render", "foliage.json");
        if (!File.Exists(path)) return; // legacy bundles
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var data = doc.RootElement;
        if (data.GetProperty("format").GetInt32() != 1) throw new InvalidDataException("Unknown groundcover format");
        if (data.GetProperty("cellSize").GetInt32() != 2048) throw new InvalidDataException("Unexpected groundcover cell size");
        string Asset(string key)
        {
            string file = data.GetProperty(key).GetString()!;
            if (Path.GetFileName(file) != file) throw new InvalidDataException("Invalid groundcover asset path");
            return Path.Combine(map.Folder, "render", "scenery", file);
        }
        using var grassImage = Image.LoadFromFile(Asset("grassTexture"));
        grassImage.GenerateMipmaps();
        var grassMaterial = Material(ImageTexture.CreateFromImage(grassImage), new Color(1.06f, 1.15f, .92f), 88f, .09f);
        var grass = GrassMesh(grassMaterial);
        var ferns = FernMeshes(Asset("fernAsset"));
        var root = new Node3D { Name = "OutdoorGroundcover" };
        map.Root.AddChild(root);
        int index = 0;
        foreach (var chunk in data.GetProperty("chunks").EnumerateArray())
        {
            var at = chunk.GetProperty("origin");
            var origin = UeSpace.ToGodot(new FVector(at[0].GetSingle(), at[1].GetSingle(), at[2].GetSingle()));
            Batch(root, grass, chunk.GetProperty("grass"), origin, "Grass" + index, 105f);
            Batch(root, ferns[index % ferns.Count], chunk.GetProperty("fern"), origin, "Ferns" + index, 75f);
            index++;
        }
    }

    private static ShaderMaterial Material(Texture2D texture, Color tint, float distance, float wind)
    {
        var material = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/hub_groundcover.gdshader") };
        material.SetShaderParameter("diffuse", texture);
        material.SetShaderParameter("tint", tint);
        material.SetShaderParameter("fade_end", distance);
        material.SetShaderParameter("wind_strength", wind);
        return material;
    }

    private static ArrayMesh GrassMesh(Material material)
    {
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var uv = new List<Vector2>();
        var indices = new List<int>();
        for (int plane = 0; plane < 3; plane++)
        {
            float angle = plane * MathF.PI / 3;
            var edge = new Vector3(MathF.Cos(angle), 0, MathF.Sin(angle)) * .5f;
            int start = vertices.Count;
            vertices.AddRange([-edge, edge, edge + Vector3.Up, -edge + Vector3.Up]);
            normals.AddRange([Vector3.Up, Vector3.Up, Vector3.Up, Vector3.Up]);
            uv.AddRange([new(0, 1), new(1, 1), new(1, 0), new(0, 0)]);
            indices.AddRange([start, start + 1, start + 2, start, start + 2, start + 3]);
        }
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices.ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = normals.ToArray();
        arrays[(int)Mesh.ArrayType.TexUV] = uv.ToArray();
        arrays[(int)Mesh.ArrayType.Index] = indices.ToArray();
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        mesh.SurfaceSetMaterial(0, material);
        return mesh;
    }

    private static List<ArrayMesh> FernMeshes(string path)
    {
        var importer = new GltfDocument();
        var state = new GltfState { CreateAnimations = false };
        var error = importer.AppendFromFile(path, state);
        if (error != Error.Ok || importer.GenerateScene(state) is not Node3D source)
            throw new InvalidDataException("Fern import failed: " + error);
        var variants = new List<ArrayMesh>();
        var materials = new Dictionary<BaseMaterial3D, ShaderMaterial>();
        try
        {
            // The source contains four separate plants. Normalize each independently,
            // rather than stamping the source's four-plant display grid into the world.
            void Visit(Node node)
            {
                if (node is MeshInstance3D instance)
                {
                    var sourceMesh = instance.Mesh;
                    var bounds = sourceMesh.GetAabb();
                    var pivot = new Vector3(bounds.GetCenter().X, bounds.Position.Y, bounds.GetCenter().Z);
                    var scale = new Vector3(1 / bounds.Size.X, 1 / bounds.Size.Y, 1 / bounds.Size.Z);
                    var mesh = new ArrayMesh();
                    for (int surface = 0; surface < sourceMesh.GetSurfaceCount(); surface++)
                    {
                        var arrays = sourceMesh.SurfaceGetArrays(surface);
                        var positions = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                        for (int i = 0; i < positions.Length; i++) positions[i] = (positions[i] - pivot) * scale;
                        arrays[(int)Mesh.ArrayType.Vertex] = positions;
                        // The soft-foliage shader uses ambient light, without tangent-space shading.
                        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
                        if (instance.GetActiveMaterial(surface) is not BaseMaterial3D pbr || pbr.AlbedoTexture is null)
                            throw new InvalidDataException("Fern is missing its base-colour texture");
                        if (!materials.TryGetValue(pbr, out var material))
                        {
                            material = Material(pbr.AlbedoTexture, pbr.AlbedoColor, 58f, .025f);
                            materials.Add(pbr, material);
                        }
                        mesh.SurfaceSetMaterial(surface, material);
                    }
                    variants.Add(mesh);
                }
                foreach (Node child in node.GetChildren()) Visit(child);
            }
            Visit(source);
        }
        finally { source.Free(); }
        if (variants.Count == 0) throw new InvalidDataException("Fern asset contains no plants");
        return variants;
    }

    private static void Batch(Node3D parent, Mesh mesh, JsonElement instances, Vector3 origin, string name, float range)
    {
        int count = instances.GetArrayLength();
        if (count == 0) return;
        var multi = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            Mesh = mesh,
            InstanceCount = count,
        };
        // Upload one packed buffer per cell instead of crossing the managed/native
        // boundary twice for every blade clump. Godot uses 3 rows of transform + RGBA.
        var buffer = new float[count * 16];
        int i = 0;
        foreach (var plant in instances.EnumerateArray())
        {
            float yaw = Mathf.DegToRad(plant[2].GetSingle());
            float width = UeSpace.LengthToGodot(plant[3].GetSingle());
            float height = UeSpace.LengthToGodot(plant[4].GetSingle());
            float c = MathF.Cos(yaw) * width, s = MathF.Sin(yaw) * width;
            float tone = plant[5].GetSingle() / 100f;
            buffer[i] = c; buffer[i + 2] = s; buffer[i + 3] = UeSpace.LengthToGodot(plant[0].GetSingle());
            buffer[i + 5] = height;
            buffer[i + 8] = -s; buffer[i + 10] = c; buffer[i + 11] = UeSpace.LengthToGodot(plant[1].GetSingle());
            buffer[i + 12] = .82f + tone * .23f;
            buffer[i + 13] = .89f + tone * .12f;
            buffer[i + 14] = .76f + tone * .20f;
            buffer[i + 15] = 1;
            i += 16;
        }
        multi.Buffer = buffer;
        // Cell-local bounds include the widest fronds and wind at the edges.
        multi.CustomAabb = new Aabb(new Vector3(-1.3f, -.05f, -1.3f), new Vector3(23.1f, 1.2f, 23.1f));
        parent.AddChild(new MultiMeshInstance3D
        {
            Name = name, Position = origin, Multimesh = multi,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            VisibilityRangeEnd = range,
        });
    }
}
