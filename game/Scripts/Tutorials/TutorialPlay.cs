using System;
using System.Collections.Generic;
using Godot;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.GameFlow;
using OpenScct.Game.SBase;
using OpenScct.Provenance;
using OpenScct.Server;
using OpenScctGame.Play;
using OpenScctGame.Play.Session;
using OpenScctGame.UI;
using GK = OpenScct.Game.GameFlow.K;

namespace OpenScctGame.Tutorials;

/// <summary>
/// Offline play of a tutorial map or a Visit a Map (spec/game/tutorials.md): the player alone on the map, no bots,
/// Story, the offline launch rules (no time limit, unlimited lives, 5 s respawn), through the same listen-server path
/// as Create a Session's offline play. A <see cref="TutorialRoundWatch"/> on the play scene marks a won tutorial done
/// and, after the end delay, plays the next tutorial or goes back to the Tutorials page.
/// </summary>
public static class TutorialPlay
{
    /// <summary>The map can be played here (converted, with its render).</summary>
    public static bool CanPlay(SceneTree tree, string map) =>
        OfflinePlayEntry.PlayableFolder(map, App.From(tree.Root).Options.MapRoots) is not null;

    /// <summary>
    /// Tuto_MapsList.LoadMap and the loading page with a named tutorial: plays <paramref name="map"/> as the side of
    /// <paramref name="team"/> (0 spies, 1 mercs), with the in-game help off.
    /// </summary>
    [PortOf("uc:GUI.Tuto_MapsList:LoadMap", Spec = "spec/game/tutorials.md#rules")]
    public static void PlayTutorial(SceneTree tree, string map, int team) => Start(tree, map, team == 0 ? PlaySide.Spy : PlaySide.Merc, help: false);

    /// <summary>
    /// The loading page's "TutoSpy" / "TutoSentry" with no map named: the team's first tutorial not done, or the
    /// Tutorials page when every one is done (the original then replays the current map; from the menu there is none).
    /// </summary>
    [PortOf("uc:GUI.TutoOrExam:ButtonClick", Spec = "spec/game/tutorials.md#rules")]
    public static void PlayNextTutorial(SceneTree tree, int team)
    {
        TutorialProgress progress = TutorialProfile.Current;
        if (TutorialMaps.NextTutorial(team, i => progress.GetMapTutorialDone(i, team)) is { } next)
        {
            PlayTutorial(tree, next, team);
        }
        else
        {
            TutorialsScreen.Show(tree);
        }
    }

    /// <summary>Visit_Map_Set's Launch: the map alone as <paramref name="side"/>, with the help paths shown.</summary>
    [PortOf("uc:GUI.Visit_Map_Set:InternalOnKeyEvent", Spec = "spec/game/tutorials.md#rules")]
    public static void Visit(SceneTree tree, string map, PlaySide side) => Start(tree, map, side, help: true);

    private static void Start(SceneTree tree, string map, PlaySide side, bool help)
    {
        ArgumentNullException.ThrowIfNull(tree);
        LaunchOptions options = App.From(tree.Root).Options;
        PlayConfig config = PlayConfig.FromOptions(options);
        string name = OpenScctGame.Options.SettingsStore.Current.PlayerName;
        config = config with
        {
            Settings = config.Settings with
            {
                Map = map,
                Side = side,
                Mode = "Story",
                Rules = PlayRules.MapSettings,
                OfflineRules = true,
                Bots = 0,
                PlayerName = string.IsNullOrWhiteSpace(name) ? "Player" : name,
            },
        };

        // Visit_Map_Set sets the profile's in-game help before the map loads (the help paths read it at begin play).
        TutorialProfile.DisplayHelpInGame = help;
        var roots = new List<string>(options.MapRoots);
        roots.AddRange(MapCatalog.CreateDefault().Roots);
        LoadingScreen.Open(tree, map, side == PlaySide.Merc, EGameMode.GM_Multi, new MapCatalog([.. roots]), () =>
        {
            PlayRoot root = PlayRoot.Enter(tree, config);
            root.AddChild(new TutorialRoundWatch { Visit = help });
        });
    }
}

/// <summary>
/// Watches an offline tutorial or visit round on the play scene (SPlayerController GameEnded.PlayerTick's offline
/// branch): once the winner is known, a won tutorial is marked done and saved (MarkTutoAsDone); after the end delay
/// the next spy tutorial plays (the Tutorials page once the exam is done), the next merc tutorial plays (the Tutorials
/// page once all three are done), and a visit goes back to the Tutorials page.
/// </summary>
public partial class TutorialRoundWatch : Node
{
    private bool _ended;
    private double _endedFor;
    private bool _left;

    /// <summary>A Visit a Map round (no tutorial progress).</summary>
    public bool Visit { get; init; }

    [Provenance(ProvenanceKind.Decoded, "SPlayerController GameEnded.PlayerTick, offline tutorial branch (spec/game/tutorials.md rule 3)")]
    public override void _Process(double delta)
    {
        if (_left || GetParent() is not PlayRoot { Session: { IsStopped: false } session })
        {
            return;
        }

        SPlayerController? pc = session.LocalController;
        if (!_ended)
        {
            if (pc?.GameReplicationInfo is not { } gri || gri.WinningTeam == GK.NoResult)
            {
                return;
            }

            _ended = true;
            if (!Visit && TutorialMaps.CurLevelIsTutorial(session.Settings.Map, out int numTut, out int numTeam) &&
                gri.WinningTeam == (int)pc.SPawnType)
            {
                TutorialProfile.Current.MarkTutoAsDone(numTut, numTeam);
                TutorialProfile.Save();
            }

            return;
        }

        _endedFor += delta;
        if (_endedFor > GK.EndMenuDelay)
        {
            _left = true;
            Leave(session);
        }
    }

    /// <summary>Visit a Map ends with the play scene: the next launch starts with the help off again.</summary>
    public override void _ExitTree() => TutorialProfile.DisplayHelpInGame = false;

    private void Leave(PlaySession session)
    {
        SceneTree tree = GetTree();
        Node? scene = GetParent();
        string map = session.Settings.Map;
        session.Stop();
        OpenScctGame.Controls.Pointer.Mode = global::Godot.Input.MouseModeEnum.Visible;
        bool tutorial = TutorialMaps.CurLevelIsTutorial(map, out _, out int team);
        TutorialProgress progress = TutorialProfile.Current;
        if (Visit || !tutorial || (team == 0 && progress.ExamDone) || (team == 1 && progress.AllMercDone()))
        {
            TutorialsScreen.Show(tree);
        }
        else
        {
            TutorialPlay.PlayNextTutorial(tree, team);
        }

        if (scene is not null && IsInstanceValid(scene) && !scene.IsQueuedForDeletion())
        {
            scene.QueueFree();
        }
    }
}
