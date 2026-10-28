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
    private readonly HashSet<string> _additive = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Mesh?> _meshes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, MeshPackage?> _packages = new(StringComparer.OrdinalIgnoreCase);
    private readonly TextureCache _textures;
    private readonly string _folder;

    private FxLibrary(string folder)
    {
        _folder = folder;
        _textures = new TextureCache(folder);
    }

    /// <summary>
    /// The effects of a shared content root (an empty library when the fx step has not run). Each play scene loads its
    /// own: the library holds Godot resources, which must go with the scene rather than outlive the engine.
    /// </summary>
    public static FxLibrary Load(string sharedRoot)
    {
        ArgumentException.ThrowIfNullOrEmpty(sharedRoot);
        string folder = System.IO.Path.GetFullPath(System.IO.Path.Combine(sharedRoot, "fx"));
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
            if (!_packages.TryGetValue(entry.Glb, out MeshPackage? package))
            {
                package = MeshPackage.Open(System.IO.Path.Combine(_folder, entry.Glb));
                _packages[entry.Glb] = package;
            }

            mesh = package?.Build(entry.Node, lit ? LightSource.Actor : LightSource.None);
        }

        _meshes[key] = mesh;
        return mesh;
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

        return textures;
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
        private readonly MaterialCompiler _compiler;

        private MeshPackage(GlbDocument glb, MaterialCompiler compiler)
        {
            _glb = glb;
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
            var compiler = new MaterialCompiler(MaterialsDoc.Load(materials), new TextureCache(folder)) { VisionLookup = VisionLookup.ModifierName };
            return new MeshPackage(GlbDocument.Load(glbPath), compiler);
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
