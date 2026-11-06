using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;

namespace OpenScct.Launcher.Launch;

/// <summary>A running game process and the exe it was started from, when the operating system lets us read it.</summary>
public sealed record RunningGame(int ProcessId, string? ExePath);

/// <summary>Finds running game processes, so the launcher never deletes or replaces files a game has open.</summary>
public interface IGameProcessProbe
{
    IReadOnlyList<RunningGame> Running();
}

public sealed class GameProcessProbe : IGameProcessProbe
{
    private readonly string[] _names;

    public GameProcessProbe(LauncherPlatform? platform = null)
    {
        _names = ProcessNames(platform ?? LauncherPlatform.Current);
    }

    internal static string[] ProcessNames(LauncherPlatform platform) => platform.IsLinux
        ? ["OpenSCCT.x86_64", "OpenSCCT"]
        : ["OpenSCCT", "OpenSCCT.console"];

    public IReadOnlyList<RunningGame> Running()
    {
        var found = new List<RunningGame>();
        var seen = new HashSet<int>();
        foreach (string name in _names)
        {
            foreach (Process process in Process.GetProcessesByName(name))
            {
                using (process)
                {
                    if (!seen.Add(process.Id))
                    {
                        continue;
                    }

                    string? path = null;
                    try
                    {
                        path = process.MainModule?.FileName;
                    }
                    catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
                    {
                        // Unknown path: callers treat it as using every version.
                    }

                    found.Add(new RunningGame(process.Id, path));
                }
            }
        }

        return found;
    }
}
