using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace OpenScct.Launcher.Update;

/// <summary>SHA-256 checksum files and file hashing.</summary>
public static class Checksums
{
    /// <summary>
    /// The expected hash of <paramref name="fileName"/> in a checksum file. Accepts the sha256sum format
    /// (<c>&lt;hex&gt;  name</c> or <c>&lt;hex&gt; *name</c>, one per line) and a bare hash (a single-file .sha256).
    /// Returns null when the file names other files only or has no valid hash.
    /// </summary>
    public static string? FindExpected(string checksumText, string fileName)
    {
        ArgumentNullException.ThrowIfNull(checksumText);
        ArgumentException.ThrowIfNullOrEmpty(fileName);
        string? bare = null;
        int lines = 0;
        foreach (string raw in checksumText.Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            lines++;
            int space = line.IndexOfAny([' ', '\t']);
            string hash = space < 0 ? line : line[..space];
            if (!IsSha256Hex(hash))
            {
                continue;
            }

            if (space < 0)
            {
                bare = hash;
                continue;
            }

            string name = line[(space + 1)..].TrimStart(' ', '\t');
            if (name.StartsWith('*'))
            {
                name = name[1..];
            }

            name = Path.GetFileName(name.Replace('\\', '/'));
            if (string.Equals(name, fileName, StringComparison.OrdinalIgnoreCase))
            {
                return hash.ToLowerInvariant();
            }
        }

        return lines == 1 ? bare?.ToLowerInvariant() : null;
    }

    public static bool IsSha256Hex(string s)
    {
        if (s.Length != 64)
        {
            return false;
        }

        foreach (char ch in s)
        {
            if (!char.IsAsciiHexDigit(ch))
            {
                return false;
            }
        }

        return true;
    }

    public static async Task<string> HashFileAsync(string path, CancellationToken cancel)
    {
        await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.Asynchronous | FileOptions.SequentialScan);
        byte[] hash = await SHA256.HashDataAsync(stream, cancel).ConfigureAwait(false);
        return Convert.ToHexStringLower(hash);
    }

    public static async Task<bool> VerifyAsync(string path, string expectedHex, CancellationToken cancel)
    {
        ArgumentException.ThrowIfNullOrEmpty(expectedHex);
        string actual = await HashFileAsync(path, cancel).ConfigureAwait(false);
        return string.Equals(actual, expectedHex.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
