using System;
using System.Collections.Generic;
using Godot;
using OpenScct.Core;
using OpenScct.Server;
using OpenScctGame.Options;
using OpenScctGame.Play;
using OpenScctGame.Play.Session;
using OpenScctGame.UI;

namespace OpenScctGame.Tutorials;

/// <summary>The Tutorials menu's interactive drills use the same simulation as the wiki's browser lessons.</summary>
public static class InteractiveLessonPlay
{
    public static void Start(SceneTree tree, string kind)
    {
        if (kind is not ("vision-cancelling" or "spy-grab")) throw new ArgumentOutOfRangeException(nameof(kind));
        if (OperatingSystem.IsBrowser())
        {
            BrowserPlatform.StartRound?.Invoke(kind);
            return;
        }

        LaunchOptions options = App.From(tree.Root).Options;
        var roots = new List<string>(options.MapRoots);
        roots.AddRange(MapCatalog.CreateDefault().Roots);
        var settings = new PlaySettings
        {
            Map = "TestRange", MapRoots = [.. roots], Side = PlaySide.Spy,
            Rules = PlayRules.PlayLevel, HostMode = PlayHostMode.Inline,
            Bots = kind == "spy-grab" ? 1 : 0, StartAt = BrowserLesson.Start(kind),
            PlayerName = SettingsStore.Current.PlayerName,
        };
        LoadingScreen.Open(tree, "TestRange", false, EGameMode.GM_Multi, new MapCatalog([.. roots]), () =>
        {
            PlayRoot root = PlayRoot.Enter(tree, new PlayConfig { Settings = settings, GameRoot = options.GameRoot });
            root.AddChild(new InteractiveLessonCoach { Kind = kind });
        });
    }
}
