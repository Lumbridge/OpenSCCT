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
/// (<c>v0.6.1</c>, the game's version) carries only the launcher, <c>OpenSCCT.Launcher.exe</c>, so players see one
/// download. Its name has no version, so it never changes on disk; the launcher's own version is in the asset's label
/// (<c>OpenSCCT Launcher 0.8.8</c>). Everything else is in the game files release (<see cref="StoreTag"/>): the
/// build's file list <c>OpenSCCT-0.6.1-win-x64-files.json</c>, the files themselves, one <c>&lt;asset&gt;.sha256</c>
/// per file list, and the launcher's <c>OpenSCCT.Launcher-0.8.8-win-x64.exe.sha256</c> (named by its version, since
/// every version shares that release). Older releases with a versioned launcher name
/// (<c>OpenSCCT.Launcher-0.6.0-win-x64.exe</c>), <c>OpenSCCT-0.2.0-win-x64.zip</c> or <c>checksums.txt</c> still work.
/// </summary>
public static class ReleasePicker
{
    public const string GamePrefix = "OpenSCCT-";
    public const string LauncherPrefix = "OpenSCCT.Launcher-";

    /// <summary>The launcher's file name, in releases and on disk.</summary>
    public const string LauncherExeName = "OpenSCCT.Launcher.exe";

    /// <summary>Start of the launcher asset's label, followed by its version (<c>OpenSCCT Launcher 0.8.8</c>).</summary>
    public const string LauncherLabelPrefix = "OpenSCCT Launcher ";
    public const string Platform = "win-x64";
    public const string ChecksumsFile = "checksums.txt";

    /// <summary>Tag of the release that holds every version's game files (not a version, so never picked as one).</summary>
    public const string StoreTag = "game-files";

    /// <summary>An asset by name: in the version release first, then in the game files release.</summary>
    private static ReleaseAsset? Find(VersionedRelease release, string name) =>
        release.Release.Assets.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase))
        ?? release.Store?.Assets.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));

    public static string GameAssetName(SemVersion version) => $"{GamePrefix}{version}-{Platform}.zip";

    /// <summary>The launcher's old versioned name, still used for its checksum file in the game files release.</summary>
    public static string LauncherAssetName(SemVersion version) => $"{LauncherPrefix}{version}-{Platform}.exe";

    public static string LauncherChecksumName(SemVersion version) => LauncherAssetName(version) + ".sha256";

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

    /// <summary>
    /// The launcher exe of the release, or null when the release does not carry one. The launcher has its own version
    /// (LauncherVersion in Directory.Build.props), in the asset's label (or, in older releases, its name): a game
    /// release carries the current launcher, often an older version than the game's. With several, the newest.
    /// </summary>
    public static ReleaseAsset? LauncherAsset(VersionedRelease release) => Launcher(release)?.Asset;

    /// <summary>The version of the release's launcher exe (from its label or name), or null when it carries none.</summary>
    public static SemVersion? LauncherVersion(VersionedRelease release) => Launcher(release)?.Version;

    private static (ReleaseAsset Asset, SemVersion Version)? Launcher(VersionedRelease release)
    {
        ArgumentNullException.ThrowIfNull(release);
        (ReleaseAsset Asset, SemVersion Version)? best = null;
        string suffix = $"-{Platform}.exe";
        foreach (ReleaseAsset asset in release.Release.Assets)
        {
            string name = asset.Name;
            SemVersion? version = null;
            if (string.Equals(name, LauncherExeName, StringComparison.OrdinalIgnoreCase))
            {
                string? label = asset.Label?.Trim();
                if (label is null
                    || !label.StartsWith(LauncherLabelPrefix, StringComparison.OrdinalIgnoreCase)
                    || !SemVersion.TryParse(label[LauncherLabelPrefix.Length..].Trim(), out version))
                {
                    continue;
                }
            }
            else if (!name.StartsWith(LauncherPrefix, StringComparison.OrdinalIgnoreCase)
                || !name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
                || name.Length <= LauncherPrefix.Length + suffix.Length
                || !SemVersion.TryParse(name[LauncherPrefix.Length..^suffix.Length], out version))
            {
                continue;
            }

            if (best is null || version > best.Value.Version)
            {
                best = (asset, version!);
            }
        }

        return best;
    }

    /// <summary>
    /// Where the checksum of <paramref name="asset"/> is published: its own .sha256 file (in the version release or
    /// the game files release; for the launcher, the one named by its version) first, then the version release's
    /// checksums.txt.
    /// </summary>
    public static ReleaseAsset? ChecksumAsset(VersionedRelease release, ReleaseAsset asset)
    {
        ArgumentNullException.ThrowIfNull(release);
        ArgumentNullException.ThrowIfNull(asset);
        if (string.Equals(asset.Name, LauncherExeName, StringComparison.OrdinalIgnoreCase)
            && Launcher(release) is { } launcher
            && ReferenceEquals(launcher.Asset, asset)
            && Find(release, LauncherChecksumName(launcher.Version)) is { } versioned)
        {
            return versioned;
        }

        return Find(release, asset.Name + ".sha256")
            ?? release.Release.Assets.FirstOrDefault(a => string.Equals(a.Name, ChecksumsFile, StringComparison.OrdinalIgnoreCase));
    }
}
