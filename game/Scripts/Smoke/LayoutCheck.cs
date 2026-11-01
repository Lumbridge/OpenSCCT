using System.Collections.Generic;
using Godot;

namespace OpenScctGame.Smoke;

/// <summary>
/// What a player cannot read on a menu page: a one-line label whose text is wider than the label (cut by its own clip or
/// an ellipsis) or that ends past the edge of the scroll area or window it sits in, and a scroll area that scrolls
/// sideways (its bar sits over the last row).
/// </summary>
public static class LayoutCheck
{
    /// <summary>Interactive controls must fit horizontally; only scrollable content may extend vertically.</summary>
    public static List<string> BoundsProblems(Node root, Vector2 viewport)
    {
        var found = new List<string>();
        foreach (Node node in root.FindChildren("*", nameof(Control), recursive: true, owned: false))
        {
            if (node is not Control c || !c.IsVisibleInTree() ||
                c is not (BaseButton or Label or LineEdit or TextEdit or Tree or TabContainer or ScrollContainer)) continue;
            Rect2 rect = c.GetGlobalRect();
            float left = 0f, right = viewport.X;
            bool scrollsVertically = false;
            for (Node? parent = c.GetParent(); parent is not null; parent = parent.GetParent())
            {
                if (parent is not ScrollContainer scroll) continue;
                Rect2 area = scroll.GetGlobalRect();
                left = Mathf.Max(left, area.Position.X);
                right = Mathf.Min(right, area.End.X);
                scrollsVertically |= scroll.VerticalScrollMode != ScrollContainer.ScrollMode.Disabled;
            }
            if (rect.Position.X < left - 1f || rect.End.X > right + 1f)
                found.Add($"{c.GetPath()}: horizontal bounds {rect.Position.X:0}..{rect.End.X:0} outside {left:0}..{right:0}");
            if (!scrollsVertically && (rect.Position.Y < -1f || rect.End.Y > viewport.Y + 1f))
                found.Add($"{c.GetPath()}: vertical bounds {rect.Position.Y:0}..{rect.End.Y:0} outside viewport height {viewport.Y:0}");
        }
        return found;
    }

    public static List<string> Problems(Node root, float windowWidth)
    {
        var found = new List<string>();
        foreach (Node n in root.FindChildren("*", nameof(Control), recursive: true, owned: false))
        {
            if (n is not Control { Visible: true } c || !c.IsVisibleInTree())
            {
                continue;
            }

            if (c is ScrollContainer scroll && scroll.GetHScrollBar() is { Visible: true })
            {
                found.Add($"'{scroll.Name}' scrolls sideways");
            }

            if (c is not Label { AutowrapMode: TextServer.AutowrapMode.Off } label || label.Text.Length == 0)
            {
                continue;
            }

            Font font = label.GetThemeFont("font");
            int size = label.GetThemeFontSize("font_size");
            float wide = 0f;
            foreach (string line in label.Text.Split('\n'))
            {
                wide = Mathf.Max(wide, font.GetStringSize(line, HorizontalAlignment.Left, -1, size).X);
            }

            Rect2 rect = label.GetGlobalRect();
            float right = rect.Position.X + wide;
            float edge = windowWidth;
            for (Node? p = label.GetParent(); p is not null; p = p.GetParent())
            {
                if (p is ScrollContainer s)
                {
                    edge = Mathf.Min(edge, s.GetGlobalRect().End.X);
                    break;
                }
            }

            if (wide > rect.Size.X + 1f || right > edge + 1f)
            {
                found.Add($"'{label.Text}' ({wide:0} px in {rect.Size.X:0} px at {rect.Position.X:0}, the edge at {edge:0})");
            }
        }

        return found;
    }
}
