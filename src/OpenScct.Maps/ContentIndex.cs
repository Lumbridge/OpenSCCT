using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace OpenScct.Maps;

/// <summary>One downloadable archive of the content index: where, how big, its SHA-256.</summary>
public sealed record ContentDownload(string Url, long Size, string Sha256);

/// <summary>A shared pack (core, chars, audio): what converted maps need from outside their own folder.</summary>
public sealed record ContentSharedPack
{
    public required string Id { get; init; }

    public string Title { get; init; } = string.Empty;

    public string Version { get; init; } = string.Empty;

    /// <summary>The folders and files it installs under <c>shared/</c>.</summary>
    public IReadOnlyList<string> Contents { get; init; } = [];

    /// <summary>The hashes maps' <c>requires</c> name (pack name to SHA-256).</summary>
    public IReadOnlyDictionary<string, string> Hashes { get; init; } = new Dictionary<string, string>();

    public required ContentDownload Download { get; init; }
}

/// <summary>A pack a map was built against (its manifest's <c>requires</c>).</summary>
public sealed record ContentRequirement(string Pack, string Sha256);

/// <summary>A converted map of the content index.</summary>
public sealed record ContentMap
{
    /// <summary><c>&lt;category&gt;/&lt;code&gt;</c>.</summary>
    public required string Id { get; init; }

    /// <summary>The map's package name, its svmap folder and its name in lobbies.</summary>
    public required string Code { get; init; }

    public string Title { get; init; } = string.Empty;

    /// <summary>stock, original, community, enhanced, recovered or ports.</summary>
    public string Category { get; init; } = string.Empty;

    public string Version { get; init; } = string.Empty;

    /// <summary>Mode names (Story, DiskHunt, Deathmatch, MercDeathmatch, ...).</summary>
    public IReadOnlyList<string> Modes { get; init; } = [];

    /// <summary>MapCatalog.ContentHashOf of the pack: what lobbies and the hub compare.</summary>
    public string ContentHash { get; init; } = string.Empty;

    public string ManifestSha256 { get; init; } = string.Empty;

    public IReadOnlyList<ContentRequirement> Requires { get; init; } = [];

    /// <summary>Everything the game needs.</summary>
    public required ContentDownload Full { get; init; }

    /// <summary>manifest.json and sim/ only (servers); the full archive when the index has none.</summary>
    public required ContentDownload Sim { get; init; }

    public string DisplayTitle => Title.Length > 0 ? Title : Code;
}

/// <summary>
/// The OpenSCCT-Content index (docs/CONTENT-PACKS.md): the shared packs and the converted maps, each one a release
/// asset with its size and SHA-256.
/// </summary>
public sealed record ContentIndex
{
    public const string Format = "openscct.content";

    /// <summary>The index version this game reads.</summary>
    public const int FormatVersion = 1;

    /// <summary>The svmap major version this game loads.</summary>
    public const int SvMapMajor = 1;

    public DateTimeOffset Updated { get; init; }

    public string SvMapFormat { get; init; } = string.Empty;

    public IReadOnlyList<ContentSharedPack> Shared { get; init; } = [];

    public IReadOnlyList<ContentMap> Maps { get; init; } = [];

    /// <summary>Why the maps were all left out (a newer svmap format), else null.</summary>
    public string? Problem { get; init; }

    public static ContentIndex Empty { get; } = new();

    /// <summary>
    /// The map of <paramref name="code"/> (case-insensitive), or null. Two categories may hold the same code (a
    /// community map and its recovered version): the one whose <paramref name="contentHash"/> matches wins, else the
    /// first by <see cref="CategoryRank"/>, so a player and a hub asking for a code get the same map.
    /// </summary>
    public ContentMap? FindMap(string code, string? contentHash = null)
    {
        ContentMap? best = null;
        foreach (ContentMap m in Maps)
        {
            if (!string.Equals(m.Code, code, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (contentHash is { Length: > 0 } && string.Equals(m.ContentHash, contentHash, StringComparison.OrdinalIgnoreCase))
            {
                return m;
            }

            if (best is null || CategoryRank(m.Category) < CategoryRank(best.Category))
            {
                best = m;
            }
        }

        return best;
    }

    /// <summary>Which category's copy of a code wins: stock, original, enhanced, recovered, ports, then community.</summary>
    public static int CategoryRank(string category) => category switch
    {
        "stock" => 0,
        "original" => 1,
        "enhanced" => 2,
        "recovered" => 3,
        "ports" => 4,
        "community" => 5,
        _ => 6,
    };

    /// <summary>The maps a player can pick (one per code, with modes: not the front end's Entry and Lobby).</summary>
    public IEnumerable<ContentMap> PlayableMaps()
    {
        foreach (ContentMap m in Maps)
        {
            if (m.Modes.Count > 0 && ReferenceEquals(FindMap(m.Code), m))
            {
                yield return m;
            }
        }
    }

    public ContentSharedPack? FindShared(string id)
    {
        foreach (ContentSharedPack p in Shared)
        {
            if (string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase))
            {
                return p;
            }
        }

        return null;
    }

    /// <summary>
    /// Reads an index. Throws <see cref="InvalidDataException"/> for one this game cannot read (another format, a
    /// newer major version); a map or pack with a bad field is left out, not the whole index.
    /// </summary>
    public static ContentIndex Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        }
        catch (JsonException e)
        {
            throw new System.IO.InvalidDataException("The content index is not valid JSON: " + e.Message, e);
        }

        using (doc)
        {
            JsonElement root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || Str(root, "format") != Format)
            {
                throw new System.IO.InvalidDataException("This is not an OpenSCCT content index.");
            }

            int version = root.TryGetProperty("formatVersion", out JsonElement v) && v.TryGetInt32(out int fv) ? fv : 0;
            if (version > FormatVersion)
            {
                throw new System.IO.InvalidDataException("The content index is newer than this game: update OpenSCCT to download maps.");
            }

            string svmap = Str(root, "svmapFormat");
            string? problem = null;
            if (svmap.Length > 0 && MajorOf(svmap) is int major && major != SvMapMajor)
            {
                problem = $"The content index has maps in svmap {svmap}; this game reads svmap {SvMapMajor}.x. Update OpenSCCT.";
            }

            var shared = new List<ContentSharedPack>();
            foreach (JsonElement p in Array(root, "shared"))
            {
                if (Str(p, "id") is { Length: > 0 } id && Download(p, "download") is { } dl)
                {
                    shared.Add(new ContentSharedPack
                    {
                        Id = id, Title = Str(p, "title"), Version = Str(p, "version"), Contents = Strings(p, "contents"),
                        Hashes = Map(p, "hashes"), Download = dl,
                    });
                }
            }

            var maps = new List<ContentMap>();
            if (problem is null)
            {
                foreach (JsonElement m in Array(root, "maps"))
                {
                    if (Str(m, "code") is not { Length: > 0 } code || !IsSafeCode(code) || Download(m, "full") is not { } full)
                    {
                        continue;
                    }

                    var requires = new List<ContentRequirement>();
                    foreach (JsonElement r in Array(m, "requires"))
                    {
                        if (Str(r, "pack") is { Length: > 0 } pack)
                        {
                            requires.Add(new ContentRequirement(pack, Str(r, "sha256")));
                        }
                    }

                    maps.Add(new ContentMap
                    {
                        Id = Str(m, "id") is { Length: > 0 } id ? id : code,
                        Code = code,
                        Title = Str(m, "title"),
                        Category = Str(m, "category"),
                        Version = Str(m, "version"),
                        Modes = Strings(m, "modes"),
                        ContentHash = Str(m, "contentHash").ToLowerInvariant(),
                        ManifestSha256 = Str(m, "manifestSha256").ToLowerInvariant(),
                        Requires = requires,
                        Full = full,
                        Sim = Download(m, "sim") ?? full,
                    });
                }
            }

            return new ContentIndex
            {
                Updated = DateTimeOffset.TryParse(Str(root, "updated"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset at) ? at : default,
                SvMapFormat = svmap,
                Shared = shared,
                Maps = maps,
                Problem = problem,
            };
        }
    }

    /// <summary>A map code safe as a folder name (letters, digits, '_', '-', '.'; not "." or "..").</summary>
    public static bool IsSafeCode(string code)
    {
        if (code.Length is 0 or > 64 || code is "." or "..")
        {
            return false;
        }

        foreach (char c in code)
        {
            if (!(char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.'))
            {
                return false;
            }
        }

        return true;
    }

    private static int? MajorOf(string version)
    {
        int dot = version.IndexOf('.', StringComparison.Ordinal);
        return int.TryParse(dot < 0 ? version : version[..dot], NumberStyles.Integer, CultureInfo.InvariantCulture, out int m) ? m : null;
    }

    private static ContentDownload? Download(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out JsonElement d) || d.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        string url = Str(d, "url");
        string sha = Str(d, "sha256").ToLowerInvariant();
        long size = d.TryGetProperty("size", out JsonElement s) && s.TryGetInt64(out long n) ? n : -1;
        bool https = Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) && uri.Scheme == Uri.UriSchemeHttps;
        return https && size > 0 && sha.Length == 64 ? new ContentDownload(url, size, sha) : null;
    }

    private static IEnumerable<JsonElement> Array(JsonElement e, string name) =>
        e.TryGetProperty(name, out JsonElement a) && a.ValueKind == JsonValueKind.Array ? a.EnumerateArray() : [];

    private static string Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? string.Empty : string.Empty;

    private static List<string> Strings(JsonElement e, string name)
    {
        var list = new List<string>();
        foreach (JsonElement s in Array(e, name))
        {
            if (s.ValueKind == JsonValueKind.String && s.GetString() is { Length: > 0 } text)
            {
                list.Add(text);
            }
        }

        return list;
    }

    private static Dictionary<string, string> Map(JsonElement e, string name)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (e.TryGetProperty(name, out JsonElement o) && o.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty p in o.EnumerateObject())
            {
                if (p.Value.ValueKind == JsonValueKind.String)
                {
                    map[p.Name] = p.Value.GetString() ?? string.Empty;
                }
            }
        }

        return map;
    }
}
