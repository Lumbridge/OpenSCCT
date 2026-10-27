using System;
using System.Collections.Generic;

namespace OpenScct.Content;

/// <summary>
/// A property value naming a non-actor object: a mesh, material, sound... as <c>Package.Group.Name</c> with every
/// group level. An asset whose package is the map id lives in the map's own <c>render/</c> library.
/// </summary>
public sealed class AssetRef
{
    public AssetRef(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (!IsValidPath(path))
        {
            throw new ArgumentException($"'{path}' is not an asset path (Package.Group.Name).", nameof(path));
        }

        Path = path;
    }

    /// <summary>The full path, <c>Package.Group.Name</c>.</summary>
    public string Path { get; }

    /// <summary>The first segment.</summary>
    public string Package => Path[..Path.IndexOf('.', StringComparison.Ordinal)];

    /// <summary>The last segment.</summary>
    public string Name => Path[(Path.LastIndexOf('.') + 1)..];

    /// <summary>The segments between package and name, dot separated; empty for <c>Package.Name</c>.</summary>
    public string Group
    {
        get
        {
            int first = Path.IndexOf('.', StringComparison.Ordinal);
            int last = Path.LastIndexOf('.');
            return first == last ? string.Empty : Path[(first + 1)..last];
        }
    }

    /// <summary>True when the asset belongs to the map's own library (its package is the map id).</summary>
    public bool IsInMap(string mapId) => AsciiCaseComparer.AreEqual(Package, mapId);

    public override string ToString() => Path;

    /// <summary>At least two non-empty segments without whitespace (<c>^[^.\s]+(\.[^.\s]+)+$</c>).</summary>
    public static bool IsValidPath(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        int segments = 1;
        int length = 0;
        foreach (char c in path)
        {
            if (c == '.')
            {
                if (length == 0)
                {
                    return false;
                }

                segments++;
                length = 0;
            }
            else if (char.IsWhiteSpace(c))
            {
                return false;
            }
            else
            {
                length++;
            }
        }

        return segments >= 2 && length > 0;
    }
}

/// <summary>
/// Resolves asset references to loaded objects on first use and remembers them. The loader decides what an asset
/// becomes (a render mesh, a sound, a collision shape); paths match ignoring ASCII case.
/// </summary>
public sealed class AssetResolver<T>
    where T : class
{
    private readonly Func<AssetRef, T?> _load;
    private readonly Dictionary<string, T?> _cache = new(AsciiCaseComparer.Instance);

    public AssetResolver(Func<AssetRef, T?> load)
    {
        ArgumentNullException.ThrowIfNull(load);
        _load = load;
    }

    /// <summary>Number of distinct paths resolved so far, including ones that loaded as null.</summary>
    public int Count => _cache.Count;

    public T? Resolve(AssetRef? asset)
    {
        if (asset is null)
        {
            return null;
        }

        if (!_cache.TryGetValue(asset.Path, out T? value))
        {
            value = _load(asset);
            _cache[asset.Path] = value;
        }

        return value;
    }
}
