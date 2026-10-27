using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using OpenScct.Content;

namespace OpenScctGame.Maps;

/// <summary>
/// Finds SVMap folders by map id. A root is a folder of map folders (each with a manifest.json); a map matches
/// when its folder name or its manifest's map id equals the requested id, ignoring ASCII case as UE2 names do.
/// <para>Default roots, in order: every <c>--svm-maps=dir</c>, <c>maps</c> next to the executable, and when not
/// running an export, the repository's <c>fixtures</c> and <c>.content/maps</c> next to the project.</para>
/// </summary>
public sealed class MapLocator
{
    public MapLocator(IEnumerable<string> roots)
    {
        ArgumentNullException.ThrowIfNull(roots);
        var list = new List<string>();
        foreach (string root in roots)
        {
            if (!string.IsNullOrWhiteSpace(root))
            {
                list.Add(Path.GetFullPath(root));
            }
        }

        Roots = list;
    }

    public IReadOnlyList<string> Roots { get; }

    public static MapLocator CreateDefault(LaunchOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var roots = new List<string>(options.MapRoots);
        roots.AddRange(OpenScct.Server.ContentPaths.MapRoots);
        string executableDir = Path.GetDirectoryName(OS.GetExecutablePath()) ?? ".";
        roots.Add(Path.Combine(executableDir, "maps"));
        if (!OS.HasFeature("template"))
        {
            string projectDir = ProjectSettings.GlobalizePath("res://");
            string repoDir = Path.GetFullPath(Path.Combine(projectDir, ".."));
            roots.Add(Path.Combine(repoDir, "fixtures"));
            roots.Add(Path.Combine(repoDir, ".content", "maps"));
        }

        return new MapLocator(roots);
    }

    /// <summary>The folder of map <paramref name="mapId"/>, or null when no root has it.</summary>
    public string? Find(string mapId)
    {
        ArgumentException.ThrowIfNullOrEmpty(mapId);
        foreach (string root in Roots)
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            // A root may itself be a map folder.
            if (IsMap(root, mapId, matchFolderName: false))
            {
                return root;
            }

            string[] folders = Directory.GetDirectories(root);
            Array.Sort(folders, StringComparer.Ordinal);
            foreach (string folder in folders)
            {
                if (AsciiCaseComparer.AreEqual(Path.GetFileName(folder), mapId) && IsMap(folder, mapId, matchFolderName: true))
                {
                    return folder;
                }
            }

            foreach (string folder in folders)
            {
                if (IsMap(folder, mapId, matchFolderName: false))
                {
                    return folder;
                }
            }
        }

        return null;
    }

    private static bool IsMap(string folder, string mapId, bool matchFolderName)
    {
        string manifest = Path.Combine(folder, SvMapHash.ManifestFile);
        if (!File.Exists(manifest))
        {
            return false;
        }

        if (matchFolderName)
        {
            return true;
        }

        try
        {
            return AsciiCaseComparer.AreEqual(SvMapManifest.Load(manifest).Map, mapId);
        }
        catch (SvMapFormatException)
        {
            return false;
        }
    }
}
