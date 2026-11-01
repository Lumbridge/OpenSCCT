using System.Collections.Generic;
using Godot;
using OpenScct.Provenance;
using OpenScctGame.Play.Session;
using OpenScctGame.Tutorials;

namespace OpenScctGame.UI;

/// <summary>
/// Tips (GUI.Tutorials_List and Tutorials_Tuto): the list of a side's tips with Switch Team; a tip shows its picture
/// (Tutorials_PC SPY_TutoNN / SENTRY_TutoNN), its legend and its explanation, the key tokens shown as the player's
/// keys; Next moves to the following tip, wrapping to the first.
/// </summary>
public partial class TipsScreen : MenuPage
{
    private const string ListSection = "Tutorials_List";
    private const string TipSection = "Tutorials_Tuto";
    private BoxContainer? _top;
    private TextureRect? _picture;

    /// <summary>0 spies, 1 mercs.</summary>
    public int Team { get; init; }

    /// <summary>The tip shown, or -1 for the list.</summary>
    public int Tip { get; init; } = -1;

    public static TipsScreen Show(SceneTree tree, int team, int tip = -1) => FrontEnd.Show(tree, new TipsScreen { Team = team, Tip = tip });

    /// <summary>The list of a side (TitreExpliATT / TitreExpliDEF).</summary>
    public static IReadOnlyList<string> Titles(int team) => TutorialText.Array(ListSection, team == 0 ? "TitreExpliATT" : "TitreExpliDEF");

    public override void _Ready()
    {
        Name = "Tips";
        string title = Team == 0 ? TutorialText.Caption(ListSection, "TitreATT", "Spy Tips") : TutorialText.Caption(ListSection, "TitreDEF", "Mercenary Tips");
        BuildFrame(title, 900f);
        if (Tip < 0)
        {
            BuildList();
        }
        else
        {
            BuildTip();
        }
    }

    public override void _Process(double delta)
    {
        base._Process(delta);
        if (_top is not null) _top.Vertical = AvailableBodyWidth < 820f;
        if (_picture is not null)
        {
            float side = Mathf.Clamp((GetViewportRect().Size.Y - 156f) * 0.55f, 160f, 360f);
            _picture.CustomMinimumSize = new Vector2(side, side);
        }
    }

    [PortOf("uc:GUI.Tutorials_List:RefreshList", Spec = "spec/game/tutorials.md#rules")]
    [PortOf("uc:GUI.Tutorials_List:ListClick", Spec = "spec/game/tutorials.md#rules")]
    [PortOf("uc:GUI.Tutorials_List:SwitchTeam", Spec = "spec/game/tutorials.md#rules")]
    private void BuildList()
    {
        IReadOnlyList<string> titles = Titles(Team);
        VBoxContainer column = Panel(Body, "List", null, 520f);
        column.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        Button? first = null;
        for (int i = 0; i < titles.Count; i++)
        {
            int tip = i;
            Button b = UiStyle.MenuButton(titles[i], () => Show(GetTree(), Team, tip), "Tip" + i);
            first ??= b;
            column.AddChild(b);
        }

        if (titles.Count == 0)
        {
            column.AddChild(new Label { Text = "The tips need the converted menu texts (the ui content step).", Modulate = UiStyle.Dim, AutowrapMode = TextServer.AutowrapMode.WordSmart });
        }

        PageButton("Back", () => TutorialsScreen.Show(GetTree()), "Back");
        PageButton(TutorialText.Caption(ListSection, "ButtonY", "Switch Team"), () => Show(GetTree(), Team == 0 ? 1 : 0), "SwitchTeam");
        (first ?? PageButtons[0]).GrabFocus();
    }

    [PortOf("uc:GUI.Tutorials_Tuto:RefreshAll", Spec = "spec/game/tutorials.md#rules")]
    [PortOf("uc:GUI.Tutorials_Tuto:NextTutorial", Spec = "spec/game/tutorials.md#rules")]
    private void BuildTip()
    {
        string side = Team == 0 ? "SPY" : "SENTRY";
        var top = _top = new BoxContainer { Name = "Top" };
        top.AddThemeConstantOverride("separation", 24);
        Body.AddChild(top);
        var texture = UiStyle.Assets?.Texture("Tutorials_PC", $"{side}_Tuto{Tip + 1:00}");
        _picture = new TextureRect
        {
            Name = "Picture", CustomMinimumSize = new Vector2(360f, 360f), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            Texture = texture, Visible = texture is not null,
        };
        top.AddChild(_picture);
        VBoxContainer legend = Panel(top, "Legend", null);
        ((Control)legend.GetParent()).SizeFlagsHorizontal = SizeFlags.ExpandFill;
        legend.AddChild(new Label
        {
            Name = "Text", Text = TutorialText.Lines(TutorialText.Item(TipSection, side + "_TutoLegend", Tip)),
            AutowrapMode = TextServer.AutowrapMode.WordSmart, Modulate = UiStyle.MenuFocus,
        });
        PlaySide playSide = Team == 0 ? PlaySide.Spy : PlaySide.Merc;
        Body.AddChild(new Label
        {
            Name = "Explanation", Text = TutorialText.WithKeys(TutorialText.Item(TipSection, side + "_TutoExpli", Tip), playSide),
            AutowrapMode = TextServer.AutowrapMode.WordSmart, Modulate = UiStyle.MenuText,
        });

        int count = Titles(Team).Count;
        PageButton("Back", () => Show(GetTree(), Team), "Back");
        Button next = PageButton(TutorialText.Caption(TipSection, "ButtonA", "Next"), () => Show(GetTree(), Team, count == 0 || Tip + 1 >= count ? 0 : Tip + 1), "Next");
        next.GrabFocus();
    }
}
