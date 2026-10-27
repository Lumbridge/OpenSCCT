using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace OpenScct.Maps;

/// <summary>Checks on paths and hashes that come from the map repository, before anything is downloaded or written.</summary>
public static class MapPaths
{
    private static readonly string[] TextExtensions = [".int", ".eng", ".fra", ".deu", ".ita", ".esp"];

    /// <summary>
    /// Whether a file of a map is one the game plays with: the map (Packages/Maps/*.sdc), its textures, meshes,
    /// sounds and animations, and its text (System/_PC_). Editor maps (Packages/MapsEd) and notes are not.
    /// </summary>
    public static bool IsPlayable(string path)
    {
        string[] p = path.Split('/');
        string ext = Path.GetExtension(path).ToLowerInvariant();
        if (p.Length == 3 && p[0] == "System" && p[1] == "_PC_")
        {
            return TextExtensions.Contains(ext);
        }

        if (p.Length != 3 || p[0] != "Packages")
        {
            return false;
        }

        return p[1] switch
        {
            "Maps" => ext == ".sdc",
            "Textures" => ext is ".utx" or ".utc",
            "StaticMeshes" => ext == ".usx",
            "Sounds" => ext is ".uax" or ".uas" or ".mux" or ".usxx",
            "Animations" => ext == ".ukx",
            _ => false,
        };
    }

    /// <summary>A relative path with forward slashes that cannot leave its folder or name a device.</summary>
    public static void ValidateRelative(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Contains('\\') || Path.IsPathRooted(path))
        {
            throw new InvalidDataException("Unsafe map path: " + path);
        }

        foreach (string part in path.Split('/'))
        {
            if (part.Length == 0 || part is "." or ".." || part.EndsWith('.') || part.EndsWith(' ')
                || part.IndexOfAny([':', '*', '?', '"', '<', '>', '|', '\0']) >= 0 || part.Any(char.IsControl)
                || Regex.IsMatch(part.Split('.')[0], "^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$", RegexOptions.IgnoreCase))
            {
                throw new InvalidDataException("Unsafe map path: " + path);
            }
        }
    }

    public static void ValidateHash(string hash)
    {
        if (!Regex.IsMatch(hash ?? string.Empty, "^[a-fA-F0-9]{40}$"))
        {
            throw new InvalidDataException("Invalid Git object hash in the map list.");
        }
    }

    public static void ValidateSize(long size)
    {
        if (size is < 0 or > 2L * 1024 * 1024 * 1024)
        {
            throw new InvalidDataException("Unsupported map file size.");
        }
    }

    /// <summary>The full path of <paramref name="relative"/> under <paramref name="root"/>; refuses one that leaves it or passes through a link.</summary>
    public static string Under(string root, string relative)
    {
        ValidateRelative(relative);
        string basePath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        string path = Path.GetFullPath(Path.Combine(basePath, relative));
        if (!path.StartsWith(basePath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Map file escapes the maps folder: " + relative);
        }

        for (string? p = path; p is not null && p.Length >= basePath.Length - 1; p = Path.GetDirectoryName(p))
        {
            if ((File.Exists(p) || Directory.Exists(p)) && File.GetAttributes(p).HasFlag(FileAttributes.ReparsePoint))
            {
                throw new IOException("Linked folders are not supported in the maps folder: " + p);
            }
        }

        return path;
    }

    /// <summary>Writes a small file so readers see either the old or the new content, never half of it.</summary>
    public static void WriteAllTextAtomic(string path, string content)
    {
        string full = Path.GetFullPath(path);
        string dir = Path.GetDirectoryName(full)!;
        Directory.CreateDirectory(dir);
        string temp = Path.Combine(dir, "." + Path.GetFileName(full) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.WriteAllText(temp, content, new UTF8Encoding(false));
            File.Move(temp, full, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
    }

    /// <summary>The Git blob id of a file (SHA-1 of "blob &lt;size&gt;\0" and the bytes): what the repository's tree lists.</summary>
    public static string GitBlobSha1(string path)
    {
        using FileStream stream = File.OpenRead(path);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        hash.AppendData(Encoding.ASCII.GetBytes("blob " + stream.Length + "\0"));
        byte[] buffer = new byte[128 * 1024];
        int n;
        while ((n = stream.Read(buffer)) > 0)
        {
            hash.AppendData(buffer.AsSpan(0, n));
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    /// <summary>
    /// Whether a file has the listed hash: its Git blob id (what the repository's tree and the ports list), or its
    /// plain SHA-1 (what the stock asset packs of <c>asset-catalog.json</c> list). Both are read in one pass.
    /// </summary>
    public static bool MatchesHash(string path, string hash)
    {
        using FileStream stream = File.OpenRead(path);
        using var blob = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        using var plain = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        blob.AppendData(Encoding.ASCII.GetBytes("blob " + stream.Length + "\0"));
        byte[] buffer = new byte[128 * 1024];
        int n;
        while ((n = stream.Read(buffer)) > 0)
        {
            blob.AppendData(buffer.AsSpan(0, n));
            plain.AppendData(buffer.AsSpan(0, n));
        }

        return string.Equals(Convert.ToHexString(blob.GetHashAndReset()), hash, StringComparison.OrdinalIgnoreCase)
            || string.Equals(Convert.ToHexString(plain.GetHashAndReset()), hash, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A short id of a set of files (their paths and hashes).</summary>
    public static string Fingerprint(IEnumerable<MapFile> files) =>
        Fingerprint(files.Select(f => f.Destination.ToLowerInvariant() + ":" + f.Hash.ToLowerInvariant()));

    /// <summary>A short id of a set of lines (order does not matter).</summary>
    public static string Fingerprint(IEnumerable<string> lines) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        string.Join('\n', lines.OrderBy(l => l, StringComparer.Ordinal)))))[..16].ToLowerInvariant();
}
