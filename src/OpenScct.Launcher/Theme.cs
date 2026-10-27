using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace OpenScct.Launcher.App;

/// <summary>The night-vision look of game/branding: near-black green panels, bright green accents.</summary>
internal static class Theme
{
    public static readonly Color Background = Color.FromArgb(16, 22, 18);
    public static readonly Color Panel = Color.FromArgb(22, 31, 25);
    public static readonly Color Border = Color.FromArgb(36, 51, 42);
    public static readonly Color Accent = Color.FromArgb(120, 255, 140);
    public static readonly Color AccentDim = Color.FromArgb(58, 140, 74);
    public static readonly Color Text = Color.FromArgb(216, 232, 220);
    public static readonly Color Muted = Color.FromArgb(127, 149, 133);
    public static readonly Color Warning = Color.FromArgb(255, 196, 92);
    public static readonly Color Error = Color.FromArgb(255, 110, 100);

    public static readonly Font Body = new("Segoe UI", 9.5f);
    public static readonly Font Small = new("Segoe UI", 8.5f);
    public static readonly Font Heading = new("Segoe UI Semibold", 11f);
    public static readonly Font Title = new("Segoe UI Semibold", 20f);
    public static readonly Font PlayFont = new("Segoe UI Semibold", 14f);
    public static readonly Font Mono = new("Consolas", 9.5f);

    public static Icon LoadIcon(int size)
    {
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("OpenScct.Launcher.icon.ico")
            ?? throw new InvalidOperationException("Icon resource missing.");
        return new Icon(stream, size, size);
    }

    public static Button Button(string text, bool primary = false)
    {
        // Sized to its text (at any display scaling), never narrower than its minimum.
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = primary ? new Padding(24, 8, 24, 8) : new Padding(12, 4, 12, 4),
            MinimumSize = primary ? new Size(180, 52) : new Size(0, 32),
            FlatStyle = FlatStyle.Flat,
            BackColor = primary ? AccentDim : Panel,
            ForeColor = primary ? Color.White : Text,
            Font = primary ? PlayFont : Body,
            Cursor = Cursors.Hand,
            UseVisualStyleBackColor = false,
        };
        button.FlatAppearance.BorderColor = primary ? Accent : Border;
        button.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(72, 170, 90) : Border;
        button.FlatAppearance.MouseDownBackColor = primary ? Color.FromArgb(48, 118, 62) : Background;
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
        e.Graphics.Clear(Theme.Panel);
        int width = (int)Math.Round((ClientSize.Width - 2) * _fraction);
        if (width > 0)
        {
            using var brush = new SolidBrush(Theme.Accent);
            e.Graphics.FillRectangle(brush, 1, 1, width, ClientSize.Height - 2);
        }

        using var pen = new Pen(Theme.Border);
        e.Graphics.DrawRectangle(pen, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
    }
}
