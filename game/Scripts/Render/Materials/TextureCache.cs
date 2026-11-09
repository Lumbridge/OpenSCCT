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
    // File textures of every Shared cache, by full path and preparation, so a PNG used by several models is made once.
    private static readonly object s_sharedLock = new();
    private static readonly Dictionary<string, Texture2D> s_shared = new(StringComparer.Ordinal);

    private readonly string _root;
    private readonly Dictionary<string, Texture2D?> _textures = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Image?> _images = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Image?> _decoded = new(StringComparer.Ordinal);

    public TextureCache(string root)
    {
        _root = root;
    }

    public int Count => _textures.Count;

    /// <summary>
    /// File textures come from (and go to) a process-wide store shared by every Shared cache, keyed by the file's full
    /// path, the mip flag and <see cref="MipDrop"/>/<see cref="MipDropMinSize"/>. Textures added with <see cref="Add"/>,
    /// CPU images (<see cref="GetImage"/>), <see cref="Missing"/> and <see cref="Count"/> stay this cache's own.
    /// Off by default.
    /// </summary>
    public bool Shared { get; init; }

    /// <summary>Drops the shared store (at exit, with the models that use it).</summary>
    public static void ReleaseShared()
    {
        lock (s_sharedLock)
        {
            s_shared.Clear();
        }
    }

    public Texture2D? GetAuthored(string slot, OpenScct.Content.Materials.MaterialTexture binding, OpenScct.Content.Materials.MaterialAnimation? animation = null)
    {
        string key = binding.File + "#authored:" + AuthoredTextureImage.Key(slot, binding, animation);
        if (_textures.TryGetValue(key, out var known)) return known;
        string file = Path.Combine(_root, binding.File);
        string? shared = Shared ? SharedKey(file, key) : null;
        if (shared is not null && FromShared(shared) is { } stored) return _textures[key] = stored;
        if (!File.Exists(file)) { Missing.Add(binding.File); return null; }
        using Image image = AuthoredTextureImage.Decode(File.ReadAllBytes(file), slot, binding, animation);
        if (image.HasMipmaps() && MipDrop > 0)
        {
            int columns = slot == "environment" ? 1 : animation?.Columns ?? 1, rows = slot == "environment" ? 1 : animation?.Rows ?? 1, level = 0;
            while (level < Math.Min(MipDrop, image.GetMipmapCount()) && (image.GetWidth() >> (level + 1)) / columns >= Math.Max(1, MipDropMinSize) && (image.GetHeight() >> (level + 1)) / rows >= 1) level++;
            if (level > 0) { byte[] bytes = image.GetData(); int offset = (int)image.GetMipmapOffset(level); image.SetData(image.GetWidth() >> level, image.GetHeight() >> level, level < image.GetMipmapCount(), image.GetFormat(), bytes[offset..]); }
        }
        Texture2D made = ImageTexture.CreateFromImage(image);
        if (shared is not null) ToShared(shared, made);
        return _textures[key] = made;
    }

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
            if (!string.IsNullOrEmpty(uri) && !_textures.ContainsKey(Key(uri, mipmaps)) && !_decoded.ContainsKey(Key(uri, mipmaps)) &&
                (mipmaps || !_images.ContainsKey(uri)) && !(Shared && FromShared(SharedKey(uri, mipmaps)) is not null) &&
                !work.Contains((uri, mipmaps)))
            {
                work.Add((uri, mipmaps));
            }
        }

        Parallel.ForEach(work, item => _decoded[Key(item.Uri, item.Mipmaps)] = Decode(item.Uri, item.Mipmaps));
    }

    /// <summary>Disposes the images <see cref="Prefetch"/> decoded that no <see cref="Get"/> or <see cref="GetImage"/> took.</summary>
    public void DropPrefetched()
    {
        foreach (string key in _decoded.Keys)
        {
            if (_decoded.TryRemove(key, out Image? image))
            {
                image?.Dispose();
            }
        }
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

        string? shared = Shared ? SharedKey(uri, mipmaps) : null;
        if (shared is not null && FromShared(shared) is { } stored)
        {
            // Another cache made it first: an image decoded here ahead of time is not needed.
            if (_decoded.TryRemove(key, out Image? unused))
            {
                unused?.Dispose();
            }

            return _textures[key] = stored;
        }

        // A CPU image kept for GetImage stays; any other image is only the source of the upload.
        Image? source = null;
        bool retained = !mipmaps && _images.TryGetValue(uri, out source);
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        Image? image = retained ? source
            : _decoded.TryRemove(key, out Image? pre) ? pre : Decode(uri, mipmaps);
        OpenScctGame.Diagnostics.LoadTimer.Add("texture-decode", started);
        started = System.Diagnostics.Stopwatch.GetTimestamp();
        Texture2D? texture = image is null ? null : ImageTexture.CreateFromImage(image);
        OpenScctGame.Diagnostics.LoadTimer.Add("texture-create", started);
        if (!retained)
        {
            image?.Dispose();
        }

        if (texture is null)
        {
            Missing.Add(uri);
        }
        else if (shared is not null)
        {
            ToShared(shared, texture);
        }

        _textures[key] = texture;
        return texture;
    }

    /// <summary>
    /// A retained CPU image without mipmaps, decoded from the PNG rather than read back from a GPU texture.
    /// Only images explicitly requested here are retained. Callers must copy it before changing or disposing it.
    /// </summary>
    public Image? GetImage(string? uri)
    {
        if (string.IsNullOrEmpty(uri)) return null;
        if (_images.TryGetValue(uri, out Image? cached)) return cached;
        string key = Key(uri, false);
        Image? image = _decoded.TryRemove(key, out Image? pre) ? pre : Decode(uri, false);
        if (image is null) Missing.Add(uri);
        _images[uri] = image;
        return image;
    }

    /// <summary>Keeps a texture made in memory under <paramref name="uri"/> (a name no file has), with or without mipmaps.</summary>
    public void Add(string uri, Texture2D texture)
    {
        ArgumentException.ThrowIfNullOrEmpty(uri);
        _textures[Key(uri, true)] = texture;
        _textures[Key(uri, false)] = texture;
    }

    /// <summary>True when the cache already has a texture under <paramref name="uri"/> (added with <see cref="Add"/> or loaded).</summary>
    public bool Holds(string uri) => _textures.TryGetValue(Key(uri, true), out Texture2D? t) && t is not null;

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

    // The full path names the file; the rest names how it was prepared, so no cache gets a texture made differently.
    private string SharedKey(string uri, bool mipmaps) =>
        SharedKey(Path.Combine(_root, uri.Replace('/', Path.DirectorySeparatorChar)), Key(uri, mipmaps));

    private string SharedKey(string file, string key)
    {
        int mark = key.IndexOf('#', StringComparison.Ordinal);
        return Path.GetFullPath(file) + (mark < 0 ? "" : key[mark..]) + "|" + MipDrop + "|" + MipDropMinSize;
    }

    private static Texture2D? FromShared(string key)
    {
        lock (s_sharedLock)
        {
            return s_shared.TryGetValue(key, out Texture2D? texture) && GodotObject.IsInstanceValid(texture) ? texture : null;
        }
    }

    private static void ToShared(string key, Texture2D texture)
    {
        lock (s_sharedLock)
        {
            s_shared[key] = texture;
        }
    }
}
