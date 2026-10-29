using System;
using System.Collections.Generic;
using System.IO;
using OpenScct.Content;
using OpenScct.Server;
using OpenScctGame.Play.Session;

namespace OpenScctGame.Online;

/// <summary>
/// The maps an online client plays, loaded the way a playing machine needs them: every installed map is registered
/// with a <see cref="PlayMapLoader"/> (made on first use), so a round's session loads its map with the movement, camera,
/// device animations and combat layers the play scene draws. The online lobby stays the built-in empty level.
/// </summary>
public sealed class PlayCatalog
{
    private readonly Dictionary<string, PlayMapLoader> _loaders = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public PlayCatalog(IReadOnlyList<string> extraRoots)
    {
        ArgumentNullException.ThrowIfNull(extraRoots);
        var roots = new List<string>(extraRoots);
        roots.AddRange(MapCatalog.CreateDefault().Roots);
        Maps = new MapCatalog(roots.ToArray());

        // A map downloaded while the game runs (the in-game map downloader) plays through the same loader; without it
        // the plain load has no movement service, so the pawns stand in their bind pose.
        Maps.FolderBuilder = (id, folder) => Directory.Exists(Path.Combine(folder, "render"))
            ? (world, isClient) => Loader(id).Load(world, isClient)
            : null;
        foreach (MapListEntry entry in Maps.ListMaps())
        {
            if (entry.Folder is { } folder && Directory.Exists(Path.Combine(folder, "render")))
            {
                string id = entry.Id;
                Maps.Register(id, (world, isClient) => Loader(id).Load(world, isClient));
            }
        }
    }

    /// <summary>The catalog the client sessions load with.</summary>
    public MapCatalog Maps { get; }

    /// <summary>The play loader of a map (its SVMap parsed once).</summary>
    public PlayMapLoader Loader(string map)
    {
        lock (_gate)
        {
            if (!_loaders.TryGetValue(map, out PlayMapLoader? loader))
            {
                string folder = Maps.FindFolder(map) ?? throw new FileNotFoundException($"Map '{map}' is not installed.");
                loader = new PlayMapLoader(SvMap.Load(folder), string.Empty, PlayMapLoader.FindCharsDir(folder));
                _loaders[map] = loader;
            }

            return loader;
        }
    }

    /// <summary>True when the map has the render content the play scene draws.</summary>
    public bool CanRender(string map) =>
        Maps.FindFolder(map) is { } folder && Directory.Exists(Path.Combine(folder, "render"));
}
