using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using OpenScct.Launcher.Editor;
using OpenScct.Launcher.GitHub;

namespace OpenScct.Launcher.App;

internal sealed partial class MainForm
{
    private const string EditorPage = "https://github.com/Lumbridge/OpenSCCT-Editor";
    private EditorService _editor = null!;
    private EditorCheck? _editorCheck;
    private bool _editorSelected;
    private bool _operationEditor;
    private bool _operationCanCancel = true;
    private bool _closeAfterWork;
    private readonly Button _gameTab = Theme.Button("Game");
    private readonly Button _editorTab = Theme.Button("Editor");
    private PictureBox _brandLogo = null!;
    private Label _brandSubtitle = null!;
    private enum StatusKind { Normal, Success, Warning, Error }
    private sealed record ProductStatus(string Text, StatusKind Kind, string? Path = null);
    private ProductStatus _gameStatus = new(string.Empty, StatusKind.Normal);
    private ProductStatus _editorStatus = new(string.Empty, StatusKind.Normal);
    private ProductStatus _operationStatus = new(string.Empty, StatusKind.Normal);
    private ThemePalette Palette => _editorSelected ? ThemePalette.Editor : ThemePalette.Game;
    private ReleaseInfo? SelectedRelease => _editorSelected ? _editorCheck?.Latest?.Release.Release : _lastCheck?.Latest?.Release;

    private Control BuildProductTabs()
    {
        _gameTab.Name = "GameTab";
        _editorTab.Name = "EditorTab";
        _gameTab.AccessibleName = "Game tab";
        _editorTab.AccessibleName = "Editor tab";
        _gameTab.Click += (_, _) => SelectProduct(false);
        _editorTab.Click += (_, _) => SelectProduct(true);
        var tabs = Theme.Flow(false, _gameTab, _editorTab);
        tabs.Name = "ProductTabs";
        tabs.Margin = new Padding(0);
        return tabs;
    }

    private void SelectProduct(bool editor)
    {
        _editorSelected = editor;
        _settings.SelectedProduct = editor ? "Editor" : "Game";
        try { _settings.Save(_layout.Settings); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetStatus("Could not remember the selected tab: " + ex.Message, Theme.Warning);
        }
        ApplyProductTheme();
        RefreshReleasePreview();
        RefreshLocal();
        if (editor && _webJoinReady && _settings.CheckOnStart && _editorCheck is null && _work is null)
            _ = RunAsync(CheckSelectedAsync);
    }

    private void ApplyProductTheme()
    {
        Theme.Apply(this, Palette);
        Theme.SetButtonEmphasis(_gameTab, !_editorSelected, Palette);
        Theme.SetButtonEmphasis(_editorTab, _editorSelected, Palette);
        if (_brandLogo is null) return;
        Image? old = _brandLogo.Image;
        using Icon icon = _editorSelected ? Theme.LoadEditorIcon(64) : Theme.LoadIcon(64);
        _brandLogo.Image = icon.ToBitmap();
        old?.Dispose();
        Icon?.Dispose();
        Icon = _editorSelected ? Theme.LoadEditorIcon(32) : Theme.LoadIcon(32);
        SetProductCopy(_editorSelected);
        _tips.SetToolTip(_website, _editorSelected ? "Visit the OpenSCCT Editor project." : "Visit the OpenSCCT website.");
    }

    private void SetProductCopy(bool editor)
    {
        _brandSubtitle.Text = editor ? "OpenSCCT Editor · Create and test your maps" : "Splinter Cell Chaos Theory Versus, rebuilt";
        _website.Text = editor ? "Editor website" : "Website";
    }

    private void RefreshEditorPanel(bool busy)
    {
        EditorInstallation? installed = _editor.Installed();
        string? prerequisite = _editor.PrerequisiteProblem();
        bool update = installed is not null && _editorCheck?.Latest is { } latest && latest.Version > installed.Version;
        bool recover = _editor.NeedsRecovery;
        _installedInfo.Text = installed is null ? "Optional map editor · Shares your installed game files" : "Editor " + installed.Version;
        _heading.Text = busy ? VisibleOperationTitle : recover ? "Recover editor update" : installed is null ? "Set up OpenSCCT Editor"
            : prerequisite is not null ? "Finish editor setup" : update ? "Editor update available" : "Ready to create";
        _launchDetail.Text = recover ? "An interrupted editor update needs recovery. Your project files are kept."
            : installed is null
            ? "Install the editor with the game and shared maps it needs. The game will not start during setup."
            : prerequisite ?? (update ? $"Editor {_editorCheck!.Latest!.Version} is ready to download." : "Create, edit, and test your maps.");
        _launchDetail.Visible = !busy && VisibleStatus.Kind is not (StatusKind.Warning or StatusKind.Error);
        _main.Text = busy ? (_operationCanCancel ? "Cancel" : "Finishing update")
            : recover ? "Recover Editor" : installed is null || prerequisite is not null ? "Set up Editor" : update ? "Update Editor" : "Open Editor";
        _main.Enabled = !busy || _operationCanCancel;
        Theme.SetButtonEmphasis(_main, !busy, Palette);
        _playCurrent.Text = "Open installed editor";
        _playCurrent.Visible = !busy && !recover && update && prerequisite is null;
        _playCurrent.Enabled = !busy;
        _check.Visible = true;
    }

    private string VisibleOperationTitle => _work is not null && _operationEditor != _editorSelected
        ? (_operationEditor ? "Editor · " : "Game · ") + _operationTitle : _operationTitle;
    private ProductStatus VisibleStatus => _work is not null ? _operationStatus : _editorSelected ? _editorStatus : _gameStatus;

    private void RenderProductStatus()
    {
        ProductStatus state = VisibleStatus;
        _status.Text = state.Text;
        _status.Visible = state.Text.Length > 0;
        _status.ForeColor = state.Kind switch
        {
            StatusKind.Success => Palette.Accent,
            StatusKind.Warning => Palette.Warning,
            StatusKind.Error => Palette.Error,
            _ => Palette.Muted,
        };
        _status.Links.Clear();
        int at = state.Path is null ? -1 : state.Text.IndexOf(state.Path, StringComparison.Ordinal);
        if (at >= 0) _status.Links.Add(at, state.Path!.Length, state.Path);
    }

    private async Task CheckSelectedAsync(CancellationToken cancel)
    {
        if (!_operationEditor)
        {
            await CheckAsync(cancel);
            // Load the other changelog too so the shared window already fits when the user switches.
            // This is only release metadata, never an editor package download.
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            try { _editorCheck = await _editor.CheckAsync(_settings.EditorIncludePrereleases, timeout.Token); }
            catch (OperationCanceledException) when (!cancel.IsCancellationRequested) { }
            catch (Exception ex) when (ex is IOException or System.Net.Http.HttpRequestException or UnauthorizedAccessException)
            { /* Optional prefetch must not replace either product's operation status. */ }
            RefreshReleasePreview();
            return;
        }
        // The launcher's own updates always come from the game repository, even for editor-only users.
        SetOperation("Checking launcher updates");
        var game = await _service.CheckAsync(_settings.IncludePrereleases, _launcherVersion, cancel);
        ShowReleaseCheck(game);
        if (game.LauncherUpdateAvailable && _swap is { HasStaged: false } swap && swap.CanWriteFolder())
        {
            try { await UpdateLauncherAsync(cancel); }
            catch (Exception ex) when (ex is IOException or System.Net.Http.HttpRequestException or UnauthorizedAccessException)
            { SetStatus("Launcher update could not be downloaded: " + ex.Message, Theme.Warning); }
        }
        await CheckEditorAsync(cancel);
    }

    private async Task CheckEditorAsync(CancellationToken cancel)
    {
        SetOperation("Checking editor updates");
        _editorCheck = await _editor.CheckAsync(_settings.EditorIncludePrereleases, cancel);
        RefreshReleasePreview();
        SetStatus(_editorCheck.Feed.Problem ?? (_editorCheck.Latest is null ? "No compatible editor release is available yet."
            : _editorCheck.UpdateAvailable ? "An editor update is available." : "Editor is up to date."),
            _editorCheck.Feed.Problem is null ? Theme.Muted : Theme.Warning);
    }

    private async Task DoEditorActionAsync()
    {
        if (_work is not null) { if (_operationCanCancel) _work.Cancel(); return; }
        if (_editor.NeedsRecovery)
        {
            await RunAsync(async cancel =>
            {
                SetOperation("Recovering editor update");
                _operationCanCancel = false;
                RefreshLocal();
                await _editor.RecoverAsync(null, cancel);
                SetStatus("Editor recovered. Your project files were kept.", Theme.Accent);
            });
            return;
        }
        if (_editor.Installed() is not null && _editor.PrerequisiteProblem() is null
            && !(_editorCheck?.Latest is { } latest && latest.Version > _editor.Installed()!.Version))
        { OpenEditor(); return; }
        await RunAsync(SetupOrUpdateEditorAsync);
    }

    private async Task SetupOrUpdateEditorAsync(CancellationToken cancel)
    {
        if (_editor.IsRunning) throw new InvalidOperationException("Save your work and close the editor and its command-line tools before setup or updating.");
        await CheckEditorAsync(cancel);
        if (_editor.Installed() is null && _editorCheck?.Latest is null)
            throw new InvalidOperationException("No launcher-compatible editor package is available. Try checking for updates later.");
        if (_editor.PrerequisiteProblem() is not null)
        {
            await EnsureEditorPrerequisitesAsync(cancel);
        }
        if (_editorCheck?.Latest is { } offer && (_editor.Installed() is not { } installed || offer.Version > installed.Version))
        {
            SetOperation("Installing editor");
            var progress = new Progress<EditorProgress>(p =>
            {
                if (IsDisposed || _work is null) return;
                _operationCanCancel = p.CanCancel;
                SetOperation(p.Phase);
                _progress.Visible = p.Total > 0;
                _progress.Fraction = p.Total > 0 ? (double)p.Received / p.Total : 0;
                _progressText.Visible = true;
                _progressText.Text = p.Total > 0 ? $"{Mb(p.Received)} of {Mb(p.Total)}" : p.Phase;
                RefreshLocal();
            });
            await _editor.InstallOrUpdateAsync(offer, progress, cancel);
        }
        // A new editor can raise its minimum game requirement independently of the installed editor.
        _operationCanCancel = true;
        RefreshLocal();
        if (_closeAfterWork) return;
        if (_editor.PrerequisiteProblem() is not null) await EnsureEditorPrerequisitesAsync(cancel);
        SetStatus("Editor is ready. Choose Open Editor to start creating.", Theme.Accent);
    }

    private async Task EnsureEditorPrerequisitesAsync(CancellationToken cancel)
    {
        await CheckAsync(cancel);
        await UpdateGameAsync(cancel);
        if (_store.Current() is null) throw new InvalidOperationException("The required game release could not be installed. Check your connection and try again.");
        // Resume the first-install map selection if its asset library was removed while shared packs remained.
        if (_editor.PrerequisiteProblem() is not null) await DownloadEditorContentAsync(cancel);
        if (_editor.PrerequisiteProblem() is { } problem) throw new InvalidOperationException(problem);
    }

    private async Task DownloadEditorContentAsync(CancellationToken cancel)
    {
        if (_editor.IsRunning) throw new InvalidOperationException("Close the editor before changing shared content.");
        SetOperation("Downloading editor map assets");
        var index = await _packs.FetchIndexAsync(cancel);
        _packIndex = index;
        foreach (var map in Content.PackedContent.MapsToFetch(index, _packs, firstDownload: true))
            await _packs.InstallMapAsync(map, Maps.ContentKind.Full, PackReporter(map.DisplayTitle), cancel);
        foreach (var pack in Content.PackedContent.SharedToFetch(index, _packs))
            await _packs.InstallSharedAsync(pack, PackReporter(pack.Title), cancel);
    }

    private void OpenEditor()
    {
        if (_work is not null) return;
        try
        {
            if (_editor.PrerequisiteProblem() is { } problem) throw new InvalidOperationException(problem);
            using Process? process = Process.Start(_editor.StartInfo());
            if (process is null) throw new IOException("The editor process did not start.");
            SetStatus("Started OpenSCCT Editor.", Theme.Accent);
        }
        catch (Exception ex) when (ex is IOException or Win32Exception or InvalidOperationException)
        { SetStatus("Could not start the editor: " + ex.Message, Theme.Error); }
    }

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        if (_editor is not null && _mainLayout is not null && !IsDisposed) RefreshLocal();
    }
}
