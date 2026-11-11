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
    private readonly ComboBox _renderer = new()
    {
        Name = "GameRenderer", AccessibleName = "Game renderer", DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat,
        BackColor = Theme.Panel, ForeColor = Theme.Text, Font = Theme.Body,
    };

    private readonly ComboBox _monitor = new()
    {
        Name = "GameMonitor", AccessibleName = "Game monitor", DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat,
        BackColor = Theme.Panel, ForeColor = Theme.Text, Font = Theme.Body,
    };
    private readonly int _savedMonitor;

    private readonly Label _importStatus = Theme.Label("", Theme.Small, Theme.Muted);
    private readonly Button _importCancel = Theme.Button("Cancel import");

    public SettingsForm(LauncherSettings settings, LauncherLayout layout, ThemePalette? palette = null,
        string graphicsDriver = GameRendererSettings.Vulkan, bool gameRunning = false, int monitor = 1, string? enhancedImport = null)
    {
        EnhancedImport = enhancedImport;
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(layout);
        _layout = layout;
        _editorMode = palette == ThemePalette.Editor || (palette is null && settings.SelectedProduct == "Editor");
        Result = settings;

        Text = _editorMode ? "Editor settings" : "Game settings";
        Icon = _editorMode ? Theme.LoadEditorIcon(32) : Theme.LoadIcon(32);
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96f, 96f);
        ClientSize = new Size(560, _editorMode ? 440 : 680);
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
        _renderer.Items.AddRange(GameRendererSettings.Choices);
        _renderer.SelectedItem = graphicsDriver;
        if (_renderer.SelectedIndex < 0) _renderer.SelectedItem = GameRendererSettings.Vulkan;
        _renderer.Enabled = !gameRunning;
        _savedMonitor = monitor;
        // The game's numbering: the primary screen is monitor 1, the others follow from left to right.
        Screen[] screens = Screen.AllScreens;
        foreach (int i in GameMonitorSettings.Order(Array.ConvertAll(screens, s => (s.Primary, s.Bounds.X, s.Bounds.Y))))
        {
            Screen screen = screens[i];
            _monitor.Items.Add($"{_monitor.Items.Count + 1}: {screen.Bounds.Width} x {screen.Bounds.Height}{(screen.Primary ? " (primary)" : "")}");
        }

        _monitor.SelectedIndex = Math.Clamp(monitor, 1, Math.Max(1, _monitor.Items.Count)) - 1;
        _monitor.Enabled = !gameRunning;

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
        if (!_editorMode)
        {
            Row(Theme.Label("Monitor", Theme.Heading, Theme.Accent), 18);
            Row(_monitor, 6);
            Row(Theme.Wrapping(Theme.Label(gameRunning
                ? "Close the game to change its monitor."
                : "The screen the game opens on, the same setting as Options > Display > Monitor in the game.", Theme.Small, Theme.Muted)), 4);

            Row(Theme.Label("Import from Enhanced SCCT Versus", Theme.Heading, Theme.Accent), 18);
            Row(Theme.Wrapping(Theme.Label(
                "Bring your Enhanced settings into OpenSCCT the next time the game starts: sensitivities, key bindings, display, HUD, voice, volumes and gear. " +
                "Choose any folder of your Enhanced install. Without the install, only key bindings, volumes and gear can be imported.", Theme.Small, Theme.Muted)), 6);
            Button choose = Theme.Button("Choose Enhanced folder...");
            choose.Name = "ChooseEnhancedFolder";
            choose.Click += (_, _) => ChooseEnhancedFolder();
            Button profileOnly = Theme.Button("Bindings only (no install)");
            profileOnly.Name = "ImportEnhancedProfileOnly";
            profileOnly.Click += (_, _) => SetEnhancedImport("");
            _importCancel.Name = "CancelEnhancedImport";
            _importCancel.Click += (_, _) => SetEnhancedImport(null);
            Row(Theme.Flow(false, choose, profileOnly, _importCancel), 4);
            _importStatus.Name = "EnhancedImportStatus";
            Row(Theme.Wrapping(_importStatus), 4);
            SetEnhancedImport(EnhancedImport);
        }
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

    public string GraphicsDriver => _renderer.SelectedItem as string ?? GameRendererSettings.Vulkan;

    public bool CanChangeRenderer => !_editorMode && _renderer.Enabled;

    /// <summary>The chosen monitor, numbered from 1 as the game numbers them.</summary>
    public int Monitor => _monitor.SelectedIndex + 1;

    /// <summary>True when the player picked another monitor than the saved one (an untouched choice writes nothing).</summary>
    public bool MonitorChanged => !_editorMode && _monitor.Enabled && _monitor.SelectedIndex >= 0 && Monitor != _savedMonitor;

    /// <summary>
    /// The Enhanced import to carry out at the game's next start: the install folder, empty for the profile and key
    /// layouts alone, or null for none (<see cref="EnhancedImportRequest"/>).
    /// </summary>
    public string? EnhancedImport { get; private set; }

    private void ChooseEnhancedFolder()
    {
        using var picker = new FolderBrowserDialog
        {
            Description = "Choose your Enhanced SCCT Versus folder",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false,
            InitialDirectory = EnhancedImport is { Length: > 0 } current && Directory.Exists(current)
                ? current : Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        };
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        if (!EnhancedImportRequest.LooksLikeInstall(picker.SelectedPath))
        {
            MessageBox.Show(this,
                "No Enhanced SCCT Versus install was found there.\n\nChoose the install's folder (the one holding System) or any folder inside it.",
                "Import from Enhanced SCCT Versus", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        SetEnhancedImport(picker.SelectedPath);
    }

    private void SetEnhancedImport(string? folder)
    {
        EnhancedImport = folder;
        _importCancel.Visible = folder is not null;
        _importStatus.Text = folder switch
        {
            null => "Nothing is queued.",
            "" => "Queued for the next start: key bindings, volumes and gear from this PC's Enhanced profile. Save to confirm.",
            _ => "Queued for the next start: everything from " + folder + ". Save to confirm.",
        };
    }

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
