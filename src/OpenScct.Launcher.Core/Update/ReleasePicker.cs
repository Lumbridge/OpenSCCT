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
/// Picks platform-specific release assets. Launcher downloads have a stable filename and an independent
/// version in their label; their checksums retain that version in the name. Game manifests, blobs and
/// checksums may live in the shared <see cref="StoreTag"/> release. Older versioned launcher names and
/// full game ZIPs remain supported. Omitted platform arguments use the current operating system.
/// </summary>
public static class ReleasePicker
{
    public const string GamePrefix = "OpenSCCT-";
    public const string LauncherPrefix = "OpenSCCT.Launcher-";

    /// <summary>The launcher's file name, in releases and on disk.</summary>
    public static string LauncherExeName => LauncherPlatform.Current.LauncherFileName;

    /// <summary>Start of the launcher asset's label, followed by its version (<c>OpenSCCT Launcher 0.8.8</c>).</summary>
    public const string LauncherLabelPrefix = "OpenSCCT Launcher ";
    public static string Platform => LauncherPlatform.Current.RuntimeIdentifier;
    public const string ChecksumsFile = "checksums.txt";

    /// <summary>Tag of the release that holds every version's game files (not a version, so never picked as one).</summary>
    public const string StoreTag = "game-files";

    /// <summary>An asset by name: in the version release first, then in the game files release.</summary>
    private static ReleaseAsset? Find(VersionedRelease release, string name) =>
        release.Release.Assets.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase))
        ?? release.Store?.Assets.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));

    public static string GameAssetName(SemVersion version, LauncherPlatform? platform = null) => $"{GamePrefix}{version}-{(platform ?? LauncherPlatform.Current).RuntimeIdentifier}.zip";

    /// <summary>The launcher's old versioned name, still used for its checksum file in the game files release.</summary>
    public static string LauncherAssetName(SemVersion version, LauncherPlatform? platform = null) => $"{LauncherPrefix}{version}-{(platform ?? LauncherPlatform.Current).RuntimeIdentifier}{(platform ?? LauncherPlatform.Current).ExecutableSuffix}";

    public static string LauncherChecksumName(SemVersion version, LauncherPlatform? platform = null) => LauncherAssetName(version, platform) + ".sha256";

    /// <summary>The build's file list (<see cref="BuildFiles"/>), for updates that download only changed files.</summary>
    public static string FilesAssetName(SemVersion version, LauncherPlatform? platform = null) => $"{GamePrefix}{version}-{(platform ?? LauncherPlatform.Current).RuntimeIdentifier}-files.json";

    /// <summary>The file list of the release (its own or in the game files release), or null when there is none.</summary>
    public static ReleaseAsset? FilesAsset(VersionedRelease release, LauncherPlatform? platform = null)
    {
        ArgumentNullException.ThrowIfNull(release);
        return Find(release, FilesAssetName(release.Version, platform));
    }

    /// <summary>
    /// The newest semantic version; excludes drafts and optionally prereleases. An explicit platform also
    /// requires a matching game or launcher asset, looking in the supplied shared store for game manifests.
    /// </summary>
    public static VersionedRelease? Latest(IEnumerable<ReleaseInfo> releases, bool includePrereleases, LauncherPlatform? platform = null, ReleaseInfo? store = null)
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

            var candidate = new VersionedRelease(version, release) { Store = store };
            if (platform is not null && FilesAsset(candidate, platform) is null
                && GameAsset(candidate, platform) is null && LauncherAsset(candidate, platform) is null)
            {
                continue;
            }

            if (best is null || version > best.Version)
            {
                best = candidate;
            }
        }

        return best;
    }

    /// <summary>The game ZIP: the exact name first, then a legacy ZIP for the selected platform.</summary>
    public static ReleaseAsset? GameAsset(VersionedRelease release, LauncherPlatform? platform = null)
    {
        ArgumentNullException.ThrowIfNull(release);
        List<ReleaseAsset> assets = release.Release.Assets;
        return Find(release, GameAssetName(release.Version, platform))
            ?? assets.FirstOrDefault(a =>
                a.Name.StartsWith(GamePrefix, StringComparison.OrdinalIgnoreCase)
                && a.Name.EndsWith($"-{(platform ?? LauncherPlatform.Current).RuntimeIdentifier}.zip", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The launcher exe of the release, or null when the release does not carry one. The launcher has its own version
    /// (LauncherVersion in Directory.Build.props), in the asset's label (or, in older releases, its name): a game
    /// release carries the current launcher, often an older version than the game's. With several, the newest.
    /// </summary>
    public static ReleaseAsset? LauncherAsset(VersionedRelease release, LauncherPlatform? platform = null) => Launcher(release, platform)?.Asset;

    /// <summary>The version of the release's launcher exe (from its label or name), or null when it carries none.</summary>
    public static SemVersion? LauncherVersion(VersionedRelease release, LauncherPlatform? platform = null) => Launcher(release, platform)?.Version;

    private static (ReleaseAsset Asset, SemVersion Version)? Launcher(VersionedRelease release, LauncherPlatform? platform = null)
    {
        ArgumentNullException.ThrowIfNull(release);
        (ReleaseAsset Asset, SemVersion Version)? best = null;
        platform ??= LauncherPlatform.Current;
        string suffix = $"-{platform.RuntimeIdentifier}{platform.ExecutableSuffix}";
        foreach (ReleaseAsset asset in release.Release.Assets)
        {
            string name = asset.Name;
            SemVersion? version = null;
            if (string.Equals(name, platform.LauncherFileName, StringComparison.OrdinalIgnoreCase))
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
    public static ReleaseAsset? ChecksumAsset(VersionedRelease release, ReleaseAsset asset, LauncherPlatform? platform = null)
    {
        ArgumentNullException.ThrowIfNull(release);
        ArgumentNullException.ThrowIfNull(asset);
        if (string.Equals(asset.Name, (platform ?? LauncherPlatform.Current).LauncherFileName, StringComparison.OrdinalIgnoreCase)
            && Launcher(release, platform) is { } launcher
            && ReferenceEquals(launcher.Asset, asset)
            && Find(release, LauncherChecksumName(launcher.Version, platform)) is { } versioned)
        {
            return versioned;
        }

        return Find(release, asset.Name + ".sha256")
            ?? release.Release.Assets.FirstOrDefault(a => string.Equals(a.Name, ChecksumsFile, StringComparison.OrdinalIgnoreCase));
    }
}
