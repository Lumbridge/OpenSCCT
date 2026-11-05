using System;
using System.Linq;
using Godot;

namespace OpenScctGame.Options;

public partial class OptionsMenu
{
    /// <summary>Arrow navigation follows rows even when the next row lies outside a scroll viewport.</summary>
    internal static void QueueScrollFocusLinks(Control rows)
    {
        Callable.From(() =>
        {
            if (!IsInstanceValid(rows) || !rows.IsInsideTree()) return;
            var controls = rows.GetChildren().OfType<Control>()
                .Select(row => new[] { row }.Concat(row.FindChildren("*", nameof(Control), true, false).OfType<Control>())
                    .Where(control => control.FocusMode == FocusModeEnum.All).ToArray()).ToArray();
            foreach (Control control in controls.SelectMany(row => row))
            {
                control.FocusNeighborTop = new NodePath();
                control.FocusNeighborBottom = new NodePath();
            }
            var visible = controls.Select(row => row.Where(control => control.IsVisibleInTree() &&
                control is not BaseButton { Disabled: true }).ToArray()).Where(row => row.Length > 0).ToArray();
            for (int index = 0; index < visible.Length; index++)
            {
                foreach (Control control in visible[index])
                {
                    Control Closest(Control[] next) => next.MinBy(candidate =>
                        Math.Abs(candidate.GetGlobalRect().GetCenter().X - control.GetGlobalRect().GetCenter().X))!;
                    if (index > 0) control.FocusNeighborTop = control.GetPathTo(Closest(visible[index - 1]));
                    if (index + 1 < visible.Length) control.FocusNeighborBottom = control.GetPathTo(Closest(visible[index + 1]));
                }
            }
        }).CallDeferred();
    }
}
