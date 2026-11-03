using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using OpenScct.Launcher.Install;
using OpenScct.Launcher.Settings;

namespace OpenScct.Launcher.App;

internal sealed class SettingsForm : Form
{
    private readonly LauncherLayout _layout;
    private readonly ToolTip _folderTips = new();
    private readonly CheckBox _prereleases = new() { Text = "Include preview releases", AutoSize = true, ForeColor = Theme.Text };
    private readonly CheckBox _checkOnStart = new() { Text = "Check for updates on startup", AutoSize = true, ForeColor = Theme.Text };

    public SettingsForm(LauncherSettings settings, LauncherLayout layout)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(layout);
        _layout = layout;
        Result = settings;

        Text = "Settings";
        Icon = Theme.LoadIcon(32);
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96f, 96f);
        ClientSize = new Size(560, 440);
        MinimumSize = new Size(460, 360);
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Font = Theme.Body;

        _prereleases.Checked = settings.IncludePrereleases;
        _checkOnStart.Checked = settings.CheckOnStart;
        _prereleases.Name = "IncludePreviewReleases";
        _checkOnStart.Name = "CheckForUpdatesOnStart";

        Button ok = Theme.Button("Save");
        ok.Name = "SaveSettings";
        ok.MinimumSize = new Size(92, 32);
        ok.Click += (_, _) => Save();
        Button cancel = Theme.Button("Cancel");
        cancel.Name = "CancelSettings";
        cancel.MinimumSize = new Size(92, 32);
        cancel.DialogResult = DialogResult.Cancel;
        FlowLayoutPanel buttons = Theme.Flow(false, cancel, ok);
        buttons.FlowDirection = FlowDirection.RightToLeft;
        buttons.Dock = DockStyle.Bottom;
        buttons.Padding = new Padding(20, 12, 20, 12);
        AcceptButton = ok;
        CancelButton = cancel;

        // Keep Save and Cancel visible even when the content needs to scroll at a larger display scaling.
        var scroll = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            Padding = new Padding(20, 16, 20, 12),
            BackColor = Theme.Background,
        };
        TableLayoutPanel root = Theme.Grid(Theme.Fill);
        root.Dock = DockStyle.Top;
        void Row(Control c, int top = 0)
        {
            c.Margin = new Padding(0, top, 0, 2);
            c.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            Theme.AddRow(root, c);
        }

        Row(Theme.Label("Updates", Theme.Heading, Theme.Accent));
        Row(_checkOnStart, 6);
        Row(_prereleases, 4);
        Row(Theme.Label("Folders", Theme.Heading, Theme.Accent), 18);
        Row(Link("Open launcher folder", layout.Root), 6);
        Row(Link("Open game settings and saves folder", LauncherLayout.GameUserData), 4);
        Button reset = Theme.Button("Reset install...");
        reset.Click += (_, _) => ConfirmReset();
        Row(Theme.Label("Reset", Theme.Heading, Theme.Accent), 18);
        Row(Theme.Wrapping(Theme.Label("Remove installed versions and game files, then download a fresh install. Keeps your launcher preferences, game settings and saves.", Theme.Small, Theme.Muted)), 6);
        Row(reset);
        scroll.Controls.Add(root);
        Controls.Add(scroll);
        Controls.Add(buttons);
    }

    public LauncherSettings Result { get; private set; }

    /// <summary>The player asked to reset the install (the settings in <see cref="Result"/> are saved first).</summary>
    public bool ResetRequested { get; private set; }

    private void ConfirmReset()
    {
        DialogResult answer = MessageBox.Show(
            this,
            "Remove all downloaded game versions, game files and maps?\n\nThe launcher will download everything again. Your launcher preferences, game settings and saves are kept.",
            "Reset install",
            MessageBoxButtons.OKCancel,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        if (answer == DialogResult.OK)
        {
            ResetRequested = true;
            Save();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _folderTips.Dispose();
        base.Dispose(disposing);
    }

    private LinkLabel Link(string text, string folder)
    {
        var link = new LinkLabel
        {
            Text = text,
            AutoSize = true,
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            Font = Theme.Small,
            LinkColor = Theme.Accent,
            ActiveLinkColor = Theme.Text,
            VisitedLinkColor = Theme.Accent,
            AccessibleDescription = folder,
        };
        _folderTips.SetToolTip(link, folder);
        link.LinkClicked += (_, _) =>
        {
            if (Directory.Exists(folder))
            {
                using Process? explorer = Process.Start(new ProcessStartInfo("explorer.exe", "\"" + folder + "\"") { UseShellExecute = false });
            }
        };
        return link;
    }

    private void Save()
    {
        Result = new LauncherSettings
        {
            IncludePrereleases = _prereleases.Checked,
            CheckOnStart = _checkOnStart.Checked,
        };
        Directory.CreateDirectory(_layout.Root);
        DialogResult = DialogResult.OK;
        Close();
    }
}
