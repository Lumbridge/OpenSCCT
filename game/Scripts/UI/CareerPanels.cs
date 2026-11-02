using System;
using Godot;

namespace OpenScctGame.UI;

/// <summary>Compact dashboard cards shared by career stats and recorded matches.</summary>
internal static class CareerPanels
{
    public static VBoxContainer Card(Container parent, string name, string caption)
    {
        var frame = new PanelContainer { Name = name, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        var style = (StyleBox)UiStyle.MenuPanelBox().Duplicate();
        style.ContentMarginLeft = style.ContentMarginRight = 10f;
        style.ContentMarginTop = style.ContentMarginBottom = 8f;
        frame.AddThemeStyleboxOverride("panel", style);
        parent.AddChild(frame);
        var column = new VBoxContainer { Name = "Column" };
        column.AddThemeConstantOverride("separation", 6);
        frame.AddChild(column);
        var heading = Text(column, "Caption", caption, size: 14);
        heading.Modulate = UiStyle.MenuFocus;
        if (!UiStyle.IsOriginal)
        {
            heading.Uppercase = true;
            if (UiArt.CaptionFont is { } font) heading.AddThemeFontOverride("font", font);
        }
        return column;
    }

    public static Label Text(Container parent, string name, string text, bool dim = false, int size = 0)
    {
        var label = new Label
        {
            Name = name, Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore,
            Modulate = dim ? UiStyle.Dim : UiStyle.Text,
        };
        if (size > 0) label.AddThemeFontSizeOverride("font_size", size);
        parent.AddChild(label);
        return label;
    }

    public static void StatRow(Container parent, string label, string value, string? name = null)
    {
        var row = new HBoxContainer { Name = name ?? "Stat", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        row.AddThemeConstantOverride("separation", 16);
        parent.AddChild(row);
        Text(row, "Label", label, size: 16);
        var total = new Label
        {
            Name = "Value", Text = value, HorizontalAlignment = HorizontalAlignment.Right,
            Modulate = UiStyle.MenuFocus, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        total.AddThemeFontSizeOverride("font_size", 16);
        if (!UiStyle.IsOriginal && UiArt.CaptionFont is { } font) total.AddThemeFontOverride("font", font);
        row.AddChild(total);
    }

    public static GridContainer Grid(Container parent, string name, float columnWidth = 300f, int maxColumns = 2)
    {
        var grid = new GridContainer { Name = name, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        grid.AddThemeConstantOverride("h_separation", 10);
        grid.AddThemeConstantOverride("v_separation", 10);
        // Use the assigned width, not the physical window, so the same cards reflow on a scaled canvas.
        grid.Resized += () => grid.Columns = Math.Clamp((int)((grid.Size.X + 10f) / (columnWidth + 10f)), 1, maxColumns);
        parent.AddChild(grid);
        return grid;
    }

    public static void Clear(Node parent)
    {
        foreach (Node child in parent.GetChildren()) { parent.RemoveChild(child); child.QueueFree(); }
    }
}
