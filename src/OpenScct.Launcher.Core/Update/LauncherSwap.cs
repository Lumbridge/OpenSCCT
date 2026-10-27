using System;
using System.IO;
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

    /// <summary>Puts the staged exe in place. The caller then starts <see cref="ExePath"/> and exits.</summary>
    public void Swap()
    {
        if (!HasStaged)
        {
            throw new InvalidOperationException("No launcher update is staged.");
        }

        if (File.Exists(OldPath))
        {
            File.Delete(OldPath);
        }

        File.Move(ExePath, OldPath);
        try
        {
            File.Move(NewPath, ExePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            File.Move(OldPath, ExePath);
            throw;
        }
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

        DateTime until = DateTime.UtcNow + waitForOldProcess;
        while (File.Exists(OldPath))
        {
            try
            {
                File.Delete(OldPath);
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
