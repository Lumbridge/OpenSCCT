using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using OpenScctGame.Options;
using OpenScctGame.UI;

namespace OpenScctGame.Smoke;

/// <summary>Checks the settled editor layout, including the focus paint and independent preview hit area.</summary>
internal static class HudEditorSidebarChecks
{
    internal static List<string> Run(Control root)
    {
        var problems = new List<string>();
        var sidebar = root.FindChild("HudEditorCategories", true, false) as ScrollContainer;
        var preview = root.FindChild("HudEditorPreview", true, false) as HudEditorPreview;
        var divider = root.FindChild("HudEditorPreviewDivider", true, false) as Control;
        var compact = root.FindChild("HudEditorElement", true, false) as OptionButton;
        if (sidebar is null || preview is null || divider is null || compact is null)
        {
            problems.Add("HUD editor sidebar/preview controls are missing");
            return problems;
        }
        if (!preview.IsVisibleInTree()) return problems;
        Rect2 frame = preview.ScreenRect;
        const float tolerance = 1.1f;
        if (frame.Size.X <= 0 || frame.Size.Y <= 0 || Math.Abs(frame.Size.X / frame.Size.Y - preview.Aspect) > .01f)
            problems.Add("HUD editor preview does not retain the selected screen aspect");
        if (frame.Position.X < HudEditorPreview.PreviewPadding - tolerance || frame.Position.Y < HudEditorPreview.PreviewPadding - tolerance ||
            frame.End.X > preview.Size.X - HudEditorPreview.PreviewPadding + tolerance || frame.End.Y > preview.Size.Y - HudEditorPreview.PreviewPadding + tolerance)
            problems.Add("HUD editor sample screen reaches the preview edge instead of its inset");
        if (!preview.ClipContents)
            problems.Add("HUD editor preview can draw over the sidebar or inspector");
        if (!sidebar.IsVisibleInTree())
        {
            if (!compact.IsVisibleInTree() || divider.IsVisibleInTree()) problems.Add("compact HUD editor retains its sidebar divider or loses element selection");
            return problems;
        }
        Rect2 sideRect = sidebar.GetGlobalRect(), previewRect = preview.GetGlobalRect(), dividerRect = divider.GetGlobalRect();
        if (!divider.IsVisibleInTree() || sideRect.End.X > dividerRect.Position.X - 8 || dividerRect.End.X > previewRect.Position.X - 8)
            problems.Add("HUD editor sidebar and preview do not have separate gutters");
        if (sideRect.Intersects(previewRect) || !sidebar.ClipContents || divider.MouseFilter != Control.MouseFilterEnum.Ignore)
            problems.Add("HUD editor sidebar overlaps or intercepts the preview");
        float rowRight = sideRect.End.X - (sidebar.GetVScrollBar().Visible ? sidebar.GetVScrollBar().Size.X : 0);
        foreach (Button button in sidebar.FindChildren("*", nameof(Button), true, false).OfType<Button>())
        {
            Rect2 buttonRect = button.GetGlobalRect();
            if (buttonRect.Position.X < sideRect.Position.X + 1 || buttonRect.End.X > rowRight - 4)
                problems.Add("HUD editor element row reaches outside its sidebar content: " + button.Text);
            if (button.GetThemeStylebox("focus") is not UiBox { Expand: <= -1f } ||
                button.GetThemeStylebox("hover") is not StyleBoxFlat)
                problems.Add("HUD editor element focus/hover paint can extend outside its row: " + button.Text);
            if (buttonRect.Intersects(previewRect)) problems.Add("HUD editor element hit area overlaps preview: " + button.Text);
        }
        return problems;
    }
}
