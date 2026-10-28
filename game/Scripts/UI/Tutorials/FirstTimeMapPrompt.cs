using System;
using Godot;
using OpenScct.Provenance;
using OpenScctGame.Tutorials;

namespace OpenScctGame.UI;

/// <summary>
/// "This is the first time you have played this map..." (GUI.FirstTimeToPlayThisMap): shown over the Lobby Room when
/// the player presses Launch or Ready on a map the profile has not played (Lobby_Create.ButtonLaunch,
/// isMapAlreadyPlayed). OK marks the map played, saves the profile and closes the box; the press then goes on, as
/// the lobby's page does once the box has closed.
/// </summary>
public partial class FirstTimeMapPrompt : PanelContainer
{
    public const string NodeName = "FirstTimeMap";

    private const string Section = "FirstTimeToPlayThisMap";

    public string Map { get; init; } = string.Empty;

    /// <summary>Runs after OK (the Launch or Ready press that opened the box).</summary>
    public Action? Then { get; init; }

    /// <summary>
    /// Opens the box over <paramref name="parent"/> when <paramref name="map"/> is played for the first time and returns
    /// true; false when the map was played before (the caller goes on at once). Smoke runs (isolated settings) never
    /// show it, as they start from an empty profile.
    /// </summary>
    [PortOf("uc:GUI.Lobby_Create:ButtonLaunch", Spec = "spec/game/tutorials.md#rules")]
    public static bool OpenIfFirstTime(Control parent, string map, Action then)
    {
        ArgumentNullException.ThrowIfNull(parent);
        if (OpenScctGame.Options.SettingsStore.Isolated || string.IsNullOrEmpty(map) || TutorialProfile.Current.isMapAlreadyPlayed(map) || parent.GetNodeOrNull(NodeName) is not null)
        {
            return false;
        }

        parent.AddChild(new FirstTimeMapPrompt { Name = NodeName, Map = map, Then = then });
        return true;
    }

    public override void _Ready()
    {
        AnchorLeft = 0.3f;
        AnchorRight = 0.7f;
        AnchorTop = 0.35f;
        AnchorBottom = 0.6f;
        AddThemeStyleboxOverride("panel", UiStyle.MenuPanelBox());
        var column = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        column.AddThemeConstantOverride("separation", 16);
        column.AddChild(new Label
        {
            Name = "Text", HorizontalAlignment = HorizontalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Text = TutorialText.Caption(Section, "Text.Caption",
                "This is the first time you have played this map. To learn more about it, try it in Tutorials, Visit a Map."),
        });
        Button ok = UiStyle.MenuButton(TutorialText.Caption(Section, "Ok.Caption", "OK"), Ok, "FirstTimeOk");
        ok.CustomMinimumSize = new Vector2(160f, 40f);
        ok.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        column.AddChild(ok);
        AddChild(column);
        ok.GrabFocus();
    }

    [PortOf("uc:GUI.FirstTimeToPlayThisMap:OkButtonClick", Spec = "spec/game/tutorials.md#rules")]
    private void Ok()
    {
        TutorialProfile.MarkMapPlayed(Map);
        QueueFree();
        Then?.Invoke();
    }
}
