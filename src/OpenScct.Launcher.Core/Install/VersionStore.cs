using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using OpenScct.Launcher.Versioning;

namespace OpenScct.Launcher.Install;

/// <summary>
/// The installed game builds: one folder per version under versions\, a manifest naming the current and the
/// previous one. Installing never touches a version that is already there, so a running game keeps its files.
/// </summary>
public sealed class VersionStore
{
    public const string GameExe = "OpenSCCT.exe";
    private const string StagingPrefix = ".staging-";
    private const string TrashPrefix = ".trash-";

    private readonly LauncherLayout _layout;
    private readonly TimeProvider _time;

    public VersionStore(LauncherLayout layout, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(layout);
        _layout = layout;
        _time = time ?? TimeProvider.System;
    }

    public InstallManifest LoadManifest() => InstallManifest.Load(_layout.Manifest);

    /// <summary>Every complete version folder, newest first.</summary>
    public IReadOnlyList<SemVersion> Installed()
    {
        if (!Directory.Exists(_layout.Versions))
        {
            return [];
        }

        var found = new List<SemVersion>();
        foreach (string dir in Directory.EnumerateDirectories(_layout.Versions))
        {
            string name = Path.GetFileName(dir);
            if (SemVersion.TryParse(name, out SemVersion? version)
                && string.Equals(version.ToString(), name, StringComparison.Ordinal)
                && File.Exists(Path.Combine(dir, GameExe)))
            {
                found.Add(version);
            }
        }

        found.Sort((a, b) => b.CompareTo(a));
        return found;
    }

    /// <summary>The version Play starts: the manifest's current if it is still complete, else the newest installed.</summary>
    public SemVersion? Current()
    {
        IReadOnlyList<SemVersion> installed = Installed();
        InstallManifest manifest = LoadManifest();
        if (SemVersion.TryParse(manifest.Current, out SemVersion? current) && installed.Contains(current))
        {
            return current;
        }

        return installed.Count > 0 ? installed[0] : null;
    }

    public SemVersion? Previous()
    {
        InstallManifest manifest = LoadManifest();
        return SemVersion.TryParse(manifest.Previous, out SemVersion? previous) && Installed().Contains(previous) ? previous : null;
    }

    public string ExePath(SemVersion version) => Path.Combine(_layout.VersionDir(version.ToString()), GameExe);

    public bool IsInstalled(SemVersion version) => File.Exists(ExePath(version));

    /// <summary>
    /// Extracts a downloaded build into its own folder next to the others. The zip is unpacked into a staging
    /// folder first and renamed into place in one step, so a version folder is either complete or absent.
    /// A zip with one top-level folder around the build is accepted. The new version is not made current here.
    /// </summary>
    public string InstallFromZip(string zipPath, SemVersion version)
    {
        ArgumentException.ThrowIfNullOrEmpty(zipPath);
        ArgumentNullException.ThrowIfNull(version);
        string target = _layout.VersionDir(version.ToString());
        if (IsInstalled(version))
        {
            return target;
        }

        string staging = NewStagingDir(version);
        try
        {
            // ExtractToDirectory refuses entries that would land outside the folder.
            ZipFile.ExtractToDirectory(zipPath, staging);
            string payload = FindPayload(staging)
                ?? throw new InvalidDataException($"The build has no {GameExe} at its top level.");
            MoveIntoPlace(payload, version);
        }
        finally
        {
            DiscardStaging(staging);
        }

        return target;
    }

    /// <summary>A new, empty staging folder for <paramref name="version"/> beside the version folders (pruned if left behind).</summary>
    public string NewStagingDir(SemVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);
        Directory.CreateDirectory(_layout.Versions);
        string staging = Path.Combine(_layout.Versions, StagingPrefix + version + "-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(staging);
        return staging;
    }

    /// <summary>
    /// Renames a complete staged build (with <see cref="GameExe"/> at its top) to its version folder in one step.
    /// The new version is not made current here.
    /// </summary>
    public string CommitStaging(string staging, SemVersion version)
    {
        ArgumentException.ThrowIfNullOrEmpty(staging);
        ArgumentNullException.ThrowIfNull(version);
        if (!File.Exists(Path.Combine(staging, GameExe)))
        {
            throw new InvalidDataException($"The build has no {GameExe} at its top level.");
        }

        MoveIntoPlace(staging, version);
        return _layout.VersionDir(version.ToString());
    }

    /// <summary>Deletes a staging folder that was not committed (nothing when it is gone already).</summary>
    public void DiscardStaging(string staging)
    {
        if (Directory.Exists(staging))
        {
            TryDelete(staging);
        }
    }

    private void MoveIntoPlace(string payload, SemVersion version)
    {
        string target = _layout.VersionDir(version.ToString());
        if (Directory.Exists(target))
        {
            // An incomplete folder from an older launcher or a manual copy: set it aside, it is pruned later.
            Directory.Move(target, Path.Combine(_layout.Versions, TrashPrefix + version + "-" + Guid.NewGuid().ToString("N")[..8]));
        }

        Directory.Move(payload, target);
    }

    /// <summary>Makes <paramref name="version"/> current and keeps the one it replaces for rollback.</summary>
    public void Activate(SemVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);
        if (!IsInstalled(version))
        {
            throw new InvalidOperationException($"Version {version} is not installed.");
        }

        InstallManifest manifest = LoadManifest();
        SemVersion? old = Current();
        if (old is not null && old == version)
        {
            if (manifest.Pending is not null)
            {
                manifest.Pending = null;
                manifest.Save(_layout.Manifest);
            }

            return;
        }

        manifest.Previous = old?.ToString() ?? manifest.Previous;
        manifest.Current = version.ToString();
        manifest.Pending = null;
        manifest.SwitchedAt = _time.GetUtcNow();
        manifest.Save(_layout.Manifest);
    }

    /// <summary>Records an installed version that becomes current when the game is closed.</summary>
    public void MarkPending(SemVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);
        InstallManifest manifest = LoadManifest();
        manifest.Pending = version.ToString();
        manifest.Save(_layout.Manifest);
    }

    /// <summary>The pending version if it is still installed and newer than the current one.</summary>
    public SemVersion? Pending()
    {
        InstallManifest manifest = LoadManifest();
        return SemVersion.TryParse(manifest.Pending, out SemVersion? pending) && IsInstalled(pending) && pending > Current() ? pending : null;
    }

    /// <summary>Switches back to the previous version; the current one becomes the new previous. False when there is none.</summary>
    public bool Rollback()
    {
        SemVersion? previous = Previous();
        SemVersion? current = Current();
        if (previous is null || current is null || previous == current)
        {
            return false;
        }

        var manifest = new InstallManifest
        {
            Current = previous.ToString(),
            Previous = current.ToString(),
            SwitchedAt = _time.GetUtcNow(),
        };
        manifest.Save(_layout.Manifest);
        return true;
    }

    /// <summary>
    /// Deletes version folders other than the current and the previous one, and leftovers of interrupted installs.
    /// Folders in <paramref name="inUse"/> (a game is running from them) are kept. Returns what was deleted.
    /// </summary>
    public IReadOnlyList<string> Prune(IReadOnlyCollection<string> inUse)
    {
        ArgumentNullException.ThrowIfNull(inUse);
        if (!Directory.Exists(_layout.Versions))
        {
            return [];
        }

        var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (Current() is { } current)
        {
            keep.Add(current.ToString());
        }

        if (Previous() is { } previous)
        {
            keep.Add(previous.ToString());
        }

        var inUseFull = new HashSet<string>(inUse.Select(p => Path.TrimEndingDirectorySeparator(Path.GetFullPath(p))), StringComparer.OrdinalIgnoreCase);
        var deleted = new List<string>();
        foreach (string dir in Directory.EnumerateDirectories(_layout.Versions))
        {
            string name = Path.GetFileName(dir);
            bool leftover = name.StartsWith(StagingPrefix, StringComparison.Ordinal) || name.StartsWith(TrashPrefix, StringComparison.Ordinal);
            bool oldVersion = SemVersion.TryParse(name, out _) && !keep.Contains(name);
            if ((!leftover && !oldVersion) || inUseFull.Contains(Path.TrimEndingDirectorySeparator(Path.GetFullPath(dir))))
            {
                continue;
            }

            if (TryDelete(dir))
            {
                deleted.Add(dir);
            }
        }

        return deleted;
    }

    /// <summary>The version folder a running exe belongs to, or null when it is not one of ours.</summary>
    public string? VersionDirOf(string exePath)
    {
        string full = Path.GetFullPath(exePath);
        string versions = Path.TrimEndingDirectorySeparator(Path.GetFullPath(_layout.Versions)) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(versions, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string rest = full[versions.Length..];
        int sep = rest.IndexOf(Path.DirectorySeparatorChar, StringComparison.Ordinal);
        return sep <= 0 ? null : Path.Combine(_layout.Versions, rest[..sep]);
    }

    private static string? FindPayload(string staging)
    {
        if (File.Exists(Path.Combine(staging, GameExe)))
        {
            return staging;
        }

        string[] dirs = Directory.GetDirectories(staging);
        string[] files = Directory.GetFiles(staging);
        if (dirs.Length == 1 && files.Length == 0 && File.Exists(Path.Combine(dirs[0], GameExe)))
        {
            return dirs[0];
        }

        return null;
    }

    private bool TryDelete(string dir)
    {
        try
        {
            SafeDelete.Tree(dir, _layout.Versions);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Files still open (an antivirus scan, a game we did not see): try again on the next prune.
            return false;
        }
    }
}
