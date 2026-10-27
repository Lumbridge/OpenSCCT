using Godot;
using OpenScct.Net.Discovery;

namespace OpenScctGame.UI;

/// <summary>The version and build in a page's bottom-right corner ("OpenSCCT 0.5.0  build 20261026.1412 (d3cd88b)", <see cref="BuildInfo.Text"/>).</summary>
public static class BuildLabel
{
    public static Label Corner()
    {
        var label = new Label
        {
            Name = "BuildInfo",
            Text = BuildInfo.Text,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Modulate = UiStyle.Dim,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        label.AddThemeFontSizeOverride("font_size", 14);
        label.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.BottomRight, Control.LayoutPresetMode.Minsize, 12);
        label.GrowHorizontal = Control.GrowDirection.Begin;
        label.GrowVertical = Control.GrowDirection.Begin;
        return label;
    }
}
