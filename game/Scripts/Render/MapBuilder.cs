using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using Godot;
using OpenScct.Content.Collision;
using OpenScct.Core;
using OpenScct.Physics.Collision;
using OpenScct.Provenance;
using OpenScctGame.Render.Data;
using OpenScctGame.Render.Geometry;
using OpenScctGame.Render.Gltf;
using OpenScctGame.Render.Materials;

namespace OpenScctGame.Render;

/// <summary>How <see cref="MapBuilder"/> builds a map.</summary>
public sealed record RenderOptions
{
    /// <summary>Side of the square cells static geometry is merged in, in uu (UE X and Y).</summary>
    public float CellSize { get; init; } = 4096f;

    /// <summary>BSP lightmap factor; <see cref="RenderK.LightmapScale"/> unless a calibration run changes it.</summary>
    public float LightmapScale { get; init; } = RenderK.LightmapScale;

    /// <summary>Top mip levels left out of colour textures (TextureDetailWorld); 0 keeps them all.</summary>
    public int MipDrop { get; init; }

    /// <summary>Load sim/collision.bin for zone lookups (zone fog and ambient).</summary>
    public bool LoadZones { get; init; } = true;

    /// <summary>Place the class-default meshes of gameplay actors from the shared render pack (<see cref="GameplayObjects"/>).</summary>
    public bool GameplayObjects { get; init; } = true;

    /// <summary>Simulate and draw the map's particle emitters (render/fx.json).</summary>
    public bool Emitters { get; init; } = true;
}

/// <summary>Counts of one build.</summary>
public sealed class RenderMapStats
{
    public double LoadSeconds { get; set; }

    public int Triangles { get; set; }

    public int Vertices { get; set; }

    public int StaticNodes { get; set; }

    public int StaticSurfaces { get; set; }

    public int GameplayNodes { get; set; }

    public int GameplaySurfaces { get; set; }

    public int Instances { get; set; }

    public int Textures { get; set; }

    public int Materials { get; set; }

    public int ShaderVariants { get; set; }

    public int DynamicLights { get; set; }

    /// <summary>Gameplay actors drawn with their class-default mesh from the render pack.</summary>
    public int GameplayObjects { get; set; }

    /// <summary>Sprite emitters simulated and drawn.</summary>
    public int Emitters { get; set; }

    /// <summary>Materials with a heat or EMF texture from the render pack.</summary>
    public int VisionMaterials { get; set; }

    /// <summary>BSP triangles whose Godot winding agrees with the stored normal, of all BSP triangles checked.</summary>
    public int HandednessAgree { get; set; }

    public int HandednessChecked { get; set; }

    /// <summary>Triangles (BSP and baked static meshes) per zone index, for checking the zone classification.</summary>
    public SortedDictionary<int, int> ZoneTriangles { get; } = [];

    internal void CountZone(int zone) => ZoneTriangles[zone] = ZoneTriangles.GetValueOrDefault(zone) + 1;

    /// <summary>BSP triangles in the sky zone (drawn by the sky view only).</summary>
    public int SkyTriangles { get; set; }

    public override string ToString() => string.Create(CultureInfo.InvariantCulture,
        $"load={LoadSeconds:F2}s tris={Triangles} dynLights={DynamicLights} gameplayObjects={GameplayObjects} emitters={Emitters} visionMaterials={VisionMaterials} verts={Vertices} static={StaticNodes}/{StaticSurfaces} gameplay={GameplayNodes}/{GameplaySurfaces} instances={Instances} textures={Textures} materials={Materials} shaders={ShaderVariants} handedness={HandednessAgree}/{HandednessChecked} skyTris={SkyTriangles}");
}

/// <summary>A gameplay mesh actor drawn on its own node, so views can move, hide or relight it.</summary>
public sealed class GameplayMesh
{
    public required string ActorId { get; init; }

    public required string Class { get; init; }

    public required MeshInstance3D Node { get; init; }

    public required FVector Location { get; init; }

    public required LightSource Light { get; init; }

    public int AmbientGlow { get; init; }

    public float Radius { get; init; }
}

/// <summary>The Godot side of one map's render folder.</summary>
public sealed class RenderMap
{
    internal RenderMap(string folder, string mapId, Node3D root, MaterialsDoc materials, LightingDoc lighting, TextureCache textures,
        MaterialCompiler compiler, ActorLighting actorLighting, CollisionWorld? zones)
    {
        Folder = folder;
        MapId = mapId;
        Root = root;
        Materials = materials;
        Lighting = lighting;
        Textures = textures;
        Compiler = compiler;
        ActorLighting = actorLighting;
        Zones = zones;
        WorldLights = new WorldLights(lighting);
    }

    /// <summary>The map folder (the one holding manifest.json and render/).</summary>
    public string Folder { get; }

    /// <summary>The shared packs next to the map folders (.content/shared beside .content/svmap/&lt;Map&gt;).</summary>
    public string SharedRoot => SharedRootOf(Folder);

    /// <summary>
    /// The shared packs of a map folder: .content/shared beside .content/svmap/&lt;Map&gt;, else the checkout's (maps kept
    /// in fixtures/, such as the hub), else the export's or project's default.
    /// </summary>
    public static string SharedRootOf(string mapFolder) =>
        OpenScctGame.Play.Session.SharedContent.FindRoot(mapFolder) ?? Characters.SkeletalModel.DefaultSharedRoot();

    /// <summary>The shared render pack (vision textures, gameplay meshes), or null when it was not built.</summary>
    public RenderPack? Pack { get; internal set; }

    /// <summary>The map's gameplay actors (sim/gameplay.json over sim/classes.json).</summary>
    public GameplayActors Actors { get; internal set; } = new();

    /// <summary>LevelInfo.MTVSlope of the map (motion vision ramp).</summary>
    public int MtvSlope => Actors.LevelInfo?.Int("MTVSlope", RenderK.DefaultMtvSlope) ?? RenderK.DefaultMtvSlope;

    /// <summary>LevelInfo.MTVOffset of the map.</summary>
    public int MtvOffset => Actors.LevelInfo?.Int("MTVOffset", RenderK.DefaultMtvOffset) ?? RenderK.DefaultMtvOffset;

    public string MapId { get; }

    public Node3D Root { get; }

    public MaterialsDoc Materials { get; }

    public LightingDoc Lighting { get; }

    public TextureCache Textures { get; }

    public MaterialCompiler Compiler { get; }

    public ActorLighting ActorLighting { get; }

    /// <summary>Dynamic world lights (triggered, pulsing): applied per pixel to BSP and static meshes.</summary>
    public WorldLights WorldLights { get; }

    private readonly Dictionary<string, bool> _lightSwitches = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Switches a map light by its actor name (an STriggerLight's LightType on or off, spec/devices/trigger-light.md):
    /// the world term, the actor lighting and its corona follow.
    /// </summary>
    public void SetLightOn(string name, bool on)
    {
        _lightSwitches[name] = on;
        WorldLights.SetOn(name, on);
        ActorLighting.SetOn(name, on);
    }

    /// <summary>Whether the light <paramref name="name"/> is on: as last switched, else <paramref name="placed"/>.</summary>
    public bool IsLightOn(string name, bool placed) => _lightSwitches.TryGetValue(name, out bool on) ? on : placed;

    /// <summary>Scene time in seconds (Level.TimeSeconds): animated texture modifiers and light types follow it.</summary>
    public float Time { get; private set; }

    public void SetTime(float seconds)
    {
        Time = seconds;
        MaterialCompiler.SetTime(seconds);
        WorldLights.Update(seconds);
        ActorLighting.Time = seconds;
        Emitters?.SetTime(seconds);
    }

    /// <summary>The map's texture projectors (render/projectors.json), or null for content without them.</summary>
    public OpenScctGame.Render.Projectors.MapProjectors? Projectors { get; internal set; }

    /// <summary>True when the projectors are drawn as volumes, so the converter's PROJ_ decal meshes are left out.</summary>
    internal bool SkipProjectorDecals { get; set; }

    /// <summary>The light beams of the map's lights (render/lightbeams.json), or null for content without them.</summary>
    public OpenScctGame.Render.LightBeams.MapLightBeams? LightBeams { get; internal set; }

    /// <summary>The map's particle emitters, or null when the build left them out.</summary>
    public Particles.Emitters? Emitters { get; internal set; }

    /// <summary>The map's soft bodies (cloth patches and strip doors), simulated on this machine.</summary>
    public OpenScctGame.Render.SoftBodies.SoftBodyViews? SoftBodies { get; internal set; }

    internal CollisionWorld? Zones { get; }

    public RenderMapStats Stats { get; } = new();

    public List<string> Warnings { get; } = [];

    /// <summary>Bounds of the sky zones (collision ZONE records flagged skyZone), in Godot space.</summary>
    public List<Aabb> SkyBoxes { get; } = [];

    /// <summary>Gameplay mesh actors by actor id (movers, SGamePlayObjects, switches...).</summary>
    public Dictionary<string, GameplayMesh> Gameplay { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// The meshes gameplay objects swap to (TriggeredMesh, DamagedMeshes) that no placed actor shows, by asset path
    /// (ignoring case), lit by the actor.
    /// </summary>
    public Dictionary<string, ArrayMesh> SwapMeshes { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The static meshes the map's MeshEmitters draw, by asset path (ignoring case), lit by the actor.</summary>
    public Dictionary<string, ArrayMesh> ParticleMeshes { get; } = new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, (FVector Location, FRotator Rotation)> _lightPlaces = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Moves a map light by its actor name (an STriggerLight riding a mover or hung on an SAnimatedMesh bone): the
    /// world term, the actor lighting and its corona follow.
    /// </summary>
    public void MoveLight(string name, FVector location, FRotator rotation)
    {
        _lightPlaces[name] = (location, rotation);
        WorldLights.SetPlace(name, location, rotation);
        ActorLighting.SetPlace(name, location, rotation);
    }

    /// <summary>Where the light <paramref name="name"/> was last moved to; false while it has not moved.</summary>
    public bool TryGetLightPlace(string name, out FVector location, out FRotator rotation)
    {
        bool moved = _lightPlaces.TryGetValue(name, out (FVector Location, FRotator Rotation) place);
        (location, rotation) = place;
        return moved;
    }

    /// <summary>True when the world BSP alone does not block the line (static meshes and volumes are ignored).</summary>
    public bool BspLineOfSight(FVector from, FVector to) =>
        Zones is null || !Zones.LineCheck(out _, null, to, from, default,
            OpenScct.Physics.Collision.ETraceFlags.TRACE_Level | OpenScct.Physics.Collision.ETraceFlags.TRACE_StopAtFirstHit);

    /// <summary>The zone index of a point (UE space) through the collision BSP, or -1 without zone data.</summary>
    public int ZoneIndexAt(FVector location) => Zones?.GetZoneIndex(location) ?? -1;

    /// <summary>Light set of an actor at <paramref name="location"/> (zone ambient, AmbientGlow, own-zone lights).</summary>
    public ActorLightSet LightActor(FVector location, float radius, int ambientGlow) =>
        ActorLighting.Select(location, radius, AmbientAt(location, ambientGlow), ZoneIndexAt(location));

    /// <summary>The zone of a point (UE space) through the collision BSP, or null without zone data.</summary>
    public ZoneEntry? ZoneAt(FVector location) => Zones is null ? null : Lighting.ZoneByIndex(Zones.GetZoneIndex(location));

    /// <summary>
    /// A: the zone ambient plus AmbientGlow as grey, saturating, in display units. The zone ambient is
    /// m_RefAmbientColor, which every map stores as FGetHSV(AmbientHue, AmbientSaturation, AmbientBrightness); the
    /// converter computes that where it is not stored (generated maps).
    /// </summary>
    public Vector3 AmbientAt(FVector location, int ambientGlow)
    {
        Color a = ZoneAt(location)?.Ambient ?? new Color(0f, 0f, 0f, 0f);
        float g = ambientGlow / 255f;
        return new Vector3(MathF.Min(1f, a.R + g), MathF.Min(1f, a.G + g), MathF.Min(1f, a.B + g));
    }

    private readonly List<Node3D> _visionOnly = [];
    private bool _visionOnlyShown;

    /// <summary>
    /// Registers a node drawn only in thermal and EMF vision (RendMap 36 and 38): laser beams (bHideInNormalVision).
    /// docs/RENDERING.md#vision-modes.
    /// </summary>
    public void AddVisionOnly(Node3D node)
    {
        ArgumentNullException.ThrowIfNull(node);
        _visionOnly.Add(node);
        node.Visible = _visionOnlyShown;
    }

    public void RemoveVisionOnly(Node3D node) => _visionOnly.Remove(node);

    /// <summary>Shows or hides every vision-only node (the view calls this when RendMap changes).</summary>
    public void ShowVisionOnly(bool shown)
    {
        _visionOnlyShown = shown;
        foreach (Node3D node in _visionOnly)
        {
            if (GodotObject.IsInstanceValid(node))
            {
                node.Visible = shown;
            }
        }
    }

    /// <summary>True while thermal or EMF vision shows the vision-only nodes.</summary>
    public bool VisionOnlyShown => _visionOnlyShown;

    /// <summary>Relights every actor-lit gameplay mesh from its current location.</summary>
    public void RelightGameplay()
    {
        foreach (GameplayMesh g in Gameplay.Values)
        {
            if (g.Light == LightSource.Actor)
            {
                FVector at = UeSpace.ToUe(g.Node.GlobalPosition);
                ActorLighting.Apply(g.Node, LightActor(at, g.Radius, g.AmbientGlow));
            }
        }
    }
}

/// <summary>
/// Builds the Faithful view of an SVMap render folder (spec/svmap/1.1 render/): the world BSP and every placed static
/// mesh, merged per material and cell so a view draws few batches; lightmaps on UV2; baked per-instance vertex colours;
/// fullbright and actor-lit surfaces; gameplay meshes on their own nodes. Everything is placed through
/// <see cref="UeSpace"/>, the one UE to Godot conversion. docs/RENDERING.md.
/// </summary>
[PortOf("eng:Render.MapBuilder", Spec = "docs/RENDERING.md#map-builder")]
public static class MapBuilder
{
    public const string WorldFile = "render/world.glb";
    public const string MaterialsFile = "render/materials.json";
    public const string LightingFile = "render/lighting.json";
    public const string CollisionFile = "sim/collision.bin";

    /// <summary>The map folders searched by id: every given root, then the repository's .content/svmap, maps next to the exe, and an export's checkout.</summary>
    public static string? FindMapFolder(string mapId, IEnumerable<string>? roots = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(mapId);
        var all = new List<string>();
        if (roots is not null)
        {
            all.AddRange(roots);
        }

        all.AddRange(OpenScct.Server.ContentPaths.MapRoots);
        string projectDir = ProjectSettings.GlobalizePath("res://");
        string repo = Path.GetFullPath(Path.Combine(projectDir, ".."));
        all.Add(Path.Combine(repo, ".content", "svmap"));
        all.Add(Path.Combine(Path.GetDirectoryName(OS.GetExecutablePath()) ?? ".", "maps"));

        // An export inside a checkout (local\testbuild): res:// is the exe's folder there, so the checkout is found
        // the way the play path's MapCatalog finds it.
        if (OpenScct.Server.MapCatalog.FindRepoRoot(Path.GetDirectoryName(OS.GetExecutablePath()) ?? ".") is { } checkout)
        {
            all.Add(Path.Combine(checkout, ".content", "svmap"));
        }
        foreach (string root in all)
        {
            string folder = Path.Combine(root, mapId);
            if (File.Exists(Path.Combine(folder, WorldFile)))
            {
                return folder;
            }

            if (File.Exists(Path.Combine(root, WorldFile)) &&
                string.Equals(Path.GetFileName(Path.GetFullPath(root)), mapId, StringComparison.OrdinalIgnoreCase))
            {
                return root;
            }
        }

        return null;
    }

    public static RenderMap Build(string mapFolder, RenderOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(mapFolder);
        options ??= new RenderOptions();
        var clock = Stopwatch.StartNew();
        string glbPath = Path.Combine(mapFolder, WorldFile);
        if (!File.Exists(glbPath))
        {
            throw new FileNotFoundException($"no {WorldFile} in {mapFolder}", glbPath);
        }

        string renderDir = Path.Combine(mapFolder, "render");
        MaterialsDoc materials = MaterialsDoc.Load(Path.Combine(mapFolder, MaterialsFile));
        LightingDoc lighting = LightingDoc.Load(Path.Combine(mapFolder, LightingFile));
        var textures = new TextureCache(renderDir) { MipDrop = options.MipDrop };
        RenderPack? pack = RenderPack.Load(RenderMap.SharedRootOf(mapFolder));
        var compiler = new MaterialCompiler(materials, textures) { LightmapScale = options.LightmapScale, Pack = pack };
        MaterialCompiler.SetEmfEnvironment(pack?.Texture(RenderPack.EmfEnvironment));
        CollisionWorld? zones = null;
        var skyBoxes = new List<Aabb>();
        string collisionPath = Path.Combine(mapFolder, CollisionFile);
        if (options.LoadZones && File.Exists(collisionPath))
        {
            CollisionData collision = CollisionReader.Load(collisionPath);
            zones = CollisionWorld.Build(collision);
            foreach (CollisionZone z in collision.Zones)
            {
                if ((z.Flags & CollisionZoneFlags.SkyZone) != 0)
                {
                    Vector3 a = UeSpace.ToGodot(z.AabbMin);
                    Vector3 b = UeSpace.ToGodot(z.AabbMax);
                    skyBoxes.Add(new Aabb(a.Min(b), (a - b).Abs()).Grow(UeSpace.LengthToGodot(1f)));
                }
            }
        }

        using GlbDocument glb = GlbDocument.Load(glbPath);
        string mapId = Path.GetFileName(Path.GetFullPath(mapFolder));
        var root = new Node3D { Name = "RenderMap" };
        var map = new RenderMap(mapFolder, mapId, root, materials, lighting, textures, compiler, new ActorLighting(lighting), zones);
        map.SkyBoxes.AddRange(skyBoxes);
        map.Pack = pack;
        map.Actors = GameplayActors.Load(mapFolder);
        List<OpenScctGame.Render.Projectors.ProjectorEntry>? projectors = OpenScctGame.Render.Projectors.ProjectorsDoc.Load(mapFolder);
        map.SkipProjectorDecals = projectors is not null;
        map.SoftBodies = new OpenScctGame.Render.SoftBodies.SoftBodyViews(root);
        new Builder(map, glb, options).Run();
        if (options.GameplayObjects)
        {
            OpenScctGame.Render.GameplayObjects.Build(map, map.Actors, pack);
        }

        OpenScctGame.Render.GameplayObjects.ApplyVision(map, map.Actors);
        if (options.Emitters)
        {
            map.Emitters = new Particles.Emitters(map, root);
            map.Stats.Emitters = map.Emitters.Count;
        }

        if (projectors is not null)
        {
            map.Projectors = OpenScctGame.Render.Projectors.MapProjectors.Build(map, projectors, root);
        }

        map.LightBeams = OpenScctGame.Render.LightBeams.MapLightBeams.Build(map, root);
        if (pack is null)
        {
            map.Warnings.Add("no render pack (.content/shared/render): vision textures and gameplay object meshes are missing");
        }
        map.SetTime(0f);
        map.Stats.LoadSeconds = clock.Elapsed.TotalSeconds;
        map.Stats.Textures = textures.Count;
        map.Stats.Materials = compiler.MaterialCount;
        map.Stats.ShaderVariants = compiler.ShaderVariants;
        map.Stats.VisionMaterials = compiler.VisionMaterials;
        map.Stats.DynamicLights = map.WorldLights.Count;
        foreach (string unknown in compiler.Unknown)
        {
            map.Warnings.Add("material not drawn (no texture or flags): " + unknown);
        }

        foreach (string missing in textures.Missing)
        {
            map.Warnings.Add("texture missing: " + missing);
        }

        return map;
    }

    /// <summary>One build: reads the glTF once and fills the map's nodes.</summary>
    private sealed class Builder
    {
        private readonly RenderMap _map;
        private readonly GlbDocument _glb;
        private readonly RenderOptions _options;
        private readonly List<string> _materialNames = [];
        private readonly Dictionary<string, int> _libraryMeshes = new(StringComparer.Ordinal);
        private readonly HashSet<string> _usedLibraryMeshes = new(StringComparer.Ordinal);
        private readonly HashSet<string> _particleMeshes = new(StringComparer.Ordinal);
        private readonly Dictionary<int, MeshPrimitive[]> _primitives = [];
        private readonly SortedDictionary<(int X, int Y, int Sky), Dictionary<(string, LightSource), SurfaceBuilder>> _cells = new();
        private readonly Dictionary<(string Mesh, LightSource Light, int Glow), ArrayMesh> _shared = [];
        private readonly Node3D _static = new() { Name = "Static" };
        private readonly Node3D _gameplay = new() { Name = "Gameplay" };

        public Builder(RenderMap map, GlbDocument glb, RenderOptions options)
        {
            _map = map;
            _glb = glb;
            _options = options;
        }

        public void Run()
        {
            _map.Root.AddChild(_static);
            _map.Root.AddChild(_gameplay);
            ReadMaterials();
            ReadNodes(out List<(int Mesh, bool Sky)> bspMeshes);
            Prefetch();
            foreach ((int mesh, bool sky) in bspMeshes)
            {
                AddBsp(mesh, sky);
            }

            if (_glb.Root.TryGetProperty("extras", out JsonElement extras) && extras.TryGetProperty("svmap", out JsonElement svmap)
                && svmap.TryGetProperty("instances", out JsonElement instances))
            {
                foreach (JsonElement inst in instances.EnumerateArray())
                {
                    AddInstance(inst);
                }
            }

            AddSwapMeshes();
            FlushCells();
        }

        /// <summary>
        /// The library meshes no placed actor uses: the meshes gameplay objects swap to (TriggeredMesh,
        /// DamagedMeshes[].StaticMesh), which the converter adds to the library. They are built once, lit by the
        /// actor, for <see cref="RenderMap.SwapMeshes"/>.
        /// </summary>
        private void AddSwapMeshes()
        {
            foreach ((string name, int mesh) in _libraryMeshes)
            {
                bool used = _usedLibraryMeshes.Contains(name);
                bool particle = _particleMeshes.Contains(name);
                if ((used && !particle) || name.Contains('#', StringComparison.Ordinal))
                {
                    continue;
                }

                var arrayMesh = new ArrayMesh { ResourceName = name };
                foreach (MeshPrimitive prim in Primitives(mesh))
                {
                    if (_map.Compiler.Get(prim.Material, LightSource.Actor) is not { } material)
                    {
                        continue;
                    }

                    var surface = new SurfaceBuilder(false, false);
                    surface.Add(Transform3D.Identity, prim, 0, null);
                    surface.AddTo(arrayMesh, material);
                }

                if (arrayMesh.GetSurfaceCount() > 0)
                {
                    if (!used)
                    {
                        _map.SwapMeshes[name] = arrayMesh;
                    }

                    if (particle)
                    {
                        _map.ParticleMeshes[name] = arrayMesh;
                    }
                }
            }
        }

        private void ReadMaterials()
        {
            if (!_glb.Root.TryGetProperty("materials", out JsonElement mats))
            {
                return;
            }

            foreach (JsonElement m in mats.EnumerateArray())
            {
                string name = m.TryGetProperty("name", out JsonElement n) ? n.GetString() ?? "" : "";
                _materialNames.Add(name);
                if (m.TryGetProperty("extras", out JsonElement ex) && ex.TryGetProperty("scct_lightmap", out JsonElement lm)
                    && _map.Materials.Find(name) is { } entry)
                {
                    entry.Lightmap = lm.GetString();
                }
            }
        }

        private void ReadNodes(out List<(int Mesh, bool Sky)> bspMeshes)
        {
            bspMeshes = [];
            JsonElement nodes = _glb.Root.GetProperty("nodes");
            foreach (JsonElement node in nodes.EnumerateArray())
            {
                if (!node.TryGetProperty("mesh", out JsonElement meshEl) || !node.TryGetProperty("extras", out JsonElement ex)
                    || !ex.TryGetProperty("svmap", out JsonElement sv))
                {
                    continue;
                }

                string role = sv.TryGetProperty("role", out JsonElement r) ? r.GetString() ?? "" : "";
                if (role == "bsp")
                {
                    bool sky = sv.TryGetProperty("sky", out JsonElement skyEl) && skyEl.ValueKind == JsonValueKind.True;
                    bspMeshes.Add((meshEl.GetInt32(), sky));
                }
                else if (role == "libraryMesh" && node.TryGetProperty("name", out JsonElement name))
                {
                    _libraryMeshes[name.GetString() ?? ""] = meshEl.GetInt32();
                    if (sv.TryGetProperty("particle", out JsonElement particle) && particle.ValueKind == JsonValueKind.True)
                    {
                        _particleMeshes.Add(name.GetString() ?? "");
                    }
                }
            }
        }

        private void Prefetch()
        {
            var wanted = new List<(string, bool)>();
            foreach (string name in _materialNames)
            {
                if (_map.Materials.Find(name) is { } m)
                {
                    wanted.AddRange(_map.Compiler.TexturesOf(m));
                }
            }

            _map.Textures.Prefetch(wanted);
        }

        private MeshPrimitive[] Primitives(int mesh)
        {
            if (_primitives.TryGetValue(mesh, out MeshPrimitive[]? cached))
            {
                return cached;
            }

            JsonElement prims = _glb.Root.GetProperty("meshes")[mesh].GetProperty("primitives");
            var list = new List<MeshPrimitive>();
            foreach (JsonElement p in prims.EnumerateArray())
            {
                JsonElement attrs = p.GetProperty("attributes");
                int material = p.TryGetProperty("material", out JsonElement mi) ? mi.GetInt32() : -1;
                float[] positions = _glb.ReadFloats(attrs.GetProperty("POSITION").GetInt32());
                int[] indices = p.TryGetProperty("indices", out JsonElement ii)
                    ? _glb.ReadIndices(ii.GetInt32())
                    : Sequence(positions.Length / 3);
                list.Add(new MeshPrimitive
                {
                    Material = material >= 0 && material < _materialNames.Count ? _materialNames[material] : "",
                    Positions = positions,
                    Normals = attrs.TryGetProperty("NORMAL", out JsonElement nn) ? _glb.ReadFloats(nn.GetInt32()) : null,
                    Uvs = attrs.TryGetProperty("TEXCOORD_0", out JsonElement t0) ? _glb.ReadFloats(t0.GetInt32()) : null,
                    Uv2s = attrs.TryGetProperty("TEXCOORD_1", out JsonElement t1) ? _glb.ReadFloats(t1.GetInt32()) : null,
                    Colours = attrs.TryGetProperty("COLOR_0", out JsonElement c0) ? _glb.ReadBytes(c0.GetInt32()) : null,
                    Indices = indices,
                });
            }

            MeshPrimitive[] result = [.. list];
            _primitives[mesh] = result;
            return result;
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

        /// <summary>A BSP mesh of the world; <paramref name="sky"/> marks the sky zone's own BSP (role bsp, sky true).</summary>
        private void AddBsp(int mesh, bool sky)
        {
            foreach (MeshPrimitive prim in Primitives(mesh))
            {
                CheckHandedness(prim);
                LightSource light = prim.Uv2s is not null ? LightSource.Lightmap : LightSource.None;

                // BSP primitives can span the map: split them per triangle into cells.
                AddSplitByCell(prim, light, sky);
                _map.Stats.Triangles += prim.Indices.Length / 3;
            }
        }

        private void AddSplitByCell(MeshPrimitive prim, LightSource light, bool sky)
        {
            var byCell = new Dictionary<(int, int, int), List<int>>();
            var byCellZones = new Dictionary<(int, int, int), List<byte>>();
            for (int t = 0; t + 2 < prim.Indices.Length; t += 3)
            {
                Vector3 a = Pos(prim, prim.Indices[t]);
                Vector3 b = Pos(prim, prim.Indices[t + 1]);
                Vector3 c = Pos(prim, prim.Indices[t + 2]);
                Vector3 center = (a + b + c) / 3f;
                byte zone = ZoneOfSurface(center, (b - a).Cross(c - a));
                (int, int, int) cell = CellOf(center, sky || InSkyZone(center));
                if (!byCell.TryGetValue(cell, out List<int>? tris))
                {
                    tris = [];
                    byCell[cell] = tris;
                    byCellZones[cell] = [];
                }

                byCellZones[cell].Add(zone);
                tris.Add(prim.Indices[t]);
                tris.Add(prim.Indices[t + 1]);
                tris.Add(prim.Indices[t + 2]);
            }

            foreach (((int, int, int) cell, List<int> tris) in byCell)
            {
                var part = new MeshPrimitive
                {
                    Material = prim.Material,
                    Positions = prim.Positions,
                    Normals = prim.Normals,
                    Uvs = prim.Uvs,
                    Uv2s = prim.Uv2s,
                    Colours = prim.Colours,
                    Indices = [.. tris],
                };
                Bucket(cell, prim.Material, light, light == LightSource.Lightmap, true)
                    .Add(Transform3D.Identity, part, 0, [.. byCellZones[cell]]);
            }
        }

        private void CheckHandedness(MeshPrimitive prim)
        {
            if (prim.Normals is null)
            {
                return;
            }

            for (int t = 0; t + 2 < prim.Indices.Length; t += 3)
            {
                // Godot order of a glTF triangle (a, c, b) is (a, b, c) swapped: its front normal is (c' - a) x (b' - a)
                // with b' = glTF c and c' = glTF b, i.e. (b - a) x (c - a) in glTF order.
                Vector3 a = Pos(prim, prim.Indices[t]);
                Vector3 b = Pos(prim, prim.Indices[t + 1]);
                Vector3 c = Pos(prim, prim.Indices[t + 2]);
                Vector3 geometric = (b - a).Cross(c - a);
                if (geometric.LengthSquared() < 1e-12f)
                {
                    continue;
                }

                int v = prim.Indices[t];
                var stored = new Vector3(prim.Normals[v * 3], prim.Normals[(v * 3) + 1], prim.Normals[(v * 3) + 2]);
                _map.Stats.HandednessChecked++;
                if (geometric.Dot(stored) > 0f)
                {
                    _map.Stats.HandednessAgree++;
                }
            }
        }

        /// <summary>
        /// The zone of a BSP surface: of a point just in front of it, through the collision BSP. Points 2, 8 and 32 uu
        /// out are tried in turn until one lands in a zone other than 0 (the outside), so thin solids and coplanar
        /// collision faces do not leave a surface without its zone.
        /// </summary>
        private byte ZoneOfSurface(Vector3 center, Vector3 frontNormal)
        {
            if (_map.Zones is null || frontNormal.LengthSquared() < 1e-12f)
            {
                return 0;
            }

            FVector c = UeSpace.ToUe(center);
            FVector n = UeSpace.DirectionToUe(frontNormal.Normalized());
            int zone = 0;
            foreach (float d in SurfaceZoneOffsets)
            {
                zone = _map.Zones.GetZoneIndex(c + (n * d));
                if (zone != 0)
                {
                    break;
                }
            }

            _map.Stats.CountZone(zone);
            return (byte)Math.Clamp(zone, 0, 255);
        }

        private static readonly float[] SurfaceZoneOffsets = [2f, 8f, 32f];

        /// <summary>
        /// Zones of a mesh's triangles placed by <paramref name="transform"/>, each from its triangle's surface (like the
        /// BSP), so decals and meshes spanning a portal take the zone of the room they face. Faces whose surface point
        /// is inside solid (zone 0: backs against walls, faces inside door frames) take the actor's zone, the one UE2
        /// lights the whole actor by.
        /// </summary>
        private byte[] ZonesOf(MeshPrimitive prim, in Transform3D transform, FVector actorLocation)
        {
            var zones = new byte[prim.Indices.Length / 3];
            if (_map.Zones is null)
            {
                return zones;
            }

            bool mirrored = transform.Basis.Determinant() < 0f;
            byte? actorZone = null;
            for (int t = 0; t + 2 < prim.Indices.Length; t += 3)
            {
                Vector3 a = transform * Pos(prim, prim.Indices[t]);
                Vector3 b = transform * Pos(prim, prim.Indices[t + 1]);
                Vector3 c = transform * Pos(prim, prim.Indices[t + 2]);
                Vector3 front = (b - a).Cross(c - a) * (mirrored ? -1f : 1f);
                byte zone = ZoneOfSurface((a + b + c) / 3f, front);
                if (zone == 0)
                {
                    zone = actorZone ??= (byte)Math.Clamp(_map.Zones.GetZoneIndex(actorLocation), 0, 255);
                }

                zones[t / 3] = zone;
            }

            return zones;
        }

        private static Vector3 Pos(MeshPrimitive p, int v) => new(p.Positions[v * 3], p.Positions[(v * 3) + 1], p.Positions[(v * 3) + 2]);

        private (int, int, int) CellOf(Vector3 godot, bool sky)
        {
            float size = UeSpace.LengthToGodot(_options.CellSize);
            return ((int)MathF.Floor(godot.X / size), (int)MathF.Floor(godot.Z / size), sky ? 1 : 0);
        }

        /// <summary>
        /// True when a BSP triangle lies in a sky zone (its centre inside the zone's bounds). Sky zone geometry is
        /// drawn only by the sky view, as UE2 draws it only in the sky pass.
        /// </summary>
        private bool InSkyZone(Vector3 center)
        {
            foreach (Aabb box in _map.SkyBoxes)
            {
                if (box.HasPoint(center))
                {
                    _map.Stats.SkyTriangles++;
                    return true;
                }
            }

            return false;
        }

        private SurfaceBuilder Bucket((int, int, int) cell, string material, LightSource light, bool uv2, bool colour)
        {
            if (!_cells.TryGetValue(cell, out Dictionary<(string, LightSource), SurfaceBuilder>? buckets))
            {
                buckets = [];
                _cells[cell] = buckets;
            }

            if (!buckets.TryGetValue((material, light), out SurfaceBuilder? b))
            {
                b = new SurfaceBuilder(uv2, colour, hasVision: true);
                buckets[(material, light)] = b;
            }

            // BSP and instances without values of their own draw at the Actor defaults.
            b.Vision = SurfaceBuilder.DefaultVision;
            return b;
        }

        private void AddInstance(JsonElement inst)
        {
            string meshName = Str(inst, "mesh") ?? "";
            if (!_libraryMeshes.TryGetValue(meshName, out int mesh))
            {
                _map.Warnings.Add("instance mesh not in the library: " + meshName);
                return;
            }

            _usedLibraryMeshes.Add(meshName);
            if (_map.SkipProjectorDecals && inst.TryGetProperty("projectorOf", out _))
            {
                return;   // drawn as a projector volume (MapProjectors)
            }

            _map.Stats.Instances++;
            string actor = Str(inst, "actor") ?? "";
            string cls = Str(inst, "class") ?? "";
            bool hidden = Bool(inst, "hidden");
            bool gameplay = Bool(inst, "gameplay") || Str(inst, "kind") == "mover";
            bool unlit = Bool(inst, "unlit");
            int glow = inst.TryGetProperty("ambientGlow", out JsonElement g) ? g.GetInt32() : 0;
            FVector location = Vec(inst, "location");
            FRotator rotation = Rot(inst, "rotation");
            FVector scale = inst.TryGetProperty("scale", out _) ? Vec(inst, "scale") : new FVector(1f, 1f, 1f);
            FVector prePivot = Vec(inst, "prePivot");
            Transform3D transform = UeSpace.TransformToGodot(location, rotation, scale, prePivot);
            MeshPrimitive[] prims = Primitives(mesh);
            bool baked = prims.Length > 0 && Array.TrueForAll(prims, p => p.Colours is not null);
            LightSource light = unlit ? LightSource.None : baked ? LightSource.Vertex : LightSource.Actor;

            if (hidden && !gameplay)
            {
                return;
            }

            if (!hidden && inst.TryGetProperty("softBody", out JsonElement softBody)
                && _map.SoftBodies?.Add(actor, softBody, transform, location, prims, _map.Compiler, VisionOf(inst)) == true)
            {
                return;
            }

            if (gameplay || light == LightSource.Actor)
            {
                AddGameplay(actor, cls, meshName, mesh, transform, location, light, glow, hidden, VisionOf(inst));
                return;
            }

            Aabb box = Bounds(prims, transform);
            (int, int, int) cell = CellOf(box.GetCenter(), Bool(inst, "skyZone") || InSkyZone(box.GetCenter()));
            Vector4 vision = VisionOf(inst);
            foreach (MeshPrimitive prim in prims)
            {
                SurfaceBuilder bucket = Bucket(cell, prim.Material, light, false, light == LightSource.Vertex);
                bucket.Vision = vision;
                byte[]? zones = light == LightSource.Vertex ? ZonesOf(prim, transform, location) : null;
                if (light == LightSource.Vertex && glow > 0)
                {
                    AddGlowing(bucket, transform, prim, glow, zones);
                }
                else
                {
                    bucket.Add(transform, prim, 0, zones);
                }

                _map.Stats.Triangles += prim.Indices.Length / 3;
            }
        }

        /// <summary>
        /// Baked vertex light with AmbientGlow: light = min(2, 2 VC + min(1, 2 glow / 255)). COLOR stores half of it,
        /// which keeps 8-bit precision for the 2x stage in the shader.
        /// </summary>
        [Provenance(ProvenanceKind.Measured, "BankD skyline sweep (AmbientGlow over baked colours, term capped at 1)")]
        private static void AddGlowing(SurfaceBuilder bucket, in Transform3D transform, MeshPrimitive prim, int glow, byte[]? zones)
        {
            float term = MathF.Min(RenderK.AmbientGlowCap, 2f * glow / 255f) * 0.5f;
            var colours = new byte[prim.VertexCount * 4];
            for (int i = 0; i < prim.VertexCount; i++)
            {
                Color c = prim.ColourAt(i);
                colours[i * 4] = ToByte(MathF.Min(1f, c.R + term));
                colours[(i * 4) + 1] = ToByte(MathF.Min(1f, c.G + term));
                colours[(i * 4) + 2] = ToByte(MathF.Min(1f, c.B + term));
                colours[(i * 4) + 3] = 255;
            }

            var lit = new MeshPrimitive
            {
                Material = prim.Material,
                Positions = prim.Positions,
                Normals = prim.Normals,
                Uvs = prim.Uvs,
                Uv2s = prim.Uv2s,
                Colours = colours,
                Indices = prim.Indices,
            };
            bucket.Add(transform, lit, 0, zones);
        }

        private static byte ToByte(float v) => (byte)Math.Clamp((int)MathF.Round(v * 255f), 0, 255);

        /// <summary>
        /// An instance's vision values (instance extras "vision": the actor's HeatIntensity, HeatOpacity, EMFIntensity,
        /// EMFOpacity and bEMFIsOn), in the template's layout; the Actor defaults without them.
        /// </summary>
        private static Vector4 VisionOf(JsonElement inst)
        {
            if (!inst.TryGetProperty("vision", out JsonElement v) || v.ValueKind != JsonValueKind.Object)
            {
                return SurfaceBuilder.DefaultVision;
            }

            float F(string k, float d) => v.TryGetProperty(k, out JsonElement x) && x.ValueKind == JsonValueKind.Number ? x.GetSingle() : d;
            bool on = !v.TryGetProperty("emfOn", out JsonElement o) || o.ValueKind != JsonValueKind.False;
            return new Vector4(F("heat", 0f), F("heatOpacity", 1f), on ? F("emf", 0f) : 0f, F("emfOpacity", 1f));
        }

        private static Aabb Bounds(MeshPrimitive[] prims, in Transform3D transform)
        {
            var box = new Aabb();
            bool any = false;
            foreach (MeshPrimitive p in prims)
            {
                Aabb b = p.Bounds(transform);
                box = any ? box.Merge(b) : b;
                any = true;
            }

            return box;
        }

        private void AddGameplay(string actor, string cls, string meshName, int mesh, in Transform3D transform, FVector location,
            LightSource light, int glow, bool hidden, Vector4 vision)
        {
            int glowKey = light == LightSource.Vertex ? glow : 0;
            // Vertex-lit gameplay meshes carry their zones per vertex, so they are not shared between actors.
            string shareKey = light == LightSource.Vertex ? meshName + "@" + actor : meshName;
            if (!_shared.TryGetValue((shareKey, light, glowKey), out ArrayMesh? arrayMesh))
            {
                arrayMesh = new ArrayMesh { ResourceName = meshName };
                foreach (MeshPrimitive prim in Primitives(mesh))
                {
                    Material? material = _map.Compiler.Get(prim.Material, light);
                    if (material is null)
                    {
                        continue;
                    }

                    var surface = new SurfaceBuilder(false, light == LightSource.Vertex);
                    byte[]? zones = light == LightSource.Vertex ? ZonesOf(prim, transform, location) : null;
                    if (light == LightSource.Vertex && glow > 0)
                    {
                        AddGlowing(surface, Transform3D.Identity, prim, glow, zones);
                    }
                    else
                    {
                        surface.Add(Transform3D.Identity, prim, 0, zones);
                    }

                    surface.AddTo(arrayMesh, material);
                    _map.Stats.Vertices += surface.VertexCount;
                }

                _shared[(shareKey, light, glowKey)] = arrayMesh;
            }

            var node = new MeshInstance3D { Name = actor.Length > 0 ? actor : meshName, Mesh = arrayMesh, Transform = transform, Visible = !hidden };
            _gameplay.AddChild(node);
            node.SetInstanceShaderParameter(OpenScctGame.Render.GameplayObjects.VisionActorParameter, vision);
            _map.Stats.GameplayNodes++;
            _map.Stats.GameplaySurfaces += arrayMesh.GetSurfaceCount();
            foreach (MeshPrimitive prim in Primitives(mesh))
            {
                _map.Stats.Triangles += prim.Indices.Length / 3;
            }

            Aabb box = arrayMesh.GetSurfaceCount() > 0 ? transform * arrayMesh.GetAabb() : new Aabb(transform.Origin, Vector3.Zero);
            float radius = UeSpace.UnitsPerMetre * box.Size.Length() * 0.5f;
            var entry = new GameplayMesh
            {
                ActorId = actor,
                Class = cls,
                Node = node,
                Location = location,
                Light = light,
                AmbientGlow = glow,
                Radius = radius,
            };
            if (actor.Length > 0)
            {
                _map.Gameplay[actor] = entry;
            }

            if (light == LightSource.Actor && arrayMesh.GetSurfaceCount() > 0)
            {
                FVector center = UeSpace.ToUe(box.GetCenter());
                ActorLighting.Apply(node, _map.LightActor(center, radius, glow));
            }
        }

        /// <summary>Godot's limit on surfaces in one mesh (RenderingServer MAX_MESH_SURFACES).</summary>
        private const int MaxSurfacesPerMesh = 256;

        private void FlushCells()
        {
            foreach (((int x, int y, int sky) cell, Dictionary<(string, LightSource), SurfaceBuilder> buckets) in _cells)
            {
                string baseName = string.Create(CultureInfo.InvariantCulture, $"{(cell.sky != 0 ? "Sky" : "Cell")}_{cell.x}_{cell.y}");
                var keys = new List<(string, LightSource)>(buckets.Keys);
                keys.Sort((a, b) => string.CompareOrdinal(a.Item1, b.Item1) != 0 ? string.CompareOrdinal(a.Item1, b.Item1) : a.Item2.CompareTo(b.Item2));

                // A cell with more materials than one mesh can hold (MoonD's cells pass 256) is split over several
                // meshes, so no surface is dropped.
                int part = 0;
                var mesh = new ArrayMesh { ResourceName = baseName };
                foreach ((string, LightSource) key in keys)
                {
                    Material? material = _map.Compiler.Get(key.Item1, key.Item2);
                    if (material is null)
                    {
                        continue;
                    }

                    SurfaceBuilder b = buckets[key];
                    if (b.TriangleCount == 0)
                    {
                        continue;
                    }

                    if (mesh.GetSurfaceCount() == MaxSurfacesPerMesh)
                    {
                        AddCellMesh(mesh, cell.sky);
                        part++;
                        mesh = new ArrayMesh { ResourceName = string.Create(CultureInfo.InvariantCulture, $"{baseName}_{part}") };
                    }

                    b.AddTo(mesh, material);
                    _map.Stats.Vertices += b.VertexCount;
                }

                AddCellMesh(mesh, cell.sky);
            }
        }

        private void AddCellMesh(ArrayMesh mesh, int sky)
        {
            if (mesh.GetSurfaceCount() == 0)
            {
                return;
            }

            _static.AddChild(new MeshInstance3D
            {
                Name = mesh.ResourceName,
                Mesh = mesh,
                Layers = sky != 0 ? RenderLayers.Sky : RenderLayers.World,
            });
            _map.Stats.StaticNodes++;
            _map.Stats.StaticSurfaces += mesh.GetSurfaceCount();
        }

        private static string? Str(JsonElement e, string name) =>
            e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        private static bool Bool(JsonElement e, string name) =>
            e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.True;

        private static FVector Vec(JsonElement e, string name)
        {
            if (!e.TryGetProperty(name, out JsonElement v) || v.ValueKind != JsonValueKind.Array || v.GetArrayLength() < 3)
            {
                return default;
            }

            return new FVector(v[0].GetSingle(), v[1].GetSingle(), v[2].GetSingle());
        }

        private static FRotator Rot(JsonElement e, string name)
        {
            if (!e.TryGetProperty(name, out JsonElement v) || v.ValueKind != JsonValueKind.Object)
            {
                return default;
            }

            int Get(string k) => v.TryGetProperty(k, out JsonElement x) ? x.GetInt32() : 0;
            return new FRotator(Get("Pitch"), Get("Yaw"), Get("Roll"));
        }
    }
}
