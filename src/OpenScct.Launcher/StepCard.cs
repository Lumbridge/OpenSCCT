using System;
using System.Drawing;
using System.Windows.Forms;

namespace OpenScct.Launcher.App;

/// <summary>Where a step stands: shown by its badge (number, tick or "!") and colours.</summary>
internal enum StepState
{
    /// <summary>Not reachable yet: an earlier step comes first.</summary>
    Waiting,

    /// <summary>The step to do now.</summary>
    Todo,

    /// <summary>Done.</summary>
    Done,

    /// <summary>Needs the player's help (game not found, a build that failed).</summary>
    Problem,
}

/// <summary>
/// One numbered step of the main window: a badge, a title, one line saying where it stands, and an optional small
/// button on the right.
/// </summary>
internal sealed class StepCard : TableLayoutPanel
{
    private readonly int _number;
    private readonly Badge _badge;
    private readonly Label _title;
    private readonly Label _detail;

    public StepCard(int number, string title, Button? action = null)
    {
        _number = number;
        ColumnCount = 3;
        ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        RowCount = 1;
        RowStyles.Add(new RowStyle(SizeType.AutoSize));
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
        BackColor = Theme.Panel;
        Padding = new Padding(14, 10, 14, 10);
        Margin = new Padding(0, 8, 0, 0);

        _badge = new Badge
        {
            Size = new Size(34, 34),
            Font = Theme.Heading,
            Margin = new Padding(0, 2, 14, 0),
            Anchor = AnchorStyles.Top | AnchorStyles.Left,
        };
        _title = Theme.Label(title, Theme.Heading);
        _detail = Theme.Wrapping(Theme.Label(string.Empty, Theme.Body, Theme.Muted));
        _detail.Margin = new Padding(0, 2, 0, 0);

        TableLayoutPanel text = Theme.Grid(Theme.Fill);
        Theme.AddRow(text, _title);
        Theme.AddRow(text, _detail);

        Controls.Add(_badge, 0, 0);
        Controls.Add(text, 1, 0);
        if (action is not null)
        {
            action.Anchor = AnchorStyles.Right;
            action.Margin = new Padding(12, 0, 0, 0);
            Controls.Add(action, 2, 0);
        }

        Set(StepState.Waiting, string.Empty);
    }

    public void Set(StepState state, string detail)
    {
        _detail.Text = detail;
        _badge.Text = state switch
        {
            StepState.Done => "✓",
            StepState.Problem => "!",
            _ => _number.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
        (Color badgeBack, Color badgeText, Color title, Color detailColor) = state switch
        {
            StepState.Done => (Theme.AccentDim, Color.White, Theme.Text, Theme.Muted),
            StepState.Todo => (Theme.Accent, Theme.Background, Theme.Accent, Theme.Text),
            StepState.Problem => (Theme.Warning, Theme.Background, Theme.Warning, Theme.Text),
            _ => (Theme.Border, Theme.Muted, Theme.Muted, Theme.Muted),
        };
        _badge.Fill = badgeBack;
        _badge.ForeColor = badgeText;
        _badge.Invalidate();
        _title.ForeColor = title;
        _detail.ForeColor = detailColor;
    }
}

/// <summary>A filled circle with a character in it, drawn smooth at any display scaling.</summary>
internal sealed class Badge : Control
{
    public Badge()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint
            | ControlStyles.SupportsTransparentBackColor | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Panel;
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Color Fill { get; set; } = Theme.Border;

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var brush = new SolidBrush(Fill);
        e.Graphics.FillEllipse(brush, 0, 0, Width - 1, Height - 1);
        TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, ForeColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        Invalidate();
    }
}
