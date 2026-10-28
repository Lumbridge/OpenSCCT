using System.Collections.Generic;
using Godot;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.SBase;
using OpenScct.Provenance;
using OpenScctGame.Tutorials;

namespace OpenScctGame.UI;

/// <summary>
/// Gadget Details (GUI.Tuto_Gadgets and Tuto_OneGadget): a side's eight gadgets with Switch Team; a gadget shows its
/// 64 x 64 tile of the HUD's gadget atlas (SHud Tex_All_Gadgets at the class's IconU / IconV, the gadgets in the team's
/// ClassInventory order), its explanation and its legend (format, range, duration); Next moves to the following one.
/// </summary>
public partial class GadgetDetailsScreen : MenuPage
{
    private const string ListSection = "Tuto_Gadgets";
    private const string GadgetSection = "Tuto_OneGadget";

    [Provenance(ProvenanceKind.Decoded, "GUI.Tuto_OneGadget.RefreshAll: x2 = x1 + 64, y2 = y1 + 64")]
    private const float IconSize = 64f;

    private static readonly UcClassIndex Classes = new(CorePackage.Info, EnginePackage.Info, SBasePackage.Info);

    /// <summary>0 spies, 1 mercs.</summary>
    public int Team { get; init; }

    /// <summary>The gadget shown, or -1 for the list.</summary>
    public int Gadget { get; init; } = -1;

    public static GadgetDetailsScreen Show(SceneTree tree, int team, int gadget = -1) =>
        FrontEnd.Show(tree, new GadgetDetailsScreen { Team = team, Gadget = gadget });

    /// <summary>The list of a side (TitleGadgetSpy / TitleGadgetMerc).</summary>
    public static IReadOnlyList<string> Titles(int team) => TutorialText.Array(ListSection, team == 0 ? "TitleGadgetSpy" : "TitleGadgetMerc");

    public override void _Ready()
    {
        Name = "GadgetDetails";
        IReadOnlyList<string> titles = Titles(Team);
        string title = Gadget >= 0 && Gadget < titles.Count
            ? titles[Gadget]
            : Team == 0 ? TutorialText.Caption(ListSection, "TitlePageSpy", "Spy Gadgets") : TutorialText.Caption(ListSection, "TitlePageMerc", "Mercenary Gadgets");
        BuildFrame(title, 900f);
        if (Gadget < 0)
        {
            BuildList(titles);
        }
        else
        {
            BuildGadget(titles.Count);
        }
    }

    [PortOf("uc:GUI.Tuto_Gadgets:RefreshList", Spec = "spec/game/tutorials.md#rules")]
    [PortOf("uc:GUI.Tuto_Gadgets:ListClick", Spec = "spec/game/tutorials.md#rules")]
    [PortOf("uc:GUI.Tuto_Gadgets:SwitchTeam", Spec = "spec/game/tutorials.md#rules")]
    private void BuildList(IReadOnlyList<string> titles)
    {
        VBoxContainer column = Panel(Body, "List", null, 520f);
        column.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        Button? first = null;
        for (int i = 0; i < titles.Count; i++)
        {
            int gadget = i;
            Button b = UiStyle.MenuButton(titles[i], () => Show(GetTree(), Team, gadget), "Gadget" + i);
            if (Icon(Team, i) is { } icon)
            {
                b.Icon = icon;
                b.ExpandIcon = true;
                b.CustomMinimumSize = b.CustomMinimumSize with { Y = 52f };
            }

            first ??= b;
            column.AddChild(b);
        }

        if (titles.Count == 0)
        {
            column.AddChild(new Label { Text = "The gadget pages need the converted menu texts (the ui content step).", Modulate = UiStyle.Dim });
        }

        PageButton("Back", () => TutorialsScreen.Show(GetTree()), "Back");
        PageButton(TutorialText.Caption(ListSection, "ButtonY", "Switch Team"), () => Show(GetTree(), Team == 0 ? 1 : 0), "SwitchTeam");
        (first ?? PageButtons[0]).GrabFocus();
    }

    [PortOf("uc:GUI.Tuto_OneGadget:RefreshAll", Spec = "spec/game/tutorials.md#rules")]
    [PortOf("uc:GUI.Tuto_OneGadget:NextTutorial", Spec = "spec/game/tutorials.md#rules")]
    private void BuildGadget(int count)
    {
        string side = Team == 0 ? "Spy" : "Merc";
        var top = new HBoxContainer { Name = "Top", Alignment = BoxContainer.AlignmentMode.Center };
        top.AddThemeConstantOverride("separation", 32);
        Body.AddChild(top);
        top.AddChild(new TextureRect
        {
            Name = "Picture", CustomMinimumSize = new Vector2(192f, 192f), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, Texture = Icon(Team, Gadget),
        });
        VBoxContainer legend = Panel(top, "Legend", null, 420f);
        legend.AddChild(new Label
        {
            Name = "Text", Text = TutorialText.Lines(TutorialText.Item(GadgetSection, side + "_TutorialLegend", Gadget)),
            AutowrapMode = TextServer.AutowrapMode.WordSmart, Modulate = UiStyle.MenuFocus, CustomMinimumSize = new Vector2(400f, 0f),
        });
        Body.AddChild(new Label
        {
            Name = "Explanation", Text = TutorialText.Item(GadgetSection, side + "_TutorialText", Gadget),
            AutowrapMode = TextServer.AutowrapMode.WordSmart, Modulate = UiStyle.MenuText, CustomMinimumSize = new Vector2(860f, 0f),
        });

        PageButton("Back", () => Show(GetTree(), Team), "Back");
        Button next = PageButton(TutorialText.Caption(GadgetSection, "ButtonA", "Next"), () => Show(GetTree(), Team, count == 0 || Gadget + 1 >= count ? 0 : Gadget + 1), "Next");
        next.GrabFocus();
    }

    /// <summary>The gadget's tile of the HUD's gadget atlas (TeamInfo.ClassInventory[i] IconU / IconV), or null.</summary>
    public static Texture2D? Icon(int team, int index)
    {
        if (UiStyle.Assets?.Texture("SHudTextures", "Icones_Gadgets") is not { } atlas ||
            Classes.Find(team == 0 ? "SBase.STeamAttaquant" : "SBase.STeamDefenseur")?.Create() is not STeamInfo info ||
            index < 0 || index >= info.ClassInventory.Count || Classes.Find(info.ClassInventory[index])?.Create() is not Inventory item)
        {
            return null;
        }

        return new AtlasTexture { Atlas = atlas, Region = new Rect2(item.IconU, item.IconV, IconSize, IconSize) };
    }
}
