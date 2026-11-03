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
using OpenScct.Launcher.Update;
using OpenScct.Launcher.Versioning;
using OpenScct.Project;

namespace OpenScct.Launcher.App;

/// <summary>
/// The launcher window: download OpenSCCT, then its game files. One big button does the next thing and becomes PLAY
/// when everything is ready. Less common actions are in the row below it.
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
        DownloadFiles,
        Play,
        Cancel,
    }

    private readonly LauncherLayout _layout = new(LauncherLayout.DefaultRoot);
    private readonly VersionStore _store;
    private readonly ContentStore _packs;
    private readonly IGameProcessProbe _games = new GameProcessProbe();
    private readonly LauncherSwap? _swap;
    private readonly SemVersion? _launcherVersion;
    private LauncherSettings _settings;
    private UpdateService _service;
    private UpdateCheck? _lastCheck;
    private CancellationTokenSource? _work;
    private bool _downloadingContent;
    private ContentIndex? _packIndex;
    private NextAction _next;
    private string _notesTitle = "What's new";
    private string _notesText = "Checking for releases...";
    private Control? _root;
    private bool _fitPending;
    private bool _fitting;

    private readonly StepCard _installStep;
    private readonly StepCard _filesStep;
    private readonly GreenProgressBar _progress = new();
    private readonly Label _progressText = Theme.Label(string.Empty, Theme.Small, Theme.Muted);
    private readonly Button _main = Theme.Button("PLAY", primary: true);
    private readonly Button _playCurrent = Theme.Button("Play current version");
    private readonly LinkLabel _status = new()
    {
        AutoSize = true, Font = Theme.Body, ForeColor = Theme.Muted, BackColor = Color.Transparent,
        LinkColor = Theme.Accent, ActiveLinkColor = Theme.Text, VisitedLinkColor = Theme.Accent,
    };
    private readonly Button _whatsNew = Theme.Button("What's new");
    private readonly Button _check = Theme.Button("Check for updates");
    private readonly Button _website = Theme.Button("Website");
    private readonly Button _help = Theme.Button("&Help");
    private readonly Button _mapsButton = Theme.Button("Maps");
    private readonly Button _settingsButton = Theme.Button("Settings");
    private readonly TableLayoutPanel _launcherBanner = Theme.Grid(Theme.Fill, Theme.Auto);
    private readonly Label _launcherInfo = Theme.Label(string.Empty, Theme.Body, Theme.Warning);
    private readonly Button _launcherButton = Theme.Button("Restart to finish");
    private readonly System.Windows.Forms.Timer _pendingTimer = new() { Interval = 3000 };

    public MainForm(string? joinServerId = null, string? protocolProblem = null)
    {
        _pendingWebJoin = joinServerId;
        _protocolProblem = protocolProblem;
        _store = new VersionStore(_layout);
        _packs = new ContentStore(_layout.Packs, Http);
        _settings = LauncherSettings.Load(_layout.Settings);
        _service = CreateService();
        if (Program.ExePath is { } exe)
        {
            _swap = new LauncherSwap(exe);
        }

        string? info = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        _launcherVersion = SemVersion.TryParse(info, out SemVersion? v) ? v : null;

        _installStep = new StepCard(1, "Download OpenSCCT");
        _filesStep = new StepCard(2, "Download game files");

        BuildUi();
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
        ClientSize = new Size(720, 520);
        MinimumSize = new Size(600, 480);
        FormBorderStyle = FormBorderStyle.Sizable;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Font = Theme.Body;

        // Laid out in tables that size to their text, so nothing is cut off at any display scaling (the window can
        // also be made larger).
        Padding = new Padding(24, 16, 24, 12);
        var logo = new PictureBox
        {
            Image = Theme.LoadIcon(64).ToBitmap(),
            SizeMode = PictureBoxSizeMode.Zoom,
            Size = new Size(56, 56),
            Margin = new Padding(0, 0, 12, 0),
            BackColor = Color.Transparent,
        };
        var title = new Wordmark { Margin = new Padding(3, 0, 3, 0) };
        Label subtitle = Theme.Label("Splinter Cell Chaos Theory Versus, rebuilt", Theme.Small, Theme.Muted);
        Label version = Theme.Label("Launcher " + (_launcherVersion?.ToString() ?? "dev"), Theme.Small, Theme.Muted);
        version.Anchor = AnchorStyles.Right | AnchorStyles.Top;
        version.Margin = new Padding(12, 0, 0, 0);
        TableLayoutPanel header = Theme.Grid(Theme.Auto, Theme.Fill, Theme.Auto);
        Theme.AddRow(header, logo, Theme.Flow(true, title, subtitle), version);
        header.Margin = new Padding(0, 0, 0, 4);

        // A launcher update waiting for a restart: a banner above the steps.
        _launcherBanner.BackColor = Theme.Panel;
        _launcherBanner.Padding = new Padding(14, 8, 14, 8);
        _launcherBanner.Margin = new Padding(0, 8, 0, 0);
        _launcherBanner.Visible = false;
        _launcherButton.Anchor = AnchorStyles.Right;
        Theme.AddRow(_launcherBanner, Theme.Wrapping(_launcherInfo), _launcherButton);

        _progress.Visible = false;
        _progress.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _progress.Margin = new Padding(0, 14, 0, 2);
        Theme.Wrapping(_progressText);

        // The big button on the right, with a smaller option to defer an update and play.
        _main.Anchor = AnchorStyles.Right | AnchorStyles.Top;
        _playCurrent.Anchor = AnchorStyles.Right;
        _playCurrent.Margin = new Padding(0, 0, 12, 0);
        _playCurrent.Visible = false;
        Theme.Wrapping(_status);
        _status.Margin = new Padding(0, 8, 16, 0);
        _status.LinkClicked += (_, e) => ShowInExplorer(e.Link?.LinkData as string);
        TableLayoutPanel actions = Theme.Grid(Theme.Fill, Theme.Auto, Theme.Auto);
        actions.Margin = new Padding(0, 14, 0, 0);
        Theme.AddRow(actions, _status, _playCurrent, _main);

        // The less common actions, small, along the bottom edge.
        FlowLayoutPanel more = Theme.Flow(false, _whatsNew, _check, _website, _mapsButton, _settingsButton, _help);
        more.Dock = DockStyle.Bottom;
        more.Padding = new Padding(0, 12, 0, 0);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, BackColor = Color.Transparent };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        foreach (Control row in new Control[] { header, _launcherBanner, _installStep, _filesStep, _progress, _progressText, actions })
        {
            int r = root.RowCount++;
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.Controls.Add(row, 0, r);
        }

        Controls.Add(root);
        Controls.Add(more);
        _root = root;

        // Contents grow after the first fit (the launcher banner, an update line, a long status): grow the window
        // again once that layout has settled, so the big button is never cut off.
        // FitToContent lays the root out itself: the layouts it causes are ignored, or each fit would queue the next.
        root.Layout += (_, _) =>
        {
            if (_fitPending || _fitting || !IsHandleCreated)
            {
                return;
            }

            _fitPending = true;
            BeginInvoke(() =>
            {
                _fitting = true;
                try
                {
                    FitFooterWidth();
                    Theme.FitToContent(this, root);
                }
                finally
                {
                    _fitting = false;
                    _fitPending = false;
                }
            });
        };

        var tips = new ToolTip();
        tips.SetToolTip(_website, "Visit the OpenSCCT website in your browser.");
        tips.SetToolTip(_help, "Get help, report a problem or contribute to OpenSCCT.");
        tips.SetToolTip(_mapsButton, "Download new maps made by the community.");

        _main.Click += async (_, _) => await DoNextAsync();
        _playCurrent.Click += async (_, _) => await PlayOrJoinAsync();
        _whatsNew.Click += async (_, _) => await OpenChangelogAsync();
        _check.Click += async (_, _) => await RunAsync(CheckAsync);
        _website.Click += (_, _) => OpenLink("https://master.lumbridge.co.uk/");
        _help.Click += (_, _) => OpenHelp();
        _settingsButton.Click += (_, _) => OpenSettings();
        _mapsButton.Click += (_, _) => OpenMaps();
        _launcherButton.Click += async (_, _) => await RunAsync(UpdateLauncherAsync);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        FitFooterWidth();
        if (_root is not null)
        {
            Theme.FitToContent(this, _root);
        }
    }

    private void FitFooterWidth()
    {
        // Measure after DPI scaling, including Check for updates when temporarily hidden. Keep every action on
        // one row even at the minimum window width; the version lives in the header so it cannot crowd them out.
        int width = Padding.Horizontal + Width - ClientSize.Width;
        foreach (Button button in new[] { _whatsNew, _check, _website, _mapsButton, _settingsButton, _help })
        {
            width += button.GetPreferredSize(Size.Empty).Width + button.Margin.Horizontal;
        }

        if (width > MinimumSize.Width)
        {
            MinimumSize = new Size(width, MinimumSize.Height);
        }
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _service.TryFinishPending();
        RefreshLocal();
        _pendingTimer.Start();
        if (_settings.CheckOnStart)
        {
            await RunAsync(CheckAsync);
        }
        else
        {
            _notesText = "Update checks on start are off. Use Check for updates.";
        }
        _webJoinReady = true;
        if (_pendingWebJoin is not null) await TryWebJoinAsync();
        else if (_protocolProblem is not null) SetStatus(_protocolProblem, Theme.Warning);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _work?.Cancel();
        _pendingTimer.Stop();
        base.OnFormClosing(e);
    }

    // ---- the steps ---------------------------------------------------------------------------------------------

    private void RefreshLocal()
    {
        SemVersion? current = _store.Current();
        bool busy = _work is not null;

        RefreshInstallStep(current);
        RefreshFilesStep(current);

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

        RefreshMainButton(current, busy);
    }

    private void RefreshInstallStep(SemVersion? current)
    {
        SemVersion? pending = _store.Pending();
        VersionedRelease? latest = _lastCheck?.Latest;
        bool update = latest is not null && current is not null && latest.Version > current
            && _lastCheck is { GameUpdateAvailable: true };
        if (pending is not null)
        {
            _installStep.Set(StepState.Done, $"Version {pending} is ready and starts the next time you play.");
        }
        else if (current is not null && update && latest is not null)
        {
            _installStep.Set(StepState.Choice, $"Version {current} is installed. Version {latest.Version} is available. Press Update, or choose Play current version to update later.");
        }
        else if (current is not null)
        {
            bool checkedOnline = _lastCheck is { Feed.Source: ReleaseFeedSource.Network or ReleaseFeedSource.NotModified };
            _installStep.Set(StepState.Done, checkedOnline
                ? $"Version {current} is installed and up to date."
                : $"Version {current} is installed. PLAY checks for updates when online.");
        }
        else if (_lastCheck is null)
        {
            _installStep.Set(StepState.Todo, _work is not null
                ? "Looking for the latest version..."
                : "Press Check for updates to find the latest version.");
        }
        else if (latest is not null)
        {
            _installStep.Set(StepState.Todo, $"Not installed yet. Version {latest.Version} is ready to download.");
        }
        else
        {
            _installStep.Set(StepState.Problem, "No download found yet. Check your internet connection, then press Check again.");
        }
    }

    /// <summary>Step 2: the game files downloaded from OpenSCCT-Content.</summary>
    private void RefreshFilesStep(SemVersion? current)
    {
        if (_downloadingContent)
        {
            _filesStep.Set(StepState.Todo, "Downloading the maps, characters and sounds...");
        }
        else if (current is null)
        {
            _filesStep.Set(StepState.Waiting, "Downloads automatically after OpenSCCT is installed.");
        }
        else if (!PackedContent.Installed(_packs))
        {
            string size = _packIndex is { } index
                ? " (about " + Gb(PackedContent.DownloadSize(PackedContent.SharedToFetch(index, _packs), PackedContent.MapsToFetch(index, _packs, firstDownload: true))) + ")"
                : string.Empty;
            _filesStep.Set(StepState.Todo, $"Downloads the maps, characters and sounds{size}. You can stop and carry on later.");
        }
        else if (!PacksCurrent())
        {
            _filesStep.Set(StepState.Choice, "Newer game files are available. Press Update, or choose Play current version to use your installed files.");
        }
        else
        {
            int maps = _packs.Installed().Count(p => !p.IsShared);
            _filesStep.Set(StepState.Done, $"Ready: {maps} maps downloaded. Get more from Maps.");
        }
    }

    /// <summary>The downloaded game files are installed and, when the index is known, none has a newer copy.</summary>
    private bool PacksCurrent() =>
        PackedContent.Installed(_packs)
        && (_packIndex is not { } index
            || (PackedContent.SharedToFetch(index, _packs).Count == 0 && PackedContent.MapsToFetch(index, _packs, firstDownload: false).Count == 0));

    private bool PackUpdateAvailable => PackedContent.Installed(_packs) && !PacksCurrent();

    /// <summary>The game is installed and its downloaded files have no known updates.</summary>
    private bool FilesReady(SemVersion? current) => current is not null && PacksCurrent();

    /// <summary>Installed downloaded files remain playable when newer packs are offered.</summary>
    private bool DownloadedFilesPlayable(SemVersion? current) => current is not null && PackedContent.Installed(_packs);

    /// <summary>The big button: the next thing to do, or PLAY.</summary>
    private void RefreshMainButton(SemVersion? current, bool busy)
    {
        bool update = current is not null && (PackUpdateAvailable || (_store.Pending() is null
            && _lastCheck is { GameUpdateAvailable: true, Latest: { } latest } && latest.Version > current));
        bool playable = DownloadedFilesPlayable(current);
        (NextAction next, string text, bool enabled) = busy
            ? (NextAction.Cancel, _downloadingContent ? "Stop" : "Cancel", true)
            : current is null
                ? _lastCheck is { Latest: not null, GameUpdateAvailable: true }
                    ? (NextAction.Install, "Install", true)
                    : (NextAction.Check, _lastCheck is null ? "Check for updates" : "Check again", true)
                : update
                    ? (NextAction.Update, "Update", true)
                    : !playable
                        ? (NextAction.DownloadFiles, "Download game files", true)
                        : (NextAction.Play, "PLAY", true);
        if (!busy && _pendingWebJoin is not null && current is not null)
        {
            if (current < WebJoinRequest.MinimumGameVersion && next == NextAction.Play)
                (next, text, enabled) = (NextAction.Check, "Check for updates", true);
            else if (next == NextAction.Play) text = "JOIN SESSION";
        }
        _next = next;
        _playCurrent.Visible = update && playable
            && (_pendingWebJoin is null || current! >= WebJoinRequest.MinimumGameVersion);
        _playCurrent.Enabled = !busy;
        _playCurrent.Text = _pendingWebJoin is null ? "Play current version" : "Join with current version";
        _check.Visible = next != NextAction.Check;
        _main.Text = text;
        _main.Enabled = enabled;
    }

    private async Task DoNextAsync()
    {
        switch (_next)
        {
            case NextAction.Cancel:
                _work?.Cancel();
                break;
            case NextAction.Check:
                await RunAsync(CheckAsync);
                break;
            case NextAction.Install:
            case NextAction.Update:
                await RunAsync(UpdateGameAsync);
                break;
            case NextAction.DownloadFiles:
                await RunAsync(DownloadContentAsync);
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
        const string url = "https://master.lumbridge.co.uk/changelog";
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
            Text = _notesTitle,
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
        notes.Rtf = ReleaseNotesRtf.Convert(_notesText, Rgb(Theme.Text), Rgb(Theme.Accent), Rgb(Theme.Muted), Theme.Body.SizeInPoints);
        notes.LinkClicked += (_, e) => OpenLink(e.LinkText);
        frame.Controls.Add(notes);
        dialog.Controls.Add(frame);
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
        using var dialog = new HelpForm(context);
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

    /// <summary>The Maps window manages downloaded maps that are ready to play immediately.</summary>
    private void OpenMaps()
    {
        if (_work is not null)
        {
            return;
        }

        using var dialog = new MapsForm(Http, _layout.Packs);
        dialog.ShowDialog(this);
        RefreshLocal();
    }

    // ---- the work ----------------------------------------------------------------------------------------------

    /// <summary>
    /// Downloads the game files from OpenSCCT-Content: the shared packs, and the stock
    /// maps the first time (later only newer copies of maps already downloaded). The store resumes a stopped download
    /// and checks every archive and file.
    /// </summary>
    private async Task DownloadContentAsync(CancellationToken cancel)
    {
        _downloadingContent = true;
        RefreshLocal();
        try
        {
            SetStatus("Loading the list of game files...", Theme.Muted);
            ContentIndex index = await _packs.FetchIndexAsync(cancel);
            if (index.Problem is { } problem && index.Shared.Count == 0)
            {
                throw new IOException(problem);
            }

            _packIndex = index;
            bool first = !PackedContent.Installed(_packs);
            IReadOnlyList<ContentSharedPack> shared = PackedContent.SharedToFetch(index, _packs);
            IReadOnlyList<ContentMap> maps = PackedContent.MapsToFetch(index, _packs, first);
            int total = shared.Count + maps.Count;
            int n = 0;
            // Keep the first download incomplete until its stock maps are installed, so a restart continues
            // fetching missing maps instead of treating an interrupted setup as a later shared-pack update.
            foreach (ContentMap map in maps)
            {
                n++;
                await _packs.InstallMapAsync(map, ContentKind.Full, PackReporter($"{map.DisplayTitle} ({n} of {total})"), cancel);
            }

            foreach (ContentSharedPack pack in shared)
            {
                n++;
                string name = pack.Title.Length > 0 ? pack.Title : pack.Id;
                await _packs.InstallSharedAsync(pack, PackReporter($"{name} ({n} of {total})"), cancel);
            }

            SetStatus("All set. Press PLAY.", Theme.Accent);
        }
        finally
        {
            _downloadingContent = false;
        }
    }

    /// <summary>The content index, fetched or cached, so step 2 can show available game file updates.</summary>
    private async Task RefreshPackIndexAsync(CancellationToken cancel)
    {
        try
        {
            _packIndex = await _packs.FetchIndexAsync(cancel);
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException)
        {
            // Offline with no cached index: step 2 only checks the packs are there.
        }
    }

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
        await RefreshPackIndexAsync(cancel);

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
            SetStatus((check.GameUpdateAvailable || PackUpdateAvailable) && _store.Current() is not null
                ? "An update is available."
                : string.Empty, Theme.Muted);
        }
    }

    private async Task UpdateGameAsync(CancellationToken cancel)
    {
        SemVersion? current = _store.Current();
        // The main Update button also handles newer game files without a newer game build (or release data).
        // Never reinstall an older release when only the downloaded files need updating.
        if (_lastCheck is { GameUpdateAvailable: true, Latest: { } latest } && _store.Pending() is null
            && (current is null || latest.Version > current))
        {
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

        // Continue through the game files after installing, or update those files on their own.
        current = _store.Current();
        if (current is not null && !FilesReady(current))
        {
            await DownloadContentAsync(cancel);
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

        if (!DownloadedFilesPlayable(current))
        {
            SetStatus("Download the game files first (step 2).", Theme.Warning);
            return false;
        }

        string exe = _store.ExePath(current);
        try
        {
            using Process? process = Process.Start(GameStart.Create(exe,
                extraUserArgs: joinServerId is null ? null : [WebJoinRequest.GameArgumentPrefix + joinServerId]));
            if (process is null) throw new IOException("The game process did not start.");
            SetStatus(joinServerId is null ? $"Started OpenSCCT {current}." : "Started OpenSCCT with your Join request. Continue in the game.", Theme.Accent);
            WindowState = FormWindowState.Minimized;
            return true;
        }
        catch (Exception ex) when (ex is Win32Exception or IOException)
        {
            SetStatus("Could not start the game: " + ex.Message, Theme.Error);
            return false;
        }
    }

    private void OpenSettings()
    {
        using var dialog = new SettingsForm(_settings, _layout);
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        _settings = dialog.Result;
        _settings.Save(_layout.Settings);
        _service = CreateService();
        _lastCheck = null;
        if (dialog.ResetRequested && !ResetInstall())
        {
            RefreshLocal();
            return;
        }

        RefreshLocal();

        _ = RunAsync(CheckAsync);
    }

    /// <summary>Deletes what the launcher installed (<see cref="InstallReset"/>); false when a game is running.</summary>
    private bool ResetInstall()
    {
        if (_games.Running().Count > 0)
        {
            SetStatus("Close OpenSCCT before resetting the install.", Theme.Warning);
            return false;
        }

        IReadOnlyList<string> failed = InstallReset.Run(_layout, Program.ExePath);
        _packIndex = null;
        SetStatus(
            failed.Count == 0
                ? "Install reset. Press the button to download the game again."
                : "Install reset, but some files are in use and were kept: " + string.Join(", ", failed),
            failed.Count == 0 ? Theme.Accent : Theme.Warning);
        return true;
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
                SetStatus($"Now on version {_store.Current()}.", Theme.Accent);
                RefreshLocal();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetStatus("Could not switch versions yet: " + ex.Message, Theme.Warning);
        }
    }

    private Progress<DownloadProgress> ProgressReporter()
    {
        _progress.Fraction = 0;
        _progress.Visible = true;
        return new Progress<DownloadProgress>(p =>
        {
            _progress.Fraction = p.Total > 0 ? (double)p.Received / p.Total : 0;
            string rate = p.BytesPerSecond > 0 ? $"  {Mb(p.BytesPerSecond)}/s" : string.Empty;
            _progressText.Text = p.Total > 0 ? $"{Mb(p.Received)} of {Mb(p.Total)}{rate}" : Mb(p.Received) + rate;
        });
    }

    private Progress<PackProgress> PackReporter(string prefix)
    {
        _progress.Fraction = 0;
        _progress.Visible = true;
        return new Progress<PackProgress>(p =>
        {
            _progress.Fraction = p.Total > 0 ? (double)p.Received / p.Total : 1;
            _progressText.Text = p.Extracting ? $"{prefix}: installing..." : $"{prefix}: {Mb(p.Received)} of {Mb(p.Total)}";
        });
    }

    private static string Gb(long bytes) =>
        bytes >= 1L << 30
            ? (bytes / (double)(1L << 30)).ToString("0.0", CultureInfo.CurrentCulture) + " GB"
            : Mb(bytes);

    private static string Mb(double bytes) => (bytes / (1024 * 1024)).ToString("0.0", CultureInfo.CurrentCulture) + " MB";

    private async Task RunAsync(Func<CancellationToken, Task> work)
    {
        if (_work is not null)
        {
            return;
        }

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
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException)
        {
            SetStatus(ex.Message + " Press the button to try again; downloads carry on where they stopped.", Theme.Error);
        }
        finally
        {
            _work.Dispose();
            _work = null;
            _progress.Visible = false;
            _progressText.Text = string.Empty;
            if (!IsDisposed)
            {
                RefreshLocal();
            }
        }
        if (completed && !IsDisposed && _webJoinReady && _pendingWebJoin is not null) await TryWebJoinAsync();
    }

    private void SetStatus(string text, Color color, string? path = null)
    {
        _status.Text = text;
        _status.ForeColor = color;

        // A file named in the status opens its folder with the file selected.
        _status.Links.Clear();
        int at = path is null ? -1 : text.IndexOf(path, StringComparison.Ordinal);
        if (at >= 0)
        {
            _status.Links.Add(at, path!.Length, path);
        }
    }

    private static void ShowInExplorer(string? path)
    {
        if (path is null)
        {
            return;
        }

        string args = File.Exists(path) ? "/select,\"" + path + "\"" : "\"" + Path.GetDirectoryName(path) + "\"";
        if (File.Exists(path) || Directory.Exists(Path.GetDirectoryName(path)))
        {
            using Process? explorer = Process.Start(new ProcessStartInfo("explorer.exe", args) { UseShellExecute = false });
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _pendingTimer.Dispose();
            _work?.Dispose();
        }

        base.Dispose(disposing);
    }
}
