using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using OpenScct.Content;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.AI;
using OpenScct.Game.SBase;
using OpenScct.Server;

namespace OpenScct.Bots;

/// <summary>
/// The navigation graphs of the server bots (<see cref="NavGraph"/>), built once per map content hash and side and
/// shared by every bot of a process, with a copy on disk so the next run loads it in a moment. The cache folder is
/// outside the checkout (<c>%LOCALAPPDATA%/SVM/ai-nav</c>, or <c>SVM_AI_NAV_CACHE</c>); nothing derived from a map
/// is written to tracked paths.
/// </summary>
public sealed class AiNavCache
{
    private readonly Dictionary<string, NavGraph> _graphs = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    /// <summary>A cache over <paramref name="directory"/>; null for the default folder, empty for memory only.</summary>
    public AiNavCache(string? directory = null)
    {
        Directory = directory ?? DefaultDirectory();
    }

    /// <summary>The folder of the cache files, or empty when the cache lives in memory only.</summary>
    public string Directory { get; }

    /// <summary>Widening of the sampled box around the map's starts, objectives and GEs (uu): X, Y, below and above.</summary>
    public FVector Margin { get; init; } = new(1200f, 1200f, 700f);

    /// <summary>Graphs built (not loaded) by this cache (diagnostics).</summary>
    public int Built { get; private set; }

    /// <summary>Graphs loaded from disk by this cache.</summary>
    public int Loaded { get; private set; }

    public static string DefaultDirectory()
    {
        string? env = Environment.GetEnvironmentVariable("SVM_AI_NAV_CACHE");
        if (env is not null)
        {
            return env;
        }

        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return string.IsNullOrEmpty(local) ? string.Empty : Path.Combine(local, "SVM", "ai-nav");
    }

    /// <summary>The graph of <paramref name="map"/> for a spy (traversal links) or a merc.</summary>
    public NavGraph Get(MapInfo map, bool spy, byte activationMask)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (map.Physics is not { } physics)
        {
            throw new InvalidOperationException($"Map {map.Name} has no collision to plan on.");
        }

        NavProfile profile = spy ? NavProfile.Spy : NavProfile.Merc;
        string mapKey = string.Create(CultureInfo.InvariantCulture, $"{map.Name}-{map.SimHash}-m{activationMask}");
        string key = mapKey + "|" + profile.Key;
        lock (_gate)
        {
            if (_graphs.TryGetValue(key, out NavGraph? graph))
            {
                return graph;
            }

            string? file = Directory.Length == 0 ? null : Path.Combine(Directory, SafeName(mapKey + "-" + profile.Key) + ".svmnav");
            if (file is not null && File.Exists(file))
            {
                try
                {
                    using FileStream s = File.OpenRead(file);
                    graph = NavGraph.Read(s, mapKey, profile);
                }
                catch (IOException)
                {
                    graph = null;
                }

                if (graph is not null)
                {
                    Loaded++;
                }
            }

            if (graph is null)
            {
                var ges = new List<GERecord>(physics.GEs.Count);
                for (int i = 0; i < physics.GEs.Count; i++)
                {
                    ges.Add(physics.GEs[i]);
                }

                (FVector min, FVector max) = Bounds(map, ges);
                graph = NavBuilder.Build(physics.Collision, min, max, profile, ges, activationMask);
                Built++;
                if (file is not null)
                {
                    TryWrite(file, graph, mapKey);
                }
            }

            _graphs[key] = graph;
            return graph;
        }
    }

    private static void TryWrite(string file, NavGraph graph, string mapKey)
    {
        try
        {
            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            string temp = file + ".tmp";
            using (FileStream s = File.Create(temp))
            {
                graph.Write(s, mapKey);
            }

            File.Move(temp, file, overwrite: true);
        }
        catch (IOException)
        {
            // A cache that cannot be written is rebuilt next time.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static string SafeName(string name)
    {
        char[] chars = name.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            if (Array.IndexOf(Path.GetInvalidFileNameChars(), chars[i]) >= 0 || chars[i] == '|')
            {
                chars[i] = '_';
            }
        }

        return new string(chars);
    }

    /// <summary>
    /// Builds (or loads) both sides' graphs of <paramref name="mapName"/> now, from a scratch server load of the map, so
    /// a real-time host does not stall on its first tick (SteeE takes tens of seconds to build in a Debug build).
    /// </summary>
    public void Prepare(MapCatalog maps, string mapName)
    {
        ArgumentNullException.ThrowIfNull(maps);
        var world = new World(0, ENetMode.NM_DedicatedServer);
        if (!maps.TryLoad(world, mapName, false, out MapInfo? map, out string? error) || map is null)
        {
            throw new InvalidOperationException($"Map {mapName} did not load: {error}");
        }

        byte mask = world.Level.Info.GEActivationMask;
        Get(map, spy: true, mask);
        Get(map, spy: false, mask);
    }

    /// <summary>The box the graph samples: the map's starts, objective actors and GE points, widened by <see cref="Margin"/>.</summary>
    public (FVector Min, FVector Max) Bounds(MapInfo map, IReadOnlyList<GERecord> ges)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(ges);
        var points = new List<FVector>();
        foreach (Actor? a in map.MapActors)
        {
            if (a is PlayerStart or SObjectiveTrigger or SFlagDropZone or SInterrupteur)
            {
                points.Add(a.Location);
            }
        }

        foreach (MapStart s in map.Starts)
        {
            points.Add(s.Location);
        }

        foreach (GERecord ge in ges)
        {
            points.Add(ge.P0);
            points.Add(ge.P1);
        }

        if (points.Count == 0)
        {
            points.Add(default);
        }

        return NavBuilder.BoundsOf(points, Margin);
    }
}
