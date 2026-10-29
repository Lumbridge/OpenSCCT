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

namespace OpenScct.Launcher.App;

/// <summary>
/// The launcher window, laid out as the three steps a player goes through: find Splinter Cell Chaos Theory (optional),
/// download OpenSCCT, prepare the game files (converted from their copy, or downloaded from OpenSCCT-Content when they
/// have none). One big button always does the next thing and becomes PLAY when everything is ready. Less common actions (what's new, check again, roll back, settings) are in the row below it.
/// </summary>
internal sealed class MainForm : Form
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
        ChooseGame,
        Check,
        Install,
        Update,
        Prepare,
        Play,
        Cancel,
    }

    private readonly LauncherLayout _layout = new(LauncherLayout.DefaultRoot);
    private readonly VersionStore _store;
    private readonly MapLibrary _maps;
    private readonly ContentStore _packs;
    private readonly TokenStore _tokens;
    private readonly IGameProcessProbe _games = new GameProcessProbe();
    private readonly LauncherSwap? _swap;
    private readonly SemVersion? _launcherVersion;
    private LauncherSettings _settings;
    private UpdateService _service;
    private UpdateCheck? _lastCheck;
    private CancellationTokenSource? _work;
    private bool _buildingContent;
    private bool _downloadingContent;
    private ContentIndex? _packIndex;
    private NextAction _next;
    private string _notesTitle = "What's new";
    private string _notesText = "Checking for releases...";
    private Control? _root;
    private bool _fitPending;
    private bool _fitting;
    private Control? _more;

    private readonly Button _changeGame = Theme.Button("Change...");
    private readonly Button _rebuild = Theme.Button("Rebuild");
    private readonly StepCard _gameStep;
    private readonly StepCard _installStep;
    private readonly StepCard _filesStep;
    private readonly GreenProgressBar _progress = new();
    private readonly Label _progressText = Theme.Label(string.Empty, Theme.Small, Theme.Muted);
    private readonly Button _main = Theme.Button("PLAY", primary: true);
    private readonly LinkLabel _status = new()
    {
        AutoSize = true, Font = Theme.Body, ForeColor = Theme.Muted, BackColor = Color.Transparent,
        LinkColor = Theme.Accent, ActiveLinkColor = Theme.Text, VisitedLinkColor = Theme.Accent,
    };
    private readonly Button _whatsNew = Theme.Button("What's new");
    private readonly Button _check = Theme.Button("Check for updates");
    private readonly Button _rollback = Theme.Button("Roll back");
    private readonly Button _mapsButton = Theme.Button("Maps");
    private readonly Button _settingsButton = Theme.Button("Settings");
    private readonly TableLayoutPanel _launcherBanner = Theme.Grid(Theme.Fill, Theme.Auto);
    private readonly Label _launcherInfo = Theme.Label(string.Empty, Theme.Body, Theme.Warning);
    private readonly Button _launcherButton = Theme.Button("Restart to finish");
    private readonly System.Windows.Forms.Timer _pendingTimer = new() { Interval = 3000 };

    public MainForm()
    {
        _store = new VersionStore(_layout);
        _maps = new MapLibrary(_layout.Maps);
        _packs = new ContentStore(_layout.Packs, Http);
        _tokens = new TokenStore(_layout.Token, new DpapiProtector());
        _settings = LauncherSettings.Load(_layout.Settings);
        _service = CreateService();
        if (Program.ExePath is { } exe)
        {
            _swap = new LauncherSwap(exe);
        }

        string? info = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        _launcherVersion = SemVersion.TryParse(info, out SemVersion? v) ? v : null;

        _gameStep = new StepCard(1, "Find Splinter Cell Chaos Theory (optional)", _changeGame);
        _installStep = new StepCard(2, "Download OpenSCCT");
        _filesStep = new StepCard(3, "Prepare the game files", _rebuild);

        BuildUi();
        _pendingTimer.Tick += (_, _) => OnPendingTick();
    }

    private UpdateService CreateService()
    {
        var source = new ReleaseSource(_settings.Owner, _settings.Repo, _tokens.Load());
        var github = new GitHubReleaseClient(Http, source, _layout.Cache);
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
        TableLayoutPanel header = Theme.Grid(Theme.Auto, Theme.Fill);
        Theme.AddRow(header, logo, Theme.Flow(true, title, subtitle));
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

        // The big button on the right, the status line next to it.
        _main.Anchor = AnchorStyles.Right | AnchorStyles.Top;
        Theme.Wrapping(_status);
        _status.Margin = new Padding(0, 8, 16, 0);
        _status.LinkClicked += (_, e) => ShowInExplorer(e.Link?.LinkData as string);
        TableLayoutPanel actions = Theme.Grid(Theme.Fill, Theme.Auto);
        actions.Margin = new Padding(0, 14, 0, 0);
        Theme.AddRow(actions, _status, _main);

        // The less common actions, small, along the bottom edge.
        Label version = Theme.Label("Launcher " + (_launcherVersion?.ToString() ?? "dev"), Theme.Small, Theme.Muted);
        version.Anchor = AnchorStyles.Right;
        version.Margin = new Padding(12, 0, 0, 0);
        TableLayoutPanel more = Theme.Grid(Theme.Fill, Theme.Auto);
        more.Dock = DockStyle.Bottom;
        more.Padding = new Padding(0, 12, 0, 0);
        Theme.AddRow(more, Theme.Flow(false, _whatsNew, _check, _rollback, _mapsButton, _settingsButton), version);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, BackColor = Color.Transparent };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        foreach (Control row in new Control[] { header, _launcherBanner, _gameStep, _installStep, _filesStep, _progress, _progressText, actions })
        {
            int r = root.RowCount++;
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.Controls.Add(row, 0, r);
        }

        Controls.Add(root);
        Controls.Add(more);
        _root = root;
        _more = more;

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
        tips.SetToolTip(_changeGame, "Pick the folder you installed Splinter Cell Chaos Theory in. Without it the game files are downloaded.");
        tips.SetToolTip(_rebuild, "Prepare the game files again from your copy, for example if a map looks wrong.");
        tips.SetToolTip(_rollback, "Go back to the version you had before the last update.");
        tips.SetToolTip(_mapsButton, "Download new maps made by the community.");

        _main.Click += async (_, _) => await DoNextAsync();
        _changeGame.Click += (_, _) => ChooseGameFolder();
        _rebuild.Click += async (_, _) => await RunAsync(BuildContentAsync);
        _whatsNew.Click += (_, _) => ShowWhatsNew();
        _check.Click += async (_, _) => await RunAsync(CheckAsync);
        _rollback.Click += (_, _) => Rollback();
        _settingsButton.Click += (_, _) => OpenSettings();
        _mapsButton.Click += (_, _) => OpenMaps();
        _launcherButton.Click += async (_, _) => await RunAsync(UpdateLauncherAsync);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        // The bottom row of buttons does not wrap: widen the window to it (at this display scaling), and never let
        // it be dragged narrower.
        if (_more is not null)
        {
            int need = _more.GetPreferredSize(Size.Empty).Width + Padding.Horizontal;
            Rectangle work = Screen.FromControl(this).WorkingArea;
            int width = Math.Min(Math.Max(ClientSize.Width, need), work.Width - (Width - ClientSize.Width));
            if (width != ClientSize.Width)
            {
                ClientSize = new Size(width, ClientSize.Height);
            }

            MinimumSize = new Size(Math.Max(MinimumSize.Width, SizeFromClientSize(new Size(width, 0)).Width), MinimumSize.Height);
        }

        if (_root is not null)
        {
            Theme.FitToContent(this, _root);
        }
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        FindGameIfUnset();
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
    }

    /// <summary>
    /// The player's Chaos Theory Versus folder, found without asking (their Steam libraries, then common folders)
    /// when none is set or the set one is gone. Step 1's Change button can still pick another.
    /// </summary>
    private void FindGameIfUnset()
    {
        if (GameLocator.Normalize(_settings.OriginalGamePath) is { } set)
        {
            if (!string.Equals(set, _settings.OriginalGamePath, StringComparison.OrdinalIgnoreCase))
            {
                _settings.OriginalGamePath = set;
                _settings.Save(_layout.Settings);
            }

            return;
        }

        if (GameLocator.Find() is { } found)
        {
            _settings.OriginalGamePath = found;
            _settings.Save(_layout.Settings);
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _work?.Cancel();
        _pendingTimer.Stop();
        base.OnFormClosing(e);
    }

    // ---- the steps ---------------------------------------------------------------------------------------------

    private bool GameFound => ContentBuildPlan.LooksLikeOriginalInstall(_settings.OriginalGamePath ?? string.Empty);

    private void RefreshLocal()
    {
        SemVersion? current = _store.Current();
        bool busy = _work is not null;

        RefreshGameStep();
        RefreshInstallStep(current);
        RefreshFilesStep(current, busy);

        _changeGame.Enabled = !busy;
        _rollback.Enabled = _store.Previous() is not null && !busy;
        _check.Enabled = !busy;
        _settingsButton.Enabled = !busy;
        _mapsButton.Enabled = !busy;
        _launcherButton.Enabled = !busy;
        if (_swap is { HasStaged: true })
        {
            _launcherInfo.Text = "A new version of this launcher is ready.";
            _launcherButton.Visible = true;
            _launcherBanner.Visible = true;
        }

        RefreshMainButton(current, busy);
    }

    private void RefreshGameStep()
    {
        string game = _settings.OriginalGamePath ?? string.Empty;
        if (GameFound)
        {
            _gameStep.Set(StepState.Done, "Found: " + game);
            _changeGame.Text = "Change...";
        }
        else if (game.Length == 0)
        {
            _gameStep.Set(StepState.Done, "Not found on this PC, so the game files are downloaded instead. If you have it, choose its folder to make them from your copy.");
            _changeGame.Text = "Choose folder...";
        }
        else
        {
            _gameStep.Set(StepState.Problem, "This folder is not a Splinter Cell Chaos Theory install, so the game files are downloaded instead: " + game);
            _changeGame.Text = "Choose folder...";
        }
    }

    private void RefreshInstallStep(SemVersion? current)
    {
        SemVersion? pending = _store.Pending();
        VersionedRelease? latest = _lastCheck?.Latest;
        bool update = _lastCheck is { GameUpdateAvailable: true };
        if (pending is not null)
        {
            _installStep.Set(StepState.Done, $"Version {pending} is ready and starts the next time you play.");
        }
        else if (current is not null && update && latest is not null)
        {
            _installStep.Set(StepState.Todo, $"Version {current} is installed. Version {latest.Version} is available.");
        }
        else if (current is not null)
        {
            _installStep.Set(StepState.Done, $"Version {current} is installed and up to date.");
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

    private void RefreshFilesStep(SemVersion? current, bool busy)
    {
        if (!GameFound)
        {
            RefreshPackFilesStep(current);
            return;
        }

        ContentState? state = ContentState.Load(_layout.Content);
        bool hasBuilder = current is not null && ContentBuildPlan.Python(_layout.VersionDir(current.ToString())) is not null;
        _rebuild.Visible = state is { Ok: true } && !_buildingContent;
        _rebuild.Enabled = !busy && GameFound && hasBuilder;
        if (_buildingContent)
        {
            _filesStep.Set(StepState.Todo, "Converting the maps, characters and sounds from your copy...");
        }
        else if (!GameFound || current is null)
        {
            _filesStep.Set(StepState.Waiting, "Made from your own copy of the game once steps 1 and 2 are done. Nothing is downloaded.");
        }
        else if (!hasBuilder)
        {
            _filesStep.Set(StepState.Problem, $"Version {current} cannot prepare game files. Update OpenSCCT (step 2).");
        }
        else if (state is null)
        {
            _filesStep.Set(StepState.Todo, "Converts the maps, characters and sounds from your copy. Takes a few minutes the first time.");
        }
        else if (!state.Ok)
        {
            _filesStep.Set(StepState.Problem, "The last run did not finish. Run it again to carry on where it stopped.");
        }
        else if (!ContentIsCurrent(current))
        {
            bool sameVersion = state.Version == current.ToString();
            bool sameLanguage = ContentBuildPlan.NormaliseLanguage(state.Language) == ContentBuildPlan.NormaliseLanguage(_settings.Language);
            _filesStep.Set(StepState.Todo, !sameVersion
                ? "OpenSCCT was updated. The game files need a quick refresh."
                : !sameLanguage
                    ? "Your language changed in Settings. Prepare the game files again to use it."
                    : "Your downloaded maps changed. Prepare the game files to add them.");
        }
        else
        {
            string language = ContentBuildPlan.Languages.FirstOrDefault(l => l.Code == ContentBuildPlan.NormaliseLanguage(state.Language)).Name ?? "English";
            _filesStep.Set(StepState.Done, $"Ready: {state.Maps} maps, in {language}.");
        }
    }

    /// <summary>Step 3 without an install: the game files come from OpenSCCT-Content.</summary>
    private void RefreshPackFilesStep(SemVersion? current)
    {
        _rebuild.Visible = false;
        if (_downloadingContent)
        {
            _filesStep.Set(StepState.Todo, "Downloading the maps, characters and sounds...");
        }
        else if (current is null)
        {
            _filesStep.Set(StepState.Waiting, "Downloaded once step 2 is done (you do not need your own copy of the game).");
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
            _filesStep.Set(StepState.Todo, "Newer game files are available. Download them to play with everyone else.");
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

    /// <summary>The game files are ready for the current version: built from the install, else downloaded.</summary>
    private bool FilesReady(SemVersion? current) => GameFound ? ContentIsCurrent(current) : current is not null && PacksCurrent();

    /// <summary>The big button: the next thing to do, or PLAY.</summary>
    private void RefreshMainButton(SemVersion? current, bool busy)
    {
        (NextAction next, string text, bool enabled) = busy
            ? (NextAction.Cancel, _buildingContent || _downloadingContent ? "Stop" : "Cancel", true)
            : current is null
                ? _lastCheck is { Latest: not null, GameUpdateAvailable: true }
                    ? (NextAction.Install, "Install", true)
                    : (NextAction.Check, _lastCheck is null ? "Check for updates" : "Check again", true)
                : _lastCheck is { GameUpdateAvailable: true }
                    ? (NextAction.Update, "Update", true)
                    : !FilesReady(current)
                        ? GameFound
                            ? (NextAction.Prepare, "Prepare game files", ContentBuildPlan.Python(_layout.VersionDir(current.ToString())) is not null)
                            : (NextAction.Prepare, "Download game files", true)
                        : (NextAction.Play, "PLAY", true);
        _next = next;
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
            case NextAction.ChooseGame:
                ChooseGameFolder();
                break;
            case NextAction.Check:
                await RunAsync(CheckAsync);
                break;
            case NextAction.Install:
            case NextAction.Update:
                await RunAsync(UpdateGameAsync);
                break;
            case NextAction.Prepare:
                await RunAsync(GameFound ? BuildContentAsync : DownloadContentAsync);
                break;
            case NextAction.Play:
                Play();
                break;
        }
    }

    private void ChooseGameFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Choose the folder you installed Splinter Cell Chaos Theory in",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false,
        };
        if (Directory.Exists(_settings.OriginalGamePath))
        {
            dialog.InitialDirectory = _settings.OriginalGamePath;
        }

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        if (GameLocator.Normalize(dialog.SelectedPath) is not { } versus)
        {
            SetStatus("That folder does not have Splinter Cell Chaos Theory in it. Pick the folder with the Versus folder inside.", Theme.Warning);
            return;
        }

        bool changed = !string.Equals(versus, _settings.OriginalGamePath, StringComparison.OrdinalIgnoreCase);
        _settings.OriginalGamePath = versus;
        _settings.Save(_layout.Settings);
        SetStatus(changed ? "Game folder saved." : "That is the folder already in use.", Theme.Accent);
        RefreshLocal();
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

    /// <summary>Opens a link of the release notes in the browser (http and https only).</summary>
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

    private bool ContentIsCurrent(SemVersion? current) =>
        current is not null && ContentState.Load(_layout.Content) is { } state
        && state.IsCurrent(current.ToString(), _settings.OriginalGamePath, _settings.Language, DownloadedMapsFor(current));

    /// <summary>
    /// The downloaded maps the current version's builder would read (their fingerprint), or empty when there are
    /// none or the builder is older than downloaded maps (it then builds without them).
    /// </summary>
    private string DownloadedMapsFor(SemVersion current) =>
        _maps.HasMaps && ContentBuildPlan.SupportsAddons(_layout.VersionDir(current.ToString())) ? _maps.Fingerprint() : string.Empty;

    /// <summary>
    /// The Maps window. Downloaded maps are ready to play; removing a map an older launcher took from SCCT-Maps
    /// prepares the game files again when they were ready before.
    /// </summary>
    private void OpenMaps()
    {
        if (_work is not null)
        {
            return;
        }

        bool changed;
        using (var dialog = new MapsForm(_maps, Http, _layout.Packs, _layout.Content))
        {
            dialog.ShowDialog(this);
            changed = dialog.Changed;
        }

        RefreshLocal();
        SemVersion? current = _store.Current();
        if (!changed || current is null)
        {
            return;
        }

        if (!ContentBuildPlan.SupportsAddons(_layout.VersionDir(current.ToString())))
        {
            SetStatus($"Version {current} cannot add downloaded maps yet. They are added once OpenSCCT is updated.", Theme.Warning);
            return;
        }

        if (ContentState.Load(_layout.Content) is not null && GameFound && !ContentIsCurrent(current))
        {
            _ = RunAsync(BuildContentAsync);
        }
    }

    // ---- the work ----------------------------------------------------------------------------------------------

    /// <summary>
    /// Builds the player's content from their install with the current version's builder. The builder's cache keeps
    /// what is still valid, so a stopped build resumes and an update only redoes what changed.
    /// </summary>
    private async Task BuildContentAsync(CancellationToken cancel)
    {
        SemVersion? current = _store.Current();
        string game = _settings.OriginalGamePath ?? string.Empty;
        if (current is null || !ContentBuildPlan.LooksLikeOriginalInstall(game)
            || ContentBuildPlan.Python(_layout.VersionDir(current.ToString())) is not { } python)
        {
            return;
        }

        _buildingContent = true;
        RefreshLocal();
        _progress.Fraction = 0;
        _progress.Visible = true;
        SetStatus("Preparing the game files. The first time takes a few minutes; you can stop and carry on later.", Theme.Muted);
        string logPath = Path.Combine(_layout.Content, "build.log");
        Directory.CreateDirectory(_layout.Content);

        // The text under the bar redraws every second as well as on each builder line, so a slow unit still shows a
        // moving clock and what it is working on instead of looking frozen.
        var started = Stopwatch.StartNew();
        var sinceLast = Stopwatch.StartNew();
        ContentProgress? last = null;
        void ShowProgress()
        {
            _progressText.Text = ContentProgressText(last, started.Elapsed, sinceLast.Elapsed);
        }

        using var ticker = new System.Windows.Forms.Timer { Interval = 1000 };
        ticker.Tick += (_, _) => ShowProgress();
        ShowProgress();
        ticker.Start();
        try
        {
            await using var log = new StreamWriter(logPath, append: false) { AutoFlush = true };
            var progress = new Progress<ContentProgress>(p =>
            {
                if (last is null || p.Done != last.Done)
                {
                    sinceLast.Restart();
                }

                last = p;
                _progress.Fraction = p.Total > 0 ? (double)p.Done / p.Total : 0;
                ShowProgress();
            });
            object gate = new();
            string downloaded = DownloadedMapsFor(current);
            ContentState state = await ContentBuilder.RunAsync(python, current.ToString(), game, _layout.Content, ContentBuildPlan.DefaultJobs,
                progress, line =>
                {
                    lock (gate)
                    {
                        log.WriteLine(line);
                    }
                },
                cancel, _settings.Language, downloaded.Length > 0 ? _maps.GameFolder : null, downloaded);
            SetStatus(state.Ok
                ? "All set. Press PLAY."
                : $"Some files could not be prepared ({state.Failed}). Press the button to try again. Details: {logPath}", state.Ok ? Theme.Accent : Theme.Error,
                state.Ok ? null : logPath);
        }
        finally
        {
            ticker.Stop();
            _buildingContent = false;
        }
    }

    /// <summary>
    /// Downloads the game files from OpenSCCT-Content for a player without an install: the shared packs, and the stock
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
            foreach (ContentSharedPack pack in shared)
            {
                n++;
                string name = pack.Title.Length > 0 ? pack.Title : pack.Id;
                await _packs.InstallSharedAsync(pack, PackReporter($"{name} ({n} of {total})"), cancel);
            }

            foreach (ContentMap map in maps)
            {
                n++;
                await _packs.InstallMapAsync(map, ContentKind.Full, PackReporter($"{map.DisplayTitle} ({n} of {total})"), cancel);
            }

            SetStatus("All set. Press PLAY.", Theme.Accent);
        }
        finally
        {
            _downloadingContent = false;
        }
    }

    /// <summary>The index of OpenSCCT-Content (fetched, else the cached copy), so step 3 can tell newer game files.</summary>
    private async Task RefreshPackIndexAsync(CancellationToken cancel)
    {
        try
        {
            _packIndex = await _packs.FetchIndexAsync(cancel);
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException)
        {
            // Offline with no cached index: step 3 only checks the packs are there.
        }
    }

    private async Task CheckAsync(CancellationToken cancel)
    {
        if (!GameFound)
        {
            await RefreshPackIndexAsync(cancel);
        }

        SetStatus("Looking for updates...", Theme.Muted);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        UpdateCheck check = await _service.CheckAsync(_settings.IncludePrereleases, _launcherVersion, timeout.Token);
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

        // The launcher fetches its own update at once (verified, staged as .new); the swap waits for "Restart to
        // finish" so a running download or build is never cut off. A newer launcher can read newer release formats.
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

        // Only when it cannot stage the update itself (read-only folder). One already staged keeps its restart button.
        if (check.LauncherUpdateAvailable && check.LatestLauncher is { } l && _swap is not { HasStaged: true })
        {
            _launcherInfo.Text = $"Launcher {l} is available. Download it from the OpenSCCT releases page (this folder is read only).";
            _launcherButton.Visible = false;
            _launcherBanner.Visible = true;
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
        if (_lastCheck?.Latest is not { } latest)
        {
            return;
        }

        SetStatus($"Downloading OpenSCCT {latest.Version}...", Theme.Muted);
        InstallOutcome outcome = await _service.InstallAsync(latest, ProgressReporter(), cancel);
        SetStatus(outcome switch
        {
            InstallOutcome.Activated => $"OpenSCCT {latest.Version} is installed.",
            InstallOutcome.WaitingForGameToClose => $"OpenSCCT {latest.Version} is ready and starts once you close the game.",
            _ => $"OpenSCCT {latest.Version} is already installed.",
        }, Theme.Accent);
        await CheckAsync(cancel);

        // A new install (or a changed builder) needs its game files before PLAY: carry straight on.
        if (outcome == InstallOutcome.Activated && !FilesReady(_store.Current()))
        {
            await (GameFound ? BuildContentAsync(cancel) : DownloadContentAsync(cancel));
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
            Process.Start(new ProcessStartInfo(started, Program.AfterUpdateArg) { UseShellExecute = false });
            Close();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception)
        {
            SetStatus("Could not replace the launcher: " + ex.Message, Theme.Error);
        }
    }

    private void Play()
    {
        SemVersion? current = _store.Current();
        if (current is null)
        {
            return;
        }

        // Content built from an install wins; without it the downloaded packs are enough (the game finds them itself).
        bool built = ContentState.Load(_layout.Content) is { Ok: true };
        if (!built && !PackedContent.Installed(_packs))
        {
            SetStatus("Get the game files first (step 3).", Theme.Warning);
            return;
        }

        string exe = _store.ExePath(current);
        try
        {
            using Process? process = Process.Start(GameStart.Create(exe, GameFound ? _settings.OriginalGamePath : null,
                built ? _layout.Content : null, mapsRoot: _layout.Maps));
            SetStatus($"Started OpenSCCT {current}.", Theme.Accent);
            WindowState = FormWindowState.Minimized;
        }
        catch (Exception ex) when (ex is Win32Exception or IOException)
        {
            SetStatus("Could not start the game: " + ex.Message, Theme.Error);
        }
    }

    private void Rollback()
    {
        SemVersion? previous = _store.Previous();
        if (previous is null)
        {
            return;
        }

        if (MessageBox.Show(this, $"Go back to OpenSCCT {previous}?", "Roll back", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        try
        {
            SetStatus(_service.Rollback() ? $"Back on version {previous}." : "There is no earlier version to go back to.", Theme.Accent);
        }
        catch (InvalidOperationException ex)
        {
            SetStatus(ex.Message, Theme.Warning);
        }

        RefreshLocal();
    }

    private void OpenSettings()
    {
        using var dialog = new SettingsForm(_settings, _tokens, _layout);
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        bool languageChanged = dialog.Result.Language != _settings.Language;
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

        // Text is part of the game files: a new language prepares them again (the cache redoes only the text steps
        // and the maps, which hold their own text).
        bool rebuild = languageChanged && ContentState.Load(_layout.Content) is not null && GameFound;
        _ = RunAsync(async cancel =>
        {
            await CheckAsync(cancel);
            if (rebuild && !_buildingContent)
            {
                await BuildContentAsync(cancel);
            }
        });
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
                ? "Install reset. Press the button to download and prepare the game again."
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

    /// <summary>
    /// "12 of 140 done · 3:05 so far · Working on characters, Ark and 2 more": what a content build is doing, with a
    /// note once nothing has finished for a while (some units take minutes).
    /// </summary>
    private static string ContentProgressText(ContentProgress? p, TimeSpan elapsed, TimeSpan sinceLast)
    {
        var parts = new List<string>();
        if (p is { Total: > 0 })
        {
            parts.Add($"{p.Done} of {p.Total} done");
        }
        else
        {
            parts.Add("Starting up");
        }

        parts.Add(Clock(elapsed) + " so far");
        if (p is { Running.Count: > 0 })
        {
            IReadOnlyList<string> running = p.Running;
            string names = running.Count <= 3
                ? string.Join(", ", running)
                : string.Join(", ", running.Take(2)) + $" and {running.Count - 2} more";
            parts.Add("Working on " + names);
        }

        if (sinceLast >= TimeSpan.FromSeconds(45))
        {
            parts.Add($"still working ({Clock(sinceLast)} on this part, some take a few minutes)");
        }

        return string.Join("  ·  ", parts);
    }

    private static string Clock(TimeSpan t) =>
        t.TotalHours >= 1
            ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}"
            : $"{(int)t.TotalMinutes}:{t.Seconds:00}";

    private static string Mb(double bytes) => (bytes / (1024 * 1024)).ToString("0.0", CultureInfo.CurrentCulture) + " MB";

    private async Task RunAsync(Func<CancellationToken, Task> work)
    {
        if (_work is not null)
        {
            return;
        }

        _work = new CancellationTokenSource();
        RefreshLocal();
        try
        {
            await work(_work.Token);
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
