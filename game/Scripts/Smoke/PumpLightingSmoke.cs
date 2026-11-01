using System;
using System.IO;
using System.Linq;
using Godot;
using OpenScctGame.Render;
using OpenScctGame.Render.Gltf;
using OpenScctGame.Render.Materials;

namespace OpenScctGame.Smoke;

/// <summary>Real map bake regression: preserve pump intensity without removing opaque door ambient.</summary>
public sealed class PumpLightingSmoke : SmokeTest
{
    public const string TestName = "pump-lighting";
    public override string Name => TestName;
    public override SmokeResult Step(SmokeContext context)
    {
        string? folder = MapBuilder.FindMapFolder("AquaE", context.App.Options.MapRoots);
        if (folder is null) return SmokeResult.Fail("AquaE content missing");
        using var glb = GlbDocument.Load(Path.Combine(folder, "render", "world.glb"));
        var map = MapBuilder.Build(folder, new RenderOptions());
        int compared = 0;
        try
        {
            foreach ((string actor, int floor) in new[] { ("Mover16072", 0), ("Mover362", 20) })
            {
                var instance = glb.Root.GetProperty("extras").GetProperty("svmap").GetProperty("instances")
                    .EnumerateArray().Single(x => x.GetProperty("actor").GetString() == actor);
                string meshName = instance.GetProperty("mesh").GetString()!;
                var sourceNode = glb.Root.GetProperty("nodes").EnumerateArray()
                    .Single(x => x.TryGetProperty("name", out var name) && name.GetString() == meshName);
                var primitives = glb.Root.GetProperty("meshes")[sourceNode.GetProperty("mesh").GetInt32()].GetProperty("primitives");
                if (!map.Gameplay.TryGetValue(actor, out var built) || built.Light != LightSource.Vertex)
                    return SmokeResult.Fail(actor + " missing baked gameplay mesh");
                var mesh = (ArrayMesh)built.Node.Mesh;
                if (mesh.GetSurfaceCount() != primitives.GetArrayLength())
                    return SmokeResult.Fail(actor + " lost surfaces");
                for (int s = 0; s < primitives.GetArrayLength(); s++)
                {
                    var primitive = primitives[s];
                    byte[] source = glb.ReadBytes(primitive.GetProperty("attributes").GetProperty("COLOR_0").GetInt32());
                    int[] triangles = glb.ReadIndices(primitive.GetProperty("indices").GetInt32());
                    var arrays = mesh.SurfaceGetArrays(s);
                    Color[] colors = arrays[(int)Mesh.ArrayType.Color].AsColorArray();
                    int[] indices = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
                    if (indices.Length != triangles.Length) return SmokeResult.Fail(actor + " triangle count changed");
                    for (int t = 0; t < triangles.Length; t += 3)
                    for (int corner = 0; corner < 3; corner++)
                    {
                        // MapBuilder changes glTF CCW to Godot CW; compare triangle corners,
                        // independent of vertex deduplication and per-zone duplication.
                        int original = triangles[t + corner] * 4;
                        Color actual = colors[indices[t + (corner == 0 ? 0 : 3 - corner)]];
                        for (int channel = 0; channel < 3; channel++)
                        {
                            float expected = (source[original + channel] + floor) / 255f;
                            if (MathF.Abs(actual[channel] - expected) > 1.1f / 255f)
                                return SmokeResult.Fail($"{actor} surface {s} triangle {t / 3}: channel {channel} {actual[channel]:F4}, expected {expected:F4}");
                        }
                        compared++;
                    }
                }
            }
            return SmokeResult.Pass($"{compared} triangle corners: pump preserves saved RGB; opaque door retains ambient floor");
        }
        finally { map.Root.Free(); }
    }
}
