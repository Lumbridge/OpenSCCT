using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

namespace OpenScct.Launcher.App;

/// <summary>Product colours; amber and red retain the same meaning in both products.</summary>
internal sealed record ThemePalette(Color Background, Color Panel, Color Border, Color Accent, Color AccentDim,
    Color Text, Color Muted, Color Hover, Color Pressed)
{
    public Color Warning => Theme.Warning;
    public Color Error => Theme.Error;

    public static ThemePalette Game { get; } = new(
        Color.FromArgb(16, 22, 18), Color.FromArgb(22, 31, 25), Color.FromArgb(45, 63, 51),
        Color.FromArgb(120, 255, 140), Color.FromArgb(58, 140, 74), Color.FromArgb(216, 232, 220),
        Color.FromArgb(148, 170, 155), Color.FromArgb(146, 255, 163), Color.FromArgb(94, 220, 113));

    public static ThemePalette Editor { get; } = new(
        Color.FromArgb(19, 21, 27), Color.FromArgb(26, 31, 42), Color.FromArgb(48, 64, 88),
        Color.FromArgb(84, 148, 245), Color.FromArgb(52, 92, 156), Color.FromArgb(220, 228, 241),
        Color.FromArgb(151, 166, 190), Color.FromArgb(119, 173, 255), Color.FromArgb(65, 124, 218));
}

/// <summary>The night-vision look of game/branding: near-black green panels, bright green accents.</summary>
internal static class Theme
{
    private sealed class ButtonStyle
    {
        public bool Primary;
        public bool Quiet;
        public bool OnPanel;
        public ThemePalette Palette = ThemePalette.Game;
    }

    private static readonly ConditionalWeakTable<Button, ButtonStyle> ButtonStyles = new();
    public static readonly Color Background = Color.FromArgb(16, 22, 18);
    public static readonly Color Panel = Color.FromArgb(22, 31, 25);
    public static readonly Color Border = Color.FromArgb(45, 63, 51);
    public static readonly Color Accent = Color.FromArgb(120, 255, 140);
    public static readonly Color AccentDim = Color.FromArgb(58, 140, 74);
    public static readonly Color Text = Color.FromArgb(216, 232, 220);
    public static readonly Color Muted = Color.FromArgb(148, 170, 155);
    public static readonly Color Warning = Color.FromArgb(255, 196, 92);
    public static readonly Color Error = Color.FromArgb(255, 110, 100);

    public static readonly Font Body = new("Segoe UI", 10f);
    public static readonly Font Small = new("Segoe UI", 9f);
    public static readonly Font Heading = new("Segoe UI Semibold", 11f);
    public static readonly Font Title = new("Segoe UI Semibold", 20f);
    public static readonly Font Status = new("Segoe UI Semibold", 18f);
    public static readonly Font PlayFont = new("Segoe UI Semibold", 14f);
    public static readonly Font Mono = new("Consolas", 9.5f);

    public static Icon LoadIcon(int size)
    {
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("OpenScct.Launcher.icon.ico")
            ?? throw new InvalidOperationException("Icon resource missing.");
        return new Icon(stream, size, size);
    }

    public static Icon LoadEditorIcon(int size)
    {
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("OpenScct.Launcher.editor.ico")
            ?? throw new InvalidOperationException("Editor icon resource missing.");
        return new Icon(stream, size, size);
    }

    /// <summary>Applies a product palette without changing layout or semantic warning/error colours.</summary>
    public static void Apply(Control root, ThemePalette palette)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(palette);
        root.BackColor = Map(root.BackColor, palette);
        root.ForeColor = Map(root.ForeColor, palette);
        if (root is Button button)
        {
            ButtonStyle style = ButtonStyles.GetOrCreateValue(button);
            style.Palette = palette;
            PaintButton(button, style);
        }
        if (root is LinkLabel link)
        {
            link.LinkColor = palette.Accent;
            link.ActiveLinkColor = palette.Text;
            link.VisitedLinkColor = palette.Accent;
        }
        if (root is GreenProgressBar progress) progress.Palette = palette;
        if (root is Wordmark wordmark) wordmark.Palette = palette;
        if (root is ListView list)
            foreach (ListViewItem item in list.Items) item.ForeColor = Map(item.ForeColor, palette);
        foreach (Control child in root.Controls) Apply(child, palette);
        root.Invalidate();
    }

    public static Color Map(Color color, ThemePalette palette)
    {
        foreach (ThemePalette source in new[] { ThemePalette.Game, ThemePalette.Editor })
        {
            if (color == source.Background) return palette.Background;
            if (color == source.Panel) return palette.Panel;
            if (color == source.Border) return palette.Border;
            if (color == source.Accent) return palette.Accent;
            if (color == source.AccentDim) return palette.AccentDim;
            if (color == source.Text) return palette.Text;
            if (color == source.Muted) return palette.Muted;
            if (color == source.Hover) return palette.Hover;
            if (color == source.Pressed) return palette.Pressed;
        }
        return color;
    }

    public static Button Button(string text, bool primary = false)
    {
        // Sized to its text (at any display scaling), never narrower than its minimum.
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = primary ? new Padding(24, 10, 24, 10) : new Padding(12, 5, 12, 5),
            MinimumSize = primary ? new Size(180, 56) : new Size(0, 34),
            FlatStyle = FlatStyle.Flat,
            Font = primary ? PlayFont : Body,
            Cursor = Cursors.Hand,
            UseVisualStyleBackColor = false,
        };
        SetButtonEmphasis(button, primary);
        return button;
    }

    /// <summary>Changes emphasis without changing size, including when the launch action becomes Stop.</summary>
    public static void SetButtonEmphasis(Button button, bool primary, ThemePalette? palette = null)
    {
        ButtonStyle style = ButtonStyles.GetOrCreateValue(button);
        style.Primary = primary;
        style.Quiet = false;
        if (palette is not null) style.Palette = palette;
        PaintButton(button, style);
    }

    private static void PaintButton(Button button, ButtonStyle style)
    {
        ThemePalette p = style.Palette;
        button.BackColor = style.Primary ? p.Accent : style.Quiet && !style.OnPanel ? p.Background : p.Panel;
        button.ForeColor = style.Primary ? p.Background : style.Quiet ? style.OnPanel ? p.Accent : p.Muted : p.Text;
        button.FlatAppearance.BorderColor = style.Primary ? p.Accent : p.Border;
        button.FlatAppearance.MouseOverBackColor = style.Primary ? p.Hover : p.Border;
        button.FlatAppearance.MouseDownBackColor = style.Primary ? p.Pressed : p.Background;
    }

    public static Button QuietButton(string text, bool onPanel = false)
    {
        Button button = Button(text);
        button.BackColor = onPanel ? Panel : Background;
        button.ForeColor = onPanel ? Accent : Muted;
        button.FlatAppearance.BorderSize = 0;
        button.Padding = new Padding(8, 4, 8, 4);
        ButtonStyle style = ButtonStyles.GetOrCreateValue(button);
        style.Quiet = true;
        style.OnPanel = onPanel;
        return button;
    }

    public static Label Label(string text, Font? font = null, Color? color = null) => new()
    {
        Text = text,
        AutoSize = true,
        Font = font ?? Body,
        ForeColor = color ?? Text,
        BackColor = Color.Transparent,
    };

    /// <summary>A label that wraps to the width of its table cell (AutoSize and anchored left and right).</summary>
    public static Label Wrapping(Label label)
    {
        label.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        return label;
    }

    /// <summary>A table that grows to its contents: <paramref name="columns"/> as given, one auto-sized row per Add.</summary>
    public static TableLayoutPanel Grid(params ColumnStyle[] columns)
    {
        var grid = new TableLayoutPanel
        {
            ColumnCount = columns.Length,
            RowCount = 0,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
            Padding = new Padding(0),
        };
        foreach (ColumnStyle c in columns)
        {
            grid.ColumnStyles.Add(c);
        }

        return grid;
    }

    /// <summary>Adds one row of cells to a <see cref="Grid"/> (null leaves a cell empty).</summary>
    public static void AddRow(TableLayoutPanel grid, params Control?[] cells)
    {
        int row = grid.RowCount++;
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        for (int i = 0; i < cells.Length; i++)
        {
            if (cells[i] is { } c)
            {
                grid.Controls.Add(c, i, row);
            }
        }
    }

    public static ColumnStyle Auto => new(SizeType.AutoSize);

    public static ColumnStyle Fill => new(SizeType.Percent, 100f);

    /// <summary>Controls side by side (or stacked when <paramref name="down"/>), sized to them.</summary>
    public static FlowLayoutPanel Flow(bool down = false, params Control[] controls)
    {
        var flow = new FlowLayoutPanel
        {
            FlowDirection = down ? FlowDirection.TopDown : FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
            Padding = new Padding(0),
        };
        flow.Controls.AddRange(controls);
        return flow;
    }

    /// <summary>
    /// Grows <paramref name="form"/> so <paramref name="root"/> fits without scrolling, up to the screen's working
    /// area: text at a large display scaling, or a long path, makes the contents taller than the window was laid out
    /// for. Call once the form has its handle and scaling (OnLoad).
    /// </summary>
    public static void FitToContent(Form form, Control root)
    {
        // Minimised the client area is empty and everything seems to overhang; maximised it is the user's choice.
        if (form.WindowState != FormWindowState.Normal)
        {
            return;
        }

        Rectangle work = Screen.FromControl(form).WorkingArea;
        Size chrome = form.Size - form.ClientSize;
        int width = Math.Min(form.ClientSize.Width, work.Width - chrome.Width);
        Size wanted = root.GetPreferredSize(new Size(width - form.Padding.Horizontal, 0));
        int height = wanted.Height + form.Padding.Vertical;
        if (height > form.ClientSize.Height)
        {
            form.ClientSize = new Size(width, Math.Min(height, work.Height - chrome.Height));
        }

        // The estimate can come out a little short (text wraps differently once laid out): grow by what the last
        // row still overhangs.
        for (int pass = 0; pass < 3; pass++)
        {
            root.PerformLayout();
            int bottom = 0;
            foreach (Control c in root.Controls)
            {
                if (c.Visible)
                {
                    bottom = Math.Max(bottom, c.Bounds.Bottom + c.Margin.Bottom);
                }
            }

            int overhang = bottom - root.ClientSize.Height;
            int room = work.Height - chrome.Height - form.ClientSize.Height;
            if (overhang <= 0 || room <= 0)
            {
                break;
            }

            form.ClientSize = new Size(form.ClientSize.Width, form.ClientSize.Height + Math.Min(overhang, room));
        }

        if (form.Bottom > work.Bottom || form.Top < work.Top)
        {
            form.Top = Math.Max(work.Top, work.Bottom - form.Height);
        }
    }

    public static TextBox TextBox() => new()
    {
        BackColor = Panel,
        ForeColor = Text,
        BorderStyle = BorderStyle.FixedSingle,
        Font = Body,
    };
}

/// <summary>A flat green progress bar; the system bar ignores colours under visual styles.</summary>
internal sealed class GreenProgressBar : Control
{
    private double _fraction;
    private ThemePalette _palette = ThemePalette.Game;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public ThemePalette Palette
    {
        get => _palette;
        set { _palette = value; BackColor = value.Panel; Invalidate(); }
    }

    public GreenProgressBar()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Panel;
        Height = 8;
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public double Fraction
    {
        get => _fraction;
        set
        {
            _fraction = Math.Clamp(value, 0, 1);
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Palette.Panel);
        int width = (int)Math.Round((ClientSize.Width - 2) * _fraction);
        if (width > 0)
        {
            using var brush = new SolidBrush(Palette.Accent);
            e.Graphics.FillRectangle(brush, 1, 1, width, ClientSize.Height - 2);
        }

        using var pen = new Pen(Palette.Border);
        e.Graphics.DrawRectangle(pen, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
    }
}

/// <summary>
/// The OpenSCCT name in two tones, as game/branding's splash draws it (tools/branding/make_branding.py): "Open" in the
/// light text colour and "SCCT" in the accent green.
/// </summary>
internal sealed class Wordmark : Control
{
    private ThemePalette _palette = ThemePalette.Game;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public ThemePalette Palette
    {
        get => _palette;
        set { _palette = value; Invalidate(); }
    }
    private const string Light = "Open";
    private const string Bold = "SCCT";
    private const TextFormatFlags Flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;

    /// <summary>The splash's colour for "Open".</summary>
    public static readonly Color LightColor = Color.FromArgb(225, 235, 228);

    public Wordmark()
    {
        SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint
                 | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Color.Transparent;
        Font = Theme.Title;
        Text = Light + Bold;
        AccessibleName = Light + Bold;
        Size = GetPreferredSize(Size.Empty);
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        Size light = TextRenderer.MeasureText(Light, Font, Size.Empty, Flags);
        Size bold = TextRenderer.MeasureText(Bold, Font, Size.Empty, Flags);
        // A little room below for descenders and the label-like line height.
        return new Size(light.Width + bold.Width + 2, Math.Max(light.Height, bold.Height) + 4);
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        Size = GetPreferredSize(Size.Empty);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        int light = TextRenderer.MeasureText(e.Graphics, Light, Font, Size.Empty, Flags).Width;
        TextRenderer.DrawText(e.Graphics, Light, Font, new Point(0, 0), LightColor, Flags);
        TextRenderer.DrawText(e.Graphics, Bold, Font, new Point(light, 0), Palette.Accent, Flags);
    }
}
