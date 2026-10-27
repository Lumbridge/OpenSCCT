using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenScct.Launcher.Update;

/// <summary>
/// The file list of a game build (release asset <c>OpenSCCT-&lt;version&gt;-files.json</c>, written by
/// tools/release/New-Release.ps1): every file of the build with its size and SHA-256, and the release asset that
/// holds it, gzipped and named by its hash. A file that did not change keeps pointing at the release that first
/// uploaded it, so a release only uploads what changed and the launcher only downloads what it does not have.
/// <code>
/// { "format": 1, "version": "0.6.0", "commit": "cbae4ce...",
///   "files": [ { "path": "OpenSCCT.pck", "size": 21733120, "sha256": "9c5a...",
///                "blob": { "tag": "v0.6.0", "name": "9c5a....gz", "size": 8012233 } } ] }
/// </code>
/// </summary>
public sealed class BuildFiles
{
    public const int CurrentFormat = 1;

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    [JsonPropertyName("format")]
    public int Format { get; set; }

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("commit")]
    public string? Commit { get; set; }

    [JsonPropertyName("files")]
    public List<BuildFile> Files { get; set; } = [];

    /// <summary>Reads and checks a file list. Throws <see cref="UpdateRejectedException"/> when it is not usable.</summary>
    public static BuildFiles Parse(string json)
    {
        BuildFiles? files;
        try
        {
            files = JsonSerializer.Deserialize<BuildFiles>(json, Json);
        }
        catch (JsonException ex)
        {
            throw new UpdateRejectedException("The build's file list could not be read (" + ex.Message + ").");
        }

        if (files is null || files.Format != CurrentFormat)
        {
            throw new UpdateRejectedException($"The build's file list has format {files?.Format}; this launcher reads format {CurrentFormat}. Update the launcher.");
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (BuildFile f in files.Files)
        {
            if (!IsSafeRelativePath(f.Path) || !seen.Add(f.Path) || f.Size < 0 || !Checksums.IsSha256Hex(f.Sha256)
                || f.Blob is null || string.IsNullOrWhiteSpace(f.Blob.Tag) || string.IsNullOrWhiteSpace(f.Blob.Name))
            {
                throw new UpdateRejectedException($"The build's file list has a bad entry ({f.Path}).");
            }
        }

        return files;
    }

    /// <summary>A relative path inside the build folder: no root, no drive, no "..", no empty parts.</summary>
    public static bool IsSafeRelativePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) || path.Contains(':', StringComparison.Ordinal))
        {
            return false;
        }

        foreach (string part in path.Split('/', '\\'))
        {
            if (part.Length == 0 || part == "." || part == "..")
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>One file of a build.</summary>
public sealed class BuildFile
{
    /// <summary>Path inside the build folder, with / between folders.</summary>
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("size")]
    public long Size { get; set; }

    /// <summary>SHA-256 of the file itself (not of the gzipped blob), lower-case hex.</summary>
    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; } = string.Empty;

    [JsonPropertyName("blob")]
    public BlobRef? Blob { get; set; }
}

/// <summary>Where a file's bytes are: a gzipped release asset of some release (often an older one).</summary>
public sealed class BlobRef
{
    [JsonPropertyName("tag")]
    public string Tag { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Size of the asset (the gzipped file, or the whole pack).</summary>
    [JsonPropertyName("size")]
    public long Size { get; set; }

    /// <summary>
    /// For a small file: its entry in the asset, a zip pack of the release's new small files (named by their SHA-256),
    /// so a release stays under GitHub's asset limit. Null for a file that is its own gzipped asset.
    /// </summary>
    [JsonPropertyName("member")]
    public string? Member { get; set; }
}
