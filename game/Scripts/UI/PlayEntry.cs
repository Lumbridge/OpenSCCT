using System;
using Godot;
using OpenScct.Server;
using OpenScctGame.Hud;
using OpenScctGame.Maps;
using OpenScctGame.Play;

namespace OpenScctGame.UI;

/// <summary>What the front end asks the game to start.</summary>
public sealed record PlayRequest
{
    public const int DefaultPort = 28888;

    public string Map { get; init; } = "SteeE";

    public HudSide Side { get; init; } = HudSide.Spy;

    public string PlayerName { get; init; } = "Player";

    /// <summary>Bot players next to the local one (offline and hosted matches).</summary>
    public int Bots { get; init; } = 3;

    public int Port { get; init; } = DefaultPort;

    /// <summary>Host address for a join ("host" or "host:port").</summary>
    public string Address { get; init; } = "";
}

/// <summary>
/// How the front end starts play. The play client registers its own entry in <see cref="PlayEntry.Current"/>
/// (offline, listen host, join by address); until it does, <see cref="DefaultPlayEntry"/> starts a local match
/// through <see cref="GameRoot"/> and reports hosting and joining as unavailable.
/// </summary>
public interface IPlayEntry
{
    bool CanHost { get; }

    bool CanJoin { get; }

    /// <summary>Starts an offline match (local listen server with bots) as the requested side.</summary>
    void PlayOffline(SceneTree tree, PlayRequest request);

    /// <summary>Starts a listen server match that others can join.</summary>
    void Host(SceneTree tree, PlayRequest request);

    /// <summary>Joins a match at <see cref="PlayRequest.Address"/>.</summary>
    void Join(SceneTree tree, PlayRequest request);

    /// <summary>A lobby for a hosted or joined match, or null to use the front end's local lobby.</summary>
    ILobbySession? CreateLobby(PlayRequest request, bool host);
}

/// <summary>The registered play entry.</summary>
public static class PlayEntry
{
    private static IPlayEntry _current = new DefaultPlayEntry();

    public static IPlayEntry Current
    {
        get => _current;
        set => _current = value ?? throw new ArgumentNullException(nameof(value));
    }
}

/// <summary>Starts every match as a local match on the game scene; no network play.</summary>
public sealed class DefaultPlayEntry : IPlayEntry
{
    public bool CanHost => false;

    public bool CanJoin => false;

    public void PlayOffline(SceneTree tree, PlayRequest request)
    {
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentNullException.ThrowIfNull(request);
        // The local match runs any map the locator finds; without converted maps it falls back to TestBox.
        string map = MapLocator.CreateDefault(App.From(tree.Root).Options).Find(request.Map) is not null ? request.Map : MapCatalog.TestBox;
        GameRoot.Enter(tree, new GameConfig { Map = map, Bots = request.Bots });
    }

    public void Host(SceneTree tree, PlayRequest request) => PlayOffline(tree, request);

    public void Join(SceneTree tree, PlayRequest request) =>
        throw new NotSupportedException("joining a match needs the network play client");

    public ILobbySession? CreateLobby(PlayRequest request, bool host) => null;
}
