using System.Collections.Generic;
using System;
using Godot;
using OpenScct.Game.GameFlow;
using OpenScct.Provenance;
using OpenScctGame.Tutorials;

namespace OpenScctGame.UI;

/// <summary>
/// Spy or Mercenary Tutorial Maps (GUI.Tuto_MapsList): one row per tutorial map of the side, a tick when it is done
/// (the profile's GetMapTutorialDone; the exam row ticks with bExamDone) and its name; pressing a row plays that map
/// alone as the side. The spy page lists four tutorials and the exam; the merc page shows rows 1, 3 and 4 of the
/// page (Movements and Basic Actions, Objectives, Alarms...) for its three maps.
/// </summary>
public partial class TutorialMapsScreen : MenuPage
{
    public const string Section = "Tuto_MapsList";
    private ScrollContainer? _scroll;

    protected override bool CenterBodyVertically => true;
    protected override Vector2 ResponsiveReference => new(1600f, 900f);

    /// <summary>The page's caption control for each map of a side (Tuto_MapsList.RefreshPage: the merc page hides rows 2 and 5).</summary>
    [Provenance(ProvenanceKind.Decoded, "GUI.Tuto_MapsList.RefreshPage and ButtonClick: merc maps 0, 1, 2 on rows 0, 2, 3")]
    private static readonly int[][] Rows = [[0, 1, 2, 3, 4], [0, 2, 3]];

    private static readonly string[] RowFallback =
        ["Movements and Basic Actions", "Enhanced Reality", "Objectives", "Alarms, Isolations, and Gadgets", "Exam"];

    /// <summary>0 spies, 1 mercs.</summary>
    public int Team { get; init; }

    public static TutorialMapsScreen Show(SceneTree tree, int team) => FrontEnd.Show(tree, new TutorialMapsScreen { Team = team });

    [PortOf("uc:GUI.Tuto_MapsList:RefreshPage", Spec = "spec/game/tutorials.md#rules")]
    [PortOf("uc:GUI.Tuto_MapsList:ButtonClick", Spec = "spec/game/tutorials.md#rules")]
    public override void _Ready()
    {
        Name = Team == 0 ? "SpyTutorialMaps" : "MercTutorialMaps";
        BuildFrame(Team == 0 ? TutorialText.Caption(Section, "TitleSpy", "Spy Tutorial Maps") : TutorialText.Caption(Section, "TitleMerc", "Mercenary Tutorial Maps"), 1000f, responsive: true);
        Body.AddThemeConstantOverride("separation", 14);
        TutorialProgress progress = TutorialProfile.Current;
        IReadOnlyList<string> maps = TutorialMaps.Of(Team);
        VBoxContainer column = Panel(Body, "Maps", Team == 0 ? "Spy training" : "Mercenary training");
        column.AddThemeConstantOverride("separation", 10);
        var grid = new MapBrowserGrid();
        column.AddChild(grid.ViewToggle);
        var scroll = _scroll = new ScrollContainer { Name = "MapScroll", FollowFocus = true, CustomMinimumSize = new Vector2(0, 360), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        column.AddChild(scroll);
        scroll.AddChild(grid);
        Button? first = null;
        for (int i = 0; i < maps.Count; i++)
        {
            int row = Rows[Team][i];
            string map = maps[i];
            bool done = Team == 0 && i == TutorialMaps.ExamIndex ? progress.ExamDone : progress.GetMapTutorialDone(i, Team);
            string title = TutorialText.Caption(Section, "TutoMap" + (row + 1) + ".Caption", RowFallback[row]);
            Button b = grid.AddMap(MapImages.Default, map, title + (done ? "  (Done)" : "  (Not done yet)"));
            b.Pressed += () => TutorialPlay.PlayTutorial(GetTree(), map, Team);
            if (!TutorialPlay.CanPlay(GetTree(), map))
            {
                b.Disabled = true;
                b.TooltipText = map + " is not installed (convert the maps first).";
            }

            first ??= b.Disabled ? null : b;
        }

        Fit();
        PageButton("Back", () => TutorialsScreen.Show(GetTree()), "Back");
        (first ?? PageButtons[0]).GrabFocus();
    }

    public override void _Process(double delta)
    {
        base._Process(delta);
        Fit();
    }

    private void Fit()
    {
        if (_scroll is not null)
            _scroll.CustomMinimumSize = new Vector2(0f, Math.Clamp(LayoutSize.Y - 240f, 240f, 480f));
    }
}
