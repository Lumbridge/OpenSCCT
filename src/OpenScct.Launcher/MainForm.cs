using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using OpenScct.Launcher.Content;
using OpenScct.Launcher.GitHub;
using OpenScct.Launcher.Install;
using OpenScct.Launcher.Launch;
using OpenScct.Maps;
using OpenScct.Launcher.Settings;
using OpenScct.Launcher.Unreal;
using OpenScct.Launcher.Update;
using OpenScct.Launcher.Versioning;
using OpenScct.Project;

namespace OpenScct.Launcher.App;

/// <summary>
/// A play-first launcher: one action installs, updates or starts OpenSCCT. Setup phases share a status panel;
/// release highlights and less common tools stay available below it.
/// </summary>
internal sealed partial class MainForm : Form
{
    private static readonly HttpClient Http = new(new SocketsHttpHandler
    {
        AutomaticDecompression = DecompressionMethods.All,
        ConnectTimeout = TimeSpan.FromSeconds(15),
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
    })
    {
        // Covers the time to the response headers; a download body is read without this limit.
        Timeout = TimeSpan.FromSeconds(60),
    };

    /// <summary>What the big button does now.</summary>
    private enum NextAction
    {
        None,
        Check,
        Install,
        Update,
        ChooseSource,
        BuildFiles,
        Play,
        Cancel,
    }

    private readonly LauncherLayout _layout;
    private readonly bool _discoverRetail;
    private readonly VersionStore _store;
    private readonly IGameProcessProbe _games = new GameProcessProbe();
    private readonly LauncherSwap? _swap;
    private readonly SemVersion? _launcherVersion;
    private LauncherSettings _settings;
    private UpdateService _service;
    private UpdateCheck? _lastCheck;
    private CancellationTokenSource? _work;
    private readonly GodotSteamContent _steamContent;
    private Lazy<string?> _steamSource;
    private string? _steamContentRoot;
    private NextAction _next;
    private string _notesTitle = "What's new";
    private string _notesText = "Checking for releases...";
    private string _operationTitle = "Getting ready";
    private TableLayoutPanel? _mainLayout;
    private Panel? _releasePreview;
    private TableLayoutPanel? _releaseNotes;
    private FlowLayoutPanel? _tools;
    private bool _initialSizeSet;
    private bool _initialFitQueued;
    private readonly Label _heading = Theme.Wrapping(Theme.Label("Install OpenSCCT", Theme.Status));
    private readonly Label _installedInfo = Theme.Wrapping(Theme.Label(string.Empty, Theme.Small, Theme.Muted));
    private readonly Label _launchDetail = Theme.Wrapping(Theme.Label(string.Empty, Theme.Body, Theme.Muted));
    private readonly Label _previewTitle = Theme.Wrapping(Theme.Label("Latest release", Theme.Heading));
    private readonly Label[] _previewItems = new Label[3];
    private readonly GreenProgressBar _progress = new();
    private readonly Label _progressText = Theme.Label(string.Empty, Theme.Small, Theme.Muted);
    private readonly Button _main = Theme.Button("PLAY", primary: true);
    private readonly Button _playCurrent = Theme.QuietButton("Play current version", onPanel: true);
    private readonly LinkLabel _status = new()
    {
        AutoSize = true, Font = Theme.Body, ForeColor = Theme.Muted, BackColor = Color.Transparent,
        LinkColor = Theme.Accent, ActiveLinkColor = Theme.Text, VisitedLinkColor = Theme.Accent, UseMnemonic = false,
    };
    private readonly Button _forceClose = Theme.Button("Force close all…");
    private readonly Button _whatsNew = Theme.QuietButton("Full changelog", onPanel: true);
    private readonly Button _check = Theme.QuietButton("Check for updates");
    private readonly Button _website = Theme.QuietButton("Website");
    private readonly Button _help = Theme.QuietButton("&Help");
    private readonly Button _mapsButton = Theme.QuietButton("Maps");
    private readonly Button _settingsButton = Theme.QuietButton("Settings");
    private readonly TableLayoutPanel _launcherBanner = Theme.Grid(Theme.Fill, Theme.Auto);
    private readonly Label _launcherInfo = Theme.Label(string.Empty, Theme.Body, Theme.Warning);
    private readonly Button _launcherButton = Theme.Button("Restart to finish");
    private readonly System.Windows.Forms.Timer _pendingTimer = new() { Interval = 3000 };
    private readonly ToolTip _tips = new();

    public MainForm(string? joinServerId = null, string? protocolProblem = null)
        : this(new LauncherLayout(LauncherLayout.DefaultRoot), joinServerId, protocolProblem, Program.ExePath, true)
    {
    }

    // Also used by the offscreen smoke harness, with an isolated installation and launcher path.
    internal MainForm(LauncherLayout layout, string? joinServerId, string? protocolProblem, string? launcherPath)
        : this(layout, joinServerId, protocolProblem, launcherPath, false)
    {
    }

    private MainForm(LauncherLayout layout, string? joinServerId, string? protocolProblem, string? launcherPath, bool discoverRetail)
    {
        _discoverRetail = discoverRetail;
        _layout = layout;
        _pendingWebJoin = joinServerId;
        _protocolProblem = protocolProblem;
        _store = new VersionStore(_layout);
        _settings = LauncherSettings.Load(_layout.Settings);
        _steamContent = new GodotSteamContent(_layout);
        _steamSource = new Lazy<string?>(() => discoverRetail || _settings.GodotSteamFolder is not null
            ? GodotSteamContent.FindSource(_settings.GodotSteamFolder) : null);
        _service = CreateService();
        _editor = new OpenScct.Launcher.Editor.EditorService(_layout, Http);
        _unreal = new UnrealService(_layout, Http, _settings.UnrealRepository);
        // A website Join always opens the Game tab.
        _selected = joinServerId is not null ? Product.Game
            : File.Exists(_layout.Settings) || File.Exists(_layout.Manifest) ? ParseProduct(_settings.SelectedProduct) : Product.Ps2;
        if (launcherPath is { } exe)
        {
            _swap = new LauncherSwap(exe);
        }

        string? info = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        _launcherVersion = SemVersion.TryParse(info, out SemVersion? v) ? v : null;

        BuildUi();
        ApplyProductTheme();
        _pendingTimer.Tick += (_, _) => OnPendingTick();
    }

    private UpdateService CreateService()
    {
        var github = new GitHubReleaseClient(Http, ReleaseSource.Official, _layout.Cache);
        return new UpdateService(_layout, _store, github, Http, _games);
    }

    private void BuildUi()
    {
        Text = "OpenSCCT Launcher";
        Icon = Theme.LoadIcon(32);
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96f, 96f);
        ClientSize = new Size(1060, 670);
        MinimumSize = new Size(600, 420);
        FormBorderStyle = FormBorderStyle.Sizable;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Font = Theme.Body;
        Padding = new Padding(16, 12, 16, 12);

        _launcherBanner.Name = "LauncherUpdate";
        _launcherBanner.BackColor = Theme.Panel;
        _launcherBanner.Padding = new Padding(14, 10, 14, 10);
        _launcherBanner.Margin = new Padding(0, 0, 0, 12);
        _launcherBanner.Visible = false;
        _launcherButton.Anchor = AnchorStyles.Right;
        _launcherButton.Name = "UpdateLauncher";
        Theme.AddRow(_launcherBanner, Theme.Wrapping(_launcherInfo), _launcherButton);

        _heading.Name = "LaunchHeading";
        _heading.Margin = new Padding(0);
        _installedInfo.Name = "InstalledVersion";
        _installedInfo.Margin = new Padding(0, 6, 0, 0);
        _launchDetail.Margin = new Padding(0, 8, 0, 0);
        TableLayoutPanel summary = Theme.Grid(Theme.Fill);
        Theme.AddRow(summary, _heading);
        Theme.AddRow(summary, _installedInfo);
        Theme.AddRow(summary, _launchDetail);
        summary.Margin = new Padding(0, 0, 16, 0);

        _main.Name = "LaunchAction";
        _main.Anchor = AnchorStyles.Right | AnchorStyles.Top;
        _main.Margin = new Padding(0);
        _main.TabIndex = 0;
        _playCurrent.Name = "PlayCurrent";
        _playCurrent.Anchor = AnchorStyles.Right;
        _playCurrent.Margin = new Padding(0, 4, 0, 0);
        _playCurrent.Visible = false;
        TableLayoutPanel actions = Theme.Grid(Theme.Auto);
        Theme.AddRow(actions, _main);
        Theme.AddRow(actions, _playCurrent);
        Theme.AddRow(actions, _retailSecondary);
        actions.Anchor = AnchorStyles.Right | AnchorStyles.Top;

        Theme.Wrapping(_status);
        _status.Name = "OperationStatus";
        _status.AccessibleRole = AccessibleRole.Alert;
        _status.Margin = new Padding(0, 12, 0, 0);
        _status.Visible = false;
        _status.LinkClicked += (_, e) => ShowInExplorer(e.Link?.LinkData as string);
        _forceClose.Name = "ForceCloseProcesses";
        _forceClose.Anchor = AnchorStyles.Left;
        _forceClose.Margin = new Padding(0, 6, 0, 0);
        _forceClose.Visible = false;
        _forceClose.Click += async (_, _) =>
        {
            switch (_selected)
            {
                case Product.Editor: await ForceCloseAndUpdateEditorAsync(); break;
                case Product.Unreal: await ForceCloseAndUpdateUnrealAsync(); break;
                default: await ForceCloseAndRemoveAsync(); break;
            }
        };
        _progress.Name = "DownloadProgress";
        _progress.Visible = false;
        _progress.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _progress.Margin = new Padding(0, 14, 0, 0);
        Theme.Wrapping(_progressText);
        _progressText.Margin = new Padding(0, 6, 0, 0);
        _progressText.Visible = false;

        TableLayoutPanel launch = Theme.Grid(Theme.Fill, Theme.Auto);
        launch.Name = "LaunchPanel";
        launch.BackColor = Theme.Panel;
        launch.Padding = new Padding(20, 18, 20, 18);
        launch.TabIndex = 0;
        Theme.AddRow(launch, summary, actions);
        foreach (Control row in new Control[] { _status, _forceClose, _progress, _progressText })
        {
            Theme.AddRow(launch, row);
            launch.SetColumnSpan(row, 2);
        }

        _whatsNew.Name = "FullChangelog";
        _whatsNew.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _whatsNew.Margin = new Padding(12, 0, 0, 0);
        _previewTitle.Name = "ReleaseTitle";
        _previewTitle.UseMnemonic = false;
        _previewTitle.Margin = new Padding(0, 6, 0, 0);
        TableLayoutPanel notesHeader = Theme.Grid(Theme.Fill, Theme.Auto);
        Theme.AddRow(notesHeader, _previewTitle, _whatsNew);
        TableLayoutPanel notes = Theme.Grid(Theme.Fill);
        notes.Dock = DockStyle.Top;
        Theme.AddRow(notes, notesHeader);
        for (int i = 0; i < _previewItems.Length; i++)
        {
            Label item = Theme.Wrapping(Theme.Label(string.Empty));
            item.Name = "ReleaseHighlight" + (i + 1);
            item.UseMnemonic = false;
            item.Margin = new Padding(0, 10, 0, 0);
            _previewItems[i] = item;
            Theme.AddRow(notes, item);
        }
        var preview = new Panel
        {
            Name = "ReleasePreview",
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = Theme.Panel,
            Padding = new Padding(16, 10, 16, 16),
            Margin = new Padding(0, 16, 0, 0),
            TabIndex = 1,
        };
        preview.Controls.Add(notes);
        _releasePreview = preview;
        _releaseNotes = notes;
        RefreshReleasePreview();

        FlowLayoutPanel more = Theme.Flow(false, _check, _mapsButton);
        more.Name = "LauncherTools";
        more.Dock = DockStyle.Bottom;
        more.WrapContents = true;
        more.Padding = new Padding(0, 10, 0, 0);
        more.TabIndex = 1;
        _tools = more;
        foreach (Control tool in more.Controls) tool.Margin = new Padding(0, 0, 6, 0);

        BuildLibraryShell(launch, preview, more);

        _tips.SetToolTip(_website, "Visit the OpenSCCT website in your browser.");
        _tips.SetToolTip(_help, "Get help, suggest a feature or contribute to OpenSCCT.");
        _tips.SetToolTip(_mapsButton, "Download new maps made by the community.");

        _retailSecondary.Click += (_, _) => RetailSecondaryAction();
        _main.Click += async (_, _) => await DoNextAsync();
        _playCurrent.Click += async (_, _) =>
        {
            switch (_selected)
            {
                case Product.Editor: OpenEditor(); break;
                case Product.Unreal: await OpenUnrealAsync(); break;
                default: await PlayOrJoinAsync(); break;
            }
        };
        _whatsNew.Click += async (_, _) => await OpenChangelogAsync();
        _check.Click += async (_, _) => await RunAsync(CheckSelectedAsync);
        _website.Click += (_, _) => OpenLink(_selected switch
        {
            Product.Editor => EditorPage,
            Product.Unreal => _unreal.ReleasesPage,
            _ => "https://master.lumbridge.co.uk/",
        });
        _help.Click += (_, _) => OpenHelp();
        _settingsButton.Click += (_, _) => SelectPage(LibraryPage.Settings);
        _mapsButton.Click += (_, _) => OpenMaps();
        _launcherButton.Click += async (_, _) => await RunAsync(UpdateLauncherAsync);
    }

    private void RefreshReleasePreview()
    {
        ReleaseInfo? release = SelectedRelease;
        IReadOnlyList<string> highlights = ReleaseNotesPreview.Create(release?.Body);
        string when = release?.PublishedAt?.ToLocalTime().ToString("d MMM yyyy", CultureInfo.CurrentCulture) ?? string.Empty;
        _previewTitle.Text = release is null ? "Latest release" : "Latest: " + (release.Name ?? release.TagName)
            + (when.Length == 0 ? string.Empty : " · " + when);
        for (int i = 0; i < _previewItems.Length; i++)
        {
            Label item = _previewItems[i];
            item.Visible = i < highlights.Count || (i == 0 && highlights.Count == 0);
            item.ForeColor = highlights.Count == 0 ? Palette.Muted : Palette.Text;
            item.Text = i < highlights.Count ? "•  " + highlights[i]
                : !HasChecked(_selected) ? "Check for updates to load release notes."
                : release is null ? "No release information available." : "No release notes available.";
        }
        QueueInitialFit();
    }

    private void QueueInitialFit()
    {
        if (_initialSizeSet || _initialFitQueued || !IsHandleCreated || IsDisposed || Disposing
            || _work is not null || SelectedRelease is null || _page != LibraryPage.Games || IsOriginalEdition) return;
        _initialFitQueued = true;
        BeginInvoke(() =>
        {
            _initialFitQueued = false;
            FitUpdateHeight();
        });
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        // Keep the window on-screen when Windows scales it for a smaller working area.
        Rectangle work = Screen.FromControl(this).WorkingArea;
        MinimumSize = new Size(Math.Min(MinimumSize.Width, work.Width), Math.Min(MinimumSize.Height, work.Height));
        Size = new Size(Math.Min(Width, work.Width), Math.Min(Height, work.Height));
        QueueInitialFit();
    }

    protected override void OnResizeBegin(EventArgs e)
    {
        // A user move or resize takes precedence even if the startup fit is still queued.
        _initialSizeSet = true;
        base.OnResizeBegin(e);
    }

    /// <summary>Choose the initial shared height once; subsequent layout changes use scrolling.</summary>
    private void FitUpdateHeight()
    {
        if (_initialSizeSet || IsDisposed || Disposing || _work is not null || SelectedRelease is null
            || WindowState != FormWindowState.Normal
            || _mainLayout is null || _releasePreview is null || _releaseNotes is null || _tools is null) return;
        _initialSizeSet = true;
        Product selected = _selected;
        Control? focused = ActiveControl;
        Point previewScroll = _releasePreview.AutoScrollPosition;
        SuspendLayout();
        try
        {
            int height = new[] { Product.Game, Product.Editor, Product.Unreal }.Max(MeasureProductHeight);
            Rectangle work = Screen.FromControl(this).WorkingArea;
            int maximum = Math.Max(1, work.Height - (Height - ClientSize.Height));
            int minimum = Math.Max(1, MinimumSize.Height - (Height - ClientSize.Height));
            int target = Math.Min(Math.Max(ClientSize.Height, Math.Max(height, minimum)), maximum);
            // Keep the startup width and use scrolling when the working area cannot fit the content.
            if (target != ClientSize.Height) ClientSize = new Size(ClientSize.Width, target);
            if (Bottom > work.Bottom) Top = Math.Max(work.Top, work.Bottom - Height);
        }
        finally
        {
            _selected = selected;
            SetProductCopy(selected);
            RefreshLocal();
            RefreshReleasePreview();
            ResumeLayout(true);
            _releasePreview.AutoScrollPosition = new Point(-previewScroll.X, -previewScroll.Y);
            if (focused is { IsDisposed: false, CanFocus: true } && ActiveControl != focused) focused.Focus();
        }
    }

    private int MeasureProductHeight(Product product)
    {
        // Render each product synchronously while painting/layout of the form is suspended. This does
        // not select a tab, save settings, start a request, or change operation ownership.
        _selected = product;
        SetProductCopy(product);
        RefreshLocal();
        RefreshReleasePreview();
        _mainLayout!.PerformLayout();
        int notesWidth = Math.Max(1, _releasePreview!.Width - _releasePreview.Padding.Horizontal);
        int height = Padding.Vertical + _mainLayout.Padding.Vertical + _mainLayout.Margin.Vertical
            + _tools!.GetPreferredSize(new Size(Math.Max(1, ClientSize.Width - Padding.Horizontal), 0)).Height
            + _tools.Margin.Vertical + _releasePreview.Margin.Vertical + _releasePreview.Padding.Vertical
            + _releaseNotes!.GetPreferredSize(new Size(notesWidth, 0)).Height + LogicalToDeviceUnits(6);
        foreach (Control row in _mainLayout.Controls)
        {
            if (row == _releasePreview || !row.Visible) continue;
            int width = Math.Max(1, _mainLayout.ClientSize.Width - _mainLayout.Padding.Horizontal - row.Margin.Horizontal);
            height += row.GetPreferredSize(new Size(width, 0)).Height + row.Margin.Vertical;
        }
        return height;
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _service.TryFinishPending();
        FinishUnrealSwitch();
        RefreshLocal();
        _pendingTimer.Start();
        Product started = _selected;
        bool checkedOnStart = SelectedCheckOnStart;
        if (checkedOnStart)
        {
            await RunAsync(CheckSelectedAsync);
        }
        else
        {
            _notesText = "Update checks on start are off. Use Check for updates.";
        }
        if (IsDisposed || Disposing || _closeAfterWork) return;
        _webJoinReady = true;
        if (_pendingWebJoin is not null) await TryWebJoinAsync();
        else if (_protocolProblem is not null) SetStatus(_protocolProblem, Theme.Warning);
        // A tab chosen while the startup check ran was not checked (SelectProduct waits for _webJoinReady).
        if (checkedOnStart) await CheckSwitchedTabAsync(started);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_work is not null)
        {
            _closeAfterWork = true;
            e.Cancel = true;
            if (_operationCanCancel) _work.Cancel();
            SetStatus(_operationCanCancel ? "Stopping safely before closing…" : "Finishing the current operation before closing…", Theme.Muted);
        }
        else _pendingTimer.Stop();
        base.OnFormClosing(e);
    }

    // ---- launch state ---------------------------------------------------------------------------------------------

    private void RefreshLocal()
    {
        SemVersion? current = _store.Current();
        bool busy = _work is not null;

        _steamContentRoot = _steamContent.ActiveRoot();
        RenderProductStatus();
        _retailSecondary.Visible = false;
        switch (_selected)
        {
            case Product.Ps2:
            case Product.Retail: RefreshOriginalPanel(busy); break;
            case Product.Editor: RefreshEditorPanel(busy); break;
            case Product.Unreal: RefreshUnrealPanel(busy); break;
            default: RefreshLaunchPanel(current, busy); break;
        }

        _check.Enabled = !busy;
        _settingsButton.Enabled = !busy;
        _mapsButton.Enabled = !busy;
        _launcherButton.Enabled = !busy;
        if (_swap is { HasStaged: true })
        {
            _launcherInfo.Text = "A new version of this launcher is ready.";
            _launcherButton.Text = "Restart to finish";
            _launcherButton.Visible = true;
            _launcherBanner.Visible = true;
        }

        if (_selected == Product.Game) RefreshMainButton(current, busy);
        UpdateLibraryState(busy);
        QueueInitialFit();
    }

    private void RefreshLaunchPanel(SemVersion? current, bool busy)
    {
        _launchDetail.Visible = !busy && (_status.Text.Length == 0 || (VisibleStatus.Kind is not (StatusKind.Warning or StatusKind.Error)));
        bool compatible = GodotSteamContent.SupportsClient(current);
        bool playable = compatible && _steamContentRoot is not null;
        SemVersion? pending = _store.Pending();
        VersionedRelease? latest = _lastCheck?.Latest;
        bool update = GodotUpdateAvailable(current);
        bool checkedOnline = _lastCheck is { Feed.Source: ReleaseFeedSource.Network or ReleaseFeedSource.NotModified };
        _installedInfo.Text = current is not null
            ? $"Version {current}" + (playable ? "  ·  Steam game files ready" : " installed")
            : latest is not null ? $"Latest version {latest.Version}" : "Godot client and locally built game files";

        if (busy)
        {
            _heading.Text = VisibleOperationTitle;
        }
        else if (current is not null && !compatible)
        {
            _heading.Text = pending is null ? "Godot client update required" : "Update ready";
            _launchDetail.Text = pending is null
                ? "Update the Godot client to build and play files from your Steam installation."
                : $"Close the running game to activate version {pending}.";
        }
        else if (_steamContentRoot is null && _steamSource.Value is null)
        {
            _heading.Text = "Locate Chaos Theory on Steam";
            _launchDetail.Text = "Choose the Steam installation to build the original Versus maps, characters and sounds for Godot.";
        }
        else if (current is null)
        {
            _heading.Text = "Install OpenSCCT Godot";
            _launchDetail.Text = _lastCheck is { Latest: null }
                ? "No download found. Check your connection and try again."
                : "Download the Godot client and build the game files from your Steam installation.";
        }
        else if (pending is not null)
        {
            _heading.Text = "Update ready";
            _launchDetail.Text = $"Version {pending} will become active after the running game closes.";
        }
        else if (update)
        {
            _heading.Text = "Update available";
            _launchDetail.Text = $"Version {latest!.Version} is ready to download.";
        }
        else if (!playable)
        {
            _heading.Text = "Steam files needed";
            _launchDetail.Text = "Build the original Versus maps and assets from Steam. Find additional maps under Mods.";
        }
        else
        {
            _heading.Text = _pendingWebJoin is null ? "Ready to play" : "Ready to join";
            _launchDetail.Text = checkedOnline ? "You're up to date."
                : "Your installed game is ready. Updates are checked before launch.";
        }
    }

    private void SetOperation(string title)
    {
        _operationTitle = title;
        if (_work is not null) _heading.Text = VisibleOperationTitle;
    }

    private bool GodotUpdateAvailable(SemVersion? current) => current is not null && _store.Pending() is null
        && _lastCheck is { GameUpdateAvailable: true, Latest: { } latest } && latest.Version > current
        && GodotSteamContent.SupportsClient(latest.Version);

    private void RefreshMainButton(SemVersion? current, bool busy)
    {
        bool update = GodotUpdateAvailable(current);
        bool compatible = GodotSteamContent.SupportsClient(current);
        bool playable = compatible && _steamContentRoot is not null;
        (NextAction next, string text, bool enabled) = busy
            ? (NextAction.Cancel, "Cancel", true)
            : current is not null && !compatible
                ? _store.Pending() is not null ? (NextAction.Check, "Waiting for game to close", false)
                    : update ? (NextAction.Update, "Update", true) : (NextAction.Check, "Check for updates", true)
            : _steamContentRoot is null && _steamSource.Value is null
                ? (NextAction.ChooseSource, "Choose Steam folder", true)
            : current is null
                ? (NextAction.Install, "Install", true)
                : update
                    ? (NextAction.Update, "Update", true)
                    : !playable
                        ? (NextAction.BuildFiles, "Build game files", true)
                        : (NextAction.Play, "PLAY", true);
        if (!busy && _pendingWebJoin is not null && current is not null)
        {
            if (current < WebJoinRequest.MinimumGameVersion && next == NextAction.Play)
                (next, text, enabled) = (NextAction.Check, "Check for updates", true);
            else if (next == NextAction.Play) text = "JOIN SESSION";
        }
        _next = next;
        _playCurrent.Visible = !busy && update && playable
            && (_pendingWebJoin is null || current! >= WebJoinRequest.MinimumGameVersion);
        _playCurrent.Enabled = !busy;
        _playCurrent.Text = _pendingWebJoin is null ? "Play current version" : "Join with current version";
        _check.Visible = true;
        Theme.SetButtonEmphasis(_main, !busy, Palette);
        _tips.SetToolTip(_main, busy && _operationCanCancel ? "Cancel this operation." : null);
        _main.Text = text;
        _main.Enabled = enabled && (!busy || _operationCanCancel);
        if (busy && !_operationCanCancel) _main.Text = "Please wait";
    }

    private async Task DoNextAsync()
    {
        if (IsOriginalEdition)
        {
            if (_work is not null) { if (_operationCanCancel) _work.Cancel(); }
            else if (_selected == Product.Ps2) Ps2Action();
            else RetailAction();
            return;
        }
        if (_selected == Product.Editor) { await DoEditorActionAsync(); return; }
        if (_selected == Product.Unreal) { await DoUnrealActionAsync(); return; }
        switch (_next)
        {
            case NextAction.Cancel:
                if (_operationCanCancel) _work?.Cancel();
                break;
            case NextAction.Check:
                await RunAsync(CheckAsync);
                break;
            case NextAction.Install:
                await RunAsync(async cancel =>
                {
                    if (_lastCheck is not { Latest: not null, GameUpdateAvailable: true }) await CheckAsync(cancel);
                    await UpdateGameAsync(cancel);
                });
                break;
            case NextAction.Update:
                await RunAsync(UpdateGameAsync);
                break;
            case NextAction.ChooseSource:
                ChooseSteamSource();
                break;
            case NextAction.BuildFiles:
                await RunAsync(BuildSteamContentAsync);
                break;
            case NextAction.Play:
                await PlayOrJoinAsync();
                break;
        }
    }

    private async Task PlayOrJoinAsync()
    {
        if (_pendingWebJoin is not null) await TryWebJoinAsync();
        else await RunAsync(cancel => PlayWithUpdateCheckAsync(cancel));
    }

    private async Task OpenChangelogAsync()
    {
        // The website changelog covers the game and editor; the Unreal build's notes live on its releases page.
        string url = _selected switch
        {
            Product.Editor => "https://master.lumbridge.co.uk/changelog#editor-latest",
            Product.Unreal => _unreal.ReleasesPage,
            _ => "https://master.lumbridge.co.uk/changelog#game-latest",
        };
        _whatsNew.Enabled = false;
        try
        {
            // Opening the browser alone cannot detect an offline connection.
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            using HttpResponseMessage response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (IsDisposed || Disposing) return;
            if (response.IsSuccessStatusCode)
            {
                using Process? browser = Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                return;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or Win32Exception or InvalidOperationException)
        {
            // Keep release notes available offline or if the browser cannot open.
        }
        finally
        {
            if (!IsDisposed && !Disposing) _whatsNew.Enabled = true;
        }

        if (!IsDisposed && !Disposing) ShowWhatsNew();
    }

    private void ShowWhatsNew()
    {
        using var dialog = new Form
        {
            Text = _selected switch
            {
                Product.Editor => "OpenSCCT Editor — What’s new",
                Product.Unreal => "OpenSCCT Unreal — What’s new",
                _ => _notesTitle,
            },
            Icon = Icon,
            StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(560, 420),
            BackColor = Theme.Background,
            ForeColor = Theme.Text,
            Font = Theme.Body,
            AutoScaleMode = AutoScaleMode.Dpi,
            AutoScaleDimensions = new SizeF(96f, 96f),
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            Padding = new Padding(16),
        };
        // The notes are GitHub markdown: shown formatted (headings, bullets, bold, code, links that open the browser).
        var frame = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Panel, Padding = new Padding(10, 8, 4, 8), BorderStyle = BorderStyle.FixedSingle };
        var notes = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            BackColor = Theme.Panel,
            ForeColor = Theme.Text,
            Font = Theme.Body,
            ScrollBars = RichTextBoxScrollBars.Vertical,
            DetectUrls = false,
            WordWrap = true,
            TabStop = false,
        };
        string body = _selected switch
        {
            Product.Editor => SelectedRelease?.Body ?? "No editor release notes cached.",
            Product.Unreal => SelectedRelease?.Body ?? "No OpenSCCT Unreal release notes cached.",
            _ => _notesText,
        };
        notes.Rtf = ReleaseNotesRtf.Convert(body, Rgb(Palette.Text), Rgb(Palette.Accent), Rgb(Palette.Muted), Theme.Body.SizeInPoints);
        notes.LinkClicked += (_, e) => OpenLink(e.LinkText);
        frame.Controls.Add(notes);
        dialog.Controls.Add(frame);
        Theme.Apply(dialog, Palette);
        dialog.Shown += (_, _) => notes.Select(0, 0);
        dialog.ShowDialog(this);
    }

    private static int Rgb(Color c) => (c.R << 16) | (c.G << 8) | c.B;

    private void OpenHelp()
    {
        string? installedVersion = null;
        try
        {
            installedVersion = _store.Current()?.ToString();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Help remains available when the installation cannot be read.
        }

        Assembly assembly = Assembly.GetExecutingAssembly();
        var context = new ReportContext
        {
            Source = ReportSource.Launcher,
            Version = _launcherVersion?.ToString(),
            LauncherVersion = _launcherVersion?.ToString(),
            InstalledGameVersion = installedVersion ?? "not installed",
            Build = assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(a => a.Key == "BuildNumber")?.Value,
            Commit = _launcherVersion?.Build,
            Platform = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
        };
        using var dialog = new HelpForm(context, palette: Palette, editorVersion: _selected == Product.Editor ? _editor.Installed()?.Version.ToString() ?? "not installed" : null);
        dialog.ShowDialog(this);
    }

    /// <summary>Opens a website or release notes link in the browser (http and https only).</summary>
    private void OpenLink(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true })?.Dispose();
        }
        catch (Win32Exception e)
        {
            SetStatus("Could not open the link: " + e.Message, Theme.Warning);
        }
    }

    /// <summary>The Maps window offers additional downloads for the selected product.</summary>
    private void OpenMaps()
    {
        if (_work is not null)
        {
            return;
        }

        using var dialog = new MapsForm(Http, _layout.Packs, Palette, () => EditorUsesSharedContent() || _unreal.IsRunning || _games.Running().Count > 0);
        dialog.ShowDialog(this);
        RefreshLocal();
    }

    // ---- the work ----------------------------------------------------------------------------------------------

    private void ShowReleaseCheck(UpdateCheck check)
    {
        _lastCheck = check;

        if (check.Latest is { } latest)
        {
            ReleaseInfo r = latest.Release;
            string when = r.PublishedAt?.ToLocalTime().ToString("d", CultureInfo.CurrentCulture) ?? string.Empty;
            _notesTitle = $"What's new: {r.Name ?? r.TagName}  {when}";
            _notesText = (string.IsNullOrWhiteSpace(r.Body) ? "(No notes for this release.)" : r.Body).ReplaceLineEndings("\r\n");
        }
        else
        {
            _notesTitle = "What's new";
            _notesText = check.Feed.Source == ReleaseFeedSource.None
                ? "No release information yet."
                : "No release was found.";
        }

        RefreshReleasePreview();

        if (check.LauncherUpdateAvailable && check.LatestLauncher is { } launcher && _swap is not { HasStaged: true })
        {
            bool writable = _swap?.CanWriteFolder() == true;
            _launcherInfo.Text = writable
                ? $"Launcher {launcher} is available. You can update it when you are ready."
                : $"Launcher {launcher} is available. Download it from the OpenSCCT releases page (this folder is read only).";
            _launcherButton.Text = "Update launcher";
            _launcherButton.Visible = writable;
            _launcherBanner.Visible = true;
        }
    }

    private async Task CheckAsync(CancellationToken cancel)
    {
        SetOperation("Checking for updates");

        SetStatus("Looking for updates...", Theme.Muted);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        UpdateCheck check = await _service.CheckAsync(_settings.IncludePrereleases, _launcherVersion, timeout.Token);
        ShowReleaseCheck(check);

        // The launcher fetches its own update at once (verified, staged as .new); the swap waits for "Restart to
        // finish" so a running download is never cut off. A newer launcher can read newer release formats.
        if (check.LauncherUpdateAvailable && _swap is { HasStaged: false } swap && swap.CanWriteFolder())
        {
            try
            {
                await UpdateLauncherAsync(cancel);
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or UpdateRejectedException)
            {
                SetStatus("The launcher could not update itself: " + ex.Message, Theme.Warning);
            }

            return;
        }

        if (check.Feed.Problem is { } problem)
        {
            SetStatus(problem, Theme.Warning);
        }
        else
        {
            SetStatus(check.GameUpdateAvailable && _store.Current() is not null
                ? "An update is available."
                : string.Empty, Theme.Muted);
        }
    }

    private async Task UpdateGameAsync(CancellationToken cancel)
    {
        if (_lastCheck?.Latest is { } candidate && !GodotSteamContent.SupportsClient(candidate.Version))
            throw new InvalidOperationException("A newer Godot client is required to build game files from Steam. Check for updates again.");
        await UpdateGameBinaryAsync(cancel);
        if (GodotSteamContent.SupportsClient(_store.Current()) && _steamContent.ActiveRoot() is null && _steamSource.Value is not null)
            await BuildSteamContentAsync(cancel);
    }

    private async Task UpdateGameBinaryAsync(CancellationToken cancel)
    {
        SemVersion? current = _store.Current();
        if (_lastCheck is { GameUpdateAvailable: true, Latest: { } latest } && _store.Pending() is null
            && (current is null || latest.Version > current))
        {
            SetOperation("Downloading OpenSCCT");
            SetStatus($"Downloading OpenSCCT {latest.Version}...", Theme.Muted);
            InstallOutcome outcome = await _service.InstallAsync(latest, ProgressReporter(), cancel);
            SetStatus(outcome switch
            {
                InstallOutcome.Activated => $"OpenSCCT {latest.Version} is installed.",
                InstallOutcome.WaitingForGameToClose => $"OpenSCCT {latest.Version} is ready and starts once you close the game.",
                _ => $"OpenSCCT {latest.Version} is already installed.",
            }, Theme.Accent);
            await CheckAsync(cancel);
        }
    }

    private async Task UpdateLauncherAsync(CancellationToken cancel)
    {
        if (_swap is null)
        {
            return;
        }

        if (_swap.HasStaged)
        {
            RestartIntoNewLauncher();
            return;
        }

        if (_lastCheck?.Latest is not { } latest)
        {
            return;
        }

        if (!_swap.CanWriteFolder())
        {
            SetStatus("The launcher's folder is read only. Move OpenSCCT.Launcher.exe to a folder you own to let it update itself.", Theme.Warning);
            return;
        }

        SemVersion? launcher = ReleasePicker.LauncherVersion(latest);
        SetOperation("Updating launcher");
        SetStatus($"Downloading launcher {launcher}...", Theme.Muted);
        await _service.StageLauncherAsync(latest, _swap, ProgressReporter(), cancel);
        _launcherInfo.Text = $"Launcher {launcher} is ready. Restart the launcher to use it.";
        _launcherButton.Text = "Restart to finish";
        _launcherButton.Visible = true;
        _launcherBanner.Visible = true;
        SetStatus(string.Empty, Theme.Muted);
    }

    private void RestartIntoNewLauncher()
    {
        if (_swap is null)
        {
            return;
        }

        try
        {
            string started = _swap.Swap();
            var start = new ProcessStartInfo(started) { UseShellExecute = false };
            start.ArgumentList.Add(Program.AfterUpdateArg);
            if (_pendingWebJoin is not null) start.ArgumentList.Add(WebJoinRequest.UriPrefix + _pendingWebJoin);
            Process.Start(start);
            Close();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception)
        {
            SetStatus("Could not replace the launcher: " + ex.Message, Theme.Error);
        }
    }

    /// <summary>Returns whether an update completed, so a waiting website Join can resume afterwards.</summary>
    private async Task<bool> PlayWithUpdateCheckAsync(CancellationToken cancel, string? joinServerId = null)
    {
        bool updated = false;
        bool play = await PlayUpdateFlow.RunAsync(async checkCancel =>
        {
            SetOperation("Checking for updates");
            SetStatus("Checking for updates before starting...", Theme.Muted);
            UpdateCheck check = await _service.CheckAsync(_settings.IncludePrereleases, _launcherVersion, checkCancel);
            ShowReleaseCheck(check);
            return check;
        }, check =>
        {
            if (IsDisposed || (joinServerId is not null && _pendingWebJoin != joinServerId)) return PlayUpdateChoice.Cancel;
            return ChoosePlayUpdate(check);
        }, async updateCancel =>
        {
            await UpdateGameAsync(updateCancel);
            updated = true;
        }, cancel);

        if (!play || IsDisposed || (joinServerId is not null && _pendingWebJoin != joinServerId)) return updated;
        if (Play(joinServerId) && joinServerId is not null && _pendingWebJoin == joinServerId) _pendingWebJoin = null;
        return updated;
    }

    private PlayUpdateChoice ChoosePlayUpdate(UpdateCheck check)
    {
        var update = new TaskDialogButton("Update now");
        var play = new TaskDialogButton("Play current version");
        var page = new TaskDialogPage
        {
            Caption = "OpenSCCT update available",
            Heading = $"OpenSCCT {check.Latest!.Version} is available",
            Text = $"You have version {check.Installed}. Update now, or keep playing your installed version and update later.",
            Icon = TaskDialogIcon.Information,
            AllowCancel = true,
            DefaultButton = update,
            Buttons = { update, play, TaskDialogButton.Cancel },
        };
        TaskDialogButton choice = TaskDialog.ShowDialog(this, page);
        if (choice == update) return PlayUpdateChoice.Update;
        if (choice == play) return PlayUpdateChoice.PlayCurrent;
        SetStatus("Launch cancelled. You can update or play later.", Theme.Muted);
        return PlayUpdateChoice.Cancel;
    }

    private bool Play(string? joinServerId = null)
    {
        SemVersion? current = _store.Current();
        if (current is null)
        {
            return false;
        }

        if (!GodotSteamContent.SupportsClient(current))
        {
            SetStatus("Update the Godot client to play with your Steam game files.", Theme.Warning);
            return false;
        }
        string? contentRoot = _steamContent.ActiveRoot();
        if (contentRoot is null)
        {
            SetStatus("Build the Godot game files from your Steam installation first.", Theme.Warning);
            return false;
        }

        string exe = _store.ExePath(current);
        try
        {
            using Process? process = Process.Start(GameStart.Create(exe,
                extraUserArgs: joinServerId is null ? null : [WebJoinRequest.GameArgumentPrefix + joinServerId],
                graphicsDriver: GameRendererSettings.Load(LauncherLayout.GameUserData), contentRoot: contentRoot));
            if (process is null) throw new IOException("The game process did not start.");
            SetStatus(joinServerId is null ? $"Started OpenSCCT {current}." : "Started OpenSCCT with your Join request. Continue in the game.", Theme.Accent);
            WindowState = FormWindowState.Minimized;
            return true;
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException)
        {
            SetStatus("Could not start the game: " + ex.Message, Theme.Error);
            return false;
        }
    }

    private void OpenSettings()
    {
        string renderer = GameRendererSettings.Vulkan;
        int monitor = 1;
        string? enhancedImport = null;
        try
        {
            if (_selected == Product.Game)
            {
                renderer =GameRendererSettings.Load(LauncherLayout.GameUserData);
                monitor = GameMonitorSettings.Load(LauncherLayout.GameUserData);
                enhancedImport = EnhancedImportRequest.Pending(LauncherLayout.GameUserData);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetStatus("Could not read the game renderer: " + ex.Message, Theme.Error);
            return;
        }

        string? unrealRepository = _settings.UnrealRepository;
        using var dialog = new SettingsForm(_settings, _layout, Palette, renderer, _games.Running().Count > 0, monitor, enhancedImport);
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            if (dialog.CanChangeRenderer)
            {
                // The game keeps settings in memory and could overwrite a change made while it is running.
                if (_games.Running().Count == 0)
                    GameRendererSettings.Save(LauncherLayout.GameUserData, dialog.GraphicsDriver);
                else if (dialog.GraphicsDriver != renderer)
                {
                    SetStatus("Close OpenSCCT before changing its renderer.", Theme.Warning);
                    return;
                }
            }
            if (dialog.MonitorChanged)
            {
                // As with the renderer: a running game would write its own value back over this one.
                if (_games.Running().Count > 0)
                {
                    SetStatus("Close OpenSCCT before changing its monitor.", Theme.Warning);
                    return;
                }

                GameMonitorSettings.Save(LauncherLayout.GameUserData, dialog.Monitor);
            }

            // The game reads the request when it starts, so a running game takes it at its next start.
            if (_selected == Product.Game && dialog.EnhancedImport != enhancedImport)
                EnhancedImportRequest.Save(LauncherLayout.GameUserData, dialog.EnhancedImport);
            dialog.Result.Save(_layout.Settings);
            _settings = dialog.Result;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetStatus("Could not save settings: " + ex.Message, Theme.Error);
            return;
        }
        switch (_selected)
        {
            case Product.Editor:
                _editorCheck = null;
                break;
            case Product.Unreal:
                _unrealCheck = null;
                break;
            default:
                _service = CreateService();
                _lastCheck = null;
                break;
        }
        // The Unreal release repository has no control in the dialog, but a changed value needs a new service.
        if (!string.Equals(unrealRepository, _settings.UnrealRepository, StringComparison.Ordinal))
        {
            _unreal = new UnrealService(_layout, Http, _settings.UnrealRepository);
            _unrealCheck = null;
        }
        if (dialog.UninstallEditorRequested)
        {
            _ = RunAsync(UninstallEditorAsync);
            return;
        }
        if (dialog.UninstallUnrealRequested)
        {
            RefreshLocal();
            _ = UninstallUnrealAsync();
            return;
        }
        if (dialog.RemoveGameFilesRequested)
        {
            RemoveGameFiles();
            RefreshLocal();
            return;
        }

        RefreshLocal();

        _ = RunAsync(CheckSelectedAsync);
    }

    private async Task ForceCloseAndRemoveAsync()
    {
        if (_work is not null || !VisibleStatus.CanForceClose) return;
        if (MessageBox.Show(this,
            "Force close all running OpenSCCT games (OpenSCCT Unreal included), the installed editor and its command-line tools, then retry removing the game and shared files?\n\nUnsaved work will be lost.",
            "Force close OpenSCCT", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.OK)
            return;

        await RunAsync(async cancel =>
        {
            _operationCanCancel = false;
            SetOperation("Closing OpenSCCT processes");
            RefreshLocal();
            try
            {
                await InstallReset.ForceCloseProcessesAsync(_layout);
                // Unreal can hold the Steam generation open while it runs.
                await _unreal.ForceCloseAsync(cancel);
                RemoveGameFiles(forceCloseAttempted: true);
            }
            catch (Win32Exception ex)
            {
                SetStatus("Could not check running OpenSCCT processes: " + ex.Message, Theme.Warning, canForceClose: true);
            }
        });
    }

    /// <summary>
    /// Deletes what the launcher installed once the game and editor release their files. The OpenSCCT Unreal
    /// install and its map packs are kept (they have their own uninstall on the Unreal tab).
    /// </summary>
    private void RemoveGameFiles(bool forceCloseAttempted = false)
    {
        // Unreal and the editor both read these generated files.
        if (_editor.IsRunning || _games.Running().Count > 0 || _unreal.IsRunning)
        {
            string warning = _editor.UpdateInProgress
                ? "Wait for the editor update to finish before removing game and shared files."
                : forceCloseAttempted
                    ? "Some OpenSCCT processes are still running. Close them before removing game and shared files."
                    : "Close OpenSCCT, OpenSCCT Unreal, the editor and its command-line tools before removing game and shared files.";
            SetStatus(warning, Theme.Warning, canForceClose: true);
            return;
        }

        IReadOnlyList<string> failed = InstallReset.Run(_layout, Program.ExePath);
        SetStatus(
            failed.Count == 0
                ? "Game and shared files removed. Choose Install to set up Godot again."
                : "Some game files are in use and were kept: " + string.Join(", ", failed),
            failed.Count == 0 ? Theme.Accent : Theme.Warning);
    }

    private void OnPendingTick()
    {
        if (_work is not null)
        {
            return;
        }

        try
        {
            if (_service.TryFinishPending() == InstallOutcome.Activated)
            {
                _gameStatus = new($"Now on version {_store.Current()}.", StatusKind.Success);
                RefreshLocal();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _gameStatus = new("Could not switch versions yet: " + ex.Message, StatusKind.Warning);
        }
        FinishUnrealSwitch();
        RefreshLocal();
    }

    private Progress<DownloadProgress> ProgressReporter()
    {
        _progress.Fraction = 0;
        _progress.Visible = true;
        _progressText.Visible = true;
        return new Progress<DownloadProgress>(p =>
        {
            _progress.Visible = p.Total > 0;
            _progress.Fraction = p.Total > 0 ? (double)p.Received / p.Total : 0;
            string rate = p.BytesPerSecond > 0 ? $"  {Mb(p.BytesPerSecond)}/s" : string.Empty;
            _progressText.Text = p.Total > 0 ? $"{Mb(p.Received)} of {Mb(p.Total)}{rate}" : Mb(p.Received) + rate;
        });
    }

    private static string Mb(double bytes) => (bytes / (1024 * 1024)).ToString("0.0", CultureInfo.CurrentCulture) + " MB";

    private async Task RunAsync(Func<CancellationToken, Task> work)
    {
        if (_work is not null)
        {
            return;
        }

        _operationTitle = "Getting ready";
        _operationProduct = _selected;
        Product started = _selected;
        _operationCanCancel = true;
        _operationStatus = new(string.Empty, StatusKind.Normal);
        _work = new CancellationTokenSource();
        bool completed = false;
        RefreshLocal();
        try
        {
            await work(_work.Token);
            completed = true;
        }
        catch (OperationCanceledException)
        {
            SetStatus("Stopped. Press the button to carry on.", Theme.Muted);
        }
        catch (UpdateRejectedException ex)
        {
            SetStatus(ex.Message, Theme.Error);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException or Win32Exception)
        {
            SetStatus(ex.Message + " Press the button to try again.", Theme.Error);
        }
        finally
        {
            _work.Dispose();
            _work = null;
            _progress.Visible = false;
            _progressText.Text = string.Empty;
            _progressText.Visible = false;
            if (!IsDisposed)
            {
                RefreshLocal();
            }
        }
        if (_closeAfterWork && !IsDisposed) { Close(); return; }
        if (completed && !IsDisposed && _webJoinReady && _pendingWebJoin is not null) await TryWebJoinAsync();
        if (_webJoinReady) await CheckSwitchedTabAsync(started);
    }

    /// <summary>
    /// SelectProduct skips its check while other work runs: once that work ends, checks the tab the player switched to
    /// during it, when that tab checks on start and has not been checked yet.
    /// </summary>
    private async Task CheckSwitchedTabAsync(Product started)
    {
        if (IsDisposed || Disposing || _closeAfterWork || _work is not null || _selected == started) return;
        if (SelectedCheckOnStart && !HasChecked(_selected)) await RunAsync(CheckSelectedAsync);
    }

    private void SetStatus(string text, Color color, string? path = null, bool canForceClose = false)
    {
        StatusKind kind = color == Theme.Warning ? StatusKind.Warning : color == Theme.Error ? StatusKind.Error
            : color == Theme.Accent || color == Palette.Accent ? StatusKind.Success : StatusKind.Normal;
        var state = new ProductStatus(text, kind, path, canForceClose);
        switch (_work is not null ? _operationProduct : _selected)
        {
            case Product.Ps2: _ps2ActionStatus = state; break;
            case Product.Retail: _retailActionStatus = state; break;
            case Product.Editor: _editorStatus = state; break;
            case Product.Unreal: _unrealStatus = state; break;
            default: _gameStatus = state; break;
        }
        if (_work is not null) _operationStatus = state;
        RenderProductStatus();
        _launchDetail.Visible = _work is null && kind is not (StatusKind.Warning or StatusKind.Error);
    }

    private static void ShowInExplorer(string? path)
    {
        if (path is null)
        {
            return;
        }

        string folder = Directory.Exists(path) ? path : Path.GetDirectoryName(path)!;
        string args = File.Exists(path) ? "/select,\"" + path + "\"" : "\"" + folder + "\"";
        if (File.Exists(path) || Directory.Exists(folder))
        {
            using Process? explorer = Process.Start(new ProcessStartInfo("explorer.exe", args) { UseShellExecute = false });
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _pendingTimer.Dispose();
            _tips.Dispose();
            if (_brandLogo is { } logo)
            {
                Image? image = logo.Image;
                logo.Image = null;
                image?.Dispose();
            }
            _heroImage?.Dispose();
            _work?.Dispose();
        }

        base.Dispose(disposing);
    }
}
