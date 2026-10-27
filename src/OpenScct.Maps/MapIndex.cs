using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace OpenScct.Maps;

/// <summary>
/// One file of a downloadable map: where it comes from in the SCCT-Maps repository (a path at the catalogue's commit,
/// or <c>releases/download/&lt;tag&gt;/&lt;name&gt;</c>), where it goes in the downloaded maps folder
/// (<c>Packages/Maps/ShipD.sdc</c>), its Git blob SHA-1 and its size.
/// </summary>
public sealed record MapFile(
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("path")] string Destination,
    [property: JsonPropertyName("hash")] string Hash,
    [property: JsonPropertyName("size")] long Size);

/// <summary>
/// A map players can download: <see cref="Id"/> is the repository's (<c>original/ShipD</c>, <c>community/Eden</c>,
/// <c>ports/splinter-cell-double-agent/blackwing</c>), <see cref="Codes"/> the map codes it adds (its
/// <c>Packages/Maps/*.sdc</c>). Only the files the game plays with are listed: editor maps (MapsEd) are left out.
/// </summary>
public sealed record MapPackage(
    string Id,
    string Name,
    string Category,
    string Version,
    string Game,
    string Commit,
    string NotesPath,
    IReadOnlyList<MapFile> Files)
{
    public IReadOnlyList<string> Codes => Files
        .Where(f => f.Destination.StartsWith("Packages/Maps/", StringComparison.OrdinalIgnoreCase))
        .Select(f => Path.GetFileNameWithoutExtension(f.Destination))
        .OrderBy(c => c, StringComparer.OrdinalIgnoreCase)
        .ToList();

    public long Bytes => Files.Sum(f => f.Size);

    /// <summary>Changes whenever any file of the map changes: the installed map is out of date when it differs.</summary>
    public string Fingerprint => MapPaths.Fingerprint(Files);
}

/// <summary>The maps of the SCCT-Maps repository at one commit.</summary>
public sealed record MapIndex(string Commit, IReadOnlyList<MapPackage> Maps)
{
    /// <summary>
    /// Every texture, mesh, sound and animation package in the repository's folders, by package name (ignoring
    /// case): the files a downloaded map can take a package it needs from when the player's install lacks it (a map
    /// made for Enhanced SCCT Versus uses packages that other maps in the repository carry). One file per
    /// destination: the copy most folders agree on.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<MapFile>> Packages { get; init; } =
        new Dictionary<string, IReadOnlyList<MapFile>>(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Reads the SCCT-Maps repository the way the SCCT Map Manager does (its docs/publishing.md):
/// <list type="bullet">
/// <item><c>release/&lt;Package&gt;/&lt;version&gt;/Packages/Maps/*.sdc</c>: original maps, newest version only, named by <c>release/names.json</c>;</item>
/// <item><c>community|enhanced|recovered|ports/&lt;name&gt;/Packages/Maps/*.sdc</c> and <c>ports/&lt;game&gt;/&lt;map&gt;/Packages/...</c>;
/// community maps also take <c>community/_shared</c>;</item>
/// <item><c>asset-catalog.json</c>: ports whose packages are release assets (they replace a tree entry of the same id).</item>
/// </list>
/// Editor asset packs and entries without a playable map (editor-only scenery) are not offered.
/// </summary>
public static class MapIndexParser
{
    public const string Original = "Original";

    private sealed record Blob(string Path, string Hash, long Size);

    private sealed record Root(string Id, string Name, string Category, string Version, string Game);

    /// <summary>The maps of a GitHub git tree listing (<c>git/trees/&lt;ref&gt;?recursive=1</c>).</summary>
    public static MapIndex ParseTree(string json)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;
        if (root.TryGetProperty("truncated", out JsonElement truncated) && truncated.ValueKind == JsonValueKind.True)
        {
            throw new InvalidDataException("GitHub returned an incomplete map list. Try again later.");
        }

        string commit = root.GetProperty("sha").GetString() ?? string.Empty;
        MapPaths.ValidateHash(commit);
        var blobs = root.GetProperty("tree").EnumerateArray()
            .Where(x => x.GetProperty("type").GetString() == "blob")
            .Select(x => new Blob(x.GetProperty("path").GetString()!, x.GetProperty("sha").GetString()!,
                x.TryGetProperty("size", out JsonElement s) ? s.GetInt64() : 0))
            .ToList();

        // Each folder holding Packages/Maps/*.sdc is one map (one version of it for release/).
        var roots = new Dictionary<string, Root>(StringComparer.Ordinal);
        foreach (Blob b in blobs)
        {
            string[] p = b.Path.Split('/');
            if (p.Length < 5 || !b.Path.EndsWith(".sdc", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (p[0] == "release" && p.Length == 6 && p[3] == "Packages" && p[4] == "Maps")
            {
                roots[string.Join('/', p.Take(3))] = new Root("original/" + p[1], p[1], Original, p[2], string.Empty);
            }
            else if (p[0] == "ports" && p.Length == 6 && p[3] == "Packages" && p[4] == "Maps" && p[2] != "_shared")
            {
                string id = string.Join('/', p.Take(3));
                roots[id] = new Root(id, Humanize(p[2]), "Ports", "Latest", Humanize(p[1]));
            }
            else if (p.Length == 5 && p[2] == "Packages" && p[3] == "Maps" && p[1] != "_shared")
            {
                string? category = p[0] switch
                {
                    "community" => "Community",
                    "enhanced" => "Enhanced",
                    "recovered" => "Recovered",
                    "ports" => "Ports",
                    _ => null,
                };
                if (category is not null)
                {
                    roots[p[0] + "/" + p[1]] = new Root(p[0] + "/" + p[1], p[1], category, "Latest",
                        p[0] == "ports" ? Humanize(p[1]) : string.Empty);
                }
            }
        }

        var maps = new List<MapPackage>();
        foreach (IGrouping<string, KeyValuePair<string, Root>> group in roots.GroupBy(r => r.Value.Id))
        {
            // Newest version first (v1.10 after v1.9).
            foreach (KeyValuePair<string, Root> r in group.OrderByDescending(r => VersionKey(r.Value.Version), StringComparer.Ordinal))
            {
                if (Build(r.Key, r.Value) is { } map)
                {
                    maps.Add(map);
                    break;
                }
            }
        }

        return new MapIndex(commit, maps) { Packages = PackagePool(blobs.Select(b => (b.Path, b.Hash, b.Size))) };

        MapPackage? Build(string folder, Root info)
        {
            var files = new Dictionary<string, MapFile>(StringComparer.OrdinalIgnoreCase);
            void Include(string prefix)
            {
                foreach (Blob b in blobs.Where(b => b.Path.StartsWith(prefix + "/", StringComparison.Ordinal)))
                {
                    string dest = b.Path[(prefix.Length + 1)..];
                    if (!MapPaths.IsPlayable(dest))
                    {
                        continue;
                    }

                    MapPaths.ValidateRelative(b.Path);
                    MapPaths.ValidateHash(b.Hash);
                    MapPaths.ValidateSize(b.Size);
                    files.TryAdd(dest, new MapFile(b.Path, dest, b.Hash, b.Size));
                }
            }

            Include(folder);
            if (info.Category == "Community")
            {
                Include("community/_shared");
            }

            if (!files.Keys.Any(k => k.StartsWith("Packages/Maps/", StringComparison.OrdinalIgnoreCase)))
            {
                return null;
            }

            string notes = blobs.FirstOrDefault(b => b.Path.Equals(folder + "/README.md", StringComparison.OrdinalIgnoreCase))?.Path
                ?? folder + "/README.md";
            return new MapPackage(info.Id, info.Name, info.Category, info.Version, info.Game, commit, notes,
                files.Values.OrderBy(f => f.Destination, StringComparer.OrdinalIgnoreCase).ToList());
        }
    }

    /// <summary>
    /// The texture, mesh, sound and animation packages under the map folders of a tree listing (path, blob id,
    /// size), by package name. Editor folders (MapsEd), map files and <c>assets/</c> are left out. Where folders hold
    /// different copies of one file, the copy most of them hold is used (then the first path in order).
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<MapFile>> PackagePool(IEnumerable<(string Path, string Hash, long Size)> blobs)
    {
        string[] tops = ["release", "community", "enhanced", "recovered", "ports", "stock"];
        var candidates = new List<MapFile>();
        foreach ((string path, string hash, long size) in blobs)
        {
            string[] p = path.Split('/');
            int at = Array.LastIndexOf(p, "Packages");
            if (p.Length < 4 || !tops.Contains(p[0]) || at < 1 || at != p.Length - 3 || p[at + 1] == "Maps")
            {
                continue;
            }

            string dest = string.Join('/', p.Skip(at));
            if (!MapPaths.IsPlayable(dest) || size <= 0)
            {
                continue;
            }

            try
            {
                MapPaths.ValidateRelative(path);
                MapPaths.ValidateHash(hash);
                MapPaths.ValidateSize(size);
            }
            catch (InvalidDataException)
            {
                continue;                                   // a file this player cannot take is left out
            }

            candidates.Add(new MapFile(path, dest, hash, size));
        }

        var pool = new Dictionary<string, IReadOnlyList<MapFile>>(StringComparer.OrdinalIgnoreCase);
        foreach (IGrouping<string, MapFile> byName in candidates.GroupBy(f => Path.GetFileNameWithoutExtension(f.Destination), StringComparer.OrdinalIgnoreCase))
        {
            pool[byName.Key] = byName
                .GroupBy(f => f.Destination, StringComparer.OrdinalIgnoreCase)
                .Select(byDest => byDest
                    .GroupBy(f => f.Hash, StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(h => h.Count())
                    .ThenBy(h => h.Select(f => f.Source).Min(StringComparer.Ordinal), StringComparer.Ordinal)
                    .First()
                    .OrderBy(f => f.Source, StringComparer.Ordinal)
                    .First())
                .OrderBy(f => f.Destination, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        return pool;
    }

    /// <summary>
    /// The ports of <c>asset-catalog.json</c> (packages attached to versioned releases), newest version of each.
    /// Editor asset packs (<c>assets/</c>) and entries without a playable map are skipped.
    /// </summary>
    public static IReadOnlyList<MapPackage> ParseReleased(string json, string commit)
    {
        MapPaths.ValidateHash(commit);
        using JsonDocument doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("The map release list is not a list.");
        }

        var all = new List<MapPackage>();
        foreach (JsonElement e in doc.RootElement.EnumerateArray())
        {
            string id = Text(e, "Id");
            string version = Text(e, "Version");
            if (!id.StartsWith("ports/", StringComparison.Ordinal) || !Regex.IsMatch(version, @"^v\d+\.\d+\.\d+$"))
            {
                continue;
            }

            MapPaths.ValidateRelative(id);
            var files = new List<MapFile>();
            if (e.TryGetProperty("Files", out JsonElement list) && list.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement f in list.EnumerateArray())
                {
                    string source = Text(f, "Source");
                    string dest = Text(f, "Destination");
                    string hash = Text(f, "Hash");
                    long size = f.TryGetProperty("Size", out JsonElement s) && s.ValueKind == JsonValueKind.Number ? s.GetInt64() : -1;
                    MapPaths.ValidateRelative(source);
                    string[] sp = source.Split('/');
                    if (sp.Length != 4 || sp[0] != "releases" || sp[1] != "download" || !sp[2].EndsWith("-" + version, StringComparison.Ordinal))
                    {
                        throw new InvalidDataException("Invalid map download: " + source);
                    }

                    if (!MapPaths.IsPlayable(dest))
                    {
                        continue;
                    }

                    MapPaths.ValidateRelative(dest);
                    MapPaths.ValidateHash(hash);
                    MapPaths.ValidateSize(size);
                    files.Add(new MapFile(source, dest, hash, size));
                }
            }

            if (!files.Any(f => f.Destination.StartsWith("Packages/Maps/", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            string notes = Text(e, "NotesPath");
            all.Add(new MapPackage(id, Text(e, "Name"), "Ports", version, Text(e, "Game"), commit,
                notes.Length > 0 ? notes : id + "/README.md", files.OrderBy(f => f.Destination, StringComparer.OrdinalIgnoreCase).ToList()));
        }

        return all.GroupBy(m => m.Id)
            .Select(g => g.OrderByDescending(m => Version.Parse(m.Version[1..])).First())
            .ToList();
    }

    /// <summary>The tree's maps with the released ports added (a released port replaces the tree entry with its id).</summary>
    public static MapIndex Merge(MapIndex tree, IReadOnlyList<MapPackage> released)
    {
        var ids = released.Select(r => r.Id).ToHashSet(StringComparer.Ordinal);
        return tree with { Maps = tree.Maps.Where(m => !ids.Contains(m.Id)).Concat(released).ToList() };
    }

    /// <summary>
    /// Display names of the original maps from <c>release/names.json</c> (package folder -> name, up to 60
    /// characters of plain text); unusable entries are skipped.
    /// </summary>
    public static IReadOnlyDictionary<string, string> ParseNames(string json)
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        using JsonDocument doc = JsonDocument.Parse(json.TrimStart('﻿'));
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
        {
            return names;
        }

        foreach (JsonProperty p in doc.RootElement.EnumerateObject())
        {
            if (names.Count >= 1000 || p.Value.ValueKind != JsonValueKind.String || !Regex.IsMatch(p.Name, "^[A-Za-z0-9_-]{1,64}$"))
            {
                continue;
            }

            string name = p.Value.GetString()!.Trim();
            if (name.Length is > 0 and <= 60 && !name.Any(char.IsControl))
            {
                names[p.Name] = name;
            }
        }

        return names;
    }

    public static MapIndex ApplyNames(MapIndex catalog, IReadOnlyDictionary<string, string> names) => catalog with
    {
        Maps = catalog.Maps.Select(m => m.Category == Original && names.TryGetValue(m.Id["original/".Length..], out string? n)
            ? m with { Name = n }
            : m).ToList(),
    };

    /// <summary>The catalogue in the order the Maps window lists it: original maps first, then by name.</summary>
    public static IReadOnlyList<MapPackage> Ordered(IEnumerable<MapPackage> maps) => maps
        .OrderByDescending(m => m.Category == Original)
        .ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
        .ThenBy(m => m.Category, StringComparer.OrdinalIgnoreCase)
        .ToList();

    private static string Text(JsonElement e, string name) =>
        e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? string.Empty : string.Empty;

    private static string VersionKey(string value) => Regex.Replace(value.TrimStart('v', 'V'), @"\d+", m => m.Value.PadLeft(10, '0'));

    private static string Humanize(string value) => string.Join(' ', value.Split('-', StringSplitOptions.RemoveEmptyEntries)
        .Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
}
