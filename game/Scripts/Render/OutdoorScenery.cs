using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Godot;
using OpenScct.Core;

namespace OpenScctGame.Render;

/// <summary>Authored outdoor scenery from credited glTF assets. Simulation collision remains in the map bundle.</summary>
public static class OutdoorScenery
{
    public static void Build(RenderMap map)
    {
        string path = Path.Combine(map.Folder, "render", "scenery.json");
        if (!File.Exists(path)) return;
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var templates = new Dictionary<string, (Node3D Node, Aabb Bounds)>();
        var materials = new Dictionary<Material, ShaderMaterial>();
        var root = new Node3D { Name = "OutdoorScenery" };
        map.Root.AddChild(root);
        try
        {
            foreach (var item in doc.RootElement.GetProperty("instances").EnumerateArray())
            {
                string file = item.GetProperty("asset").GetString()!;
                if (Path.GetFileName(file) != file || Path.GetExtension(file) != ".glb") throw new InvalidDataException("Invalid scenery asset path");
                bool castle = file.StartsWith("castle-", StringComparison.Ordinal);
                string masonryTexture = item.GetProperty("at")[0].GetSingle() > 16000 ? "Hub_frozen_stone.png" : "Hub_ruin_stone.png";
                string templateKey = castle ? file + "/" + masonryTexture : file;
                if (!templates.TryGetValue(templateKey, out var template))
                {
                    var importer = new GltfDocument();
                    var state = new GltfState { CreateAnimations = false };
                    var error = importer.AppendFromFile(Path.Combine(map.Folder, "render", "scenery", file), state);
                    if (error != Error.Ok || importer.GenerateScene(state) is not Node3D node)
                        throw new InvalidDataException("Scenery import failed: " + file + " (" + error + ")");
                    Aabb? bounds = null;
                    Visit(node, Transform3D.Identity, (mesh, transform) =>
                    {
                        var b = transform * mesh.GetAabb();
                        bounds = bounds is { } old ? old.Merge(b) : b;
                        mesh.VisibilityRangeEnd = UeSpace.LengthToGodot(18000);
                        mesh.VisibilityRangeEndMargin = UeSpace.LengthToGodot(2000);
                        mesh.VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Self;
                        for (int surface = 0; surface < mesh.Mesh.GetSurfaceCount(); surface++)
                        {
                            if (mesh.GetActiveMaterial(surface) is not BaseMaterial3D source) continue;
                            if (!materials.TryGetValue(source, out var material))
                            {
                                material = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/hub_scenery.gdshader") };
                                if (source.AlbedoTexture is { } albedo) material.SetShaderParameter("diffuse", albedo);
                                material.SetShaderParameter("tint", source.AlbedoColor);
                                material.SetShaderParameter("alpha_cut", source.Transparency == BaseMaterial3D.TransparencyEnum.Disabled ? 0f : .25f);
                                if (castle)
                                {
                                    var stone = Image.LoadFromFile(Path.Combine(map.Folder, "render", "textures", masonryTexture));
                                    material.SetShaderParameter("masonry", true);
                                    material.SetShaderParameter("masonry_texture", ImageTexture.CreateFromImage(stone));
                                }
                                if (source.NormalTexture is { } normal)
                                {
                                    material.SetShaderParameter("normal_map", normal);
                                    material.SetShaderParameter("has_normal", true);
                                }
                                materials[source] = material;
                            }
                            mesh.SetSurfaceOverrideMaterial(surface, material);
                        }
                    });
                    template = (node, bounds ?? throw new InvalidDataException("Empty scenery model: " + file));
                    templates[templateKey] = template;
                }
                var at = item.GetProperty("at");
                var wrapper = new Node3D
                {
                    Name = Path.GetFileNameWithoutExtension(file),
                    Position = UeSpace.ToGodot(new FVector(at[0].GetSingle(), at[1].GetSingle(), at[2].GetSingle())),
                    RotationDegrees = new Vector3(0, item.GetProperty("yaw").GetSingle(), 0),
                };
                float height = UeSpace.LengthToGodot(item.GetProperty("height").GetSingle());
                var sourceSize = item.GetProperty("sourceSize");
                var sourcePivot = item.GetProperty("sourcePivot");
                var modelSize = new Vector3(sourceSize[0].GetSingle(), sourceSize[1].GetSingle(), sourceSize[2].GetSingle());
                wrapper.Scale = Vector3.One * (height / modelSize.Y);
                if (item.TryGetProperty("size", out var size))
                    wrapper.Scale = new Vector3(UeSpace.LengthToGodot(size[0].GetSingle()) / modelSize.X,
                        height / modelSize.Y, UeSpace.LengthToGodot(size[1].GetSingle()) / modelSize.Z);
                root.AddChild(wrapper);
                var instance = (Node3D)template.Node.Duplicate();
                instance.Position -= new Vector3(sourcePivot[0].GetSingle(), sourcePivot[1].GetSingle(), sourcePivot[2].GetSingle());
                wrapper.AddChild(instance);
                map.Stats.Instances++;
            }
        }
        finally { foreach (var template in templates.Values) template.Node.Free(); }
    }

    private static void Visit(Node3D node, Transform3D parent, Action<MeshInstance3D, Transform3D> visit)
    {
        var transform = parent * node.Transform;
        if (node is MeshInstance3D mesh) visit(mesh, transform);
        foreach (Node child in node.GetChildren()) if (child is Node3D spatial) Visit(spatial, transform, visit);
    }
}
