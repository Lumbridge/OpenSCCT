using Godot;
using OpenScct.Game.GameFlow;
using OpenScct.Provenance;
using OpenScctGame.Tutorials;

namespace OpenScctGame.UI;

/// <summary>
/// Tutorials (GUI.Tutorials_Page; spec/game/tutorials.md, docs/MENUS.md "Tutorials"): View HUD, Tips, Gadget Details,
/// Spy Tutorial Maps, Mercenary Tutorial Maps and Visit a Map, with a line of the player's progress (ours).
/// </summary>
public partial class TutorialsScreen : MenuPage
{
    public const string Section = "Tutorials_Page";

    /// <summary>The main menu's Tutorials button (Menu_Multi's Tutorials).</summary>
    public static Button MenuButton(Node from) =>
        UiStyle.MenuButton(TutorialText.Caption(Section, "TitlePage", "Tutorials"), () => Show(from.GetTree()), "Tutorials");

    public static TutorialsScreen Show(SceneTree tree) => FrontEnd.Show(tree, new TutorialsScreen());

    [PortOf("uc:GUI.Tutorials_Page:ButtonClick", Spec = "spec/game/tutorials.md#rules")]
    public override void _Ready()
    {
        Name = "Tutorials";
        // Menu_Multi.InitComponent turns the in-game help off; the menus here open from it, so it is off again.
        TutorialProfile.DisplayHelpInGame = false;
        BuildFrame(TutorialText.Caption(Section, "TitlePage", "Tutorials"), 520f);
        VBoxContainer column = Panel(Body, "Pages", null);
        Button first = Add(column, "Tuto_Hud", "View HUD", "ViewHud", () => HudLegendScreen.Show(GetTree(), HudLegendPage.SpyHud));
        Add(column, "Tuto_Tips", "Tips", "Tips", () => TipsScreen.Show(GetTree(), 0));
        Add(column, "Tuto_Gadgets", "Gadget Details", "Gadgets", () => GadgetDetailsScreen.Show(GetTree(), 0));
        Add(column, "Tuto_Maps_Spy", "Spy Tutorial Maps", "SpyMaps", () => TutorialMapsScreen.Show(GetTree(), 0));
        Add(column, "Tuto_Maps_Merc", "Mercenary Tutorial Maps", "MercMaps", () => TutorialMapsScreen.Show(GetTree(), 1));
        Add(column, "Tuto_VisitMap", "Visit a Map", "VisitMap", () => VisitMapScreen.Show(GetTree()));

        Body.AddChild(new Label
        {
            Name = "Progress", Text = ProgressLine(TutorialProfile.Current), HorizontalAlignment = HorizontalAlignment.Center,
            Modulate = UiStyle.MenuFocus, AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });
        PageButton("Back", () => FrontEnd.ShowMainMenu(GetTree()), "Back");
        first.GrabFocus();
    }

    private static Button Add(VBoxContainer column, string control, string fallback, string name, System.Action pressed)
    {
        Button b = UiStyle.MenuButton(TutorialText.Caption(Section, control + ".Caption", fallback), pressed, name);
        column.AddChild(b);
        return b;
    }

    /// <summary>"Spy tutorials 2 of 4, exam passed. Mercenary tutorials 1 of 3."</summary>
    public static string ProgressLine(TutorialProgress p)
    {
        int spy = 0;
        for (int i = 0; i < TutorialMaps.ExamIndex; i++)
        {
            spy += p.GetMapTutorialDone(i, 0) ? 1 : 0;
        }

        int merc = 0;
        for (int i = 0; i < TutorialMaps.Merc.Count; i++)
        {
            merc += p.GetMapTutorialDone(i, 1) ? 1 : 0;
        }

        return $"Spy tutorials {spy} of {TutorialMaps.ExamIndex}, exam {(p.ExamDone ? "passed" : "not passed")}. " +
            $"Mercenary tutorials {merc} of {TutorialMaps.Merc.Count}.";
    }
}
