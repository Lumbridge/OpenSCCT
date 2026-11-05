using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using OpenScct.Core;

namespace OpenScct.Content.Maps;

/// <summary>A numbered place of the briefing plan and the strategic map (<c>ui/identity.json</c> <c>places</c>).</summary>
/// <param name="Floor">The index in <c>LevelInfo.MapFloors</c> the label belongs to, or null to pick it by <paramref name="At"/>'s Z.</param>
public sealed record MapPlace(int Number, string Name, FVector At, int? Floor);

/// <summary>The map's dossier: when and where it is set and one line about it (<c>SMapSettings.LegendMap</c>'s lines).</summary>
public sealed record MapDossier(string When, string Where, string Summary);

/// <summary>
/// The pictures a map carries in <c>ui/pictures/</c>, as full paths (only files that exist inside that folder, are
/// PNG or JPEG by name and are at most <see cref="MapIdentity.MaxPictureBytes"/>).
/// </summary>
public sealed class MapPictures
{
    public static readonly MapPictures None = new();

    public string? Menu { get; init; }

    public IReadOnlyList<string> LoadingSpy { get; init; } = [];

    public IReadOnlyList<string> LoadingMerc { get; init; } = [];

    public string? Briefing { get; init; }

    public string? Banner { get; init; }

    /// <summary>True when the briefing plan was drawn through <c>LevelInfo.SnapshotCamera</c>, so places can be marked on it.</summary>
    public bool BriefingThroughSnapshot { get; init; }

    /// <summary>Slots whose picture a tool made rather than an artist (<c>menu</c>, <c>briefing</c>...).</summary>
    public IReadOnlyList<string> Generated { get; init; } = [];
}

/// <summary>
/// <c>ui/identity.json</c> (SVMap 1.2, spec/svmap/1.2/README.md): what a map says about itself for the menus. Every
/// field is optional and reading never throws: a missing or broken file gives <see cref="Empty"/>, and a field that
/// breaks the spec's rules is left out. The file comes from downloaded maps, so text is capped and pictures are only
/// taken from inside <c>ui/pictures/</c>.
/// </summary>
public sealed class MapIdentity
{
    public const string RelativePath = "ui/identity.json";
    public const string PictureFolder = "ui/pictures";
    public const int MaxNameLength = 48;
    public const int MaxDescriptionLength = 300;
    public const int MaxPictureBytes = 4 * 1024 * 1024;

    /// <summary>The most players a map may recommend (the spec's limit, and the largest lobby).</summary>
    public const int MaxPlayers = 24;

    public static readonly MapIdentity Empty = new();

    private MapIdentity()
    {
    }

    /// <summary>True when the map has an identity file that parsed.</summary>
    public bool Exists { get; private init; }

    /// <summary>The name menus show, mixed case, with its variant word ("Enhanced Aquarius"), or null.</summary>
    public string? Name { get; private init; }

    /// <summary>The name without the variant word, or null.</summary>
    public string? Family { get; private init; }

    /// <summary>The variant word ("Enhanced"), or null.</summary>
    public string? Variant { get; private init; }

    public string? Description { get; private init; }

    public MapDossier? Dossier { get; private init; }

    public IReadOnlyList<string> Authors { get; private init; } = [];

    /// <summary>The author's version of the map, or null.</summary>
    public string? Version { get; private init; }

    /// <summary>The language of the map's texts (BCP 47 primary tag), <c>en</c> when absent.</summary>
    public string Language { get; private init; } = "en";

    /// <summary>The lobby size the map plays best at, or null.</summary>
    public int? RecommendedPlayers { get; private init; }

    /// <summary>Lowercase facts about the map (<c>3v3</c>, <c>night</c>...).</summary>
    public IReadOnlyList<string> Tags { get; private init; } = [];

    public IReadOnlyList<MapPlace> Places { get; private init; } = [];

    public MapPictures Pictures { get; private init; } = MapPictures.None;

    /// <summary>Reads <c>ui/identity.json</c> of an SVMap folder, in the map's own language (<see cref="Translated"/>
    /// gives it in another).</summary>
    public static MapIdentity Load(string mapFolder)
    {
        string path = Path.Combine(mapFolder, "ui", "identity.json");
        try
        {
            return File.Exists(path) ? Parse(File.ReadAllBytes(path), Path.Combine(mapFolder, "ui")) : Empty;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Empty;
        }
    }

    /// <summary>Parses an identity document whose picture paths are relative to <paramref name="uiFolder"/>.</summary>
    public static MapIdentity Parse(byte[] utf8, string uiFolder)
    {
        ArgumentNullException.ThrowIfNull(utf8);
        try
        {
            ReadOnlyMemory<byte> json = utf8.Length >= 3 && utf8[0] == 0xEF && utf8[1] == 0xBB && utf8[2] == 0xBF ? utf8.AsMemory(3) : utf8;
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return Empty;
            }

            int? recommended = root.TryGetProperty("players", out JsonElement players) && players.ValueKind == JsonValueKind.Object &&
                players.TryGetProperty("recommended", out JsonElement r) && r.ValueKind == JsonValueKind.Number &&
                r.TryGetInt32(out int n) && n is >= 1 and <= MaxPlayers
                    ? n
                    : null;
            return new MapIdentity
            {
                Exists = true,
                Name = Text(root, "name", MaxNameLength),
                Family = Text(root, "family", MaxNameLength),
                Variant = Text(root, "variant", MaxNameLength),
                Description = Text(root, "description", MaxDescriptionLength),
                Dossier = DossierOf(root),
                Authors = Strings(root, "authors", 64, MaxNameLength),
                Version = Text(root, "version", 32),
                Language = Text(root, "language", 8) is { } lang && IsLanguage(lang) ? lang : "en",
                RecommendedPlayers = recommended,
                Tags = Strings(root, "tags", 32, 24).FindAll(IsTag),
                Places = PlacesOf(root),
                Pictures = PicturesOf(root, uiFolder),
            };
        }
        catch (JsonException)
        {
            return Empty;
        }
    }

    /// <summary>This identity with a translation's texts in place of its own; each text the translation lacks keeps the
    /// map's own.</summary>
    public MapIdentity Translated(MapText text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (ReferenceEquals(text, MapText.None) || !Exists)
        {
            return this;
        }

        var dossier = new MapDossier(text.When ?? Dossier?.When ?? string.Empty, text.Where ?? Dossier?.Where ?? string.Empty,
            text.Summary ?? Dossier?.Summary ?? string.Empty);
        var places = new List<MapPlace>(Places.Count);
        foreach (MapPlace place in Places)
        {
            places.Add(text.Places.TryGetValue(place.Number, out string? name) ? place with { Name = name } : place);
        }

        return new MapIdentity
        {
            Exists = true,
            Name = text.Name ?? Name,
            Family = text.Name is null ? Family : null,
            Variant = text.Name is null ? Variant : null,
            Description = text.Description ?? Description,
            Dossier = dossier.When.Length + dossier.Where.Length + dossier.Summary.Length == 0 ? null : dossier,
            Authors = Authors,
            Version = Version,
            Language = Language,
            RecommendedPlayers = RecommendedPlayers,
            Tags = Tags,
            Places = places,
            Pictures = Pictures,
        };
    }

    /// <summary>A tag as the spec allows it: 1 to 24 of a-z, 0-9 and '-'.</summary>
    public static bool IsTag(string tag)
    {
        if (tag.Length is 0 or > 24)
        {
            return false;
        }

        foreach (char c in tag)
        {
            if (!(char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-'))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>A BCP 47 primary language tag as the spec allows it: 2 or 3 of a-z.</summary>
    public static bool IsLanguage(string tag) => tag.Length is 2 or 3 && tag.AsSpan().IndexOfAnyExceptInRange('a', 'z') < 0;

    private static MapDossier? DossierOf(JsonElement root)
    {
        if (!root.TryGetProperty("dossier", out JsonElement d) || d.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var dossier = new MapDossier(Text(d, "when", 64) ?? string.Empty, Text(d, "where", 64) ?? string.Empty,
            Text(d, "summary", MaxDescriptionLength) ?? string.Empty);
        return dossier.When.Length + dossier.Where.Length + dossier.Summary.Length == 0 ? null : dossier;
    }

    private static List<MapPlace> PlacesOf(JsonElement root)
    {
        var places = new List<MapPlace>();
        var numbers = new HashSet<int>();
        if (!root.TryGetProperty("places", out JsonElement list) || list.ValueKind != JsonValueKind.Array)
        {
            return places;
        }

        foreach (JsonElement p in list.EnumerateArray())
        {
            if (p.ValueKind != JsonValueKind.Object || !p.TryGetProperty("number", out JsonElement num) || num.ValueKind != JsonValueKind.Number ||
                !num.TryGetInt32(out int number) || number is < 1 or > 99 || !numbers.Add(number) ||
                !p.TryGetProperty("at", out JsonElement at) || at.ValueKind != JsonValueKind.Array || at.GetArrayLength() != 3)
            {
                continue;
            }

            int? floor = p.TryGetProperty("floor", out JsonElement f) && f.ValueKind == JsonValueKind.Number && f.TryGetInt32(out int fi) && fi >= 0 ? fi : null;
            places.Add(new MapPlace(number, Text(p, "name", MaxNameLength) ?? string.Empty,
                new FVector(Coord(at[0]), Coord(at[1]), Coord(at[2])), floor));
        }

        places.Sort(static (a, b) => a.Number.CompareTo(b.Number));
        return places;
    }

    private static float Coord(JsonElement v) => v.ValueKind == JsonValueKind.Number && v.TryGetSingle(out float f) && float.IsFinite(f) ? f : 0f;

    private static MapPictures PicturesOf(JsonElement root, string uiFolder)
    {
        if (!root.TryGetProperty("pictures", out JsonElement p) || p.ValueKind != JsonValueKind.Object)
        {
            return MapPictures.None;
        }

        string pictures = Path.GetFullPath(Path.Combine(uiFolder, "pictures")) + Path.DirectorySeparatorChar;
        string? One(JsonElement obj, string key) =>
            obj.TryGetProperty(key, out JsonElement v) && v.ValueKind == JsonValueKind.String ? Picture(uiFolder, pictures, v.GetString()) : null;
        List<string> Many(JsonElement obj, string key)
        {
            var list = new List<string>();
            if (obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(key, out JsonElement a) && a.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement e in a.EnumerateArray())
                {
                    if (e.ValueKind == JsonValueKind.String && Picture(uiFolder, pictures, e.GetString()) is { } path)
                    {
                        list.Add(path);
                    }
                }
            }

            return list;
        }

        JsonElement loading = p.TryGetProperty("loading", out JsonElement l) ? l : default;
        return new MapPictures
        {
            Menu = One(p, "menu"),
            LoadingSpy = Many(loading, "spy"),
            LoadingMerc = Many(loading, "merc"),
            Briefing = One(p, "briefing"),
            Banner = One(p, "banner"),
            BriefingThroughSnapshot = p.TryGetProperty("briefingProjection", out JsonElement bp) && bp.ValueKind == JsonValueKind.String &&
                bp.GetString() == "snapshot",
            Generated = Strings(p, "generated", 8, 16),
        };
    }

    /// <summary>The full path of a picture the spec allows (inside ui/pictures, PNG or JPEG, not too big), or null.</summary>
    private static string? Picture(string uiFolder, string picturesPrefix, string? rel)
    {
        if (string.IsNullOrEmpty(rel) || rel.Contains('\\', StringComparison.Ordinal) || !rel.StartsWith("pictures/", StringComparison.Ordinal))
        {
            return null;
        }

        string ext = Path.GetExtension(rel).ToLowerInvariant();
        if (ext is not (".png" or ".jpg" or ".jpeg"))
        {
            return null;
        }

        try
        {
            string path = Path.GetFullPath(Path.Combine(uiFolder, rel));
            if (!path.StartsWith(picturesPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var info = new FileInfo(path);
            return info.Exists && info.Length <= MaxPictureBytes ? path : null;
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or IOException or UnauthorizedAccessException)
        {
            // A path the file system refuses (a null character, a device name) is one broken picture, not a broken catalogue.
            return null;
        }
    }

    private static string? Text(JsonElement obj, string key, int max)
    {
        if (!obj.TryGetProperty(key, out JsonElement v) || v.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        string text = (v.GetString() ?? string.Empty).Trim();
        if (text.Length > max)
        {
            text = text[..max].TrimEnd();
        }

        return text.Length == 0 ? null : text;
    }

    private static List<string> Strings(JsonElement obj, string key, int maxCount, int maxLength)
    {
        var list = new List<string>();
        if (!obj.TryGetProperty(key, out JsonElement a) || a.ValueKind != JsonValueKind.Array)
        {
            return list;
        }

        foreach (JsonElement e in a.EnumerateArray())
        {
            if (list.Count == maxCount)
            {
                break;
            }

            string s = e.ValueKind == JsonValueKind.String ? (e.GetString() ?? string.Empty).Trim() : string.Empty;
            if (s.Length > 0 && s.Length <= maxLength && !list.Contains(s))
            {
                list.Add(s);
            }
        }

        return list;
    }
}
