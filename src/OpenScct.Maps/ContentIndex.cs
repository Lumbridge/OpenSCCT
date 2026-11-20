using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace OpenScct.Maps;

/// <summary>One downloadable archive of the content index: where, how big, its SHA-256.</summary>
public sealed record ContentDownload(string Url, long Size, string Sha256);

/// <summary>Optional Spy and Merc navigation for one simulation revision and the map's initial activation mask.</summary>
public sealed record ContentBotNavigation(string SimHash, int FormatVersion, byte ActivationMask, ContentDownload Download)
{
    internal string? ApprovedSha256 { get; init; }
}

/// <summary>The catalogue publisher's redistribution review for the exact community archives.</summary>
public sealed record ContentDistribution(string Kind, string Credits, string License, string Evidence, string FullSha256,
    string SimSha256, string NavigationSha256 = "")
{
    internal static bool Matches(string? approved, string actual) =>
        ContentIndex.IsSha256(actual) && string.Equals(approved, actual, StringComparison.OrdinalIgnoreCase);
}

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

    /// <summary>Where the pack was converted from (<c>source.kind</c>: install, repo or release), empty when not given.</summary>
    public string SourceKind { get; init; } = string.Empty;

    /// <summary>Mode names (Story, DiskHunt, Deathmatch, MercDeathmatch, ...).</summary>
    public IReadOnlyList<string> Modes { get; init; } = [];

    /// <summary>MapCatalog.ContentHashOf of the pack: what lobbies and the hub compare.</summary>
    public string ContentHash { get; init; } = string.Empty;

    public string ManifestSha256 { get; init; } = string.Empty;

    public IReadOnlyList<ContentRequirement> Requires { get; init; } = [];

    /// <summary>Everything the game needs.</summary>
    public ContentDownload? Full { get; init; }

    /// <summary>manifest.json and sim/ only (servers); the full archive when the index has none.</summary>
    public ContentDownload? Sim { get; init; }

    public ContentBotNavigation? BotNavigation { get; init; }

    public ContentDistribution? Distribution { get; init; }

    public SteamMapRecipe? SteamRecipe { get; init; }

    public bool CanDistribute => !SteamVersusMaps.Contains(Code) && !string.Equals(SourceKind, "install", StringComparison.OrdinalIgnoreCase) &&
        (SteamRecipe is { } recipe
            ? recipe.Code == Code && Full is null && Sim is null && ContentHash.Length == 16 && ContentHash.All(char.IsAsciiHexDigit)
            : Full is not null && Sim is not null && Distribution is { Kind: "community-authored" } review &&
              !string.IsNullOrWhiteSpace(review.Credits) && !string.IsNullOrWhiteSpace(review.License) &&
              Uri.TryCreate(review.Evidence, UriKind.Absolute, out var evidence) && evidence.Scheme == Uri.UriSchemeHttps &&
              ContentDistribution.Matches(review.FullSha256, Full.Sha256) && ContentDistribution.Matches(review.SimSha256, Sim.Sha256));

    public long DownloadSize(ContentKind kind = ContentKind.Full) =>
        SteamRecipe?.DownloadSize ?? (kind == ContentKind.Sim ? Sim : Full)?.Size ?? 0;

    public string FullInstallHash => SteamRecipe?.Fingerprint ?? Full?.Sha256 ?? string.Empty;

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

    /// <summary>The store could not reach the catalogue and returned a previously downloaded copy.</summary>
    public bool FromOfflineCache { get; init; }

    public string SvMapFormat { get; init; } = string.Empty;

    public IReadOnlyList<ContentSharedPack> Shared { get; init; } = [];

    public IReadOnlyList<ContentMap> Maps { get; init; } = [];

    /// <summary>Why the maps were all left out (a newer svmap format), else null.</summary>
    public string? Problem { get; init; }

    public static ContentIndex Empty { get; } = new();

    /// <summary>The reviewed public map feed. Historical shared packs and unreviewed archives are local-use only.</summary>
    public ContentIndex PublicDownloads() => this with
    {
        Shared = [],
        Maps = Maps.Where(map => map.CanDistribute).Select(map => map with
        {
            BotNavigation = map.SteamRecipe is null && map.BotNavigation is { } nav && ContentDistribution.Matches(map.Distribution?.NavigationSha256, nav.Download.Sha256)
                ? nav with { ApprovedSha256 = nav.Download.Sha256 } : null,
        }).ToArray(),
    };

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

    /// <summary>A navigation download must match the full simulation hash, including when map codes are reused.</summary>
    public ContentBotNavigation? FindBotNavigation(string code, string simHash, int formatVersion)
    {
        foreach (ContentMap map in Maps)
        {
            if (map.BotNavigation is { } nav && nav.FormatVersion == formatVersion &&
                string.Equals(map.Code, code, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(nav.SimHash, simHash, StringComparison.OrdinalIgnoreCase))
            {
                return nav;
            }
        }

        return null;
    }

    /// <summary>
    /// Reads an index. Throws <see cref="InvalidDataException"/> for one this game cannot read (another format, a
    /// newer major version); a map or pack with a bad field is left out, not the whole index.
    /// </summary>
    public static ContentIndex Parse(string json, Uri? downloadOrigin = null)
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
                if (Str(p, "id") is { Length: > 0 } id && Download(p, "download", downloadOrigin) is { } dl)
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
                    if (Str(m, "code") is not { Length: > 0 } code || !IsSafeCode(code))
                    {
                        continue;
                    }

                    var full = Download(m, "full", downloadOrigin);
                    SteamMapRecipe? recipe = null;
                    if (m.TryGetProperty("steamRecipe", out var recipeData))
                    {
                        recipe = SteamMapRecipe.Read(recipeData, code, Str(m, "contentHash").ToLowerInvariant());
                        if (recipe is null || m.TryGetProperty("full", out _) || m.TryGetProperty("sim", out _)) continue;
                    }
                    else if (full is null) continue;

                    var requires = new List<ContentRequirement>();
                    foreach (JsonElement r in Array(m, "requires"))
                    {
                        if (Str(r, "pack") is { Length: > 0 } pack)
                        {
                            requires.Add(new ContentRequirement(pack, Str(r, "sha256")));
                        }
                    }

                    string contentHash = Str(m, "contentHash").ToLowerInvariant();
                    maps.Add(new ContentMap
                    {
                        Id = Str(m, "id") is { Length: > 0 } id ? id : code,
                        Code = code,
                        Title = Str(m, "title"),
                        Category = Str(m, "category"),
                        Version = Str(m, "version"),
                        SourceKind = m.TryGetProperty("source", out JsonElement source) ? Str(source, "kind") : string.Empty,
                        Modes = Strings(m, "modes"),
                        ContentHash = contentHash,
                        ManifestSha256 = Str(m, "manifestSha256").ToLowerInvariant(),
                        Requires = requires,
                        SteamRecipe = recipe,
                        Full = full,
                        Sim = Download(m, "sim", downloadOrigin) ?? full,
                        BotNavigation = ReadBotNavigation(m, contentHash, downloadOrigin),
                        Distribution = m.TryGetProperty("distribution", out var d) && d.ValueKind == JsonValueKind.Object
                            ? new ContentDistribution(Str(d, "kind"), Str(d, "credits"), Str(d, "license"), Str(d, "evidence"),
                                Str(d, "fullSha256"), Str(d, "simSha256"), Str(d, "navigationSha256")) : null,
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

    internal static bool IsSha256(string value) => value.Length == 64 && value.AsSpan().IndexOfAnyExcept("0123456789abcdef") < 0;

    private static ContentBotNavigation? ReadBotNavigation(JsonElement map, string contentHash, Uri? downloadOrigin)
    {
        if (!map.TryGetProperty("botNavigation", out JsonElement nav) || nav.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        string hash = Str(nav, "simHash").ToLowerInvariant();
        return IsSha256(hash) && contentHash == hash[..16] &&
            nav.TryGetProperty("formatVersion", out JsonElement v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int version) && version > 0 &&
            nav.TryGetProperty("activationMask", out JsonElement a) && a.ValueKind == JsonValueKind.Number && a.TryGetByte(out byte mask) &&
            Download(nav, "download", downloadOrigin) is { } download
            ? new ContentBotNavigation(hash, version, mask, download) : null;
    }

    private static int? MajorOf(string version)
    {
        int dot = version.IndexOf('.', StringComparison.Ordinal);
        return int.TryParse(dot < 0 ? version : version[..dot], NumberStyles.Integer, CultureInfo.InvariantCulture, out int m) ? m : null;
    }

    private static ContentDownload? Download(JsonElement e, string name, Uri? downloadOrigin)
    {
        if (!e.TryGetProperty(name, out JsonElement d) || d.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        string url = Str(d, "url");
        // Browser mirrors use paths on their own HTTPS origin. Desktop indexes remain HTTPS-only as before.
        // The only plaintext exception is an explicitly supplied loopback origin for the local browser lab.
        bool local = false;
        if (downloadOrigin is not null && url.StartsWith("/", StringComparison.Ordinal) && !url.StartsWith("//", StringComparison.Ordinal) &&
            Uri.TryCreate(downloadOrigin, url, out Uri? resolved) && resolved.GetLeftPart(UriPartial.Authority) == downloadOrigin.GetLeftPart(UriPartial.Authority))
        {
            url = resolved.AbsoluteUri;
            local = resolved.Scheme == Uri.UriSchemeHttp && resolved.IsLoopback;
        }
        string sha = Str(d, "sha256").ToLowerInvariant();
        long size = d.TryGetProperty("size", out JsonElement s) && s.ValueKind == JsonValueKind.Number && s.TryGetInt64(out long n) ? n : -1;
        bool https = Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) && uri.Scheme == Uri.UriSchemeHttps;
        return (https || local) && size > 0 && IsSha256(sha) ? new ContentDownload(url, size, sha) : null;
    }

    private static IEnumerable<JsonElement> Array(JsonElement e, string name) =>
        e.TryGetProperty(name, out JsonElement a) && a.ValueKind == JsonValueKind.Array ? a.EnumerateArray() : [];

    internal static string Str(JsonElement e, string name) =>
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
