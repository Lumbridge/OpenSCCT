using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Godot;
using OpenScct.Core;
using OpenScctGame.Render;
using OpenScctGame.Render.Data;
using OpenScctGame.Render.Geometry;
using OpenScctGame.Render.Gltf;
using OpenScctGame.Render.Materials;
using OpenScctGame.Render.Particles;

namespace OpenScctGame.Fx;

/// <summary>One effect class of shared/fx/effects.json (docs/EFFECTS.md).</summary>
public sealed class FxClass
{
    public required string Path { get; init; }

    /// <summary>emitter, projectile, muzzle, impact, projector, light, tracer, mine, gadget, beam, decal.</summary>
    public required string Kind { get; init; }

    /// <summary>The sub-emitters with their properties over their class defaults (emitters only).</summary>
    public required IReadOnlyList<ParticleParams> Emitters { get; init; }

    /// <summary>The sub-emitters' stored values and class defaults, in the order of <see cref="Emitters"/>.</summary>
    public IReadOnlyList<FxSubEmitter> Sources { get; init; } = [];

    /// <summary>The asset properties of the class (StaticMesh, ImpactMaterial, ProjTexture, Skins...).</summary>
    public required IReadOnlyDictionary<string, string> Assets { get; init; }

    /// <summary>The class defaults (classes.json), or default.</summary>
    public JsonElement Defaults { get; init; }

    public bool Bool(string name) => Defaults.ValueKind == JsonValueKind.Object && Defaults.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.True;

    public float Float(string name, float fallback = 0f) =>
        Defaults.ValueKind == JsonValueKind.Object && Defaults.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.Number ? v.GetSingle() : fallback;

    public FVector Vector(string name, FVector fallback) =>
        Defaults.ValueKind == JsonValueKind.Object && Defaults.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.Object
            ? new FVector(Num(v, "X", fallback.X), Num(v, "Y", fallback.Y), Num(v, "Z", fallback.Z))
            : fallback;

    private static float Num(JsonElement e, string name, float fallback) =>
        e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.Number ? v.GetSingle() : fallback;
}

/// <summary>
/// The converted gameplay effects (shared/fx/: effects.json, textures/, meshes/): every effect class with its
/// sub-emitters as <see cref="ParticleParams"/>, and the textures and static meshes they draw with (made on first use).
/// </summary>
public sealed class FxLibrary : IParticleAssets
{
    private readonly Dictionary<string, FxClass> _classes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (string Glb, string Node)> _meshTable = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _textureTable = new(StringComparer.OrdinalIgnoreCase);
    public sealed record SkinAnimation(string Texture, int Columns, int Rows, int Frames, float Fps);
    private readonly Dictionary<string, SkinAnimation> _skinAnimations = new(StringComparer.OrdinalIgnoreCase);
    public SkinAnimation? AnimationForSkin(string path) => _skinAnimations.GetValueOrDefault(path);

    private readonly HashSet<string> _translucent = new(StringComparer.OrdinalIgnoreCase);
    public bool TextureTranslucent(string path) => _translucent.Contains(path);
    private readonly HashSet<string> _additive = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Mesh?> _meshes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, MeshPackage?> _packages = new(StringComparer.OrdinalIgnoreCase);
    private readonly TextureCache _textures;
    private readonly string _folder;

    private FxLibrary(string folder)
    {
        _folder = folder;
        // Shared: the textures are made once per process and reused by every later play scene (released at exit).
        _textures = new TextureCache(folder) { Shared = true };
    }

    // The library BeginLoad is reading on a worker thread, by its fx folder; the next Load of that folder takes it.
    private static readonly System.Threading.Lock s_pendingLock = new();
    private static (string Folder, System.Threading.Tasks.Task<FxLibrary> Library)? s_pending;

    /// <summary>
    /// The effects of a shared content root (an empty library when the fx step has not run). Each play scene loads its
    /// own: the library holds Godot resources, which must go with the scene rather than outlive the engine. Takes the
    /// library <see cref="BeginLoad"/> started for the same root, if any.
    /// </summary>
    public static FxLibrary Load(string sharedRoot)
    {
        ArgumentException.ThrowIfNullOrEmpty(sharedRoot);
        string folder = FolderOf(sharedRoot);
        System.Threading.Tasks.Task<FxLibrary>? pending = null;
        lock (s_pendingLock)
        {
            if (s_pending is { } p)
            {
                s_pending = null;
                pending = string.Equals(p.Folder, folder, StringComparison.OrdinalIgnoreCase) ? p.Library : null;
            }
        }

        // A read that failed on the worker throws its own exception here, as the read on this thread would.
        return pending?.GetAwaiter().GetResult() ?? ReadLibrary(sharedRoot, folder);
    }

    /// <summary>
    /// Starts <see cref="Load"/> of <paramref name="sharedRoot"/> on a worker thread while the scene goes on loading:
    /// effects.json is read and the PNGs <see cref="Warm"/> uploads are decoded there. No Godot resource is made there;
    /// the textures are made from the decoded images on the main thread, as before.
    /// </summary>
    public static void BeginLoad(string sharedRoot)
    {
        ArgumentException.ThrowIfNullOrEmpty(sharedRoot);
        string folder = FolderOf(sharedRoot);
        var read = System.Threading.Tasks.Task.Run(() =>
        {
            FxLibrary lib = ReadLibrary(sharedRoot, folder);
            lib.PrefetchTextures();
            return lib;
        });
        lock (s_pendingLock)
        {
            s_pending = (folder, read);
        }
    }

    private static string FolderOf(string sharedRoot) => System.IO.Path.GetFullPath(System.IO.Path.Combine(sharedRoot, "fx"));

    // Plain C# data only (JSON, tables, particle parameters): safe on any thread.
    private static FxLibrary ReadLibrary(string sharedRoot, string folder)
    {
        var lib = new FxLibrary(folder);
        string path = System.IO.Path.Combine(folder, "effects.json");
        if (File.Exists(path))
        {
            lib.Read(path, SharedClasses.Load(sharedRoot));
            lib.AddLateClasses();
        }

        return lib;
    }

    /// <summary>
    /// Effect classes the fx step lists since a later converter version (class, kind and class-default mesh), for
    /// content converted before: SGasMask, the merc's visible mask, drawn from the SGameplayObjects package the step
    /// already exports whole.
    /// </summary>
    private static readonly (string Class, string Kind, string Mesh)[] s_lateClasses =
    [
        ("SBase.SGasMask", "gadget", "SGameplayObjects.GasMask.DEF_Gasmask"),
        ("SBase.SBackPack", "gadget", "SGameplayObjects.BackPacks.DEF_Backpack"),
        ("SBase.SBackPack_Att", "gadget", "SGameplayObjects.BackPacks.ATT_Backpack"),
    ];

    private void AddLateClasses()
    {
        foreach ((string cls, string kind, string mesh) in s_lateClasses)
        {
            if (!_classes.ContainsKey(cls))
            {
                _classes[cls] = new FxClass
                {
                    Path = cls,
                    Kind = kind,
                    Emitters = [],
                    Assets = new Dictionary<string, string>(StringComparer.Ordinal) { ["StaticMesh"] = mesh },
                };
            }
        }
    }

    public bool Available => _classes.Count > 0;

    public IReadOnlyDictionary<string, FxClass> Classes => _classes;

    /// <summary>An effect class by path (SBase.SSmokeEmitter) or by name (SSmokeEmitter).</summary>
    public FxClass? Find(string cls)
    {
        if (_classes.TryGetValue(cls, out FxClass? c))
        {
            return c;
        }

        return cls.Contains('.', StringComparison.Ordinal) ? null
            : _classes.GetValueOrDefault("SBase." + cls) ?? _classes.GetValueOrDefault("Engine." + cls);
    }

    /// <summary>A new particle system of an Emitter class, or null when the class has no sub-emitters.</summary>
    public ParticleSystemSim? CreateSystem(string cls, uint seed)
    {
        if (Find(cls) is not { Kind: "emitter", Emitters.Count: > 0 } c)
        {
            return null;
        }

        var settings = new ParticleSystemParams(c.Bool("AutoDestroy"), c.Bool("AutoReset"), default, default);
        return new ParticleSystemSim(c.Emitters, settings, seed);
    }

    /// <summary>
    /// A new particle system of an Emitter class whose script changed a sub-emitter's values before it ran:
    /// <paramref name="change"/> gives (sub-emitter index, property name) a new value, or null to keep the class's.
    /// </summary>
    public ParticleSystemSim? CreateSystem(string cls, uint seed, Func<int, string, JsonElement?> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        if (Find(cls) is not { Kind: "emitter", Emitters.Count: > 0 } c || c.Sources.Count != c.Emitters.Count)
        {
            return CreateSystem(cls, seed);
        }

        var emitters = new List<ParticleParams>(c.Sources.Count);
        for (int i = 0; i < c.Sources.Count; i++)
        {
            FxSubEmitter sub = c.Sources[i];
            int index = i;
            emitters.Add(ParticleParams.From(sub.Class, sub.ObjectPath,
                n => change(index, n) ?? (sub.TryGet(n, out JsonElement v) ? v : null), sub.Texture));
        }

        var settings = new ParticleSystemParams(c.Bool("AutoDestroy"), c.Bool("AutoReset"), default, default);
        return new ParticleSystemSim(emitters, settings, seed);
    }

    public Texture2D? Texture(string? png) => png is null ? null : _textures.Get(png);

    /// <summary>The PNG of a texture or material object path, or null.</summary>
    public string? TexturePng(string? objectPath) => objectPath is not null && _textureTable.TryGetValue(objectPath, out string? png) ? png : null;

    /// <summary>True when the material at <paramref name="objectPath"/> is drawn added to the frame (an additive Shader).</summary>
    public bool TextureAdditive(string? objectPath) => objectPath is not null && _additive.Contains(objectPath);

    public Mesh? Mesh(string? objectPath) => Mesh(objectPath, lit: false);

    /// <summary>A static mesh, unlit (effects) or actor-lit (grenades, cams, mines: the node then needs ActorLighting).</summary>
    public Mesh? Mesh(string? objectPath, bool lit)
    {
        if (objectPath is null)
        {
            return null;
        }

        string key = (lit ? "lit:" : "") + objectPath;
        if (_meshes.TryGetValue(key, out Mesh? cached))
        {
            return cached;
        }

        Mesh? mesh = null;
        if (_meshTable.TryGetValue(objectPath, out (string Glb, string Node) entry) || PackageMesh(objectPath, out entry))
        {
            mesh = Package(entry.Glb)?.Build(entry.Node, lit ? LightSource.Actor : LightSource.None);
        }

        _meshes[key] = mesh;
        return mesh;
    }

    // The mesh package of meshes/Package.glb, opened on first use; null when the file is missing.
    private MeshPackage? Package(string glb)
    {
        if (!_packages.TryGetValue(glb, out MeshPackage? package))
        {
            package = MeshPackage.Open(System.IO.Path.Combine(_folder, glb));
            _packages[glb] = package;
        }

        return package;
    }

    /// <summary>
    /// A mesh the table does not list, in a package the step exported whole: meshes/Package.glb, node Package_Name
    /// (the exporter's naming), when that file exists.
    /// </summary>
    private bool PackageMesh(string objectPath, out (string Glb, string Node) entry)
    {
        entry = default;
        int first = objectPath.IndexOf('.', StringComparison.Ordinal);
        int last = objectPath.LastIndexOf('.');
        if (first <= 0 || last == first)
        {
            return false;
        }

        string package = objectPath[..first];
        string glb = "meshes/" + package + ".glb";
        if (!File.Exists(System.IO.Path.Combine(_folder, glb)))
        {
            return false;
        }

        entry = (glb, package + "_" + objectPath[(last + 1)..]);
        return true;
    }

    /// <summary>
    /// Reads every texture and mesh of the library and makes the particle shaders of every emitter now, so the first
    /// shot, impact or grenade of a match does not stop the frame to load them (40 ms at the first gunfire of a match).
    /// The library is small (about 4 MB). Returns the textures loaded.
    /// </summary>
    public int Warm()
    {
        // Decode every PNG below on worker threads first (those BeginLoad decoded already are kept); the textures
        // are then made from them here in the same order as before.
        PrefetchTextures();
        PrefetchMeshTextures();
        int textures = 0;
        foreach (string png in _textureTable.Values)
        {
            textures += Texture(png) is null ? 0 : 1;
        }

        foreach (FxClass c in _classes.Values)
        {
            foreach (ParticleParams p in c.Emitters)
            {
                textures += Texture(p.Texture) is null ? 0 : 1;
                bool mesh = p.Kind == ParticleKind.Mesh;
                if (mesh)
                {
                    Mesh(p.StaticMesh);
                }

                ParticleSystemView.ShaderFor(p.DrawStyle, p.ZWrite, p.ZTest, mesh);
            }

            if (c.Assets.TryGetValue("StaticMesh", out string? path))
            {
                Mesh(path, lit: false);
                Mesh(path, lit: true);
            }
        }

        // An image decoded ahead that no material asked for after all is not kept.
        _textures.DropPrefetched();
        foreach (MeshPackage? package in _packages.Values)
        {
            package?.DropPrefetched();
        }

        return textures;
    }

    // The PNGs Warm makes textures of: the texture table's and the emitters' (with mipmaps, as Texture makes them).
    private void PrefetchTextures()
    {
        var wanted = new List<(string Uri, bool Mipmaps)>();
        foreach (string png in _textureTable.Values)
        {
            wanted.Add((png, true));
        }

        foreach (FxClass c in _classes.Values)
        {
            foreach (ParticleParams p in c.Emitters)
            {
                if (p.Texture is { } png)
                {
                    wanted.Add((png, true));
                }
            }
        }

        _textures.Prefetch(wanted);
    }

    // The textures of the materials of the meshes Warm builds, decoded per package on worker threads.
    private void PrefetchMeshTextures()
    {
        var nodes = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        void Want(string? objectPath)
        {
            if (objectPath is not null && (_meshTable.TryGetValue(objectPath, out (string Glb, string Node) entry) || PackageMesh(objectPath, out entry)))
            {
                if (!nodes.TryGetValue(entry.Glb, out List<string>? list))
                {
                    nodes[entry.Glb] = list = [];
                }

                list.Add(entry.Node);
            }
        }

        foreach (FxClass c in _classes.Values)
        {
            foreach (ParticleParams p in c.Emitters)
            {
                if (p.Kind == ParticleKind.Mesh)
                {
                    Want(p.StaticMesh);
                }
            }

            if (c.Assets.TryGetValue("StaticMesh", out string? path))
            {
                Want(path);
            }
        }

        foreach ((string glb, List<string> wanted) in nodes)
        {
            Package(glb)?.Prefetch(wanted);
        }
    }

    /// <summary>
    /// Construct and draw real particle surfaces during loading. Creating Shader objects alone leaves the
    /// renderer's vertex-layout pipelines and the first geometry upload until the first explosion or impact.
    /// These isolated simulations never spawn actors, play audio or consume gameplay random numbers.
    /// </summary>
    public void WarmViews(Node3D samples)
    {
        var camera = ParticleCamera.From(new FVector(0f, 300f, 0f), new FRotator(0, -16384, 0));
        foreach (FxClass effect in _classes.Values)
        {
            if (CreateSystem(effect.Path, 1) is not { } sim) continue;
            var view = new ParticleSystemView(sim, this, samples, "WarmParticles", effect.Float("HeatOpacity", RenderK.DefaultHeatOpacity));
            sim.Trigger();
            sim.Step(0.1f, default, default, default);
            view.Draw(new ParticleOwner(default, default, default, sim.GlobalOffset), camera);
            view.PreloadGeometry();
        }
    }

    private void Read(string path, SharedClasses classes)
    {
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllBytes(path));
        JsonElement root = doc.RootElement;
        if (root.TryGetProperty("textures", out JsonElement textures))
        {
            foreach (JsonProperty t in textures.EnumerateObject())
            {
                if (t.Value.TryGetProperty("png", out JsonElement png) && png.GetString() is { } p)
                {
                    _textureTable[t.Name] = p;
                    if (t.Value.TryGetProperty("material", out JsonElement blendMaterial) &&
                        blendMaterial.TryGetProperty("translucent", out JsonElement translucent) && translucent.ValueKind == JsonValueKind.True)
                        _translucent.Add(t.Name);
                    if (t.Value.TryGetProperty("material", out JsonElement animationMaterial) &&
                        animationMaterial.TryGetProperty("flipbook", out JsonElement flip))
                    {
                        var animation = new SkinAnimation(flip.GetProperty("texture").GetString()!,
                            flip.GetProperty("cols").GetInt32(), flip.GetProperty("rows").GetInt32(),
                            flip.GetProperty("frames").GetInt32(), flip.GetProperty("fps").GetSingle());
                        if (animation.Columns > 0 && animation.Rows > 0 && animation.Frames > 0 && animation.Fps > 0)
                            _skinAnimations[t.Name] = animation;
                    }
                    if (t.Value.TryGetProperty("material", out JsonElement mat) && mat.ValueKind == JsonValueKind.Object &&
                        mat.TryGetProperty("additive", out JsonElement add) && add.ValueKind == JsonValueKind.True)
                    {
                        _additive.Add(t.Name);
                    }
                }
            }
        }

        if (root.TryGetProperty("meshes", out JsonElement meshes))
        {
            foreach (JsonProperty m in meshes.EnumerateObject())
            {
                if (m.Value.TryGetProperty("glb", out JsonElement glb) && m.Value.TryGetProperty("node", out JsonElement node))
                {
                    _meshTable[m.Name] = (glb.GetString() ?? "", node.GetString() ?? "");
                }
            }
        }

        if (!root.TryGetProperty("classes", out JsonElement list))
        {
            return;
        }

        foreach (JsonProperty c in list.EnumerateObject())
        {
            var emitters = new List<ParticleParams>();
            var sources = new List<FxSubEmitter>();
            if (c.Value.TryGetProperty("emitters", out JsonElement subs) && subs.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement s in subs.EnumerateArray())
                {
                    string cls = s.TryGetProperty("class", out JsonElement sc) ? sc.GetString() ?? "" : "";
                    string obj = s.TryGetProperty("object", out JsonElement so) ? so.GetString() ?? "" : "";
                    JsonElement props = s.TryGetProperty("props", out JsonElement sp) ? sp.Clone() : default;
                    string? texture = s.TryGetProperty("texture", out JsonElement st) ? st.GetString() : null;
                    emitters.Add(ParticleParams.From(cls, obj, props, classes.DefaultsOf(cls), texture));
                    sources.Add(new FxSubEmitter(obj, cls, props, classes.DefaultsOf(cls), texture));
                }
            }

            var assets = new Dictionary<string, string>(StringComparer.Ordinal);
            if (c.Value.TryGetProperty("assets", out JsonElement a) && a.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty ap in a.EnumerateObject())
                {
                    string? v = ap.Value.ValueKind == JsonValueKind.String ? ap.Value.GetString()
                        : ap.Value.ValueKind == JsonValueKind.Array && ap.Value.GetArrayLength() > 0 ? ap.Value[0].GetString() : null;
                    if (v is not null)
                    {
                        assets[ap.Name] = v;
                    }
                }
            }

            JsonElement? defaults = classes.DefaultsOf(c.Name);
            _classes[c.Name] = new FxClass
            {
                Path = c.Name,
                Kind = c.Value.TryGetProperty("kind", out JsonElement k) ? k.GetString() ?? "" : "",
                Emitters = emitters,
                Sources = sources,
                Assets = assets,
                Defaults = defaults is { } d ? d.Clone() : default,
            };
        }
    }

    /// <summary>One exported static mesh package (meshes/Package.glb with its materials.json).</summary>
    private sealed class MeshPackage
    {
        private readonly Dictionary<string, int> _nodes = new(StringComparer.Ordinal);
        private readonly List<string> _materials = [];
        private readonly GlbDocument _glb;
        private readonly MaterialsDoc _document;
        private readonly TextureCache _textures;
        private readonly MaterialCompiler _compiler;

        private MeshPackage(GlbDocument glb, MaterialsDoc document, TextureCache textures, MaterialCompiler compiler)
        {
            _glb = glb;
            _document = document;
            _textures = textures;
            _compiler = compiler;
            foreach (JsonElement n in glb.Root.GetProperty("nodes").EnumerateArray())
            {
                if (n.TryGetProperty("name", out JsonElement name) && n.TryGetProperty("mesh", out JsonElement mesh))
                {
                    _nodes[name.GetString() ?? ""] = mesh.GetInt32();
                }
            }

            if (glb.Root.TryGetProperty("materials", out JsonElement mats))
            {
                foreach (JsonElement m in mats.EnumerateArray())
                {
                    _materials.Add(m.TryGetProperty("name", out JsonElement mn) ? mn.GetString() ?? "" : "");
                }
            }
        }

        public static MeshPackage? Open(string glbPath)
        {
            if (!File.Exists(glbPath))
            {
                return null;
            }

            string folder = System.IO.Path.GetDirectoryName(glbPath)!;
            string materials = System.IO.Path.Combine(folder, System.IO.Path.GetFileNameWithoutExtension(glbPath) + ".materials.json");
            MaterialsDoc document = MaterialsDoc.Load(materials);
            RecoverAnimatedGlows(document, folder);
            // Shared: both packages and every later play scene use the same textures of this folder.
            var textures = new TextureCache(folder) { Shared = true };
            var compiler = new MaterialCompiler(document, textures) { VisionLookup = VisionLookup.ModifierName };
            return new MeshPackage(GlbDocument.Load(glbPath), document, textures, compiler);
        }

        /// <summary>Decodes on worker threads the textures of the materials <see cref="Build"/> draws <paramref name="nodes"/> with.</summary>
        public void Prefetch(IEnumerable<string> nodes)
        {
            var wanted = new List<(string Uri, bool Mipmaps)>();
            foreach (string node in nodes)
            {
                if (!_nodes.TryGetValue(node, out int mesh))
                {
                    continue;
                }

                foreach (JsonElement p in _glb.Root.GetProperty("meshes")[mesh].GetProperty("primitives").EnumerateArray())
                {
                    int index = p.TryGetProperty("material", out JsonElement mi) ? mi.GetInt32() : -1;
                    // The materials MaterialCompiler.Get builds (a skipped or untextured one is not drawn).
                    if (index >= 0 && index < _materials.Count && _document.Find(_materials[index]) is { Skip: false, Texture: not null } entry)
                    {
                        wanted.AddRange(_compiler.TexturesOf(entry));
                    }
                }
            }

            _textures.Prefetch(wanted);
        }

        /// <summary>Disposes the images <see cref="Prefetch"/> decoded that no material took.</summary>
        public void DropPrefetched() => _textures.DropPrefetched();

        /// <summary>
        /// Older mesh packs bake the mine LED's zero-opacity diffuse separately from its unmasked glow, then
        /// replace that bake with an opaque-alpha animation atlas. Keep the animated glow additive, as the
        /// proximity mine's run-time light skins already are, so the atlas's black crossed planes add nothing.
        /// Ordinary translucent surfaces keep their alpha blend, including animated glass with visible opacity.
        /// </summary>
        private static void RecoverAnimatedGlows(MaterialsDoc document, string folder)
        {
            foreach (MaterialEntry entry in document.Materials.Values)
            {
                if (entry.Blend != MaterialBlend.Translucent || !entry.Unlit || entry.Opacity is not null ||
                    entry.SelfIllumMask is not null || entry.Texture is not { } baked || entry.Flipbook is not { } animation)
                    continue;
                if (HasUniformAlpha(folder, baked, 0) && HasUniformAlpha(folder, animation.Texture, 255))
                    entry.ListBlend = MaterialBlend.Additive;
            }
        }

        private static bool HasUniformAlpha(string folder, string texture, byte alpha)
        {
            string path = System.IO.Path.Combine(folder, texture);
            if (!File.Exists(path)) return false;
            using Image image = Image.LoadFromFile(path);
            if (image.IsEmpty()) return false;
            if (image.GetFormat() != Image.Format.Rgba8) image.Convert(Image.Format.Rgba8);
            byte[] pixels = image.GetData();
            for (int i = 3; i < pixels.Length; i += 4)
                if (pixels[i] != alpha) return false;
            return pixels.Length > 0;
        }

        public ArrayMesh? Build(string node, LightSource light)
        {
            if (!_nodes.TryGetValue(node, out int mesh))
            {
                return null;
            }

            var arrayMesh = new ArrayMesh();
            JsonElement prims = _glb.Root.GetProperty("meshes")[mesh].GetProperty("primitives");
            foreach (JsonElement p in prims.EnumerateArray())
            {
                JsonElement attrs = p.GetProperty("attributes");
                int materialIndex = p.TryGetProperty("material", out JsonElement mi) ? mi.GetInt32() : -1;
                string materialName = materialIndex >= 0 && materialIndex < _materials.Count ? _materials[materialIndex] : "";
                Material? material = _compiler.Get(materialName, light);
                if (material is null)
                {
                    continue;
                }

                float[] positions = _glb.ReadFloats(attrs.GetProperty("POSITION").GetInt32());
                int[] indices = p.TryGetProperty("indices", out JsonElement ii) ? _glb.ReadIndices(ii.GetInt32()) : Sequence(positions.Length / 3);
                var prim = new MeshPrimitive
                {
                    Material = materialName,
                    Positions = positions,
                    Normals = attrs.TryGetProperty("NORMAL", out JsonElement nn) ? _glb.ReadFloats(nn.GetInt32()) : null,
                    Uvs = attrs.TryGetProperty("TEXCOORD_0", out JsonElement t0) ? _glb.ReadFloats(t0.GetInt32()) : null,
                    Indices = indices,
                };
                var surface = new SurfaceBuilder(false, false);
                surface.Add(Transform3D.Identity, prim, 0);
                surface.AddTo(arrayMesh, material);
            }

            return arrayMesh.GetSurfaceCount() > 0 ? arrayMesh : null;
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
}
