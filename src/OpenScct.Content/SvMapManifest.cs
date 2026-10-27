using System;
using System.Collections.Generic;
using System.IO;
using OpenScct.Content.Json;

namespace OpenScct.Content;

/// <summary>Where a map came from: converted from an original <c>.sdc</c>, authored natively, or synthetic.</summary>
public sealed record SvMapSource(string Kind, string Generator, long GeneratorVersion, string? OriginalFile, string? OriginalSha256)
{
    public bool IsConverted => Kind == "converted";
}

/// <summary>A shared content pack the map needs.</summary>
public readonly record struct SvMapPackRequirement(string Pack, string Sha256);

/// <summary>
/// <c>manifest.json</c>: identity, provenance, per-file digests and group hashes (spec/svmap/1.0/README.md). The
/// handshake compares <see cref="SimHash"/> and, when it differs, names the differing file from <see cref="Files"/>.
/// Unknown members are ignored, so a 1.0 reader accepts later 1.x manifests.
/// </summary>
public sealed class SvMapManifest
{
    private readonly Dictionary<string, SvMapFileDigest> _files;

    private SvMapManifest(string formatVersion, string map, string title, SvMapSource source, List<SvMapFileDigest> files,
        SortedDictionary<string, string> hashes, List<SvMapPackRequirement> requires)
    {
        FormatVersion = formatVersion;
        Map = map;
        Title = title;
        Source = source;
        files.Sort(static (a, b) => Utf8Order.Compare(a.Path, b.Path));
        Files = files;
        _files = new Dictionary<string, SvMapFileDigest>(StringComparer.Ordinal);
        foreach (SvMapFileDigest f in files)
        {
            _files[f.Path] = f;
        }

        Hashes = hashes;
        Requires = requires;
    }

    public const string Format = "svmap";

    public string FormatVersion { get; }

    /// <summary>Map id (the original file stem for converted maps).</summary>
    public string Map { get; }

    public string Title { get; }

    public SvMapSource Source { get; }

    /// <summary>Every file except the manifest, in UTF-8 path order.</summary>
    public IReadOnlyList<SvMapFileDigest> Files { get; }

    /// <summary>Group name to group hash.</summary>
    public IReadOnlyDictionary<string, string> Hashes { get; }

    public string SimHash => Hashes[SvMapHash.SimGroup];

    public IReadOnlyList<SvMapPackRequirement> Requires { get; }

    public bool TryGetFile(string relPath, out SvMapFileDigest file) => _files.TryGetValue(relPath, out file);

    public static SvMapManifest Load(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return Parse(File.ReadAllBytes(path));
    }

    public static SvMapManifest Parse(ReadOnlySpan<byte> utf8)
    {
        const string file = SvMapHash.ManifestFile;
        var j = new JsonRead(file);
        SvJsonObject root = j.Object(SvJson.Parse(utf8, file), "");
        string format = j.StringMember(root, "format", "");
        if (format != Format)
        {
            throw j.Error("/format", $"expected '{Format}', found '{format}'");
        }

        string version = j.FormatVersion(root, "");
        string map = j.StringMember(root, "map", "");
        string title = j.StringMember(root, "title", "");

        SvJsonObject src = j.ObjectMember(root, "source", "");
        string kind = j.StringMember(src, "kind", "/source");
        string generator = j.StringMember(src, "generator", "/source");
        long generatorVersion = j.IntegerMember(src, "generatorVersion", "/source", 1);
        string? originalFile = null;
        string? originalSha = null;
        if (kind == "converted")
        {
            SvJsonObject original = j.ObjectMember(src, "original", "/source");
            originalFile = j.StringMember(original, "file", "/source/original");
            originalSha = Sha256(j, original, "sha256", "/source/original");
        }
        else if (kind is not ("authored" or "synthetic"))
        {
            throw j.Error("/source/kind", $"unknown source kind '{kind}'");
        }

        var files = new List<SvMapFileDigest>();
        foreach (KeyValuePair<string, SvJsonNode> member in j.ObjectMember(root, "files", ""))
        {
            string path = $"/files/{member.Key}";
            SvJsonObject entry = j.Object(member.Value, path);
            files.Add(new SvMapFileDigest(member.Key, Sha256(j, entry, "sha256", path), j.IntegerMember(entry, "size", path, 0)));
        }

        var hashes = new SortedDictionary<string, string>(StringComparer.Ordinal);
        SvJsonObject hashObj = j.ObjectMember(root, "hashes", "");
        foreach (KeyValuePair<string, SvJsonNode> member in hashObj)
        {
            hashes[member.Key] = Sha256(j, hashObj, member.Key, "/hashes");
        }

        if (!hashes.ContainsKey(SvMapHash.SimGroup))
        {
            throw j.Error("/hashes", "missing 'sim'");
        }

        var requires = new List<SvMapPackRequirement>();
        SvJsonArray req = j.ArrayMember(root, "requires", "");
        for (int i = 0; i < req.Count; i++)
        {
            string path = $"/requires/{i}";
            SvJsonObject entry = j.Object(req[i], path);
            requires.Add(new SvMapPackRequirement(j.StringMember(entry, "pack", path), Sha256(j, entry, "sha256", path)));
        }

        return new SvMapManifest(version, map, title, new SvMapSource(kind, generator, generatorVersion, originalFile, originalSha),
            files, hashes, requires);
    }

    /// <summary>
    /// Files of <paramref name="group"/> whose digest differs between this manifest and <paramref name="other"/>,
    /// including files only one side has, in UTF-8 path order. Empty when the group hashes agree.
    /// </summary>
    public List<string> DifferingFiles(SvMapManifest other, string group = SvMapHash.SimGroup)
    {
        ArgumentNullException.ThrowIfNull(other);
        string prefix = group + "/";
        var paths = new SortedSet<string>(Comparer<string>.Create(Utf8Order.Compare));
        foreach (SvMapFileDigest f in Files)
        {
            if (f.Path.StartsWith(prefix, StringComparison.Ordinal) &&
                (!other.TryGetFile(f.Path, out SvMapFileDigest o) || o.Sha256 != f.Sha256))
            {
                paths.Add(f.Path);
            }
        }

        foreach (SvMapFileDigest f in other.Files)
        {
            if (f.Path.StartsWith(prefix, StringComparison.Ordinal) && !TryGetFile(f.Path, out _))
            {
                paths.Add(f.Path);
            }
        }

        return new List<string>(paths);
    }

    /// <summary>
    /// Problems between this manifest and the map folder at <paramref name="mapRoot"/> (file set, digests, sizes,
    /// group hashes). Empty when they agree.
    /// </summary>
    public List<string> Verify(string mapRoot)
    {
        var problems = new List<string>();
        SvMapHashes actual = SvMapHash.HashMap(mapRoot);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (SvMapFileDigest f in actual.Files)
        {
            seen.Add(f.Path);
            if (!TryGetFile(f.Path, out SvMapFileDigest listed))
            {
                problems.Add($"{f.Path}: not listed in the manifest");
            }
            else if (listed.Sha256 != f.Sha256)
            {
                problems.Add($"{f.Path}: digest {f.Sha256} differs from the manifest's {listed.Sha256}");
            }
            else if (listed.Size != f.Size)
            {
                problems.Add($"{f.Path}: size {f.Size} differs from the manifest's {listed.Size}");
            }
        }

        foreach (SvMapFileDigest f in Files)
        {
            if (!seen.Contains(f.Path))
            {
                problems.Add($"{f.Path}: listed in the manifest but missing");
            }
        }

        foreach (KeyValuePair<string, string> g in actual.Groups)
        {
            if (!Hashes.TryGetValue(g.Key, out string? listed))
            {
                problems.Add($"hashes/{g.Key}: missing from the manifest");
            }
            else if (listed != g.Value)
            {
                problems.Add($"hashes/{g.Key}: {g.Value} differs from the manifest's {listed}");
            }
        }

        foreach (KeyValuePair<string, string> g in Hashes)
        {
            if (!actual.Groups.ContainsKey(g.Key))
            {
                problems.Add($"hashes/{g.Key}: the group has no files");
            }
        }

        return problems;
    }

    private static string Sha256(JsonRead j, SvJsonObject obj, string key, string path)
    {
        string value = j.StringMember(obj, key, path);
        if (value.Length != 64)
        {
            throw j.Error($"{path}/{key}", "expected 64 lowercase hex digits");
        }

        foreach (char c in value)
        {
            if (!char.IsAsciiHexDigitLower(c) && !char.IsAsciiDigit(c))
            {
                throw j.Error($"{path}/{key}", "expected 64 lowercase hex digits");
            }
        }

        return value;
    }
}
