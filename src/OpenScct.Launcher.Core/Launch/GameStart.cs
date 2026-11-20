using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using OpenScct.Launcher.Settings;

namespace OpenScct.Launcher.Launch;

/// <summary>
/// Starts Godot with the selected local content generation. User arguments (including website Join) follow "--".
/// </summary>
public static class GameStart
{
    public static ProcessStartInfo Create(string exePath, IEnumerable<string>? extraUserArgs = null, string? graphicsDriver = null,
        string? contentRoot = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(exePath);
        var info = new ProcessStartInfo(exePath)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(exePath)!,
        };

        info.Environment.Remove("SVM_GAME_ROOT");
        info.Environment.Remove("SVM_CONTENT");

        if (graphicsDriver is not null)
        {
            info.ArgumentList.Add("--rendering-driver");
            info.ArgumentList.Add(GameRendererSettings.ToDriverName(graphicsDriver));
        }

        var user = new List<string>();

        if (contentRoot is not null)
            user.Add("--svm-content=" + Path.GetFullPath(contentRoot));

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
