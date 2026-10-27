using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using OpenScct.Net;
using OpenScctGame.Online;

namespace OpenScctGame.Play.Demo;

/// <summary>A demo file on this machine, as the Demos screen lists it.</summary>
public sealed record DemoEntry(string Path, DemoHeader Header, DemoIndex Index, long Bytes)
{
    public string FileName => System.IO.Path.GetFileName(Path);

    public TimeSpan Duration => TimeSpan.FromSeconds(Index.DurationTicks / (double)Math.Max(1, Header.TickRate));

    public DateTimeOffset Recorded => DateTimeOffset.FromUnixTimeMilliseconds(Header.StartUnixMs);
}

/// <summary>
/// Where demos live on this machine (docs/DEMOS.md): <c>user://demos</c> for kept ones (offline recordings, files
/// copied in by hand, downloads the player kept) and <c>user://demos/cache</c> for downloads, and the way into
/// playback.
/// </summary>
public static class DemoLibrary
{
    /// <summary>Kept demos.</summary>
    public static string SavedFolder => ProjectSettings.GlobalizePath("user://demos");

    /// <summary>Downloaded demos (the master keeps them 7 days; the cache is cleaned on the same schedule).</summary>
    public static string CacheFolder => ProjectSettings.GlobalizePath("user://demos/cache");

    /// <summary>The demos in <paramref name="folder"/>, newest first; unreadable files are skipped.</summary>
    public static List<DemoEntry> List(string folder)
    {
        var list = new List<DemoEntry>();
        if (!Directory.Exists(folder))
        {
            return list;
        }

        foreach (string file in Directory.EnumerateFiles(folder, "*" + DemoFile.Extension))
        {
            try
            {
                (DemoHeader header, DemoIndex index) = DemoFile.ReadInfo(file);
                list.Add(new DemoEntry(file, header, index, new FileInfo(file).Length));
            }
            catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or EndOfStreamException)
            {
                GD.Print($"SVM_DEMO skipped {file}: {e.Message}");
            }
        }

        list.Sort(static (a, b) => b.Header.StartUnixMs.CompareTo(a.Header.StartUnixMs));
        return list;
    }

    /// <summary>Moves a cached download into the kept folder (it then outlives the cache).</summary>
    public static string Keep(string cachedPath)
    {
        Directory.CreateDirectory(SavedFolder);
        string target = System.IO.Path.Combine(SavedFolder, System.IO.Path.GetFileName(cachedPath));
        if (!string.Equals(System.IO.Path.GetFullPath(cachedPath), System.IO.Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
        {
            File.Move(cachedPath, target, overwrite: true);
        }

        return target;
    }

    /// <summary>Deletes cached downloads older than <paramref name="keep"/>.</summary>
    public static void PruneCache(TimeSpan keep)
    {
        if (!Directory.Exists(CacheFolder))
        {
            return;
        }

        DateTime cutoff = DateTime.UtcNow - keep;
        foreach (string file in Directory.EnumerateFiles(CacheFolder, "*" + DemoFile.Extension))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(file) < cutoff)
                {
                    File.Delete(file);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                GD.Print($"SVM_DEMO cache file {file} not removed: {e.Message}");
            }
        }
    }

    /// <summary>
    /// Opens <paramref name="path"/> and enters its playback. On a failure (a damaged file, another game version, a
    /// map not installed) it goes to the main menu with the reason and returns false.
    /// </summary>
    public static bool Watch(SceneTree tree, string path)
    {
        ArgumentNullException.ThrowIfNull(tree);
        DemoPlayer player;
        try
        {
            player = DemoPlayer.Open(path);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or EndOfStreamException)
        {
            Fail(tree, $"Could not open the demo: {e.Message}");
            return false;
        }

        return Enter(tree, player);
    }

    /// <summary>Enters the play scene for <paramref name="player"/> (a new demo, or a restart for a seek back).</summary>
    public static bool Enter(SceneTree tree, DemoPlayer player)
    {
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentNullException.ThrowIfNull(player);
        App app = tree.Root.GetNode<App>(App.NodePath);
        try
        {
            PlayConfig config = player.CreateConfig(new PlayCatalog(app.Options.MapRoots), [.. app.Options.MapRoots], app.Options.GameRoot);
            PlayRoot.Enter(tree, config);
            return true;
        }
        catch (InvalidOperationException e)
        {
            player.Dispose();
            Fail(tree, e.Message);
            return false;
        }
    }

    private static void Fail(SceneTree tree, string message)
    {
        GD.PushWarning("SVM_DEMO " + message);
        UI.MainMenu menu = UI.FrontEnd.ShowMainMenu(tree);
        menu.ShowStatus(message);
    }
}
