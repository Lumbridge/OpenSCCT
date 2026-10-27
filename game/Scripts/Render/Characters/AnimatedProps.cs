using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Godot;
using OpenScct.Core;

namespace OpenScctGame.Render.Characters;

/// <summary>A placed skeletal mesh actor of the map (SAnimatedMesh: SteeE's crane and blast wall).</summary>
public sealed record AnimatedProp(string ActorId, SkeletalModel Model, FVector Location, float DrawScale);

/// <summary>
/// Places the map's skeletal mesh actors (DrawType DT_Mesh with a Mesh from the shared packs), posed at the first
/// frame of their first sequence until gameplay plays them. Reads sim/gameplay.json for the actors.
/// </summary>
public static class AnimatedProps
{
    private const int DT_Mesh = 2;

    public static List<AnimatedProp> Build(RenderMap map, Node3D parent, string? sharedRoot = null)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(parent);
        var props = new List<AnimatedProp>();
        sharedRoot ??= map.SharedRoot;
        string path = Path.Combine(map.Folder, "sim", "gameplay.json");
        if (!File.Exists(path))
        {
            return props;
        }

        using JsonDocument doc = JsonDocument.Parse(File.ReadAllBytes(path));
        if (!doc.RootElement.TryGetProperty("actors", out JsonElement actors))
        {
            return props;
        }

        foreach (JsonElement a in actors.EnumerateArray())
        {
            JsonElement p = a.GetProperty("props");
            if (!p.TryGetProperty("Mesh", out JsonElement mesh) || !mesh.TryGetProperty("$asset", out JsonElement assetEl)
                || !p.TryGetProperty("DrawType", out JsonElement dt) || dt.GetInt32() != DT_Mesh
                || (p.TryGetProperty("bHidden", out JsonElement hidden) && hidden.ValueKind == JsonValueKind.True))
            {
                continue;
            }

            string asset = assetEl.GetString() ?? "";
            string? glb = SkeletalModel.FindGlb(asset, sharedRoot);
            if (glb is null)
            {
                map.Warnings.Add("skeletal mesh not in the shared packs: " + asset);
                continue;
            }

            SkeletalModel model = SkeletalModel.Load(glb);
            string id = a.GetProperty("id").GetString() ?? asset;
            model.Root.Name = id;
            FVector location = Vec(p, "Location");
            FRotator rotation = p.TryGetProperty("Rotation", out JsonElement r)
                ? new FRotator(Int(r, "Pitch"), Int(r, "Yaw"), Int(r, "Roll"))
                : default;
            FVector prePivot = Vec(p, "PrePivot");
            float drawScale = p.TryGetProperty("DrawScale", out JsonElement ds) ? ds.GetSingle() : 1f;
            model.Place(location, rotation, prePivot, drawScale);
            if (p.TryGetProperty("Animations", out JsonElement anims) && anims.ValueKind == JsonValueKind.Array && anims.GetArrayLength() > 0)
            {
                model.Sampler.Play(anims[0].GetString() ?? "", 0f, false);
            }

            model.Pose();
            parent.AddChild(model.Root);
            int glow = p.TryGetProperty("AmbientGlow", out JsonElement g) ? g.GetInt32() : 0;
            // Lights are ranked against the actor's bounding sphere (ActorLighting.Score): the mesh's own bounds, which
            // for SteeE's crane (DrawScale 10) reach far from its location.
            (FVector center, float radius) = Bounds(model);
            model.Light(map.LightActor(center, radius, glow));
            props.Add(new AnimatedProp(id, model, location, drawScale));
        }

        return props;
    }

    /// <summary>The bounding sphere (UE centre and radius) of a placed model's meshes in their rest pose.</summary>
    public static (FVector Center, float Radius) Bounds(SkeletalModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        Aabb box = default;
        bool any = false;
        Transform3D toWorld = model.Root.Transform * model.Pivot.Transform;
        foreach (MeshInstance3D mesh in model.Meshes)
        {
            if (mesh.Mesh is null)
            {
                continue;
            }

            Aabb b = toWorld * mesh.Transform * mesh.Mesh.GetAabb();
            box = any ? box.Merge(b) : b;
            any = true;
        }

        return any ? (UeSpace.ToUe(box.GetCenter()), UeSpace.UnitsPerMetre * box.Size.Length() * 0.5f) : (UeSpace.ToUe(model.Root.Position), 100f);
    }

    private static int Int(JsonElement e, string name) => e.TryGetProperty(name, out JsonElement v) ? v.GetInt32() : 0;

    private static FVector Vec(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out JsonElement v) || v.ValueKind != JsonValueKind.Object)
        {
            return default;
        }

        float F(string k) => v.TryGetProperty(k, out JsonElement x) ? x.GetSingle() : 0f;
        return new FVector(F("X"), F("Y"), F("Z"));
    }
}
