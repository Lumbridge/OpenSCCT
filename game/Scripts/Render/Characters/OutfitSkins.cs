using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using OpenScct.Provenance;
using OpenScctGame.Render.Data;
using OpenScctGame.Render.Materials;
using Vector2N = System.Numerics.Vector2;

namespace OpenScctGame.Render.Characters;

/// <summary>
/// The stock suits' textures redrawn for meshes laid out differently (spec/additions/map-outfits.md), through a
/// <see cref="SurfaceTransfer"/>: a map outfit's own model gets the heat and EMF masks, dry and wet, of the stock mesh
/// it stands in for, and the merc's first-person arms get the dressed merc's sleeves and gloves. Made in memory once
/// per pair of meshes, while the map loads (<see cref="PawnOutfits.Prepare"/>).
/// </summary>
public sealed class OutfitSkins
{
    // The masks are drawn at this size: the stock heat and EMF skins are 256 or 512 texels wide.
    private const int Size = 256;

    private static readonly Dictionary<(string Model, string Stock), OutfitSkins?> s_cache = new();
    private static readonly Dictionary<string, ArmsSkin?> s_arms = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, BindMesh> s_meshes = new(StringComparer.OrdinalIgnoreCase);

    private readonly SurfaceTransfer _transfer;
    private readonly string _modelGlb;
    private readonly RenderPack? _pack;
    private readonly Dictionary<string, TexelImage?> _images = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, TexelMap> _maps = new();
    private readonly Dictionary<string, VisionMasks?> _vision = new(StringComparer.Ordinal);

    private OutfitSkins(SurfaceTransfer transfer, string modelGlb, string stockGlb)
    {
        _transfer = transfer;
        _modelGlb = modelGlb;
        // shared/chars/<Package>/<Mesh>.glb: the render pack with the vision skins is shared/render.
        _pack = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(stockGlb))) is { } shared ? RenderPack.Load(shared) : null;
    }

    /// <summary>
    /// The merc's first-person arms in his outfit: the texture, and the name it is kept under in the view model's
    /// texture cache (one name per outfit, so a later map's outfit never meets the material made for an earlier one).
    /// </summary>
    public sealed record ArmsSkin(Texture2D Texture, string Key);

    /// <summary>
    /// The skins of the outfit model <paramref name="modelGlb"/> standing in for the stock mesh
    /// <paramref name="stockGlb"/>, or null when either cannot be read as a skinned mesh.
    /// </summary>
    public static OutfitSkins? For(string modelGlb, string stockGlb)
    {
        var key = (Path.GetFullPath(modelGlb), Path.GetFullPath(stockGlb));
        if (s_cache.TryGetValue(key, out OutfitSkins? cached))
        {
            return cached;
        }

        OutfitSkins? skins = null;
        try
        {
            skins = new OutfitSkins(new SurfaceTransfer(Mesh(key.Item1), Mesh(key.Item2)), key.Item1, key.Item2);
        }
        catch (InvalidDataException e)
        {
            GD.PushWarning($"outfit model {modelGlb}: no heat, EMF or wet masks ({e.Message})");
        }

        s_cache[key] = skins;
        return skins;
    }

    /// <summary>The name of a model material's wet skin, for the local player's side.</summary>
    public static string WetName(string material, bool localSpy) => material + (localSpy ? "+wet-spy-machine" : "+wet-merc-machine");

    /// <summary>
    /// The heat and EMF masks of a material of the model, or of its wet skin (<see cref="WetName"/>): each texel takes
    /// the stock skin's heat and EMF at the nearest point of the stock mesh. Null for a material the model lacks.
    /// </summary>
    public VisionMasks? Vision(string material)
    {
        if (_vision.TryGetValue(material, out VisionMasks? made))
        {
            return made;
        }

        (string own, bool wet, bool localSpy) = Parse(material);
        int index = _transfer.Target.MaterialIndex(own);
        made = index < 0 || _pack is null || Map(index) is not { IsEmpty: false } map ? null : BakeVision(map, wet, localSpy);
        _vision[material] = made;
        return made;
    }

    /// <summary>
    /// True when the model's material <paramref name="material"/> lies over a stock surface that has a wet skin on the
    /// local player's side (<see cref="WetSkins"/>): only those get one.
    /// </summary>
    public bool GetsWet(string material, bool localSpy)
    {
        int index = _transfer.Target.MaterialIndex(material);
        if (index < 0)
        {
            return false;
        }

        int[] counts = Map(index).Counts(_transfer.Source.Materials.Length);
        for (int m = 0; m < counts.Length; m++)
        {
            if (counts[m] > 0 && WetSkins.WetSkinOf(_transfer.Source.Materials[m], localSpy) is not null)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The texture, with its cache name, of the material <paramref name="armsMaterial"/> of the view model
    /// <paramref name="viewModelGlb"/> redrawn from the merc mesh <paramref name="mercGlb"/> as dressed
    /// (<paramref name="diffuse"/>: full paths of textures drawn over its own materials, by material). Texels the arms
    /// do not cover keep the view model's own.
    /// Null when a mesh cannot be read or the view model's rig shares no bones with the merc's (the pistols' arms are
    /// a model of their own and keep their look).
    /// </summary>
    public static ArmsSkin? Arms(string viewModelGlb, string armsMaterial, string mercGlb, IReadOnlyDictionary<string, string> diffuse)
    {
        string key = string.Join("|", Path.GetFullPath(viewModelGlb), armsMaterial, Path.GetFullPath(mercGlb), string.Join(",", diffuse.Values));
        if (s_arms.TryGetValue(key, out ArmsSkin? cached))
        {
            return cached;
        }

        ArmsSkin? made = null;
        try
        {
            var transfer = new SurfaceTransfer(Mesh(viewModelGlb), Mesh(mercGlb));
            int index = transfer.Target.MaterialIndex(armsMaterial);
            if (index >= 0 && transfer.Map(index, Size) is { IsEmpty: false } map)
            {
                string viewFolder = Path.GetDirectoryName(Path.GetFullPath(viewModelGlb)) ?? ".";
                string mercFolder = Path.GetDirectoryName(Path.GetFullPath(mercGlb)) ?? ".";
                TexelImage? own = transfer.Target.Images[index] is { } uri ? TexelImage.Load(Path.Combine(viewFolder, uri), Size) : null;
                var source = new TexelImage?[transfer.Source.Materials.Length];
                for (int m = 0; m < source.Length; m++)
                {
                    string? path = diffuse.TryGetValue(transfer.Source.Materials[m], out string? over) ? over
                        : transfer.Source.Images[m] is { } u ? Path.Combine(mercFolder, u) : null;
                    source[m] = path is null ? null : TexelImage.Load(path, 0);
                }

                byte[] texels = map.Bake((m, uv) => source[m]?.Sample(uv) ?? Rgba.Grey(0.5f), i => own?.Texel(i) ?? Rgba.Grey(0.5f));
                if (own is not null)
                {
                    // The suit's colour on the view model's own alpha (its mask, where the material has one).
                    for (int i = 0; i < Size * Size; i++)
                    {
                        texels[(i * 4) + 3] = own.Texel(i).A;
                    }
                }

                made = new ArmsSkin(Texture(texels), "outfit-arms:" + key);
            }
        }
        catch (InvalidDataException e)
        {
            GD.PushWarning($"first-person arms {viewModelGlb}: the outfit's sleeves left out ({e.Message})");
        }

        s_arms[key] = made;
        return made;
    }

    // The heat as HEATTEX_Cool (heat = 4 x lum x HeatIntensity): a cool skin's texel as it is, a HEATTEX_Heat skin's
    // (lum + HeatIntensity) divided back, and a surface without a heat skin at its flat HeatIntensity. The EMF white
    // where the stock surface has none, inverted skins turned back.
    [Provenance(ProvenanceKind.Estimated, "HEATTEX_Heat texels converted to the cool form at the pawns' HeatIntensity 1 (RenderK.PawnHeatIntensity)")]
    private VisionMasks BakeVision(TexelMap map, bool wet, bool localSpy)
    {
        int count = _transfer.Source.Materials.Length;
        var heat = new (TexelImage? Image, int Type)[count];
        var emf = new (TexelImage? Image, bool Invert)[count];
        for (int m = 0; m < count; m++)
        {
            string surface = _transfer.Source.Materials[m];
            // A stock surface without a wet skin stays dry in the rain.
            string skin = wet ? WetSkins.WetSkinOf(surface, localSpy) ?? surface : surface;
            if (_pack!.VisionOfSkin(skin) is not { } v)
            {
                continue;
            }

            heat[m] = (v.HeatTexture is { } h ? Loaded(Path.Combine(_pack.Root, h)) : null, v.HeatType);
            emf[m] = (v.EmfTexture is { } e ? Loaded(Path.Combine(_pack.Root, e)) : null, v.EmfInvert);
        }

        const float intensity = RenderK.PawnHeatIntensity;
        Rgba flat = Rgba.Grey(0.25f);
        byte[] heatTexels = map.Bake((m, uv) =>
        {
            if (heat[m].Image?.Sample(uv) is not { } c)
            {
                return flat;
            }

            float lum = ((0.3f * c.R) + (0.59f * c.G) + (0.11f * c.B)) / 255f;
            return Rgba.Grey(heat[m].Type == 0 ? lum : (lum + intensity) / (4f * intensity));
        }, _ => flat);
        var white = new Rgba(255, 255, 255, 255);
        byte[] emfTexels = map.Bake((m, uv) => emf[m].Image?.Sample(uv) is { } c
            ? emf[m].Invert ? new Rgba((byte)(255 - c.R), (byte)(255 - c.G), (byte)(255 - c.B), 255) : c with { A = 255 }
            : white, _ => white);
        return new VisionMasks(Texture(heatTexels), Texture(emfTexels));
    }

    // The merc's mesh serves every view model's arms: read each file once.
    private static BindMesh Mesh(string path)
    {
        string full = Path.GetFullPath(path);
        if (!s_meshes.TryGetValue(full, out BindMesh? mesh))
        {
            s_meshes[full] = mesh = BindMesh.Load(full);
        }

        return mesh;
    }

    private TexelMap Map(int material)
    {
        if (!_maps.TryGetValue(material, out TexelMap? map))
        {
            _maps[material] = map = _transfer.Map(material, Size);
            if (map.IsEmpty)
            {
                GD.PushWarning(
                    $"outfit model {_modelGlb}: {_transfer.Target.Materials[material]} lies on no bone of the stock rig, so it gets no heat or EMF masks");
            }
        }

        return map;
    }

    private TexelImage? Loaded(string path)
    {
        if (!_images.TryGetValue(path, out TexelImage? image))
        {
            _images[path] = image = TexelImage.Load(path, 0);
        }

        return image;
    }

    private static (string Own, bool Wet, bool LocalSpy) Parse(string material)
    {
        foreach (bool localSpy in new[] { true, false })
        {
            string suffix = WetName("", localSpy);
            if (material.EndsWith(suffix, StringComparison.Ordinal))
            {
                return (material[..^suffix.Length], true, localSpy);
            }
        }

        return (material, false, false);
    }

    private static ImageTexture Texture(byte[] rgba)
    {
        Image image = Godot.Image.CreateFromData(Size, Size, false, Godot.Image.Format.Rgba8, rgba);
        image.GenerateMipmaps();
        return ImageTexture.CreateFromImage(image);
    }

    /// <summary>A decoded RGBA8 image sampled in managed code (wrapping, bilinear).</summary>
    private sealed class TexelImage
    {
        private readonly int _width;
        private readonly int _height;
        private readonly byte[] _data;

        private TexelImage(int width, int height, byte[] data)
        {
            _width = width;
            _height = height;
            _data = data;
        }

        /// <summary>The PNG at <paramref name="path"/>, resized to <paramref name="size"/> squared unless 0; null when missing.</summary>
        public static TexelImage? Load(string path, int size)
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var image = new Godot.Image();
            if (image.LoadPngFromBuffer(File.ReadAllBytes(path)) != Error.Ok)
            {
                return null;
            }

            image.Convert(Godot.Image.Format.Rgba8);
            if (size > 0 && (image.GetWidth() != size || image.GetHeight() != size))
            {
                image.Resize(size, size, Godot.Image.Interpolation.Bilinear);
            }

            return new TexelImage(image.GetWidth(), image.GetHeight(), image.GetData());
        }

        /// <summary>Texel <paramref name="index"/> of the image in row order.</summary>
        public Rgba Texel(int index)
        {
            int o = index * 4;
            return o + 3 < _data.Length ? new Rgba(_data[o], _data[o + 1], _data[o + 2], _data[o + 3]) : default;
        }

        public Rgba Sample(Vector2N uv)
        {
            float fx = (uv.X * _width) - 0.5f, fy = (uv.Y * _height) - 0.5f;
            int x0 = (int)MathF.Floor(fx), y0 = (int)MathF.Floor(fy);
            float tx = fx - x0, ty = fy - y0;
            Rgba a = Texel((Wrap(y0, _height) * _width) + Wrap(x0, _width));
            Rgba b = Texel((Wrap(y0, _height) * _width) + Wrap(x0 + 1, _width));
            Rgba c = Texel((Wrap(y0 + 1, _height) * _width) + Wrap(x0, _width));
            Rgba d = Texel((Wrap(y0 + 1, _height) * _width) + Wrap(x0 + 1, _width));
            return new Rgba(Mix(a.R, b.R, c.R, d.R, tx, ty), Mix(a.G, b.G, c.G, d.G, tx, ty), Mix(a.B, b.B, c.B, d.B, tx, ty),
                Mix(a.A, b.A, c.A, d.A, tx, ty));
        }

        private static byte Mix(byte a, byte b, byte c, byte d, float tx, float ty) =>
            (byte)Math.Clamp((int)MathF.Round(((a + ((b - a) * tx)) * (1f - ty)) + ((c + ((d - c) * tx)) * ty)), 0, 255);

        private static int Wrap(int i, int size) => ((i % size) + size) % size;
    }
}
