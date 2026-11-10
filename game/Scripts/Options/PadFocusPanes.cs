using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace OpenScctGame.Options;

/// <summary>
/// Controller moves between the columns of a menu page. A column is a scroll list (the category list and the rows
/// beside it on the options pages). Moving into a column lands on the entry used there last, else its selected
/// entry, else the one nearest in height; <see cref="PadMenus"/> calls this for the shoulder buttons and for left
/// or right past a list's edge.
/// </summary>
internal static class PadFocusPanes
{
    /// <summary>The last focused control of each column (instance ids, so freed rows drop out by themselves).</summary>
    private static readonly Dictionary<ulong, ulong> s_last = new();

    public static ScrollContainer? PaneOf(Node node)
    {
        for (Node? at = node.GetParent(); at is not null and not Window; at = at.GetParent())
        {
            if (at is ScrollContainer pane) return pane;
        }

        return null;
    }

    public static void Remember(Control control)
    {
        if (PaneOf(control) is not { } pane) return;
        if (s_last.Count > 64) s_last.Clear();
        s_last[pane.GetInstanceId()] = control.GetInstanceId();
    }

    /// <summary>
    /// Left or right from <paramref name="from"/> would leave its column or go nowhere. Controls that use the
    /// arrows themselves (sliders, text fields, tab bars, lists) never leave this way; the shoulder buttons do.
    /// </summary>
    public static bool LeavesPane(Control from, bool left)
    {
        if (from is Slider or SpinBox or LineEdit or TextEdit or TabBar or Tree or ItemList || PaneOf(from) is not { } pane)
        {
            return false;
        }

        Control? next = from.FindValidFocusNeighbor(left ? Side.Left : Side.Right);
        return next is null || next == from || PaneOf(next) != pane;
    }

    /// <summary>Focuses the nearest column to the left (-1) or right (1) of the one <paramref name="from"/> is in.</summary>
    public static bool Step(Control from, int direction)
    {
        if (PaneOf(from) is not { } pane) return false;
        Rect2 here = pane.GetGlobalRect();
        Control? best = null;
        float bestGap = float.MaxValue;
        foreach (ScrollContainer other in Neighbours(pane))
        {
            Rect2 rect = other.GetGlobalRect();
            float gap = direction < 0 ? here.Position.X - rect.End.X : rect.Position.X - here.End.X;
            bool beside = rect.Position.Y < here.End.Y && rect.End.Y > here.Position.Y;
            if (gap < -1f || !beside || gap >= bestGap || Target(other, from) is not { } target) continue;
            best = target;
            bestGap = gap;
        }

        if (best is null) return false;
        best.GrabFocus();
        return true;
    }

    /// <summary>The other columns of the same page: those under the nearest ancestor that has any.</summary>
    private static List<ScrollContainer> Neighbours(ScrollContainer pane)
    {
        var found = new List<ScrollContainer>();
        for (Node? at = pane.GetParent(); at is not null and not Window and not CanvasLayer; at = at.GetParent())
        {
            foreach (ScrollContainer other in at.FindChildren("*", nameof(ScrollContainer), true, false).OfType<ScrollContainer>())
            {
                if (other != pane && other.IsVisibleInTree() && !other.IsAncestorOf(pane) && !pane.IsAncestorOf(other)) found.Add(other);
            }

            if (found.Count > 0) break;
        }

        return found;
    }

    private static Control? Target(ScrollContainer pane, Control from)
    {
        if (s_last.TryGetValue(pane.GetInstanceId(), out ulong id) && GodotObject.InstanceFromId(id) is Control last &&
            Usable(last) && pane.IsAncestorOf(last))
        {
            return last;
        }

        Control[] all = pane.FindChildren("*", nameof(Control), true, false).OfType<Control>().Where(Usable).ToArray();
        // A list's selected entry (the active category, the selected action); check boxes are settings, not a selection.
        if (all.FirstOrDefault(c => c is Button { ToggleMode: true, ButtonPressed: true } and not CheckBox and not CheckButton) is { } selected)
        {
            return selected;
        }

        Rect2 view = pane.GetGlobalRect();
        float y = from.GetGlobalRect().GetCenter().Y;
        return all.Where(c => view.Intersects(c.GetGlobalRect()))
            .OrderBy(c => Math.Abs(c.GetGlobalRect().GetCenter().Y - y)).ThenBy(c => c.GetGlobalRect().Position.X)
            .FirstOrDefault() ?? all.FirstOrDefault();
    }

    private static bool Usable(Control control) =>
        GodotObject.IsInstanceValid(control) && !control.IsQueuedForDeletion() && control.IsInsideTree() &&
        control.FocusMode == Control.FocusModeEnum.All && control.IsVisibleInTree() && control is not BaseButton { Disabled: true };
}
