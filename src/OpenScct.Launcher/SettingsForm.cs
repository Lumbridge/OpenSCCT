using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using OpenScct.Launcher.Editor;
using OpenScct.Launcher.Install;
using OpenScct.Launcher.Settings;

namespace OpenScct.Launcher.App;

internal sealed class SettingsForm : Form
{
    private readonly LauncherLayout _layout;
    private readonly bool _editorMode;
    private readonly ToolTip _folderTips = new();
    private readonly CheckBox _prereleases = new() { Text = "Include game preview releases", AutoSize = true, ForeColor = Theme.Text };
    private readonly CheckBox _editorPrereleases = new() { Text = "Include editor preview releases", AutoSize = true, ForeColor = Theme.Text };
    private readonly CheckBox _checkOnStart = new() { Text = "Check for updates on startup", AutoSize = true, ForeColor = Theme.Text };

    public SettingsForm(LauncherSettings settings, LauncherLayout layout, ThemePalette? palette = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(layout);
        _layout = layout;
        _editorMode = palette == ThemePalette.Editor || (palette is null && settings.SelectedProduct == "Editor");
        Result = settings;

        Text = _editorMode ? "Editor settings" : "Game settings";
        Icon = _editorMode ? Theme.LoadEditorIcon(32) : Theme.LoadIcon(32);
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
        _editorPrereleases.Checked = settings.EditorIncludePrereleases;
        _editorPrereleases.Name = "IncludeEditorPreviewReleases";
        _checkOnStart.Checked = _editorMode ? settings.EditorCheckOnStart : settings.CheckOnStart;
        _checkOnStart.Text = _editorMode ? "Check for editor updates on startup" : "Check for game updates on startup";
        _prereleases.Name = "IncludePreviewReleases";
        _checkOnStart.Name = _editorMode ? "CheckForEditorUpdatesOnStart" : "CheckForUpdatesOnStart";

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
        Row(_editorMode ? _editorPrereleases : _prereleases, 4);
        Row(Theme.Label("Folders", Theme.Heading, Theme.Accent), 18);
        if (_editorMode)
        {
            Row(Link("Open editor installation folder", layout.Editor, "OpenEditorFolder"), 6);
            Row(Link("Open editor settings and autosaves folder",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OpenSCCT Editor"), "OpenEditorUserData"), 4);
            Button uninstall = Theme.Button("Uninstall editor...");
            uninstall.Name = "UninstallEditor";
            uninstall.Enabled = Directory.Exists(layout.Editor) && File.Exists(Path.Combine(layout.Editor, EditorService.ManagedMarker));
            uninstall.Click += (_, _) => ConfirmUninstallEditor();
            Row(Theme.Label("Editor installation", Theme.Heading, Theme.Accent), 18);
            Row(Theme.Wrapping(Theme.Label("Remove the editor application. Keeps your projects, editor settings and autosaves, and all game and shared files.", Theme.Small, Theme.Muted)), 6);
            Row(uninstall);
            if (!uninstall.Enabled)
                Row(Theme.Wrapping(Theme.Label("No editor installed by this launcher.", Theme.Small, Theme.Muted)), 4);
        }
        else
        {
            Row(Link("Open game installation folder", layout.Versions, "OpenGameFolder"), 6);
            Row(Link("Open game settings and saves folder", LauncherLayout.GameUserData, "OpenGameUserData"), 4);
            Button remove = Theme.Button("Remove game and shared files...");
            remove.Name = "RemoveGameFiles";
            remove.Click += (_, _) => ConfirmRemoveGameFiles();
            Row(Theme.Label("Game installation", Theme.Heading, Theme.Accent), 18);
            Row(Theme.Wrapping(Theme.Label("Remove all installed game versions and downloaded assets/maps. Keeps settings and saves. The editor application is kept, but its shared assets will need downloading again.", Theme.Small, Theme.Muted)), 6);
            Row(remove);
        }
        scroll.Controls.Add(root);
        Controls.Add(scroll);
        Controls.Add(buttons);
        Theme.Apply(this, palette ?? (_editorMode ? ThemePalette.Editor : ThemePalette.Game));
    }

    public LauncherSettings Result { get; private set; }

    /// <summary>The player asked to remove downloaded game files; preferences and saves are kept.</summary>
    public bool RemoveGameFilesRequested { get; private set; }

    public bool UninstallEditorRequested { get; private set; }

    private void ConfirmRemoveGameFiles()
    {
        DialogResult answer = MessageBox.Show(
            this,
            "Remove all downloaded game versions, shared game files and maps?\n\nThe editor will need its shared files installed again. The editor application, projects, settings, saves and launcher preferences are kept.",
            "Remove game and shared files",
            MessageBoxButtons.OKCancel,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        if (answer == DialogResult.OK)
        {
            RemoveGameFilesRequested = true;
            Save();
        }
    }

    private void ConfirmUninstallEditor()
    {
        if (MessageBox.Show(this,
            "Uninstall OpenSCCT Editor?\n\nYour projects, editor settings and autosaves are kept. The game and shared assets are kept.\n\nAny extra or modified files inside the editor installation will be kept in a separate folder. You can install the editor again from the Editor tab.",
            "Uninstall editor", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.OK)
            return;
        UninstallEditorRequested = true;
        Save();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _folderTips.Dispose();
        base.Dispose(disposing);
    }

    private LinkLabel Link(string text, string folder, string name)
    {
        var link = new LinkLabel
        {
            Text = text,
            Name = name,
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
            EditorIncludePrereleases = _editorPrereleases.Checked,
            SelectedProduct = Result.SelectedProduct,
            CheckOnStart = _editorMode ? Result.CheckOnStart : _checkOnStart.Checked,
            EditorCheckOnStart = _editorMode ? _checkOnStart.Checked : Result.EditorCheckOnStart,
        };
        Directory.CreateDirectory(_layout.Root);
        DialogResult = DialogResult.OK;
        Close();
    }
}
