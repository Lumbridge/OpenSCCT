using System;
using System.IO;
using System.Linq;
using System.Threading;

namespace OpenScct.Launcher.Update;

/// <summary>
/// Replaces the launcher's own exe. Windows lets a running exe be renamed but not overwritten, so the swap is:
/// the verified new exe is staged as <c>&lt;exe&gt;.new</c>; on restart the running exe is renamed to
/// <c>&lt;exe&gt;.old</c>, the new one takes its name and is started, and that new process deletes the .old file.
/// A swap cut short is finished (or undone) the next time the launcher starts.
/// </summary>
public sealed class LauncherSwap
{
    public LauncherSwap(string exePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(exePath);
        ExePath = Path.GetFullPath(exePath);
    }

    public string ExePath { get; }

    public string NewPath => ExePath + ".new";

    public string OldPath => ExePath + ".old";

    public bool HasStaged => File.Exists(NewPath);

    /// <summary>Copies a verified download next to the exe. Throws when the launcher folder is not writable.</summary>
    public void Stage(string verifiedFile)
    {
        ArgumentException.ThrowIfNullOrEmpty(verifiedFile);
        string temp = NewPath + ".tmp";
        File.Copy(verifiedFile, temp, overwrite: true);
        File.Move(temp, NewPath, overwrite: true);
    }

    /// <summary>
    /// Where the staged exe goes. A launcher still named as it was downloaded (<c>OpenSCCT.Launcher-0.8.0-win-x64.exe</c>)
    /// takes the name of the version it updates to, so the file name never shows an old version; any other name (a
    /// player's own, or <c>OpenSCCT.Launcher.exe</c>) is kept, as is the name when a file already has the new one.
    /// </summary>
    public string TargetPath(Versioning.SemVersion? stagedVersion)
    {
        string name = Path.GetFileName(ExePath);
        bool versionedName = name.StartsWith(ReleasePicker.LauncherPrefix, StringComparison.OrdinalIgnoreCase)
            && name.EndsWith("-" + ReleasePicker.Platform + ".exe", StringComparison.OrdinalIgnoreCase);
        if (stagedVersion is null || !versionedName)
        {
            return ExePath;
        }

        string target = Path.Combine(Path.GetDirectoryName(ExePath)!, ReleasePicker.LauncherAssetName(stagedVersion));
        return string.Equals(target, ExePath, StringComparison.OrdinalIgnoreCase) || File.Exists(target) ? ExePath : target;
    }

    /// <summary>The version stamped into the staged exe (its product version), or null when it cannot be read.</summary>
    public Versioning.SemVersion? StagedVersion()
    {
        if (!HasStaged)
        {
            return null;
        }

        string? product = System.Diagnostics.FileVersionInfo.GetVersionInfo(NewPath).ProductVersion;
        int plus = product?.IndexOf('+', StringComparison.Ordinal) ?? -1;
        return Versioning.SemVersion.TryParse(plus >= 0 ? product![..plus] : product, out Versioning.SemVersion? v) ? v : null;
    }

    /// <summary>
    /// Puts the staged exe in place, under <see cref="TargetPath"/> for its version. Returns the path to start; the
    /// caller starts it and exits.
    /// </summary>
    public string Swap()
    {
        if (!HasStaged)
        {
            throw new InvalidOperationException("No launcher update is staged.");
        }

        string target = TargetPath(StagedVersion());
        if (File.Exists(OldPath))
        {
            File.Delete(OldPath);
        }

        File.Move(ExePath, OldPath);
        try
        {
            File.Move(NewPath, target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            File.Move(OldPath, ExePath);
            throw;
        }

        return target;
    }

    /// <summary>
    /// Run at start: finishes a swap that stopped between the two renames and removes the old exe once the process
    /// that ran it has exited. Never throws; a file still locked is removed on a later start.
    /// </summary>
    public void CleanupAfterStart(TimeSpan waitForOldProcess)
    {
        try
        {
            if (!File.Exists(ExePath) && File.Exists(NewPath))
            {
                File.Move(NewPath, ExePath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        // The old exe is this one's .old, or, after an update that renamed the launcher to its new version, another
        // launcher's .old in the same folder.
        string[] olds;
        try
        {
            olds = Directory.GetFiles(Path.GetDirectoryName(ExePath)!, ReleasePicker.LauncherPrefix + "*.exe.old");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            olds = [];
        }

        DateTime until = DateTime.UtcNow + waitForOldProcess;
        foreach (string old in olds.Append(OldPath).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            while (File.Exists(old))
            {
                try
                {
                    File.Delete(old);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    if (DateTime.UtcNow >= until)
                    {
                        return;
                    }

                    Thread.Sleep(200);
                }
            }
        }
    }

    /// <summary>Whether the launcher can replace itself where it is (not under a read-only Program Files folder).</summary>
    public bool CanWriteFolder()
    {
        string probe = Path.Combine(Path.GetDirectoryName(ExePath)!, ".write-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            File.WriteAllBytes(probe, []);
            File.Delete(probe);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
