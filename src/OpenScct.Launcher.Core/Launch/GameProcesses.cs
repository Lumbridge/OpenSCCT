using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;

namespace OpenScct.Launcher.Launch;

/// <summary>A running game process and the exe it was started from, when Windows lets us read it.</summary>
public sealed record RunningGame(int ProcessId, string? ExePath);

/// <summary>Finds running game processes, so the launcher never deletes or replaces files a game has open.</summary>
public interface IGameProcessProbe
{
    IReadOnlyList<RunningGame> Running();
}

public sealed class GameProcessProbe : IGameProcessProbe
{
    private static readonly string[] Names = ["OpenSCCT", "OpenSCCT.console"];

    public IReadOnlyList<RunningGame> Running()
    {
        var found = new List<RunningGame>();
        foreach (string name in Names)
        {
            foreach (Process process in Process.GetProcessesByName(name))
            {
                using (process)
                {
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
