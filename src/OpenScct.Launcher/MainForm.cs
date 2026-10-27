using System;
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
using OpenScct.Launcher.Settings;
using OpenScct.Launcher.Update;
using OpenScct.Launcher.Versioning;

namespace OpenScct.Launcher.App;

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

    private readonly LauncherLayout _layout = new(LauncherLayout.DefaultRoot);
    private readonly VersionStore _store;
    private readonly TokenStore _tokens;
    private readonly IGameProcessProbe _games = new GameProcessProbe();
    private readonly LauncherSwap? _swap;
    private readonly SemVersion? _launcherVersion;
    private LauncherSettings _settings;
    private UpdateService _service;
    private UpdateCheck? _lastCheck;
    private CancellationTokenSource? _work;
    private bool _buildingContent;
    private Control? _root;

    private readonly Label _installedValue = Theme.Label("-", Theme.Heading, Theme.Accent);
    private readonly Label _latestValue = Theme.Label("-", Theme.Heading);
    private readonly Label _status = Theme.Label("Ready.", Theme.Body, Theme.Muted);
    private readonly Label _notesHeading = Theme.Label("Release notes", Theme.Heading);
    private readonly TextBox _notes = Theme.TextBox();
    private readonly Label _contentInfo = Theme.Label(string.Empty, Theme.Small, Theme.Muted);
    private readonly GreenProgressBar _progress = new();
    private readonly Label _progressText = Theme.Label(string.Empty, Theme.Small, Theme.Muted);
    private readonly Button _play = Theme.Button("PLAY", primary: true);
    private readonly Button _update = Theme.Button("Update");
    private readonly Button _check = Theme.Button("Check for updates");
    private readonly Button _rollback = Theme.Button("Roll back");
    private readonly Button _settingsButton = Theme.Button("Settings");
    private readonly Button _contentButton = Theme.Button("Build content");
    private readonly Button _launcherButton = Theme.Button("Update launcher");
    private readonly Label _launcherInfo = Theme.Label(string.Empty, Theme.Small, Theme.Warning);
    private readonly System.Windows.Forms.Timer _pendingTimer = new() { Interval = 3000 };

    public MainForm()
    {
        _store = new VersionStore(_layout);
        _tokens = new TokenStore(_layout.Token, new DpapiProtector());
        _settings = LauncherSettings.Load(_layout.Settings);
        _service = CreateService();
        if (Environment.ProcessPath is { } exe)
        {
            _swap = new LauncherSwap(exe);
        }

        string? info = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        _launcherVersion = SemVersion.TryParse(info, out SemVersion? v) ? v : null;

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
        ClientSize = new Size(820, 600);
        MinimumSize = new Size(640, 520);
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
        Label title = Theme.Label("OpenSCCT", Theme.Title, Theme.Accent);
        Label subtitle = Theme.Label("Splinter Cell Chaos Theory Versus, rebuilt", Theme.Small, Theme.Muted);
        Label launcherVersion = Theme.Label("Launcher " + (_launcherVersion?.ToString() ?? "dev"), Theme.Small, Theme.Muted);
        launcherVersion.Margin = new Padding(0, 10, 12, 0);
        TableLayoutPanel header = Theme.Grid(Theme.Auto, Theme.Fill, Theme.Auto);
        FlowLayoutPanel headerRight = Theme.Flow(false, launcherVersion, _settingsButton);
        headerRight.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        Theme.AddRow(header, logo, Theme.Flow(true, title, subtitle), headerRight);

        Label installedLabel = Theme.Label("Installed", Theme.Small, Theme.Muted);
        Label latestLabel = Theme.Label("Latest release", Theme.Small, Theme.Muted);
        _launcherInfo.Visible = false;
        _launcherButton.Visible = false;
        TableLayoutPanel info = Theme.Grid(Theme.Auto, Theme.Auto, Theme.Fill);
        info.BackColor = Theme.Panel;
        info.Padding = new Padding(14, 8, 14, 8);
        info.Margin = new Padding(0, 12, 0, 0);
        FlowLayoutPanel installed = Theme.Flow(true, installedLabel, _installedValue);
        installed.Margin = new Padding(0, 0, 40, 0);
        FlowLayoutPanel launcherUpdate = Theme.Flow(true, _launcherInfo, _launcherButton);
        launcherUpdate.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        Theme.AddRow(info, installed, Theme.Flow(true, latestLabel, _latestValue), launcherUpdate);

        _notesHeading.Margin = new Padding(0, 12, 0, 4);
        _notes.Multiline = true;
        _notes.ReadOnly = true;
        _notes.ScrollBars = ScrollBars.Vertical;
        _notes.WordWrap = true;
        _notes.Dock = DockStyle.Fill;
        _notes.MinimumSize = new Size(0, 90);
        _notes.Text = "Checking for releases...";

        Label contentTitle = Theme.Label("Build game content from your install", Theme.Heading);
        TableLayoutPanel content = Theme.Grid(Theme.Fill, Theme.Auto);
        content.BackColor = Theme.Panel;
        content.Padding = new Padding(14, 8, 14, 8);
        content.Margin = new Padding(0, 10, 0, 0);
        _contentButton.Anchor = AnchorStyles.Right;
        _contentButton.Margin = new Padding(12, 0, 0, 0);
        TableLayoutPanel contentText = Theme.Grid(Theme.Fill);
        Theme.AddRow(contentText, contentTitle);
        Theme.AddRow(contentText, Theme.Wrapping(_contentInfo));
        Theme.AddRow(content, contentText, _contentButton);

        _progress.Visible = false;
        _progress.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _progress.Margin = new Padding(0, 10, 0, 2);
        Theme.Wrapping(_progressText);

        _play.Anchor = AnchorStyles.Right | AnchorStyles.Top;
        TableLayoutPanel actions = Theme.Grid(Theme.Fill, Theme.Auto);
        actions.Margin = new Padding(0, 10, 0, 0);
        FlowLayoutPanel buttons = Theme.Flow(false, _update, _check, _rollback);
        buttons.Anchor = AnchorStyles.Left | AnchorStyles.Top;
        Theme.AddRow(actions, buttons, _play);
        Theme.Wrapping(_status);
        _status.Margin = new Padding(0, 6, 0, 0);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, BackColor = Color.Transparent, AutoScroll = true };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        foreach (Control row in new Control[] { header, info, _notesHeading, _notes, content, _progress, _progressText, actions, _status })
        {
            int r = root.RowCount++;
            root.RowStyles.Add(ReferenceEquals(row, _notes) ? new RowStyle(SizeType.Percent, 100f) : new RowStyle(SizeType.AutoSize));
            root.Controls.Add(row, 0, r);
        }

        Controls.Add(root);
        _root = root;

        var tips = new ToolTip();
        tips.SetToolTip(_rollback, "Switch back to the version installed before the last update.");
        tips.SetToolTip(_contentButton, "Converts maps, characters, UI and sounds from your own install. Nothing is downloaded.");

        _play.Click += (_, _) => Play();
        _update.Click += async (_, _) => await RunAsync(UpdateGameAsync);
        _check.Click += async (_, _) => await RunAsync(CheckAsync);
        _rollback.Click += (_, _) => Rollback();
        _settingsButton.Click += (_, _) => OpenSettings();
        _launcherButton.Click += async (_, _) => await RunAsync(UpdateLauncherAsync);
        _contentButton.Click += async (_, _) =>
        {
            if (_buildingContent)
            {
                _work?.Cancel();
                return;
            }

            await RunAsync(BuildContentAsync);
        };
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
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
            _notes.Text = "Update checks on start are off. Use Check for updates.";
        }
    }

    /// <summary>
    /// The player's Chaos Theory Versus folder, found without asking (their Steam libraries, then common folders)
    /// when none is set or the set one is gone. Settings can still pick another.
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
            SetStatus("Found Splinter Cell Chaos Theory: " + found, Theme.Muted);
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _work?.Cancel();
        _pendingTimer.Stop();
        base.OnFormClosing(e);
    }

    private void RefreshLocal()
    {
        SemVersion? current = _store.Current();
        SemVersion? pending = _store.Pending();
        _installedValue.Text = current?.ToString() ?? "Not installed";
        if (pending is not null)
        {
            _installedValue.Text += $"  ({pending} after the game closes)";
        }

        bool busy = _work is not null;
        _play.Enabled = current is not null && !busy;
        _rollback.Enabled = _store.Previous() is not null && !busy;
        _check.Enabled = !busy;
        _settingsButton.Enabled = !busy;
        _update.Enabled = !busy && _lastCheck is { GameUpdateAvailable: true };
        _update.Text = _lastCheck is { GameUpdateAvailable: true, Latest: { } latest }
            ? (current is null ? "Install " : "Update to ") + latest.Version
            : "Up to date";
        if (_lastCheck is null)
        {
            _update.Text = "Update";
        }

        _launcherButton.Enabled = !busy;
        if (_swap is { HasStaged: true })
        {
            _launcherInfo.Text = "A launcher update is ready.";
            _launcherInfo.Visible = true;
            _launcherButton.Text = "Restart to finish";
            _launcherButton.Visible = true;
        }

        RefreshContent(current, busy);
    }

    /// <summary>The content step's line and button: what is built, from where, and whether it needs building.</summary>
    private void RefreshContent(SemVersion? current, bool busy)
    {
        if (_buildingContent)
        {
            _contentButton.Text = "Stop";
            _contentButton.Enabled = true;
            return;
        }

        string game = _settings.OriginalGamePath ?? string.Empty;
        ContentState? state = ContentState.Load(_layout.Content);
        bool validGame = ContentBuildPlan.LooksLikeOriginalInstall(game);
        bool hasBuilder = current is not null && ContentBuildPlan.Python(_layout.VersionDir(current.ToString())) is not null;
        _contentButton.Enabled = !busy && validGame && hasBuilder;
        _contentButton.Text = state is { Ok: true } ? (ContentIsCurrent(current) ? "Rebuild" : "Update content") : "Build content";
        if (game.Length == 0)
        {
            _contentInfo.Text = "Splinter Cell Chaos Theory was not found. Install it from Steam, or pick its folder in Settings.";
        }
        else if (!validGame)
        {
            _contentInfo.Text = "Not an SCCT install (no System and Packages folders): " + game;
        }
        else if (current is null)
        {
            _contentInfo.Text = "Install the game first. Install: " + game;
        }
        else if (!hasBuilder)
        {
            _contentInfo.Text = $"OpenSCCT {current} has no content builder; update the game.";
        }
        else if (state is null)
        {
            _contentInfo.Text = "Not built yet (takes a while the first time). Install: " + game;
        }
        else if (!state.Ok)
        {
            _contentInfo.Text = $"The last build did not finish ({state.Failed} failed). Build again to resume.";
        }
        else
        {
            string builtIn = ContentBuildPlan.Languages.FirstOrDefault(l => l.Code == ContentBuildPlan.NormaliseLanguage(state.Language)).Name ?? "English";
            _contentInfo.Text = (ContentIsCurrent(current) ? $"Built in {builtIn}"
                : state.Version == current.ToString() ? $"Built in {builtIn}; build again for the language picked in Settings"
                : $"Built with {state.Version}; update for {current}") +
                $": {state.Maps} maps, {state.FinishedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)}.";
        }
    }

    private bool ContentIsCurrent(SemVersion? current) =>
        current is not null && ContentState.Load(_layout.Content) is { } state && state.IsCurrent(current.ToString(), _settings.OriginalGamePath, _settings.Language);

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
        SetStatus("Building game content from your install. The first build takes a while; you can stop and resume it.", Theme.Muted);
        string logPath = Path.Combine(_layout.Content, "build.log");
        Directory.CreateDirectory(_layout.Content);
        try
        {
            await using var log = new StreamWriter(logPath, append: false) { AutoFlush = true };
            var progress = new Progress<ContentProgress>(p =>
            {
                _progress.Fraction = p.Total > 0 ? (double)p.Done / p.Total : 0;
                _progressText.Text = $"{p.Done} of {p.Total}  {Shorten(p.Line, 90)}";
            });
            object gate = new();
            ContentState state = await ContentBuilder.RunAsync(python, current.ToString(), game, _layout.Content, ContentBuildPlan.DefaultJobs,
                progress, line =>
                {
                    lock (gate)
                    {
                        log.WriteLine(line);
                    }
                },
                cancel, _settings.Language);
            SetStatus(state.Ok
                ? $"Game content built: {state.Maps} maps. Press Play."
                : $"Content build ended with {state.Failed} failures. Details: {logPath}", state.Ok ? Theme.Accent : Theme.Error);
        }
        finally
        {
            _buildingContent = false;
        }
    }

    private static string Shorten(string text, int max) => text.Length <= max ? text : text[..(max - 3)] + "...";

    private async Task CheckAsync(CancellationToken cancel)
    {
        _status.ForeColor = Theme.Muted;
        _status.Text = $"Checking {_settings.Owner}/{_settings.Repo}...";
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        UpdateCheck check = await _service.CheckAsync(_settings.IncludePrereleases, _launcherVersion, timeout.Token);
        _lastCheck = check;

        _latestValue.Text = check.Latest?.Version.ToString() ?? "None";
        if (check.Latest is { } latest)
        {
            ReleaseInfo r = latest.Release;
            string when = r.PublishedAt?.ToLocalTime().ToString("d", CultureInfo.CurrentCulture) ?? string.Empty;
            _notesHeading.Text = $"Release notes: {r.Name ?? r.TagName}  {when}";
            string body = string.IsNullOrWhiteSpace(r.Body) ? "(No notes for this release.)" : r.Body;
            _notes.Text = body.ReplaceLineEndings("\r\n");
        }
        else
        {
            _notesHeading.Text = "Release notes";
            _notes.Text = check.Feed.Source == ReleaseFeedSource.None
                ? "No release information yet."
                : "No release with a version tag was found.";
        }

        if (check.LauncherUpdateAvailable && _swap is { HasStaged: false } && check.Latest is { } l)
        {
            _launcherInfo.Text = $"Launcher {l.Version} is available.";
            _launcherInfo.Visible = true;
            _launcherButton.Text = "Update launcher";
            _launcherButton.Visible = true;
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
                SetStatus("The launcher update could not be downloaded: " + ex.Message, Theme.Warning);
            }

            return;
        }

        string from = check.Feed.Source switch
        {
            ReleaseFeedSource.Network => "Checked just now.",
            ReleaseFeedSource.NotModified => "Checked just now, nothing new.",
            ReleaseFeedSource.Cache => "Showing the last known releases.",
            _ => string.Empty,
        };
        SetStatus(check.Feed.Problem is null ? from : check.Feed.Problem + " " + from, check.Feed.Problem is null ? Theme.Muted : Theme.Warning);
    }

    private async Task UpdateGameAsync(CancellationToken cancel)
    {
        if (_lastCheck?.Latest is not { } latest)
        {
            return;
        }

        SetStatus($"Downloading {latest.Version}...", Theme.Muted);
        InstallOutcome outcome = await _service.InstallAsync(latest, ProgressReporter(), cancel);
        SetStatus(outcome switch
        {
            InstallOutcome.Activated => $"Installed {latest.Version}. The previous version is kept for rollback.",
            InstallOutcome.WaitingForGameToClose => $"{latest.Version} is installed and becomes current when OpenSCCT closes.",
            _ => $"{latest.Version} is already current.",
        }, Theme.Accent);
        await CheckAsync(cancel);

        // A new install (or a changed builder) needs its content built before Play: start it right away.
        if (outcome == InstallOutcome.Activated && !ContentIsCurrent(_store.Current())
            && ContentBuildPlan.LooksLikeOriginalInstall(_settings.OriginalGamePath))
        {
            await BuildContentAsync(cancel);
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

        SetStatus($"Downloading launcher {latest.Version}...", Theme.Muted);
        await _service.StageLauncherAsync(latest, _swap, ProgressReporter(), cancel);
        SetStatus("Launcher update downloaded and verified. Restart to finish.", Theme.Accent);
    }

    private void RestartIntoNewLauncher()
    {
        if (_swap is null)
        {
            return;
        }

        try
        {
            _swap.Swap();
            Process.Start(new ProcessStartInfo(_swap.ExePath, Program.AfterUpdateArg) { UseShellExecute = false });
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

        if (ContentState.Load(_layout.Content) is not { Ok: true })
        {
            SetStatus(string.IsNullOrEmpty(_settings.OriginalGamePath)
                ? "Set your original game install in Settings, then build the game content."
                : "Build the game content first (Build content).", Theme.Warning);
            return;
        }

        string exe = _store.ExePath(current);
        try
        {
            using Process? process = Process.Start(GameStart.Create(exe, _settings.OriginalGamePath, _layout.Content));
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

        if (MessageBox.Show(this, $"Switch back to OpenSCCT {previous}?", "Roll back", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        try
        {
            SetStatus(_service.Rollback() ? $"Switched back to {previous}." : "Nothing to roll back to.", Theme.Accent);
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
        RefreshLocal();

        // Text is part of the built content: a new language builds it again (the cache redoes only the text steps
        // and the maps, which hold their own text).
        bool rebuild = languageChanged && ContentState.Load(_layout.Content) is not null
            && ContentBuildPlan.LooksLikeOriginalInstall(_settings.OriginalGamePath);
        _ = RunAsync(async cancel =>
        {
            await CheckAsync(cancel);
            if (rebuild && !_buildingContent)
            {
                await BuildContentAsync(cancel);
            }
        });
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
                SetStatus($"Switched to {_store.Current()}.", Theme.Accent);
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
            SetStatus("Cancelled.", Theme.Muted);
        }
        catch (UpdateRejectedException ex)
        {
            SetStatus(ex.Message, Theme.Error);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException)
        {
            SetStatus(ex.Message + " A partial download resumes on the next try.", Theme.Error);
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

    private void SetStatus(string text, Color color)
    {
        _status.Text = text;
        _status.ForeColor = color;
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
