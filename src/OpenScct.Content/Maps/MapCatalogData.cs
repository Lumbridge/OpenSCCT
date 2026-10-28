using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace OpenScct.Content.Maps;

/// <summary>One of Reloaded's map playlists as <c>catalog.json</c> stores it.</summary>
public sealed record MapPlaylistData(string Name, string Description, string GameMode, IReadOnlyList<string> Maps);

/// <summary>
/// <c>shared/maps/catalog.json</c>, written by <c>svmbuild maps-catalog</c> (docs/MAPS.md "The map catalog"): the
/// original's host map list order, the tutorial maps per side, Reloaded's playlists, map names and variant words, and
/// the pictures each map has (paths relative to the catalog folder).
/// </summary>
public sealed class MapCatalogData
{
    public const string FileName = "catalog.json";

    private MapCatalogData(string folder)
    {
        Folder = folder;
    }

    /// <summary>An empty catalog (no catalog folder installed): no playlists, names or images.</summary>
    public static MapCatalogData Empty { get; } = new(string.Empty);

    /// <summary>The folder holding catalog.json and images/ (empty for <see cref="Empty"/>).</summary>
    public string Folder { get; }

    /// <summary>Map codes in the original's host list order (GUIController.GetMapList: no menu or training maps).</summary>
    public IReadOnlyList<string> OriginalOrder { get; private init; } = [];

    /// <summary>The tutorial maps of the spies (TSP) in order.</summary>
    public IReadOnlyList<string> TrainingSpy { get; private init; } = [];

    /// <summary>The tutorial maps of the mercs (TME) in order.</summary>
    public IReadOnlyList<string> TrainingMerc { get; private init; } = [];

    /// <summary>The front end maps (Entry, Lobby).</summary>
    public IReadOnlyList<string> MenuMaps { get; private init; } = [];

    public IReadOnlyList<MapPlaylistData> Playlists { get; private init; } = [];

    /// <summary>Reloaded's map name table (code to name).</summary>
    public IReadOnlyDictionary<string, string> Names { get; private init; } = new Dictionary<string, string>();

    /// <summary>Words that mark a map as a version of another (Enhanced, Enh, Phoenix's...), in Reloaded's order.</summary>
    public IReadOnlyList<string> VariantWords { get; private init; } = [];

    private IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Images { get; init; } =
        new Dictionary<string, IReadOnlyDictionary<string, string>>();

    /// <summary>The full path of a map's picture (<c>menu</c>, <c>loading-spy</c>, <c>loading-merc</c>, <c>briefing</c>), or null.</summary>
    public string? Image(string map, string kind)
    {
        if (Images.TryGetValue(map, out IReadOnlyDictionary<string, string>? kinds) && kinds.TryGetValue(kind, out string? rel))
        {
            string path = Path.GetFullPath(Path.Combine(Folder, rel));
            return File.Exists(path) ? path : null;
        }

        return null;
    }

    /// <summary>Reads <c>catalog.json</c> in <paramref name="folder"/>; <see cref="Empty"/> when there is none.</summary>
    public static MapCatalogData Load(string? folder)
    {
        if (string.IsNullOrEmpty(folder) || !File.Exists(Path.Combine(folder, FileName)))
        {
            return Empty;
        }

        return Parse(File.ReadAllBytes(Path.Combine(folder, FileName)), folder);
    }

    /// <summary>The file a downloaded map carries for itself (docs/CONTENT-PACKS.md "A map's catalog entry").</summary>
    public const string EntryFile = "catalog/entry.json";

    /// <summary>
    /// The catalog with the entries downloaded maps bring (<c>&lt;map&gt;/catalog/entry.json</c>: a name and pictures)
    /// for every map the shared catalog has no name or pictures for. The shared catalog's own entries win.
    /// </summary>
    public MapCatalogData WithMapEntries(IEnumerable<string> mapFolders)
    {
        ArgumentNullException.ThrowIfNull(mapFolders);
        Dictionary<string, string>? names = null;
        Dictionary<string, IReadOnlyDictionary<string, string>>? images = null;
        foreach (string folder in mapFolders)
        {
            string file = Path.Combine(folder, EntryFile);
            if (!File.Exists(file))
            {
                continue;
            }

            try
            {
                using JsonDocument doc = JsonDocument.Parse(File.ReadAllBytes(file));
                JsonElement root = doc.RootElement;
                string code = Str(root, "code") is { Length: > 0 } c ? c : Path.GetFileName(folder);
                if (Str(root, "name") is { Length: > 0 } name && !Names.ContainsKey(code))
                {
                    names ??= new Dictionary<string, string>(Names, StringComparer.OrdinalIgnoreCase);
                    names.TryAdd(code, name);
                }

                if (!Images.ContainsKey(code) && root.TryGetProperty("images", out JsonElement im) && im.ValueKind == JsonValueKind.Object)
                {
                    var kinds = new Dictionary<string, string>(StringComparer.Ordinal);
                    string catalogDir = Path.GetFullPath(Path.Combine(folder, "catalog")) + Path.DirectorySeparatorChar;
                    foreach (JsonProperty kind in im.EnumerateObject())
                    {
                        // Absolute, so Image() finds it whatever the shared catalog's folder; never outside catalog/.
                        string path = Path.GetFullPath(Path.Combine(catalogDir, kind.Value.GetString() ?? string.Empty));
                        if (path.StartsWith(catalogDir, StringComparison.OrdinalIgnoreCase))
                        {
                            kinds[kind.Name] = path;
                        }
                    }

                    images ??= new Dictionary<string, IReadOnlyDictionary<string, string>>(Images, StringComparer.OrdinalIgnoreCase);
                    images.TryAdd(code, kinds);
                }
            }
            catch (Exception e) when (e is JsonException or IOException or InvalidOperationException)
            {
                // A broken entry only loses its name and pictures.
            }
        }

        if (names is null && images is null)
        {
            return this;
        }

        return new MapCatalogData(Folder)
        {
            OriginalOrder = OriginalOrder, TrainingSpy = TrainingSpy, TrainingMerc = TrainingMerc, MenuMaps = MenuMaps,
            Playlists = Playlists, Names = names ?? (IReadOnlyDictionary<string, string>)Names, VariantWords = VariantWords,
            Images = images ?? (IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>)Images,
        };
    }

    /// <summary>Parses a catalog document whose images are relative to <paramref name="folder"/>.</summary>
    public static MapCatalogData Parse(ReadOnlySpan<byte> utf8, string folder)
    {
        if (utf8.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
        {
            utf8 = utf8[3..];
        }

        var reader = new Utf8JsonReader(utf8);
        using JsonDocument doc = JsonDocument.ParseValue(ref reader);
        JsonElement root = doc.RootElement;
        if (!root.TryGetProperty("format", out JsonElement format) || format.GetString() != "svm-map-catalog")
        {
            throw new SvMapFormatException(Path.Combine(folder, FileName), "not a map catalog");
        }

        var playlists = new List<MapPlaylistData>();
        foreach (JsonElement p in Array(root, "playlists"))
        {
            playlists.Add(new MapPlaylistData(Str(p, "name"), Str(p, "description"), Str(p, "gameMode"), Strings(p, "maps")));
        }

        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (root.TryGetProperty("names", out JsonElement n) && n.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty prop in n.EnumerateObject())
            {
                names[prop.Name] = prop.Value.GetString() ?? string.Empty;
            }
        }

        var images = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        if (root.TryGetProperty("images", out JsonElement im) && im.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty map in im.EnumerateObject())
            {
                var kinds = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (JsonProperty kind in map.Value.EnumerateObject())
                {
                    kinds[kind.Name] = kind.Value.GetString() ?? string.Empty;
                }

                images[map.Name] = kinds;
            }
        }

        IReadOnlyList<string> spy = [];
        IReadOnlyList<string> merc = [];
        if (root.TryGetProperty("training", out JsonElement t) && t.ValueKind == JsonValueKind.Object)
        {
            spy = Strings(t, "spy");
            merc = Strings(t, "merc");
        }

        return new MapCatalogData(folder)
        {
            OriginalOrder = Strings(root, "originalOrder"),
            TrainingSpy = spy,
            TrainingMerc = merc,
            MenuMaps = Strings(root, "menuMaps"),
            Playlists = playlists,
            Names = names,
            VariantWords = Strings(root, "variantWords"),
            Images = images,
        };
    }

    private static IEnumerable<JsonElement> Array(JsonElement obj, string key) =>
        obj.TryGetProperty(key, out JsonElement a) && a.ValueKind == JsonValueKind.Array ? a.EnumerateArray() : [];

    private static string Str(JsonElement obj, string key) =>
        obj.TryGetProperty(key, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? string.Empty : string.Empty;

    private static List<string> Strings(JsonElement obj, string key)
    {
        var list = new List<string>();
        if (obj.TryGetProperty(key, out JsonElement a) && a.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement e in a.EnumerateArray())
            {
                if (e.ValueKind == JsonValueKind.String)
                {
                    list.Add(e.GetString()!);
                }
            }
        }

        return list;
    }
}
