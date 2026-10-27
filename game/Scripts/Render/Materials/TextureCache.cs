using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Godot;

namespace OpenScctGame.Render.Materials;

/// <summary>
/// Textures of a render folder, loaded once by their relative URI (<c>textures/X.png</c>). The images keep their
/// stored bytes: no sRGB decode anywhere, since the Faithful materials work on display values. Colour textures get a
/// plain box-filtered mip chain; lightmaps have none (UE2 lightmaps are sampled bilinear from one level).
/// </summary>
public sealed class TextureCache
{
    private readonly string _root;
    private readonly Dictionary<string, Texture2D?> _textures = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Image?> _decoded = new(StringComparer.Ordinal);

    public TextureCache(string root)
    {
        _root = root;
    }

    public int Count => _textures.Count;

    /// <summary>
    /// Top mip levels left out of colour textures (the original's TextureDetail settings drop levels of large
    /// textures); textures stay at least <see cref="MipDropMinSize"/> wide. Lightmaps keep every texel.
    /// </summary>
    public int MipDrop { get; init; }

    public int MipDropMinSize { get; init; } = 64;

    /// <summary>URIs that could not be loaded.</summary>
    public List<string> Missing { get; } = [];

    /// <summary>Decodes the PNGs on worker threads ahead of <see cref="Get"/>; textures are made on the caller's thread.</summary>
    public void Prefetch(IEnumerable<(string Uri, bool Mipmaps)> textures)
    {
        var work = new List<(string Uri, bool Mipmaps)>();
        foreach ((string uri, bool mipmaps) in textures)
        {
            if (!_textures.ContainsKey(Key(uri, mipmaps)) && !_decoded.ContainsKey(Key(uri, mipmaps)))
            {
                work.Add((uri, mipmaps));
            }
        }

        Parallel.ForEach(work, item => _decoded[Key(item.Uri, item.Mipmaps)] = Decode(item.Uri, item.Mipmaps));
    }

    public Texture2D? Get(string? uri, bool mipmaps = true)
    {
        if (string.IsNullOrEmpty(uri))
        {
            return null;
        }

        string key = Key(uri, mipmaps);
        if (_textures.TryGetValue(key, out Texture2D? cached))
        {
            return cached;
        }

        Image? image = _decoded.TryRemove(key, out Image? pre) ? pre : Decode(uri, mipmaps);
        Texture2D? texture = image is null ? null : ImageTexture.CreateFromImage(image);
        if (texture is null)
        {
            Missing.Add(uri);
        }

        _textures[key] = texture;
        return texture;
    }

    private Image? Decode(string uri, bool mipmaps)
    {
        string path = Path.Combine(_root, uri.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path))
        {
            return null;
        }

        byte[] bytes = File.ReadAllBytes(path);
        var image = new Image();
        if (image.LoadPngFromBuffer(bytes) != Error.Ok)
        {
            return null;
        }

        if (image.GetFormat() != Image.Format.Rgba8)
        {
            image.Convert(Image.Format.Rgba8);
        }

        if (mipmaps)
        {
            for (int i = 0; i < MipDrop && image.GetWidth() > MipDropMinSize && image.GetHeight() > 1; i++)
            {
                image.Resize(image.GetWidth() / 2, Math.Max(1, image.GetHeight() / 2), Image.Interpolation.Bilinear);
            }

            image.GenerateMipmaps();
        }

        return image;
    }

    private static string Key(string uri, bool mipmaps) => mipmaps ? uri : uri + "#nomip";
}
