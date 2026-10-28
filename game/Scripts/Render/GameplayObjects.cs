using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Godot;
using OpenScct.Core;
using OpenScct.Provenance;
using OpenScctGame.Render.Data;
using OpenScctGame.Render.Geometry;
using OpenScctGame.Render.Gltf;
using OpenScctGame.Render.Materials;

namespace OpenScctGame.Render;

/// <summary>
/// Gameplay actors that draw their class's default static mesh (DrawType DT_StaticMesh with no StaticMesh of their
/// own in the map: first-aid cabinets, window panes, extinguishers, ammo stocks, camera globes, objective props).
/// world.glb carries only meshes an actor stores, so these come from the shared render pack's gameplay meshes, placed
/// from sim/gameplay.json (Location, Rotation, DrawScale x DrawScale3D, PrePivot) and actor-lit. Each is a node of
/// <see cref="RenderMap.Gameplay"/>, so views can hide, move and relight it. docs/RENDERING.md#gameplay-objects.
/// </summary>
[PortOf("eng:Render.GameplayObjects", Spec = "docs/RENDERING.md#gameplay-objects")]
public static class GameplayObjects
{
    private const int DT_StaticMesh = 8;

    /// <summary>
    /// Classes whose mesh depends on the game mode and gameplay state (Disk Hunt disks, the Story flag and drop zone,
    /// neutralization terminals): built hidden, for gameplay views to show.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "SDisk / SFlag / SFlagDropZone / SComputerObjectiveTrigger setup (spec/objectives)")]
    public static readonly HashSet<string> ModeDependent = new(StringComparer.OrdinalIgnoreCase)
    {
        "SDisk", "SFlag", "SFlagDropZone", "SComputerObjectiveTrigger",
    };

    /// <summary>Builds the class-default meshes of <paramref name="map"/>'s gameplay actors; returns how many were placed.</summary>
    public static int Build(RenderMap map, GameplayActors actors, RenderPack? pack)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(actors);
        if (pack?.GameplayGlb is not { } glbPath)
        {
            return 0;
        }

        string folder = Path.GetDirectoryName(glbPath)!;
        var materials = MaterialsDoc.Load(Path.Combine(folder, "materials.json"));
        var compiler = new MaterialCompiler(materials, new TextureCache(folder)) { Pack = pack, VisionLookup = VisionLookup.ModifierName };
        var parent = new Node3D { Name = "GameplayObjects" };
        map.Root.AddChild(parent);
        using GlbDocument glb = GlbDocument.Load(glbPath);
        var nodes = new Dictionary<string, int>(StringComparer.Ordinal);
        JsonElement gltfNodes = glb.Root.GetProperty("nodes");
        foreach (JsonElement n in gltfNodes.EnumerateArray())
        {
            if (n.TryGetProperty("name", out JsonElement name) && n.TryGetProperty("mesh", out JsonElement mesh))
            {
                nodes[name.GetString() ?? ""] = mesh.GetInt32();
            }
        }

        var materialNames = new List<string>();
        if (glb.Root.TryGetProperty("materials", out JsonElement mats))
        {
            foreach (JsonElement m in mats.EnumerateArray())
            {
                materialNames.Add(m.TryGetProperty("name", out JsonElement mn) ? mn.GetString() ?? "" : "");
            }
        }

        var meshes = new Dictionary<(string Node, bool Unlit), ArrayMesh?>();
        int placed = 0;
        foreach (GameplayActor actor in actors.Actors)
        {
            if (map.Gameplay.ContainsKey(actor.Id) || actor.Int("DrawType") != DT_StaticMesh
                || actor.Asset("StaticMesh") is not { } meshPath || pack.GameplayMeshNode(meshPath) is not { } nodeName
                || !nodes.TryGetValue(nodeName, out int meshIndex))
            {
                continue;
            }

            bool unlit = actor.Bool("bUnlit");
            if (!meshes.TryGetValue((nodeName, unlit), out ArrayMesh? arrayMesh))
            {
                arrayMesh = BuildMesh(glb, meshIndex, materialNames, compiler, unlit ? LightSource.None : LightSource.Actor);
                meshes[(nodeName, unlit)] = arrayMesh;
            }

            if (arrayMesh is null)
            {
                continue;
            }

            FVector location = actor.Vector("Location");
            FVector scale = actor.Vector("DrawScale3D", new FVector(1f, 1f, 1f)) * actor.Float("DrawScale", 1f);
            Transform3D transform = UeSpace.TransformToGodot(location, actor.Rotator("Rotation"), scale, actor.Vector("PrePivot"));
            bool hidden = actor.Bool("bHidden") || ModeDependent.Contains(actor.ShortClass);
            var node = new MeshInstance3D { Name = actor.Id, Mesh = arrayMesh, Transform = transform, Visible = !hidden };
            parent.AddChild(node);
            node.SetInstanceShaderParameter(VisionActorParameter, actor.Vision());
            Aabb box = transform * arrayMesh.GetAabb();
            float radius = UeSpace.UnitsPerMetre * box.Size.Length() * 0.5f;
            int glow = actor.Int("AmbientGlow");
            var entry = new GameplayMesh
            {
                ActorId = actor.Id,
                Class = actor.ShortClass,
                Node = node,
                Location = location,
                Light = unlit ? LightSource.None : LightSource.Actor,
                AmbientGlow = glow,
                Radius = radius,
            };
            map.Gameplay[actor.Id] = entry;
            if (!unlit)
            {
                ActorLighting.Apply(node, map.LightActor(UeSpace.ToUe(box.GetCenter()), radius, glow));
            }

            placed++;
        }

        map.Stats.GameplayObjects = placed;
        return placed;
    }

    /// <summary>
    /// Shows the objective props a game mode starts with (the gameplay views take over once the simulation runs):
    /// Story shows each SFlag as its disk (HardDisk_T) with the rack it spawns at home (Server), and each SFlagDropZone
    /// as its case with the lid it spawns at Location + (0, 0, 10) - 10 X (spec/objectives/disk-flag.md rules 1 and
    /// 4); DiskHunt shows the SDisk dog tags. Returns the nodes shown.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "SFlag.PostBeginPlay, SFlagDropZone.PostBeginPlay (spec/objectives/disk-flag.md)")]
    public static int ShowMode(RenderMap map, string mode)
    {
        ArgumentNullException.ThrowIfNull(map);
        // The Story disk and drop zones are not mode-gated in the original; only Disk Hunt and the deathmatches leave
        // them out here (the missions that use them are deactivated there).
        bool story = mode.ToUpperInvariant() is "STORY" or "EXTRACTION" or "SABOTAGE" or "NEUTRALIZATION";
        bool diskHunt = string.Equals(mode, "DiskHunt", StringComparison.OrdinalIgnoreCase);
        int shown = 0;
        var spawned = new List<(string Id, ArrayMesh Mesh, FVector Location, FRotator Rotation, GameplayMesh Owner)>();
        foreach (GameplayMesh g in map.Gameplay.Values)
        {
            GameplayActor? actor = map.Actors.Find(g.ActorId);
            if (actor is null)
            {
                continue;
            }

            switch (g.Class)
            {
                case "SDisk":
                    g.Node.Visible = diskHunt;
                    shown += diskHunt ? 1 : 0;
                    break;
                case "SFlag" when story:
                    if (g.Node.Mesh is ArrayMesh rack && MeshFor(map, "SGameplayObjects.Extraction_SM.HardDisk_T", unlit: false) is { } disk)
                    {
                        spawned.Add((g.ActorId + "_Server", rack, actor.Vector("Location"), actor.Rotator("Rotation"), g));
                        g.Node.Mesh = disk;
                    }

                    g.Node.Visible = true;
                    shown++;
                    break;
                case "SFlagDropZone" when story:
                    if (MeshFor(map, "SGameplayObjects.Extraction_SM.Malette_haut", unlit: false) is { } lid)
                    {
                        FRotator r = actor.Rotator("Rotation");
                        FVector at = actor.Vector("Location") + new FVector(0f, 0f, 10f) - (r.Vector() * 10f);
                        spawned.Add((g.ActorId + "_Cover", lid, at, r, g));
                    }

                    g.Node.Visible = true;
                    shown++;
                    break;
            }
        }

        foreach ((string id, ArrayMesh mesh, FVector location, FRotator rotation, GameplayMesh owner) in spawned)
        {
            var node = new MeshInstance3D { Name = id, Mesh = mesh, Transform = UeSpace.TransformToGodot(location, rotation) };
            owner.Node.GetParent().AddChild(node);
            if (map.Actors.Find(owner.ActorId) is { } a)
            {
                node.SetInstanceShaderParameter(VisionActorParameter, a.Vision());
            }

            ActorLighting.Apply(node, map.LightActor(location, owner.Radius, owner.AmbientGlow));
            map.Gameplay[id] = new GameplayMesh
            {
                ActorId = id,
                Class = owner.Class,
                Node = node,
                Location = location,
                Light = LightSource.Actor,
                AmbientGlow = owner.AmbientGlow,
                Radius = owner.Radius,
            };
            shown++;
        }

        return shown;
    }

    /// <summary>One class-default mesh of the render pack (Package.Group.Name), actor-lit or unlit, or null when the pack lacks it.</summary>
    public static ArrayMesh? MeshFor(RenderMap map, string meshPath, bool unlit)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(meshPath);
        if (map.Pack is not { } pack || pack.GameplayGlb is not { } glbPath || pack.GameplayMeshNode(meshPath) is not { } nodeName)
        {
            return null;
        }

        string folder = Path.GetDirectoryName(glbPath)!;
        var compiler = new MaterialCompiler(MaterialsDoc.Load(Path.Combine(folder, "materials.json")), new TextureCache(folder))
        {
            Pack = pack,
            VisionLookup = VisionLookup.ModifierName,
        };
        using GlbDocument glb = GlbDocument.Load(glbPath);
        var materialNames = new List<string>();
        if (glb.Root.TryGetProperty("materials", out JsonElement mats))
        {
            foreach (JsonElement m in mats.EnumerateArray())
            {
                materialNames.Add(m.TryGetProperty("name", out JsonElement mn) ? mn.GetString() ?? "" : "");
            }
        }

        foreach (JsonElement n in glb.Root.GetProperty("nodes").EnumerateArray())
        {
            if (n.TryGetProperty("name", out JsonElement name) && name.GetString() == nodeName && n.TryGetProperty("mesh", out JsonElement mesh))
            {
                return BuildMesh(glb, mesh.GetInt32(), materialNames, compiler, unlit ? LightSource.None : LightSource.Actor);
            }
        }

        return null;
    }

    /// <summary>The template's per-actor vision values (HeatIntensity, HeatOpacity, EMF glow, EMFOpacity).</summary>
    public static readonly StringName VisionActorParameter = "svm_vision_actor";

    /// <summary>
    /// A live actor's vision values in the <see cref="VisionActorParameter"/> layout: HeatIntensity, HeatOpacity, EMF
    /// glow (EMFIntensity while bEMFIsOn) and EMFOpacity. HeatIntensity, the EMF values and bEMFIsOn are replicated.
    /// </summary>
    public static Vector4 VisionOf(OpenScct.Core.Engine.Actor actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        return new Vector4(actor.HeatIntensity, actor.HeatOpacity, OpenScct.Game.Equipment.EmfVision.Glow(actor), actor.EMFOpacity);
    }

    /// <summary>Writes every gameplay mesh's vision values from its actor (sim/gameplay.json over the class defaults).</summary>
    public static void ApplyVision(RenderMap map, GameplayActors actors)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(actors);
        foreach (GameplayMesh g in map.Gameplay.Values)
        {
            if (actors.Find(g.ActorId) is { } actor)
            {
                g.Node.SetInstanceShaderParameter(VisionActorParameter, actor.Vision());
                if (g.Light == LightSource.Actor)
                {
                    ActorLighting.SetEchelonIgnore(g.Node, ActorLighting.EchelonCode(map, actor));
                }
            }
        }
    }

    private static ArrayMesh? BuildMesh(GlbDocument glb, int mesh, List<string> materialNames, MaterialCompiler compiler, LightSource light)
    {
        var arrayMesh = new ArrayMesh();
        JsonElement prims = glb.Root.GetProperty("meshes")[mesh].GetProperty("primitives");
        int section = -1;
        foreach (JsonElement p in prims.EnumerateArray())
        {
            section++;
            JsonElement attrs = p.GetProperty("attributes");
            int materialIndex = p.TryGetProperty("material", out JsonElement mi) ? mi.GetInt32() : -1;
            string materialName = materialIndex >= 0 && materialIndex < materialNames.Count ? materialNames[materialIndex] : "";
            Material? material = compiler.Get(materialName, light);
            if (material is null)
            {
                continue;
            }

            float[] positions = glb.ReadFloats(attrs.GetProperty("POSITION").GetInt32());
            int[] indices = p.TryGetProperty("indices", out JsonElement ii) ? glb.ReadIndices(ii.GetInt32()) : Sequence(positions.Length / 3);
            var prim = new MeshPrimitive
            {
                Material = materialName,
                Positions = positions,
                Normals = attrs.TryGetProperty("NORMAL", out JsonElement nn) ? glb.ReadFloats(nn.GetInt32()) : null,
                Uvs = attrs.TryGetProperty("TEXCOORD_0", out JsonElement t0) ? glb.ReadFloats(t0.GetInt32()) : null,
                Indices = indices,
            };
            var surface = new SurfaceBuilder(false, false);
            surface.Add(Transform3D.Identity, prim, 0);
            surface.AddTo(arrayMesh, material);
            arrayMesh.SurfaceSetName(arrayMesh.GetSurfaceCount() - 1, SectionSurfacePrefix + section);
        }

        return arrayMesh.GetSurfaceCount() > 0 ? arrayMesh : null;
    }

    private const string SectionSurfacePrefix = "section";

    /// <summary>
    /// The mesh section (glTF primitive, one per drawn StaticMesh section) surface <paramref name="surface"/> of a
    /// gameplay mesh draws, which is the Skins slot that replaces its material; -1 for a mesh built elsewhere.
    /// </summary>
    [Provenance(ProvenanceKind.Estimated, "Primitive order is section order; a section the exporter skips (no faces, a skipped material) shifts the later slots")]
    public static int SectionOf(Mesh mesh, int surface)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        string name = mesh is ArrayMesh a ? a.SurfaceGetName(surface) : string.Empty;
        return name.StartsWith(SectionSurfacePrefix, StringComparison.Ordinal)
            && int.TryParse(name.AsSpan(SectionSurfacePrefix.Length), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int s)
            ? s
            : -1;
    }

    private static int[] Sequence(int n)
    {
        var s = new int[n];
        for (int i = 0; i < n; i++)
        {
            s[i] = i;
        }

        return s;
    }
}
