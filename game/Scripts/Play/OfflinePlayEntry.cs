using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using OpenScct.Server;
using OpenScctGame.Hud;
using OpenScctGame.Play.Session;
using OpenScctGame.UI;

namespace OpenScctGame.Play;

/// <summary>
/// The front end's offline play: a converted map (an SVMap folder with its render) starts the play scene through
/// the listen server path (<see cref="PlayRoot"/>) as the requested side; anything else (TestBox, maps without a
/// render) keeps the local match of <see cref="DefaultPlayEntry"/>. Hosting for others and joining are not part of
/// this entry yet.
/// </summary>
public sealed class OfflinePlayEntry : IPlayEntry
{
    private readonly DefaultPlayEntry _fallback = new();

    public bool CanHost => false;

    public bool CanJoin => false;

    /// <summary>Registers this entry as the front end's play entry.</summary>
    public static void Register() => PlayEntry.Current = new OfflinePlayEntry();

    /// <summary>The SVMap folder of <paramref name="map"/> when it can be played here (it has sim and render content), else null.</summary>
    public static string? PlayableFolder(string map, IReadOnlyList<string> extraRoots) => PlayableFolder(map, Catalog(extraRoots));

    private static string? PlayableFolder(string map, MapCatalog catalog)
    {
        string? folder = catalog.FindFolder(map);
        return folder is not null && Directory.Exists(Path.Combine(folder, "render")) ? folder : null;
    }

    /// <summary>A catalog of <paramref name="extraRoots"/> and the default roots.</summary>
    private static MapCatalog Catalog(IReadOnlyList<string> extraRoots)
    {
        ArgumentNullException.ThrowIfNull(extraRoots);
        var roots = new List<string>(extraRoots);
        roots.AddRange(MapCatalog.CreateDefault().Roots);
        return new MapCatalog([.. roots]);
    }

    public void PlayOffline(SceneTree tree, PlayRequest request)
    {
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentNullException.ThrowIfNull(request);
        LaunchOptions options = App.From(tree.Root).Options;
        MapCatalog catalog = Catalog(options.MapRoots);
        if (PlayableFolder(request.Map, catalog) is null)
        {
            _fallback.PlayOffline(tree, request);
            return;
        }

        PlayConfig config = PlayConfig.FromOptions(options);
        config = config with
        {
            Settings = config.Settings with
            {
                Map = request.Map,
                Side = request.Side == HudSide.Merc ? PlaySide.Merc : PlaySide.Spy,
                PlayerName = string.IsNullOrWhiteSpace(request.PlayerName) ? "Player" : request.PlayerName,
            },
        };
        // The loading page with the map's picture is drawn first; the play scene then loads behind it and replaces it.
        LoadingScreen.Open(tree, request.Map, request.Side == HudSide.Merc, LoadingScreen.ModeOf(config.Settings.Mode), catalog,
            () => PlayRoot.Enter(tree, config));
    }

    public void Host(SceneTree tree, PlayRequest request) => PlayOffline(tree, request);

    public void Join(SceneTree tree, PlayRequest request) => _fallback.Join(tree, request);

    public ILobbySession? CreateLobby(PlayRequest request, bool host) => null;
}
