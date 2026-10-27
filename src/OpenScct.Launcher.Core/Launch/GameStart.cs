using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace OpenScct.Launcher.Launch;

/// <summary>
/// How Play starts the game. A plain start opens the main menu, so no arguments are needed. When the original game
/// path is set, it is passed as <c>--svm-game-root=</c> (key layouts) and as SVM_GAME_ROOT. When content built by
/// the launcher exists, its folder (shared/ and svmap/) is passed as <c>--svm-content=</c>. Godot reads user
/// arguments after "--".
/// </summary>
public static class GameStart
{
    public static ProcessStartInfo Create(string exePath, string? originalGamePath, string? contentRoot, IEnumerable<string>? extraUserArgs = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(exePath);
        var info = new ProcessStartInfo(exePath)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(exePath)!,
        };

        var user = new List<string>();
        if (!string.IsNullOrWhiteSpace(originalGamePath))
        {
            user.Add("--svm-game-root=" + originalGamePath);
            info.Environment["SVM_GAME_ROOT"] = originalGamePath;
        }

        if (!string.IsNullOrWhiteSpace(contentRoot) && Directory.Exists(contentRoot))
        {
            user.Add("--svm-content=" + contentRoot);
        }

        if (extraUserArgs is not null)
        {
            user.AddRange(extraUserArgs);
        }

        if (user.Count > 0)
        {
            info.ArgumentList.Add("--");
            foreach (string arg in user)
            {
                info.ArgumentList.Add(arg);
            }
        }

        return info;
    }
}
