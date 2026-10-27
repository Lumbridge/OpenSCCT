using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using OpenScct.Server;

namespace OpenScctGame.Smoke;

/// <summary>
/// Where the smokes find the checkout's tracked files (fixtures, tests/scenarios) and its local/ folder, in the editor
/// run (<c>--path game</c>) and in an exported build. An export's <c>res://</c> is the folder of its exe, so
/// <c>res://..</c> is not the checkout there (local\testbuild gave local\fixtures\...): the checkout is the folder above
/// the exe or the game assembly that holds OpenSCCT.slnx, as <see cref="MapCatalog.FindRepoRoot"/> finds it for maps.
/// </summary>
public static class SmokePaths
{
    private static readonly Lazy<string?> Repo = new(FindRepo);

    /// <summary>The checkout root, or null when the build is not inside one.</summary>
    public static string? RepoRoot => Repo.Value;

    /// <summary>
    /// A tracked file of the checkout (<paramref name="relative"/>, e.g. fixtures/testrange/stations.json). Outside a
    /// checkout, the copy shipped next to the exe at the same relative path (tools/ci/godot-smoke.ps1 -Export copies
    /// what the smokes need there). The checkout's path when neither exists, so the error names it.
    /// </summary>
    public static string Tracked(string relative)
    {
        ArgumentException.ThrowIfNullOrEmpty(relative);
        var candidates = new List<string>();
        if (RepoRoot is { } repo)
        {
            candidates.Add(Path.Combine(repo, relative));
        }

        candidates.Add(Path.Combine(ExecutableDir, relative));
        foreach (string candidate in candidates)
        {
            if (File.Exists(candidate) || Directory.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        return Path.GetFullPath(candidates[0]);
    }

    /// <summary>A path under the checkout's local/ folder (next to the exe when the build is not in a checkout).</summary>
    public static string Local(params string[] parts)
    {
        ArgumentNullException.ThrowIfNull(parts);
        string root = RepoRoot ?? ExecutableDir;
        return Path.GetFullPath(Path.Combine([root, "local", .. parts]));
    }

    private static string ExecutableDir => Path.GetDirectoryName(OS.GetExecutablePath()) ?? ".";

    private static string? FindRepo()
    {
        if (!OS.HasFeature("template"))
        {
            // The editor run: the project is the checkout's game/ folder.
            string fromProject = Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), ".."));
            if (File.Exists(Path.Combine(fromProject, "OpenSCCT.slnx")))
            {
                return fromProject;
            }
        }

        return MapCatalog.FindRepoRoot(ExecutableDir) ?? MapCatalog.FindRepoRoot(AppContext.BaseDirectory);
    }
}
