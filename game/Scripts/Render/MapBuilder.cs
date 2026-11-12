using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using OpenScct.Core;
using OpenScct.Physics.Collision;
using OpenScct.Provenance;
using OpenScctGame.Diagnostics;
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
        MeshSpheres = new MeshLightSpheres();
        LightReach = new LightReach(root, WorldLights);
    }

    /// <summary>The bounding spheres the map's vertex-lit static meshes take their dynamic lights over.</summary>
    public MeshLightSpheres MeshSpheres { get; }

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

    /// <summary>The map's outfits (render/outfits.json, SVMap 1.2); none when the map has no file.</summary>
    public OpenScct.Content.MapOutfits Outfits { get; internal set; } = OpenScct.Content.MapOutfits.None;

    /// <summary>
    /// The outfits of a map folder. A file this build cannot read leaves the stock look (the outfits change only the
    /// look, so a map with a newer outfit file still plays).
    /// </summary>
    public static OpenScct.Content.MapOutfits OutfitsOf(string mapFolder)
    {
        try
        {
            return OpenScct.Content.OutfitsDoc.Load(mapFolder) ?? OpenScct.Content.MapOutfits.None;
        }
        catch (OpenScct.Content.SvMapFormatException e)
        {
            GD.PushWarning($"{mapFolder}: outfits left out: {e.Message}");
            return OpenScct.Content.MapOutfits.None;
        }
    }

    private readonly List<IReadOnlySet<string>> _echelonGroups = [new HashSet<string>()];
    private readonly Dictionary<string, int> _echelonGroupIds = new(StringComparer.Ordinal) { [""] = 0 };

    /// <summary>
    /// The distinct IgnoredEchelonLights lists of the map's surfaces, by group number (0: no list), for the spot
    /// slots' ignore table (<see cref="SpotShadowLight.SetIgnoreGroups"/>).
    /// </summary>
    public IReadOnlyList<IReadOnlySet<string>> EchelonGroups => _echelonGroups;

    /// <summary>
    /// The template's Echelon code of a surface: its flags (1 skips static spot shadow lights, 2 dynamic ones) plus
    /// 4 x the group of its IgnoredEchelonLights (light actor names).
    /// </summary>
    public int EchelonCode(int flags, IEnumerable<string>? ignoredLights)
    {
        int group = 0;
        if (ignoredLights is not null)
        {
            var names = new SortedSet<string>(ignoredLights, StringComparer.Ordinal);
            string key = string.Join('|', names);
            if (!_echelonGroupIds.TryGetValue(key, out group))
            {
                group = _echelonGroups.Count;
                _echelonGroups.Add(new HashSet<string>(names, StringComparer.Ordinal));
                _echelonGroupIds[key] = group;
            }
        }

        return (flags & 3) | (group << 2);
    }

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

    /// <summary>The merged cell meshes' choice of materials without the spot (and dynamic) light terms where no such light reaches.</summary>
    public LightReach LightReach { get; }

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

    /// <summary>Visible sprite actors of plain map lights, including sky sprites and attached warning lamps.</summary>
    public MapLightSprites? LightSprites { get; internal set; }

    /// <summary>The map's particle emitters, or null when the build left them out.</summary>
    public Particles.Emitters? Emitters { get; internal set; }

    /// <summary>The map's soft bodies (cloth patches and strip doors), simulated on this machine.</summary>
    public OpenScctGame.Render.SoftBodies.SoftBodyViews? SoftBodies { get; internal set; }

    internal CollisionWorld? Zones { get; }

    public RenderMapStats Stats { get; } = new();

    /// <summary>SVM_RD_PROTO only: the merged cell geometry for the RenderingDevice prototype, else null.</summary>
    internal OpenScctGame.Render.RdProto.RdProtoGeometry? RdGeometry { get; set; }

    /// <summary>SVM_RD_WORLD=1: the merged geometry the world pass on the RenderingDevice draws (Rd/RdWorldRenderer).</summary>
    internal Rd.RdWorldGeometry? RdWorld { get; set; }

    public List<string> Warnings { get; } = [];

    /// <summary>Gameplay mesh actors by actor id (movers, SGamePlayObjects, switches...).</summary>
    public Dictionary<string, GameplayMesh> Gameplay { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// The meshes gameplay objects swap to (TriggeredMesh, DamagedMeshes) that no placed actor shows, and the meshes
    /// conveyors carry as items (also those a placed actor shows), by asset path (ignoring case), lit by the actor.
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
        LightSprites?.SetPlace(name, location);
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

    /// <summary>
    /// Light set of a mesh actor whose bounding sphere is (<paramref name="center"/>, <paramref name="radius"/>): the
    /// lights are ranked and fitted over the sphere, but the zone (its ambient, and which own-zone lights reach it) is
    /// the actor's point region, taken at <paramref name="actorLocation"/> as the original does. The sphere's centre of
    /// a large mesh can sit inside solid geometry (zone 0) while the actor stands in a room: MoonD's carousel mover,
    /// whose sphere centre is inside the drum, lost the zone ambient and both own-zone plaza floods that way.
    /// </summary>
    public ActorLightSet LightActor(FVector center, float radius, int ambientGlow, FVector actorLocation) =>
        ActorLighting.Select(center, radius, AmbientAt(actorLocation, ambientGlow), ZoneIndexAt(actorLocation));

    /// <summary>Incident room lighting for volumes, including sources already baked into the surrounding surfaces.</summary>
    public ActorLightSet LightSmoke(FVector location, float radius) =>
        ActorLighting.SelectForSmoke(location, radius, AmbientAt(location, 0), ZoneIndexAt(location));

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
                var sphere = ActorLighting.SphereOf(g.Node.Mesh, g.Node.GlobalTransform);
                ActorLighting.Apply(g.Node, LightActor(sphere.Center, sphere.Radius, g.AmbientGlow, UeSpace.ToUe(g.Node.GlobalTransform.Origin)));
            }
        }

        SoftBodies?.Relight(this);
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

        // These files are plain data (no Godot objects): they are read on worker threads at once, and each is taken
        // where it was read before, so a bad file fails the same way.
        Task<MaterialsDoc> materialsRead = Task.Run(() => MaterialsDoc.Load(Path.Combine(mapFolder, MaterialsFile)));
        Task<LightingDoc> lightingRead = Task.Run(() => LightingDoc.Load(Path.Combine(mapFolder, LightingFile)));
        Task<GlbDocument> glbRead = Task.Run(() => GlbDocument.Load(glbPath));
        Task<GameplayActors> actorsRead = Task.Run(() => GameplayActors.Load(mapFolder));
        Task<List<OpenScctGame.Render.Projectors.ProjectorEntry>?> projectorsRead =
            Task.Run(() => OpenScctGame.Render.Projectors.ProjectorsDoc.Load(mapFolder));
        var textures = new TextureCache(renderDir) { MipDrop = options.MipDrop };
        RenderPack? pack = RenderPack.Load(RenderMap.SharedRootOf(mapFolder));
        MaterialsDoc materials = materialsRead.GetAwaiter().GetResult();
        LightingDoc lighting = lightingRead.GetAwaiter().GetResult();
        // Shaders compile portal fog only for a map that can draw it; decided before any of its materials is built.
        ResponsiveSmokeShaders.UsePortalFog(Post.PortalFogFrame.MapUses(lighting));
        List<OpenScctGame.Render.Projectors.ProjectorEntry>? projectors = projectorsRead.GetAwaiter().GetResult();
        // Only projectors with bProjectStaticMesh off read the solid surfaces' stencil marks.
        bool stencilMarks = projectors?.Exists(p => !p.ProjectStaticMesh) == true;
        var compiler = new MaterialCompiler(materials, textures)
        {
            LightmapScale = options.LightmapScale, Pack = pack, StencilMarks = stencilMarks,
        };
        MaterialCompiler.SetEmfEnvironment(pack?.Texture(RenderPack.EmfEnvironment));
        LoadTimer.Mark("build.docs");
        CollisionWorld? zones = null;
        string collisionPath = Path.Combine(mapFolder, CollisionFile);
        if (options.LoadZones && File.Exists(collisionPath))
        {
            // Shares the static collision the play session's worlds built from the same file (or builds it for them).
            zones = CollisionWorld.Load(collisionPath);
        }

        LoadTimer.Mark("build.zones");
        using GlbDocument glb = glbRead.GetAwaiter().GetResult();
        LoadTimer.Mark("build.glb");
        string mapId = Path.GetFileName(Path.GetFullPath(mapFolder));
        var root = new Node3D { Name = "RenderMap" };
        var map = new RenderMap(mapFolder, mapId, root, materials, lighting, textures, compiler, new ActorLighting(lighting), zones);
        map.Pack = pack;
        map.Actors = actorsRead.GetAwaiter().GetResult();
        map.Outfits = RenderMap.OutfitsOf(mapFolder);
        Characters.PawnCosmetics.Outfits = map.Outfits;
        map.SkipProjectorDecals = projectors is not null;
        map.SoftBodies = new OpenScctGame.Render.SoftBodies.SoftBodyViews(root);
        LoadTimer.Mark("build.actors");
        // SVM_RD_PROTO: copy the merged cell geometry out for the RenderingDevice prototype (RdProto/RdProtoRenderer).
        map.RdGeometry = RdProto.RdProtoGeometry.Enabled ? new RdProto.RdProtoGeometry() : null;
        Rd.RdWorldRenderer.LatchOwnFrame();
        map.RdWorld = Rd.RdWorldGeometry.Enabled ? new Rd.RdWorldGeometry(map.Compiler) : null;
        new Builder(map, glb, options).Run();
        map.MeshSpheres.Flush();
        map.RdGeometry?.Finish();
        map.RdWorld?.Finish();
        OutdoorScenery.Build(map);
        OutdoorGroundcover.Build(map);
        LoadTimer.Mark("build.outdoor");
        if (options.GameplayObjects)
        {
            OpenScctGame.Render.GameplayObjects.Build(map, map.Actors, pack);
        }

        OpenScctGame.Render.GameplayObjects.ApplyVision(map, map.Actors);
        SpotShadowLight.SetIgnoreGroups(map.EchelonGroups);
        LoadTimer.Mark("build.objects");
        if (options.Emitters)
        {
            map.Emitters = new Particles.Emitters(map, root);
            map.Stats.Emitters = map.Emitters.Count;
        }

        LoadTimer.Mark("build.emitters");
        if (projectors is not null)
        {
            map.Projectors = OpenScctGame.Render.Projectors.MapProjectors.Build(map, projectors, root);
        }

        LoadTimer.Mark("build.projectors");
        map.LightBeams = OpenScctGame.Render.LightBeams.MapLightBeams.Build(map, root);
        LoadTimer.Mark("build.beams");
        map.LightSprites = new MapLightSprites(map);
        foreach (string missing in map.LightSprites.Missing) map.Warnings.Add("light sprite texture missing: " + missing);
        if (pack is null)
        {
            map.Warnings.Add("no render pack (.content/shared/render): vision textures and gameplay object meshes are missing");
        }
        map.SetTime(0f);
        LoadTimer.Mark("build.sprites");
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
        private readonly SortedDictionary<(int X, int Y, int Sky), Dictionary<(string, LightSource, VisionPass), SurfaceBuilder>> _cells = new();

        /// <summary>How the merged surfaces of a cell bucket draw in thermal and EMF vision.</summary>
        private enum VisionPass : byte
        {
            /// <summary>Every actor fully visible: no vision cut-out can fire (<see cref="MaterialCompiler.GetPlain"/>).</summary>
            Plain,

            /// <summary>Some actor hidden in a vision (HeatOpacity or EMFOpacity 0): the solid pass cuts it out.</summary>
            Cut,

            /// <summary>Partly see-through actors (<see cref="MaterialCompiler.GetSeeThrough"/>).</summary>
            SeeThrough,
        }
        private readonly Dictionary<(string Mesh, LightSource Light, int Glow), ArrayMesh> _shared = [];
        private readonly Node3D _static = new() { Name = "Static" };
        private readonly Node3D _gameplay = new() { Name = "Gameplay" };

        /// <summary>The glTF meshes by index (indexing the JSON array walks it from the start each time).</summary>
        private readonly List<JsonElement> _meshes = [];
        private HashSet<string>? _conveyorItems;

        public Builder(RenderMap map, GlbDocument glb, RenderOptions options)
        {
            _map = map;
            _glb = glb;
            _options = options;
            if (glb.Root.TryGetProperty("meshes", out JsonElement meshes))
            {
                foreach (JsonElement m in meshes.EnumerateArray())
                {
                    _meshes.Add(m);
                }
            }
        }

        public void Run()
        {
            _map.Root.AddChild(_static);
            _map.Root.AddChild(_gameplay);
            ReadMaterials();
            ReadNodes(out List<(int Mesh, bool Sky)> bspMeshes);
            LoadTimer.Mark("build.nodes");

            // The meshes this build reads are decoded on worker threads while the textures are prefetched.
            List<(int Mesh, PrimitiveSource[] Sources)> wanted = MeshesToRead(bspMeshes);
            var decoded = new MeshPrimitive[]?[wanted.Count];
            Task decoding = Task.Run(() => ParallelFor(wanted.Count, i => decoded[i] = TryDecode(wanted[i].Sources)));
            Prefetch();
            decoding.GetAwaiter().GetResult();
            for (int i = 0; i < wanted.Count; i++)
            {
                // A mesh that failed to decode is read again where it is first used, and fails there as before.
                if (decoded[i] is { } prims)
                {
                    _primitives[wanted[i].Mesh] = prims;
                }
            }

            LoadTimer.Mark("build.prefetch");
            foreach ((int mesh, bool sky) in bspMeshes)
            {
                AddBsp(mesh, sky);
            }

            LoadTimer.Mark("build.bsp");

            if (Instances() is { } instances)
            {
                foreach (JsonElement inst in instances.EnumerateArray())
                {
                    AddInstance(inst);
                }
            }

            LoadTimer.Mark("build.instances");
            AddSwapMeshes();
            LoadTimer.Mark("build.swap-meshes");
            FlushCells();
            LoadTimer.Mark("build.cells");
        }

        /// <summary>
        /// The library meshes no placed actor uses: the meshes gameplay objects swap to (TriggeredMesh,
        /// DamagedMeshes[].StaticMesh), which the converter adds to the library; and the conveyors' item meshes, which
        /// a placed actor may use too (where its baked light does not suit an item moving through the map). They are
        /// built once, lit by the actor, for <see cref="RenderMap.SwapMeshes"/>.
        /// </summary>
        private void AddSwapMeshes()
        {
            // Materials are fetched here in the same order as one mesh at a time; the geometry, plain data, is then
            // written on worker threads and the meshes are made here, in the same order.
            var meshes = new List<(string Name, int Mesh, bool Used, bool Particle, bool Item, int First, int Count)>();
            var surfaces = new List<(MeshPrimitive Prim, Material Material, SurfaceBuilder Surface)>();
            foreach ((string name, int mesh) in _libraryMeshes)
            {
                if (!IsSwapMesh(name, _usedLibraryMeshes, out bool used, out bool particle, out bool item))
                {
                    continue;
                }

                int first = surfaces.Count;
                foreach (MeshPrimitive prim in Primitives(mesh))
                {
                    if (_map.Compiler.Get(prim.Material, LightSource.Actor) is not { } material)
                    {
                        continue;
                    }

                    var surface = new SurfaceBuilder(false, false);
                    surface.Queue(Transform3D.Identity, prim, 0, null);
                    surfaces.Add((prim, material, surface));
                }

                meshes.Add((name, mesh, used, particle, item, first, surfaces.Count - first));
            }

            ParallelFor(surfaces.Count, i => surfaces[i].Surface.Prepare());
            foreach ((string name, int mesh, bool used, bool particle, bool item, int first, int count) in meshes)
            {
                var arrayMesh = new ArrayMesh { ResourceName = name };
                for (int i = first; i < first + count; i++)
                {
                    (MeshPrimitive prim, Material material, SurfaceBuilder surface) = surfaces[i];
                    surface.AddTo(arrayMesh, material);
                    arrayMesh.SurfaceSetName(arrayMesh.GetSurfaceCount() - 1, "section" + prim.Section);
                }

                ActorLighting.ReadSphere(arrayMesh, _meshes[mesh]);
                if (arrayMesh.GetSurfaceCount() > 0)
                {
                    if (!used || item)
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

        /// <summary>
        /// Whether a library mesh is built for <see cref="RenderMap.SwapMeshes"/> or <see cref="RenderMap.ParticleMeshes"/>:
        /// unused by placed actors, or a particle or conveyor item mesh; never a per-actor mesh (# in its name).
        /// </summary>
        private bool IsSwapMesh(string name, HashSet<string> usedMeshes, out bool used, out bool particle, out bool item)
        {
            _conveyorItems ??= ConveyorItemMeshes();
            used = usedMeshes.Contains(name);
            particle = _particleMeshes.Contains(name);
            item = _conveyorItems.Contains(name);
            return !((used && !particle && !item) || name.Contains('#', StringComparison.Ordinal));
        }

        /// <summary>The placed instances (extras.svmap.instances), or null.</summary>
        private JsonElement? Instances() =>
            _glb.Root.TryGetProperty("extras", out JsonElement extras) && extras.TryGetProperty("svmap", out JsonElement svmap)
                && svmap.TryGetProperty("instances", out JsonElement instances)
                ? instances
                : null;

        /// <summary>
        /// The meshes the build will read, with their accessors: the BSP, the meshes of the placed instances (except
        /// projector decals left out) and the swap meshes, the same set <see cref="AddInstance"/> and
        /// <see cref="AddSwapMeshes"/> read. A mesh whose JSON cannot be read is left out, to fail where it is used.
        /// </summary>
        private List<(int Mesh, PrimitiveSource[] Sources)> MeshesToRead(List<(int Mesh, bool Sky)> bspMeshes)
        {
            var meshes = new List<int>();
            foreach ((int mesh, _) in bspMeshes)
            {
                meshes.Add(mesh);
            }

            var used = new HashSet<string>(StringComparer.Ordinal);
            if (Instances() is { } instances)
            {
                foreach (JsonElement inst in instances.EnumerateArray())
                {
                    string meshName = Str(inst, "mesh") ?? "";
                    if (!_libraryMeshes.TryGetValue(meshName, out int mesh))
                    {
                        continue;
                    }

                    used.Add(meshName);
                    if (!(_map.SkipProjectorDecals && inst.TryGetProperty("projectorOf", out _)))
                    {
                        meshes.Add(mesh);
                    }
                }
            }

            foreach ((string name, int mesh) in _libraryMeshes)
            {
                if (IsSwapMesh(name, used, out _, out _, out _))
                {
                    meshes.Add(mesh);
                }
            }

            var result = new List<(int, PrimitiveSource[])>();
            var seen = new HashSet<int>();
            foreach (int mesh in meshes)
            {
                if (!seen.Add(mesh) || _primitives.ContainsKey(mesh))
                {
                    continue;
                }

                try
                {
                    result.Add((mesh, SourcesOf(mesh)));
                }
                catch (Exception e) when (IsBadData(e))
                {
                    // Read again (and reported) where the mesh is first used.
                }
            }

            return result;
        }

        private static bool IsBadData(Exception e) =>
            e is KeyNotFoundException or InvalidOperationException or InvalidDataException or IndexOutOfRangeException
                or ArgumentException or FormatException or OverflowException;

        /// <summary>The asset paths of the meshes the map's conveyors carry as items (Svm.Conveyor ItemMeshes).</summary>
        private HashSet<string> ConveyorItemMeshes()
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (GameplayActor actor in _map.Actors.Actors)
            {
                if (!string.Equals(actor.Class, "Svm.Conveyor", StringComparison.OrdinalIgnoreCase) ||
                    !actor.TryGet("ItemMeshes", out JsonElement meshes) || meshes.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (JsonElement m in meshes.EnumerateArray())
                {
                    if (m.ValueKind == JsonValueKind.Object && m.TryGetProperty("$asset", out JsonElement path) &&
                        path.GetString() is { Length: > 0 } asset)
                    {
                        names.Add(asset);
                    }
                }
            }

            return names;
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

            MeshPrimitive[] result = Array.ConvertAll(SourcesOf(mesh), Decode);
            _primitives[mesh] = result;
            return result;
        }

        /// <summary>The accessors of one glTF primitive (-1: absent), its section and material name.</summary>
        private sealed record PrimitiveSource(int Section, string Material, int Positions, int Indices, int Normals, int Uvs,
            int Uv2s, int Colours, int BakedDirections);

        /// <summary>The primitives of a mesh as read from the JSON (main thread only: the JSON is not read by workers).</summary>
        private PrimitiveSource[] SourcesOf(int mesh)
        {
            JsonElement prims = _meshes[mesh].GetProperty("primitives");
            var list = new List<PrimitiveSource>();
            foreach (JsonElement p in prims.EnumerateArray())
            {
                JsonElement attrs = p.GetProperty("attributes");
                int material = p.TryGetProperty("material", out JsonElement mi) ? mi.GetInt32() : -1;
                list.Add(new PrimitiveSource(
                    p.TryGetProperty("extras", out JsonElement pe) && pe.TryGetProperty("scctSection", out JsonElement ps) ? ps.GetInt32() : list.Count,
                    material >= 0 && material < _materialNames.Count ? _materialNames[material] : "",
                    attrs.GetProperty("POSITION").GetInt32(),
                    p.TryGetProperty("indices", out JsonElement ii) ? ii.GetInt32() : -1,
                    AccessorOf(attrs, "NORMAL"),
                    AccessorOf(attrs, "TEXCOORD_0"),
                    AccessorOf(attrs, "TEXCOORD_1"),
                    AccessorOf(attrs, "COLOR_0"),
                    AccessorOf(attrs, "COLOR_1")));
            }

            return [.. list];
        }

        private static int AccessorOf(JsonElement attrs, string name) =>
            attrs.TryGetProperty(name, out JsonElement a) ? a.GetInt32() : -1;

        /// <summary>Reads one primitive's arrays from the binary chunk: plain data, safe on worker threads.</summary>
        private MeshPrimitive Decode(PrimitiveSource s)
        {
            float[] positions = _glb.ReadFloats(s.Positions);
            return new MeshPrimitive
            {
                Section = s.Section,
                Material = s.Material,
                Positions = positions,
                Normals = s.Normals >= 0 ? _glb.ReadFloats(s.Normals) : null,
                Uvs = s.Uvs >= 0 ? _glb.ReadFloats(s.Uvs) : null,
                Uv2s = s.Uv2s >= 0 ? _glb.ReadFloats(s.Uv2s) : null,
                Colours = s.Colours >= 0 ? _glb.ReadColours(s.Colours) : null,
                BakedDirections = s.BakedDirections >= 0 ? _glb.ReadFloats(s.BakedDirections) : null,
                Indices = s.Indices >= 0 ? _glb.ReadIndices(s.Indices) : Sequence(positions.Length / 3),
            };
        }

        /// <summary>The primitives of a mesh, or null when its data is bad (the main thread reads it again and fails).</summary>
        private MeshPrimitive[]? TryDecode(PrimitiveSource[] sources)
        {
            try
            {
                return Array.ConvertAll(sources, Decode);
            }
            catch (Exception e) when (IsBadData(e))
            {
                return null;
            }
        }

        /// <summary>Runs <paramref name="body"/> for 0 to <paramref name="count"/> - 1 on worker threads; a failure is rethrown as itself.</summary>
        private static void ParallelFor(int count, Action<int> body)
        {
            try
            {
                Parallel.For(0, count, body);
            }
            catch (AggregateException e) when (e.InnerExceptions.Count > 0)
            {
                ExceptionDispatchInfo.Capture(e.InnerExceptions[0]).Throw();
                throw;
            }
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
                if (sky)
                {
                    _map.Stats.SkyTriangles += prim.Indices.Length / 3;
                }
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
                (int, int, int) cell = CellOf(center, sky);
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
                    BakedDirections = prim.BakedDirections,
                    Indices = [.. tris],
                };
                Bucket(cell, prim.Material, light, light == LightSource.Lightmap, true)
                    .Queue(Transform3D.Identity, part, 0, [.. byCellZones[cell]]);
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
        /// Zone byte of an additive mesh with painted vertex colours: the world template lights it by its colours only
        /// (svm_world.gdshaderinc SVM_OVERLAY_ZONE). UE2 maps have far fewer zones, so the value is free.
        /// </summary>
        [Provenance(ProvenanceKind.Estimated, "UE2 static meshes drawn with their own vertex colours take no lighting; AquaD caustics fade by them in the original")]
        internal const byte AuthoredOverlayZone = 255;

        /// <summary>
        /// Mesh actors take zone-restricted lighting from their point region. An origin in solid is zone 0, not an
        /// unknown zone to replace with the room in front of a face (AquaE's temple Techroom sign).
        /// </summary>
        private byte[] ZonesOf(MeshPrimitive prim, FVector actorLocation)
        {
            var zones = new byte[prim.Indices.Length / 3];
            if (_map.Zones is null)
            {
                return zones;
            }

            byte actorZone = (byte)Math.Clamp(_map.Zones.GetZoneIndex(actorLocation), 0, 255);
            Array.Fill(zones, actorZone);

            return zones;
        }

        private static Vector3 Pos(MeshPrimitive p, int v) => new(p.Positions[v * 3], p.Positions[(v * 3) + 1], p.Positions[(v * 3) + 2]);

        private (int, int, int) CellOf(Vector3 godot, bool sky)
        {
            float size = UeSpace.LengthToGodot(_options.CellSize);
            return ((int)MathF.Floor(godot.X / size), (int)MathF.Floor(godot.Z / size), sky ? 1 : 0);
        }

        private SurfaceBuilder Bucket((int, int, int) cell, string material, LightSource light, bool uv2, bool colour,
            VisionPass pass = VisionPass.Plain)
        {
            if (!_cells.TryGetValue(cell, out Dictionary<(string, LightSource, VisionPass), SurfaceBuilder>? buckets))
            {
                buckets = [];
                _cells[cell] = buckets;
            }

            if (!buckets.TryGetValue((material, light, pass), out SurfaceBuilder? b))
            {
                // Vertex-lit surfaces have no lightmap: their UV2 carries each instance's light sphere instead.
                b = new SurfaceBuilder(uv2, colour, hasVision: true, hasSphere: light == LightSource.Vertex)
                {
                    OrderByFacing = _map.Materials.Find(material)?.Blend == MaterialBlend.Translucent,
                };
                buckets[(material, light, pass)] = b;
            }

            // BSP and instances without values of their own draw at the Actor defaults.
            b.Vision = SurfaceBuilder.DefaultVision;
            b.SetSphere(0);
            return b;
        }

        private readonly Dictionary<int, Vector4> _localSpheres = [];

        /// <summary>
        /// A library mesh's bounding sphere in its own space (metres): the cooked one of its export, else the sphere
        /// around its box (as <see cref="ActorLighting.SphereOf"/>).
        /// </summary>
        private Vector4 LocalSphere(int mesh, MeshPrimitive[] prims)
        {
            if (!_localSpheres.TryGetValue(mesh, out Vector4 sphere))
            {
                sphere = ActorLighting.CookedSphere(_meshes[mesh]) ?? ActorLighting.BoxSphere(Bounds(prims, Transform3D.Identity));
                _localSpheres[mesh] = sphere;
            }

            return sphere;
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
            int echelon = EchelonOf(inst);
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

            // Soft bodies are lit as actors (SoftBodyViews.Relight), bUnlit ones fullbright.
            if (!hidden && inst.TryGetProperty("softBody", out JsonElement softBody)
                && _map.SoftBodies?.Add(actor, softBody, transform, location, prims, _map.Compiler, VisionOf(inst),
                    unlit ? LightSource.None : LightSource.Actor, glow, echelon) == true)
            {
                _map.SoftBodies.Relight(_map);
                return;
            }

            if (gameplay || light == LightSource.Actor)
            {
                AddGameplay(actor, cls, meshName, mesh, transform, location, light, glow, hidden, VisionOf(inst), echelon);
                return;
            }

            Aabb box = Bounds(prims, transform);
            bool skyZone = Bool(inst, "skyZone");
            (int, int, int) cell = CellOf(box.GetCenter(), skyZone);
            Vector4 vision = VisionOf(inst);
            VisionPass pass = PassOf(vision);
            bool authored = Bool(inst, "authoredColors");
            // The sphere the instance's dynamic lights are chosen and fitted over, in the world (the merged geometry's
            // space). Sky zone backdrops keep the light at the pixel alone: their spheres span the whole sky box.
            int sphereIndex = 0;
            Vector3 sphereCentre = default;
            float sphereRadius = 0f;
            if (light == LightSource.Vertex && !skyZone)
            {
                Vector4 local = LocalSphere(mesh, prims);
                sphereCentre = transform * new Vector3(local.X, local.Y, local.Z);
                sphereRadius = local.W * MathF.Max(transform.Basis.X.Length(), MathF.Max(transform.Basis.Y.Length(), transform.Basis.Z.Length()));
                sphereIndex = _map.MeshSpheres.Add(sphereCentre, sphereRadius);
            }
            foreach (MeshPrimitive prim in prims)
            {
                // bUnlit skips lighting, but an additive overlay still needs its painted intensity and faded rim.
                bool overlay = authored && prim.Colours is not null &&
                    _map.Materials.Find(prim.Material)?.Blend == MaterialBlend.Additive;
                LightSource surfaceLight = overlay ? LightSource.Vertex : light;
                SurfaceBuilder bucket = Bucket(cell, prim.Material, surfaceLight, false, surfaceLight == LightSource.Vertex, pass);
                bucket.Vision = vision;
                bucket.Echelon = echelon;
                bucket.SetSphere(overlay ? 0 : sphereIndex, sphereCentre, sphereRadius);
                byte[]? zones = surfaceLight == LightSource.Vertex ? ZonesOf(prim, location) : null;
                if (overlay)
                {
                    // An additive overlay with painted colours (AquaD's caustic quads: 40 inside, 0 on the rim) is drawn by
                    // its colours alone: no glow, no dynamic or spot light, which would fill the faded rim to a square.
                    Array.Fill(zones!, AuthoredOverlayZone);
                    bucket.Queue(transform, prim, 0, zones);
                }
                else if (surfaceLight == LightSource.Vertex && glow > 0)
                {
                    AddGlowing(bucket, transform, prim, glow, zones);
                }
                else
                {
                    bucket.Queue(transform, prim, 0, zones);
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
                BakedDirections = prim.BakedDirections,
                Indices = prim.Indices,
            };
            bucket.Queue(transform, lit, 0, zones);
        }

        private static byte ToByte(float v) => (byte)Math.Clamp((int)MathF.Round(v * 255f), 0, 255);

        /// <summary>
        /// An instance's Echelon code (<see cref="RenderMap.EchelonCode"/>) from its extras "echelonIgnore" (the
        /// flags) and "ignoredEchelonLights" (light actor names); 0 for maps converted before they were exported.
        /// </summary>
        private int EchelonOf(JsonElement inst)
        {
            int flags = inst.TryGetProperty("echelonIgnore", out JsonElement f) && f.ValueKind == JsonValueKind.Number ? f.GetInt32() : 0;
            List<string>? lights = null;
            if (inst.TryGetProperty("ignoredEchelonLights", out JsonElement l) && l.ValueKind == JsonValueKind.Array)
            {
                lights = [];
                foreach (JsonElement n in l.EnumerateArray())
                {
                    if (n.ValueKind == JsonValueKind.String && n.GetString() is { Length: > 0 } name)
                    {
                        lights.Add(name);
                    }
                }
            }

            return _map.EchelonCode(flags, lights);
        }

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

        /// <summary>
        /// Whether an actor is partly see-through in thermal or EMF vision (HeatOpacity or EMFOpacity between 0 and 1):
        /// its surfaces take the blended vision pass (<see cref="MaterialCompiler.GetSeeThrough"/>).
        /// </summary>
        private static bool SeeThrough(Vector4 vision) =>
            vision.Y is > 0f and < 1f || vision.W is > 0f and < RenderK.EmfOpaqueFrom;

        /// <summary>
        /// The vision pass of an actor's merged surfaces. Outside the see-through range, HeatOpacity and EMFOpacity above 0
        /// mean 1 and at least <see cref="RenderK.EmfOpaqueFrom"/>: the template's cut-outs cannot fire on them.
        /// </summary>
        private static VisionPass PassOf(Vector4 vision) =>
            SeeThrough(vision) ? VisionPass.SeeThrough : vision.Y > 0f && vision.W > 0f ? VisionPass.Plain : VisionPass.Cut;

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
            LightSource light, int glow, bool hidden, Vector4 vision, int echelon)
        {
            int glowKey = light == LightSource.Vertex ? glow : 0;
            // Vertex-lit gameplay meshes carry their zones per vertex, so they are not shared between actors.
            bool seeThrough = SeeThrough(vision);
            string shareKey = (light == LightSource.Vertex ? meshName + "@" + actor : meshName) + (seeThrough ? "|seethrough" : "");
            if (!_shared.TryGetValue((shareKey, light, glowKey), out ArrayMesh? arrayMesh))
            {
                arrayMesh = new ArrayMesh { ResourceName = meshName };
                // A vertex-lit gameplay mesh (a mover) carries its sphere in its own space: the template moves it with
                // the node.
                int sphereIndex = 0;
                Vector4 localSphere = default;
                if (light == LightSource.Vertex)
                {
                    localSphere = LocalSphere(mesh, Primitives(mesh));
                    sphereIndex = _map.MeshSpheres.Add(new Vector3(localSphere.X, localSphere.Y, localSphere.Z), localSphere.W);
                }

                foreach (MeshPrimitive prim in Primitives(mesh))
                {
                    Material? material = seeThrough ? _map.Compiler.GetSeeThrough(prim.Material, light) : _map.Compiler.Get(prim.Material, light);
                    if (material is null)
                    {
                        continue;
                    }

                    var surface = new SurfaceBuilder(false, light == LightSource.Vertex, hasSphere: light == LightSource.Vertex)
                    {
                        Echelon = light == LightSource.Vertex ? echelon : 0,
                    };
                    surface.SetSphere(sphereIndex, new Vector3(localSphere.X, localSphere.Y, localSphere.Z), localSphere.W);
                    byte[]? zones = light == LightSource.Vertex ? ZonesOf(prim, location) : null;
                    // Measured on AquaE's pump volume: preserve its saved intensity
                    // instead of adding an ambient floor. Restrict this inference to
                    // baked additive effects opting out of both Echelon light kinds.
                    // The high bits encode per-light ignore groups, not these flags.
                    bool bakedEffect = light == LightSource.Vertex && (echelon & 3) == 3 &&
                        _map.Materials.Find(prim.Material)?.Blend == MaterialBlend.Additive;
                    if (light == LightSource.Vertex && glow > 0 && !bakedEffect)
                    {
                        AddGlowing(surface, Transform3D.Identity, prim, glow, zones);
                    }
                    else
                    {
                        surface.Add(Transform3D.Identity, prim, 0, zones);
                    }

                    surface.AddTo(arrayMesh, material);
                    arrayMesh.SurfaceSetName(arrayMesh.GetSurfaceCount() - 1, "section" + prim.Section);
                    _map.Stats.Vertices += surface.VertexCount;
                }

                ActorLighting.ReadSphere(arrayMesh, _meshes[mesh]);
                _shared[(shareKey, light, glowKey)] = arrayMesh;
                // Never rewritten after this, so its instances are rigid shadow casters, unless a cut-out of it animates.
                bool animatedCutOut = false;
                for (int s = 0; s < arrayMesh.GetSurfaceCount(); s++) animatedCutOut |= arrayMesh.SurfaceGetMaterial(s) is { } m && AnimatesShadow(m);
                if (!animatedCutOut) SpotShadowLight.MarkRigid(arrayMesh);
            }

            var node = new MeshInstance3D { Name = actor.Length > 0 ? actor : meshName, Mesh = arrayMesh, Transform = transform, Visible = !hidden };
            _gameplay.AddChild(node);
            OpenScctGame.Render.InstanceParams.Set(node, OpenScctGame.Render.GameplayObjects.VisionActorParameter, vision);
            if (light == LightSource.Actor)
            {
                ActorLighting.SetEchelonIgnore(node, echelon);
            }

            _map.Stats.GameplayNodes++;
            _map.Stats.GameplaySurfaces += arrayMesh.GetSurfaceCount();
            foreach (MeshPrimitive prim in Primitives(mesh))
            {
                _map.Stats.Triangles += prim.Indices.Length / 3;
            }

            var lightSphere = ActorLighting.SphereOf(arrayMesh, transform);
            float radius = lightSphere.Radius;
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
                ActorLighting.Apply(node, _map.LightActor(lightSphere.Center, radius, glow, location));
            }
        }

        /// <summary>Godot's limit on surfaces in one mesh (RenderingServer MAX_MESH_SURFACES).</summary>
        private const int MaxSurfacesPerMesh = 256;

        private void FlushCells()
        {
            // The materials first, in the order the cells ask for them. The merged geometry of every drawn bucket is
            // then written on worker threads (plain data, each bucket in its own queued order), and the meshes are made
            // here in cell order.
            var cells = new List<((int x, int y, int sky) Cell, List<((string, LightSource, VisionPass) Key, Material Material, LightReach.Twins Twins, SurfaceBuilder Surface)> Drawn)>();
            var merged = new List<SurfaceBuilder>();
            foreach (((int x, int y, int sky) cell, Dictionary<(string, LightSource, VisionPass), SurfaceBuilder> buckets) in _cells)
            {
                var keys = new List<(string, LightSource, VisionPass)>(buckets.Keys);
                keys.Sort((a, b) => string.CompareOrdinal(a.Item1, b.Item1) is var n and not 0 ? n
                    : a.Item2 != b.Item2 ? a.Item2.CompareTo(b.Item2) : a.Item3.CompareTo(b.Item3));
                var drawn = new List<((string, LightSource, VisionPass) Key, Material Material, LightReach.Twins Twins, SurfaceBuilder Surface)>(keys.Count);
                foreach ((string, LightSource, VisionPass) key in keys)
                {
                    // The sky zone draws as its own camera sees it, and never in thermal or EMF (GetSky).
                    Material? material = cell.sky != 0 ? _map.Compiler.GetSky(key.Item1, key.Item2) : key.Item3 switch
                    {
                        VisionPass.SeeThrough => _map.Compiler.GetSeeThrough(key.Item1, key.Item2),
                        VisionPass.Plain => _map.Compiler.GetPlain(key.Item1, key.Item2),
                        _ => _map.Compiler.Get(key.Item1, key.Item2),
                    };
                    if (material is not null)
                    {
                        // The sky zone (its own material variant, never drawn in thermal or EMF) keeps its materials.
                        LightReach.Twins twins = cell.sky != 0 ? default : TwinsOf(key);
                        drawn.Add((key, material, twins, buckets[key]));
                        merged.Add(buckets[key]);
                    }
                }

                cells.Add((cell, drawn));
            }

            LoadTimer.Mark("build.cell-materials");
            ParallelFor(merged.Count, i => merged[i].Prepare());
            LoadTimer.Mark("build.cell-geometry");
            foreach (((int x, int y, int sky) cell, List<((string, LightSource, VisionPass) Key, Material Material, LightReach.Twins Twins, SurfaceBuilder Surface)> drawn) in cells)
            {
                string baseName = string.Create(CultureInfo.InvariantCulture, $"{(cell.sky != 0 ? "Sky" : "Cell")}_{cell.x}_{cell.y}");

                // A cell with more materials than one mesh can hold (MoonD's cells pass 256) is split over several
                // meshes, so no surface is dropped.
                int part = 0;
                // SVM_RD_PROTO=frame draws only what it can hide from the main view by layer (StaticWorld, Sky): a
                // cell with an animated shadow material goes on the World layer, so it stays with Godot, whole.
                bool keptByGodot = RdProto.RdProtoGeometry.InFrame && cell.sky == 0 && drawn.Exists(d => AnimatesShadow(d.Material));
                var surfaces = new List<(SurfaceBuilder Surface, Material Material, LightReach.Twins Twins, bool Split)>(MaxSurfacesPerMesh);
                // The buckets the world pass draws get meshes of their own, on its layers (RdWorldGeometry).
                var rdSurfaces = new List<(SurfaceBuilder Surface, Material Material, LightReach.Twins Twins, bool Split)>();
                uint rdLayer = cell.sky != 0 ? RenderLayers.SkyRd : RenderLayers.StaticWorldRd;
                int rdPart = 0;
                foreach (((string, LightSource, VisionPass) key, Material material, LightReach.Twins twins, SurfaceBuilder b) in drawn)
                {
                    if (b.TriangleCount == 0)
                    {
                        continue;
                    }

                    if (surfaces.Count == MaxSurfacesPerMesh)
                    {
                        FlushCellPart(surfaces, CellPartName(baseName, part++), cell.sky != 0, keptByGodot);
                        surfaces.Clear();
                    }

                    // Transparent and vision see-through passes keep the original cell sort bounds.
                    // A MaterialSequence can change its blend mode by replacing the shader at runtime.
                    MaterialEntry? entry = _map.Materials.Find(key.Item1);
                    bool solid = entry is { Blend: MaterialBlend.Opaque or MaterialBlend.Masked } &&
                        !(entry.Sequences.TryGetValue("diffuse", out var sequence) && sequence.SwitchMaterial);
                    // Sky cells split the other way: each transparent surface gets a node of its own (FlushCellPart).
                    bool split = cell.sky == 0 ? key.Item3 != VisionPass.SeeThrough && material.NextPass is null && solid : !solid;
                    _map.Stats.Vertices += b.VertexCount;
                    // The own frame draws cut-outs that animate too (its colour pass does not care how their shadow is cached).
                    if (_map.RdWorld is { } rdWorld && (Rd.RdWorldRenderer.OwnFrame || !AnimatesShadow(material)) && rdWorld.TryAdd(cell, key.Item1, key.Item2, entry, b, key.Item3 == VisionPass.SeeThrough, _map.MeshSpheres))
                    {
                        if (rdSurfaces.Count == MaxSurfacesPerMesh)
                        {
                            FlushCellPart(rdSurfaces, CellPartName(baseName + "_Rd", rdPart++), cell.sky != 0, rdLayer: rdLayer);
                            rdSurfaces.Clear();
                        }

                        rdSurfaces.Add((b, material, twins, split));
                        continue;
                    }

                    surfaces.Add((b, material, twins, split));
                    if (!keptByGodot)
                    {
                        _map.RdGeometry?.Add(cell, key.Item2, material, entry, b, _map.Textures);
                    }
                }

                FlushCellPart(surfaces, CellPartName(baseName, part), cell.sky != 0, keptByGodot);
                FlushCellPart(rdSurfaces, CellPartName(baseName + "_Rd", rdPart), cell.sky != 0, rdLayer: rdLayer);
            }
        }

        /// <summary>
        /// The twins LightReach swaps in: where no spot (and no dynamic) light reaches, and outside thermal and EMF. Every
        /// vision pass draws the same normal vision twins; see-through passes keep their own material in thermal and EMF
        /// whatever reaches them (their twins would lose the blended pass). A twin a material lacks falls back to the
        /// nearest it has: no spot to full, neither to no spot.
        /// </summary>
        private LightReach.Twins TwinsOf((string Name, LightSource Light, VisionPass Pass) key)
        {
            MaterialCompiler compiler = _map.Compiler;
            bool plain = key.Pass == VisionPass.Plain;
            bool seeThrough = key.Pass == VisionPass.SeeThrough;
            Material? normal = compiler.GetNormalVision(key.Name, key.Light, plain);
            Material? normalNoSpot = compiler.GetNormalVision(key.Name, key.Light, plain, keepSpot: false) ?? normal;
            return new(
                seeThrough ? null : compiler.GetUnreached(key.Name, key.Light, plain, keepDynamic: true),
                seeThrough ? null : compiler.GetUnreached(key.Name, key.Light, plain, keepDynamic: false),
                normal,
                normalNoSpot,
                compiler.GetNormalVision(key.Name, key.Light, plain, keepSpot: false, keepDynamic: false) ?? normalNoSpot);
        }

        private static string CellPartName(string baseName, int part) =>
            part == 0 ? baseName : string.Create(CultureInfo.InvariantCulture, $"{baseName}_{part}");

        private static bool AnimatesShadow(Material material) =>
            material is ShaderMaterial { Shader: { } shader } && shader.HasMeta("svm_shadow_animated") && shader.GetMeta("svm_shadow_animated").AsBool();

        private void FlushCellPart(List<(SurfaceBuilder Surface, Material Material, LightReach.Twins Twins, bool Split)> surfaces, string name, bool sky, bool worldLayer = false, uint rdLayer = 0)
        {
            if (surfaces.Count == 0)
            {
                return;
            }

            bool animatedShadow = worldLayer;
            var grouped = new ArrayMesh { ResourceName = name };
            var groupedTwins = new List<LightReach.Twins>();
            var bounded = new List<(ArrayMesh Mesh, LightReach.Twins Twins, Aabb? Spheres)>();
            // The box around the light spheres of the grouped surfaces: dynamic lights reach the mesh through it too.
            Aabb? groupedSpheres = null;
            foreach ((SurfaceBuilder surface, Material material, LightReach.Twins twins, bool split) in surfaces)
            {
                animatedShadow |= !sky && AnimatesShadow(material);

                // Each material was already a draw call. A tight instance AABB lets Godot cull
                // it without splitting the material's merged triangles into additional calls.
                if (split && surfaces.Count > 1)
                {
                    var mesh = new ArrayMesh { ResourceName = string.Create(CultureInfo.InvariantCulture, $"{name}_Surface{bounded.Count}") };
                    surface.AddTo(mesh, material);
                    bounded.Add((mesh, twins, surface.SphereBounds));
                }
                else
                {
                    surface.AddTo(grouped, material);
                    groupedTwins.Add(twins);
                    if (surface.SphereBounds is { } spheres)
                    {
                        groupedSpheres = groupedSpheres is { } all ? all.Merge(spheres) : spheres;
                    }
                }
            }

            uint layer = rdLayer != 0 ? rdLayer : sky ? RenderLayers.Sky : animatedShadow ? RenderLayers.World : RenderLayers.StaticWorld;
            if (sky && bounded.Count != 0)
            {
                // The sky camera sorted a cell's transparent surfaces by the cell's box and, at that one distance, in
                // surface order (Godot's sort keeps the order of up to 16 transparent surfaces). Drawn by the main view
                // among many more, they keep both: the cell's box on each split mesh, the surface order as a node meta
                // (FaithfulView.UpdateSkySort).
                Aabb cellBounds = grouped.GetSurfaceCount() != 0 ? grouped.GetAabb() : bounded[0].Mesh.GetAabb();
                foreach ((ArrayMesh mesh, _, _) in bounded)
                {
                    cellBounds = cellBounds.Merge(mesh.GetAabb());
                }

                for (int i = 0; i < bounded.Count; i++)
                {
                    bounded[i].Mesh.CustomAabb = cellBounds;
                }
            }

            if (grouped.GetSurfaceCount() != 0)
            {
                if (bounded.Count != 0 && !sky)
                {
                    Aabb originalBounds = grouped.GetAabb();
                    foreach ((ArrayMesh mesh, _, _) in bounded)
                    {
                        originalBounds = originalBounds.Merge(mesh.GetAabb());
                    }
                    grouped.CustomAabb = originalBounds;
                }
                AddCellMesh(grouped, layer, groupedTwins, groupedSpheres);
            }
            else
            {
                grouped.Dispose();
            }

            for (int i = 0; i < bounded.Count; i++)
            {
                MeshInstance3D node = AddCellMesh(bounded[i].Mesh, layer, [bounded[i].Twins], bounded[i].Spheres);
                if (sky)
                {
                    node.SetMeta(FaithfulView.SkySortOrderMeta, i);
                }
            }
        }

        private MeshInstance3D AddCellMesh(ArrayMesh mesh, uint layer, IReadOnlyList<LightReach.Twins> twins, Aabb? spheres = null)
        {
            var node = new MeshInstance3D
            {
                Name = mesh.ResourceName,
                Mesh = mesh,
                Layers = layer,
            };
            _static.AddChild(node);
            _map.LightReach.Add(node, twins, spheres);
            _map.Stats.StaticNodes++;
            _map.Stats.StaticSurfaces += mesh.GetSurfaceCount();
            return node;
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
