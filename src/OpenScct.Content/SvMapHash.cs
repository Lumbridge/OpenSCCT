using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using OpenScct.Content.Json;

namespace OpenScct.Content;

/// <summary>Digest and size of one map file, as <c>manifest.json</c> lists it.</summary>
public readonly record struct SvMapFileDigest(string Path, string Sha256, long Size);

/// <summary>Every file digest of a map folder and the hash of each non-empty group.</summary>
public sealed class SvMapHashes
{
    internal SvMapHashes(IReadOnlyList<SvMapFileDigest> files, IReadOnlyDictionary<string, string> groups)
    {
        Files = files;
        Groups = groups;
    }

    /// <summary>Every file except <c>manifest.json</c>, sorted by UTF-8 path.</summary>
    public IReadOnlyList<SvMapFileDigest> Files { get; }

    /// <summary>Group name (<c>sim</c>, <c>render</c>, <c>audio</c>, <c>ui</c>) to group hash, non-empty groups only.</summary>
    public IReadOnlyDictionary<string, string> Groups { get; }

    public string? Sim => Groups.GetValueOrDefault(SvMapHash.SimGroup);
}

/// <summary>
/// SVMap hashes (conventions.md, "Canonical sim hash"). File digests are SHA-256 of the canonical bytes for
/// <c>sim/*.json</c> and of the raw bytes otherwise; a group hash is SHA-256 of the <c>sha256sum</c>-style listing
/// of its files sorted by UTF-8 path. Client and server compare the sim hash in the handshake.
/// </summary>
public static class SvMapHash
{
    public const string SimGroup = "sim";
    public const string ManifestFile = "manifest.json";

    /// <summary>The file groups of a map, in manifest order.</summary>
    public static readonly IReadOnlyList<string> GroupNames = ["sim", "render", "audio", "ui"];

    /// <summary>Lowercase hex SHA-256.</summary>
    public static string Sha256Hex(ReadOnlySpan<byte> data) => Convert.ToHexStringLower(SHA256.HashData(data));

    /// <summary>The file digest of <paramref name="relPath"/> (map-relative, <c>/</c> separators).</summary>
    public static string FileDigest(string relPath, ReadOnlySpan<byte> data)
    {
        ArgumentNullException.ThrowIfNull(relPath);
        return IsCanonicalized(relPath) ? Sha256Hex(CanonicalJson.Canonicalize(data, relPath)) : Sha256Hex(data);
    }

    /// <summary>True when the file is hashed in canonical JSON form (<c>sim/</c> files ending in <c>.json</c>).</summary>
    public static bool IsCanonicalized(string relPath) =>
        relPath.StartsWith("sim/", StringComparison.Ordinal) && relPath.EndsWith(".json", StringComparison.Ordinal);

    /// <summary>The group listing: <c>"&lt;digest&gt;  &lt;path&gt;\n"</c> per file, sorted by UTF-8 path.</summary>
    public static string GroupListing(IEnumerable<(string Path, string Digest)> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        var rows = new List<(string Path, string Digest)>(files);
        rows.Sort(static (a, b) => Utf8Order.Compare(a.Path, b.Path));
        var sb = new StringBuilder(rows.Count * 96);
        foreach ((string path, string digest) in rows)
        {
            sb.Append(digest).Append("  ").Append(path).Append('\n');
        }

        return sb.ToString();
    }

    /// <summary>SHA-256 of the UTF-8 group listing.</summary>
    public static string GroupHash(IEnumerable<(string Path, string Digest)> files) =>
        Sha256Hex(Encoding.UTF8.GetBytes(GroupListing(files)));

    /// <summary>Map-relative paths of every file under <paramref name="mapRoot"/> except the manifest, UTF-8 order.</summary>
    public static List<string> MapFiles(string mapRoot)
    {
        ArgumentNullException.ThrowIfNull(mapRoot);
        List<string> paths = RelativeFiles(Path.GetFullPath(mapRoot), Path.GetFullPath(mapRoot));
        paths.Remove(ManifestFile);
        return paths;
    }

    /// <summary>Map-relative paths of the files of one group folder (<c>sim/...</c>), UTF-8 order; empty when it is missing.</summary>
    public static List<string> GroupFiles(string mapRoot, string group)
    {
        ArgumentNullException.ThrowIfNull(mapRoot);
        ArgumentNullException.ThrowIfNull(group);
        string root = Path.GetFullPath(mapRoot);
        string dir = Path.Combine(root, group);
        return Directory.Exists(dir) ? RelativeFiles(root, dir) : new List<string>();
    }

    private static List<string> RelativeFiles(string root, string dir)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            AttributesToSkip = 0,
            IgnoreInaccessible = false,
            MatchType = MatchType.Simple,
            ReturnSpecialDirectories = false,
        };
        var paths = new List<string>();
        foreach (string full in Directory.EnumerateFiles(dir, "*", options))
        {
            paths.Add(Path.GetRelativePath(root, full).Replace(Path.DirectorySeparatorChar, '/'));
        }

        paths.Sort(Utf8Order.Compare);
        return paths;
    }

    /// <summary>
    /// Hashes a whole map folder: every file digest and size, and the hash of each non-empty group. Files outside
    /// the four group folders are an error, as in the pipeline.
    /// </summary>
    public static SvMapHashes HashMap(string mapRoot)
    {
        var files = new List<SvMapFileDigest>();
        var byGroup = new Dictionary<string, List<(string, string)>>(StringComparer.Ordinal);
        foreach (string rel in MapFiles(mapRoot))
        {
            int slash = rel.IndexOf('/', StringComparison.Ordinal);
            string group = slash < 0 ? string.Empty : rel[..slash];
            if (slash < 0 || !IsGroup(group))
            {
                throw new SvMapFormatException(rel, $"files must live in one of {string.Join(", ", GroupNames)}");
            }

            byte[] data = File.ReadAllBytes(Path.Combine(mapRoot, rel));
            string digest = FileDigest(rel, data);
            files.Add(new SvMapFileDigest(rel, digest, data.LongLength));
            if (!byGroup.TryGetValue(group, out List<(string, string)>? rows))
            {
                rows = new List<(string, string)>();
                byGroup[group] = rows;
            }

            rows.Add((rel, digest));
        }

        var groups = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, List<(string, string)>> kv in byGroup)
        {
            groups[kv.Key] = GroupHash(kv.Value);
        }

        return new SvMapHashes(files, groups);
    }

    /// <summary>The sim hash of the map at <paramref name="mapRoot"/>: every file under <c>sim/</c>.</summary>
    public static string ComputeSimHash(string mapRoot) => ComputeSimHash(mapRoot, out _);

    /// <summary>The sim hash and the digest of each sim file (UTF-8 path order).</summary>
    public static string ComputeSimHash(string mapRoot, out IReadOnlyList<SvMapFileDigest> files)
    {
        ArgumentNullException.ThrowIfNull(mapRoot);
        var list = new List<SvMapFileDigest>();
        foreach (string rel in GroupFiles(mapRoot, SimGroup))
        {
            byte[] data = File.ReadAllBytes(Path.Combine(mapRoot, rel));
            list.Add(new SvMapFileDigest(rel, FileDigest(rel, data), data.LongLength));
        }

        files = list;
        return GroupHash(list);
    }

    /// <summary>The group hash of a set of file digests.</summary>
    public static string GroupHash(IEnumerable<SvMapFileDigest> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        var rows = new List<(string, string)>();
        foreach (SvMapFileDigest f in files)
        {
            rows.Add((f.Path, f.Sha256));
        }

        return GroupHash(rows);
    }

    private static bool IsGroup(string name)
    {
        foreach (string g in GroupNames)
        {
            if (g == name)
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>Orders strings by their UTF-8 bytes (code point order), which differs from UTF-16 ordinal order above U+D7FF.</summary>
public static class Utf8Order
{
    public static int Compare(string? a, string? b)
    {
        if (ReferenceEquals(a, b))
        {
            return 0;
        }

        if (a is null)
        {
            return -1;
        }

        if (b is null)
        {
            return 1;
        }

        int i = 0;
        int j = 0;
        while (i < a.Length && j < b.Length)
        {
            int ca = CodePoint(a, ref i);
            int cb = CodePoint(b, ref j);
            if (ca != cb)
            {
                return ca < cb ? -1 : 1;
            }
        }

        bool aDone = i >= a.Length;
        bool bDone = j >= b.Length;
        return aDone == bDone ? 0 : aDone ? -1 : 1;
    }

    private static int CodePoint(string s, ref int i)
    {
        char c = s[i++];
        if (char.IsHighSurrogate(c) && i < s.Length && char.IsLowSurrogate(s[i]))
        {
            return char.ConvertToUtf32(c, s[i++]);
        }

        return c;
    }
}
