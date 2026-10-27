using System;
using System.Collections.Generic;
using System.Linq;
using OpenScct.Launcher.GitHub;
using OpenScct.Launcher.Versioning;

namespace OpenScct.Launcher.Update;

/// <summary>
/// A release whose tag is a semantic version, with the game files release (<see cref="ReleasePicker.StoreTag"/>) when
/// its files are kept there.
/// </summary>
public sealed record VersionedRelease(SemVersion Version, ReleaseInfo Release)
{
    /// <summary>The <see cref="ReleasePicker.StoreTag"/> release, or null when it is not known (or there is none).</summary>
    public ReleaseInfo? Store { get; init; }
}

/// <summary>
/// Picks the release and the files to download. Names written by tools/release/New-Release.ps1: a version release
/// (<c>v0.6.1</c>) carries only <c>OpenSCCT.Launcher-0.6.1-win-x64.exe</c>, so players see one download. Everything
/// else is in the game files release (<see cref="StoreTag"/>): the build's file list
/// <c>OpenSCCT-0.6.1-win-x64-files.json</c>, the files themselves, and one <c>&lt;asset&gt;.sha256</c> per file list
/// and launcher. Older releases with <c>OpenSCCT-0.2.0-win-x64.zip</c> and <c>checksums.txt</c> in the version release
/// still work.
/// </summary>
public static class ReleasePicker
{
    public const string GamePrefix = "OpenSCCT-";
    public const string LauncherPrefix = "OpenSCCT.Launcher-";
    public const string Platform = "win-x64";
    public const string ChecksumsFile = "checksums.txt";

    /// <summary>Tag of the release that holds every version's game files (not a version, so never picked as one).</summary>
    public const string StoreTag = "game-files";

    /// <summary>An asset by name: in the version release first, then in the game files release.</summary>
    private static ReleaseAsset? Find(VersionedRelease release, string name) =>
        release.Release.Assets.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase))
        ?? release.Store?.Assets.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));

    public static string GameAssetName(SemVersion version) => $"{GamePrefix}{version}-{Platform}.zip";

    public static string LauncherAssetName(SemVersion version) => $"{LauncherPrefix}{version}-{Platform}.exe";

    /// <summary>The build's file list (<see cref="BuildFiles"/>), for updates that download only changed files.</summary>
    public static string FilesAssetName(SemVersion version) => $"{GamePrefix}{version}-{Platform}-files.json";

    /// <summary>The file list of the release (its own or in the game files release), or null when there is none.</summary>
    public static ReleaseAsset? FilesAsset(VersionedRelease release)
    {
        ArgumentNullException.ThrowIfNull(release);
        return Find(release, FilesAssetName(release.Version));
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
        return Find(release, GameAssetName(release.Version))
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

    /// <summary>
    /// Where the checksum of <paramref name="asset"/> is published: its own .sha256 file (in the version release or
    /// the game files release) first, then the version release's checksums.txt.
    /// </summary>
    public static ReleaseAsset? ChecksumAsset(VersionedRelease release, ReleaseAsset asset)
    {
        ArgumentNullException.ThrowIfNull(release);
        ArgumentNullException.ThrowIfNull(asset);
        return Find(release, asset.Name + ".sha256")
            ?? release.Release.Assets.FirstOrDefault(a => string.Equals(a.Name, ChecksumsFile, StringComparison.OrdinalIgnoreCase));
    }
}
