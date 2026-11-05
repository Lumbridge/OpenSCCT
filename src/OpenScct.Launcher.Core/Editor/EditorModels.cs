using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using OpenScct.Launcher.GitHub;
using OpenScct.Launcher.Update;
using OpenScct.Launcher.Versioning;

namespace OpenScct.Launcher.Editor;

public sealed record EditorInstallation(SemVersion Version, SemVersion MinimumGameVersion, int LauncherProtocol);
public sealed record EditorOffer(VersionedRelease Release, ReleaseAsset Archive, ReleaseAsset Checksum, ReleaseAsset Metadata)
{
    public SemVersion Version => Release.Version;
}
public sealed record EditorCheck(ReleaseFeed Feed, EditorOffer? Latest, EditorInstallation? Installed, bool UpdateAvailable);
public sealed record EditorProgress(string Phase, long Received = 0, long Total = 0, double BytesPerSecond = 0, bool CanCancel = true);
/// <summary>The install path is free again. Modified or unlisted files, and any files cleanup could not remove,
/// remain in RetainedPath. Failures name cleanup errors, not ordinary user files deliberately kept.</summary>
public sealed record EditorUninstallResult(string? RetainedPath, IReadOnlyList<string> Failures);

/// <summary>The format-1 package contract also used by the editor's own updater.</summary>
internal sealed class EditorManifest
{
    public int Format { get; set; }
    public string Product { get; set; } = "";
    public string Version { get; set; } = "";
    public string Platform { get; set; } = "";
    public int LauncherProtocol { get; set; }
    public string MinimumGameVersion { get; set; } = "";
    public List<string> Entries { get; set; } = [];
    public List<EditorFile> Files { get; set; } = [];

    internal static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public EditorInstallation Installation => new(SemVersion.Parse(Version), SemVersion.Parse(MinimumGameVersion), LauncherProtocol);

    public static EditorManifest Read(string folder)
    {
        EditorManifest manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<EditorManifest>(File.ReadAllText(Path.Combine(folder, "version.json")), Json)
                ?? throw new InvalidDataException("The editor package has no version information.");
        }
        catch (JsonException ex) { throw new InvalidDataException("The editor package manifest could not be read.", ex); }
        if (manifest.Format != 1 || manifest.Product != "OpenSCCT Editor" || manifest.Platform != "win-x64"
            || manifest.LauncherProtocol != 1 || !SemVersion.TryParse(manifest.Version, out _)
            || !SemVersion.TryParse(manifest.MinimumGameVersion, out _) || manifest.Entries is null || manifest.Files is null
            || manifest.Entries.Count == 0 || manifest.Files.Count == 0
            || manifest.Entries.Any(e => !SafeRelative(e) || e.Contains('/') || e.Contains('\\') || e is ".update" or ".launcher-managed")
            || manifest.Entries.Distinct(StringComparer.OrdinalIgnoreCase).Count() != manifest.Entries.Count)
        {
            throw new InvalidDataException("This editor package does not support this launcher, or its manifest is invalid.");
        }
        return manifest;
    }

    public static bool SafeRelative(string? name) => !string.IsNullOrEmpty(name) && !Path.IsPathRooted(name)
        && !name.Contains(':') && !name.Contains('\\')
        && name.Split('/').All(p => p.Length > 0 && p is not "." and not ".." && !p.EndsWith('.') && !p.EndsWith(' ')
            && p.IndexOfAny(Path.GetInvalidFileNameChars()) < 0);
}

internal sealed class EditorFile
{
    public string Path { get; set; } = "";
    public long Size { get; set; }
    public string Sha256 { get; set; } = "";
}
