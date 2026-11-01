using Godot;
using OpenScct.Net.Discovery;
using OpenScctGame.Online;

namespace OpenScctGame.UI;

/// <summary>
/// The version and build in a page's bottom-right corner ("OpenSCCT 0.5.0  build 20261026.1412 (d3cd88b)",
/// <see cref="BuildInfo.Text"/>), with the master's own version on the line below
/// ("Master 1.0.0  build 20261027.1200 (abc1234)", <see cref="MasterVersionCheck"/>).
/// </summary>
public static class BuildLabel
{
    public static Label Corner()
    {
        var label = new BuildCornerLabel
        {
            Name = "BuildInfo",
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Modulate = UiStyle.Dim,
            MouseFilter = Control.MouseFilterEnum.Pass,
        };
        label.AddThemeFontSizeOverride("font_size", 14);
        label.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.BottomRight, Control.LayoutPresetMode.Minsize, 12);
        label.GrowHorizontal = Control.GrowDirection.Begin;
        label.GrowVertical = Control.GrowDirection.Begin;
        return label;
    }
}
