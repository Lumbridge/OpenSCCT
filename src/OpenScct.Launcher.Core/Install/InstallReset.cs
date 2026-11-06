using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenScct.Launcher.Editor;
using OpenScct.Launcher.Launch;

namespace OpenScct.Launcher.Install;

/// <summary>
/// Puts the launcher back to a fresh install: removes every game version, download, cache, the content built from the
/// player's install, downloaded maps and content packs, and install.json. Keeps launcher.json and never touches
/// the game's user:// data, which is not under the root. A folder holding the
/// running launcher exe is left alone.
/// </summary>
public static class InstallReset
{
    /// <summary>Force closes the game and installed editor tools, then waits briefly for their handles to close.</summary>
    public static Task ForceCloseProcessesAsync(LauncherLayout layout) => ForceCloseProcessesAsync(layout, Process.GetProcessesByName);

    internal static async Task ForceCloseProcessesAsync(LauncherLayout layout, Func<string, Process[]> findProcesses)
    {
        if (!OperatingSystem.IsWindows() || EditorService.IsUpdateInProgress(layout.Editor)) return;

        string[] gameNames = GameProcessProbe.ProcessNames(LauncherPlatform.WindowsX64);
        string editorExe = Path.Combine(layout.Editor, EditorService.ExeName);
        string cliExe = Path.Combine(layout.Editor, "ose", "ose.exe");
        string[] processNames = [.. gameNames, Path.GetFileNameWithoutExtension(editorExe), Path.GetFileNameWithoutExtension(cliExe)];
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await Task.WhenAll(processNames.SelectMany(findProcesses).Select(CloseAsync)).ConfigureAwait(false);

        async Task CloseAsync(Process process)
        {
            using (process)
            {
                try
                {
                    if (process.Id == Environment.ProcessId) return;
                    // Keep a handle open so the kill stays tied to this process if its PID is reused.
                    _ = process.SafeHandle;
                    string? exe = process.MainModule?.FileName;
                    if (exe is null) return;

                    bool game = gameNames.Contains(Path.GetFileNameWithoutExtension(exe), StringComparer.OrdinalIgnoreCase);
                    bool editorTool = exe.Equals(editorExe, StringComparison.OrdinalIgnoreCase)
                        || exe.Equals(cliExe, StringComparison.OrdinalIgnoreCase);
                    if (!game && (!editorTool || EditorService.IsUpdateInProgress(layout.Editor))) return;

                    process.Kill();
                    await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (timeout.IsCancellationRequested) { }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
                {
                    // The process may already have exited or be inaccessible. The removal guard checks again.
                }
            }
        }
    }

    /// <summary>Deletes what the launcher installed. Returns the paths it could not delete (in use or denied).</summary>
    public static IReadOnlyList<string> Run(LauncherLayout layout, string? launcherExe)
    {
        ArgumentNullException.ThrowIfNull(layout);
        string? exeDir = launcherExe is null ? null : Path.GetDirectoryName(Path.GetFullPath(launcherExe));
        var failed = new List<string>();

        foreach (string dir in new[] { layout.Versions, layout.Downloads, layout.Cache, layout.Content, layout.Maps, layout.Packs })
        {
            if (exeDir is not null && IsSameOrUnder(exeDir, dir))
            {
                continue;
            }

            try
            {
                SafeDelete.Tree(dir, layout.Root);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failed.Add(dir);
            }
        }

        try
        {
            File.Delete(layout.Manifest);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            failed.Add(layout.Manifest);
        }

        return failed;
    }

    private static bool IsSameOrUnder(string path, string dir)
    {
        string p = Path.TrimEndingDirectorySeparator(path);
        string d = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dir));
        return p.Equals(d, StringComparison.OrdinalIgnoreCase)
            || p.StartsWith(d + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
