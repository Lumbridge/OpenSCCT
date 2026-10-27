using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Godot;
using OpenScct.Content;
using OpenScct.Content.Collision;
using OpenScct.Content.Loading;
using OpenScct.Core;
using OpenScct.Core.Engine;
using PlayerStart = OpenScct.Core.Engine.PlayerStart;

namespace OpenScctGame.Maps;

/// <summary>Where the drawn world geometry came from.</summary>
public enum MapRenderSource
{
    /// <summary><c>render/world.glb</c>, the map's render files.</summary>
    RenderGlb,

    /// <summary>The collision triangles, for maps without render files.</summary>
    CollisionTriangles,
}

/// <summary>The Godot nodes built for one map.</summary>
public sealed class MapView
{
    internal MapView(Node3D root, MapRenderSource source, MeshInstance3D? world, IReadOnlyDictionary<string, Mesh> library,
        IReadOnlyList<MeshInstance3D> instances, IReadOnlyList<Marker3D> playerStarts, IReadOnlyList<string> warnings)
    {
        Root = root;
        Source = source;
        World = world;
        Library = library;
        Instances = instances;
        PlayerStarts = playerStarts;
        Warnings = warnings;
    }

    /// <summary>The map's root node; add it to the scene.</summary>
    public Node3D Root { get; }

    public MapRenderSource Source { get; }

    /// <summary>The static world geometry.</summary>
    public MeshInstance3D? World { get; }

    /// <summary>Library meshes by asset path (<c>TestBox.Meshes.SlidingDoor</c>), matched ignoring ASCII case.</summary>
    public IReadOnlyDictionary<string, Mesh> Library { get; }

    /// <summary>Static mesh instances of actors, named by actor id.</summary>
    public IReadOnlyList<MeshInstance3D> Instances { get; }

    /// <summary>One marker per PlayerStart, named by actor id.</summary>
    public IReadOnlyList<Marker3D> PlayerStarts { get; }

    /// <summary>What could not be built (missing meshes, unknown assets).</summary>
    public IReadOnlyList<string> Warnings { get; }
}

/// <summary>
/// Builds the Godot nodes of an SVMap: the world from <c>render/world.glb</c> (or from the collision triangles
/// when a map has no render files), static mesh instances for actors that name a library mesh, and markers for
/// the player starts. Everything is placed through <see cref="UeSpace"/>.
/// </summary>
public static class MapBuilder
{
    public const string RenderFile = "render/world.glb";
    public const string WorldNodeName = "World";
    public const string LibraryNodeName = "Library";

    private static readonly Color SpyColour = new(0.25f, 0.85f, 0.35f);
    private static readonly Color MercColour = new(0.9f, 0.3f, 0.25f);

    public static MapView Build(SvMap map, MapLoadResult loaded)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(loaded);
        var root = new Node3D { Name = "Map" };
        var warnings = new List<string>();
        var library = new Dictionary<string, Mesh>(AsciiCaseComparer.Instance);
        MeshInstance3D? world = null;
        MapRenderSource source = MapRenderSource.RenderGlb;

        string glb = Path.Combine(map.Root, RenderFile);
        Node3D? render = File.Exists(glb) ? LoadRenderWorld(glb, library, warnings, out world) : null;
        if (render is not null)
        {
            root.AddChild(render);
        }
        else
        {
            source = MapRenderSource.CollisionTriangles;
            world = BuildCollisionWorld(map.Collision);
            root.AddChild(world);
        }

        var instances = new List<MeshInstance3D>();
        var instanceRoot = new Node3D { Name = "Actors" };
        root.AddChild(instanceRoot);
        PlaceStaticMeshes(instanceRoot, map, loaded, library, instances, warnings);

        var starts = new List<Marker3D>();
        var startRoot = new Node3D { Name = "PlayerStarts" };
        root.AddChild(startRoot);
        PlacePlayerStarts(startRoot, loaded, starts);

        return new MapView(root, source, world, library, instances, starts, warnings);
    }

    /// <summary>
    /// Loads the glTF world. The spec gives it two root nodes, <c>World</c> and <c>Library</c>; library meshes are
    /// kept by their glTF names (asset paths) and the <c>Library</c> node is removed, since it is never drawn.
    /// </summary>
    private static Node3D? LoadRenderWorld(string path, Dictionary<string, Mesh> library, List<string> warnings,
        out MeshInstance3D? world)
    {
        world = null;
        var document = new GltfDocument();
        var state = new GltfState();
        Error error = document.AppendFromFile(path, state);
        if (error != Error.Ok)
        {
            warnings.Add($"{RenderFile}: glTF load failed ({error})");
            return null;
        }

        if (document.GenerateScene(state) is not Node3D scene)
        {
            warnings.Add($"{RenderFile}: glTF has no 3D scene");
            return null;
        }

        Godot.Collections.Array<GltfNode> nodes = state.GetNodes();
        int libraryIndex = -1;
        for (int i = 0; i < nodes.Count; i++)
        {
            GltfNode node = nodes[i];
            if (node.Parent >= 0)
            {
                continue;
            }

            if (node.OriginalName == WorldNodeName)
            {
                world = SceneNode(scene, state, node) as MeshInstance3D;
            }
            else if (node.OriginalName == LibraryNodeName)
            {
                libraryIndex = i;
            }
        }

        if (world is null)
        {
            warnings.Add($"{RenderFile}: no '{WorldNodeName}' mesh node");
        }

        if (libraryIndex >= 0)
        {
            foreach (int child in nodes[libraryIndex].Children)
            {
                if (SceneNode(scene, state, nodes[child]) is MeshInstance3D { Mesh: { } mesh })
                {
                    library[nodes[child].OriginalName] = mesh;
                }
                else
                {
                    warnings.Add($"{RenderFile}: library node '{nodes[child].OriginalName}' has no mesh");
                }
            }

            if (SceneNode(scene, state, nodes[libraryIndex]) is { } libraryNode)
            {
                libraryNode.GetParent()?.RemoveChild(libraryNode);
                libraryNode.Free();
            }
        }

        scene.Name = "Render";
        return scene;
    }

    /// <summary>
    /// The generated node of a glTF node, found by its scene path. GltfState.GetSceneNode is not used: after
    /// GenerateScene it can still point at the importer node that a mesh instance replaced.
    /// </summary>
    private static Node? SceneNode(Node3D scene, GltfState state, GltfNode node)
    {
        NodePath path = node.GetSceneNodePath(state, false);
        return path.IsEmpty ? null : scene.GetNodeOrNull(path);
    }

    /// <summary>Instances library meshes for actors whose StaticMesh is in the map's own render library.</summary>
    private static void PlaceStaticMeshes(Node3D parent, SvMap map, MapLoadResult loaded, Dictionary<string, Mesh> library,
        List<MeshInstance3D> instances, List<string> warnings)
    {
        var missing = new SortedDictionary<string, int>(StringComparer.Ordinal);
        IReadOnlyList<ActorRecord> records = map.Gameplay.AllActors;
        for (int i = 1; i < records.Count; i++)
        {
            ActorRecord record = records[i];
            AssetRef? asset = record.Props.GetAsset("StaticMesh");
            if (asset is null || record.Props.GetBool("bHidden"))
            {
                continue;
            }

            if (!asset.IsInMap(map.Id) || !library.TryGetValue(asset.Path, out Mesh? mesh))
            {
                missing[asset.Path] = missing.GetValueOrDefault(asset.Path) + 1;
                continue;
            }

            Actor? actor = loaded.ActorOf(record);
            FVector location = actor?.Location ?? record.Location;
            FRotator rotation = actor?.Rotation ?? record.Rotation;
            float drawScale = record.Props.GetFloat("DrawScale", 1f);
            FVector scale = record.Props.GetVector("DrawScale3D", new FVector(1f, 1f, 1f)) * drawScale;
            FVector prePivot = record.Props.GetVector("PrePivot");
            var instance = new MeshInstance3D
            {
                Name = record.Id,
                Mesh = mesh,
                Transform = UeSpace.TransformToGodot(location, rotation, scale, prePivot),
            };
            parent.AddChild(instance);
            instances.Add(instance);
        }

        foreach ((string path, int count) in missing)
        {
            warnings.Add(string.Create(CultureInfo.InvariantCulture, $"static mesh {path} not in the render library ({count} actors)"));
        }
    }

    private static void PlacePlayerStarts(Node3D parent, MapLoadResult loaded, List<Marker3D> starts)
    {
        foreach (Actor? actor in loaded.Actors)
        {
            if (actor is not PlayerStart start)
            {
                continue;
            }

            string id = loaded.RecordOf(start)?.Id ?? "PlayerStart" + start.ActorId.ToString(CultureInfo.InvariantCulture);
            var marker = new Marker3D
            {
                Name = id,
                Transform = UeSpace.TransformToGodot(start.Location, start.Rotation),
            };
            marker.SetMeta("team", start.TeamNumber);
            marker.AddChild(StartGizmo(start.TeamNumber == 1 ? MercColour : SpyColour));
            parent.AddChild(marker);
            starts.Add(marker);
        }
    }

    /// <summary>A flat disc with a nose pointing along the start's facing (local +X, UE forward).</summary>
    private static Node3D StartGizmo(Color colour)
    {
        var material = new StandardMaterial3D
        {
            AlbedoColor = colour,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        };
        var gizmo = new Node3D { Name = "Gizmo" };
        gizmo.AddChild(new MeshInstance3D
        {
            Name = "Disc",
            Mesh = new CylinderMesh { TopRadius = 0.32f, BottomRadius = 0.32f, Height = 0.04f, Material = material },
        });
        gizmo.AddChild(new MeshInstance3D
        {
            Name = "Nose",
            Mesh = new BoxMesh { Size = new Vector3(0.24f, 0.04f, 0.08f), Material = material },
            Position = new Vector3(0.4f, 0f, 0f),
        });
        return gizmo;
    }

    /// <summary>
    /// The collision triangles as a flat-shaded mesh: floors light, walls mid, ceilings dark. Colliders are placed
    /// with their initial transforms.
    /// </summary>
    public static MeshInstance3D BuildCollisionWorld(CollisionData collision)
    {
        ArgumentNullException.ThrowIfNull(collision);
        var positions = new List<Vector3>();
        var normals = new List<Vector3>();
        var colours = new List<Color>();
        ReadOnlySpan<FVector> vertices = collision.Vertices.AsSpan();
        foreach (Collider collider in collision.Colliders)
        {
            if (collider.TriCount == 0)
            {
                continue;
            }

            Transform3D transform = UeSpace.TransformToGodot(collider.Location, collider.Rotation, collider.Scale, collider.PrePivot);
            foreach (CollisionTriangle tri in collision.TrianglesOf(collider))
            {
                Vector3 a = transform * UeSpace.ToGodot(vertices[(int)tri.I0]);
                Vector3 b = transform * UeSpace.ToGodot(vertices[(int)tri.I1]);
                Vector3 c = transform * UeSpace.ToGodot(vertices[(int)tri.I2]);

                // Godot front faces are clockwise: keep the UE order; the front normal is (c - a) x (b - a).
                Vector3 normal = (c - a).Cross(b - a).Normalized();
                Color colour = normal.Y > 0.7f ? new Color(0.75f, 0.75f, 0.72f)
                    : normal.Y < -0.7f ? new Color(0.35f, 0.35f, 0.38f)
                    : new Color(0.55f, 0.56f, 0.6f);
                positions.Add(a);
                positions.Add(b);
                positions.Add(c);
                for (int k = 0; k < 3; k++)
                {
                    normals.Add(normal);
                    colours.Add(colour);
                }
            }
        }

        var mesh = new ArrayMesh();
        if (positions.Count > 0)
        {
            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = positions.ToArray();
            arrays[(int)Mesh.ArrayType.Normal] = normals.ToArray();
            arrays[(int)Mesh.ArrayType.Color] = colours.ToArray();
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
            mesh.SurfaceSetMaterial(0, new StandardMaterial3D { VertexColorUseAsAlbedo = true });
        }

        return new MeshInstance3D { Name = WorldNodeName, Mesh = mesh };
    }

    /// <summary>The bounds of every collider of <paramref name="kind"/>, in Godot space.</summary>
    public static Aabb ColliderBounds(CollisionData collision, ColliderKind kind)
    {
        ArgumentNullException.ThrowIfNull(collision);
        bool any = false;
        var bounds = new Aabb();
        foreach (Collider collider in collision.Colliders)
        {
            if (collider.Kind != kind)
            {
                continue;
            }

            Transform3D transform = UeSpace.TransformToGodot(collider.Location, collider.Rotation, collider.Scale, collider.PrePivot);
            foreach (FVector v in collision.VerticesOf(collider))
            {
                Vector3 p = transform * UeSpace.ToGodot(v);
                bounds = any ? bounds.Expand(p) : new Aabb(p, Vector3.Zero);
                any = true;
            }
        }

        return bounds;
    }
}
