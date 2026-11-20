using Godot;
using OpenScct.Server;

namespace OpenScctGame.UI;

/// <summary>Direct desktop and Flatpak launches need the game files built by the native launcher.</summary>
public partial class LinuxSetupScreen : Control
{
    public static bool Needed => !ContentPaths.SteamBaseReady;

    public override void _Ready()
    {
        Name = "LinuxSetup";
        Theme = new Theme { DefaultFontSize = 22 };
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var background = new ColorRect { Color = new Color(0.035f, 0.055f, 0.045f) };
        background.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(background);
        var margin = new MarginContainer();
        margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        foreach (string side in new[] { "left", "right", "top", "bottom" })
        {
            margin.AddThemeConstantOverride("margin_" + side, 40);
        }
        AddChild(margin);
        var column = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        column.AddThemeConstantOverride("separation", 20);
        margin.AddChild(column);
        column.AddChild(new Label { Text = "Set up OpenSCCT Godot", LabelSettings = new LabelSettings { FontSize = 36 } });
        column.AddChild(new Label
        {
            Name = "SteamInstructions",
            Text = "Open the OpenSCCT launcher and select Godot under Modernisations. " +
                   "Choose your installed Steam copy of Splinter Cell Chaos Theory and build its game files. " +
                   "When the build finishes, reopen Godot.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });
        if (LinuxSetupContent.OverrideProblem(ContentPaths.Root) is { } problem)
        {
            column.AddChild(new Label { Text = problem, AutowrapMode = TextServer.AutowrapMode.WordSmart });
        }
        var quit = new Button { Name = "Quit", Text = "Quit", SizeFlagsHorizontal = SizeFlags.ShrinkBegin,
            CustomMinimumSize = new Vector2(160, 56) };
        quit.Pressed += () => GetTree().Quit();
        column.AddChild(quit);
        quit.GrabFocus();
    }
}
