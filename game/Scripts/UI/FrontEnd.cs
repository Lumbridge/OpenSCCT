using System;
using Godot;

namespace OpenScctGame.UI;

/// <summary>
/// Moves between the front-end screens (game/Scenes/UI): main menu, options, lobby and results. Each call replaces
/// the current scene, as <c>GameRoot.Enter</c> does for the game scene, and frees the mouse pointer (a round that ends
/// or is left while the pointer is captured must not leave the menus without one).
/// </summary>
public static class FrontEnd
{
    public const string MainMenuScene = "res://Scenes/UI/MainMenu.tscn";
    public const string OptionsScene = "res://Scenes/UI/Options.tscn";
    public const string LobbyScene = "res://Scenes/UI/Lobby.tscn";
    public const string ResultsScene = "res://Scenes/UI/Results.tscn";

    public static MainMenu ShowMainMenu(SceneTree tree) => Enter<MainMenu>(tree, MainMenuScene, null);

    /// <summary>The options screen (OpenScctGame.Options.OptionsMenu: keys, mouse, controller, display, audio, HUD).</summary>
    public static Control ShowOptions(SceneTree tree) => OpenScctGame.Options.OptionsMenu.Show(tree);

    /// <summary>The lobby: the Lobby Room page for an online session, the offline lobby otherwise.</summary>
    public static Control ShowLobby(SceneTree tree, ILobbySession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return session is IOnlineLobbySession online
            ? Show(tree, new LobbyRoomScreen { Session = online })
            : Enter<LobbyScreen>(tree, LobbyScene, s => s.Session = session);
    }

    /// <summary>The results of a finished match; the play client calls this when the round ends.</summary>
    public static ResultsScreen ShowResults(SceneTree tree, MatchResult result) =>
        Enter<ResultsScreen>(tree, ResultsScene, s => s.Result = result ?? throw new ArgumentNullException(nameof(result)));

    /// <summary>The results of an online round, with the visit's next-round and lobby choices.</summary>
    public static ResultsScreen ShowResults(SceneTree tree, MatchResult result, OpenScct.Server.Online.OnlineClient online) =>
        Enter<ResultsScreen>(tree, ResultsScene, s =>
        {
            s.Result = result ?? throw new ArgumentNullException(nameof(result));
            s.Online = online;
        });

    /// <summary>The server browser (the master's list, the LAN and the saved addresses).</summary>
    public static ServerBrowserScreen ShowServerBrowser(SceneTree tree) => Show(tree, new ServerBrowserScreen());

    /// <summary>The server browser, starting a Discord Join to a listed server once it is up (docs/DISCORD.md).</summary>
    public static ServerBrowserScreen ShowServerBrowser(SceneTree tree, string discordMasterId) =>
        Show(tree, new ServerBrowserScreen { DiscordJoinId = discordMasterId });

    /// <summary>The server browser, joining a listed server by its master id at once (rejoining a round).</summary>
    public static ServerBrowserScreen ShowServerBrowser(SceneTree tree, string masterId, string name, string? password) =>
        Show(tree, new ServerBrowserScreen { DiscordJoinId = masterId, JoinIdName = name, JoinIdPassword = password });

    /// <summary>Joining a server or changing maps.</summary>
    public static ConnectingScreen ShowConnecting(SceneTree tree, OpenScctGame.Online.OnlineService service) =>
        Show(tree, new ConnectingScreen { Service = service });

    /// <summary>Join with IP (Server Address, Server Port).</summary>
    public static JoinWithIpScreen ShowJoinWithIp(SceneTree tree) => Show(tree, new JoinWithIpScreen());

    /// <summary>The Account page (sign in with Discord, the account's name, stats and achievements).</summary>
    public static AccountScreen ShowAccount(SceneTree tree) => Show(tree, new AccountScreen());

    public static StatsScreen ShowStats(SceneTree tree) => Show(tree, new StatsScreen());

    /// <summary>Achievements (OpenSCCT's own): every achievement, what to do and what it unlocks.</summary>
    public static AchievementsScreen ShowAchievements(SceneTree tree) => Show(tree, new AchievementsScreen());

    /// <summary>The Armoury (OpenSCCT's own): the cosmetics the achievements unlock.</summary>
    public static ArmouryScreen ShowArmoury(SceneTree tree) => Show(tree, new ArmouryScreen());

    /// <summary>Demos (OpenSCCT's own): recorded matches to watch (docs/DEMOS.md).</summary>
    public static DemosScreen ShowDemos(SceneTree tree) => Show(tree, new DemosScreen());

    /// <summary>Content (OpenSCCT's own): download, update and remove maps and shared packs (docs/CONTENT-PACKS.md).</summary>
    public static ContentScreen ShowContent(SceneTree tree) => Show(tree, new ContentScreen());

    /// <summary>Create Session (map, mode, the session's options and rules, hosting).</summary>
    public static HostScreen ShowHost(SceneTree tree) => Show(tree, new HostScreen());

    /// <summary>Makes a screen built in code the current scene.</summary>
    public static T Show<T>(SceneTree tree, T screen)
        where T : Node
    {
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentNullException.ThrowIfNull(screen);
        OpenScctGame.Controls.Pointer.Release();
        Node? previous = tree.CurrentScene;
        tree.Root.AddChild(screen);
        tree.CurrentScene = screen;
        previous?.QueueFree();
        return screen;
    }

    /// <summary>Instantiates a screen scene, sets it up before it enters the tree, and makes it the current scene.</summary>
    public static T Enter<T>(SceneTree tree, string scene, Action<T>? setup)
        where T : Node
    {
        ArgumentNullException.ThrowIfNull(tree);
        T screen = GD.Load<PackedScene>(scene).Instantiate<T>();
        setup?.Invoke(screen);
        OpenScctGame.Controls.Pointer.Release();
        Node? previous = tree.CurrentScene;
        tree.Root.AddChild(screen);
        tree.CurrentScene = screen;
        previous?.QueueFree();
        return screen;
    }
}
