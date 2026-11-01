using Godot;
using OpenScct.Net.Discovery;
using OpenScctGame.Online;

namespace OpenScctGame.UI;

/// <summary>The corner label: the game's build, and the master's line kept current while the page is shown.</summary>
public partial class BuildCornerLabel : Label
{
    private string _master = string.Empty;
    private bool? _compact;

    public override void _Ready() => UpdateLabel();

    public override void _Process(double delta) => UpdateLabel();

    private void UpdateLabel()
    {
        string master = MasterVersionCheck.Line();
        bool compact = GetViewportRect().Size.X < 1200f;
        if (!string.Equals(master, _master, System.StringComparison.Ordinal) || _compact != compact)
        {
            _master = master;
            _compact = compact;
            TooltipText = BuildInfo.Text + "\n" + master;
            Text = compact ? BuildInfo.Text + " · " + master : TooltipText;
            ClipText = compact;
            TextOverrunBehavior = compact ? TextServer.OverrunBehavior.TrimEllipsis : TextServer.OverrunBehavior.NoTrimming;
            AddThemeFontSizeOverride("font_size", compact ? 12 : 14);
            SetAnchorsAndOffsetsPreset(compact ? LayoutPreset.BottomWide : LayoutPreset.BottomRight, LayoutPresetMode.Minsize, compact ? 0 : 12);
        }
        if (compact)
        {
            // Refit after font/text minimum-size updates, below the page's Quit/Back buttons.
            OffsetLeft = 12f; OffsetRight = -12f; OffsetTop = -18f; OffsetBottom = -2f;
        }
    }
}
