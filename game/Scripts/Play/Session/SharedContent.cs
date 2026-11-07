using System;
using System.IO;
using OpenScct.Server;

namespace OpenScctGame.Play.Session;

/// <summary>
/// Finds the shared content packs (characters, render pack, fx, audio) a map folder plays with. A converted map sits in
/// <c>.content/svmap/&lt;Map&gt;</c> with <c>.content/shared</c> two folders up; a map kept in the repository (the hub and
/// test maps in <c>fixtures/</c>) has no shared folder beside it, so it uses the checkout's <c>.content/shared</c>.
/// </summary>
public static class SharedContent
{
    /// <summary>
    /// The shared folder for <paramref name="mapFolder"/>: the configured content's shared folder, else
    /// <c>shared</c> two folders up, else the <c>.content/shared</c>
    /// of the checkout holding the map or the running program, or null when none exists.
    /// </summary>
    public static string? FindRoot(string mapFolder)
    {
        ArgumentException.ThrowIfNullOrEmpty(mapFolder);
        // A downloaded map can sit beside older shared packs. Keep using the content selected
        // for this game (including a checkout's new weapons), rather than that incidental copy.
        if (ContentPaths.Root is { } root && Directory.Exists(Path.Combine(root, "shared")))
        {
            return Path.Combine(root, "shared");
        }

        string beside = Path.GetFullPath(Path.Combine(mapFolder, "..", "..", "shared"));
        // A checkout also has shared/ProjectLinks.cs. That source directory must
        // not hide the actual character/root-motion packs in .content/shared.
        if (Directory.Exists(Path.Combine(beside, "chars")) || Directory.Exists(Path.Combine(beside, "render")) ||
            Directory.Exists(Path.Combine(beside, "fx")) || Directory.Exists(Path.Combine(beside, "audio")))
        {
            return beside;
        }

        // A map shipped with an installed build (the hub): the content the launcher built.
        if (ContentPaths.Shared is { } content)
        {
            return content;
        }

        foreach (string start in new[] { mapFolder, AppContext.BaseDirectory })
        {
            if (MapCatalog.FindRepoRoot(start) is { } repo &&
                Path.Combine(repo, ".content", "shared") is { } shared && Directory.Exists(shared))
            {
                return shared;
            }
        }

        return null;
    }
}
