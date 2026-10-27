using System;
using System.Collections.Generic;
using System.Linq;
using OpenScct.Launcher.GitHub;
using OpenScct.Launcher.Versioning;

namespace OpenScct.Launcher.Update;

/// <summary>A release whose tag is a semantic version.</summary>
public sealed record VersionedRelease(SemVersion Version, ReleaseInfo Release);

/// <summary>
/// Picks the release and the files to download. Names written by tools/release/New-Release.ps1:
/// <c>OpenSCCT-0.2.0-win-x64.zip</c> (the game build), <c>OpenSCCT.Launcher-0.2.0-win-x64.exe</c> (this launcher),
/// <c>checksums.txt</c> (sha256sum format) and optionally one <c>&lt;asset&gt;.sha256</c> per file.
/// </summary>
public static class ReleasePicker
{
    public const string GamePrefix = "OpenSCCT-";
    public const string LauncherPrefix = "OpenSCCT.Launcher-";
    public const string Platform = "win-x64";
    public const string ChecksumsFile = "checksums.txt";

    public static string GameAssetName(SemVersion version) => $"{GamePrefix}{version}-{Platform}.zip";

    public static string LauncherAssetName(SemVersion version) => $"{LauncherPrefix}{version}-{Platform}.exe";

    /// <summary>The build's file list (<see cref="BuildFiles"/>), for updates that download only changed files.</summary>
    public static string FilesAssetName(SemVersion version) => $"{GamePrefix}{version}-{Platform}-files.json";

    /// <summary>The file list of the release, or null when the release only has the zip.</summary>
    public static ReleaseAsset? FilesAsset(VersionedRelease release)
    {
        ArgumentNullException.ThrowIfNull(release);
        string exact = FilesAssetName(release.Version);
        return release.Release.Assets.FirstOrDefault(a => string.Equals(a.Name, exact, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The newest release with a semantic version tag; prereleases only when asked for. Drafts never.</summary>
    public static VersionedRelease? Latest(IEnumerable<ReleaseInfo> releases, bool includePrereleases)
    {
        ArgumentNullException.ThrowIfNull(releases);
        VersionedRelease? best = null;
        foreach (ReleaseInfo release in releases)
        {
            if (release.Draft || !SemVersion.TryParse(release.TagName, out SemVersion? version))
            {
                continue;
            }

            if (!includePrereleases && (release.Prerelease || version.IsPrerelease))
            {
                continue;
            }

            if (best is null || version > best.Version)
            {
                best = new VersionedRelease(version, release);
            }
        }

        return best;
    }

    /// <summary>The game zip: the exact name first, then any OpenSCCT-*-win-x64.zip that is not the launcher.</summary>
    public static ReleaseAsset? GameAsset(VersionedRelease release)
    {
        ArgumentNullException.ThrowIfNull(release);
        List<ReleaseAsset> assets = release.Release.Assets;
        string exact = GameAssetName(release.Version);
        return assets.FirstOrDefault(a => string.Equals(a.Name, exact, StringComparison.OrdinalIgnoreCase))
            ?? assets.FirstOrDefault(a =>
                a.Name.StartsWith(GamePrefix, StringComparison.OrdinalIgnoreCase)
                && a.Name.EndsWith($"-{Platform}.zip", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The launcher exe of the release, or null when the release does not carry one.</summary>
    public static ReleaseAsset? LauncherAsset(VersionedRelease release)
    {
        ArgumentNullException.ThrowIfNull(release);
        string exact = LauncherAssetName(release.Version);
        return release.Release.Assets.FirstOrDefault(a => string.Equals(a.Name, exact, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Where the checksum of <paramref name="asset"/> is published: its own .sha256 file first, then checksums.txt.</summary>
    public static ReleaseAsset? ChecksumAsset(VersionedRelease release, ReleaseAsset asset)
    {
        ArgumentNullException.ThrowIfNull(release);
        ArgumentNullException.ThrowIfNull(asset);
        List<ReleaseAsset> assets = release.Release.Assets;
        return assets.FirstOrDefault(a => string.Equals(a.Name, asset.Name + ".sha256", StringComparison.OrdinalIgnoreCase))
            ?? assets.FirstOrDefault(a => string.Equals(a.Name, ChecksumsFile, StringComparison.OrdinalIgnoreCase));
    }
}
