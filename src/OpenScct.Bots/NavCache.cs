using System;
using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Server;

namespace OpenScct.Bots;

/// <summary>
/// The walk graphs of the maps bots play, built once per map and side and shared by every bot of a process. The
/// sampled area is the box around the map's player starts and objective actors, widened by
/// <see cref="Margin"/>; nothing about the map is stored outside the run.
/// </summary>
public sealed class NavCache
{
    private readonly Dictionary<(string Map, bool Spy), NavGrid> _grids = new();
    private readonly object _gate = new();

    /// <summary>Widening of the sampled box around the map's starts and objectives (uu): X, Y, below, above.</summary>
    public FVector Margin { get; init; } = new(1200f, 1200f, 700f);

    /// <summary>The grid of <paramref name="map"/> for a spy (or a merc, which takes shorter drops).</summary>
    public NavGrid Get(MapInfo map, bool spy)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (map.Physics is not { } physics)
        {
            throw new InvalidOperationException($"Map {map.Name} has no collision to plan on.");
        }

        lock (_gate)
        {
            if (_grids.TryGetValue((map.SimHash, spy), out NavGrid? grid))
            {
                return grid;
            }

            (FVector min, FVector max) = Bounds(map);
            grid = NavGrid.Build(physics.Collision, min, max, spy
                ? new NavGridSettings { MaxDrop = BotK.SpyMaxDrop, Height = BotK.SpyHeight }
                : new NavGridSettings { MaxDrop = BotK.MercMaxDrop });
            _grids[(map.SimHash, spy)] = grid;
            return grid;
        }
    }

    /// <summary>
    /// Builds both sides' grids of <paramref name="mapName"/> now, from a scratch client load of the map as a bot
    /// sees it (terminals blocking players or not, as the match will play), so a real-time match does not stall on
    /// its first frame (SteeE takes tens of seconds in a Debug build).
    /// </summary>
    public void Prepare(MapCatalog maps, string mapName, bool terminalsBlockPlayers = true)
    {
        ArgumentNullException.ThrowIfNull(maps);
        var world = new World(0, ENetMode.NM_Client);
        if (!maps.TryLoad(world, mapName, true, out MapInfo? map, out string? error) || map is null)
        {
            throw new InvalidOperationException($"Map {mapName} did not load: {error}");
        }

        if (!terminalsBlockPlayers)
        {
            BotMatch.UnblockTerminalsOf(world);
        }

        Get(map, spy: true);
        Get(map, spy: false);
    }

    private (FVector Min, FVector Max) Bounds(MapInfo map)
    {
        bool any = false;
        FVector min = default;
        FVector max = default;
        foreach (Actor? a in map.MapActors)
        {
            if (a is not (PlayerStart or SObjectiveTrigger or SFlagDropZone or SInterrupteur))
            {
                continue;
            }

            FVector p = a.Location;
            min = any ? new FVector(MathF.Min(min.X, p.X), MathF.Min(min.Y, p.Y), MathF.Min(min.Z, p.Z)) : p;
            max = any ? new FVector(MathF.Max(max.X, p.X), MathF.Max(max.Y, p.Y), MathF.Max(max.Z, p.Z)) : p;
            any = true;
        }

        if (!any)
        {
            foreach (MapStart s in map.Starts)
            {
                min = any ? new FVector(MathF.Min(min.X, s.Location.X), MathF.Min(min.Y, s.Location.Y), MathF.Min(min.Z, s.Location.Z)) : s.Location;
                max = any ? new FVector(MathF.Max(max.X, s.Location.X), MathF.Max(max.Y, s.Location.Y), MathF.Max(max.Z, s.Location.Z)) : s.Location;
                any = true;
            }
        }

        return (new FVector(min.X - Margin.X, min.Y - Margin.Y, min.Z - Margin.Z),
            new FVector(max.X + Margin.X, max.Y + Margin.Y, max.Z + Margin.Z));
    }
}
