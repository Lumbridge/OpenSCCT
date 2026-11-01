using System;
using Godot;

namespace OpenScctGame.UI;

/// <summary>Shared lobby layout primitives for online and offline screens.</summary>
public static class LobbyPanels
{
    public static Label Text(string text = "") => new()
    {
        Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
    };

    public static VBoxContainer Card(Container parent, string name, string title)
    {
        var panel = new PanelContainer { Name = name, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        panel.AddThemeStyleboxOverride("panel", UiStyle.MenuPanelBox());
        parent.AddChild(panel);
        var body = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 8);
        panel.AddChild(body);
        body.AddChild(new Label { Name = "Heading", Text = title, Modulate = UiStyle.MenuFocus });
        return body;
    }

    public static Button Button(string text, Action action, string name)
    {
        var button = new Button { Name = name, Text = text, CustomMinimumSize = new Vector2(0, 36) };
        button.Pressed += action;
        return button;
    }

    public static void ExpandBody(VBoxContainer body)
    {
        Node centre = body.GetParent();
        var scroll = (ScrollContainer)centre.GetParent();
        centre.RemoveChild(body); scroll.RemoveChild(centre); centre.QueueFree();
        body.CustomMinimumSize = Vector2.Zero;
        body.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        body.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        scroll.OffsetBottom = -172;
        scroll.AddChild(body);
    }

    public static VBoxContainer Preparation(Control root)
    {
        var panel = new PanelContainer { Name = "Preparation", AnchorLeft = 0, AnchorRight = 1, AnchorTop = 1, AnchorBottom = 1,
            OffsetLeft = 24, OffsetRight = -24, OffsetTop = -164, OffsetBottom = -80 };
        panel.AddThemeStyleboxOverride("panel", UiStyle.MenuPanelBox());
        root.AddChild(panel);
        var column = new VBoxContainer(); panel.AddChild(column);
        return column;
    }
}

public partial class LobbyColumns : BoxContainer
{
    public float StackBelow { get; set; } = 900f;
    public LobbyColumns() { SizeFlagsHorizontal = SizeFlags.ExpandFill; AddThemeConstantOverride("separation", 12); }
    public override void _Process(double delta)
    {
        // A wide child can force its parent beyond the viewport before it has reflowed.
        // Prefer the enclosing scroll's available width so that cannot keep the row horizontal.
        float width = GetParent() is Control parent ? parent.Size.X : Size.X;
        for (Node? node = GetParent(); node is not null; node = node.GetParent())
            if (node is ScrollContainer scroll)
            {
                width = scroll.Size.X - (scroll.GetVScrollBar().Visible ? scroll.GetVScrollBar().Size.X : 0f);
                break;
            }
        Vertical = width < StackBelow;
    }
}
