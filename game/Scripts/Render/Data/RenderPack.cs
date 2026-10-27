using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Godot;
using OpenScctGame.Render.Materials;

namespace OpenScctGame.Render.Data;

/// <summary>The heat and EMF textures of a material (a HeatTextureModifier, SpecialVisionTextureModifier or EMFTextureModifier chain).</summary>
/// <param name="HeatTexture">PNG URI in the pack, or null when the modifier names no plain texture (the game then draws the flat HeatIntensity).</param>
/// <param name="HeatType">0 HEATTEX_Cool (4 x lum x HeatIntensity), 1 HEATTEX_Heat (lum + HeatIntensity).</param>
/// <param name="EmfTexture">PNG URI in the pack, or null.</param>
/// <param name="EmfInvert">bInvertEMFTexture.</param>
public sealed record VisionTextures(string? HeatTexture, int HeatType, string? EmfTexture, bool EmfInvert);

/// <summary>
/// The shared render pack (.content/shared/render, built by the content pipeline's renderpack step): the engine
/// textures of the vision passes and lights, the vision modifiers of every texture package, and the class-default meshes
/// of gameplay actors. Loaded once per folder and shared by every map. docs/RENDERING.md#render-pack.
/// </summary>
public sealed class RenderPack
{
    public const string Folder = "render";

    // Thermal table, grain, sonar, EMF environment, torch cookie, attenuation and corona: object paths in render.json.
    public const string ThermalTable = "Engine.heat_and_attenuation_2d";
    public const string Noise = "Engine.Noise0_gauss";
    public const string Sonar = "Engine.sonar_background";
    public const string EmfEnvironment = "Echelon.EMFInvDarkBlue";
    public const string TorchCookie = "Echelon.flashlight";
    public const string SpotCookie = "Echelon.spot_bw";
    public const string LinearAttenuation = "Engine.linear_attenuation";
    public const string LateAttenuation = "Engine.late_attenuation";
    public const string TorchCorona = "sfx.def_flare";

    private static readonly ConcurrentDictionary<string, RenderPack?> s_loaded = new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, string> _textures = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, VisionTextures> _modifiers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, VisionTextures> _modifiersShort = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, VisionTextures> _bases = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _gameplayMeshes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Image?> _images = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _mips = new(StringComparer.OrdinalIgnoreCase);

    private RenderPack(string root)
    {
        Root = root;
        Textures = new TextureCache(root);
    }

    /// <summary>The pack folder (.content/shared/render).</summary>
    public string Root { get; }

    /// <summary>Textures of the pack by URI (raw display values, like every Faithful texture).</summary>
    public TextureCache Textures { get; }

    /// <summary>gameplay/gameplay.glb, or null when the pack has no gameplay meshes.</summary>
    public string? GameplayGlb => File.Exists(Path.Combine(Root, "gameplay", "gameplay.glb")) ? Path.Combine(Root, "gameplay", "gameplay.glb") : null;

    public int VisionModifierCount => _modifiers.Count;

    public int GameplayMeshCount => _gameplayMeshes.Count;

    /// <summary>The pack under a shared folder (.content/shared), loaded once; null when it was not built.</summary>
    public static RenderPack? Load(string sharedRoot)
    {
        ArgumentException.ThrowIfNullOrEmpty(sharedRoot);
        string root = Path.GetFullPath(Path.Combine(sharedRoot, Folder));
        return s_loaded.GetOrAdd(root, Read);
    }

    private static RenderPack? Read(string root)
    {
        string renderJson = Path.Combine(root, "render.json");
        if (!File.Exists(renderJson))
        {
            return null;
        }

        var pack = new RenderPack(root);
        using (JsonDocument doc = JsonDocument.Parse(File.ReadAllBytes(renderJson)))
        {
            JsonElement r = doc.RootElement;
            if (r.TryGetProperty("textures", out JsonElement textures))
            {
                foreach (JsonProperty t in textures.EnumerateObject())
                {
                    if (t.Value.ValueKind == JsonValueKind.String)
                    {
                        pack._textures[t.Name] = t.Value.GetString()!;
                    }
                }
            }

            if (r.TryGetProperty("gameplayMeshes", out JsonElement meshes))
            {
                foreach (JsonProperty m in meshes.EnumerateObject())
                {
                    pack._gameplayMeshes[m.Name] = m.Value.GetString() ?? "";
                    pack._gameplayMeshes.TryAdd(ShortPath(m.Name), m.Value.GetString() ?? "");
                }
            }
        }

        string mipsJson = Path.Combine(root, "mips.json");
        if (File.Exists(mipsJson))
        {
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllBytes(mipsJson));
            if (doc.RootElement.TryGetProperty("mips", out JsonElement mips))
            {
                foreach (JsonProperty m in mips.EnumerateObject())
                {
                    int n = m.Value.GetInt32();
                    pack._mips[m.Name] = n;
                    pack._mips.TryAdd(ShortPath(m.Name), n);
                }
            }
        }

        string visionJson = Path.Combine(root, "vision.json");
        if (File.Exists(visionJson))
        {
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllBytes(visionJson));
            JsonElement r = doc.RootElement;
            if (r.TryGetProperty("modifiers", out JsonElement modifiers))
            {
                foreach (JsonProperty m in modifiers.EnumerateObject())
                {
                    VisionTextures v = Vision(m.Value);
                    pack._modifiers[m.Name] = v;
                    pack._modifiersShort.TryAdd(ShortPath(m.Name), v);
                }
            }

            if (r.TryGetProperty("bases", out JsonElement bases))
            {
                foreach (JsonProperty b in bases.EnumerateObject())
                {
                    VisionTextures v = Vision(b.Value);
                    pack._bases[b.Name] = v;
                    pack._bases.TryAdd(ShortPath(b.Name), v);
                }
            }
        }

        return pack;
    }

    /// <summary>Package.Group.Name to Package.Name (converted mesh materials name the package and object only).</summary>
    public static string ShortPath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        int first = path.IndexOf('.', StringComparison.Ordinal);
        int last = path.LastIndexOf('.');
        return first < 0 || first == last ? path : path[..first] + path[last..];
    }

    private static VisionTextures Vision(JsonElement e)
    {
        string? heat = null;
        int heatType = 0;
        string? emf = null;
        bool invert = false;
        if (e.TryGetProperty("heat", out JsonElement h) && h.ValueKind == JsonValueKind.Object)
        {
            bool use = !h.TryGetProperty("useTexture", out JsonElement u) || u.ValueKind == JsonValueKind.True;
            heat = use ? MaterialsDoc.Str(h, "texture") : null;
            heatType = h.TryGetProperty("type", out JsonElement ty) && ty.ValueKind == JsonValueKind.Number ? ty.GetInt32() : 0;
        }

        if (e.TryGetProperty("emf", out JsonElement m) && m.ValueKind == JsonValueKind.Object)
        {
            emf = MaterialsDoc.Str(m, "texture");
            invert = MaterialsDoc.Bool(m, "invert");
        }

        return new VisionTextures(heat, heatType, emf, invert);
    }

    /// <summary>
    /// The vision textures of a map material: only when its source is a vision modifier itself (a mesh drawn with the
    /// plain texture under a modifier does not get the modifier's heat).
    /// </summary>
    public VisionTextures? VisionOf(string? materialPath) =>
        !string.IsNullOrEmpty(materialPath) && _modifiers.TryGetValue(materialPath, out VisionTextures? v) ? v : null;

    /// <summary>The vision textures of a modifier named Package.Name (gameplay pack meshes name their section's material so).</summary>
    public VisionTextures? VisionOfModifierName(string? name) =>
        !string.IsNullOrEmpty(name) && (_modifiers.TryGetValue(name, out VisionTextures? v) || _modifiersShort.TryGetValue(name, out v)) ? v : null;

    /// <summary>
    /// The vision textures of a character skin, whose converted material names the base material under its modifiers
    /// (Package.Name or Package.Group.Name): the skin's heat and EMF modifiers.
    /// </summary>
    public VisionTextures? VisionOfSkin(string? materialPath)
    {
        if (string.IsNullOrEmpty(materialPath))
        {
            return null;
        }

        return _modifiers.TryGetValue(materialPath, out VisionTextures? v) ? v
            : _bases.TryGetValue(materialPath, out v) ? v
            : _bases.TryGetValue(ShortPath(materialPath), out v) ? v
            : null;
    }

    /// <summary>
    /// The mip levels the game stores for a texture (Package.Group.Name or Package.Name), or null when the pack does not
    /// know it. A texture of one level is drawn without mipmaps, as the original does.
    /// </summary>
    public int? MipLevels(string? texturePath) =>
        texturePath is not null && (_mips.TryGetValue(texturePath, out int n) || _mips.TryGetValue(ShortPath(texturePath), out n)) ? n : null;

    /// <summary>The glTF node of a class-default mesh (Package.Group.Name or Package.Name), or null.</summary>
    public string? GameplayMeshNode(string meshPath) =>
        _gameplayMeshes.TryGetValue(meshPath, out string? node) || _gameplayMeshes.TryGetValue(ShortPath(meshPath), out node) ? node : null;

    /// <summary>The PNG URI of a named texture (render.json), or null.</summary>
    public string? TextureUri(string objectPath) => _textures.GetValueOrDefault(objectPath);

    /// <summary>A named texture as a Godot texture (raw values, with or without mips), or null.</summary>
    public Texture2D? Texture(string objectPath, bool mipmaps = false) =>
        TextureUri(objectPath) is { } uri ? Textures.Get(uri, mipmaps) : null;

    /// <summary>A named texture's image (RGBA8, raw values) for the render thread, or null.</summary>
    public Image? Image(string objectPath)
    {
        lock (_images)
        {
            if (_images.TryGetValue(objectPath, out Image? cached))
            {
                return cached;
            }

            Image? image = null;
            if (TextureUri(objectPath) is { } uri)
            {
                string path = Path.Combine(Root, uri.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(path))
                {
                    image = new Image();
                    if (image.LoadPngFromBuffer(File.ReadAllBytes(path)) != Error.Ok)
                    {
                        image = null;
                    }
                    else if (image.GetFormat() != Godot.Image.Format.Rgba8)
                    {
                        image.Convert(Godot.Image.Format.Rgba8);
                    }
                }
            }

            _images[objectPath] = image;
            return image;
        }
    }

    /// <summary>The pack next to the default shared folder (.content/shared), or null.</summary>
    public static RenderPack? Default() => Load(Characters.SkeletalModel.DefaultSharedRoot());
}
