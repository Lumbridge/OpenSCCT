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
using OpenScct.Launcher.Install;

namespace OpenScct.Launcher.App;

internal sealed partial class MainForm
{
    private const string EditorPage = "https://github.com/Lumbridge/OpenSCCT-Editor";
    private EditorService _editor = null!;
    private EditorCheck? _editorCheck;
    /// <summary>The product tabs; each has its own panel copy, palette, status and release check.</summary>
    private enum Product { Game, Editor, Unreal }
    private Product _selected;
    private Product _operationProduct;          // which product owns the running _work
    private bool _operationCanCancel = true;
    private bool _closeAfterWork;
    private bool _editorSetupFailed;
    private bool SelectedCheckOnStart => _selected switch
    {
        Product.Editor => _settings.EditorCheckOnStart,
        Product.Unreal => _settings.UnrealCheckOnStart,
        _ => _settings.CheckOnStart,
    };
    private readonly Button _gameTab = Theme.Button("Game");
    private readonly Button _editorTab = Theme.Button("Editor");
    private PictureBox _brandLogo = null!;
    private Label _brandSubtitle = null!;
    private enum StatusKind { Normal, Success, Warning, Error }
    private sealed record ProductStatus(string Text, StatusKind Kind, string? Path = null, bool CanForceClose = false);
    private ProductStatus _gameStatus = new(string.Empty, StatusKind.Normal);
    private ProductStatus _editorStatus = new(string.Empty, StatusKind.Normal);
    private ProductStatus _operationStatus = new(string.Empty, StatusKind.Normal);
    private ThemePalette Palette => _selected switch
    {
        Product.Editor => ThemePalette.Editor,
        Product.Unreal => ThemePalette.Unreal,
        _ => ThemePalette.Game,
    };
    private ReleaseInfo? SelectedRelease => _selected switch
    {
        Product.Editor => _editorCheck?.Latest?.Release.Release,
        Product.Unreal => _unrealCheck?.Latest?.Release,
        _ => _lastCheck?.Latest?.Release,
    };

    /// <summary>Whether the product's release feed was read in this session, so its notes can be shown.</summary>
    private bool HasChecked(Product product) => product switch
    {
        Product.Editor => _editorCheck is not null,
        Product.Unreal => _unrealCheck is not null,
        _ => _lastCheck is not null,
    };

    /// <summary>The saved tab; an unknown value opens the Game tab.</summary>
    private static Product ParseProduct(string? name) => name switch
    {
        "Editor" => Product.Editor,
        "Unreal" => Product.Unreal,
        _ => Product.Game,
    };

    private Control BuildProductTabs()
    {
        _gameTab.Name = "GameTab";
        _editorTab.Name = "EditorTab";
        _unrealTab.Name = "UnrealTab";
        _gameTab.AccessibleName = "Game tab";
        _editorTab.AccessibleName = "Editor tab";
        _unrealTab.AccessibleName = "Unreal tab";
        _gameTab.Click += (_, _) => SelectProduct(Product.Game);
        _editorTab.Click += (_, _) => SelectProduct(Product.Editor);
        _unrealTab.Click += (_, _) => SelectProduct(Product.Unreal);
        // Compact tabs leave the brand subtitle room for one line at the launcher's minimum width.
        foreach (Button tab in new[] { _gameTab, _editorTab, _unrealTab })
        {
            tab.Padding = new Padding(6, 5, 6, 5);
            tab.Margin = new Padding(0, 0, 4, 0);
        }
        _unrealTab.Margin = new Padding(0);
        var tabs = Theme.Flow(false, _gameTab, _editorTab, _unrealTab);
        tabs.Name = "ProductTabs";
        tabs.Margin = new Padding(0);
        return tabs;
    }

    // Kept for Web Join and the smoke harness, which choose between the Game and Editor tabs.
    private void SelectProduct(bool editor) => SelectProduct(editor ? Product.Editor : Product.Game);

    private void SelectProduct(Product product)
    {
        _selected = product;
        _settings.SelectedProduct = product.ToString();
        try { _settings.Save(_layout.Settings); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetStatus("Could not remember the selected tab: " + ex.Message, Theme.Warning);
        }
        ApplyProductTheme();
        RefreshReleasePreview();
        RefreshLocal();
        if (_webJoinReady && SelectedCheckOnStart && !HasChecked(product) && _work is null)
            _ = RunAsync(CheckSelectedAsync);
    }

    private void ApplyProductTheme()
    {
        Theme.Apply(this, Palette);
        Theme.SetButtonEmphasis(_gameTab, _selected == Product.Game, Palette);
        Theme.SetButtonEmphasis(_editorTab, _selected == Product.Editor, Palette);
        Theme.SetButtonEmphasis(_unrealTab, _selected == Product.Unreal, Palette);
        if (_brandLogo is null) return;
        // The Unreal build has no icon of its own yet: its logo is the game's in the Unreal accent, and the
        // window keeps the game's icon.
        bool editor = _selected == Product.Editor;
        Image? old = _brandLogo.Image;
        if (_selected == Product.Unreal)
        {
            _brandLogo.Image = Theme.LoadTintedLogo(64, ThemePalette.Unreal.Accent);
        }
        else
        {
            using Icon icon = editor ? Theme.LoadEditorIcon(64) : Theme.LoadIcon(64);
            _brandLogo.Image = icon.ToBitmap();
        }
        old?.Dispose();
        Icon?.Dispose();
        Icon = editor ? Theme.LoadEditorIcon(32) : Theme.LoadIcon(32);
        SetProductCopy(_selected);
        _tips.SetToolTip(_website, _selected switch
        {
            Product.Editor => "Visit the OpenSCCT Editor project.",
            Product.Unreal => "See the OpenSCCT Unreal releases.",
            _ => "Visit the OpenSCCT website.",
        });
        _settingsButton.AccessibleName = _selected switch
        {
            Product.Editor => "Editor settings",
            Product.Unreal => "Unreal settings",
            _ => "Game settings",
        };
        _tips.SetToolTip(_settingsButton, _settingsButton.AccessibleName);
    }

    private void SetProductCopy(Product product)
    {
        (_brandSubtitle.Text, _website.Text) = product switch
        {
            Product.Editor => ("OpenSCCT Editor · Create and test your maps", "Editor website"),
            Product.Unreal => ("OpenSCCT Unreal · Unreal Engine build", "Unreal releases"),
            _ => ("Splinter Cell Chaos Theory Versus, rebuilt", "Website"),
        };
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
        _main.Text = busy ? (_operationCanCancel ? "Cancel" : "Please wait")
            : recover ? "Recover Editor" : installed is null || prerequisite is not null ? "Set up Editor"
            : _editorSetupFailed ? "Retry update" : update ? "Update Editor" : "Open Editor";
        _main.Enabled = !busy || _operationCanCancel;
        Theme.SetButtonEmphasis(_main, !busy, Palette);
        _playCurrent.Text = "Use current version";
        _playCurrent.Visible = !busy && !recover && installed is not null && (update || _editorSetupFailed) && prerequisite is null;
        _playCurrent.Enabled = !busy;
        _check.Visible = true;
    }

    private string VisibleOperationTitle => _work is not null && _operationProduct != _selected
        ? _operationProduct + " · " + _operationTitle : _operationTitle;
    private ProductStatus VisibleStatus => _work is not null ? _operationStatus : _selected switch
    {
        Product.Editor => _editorStatus,
        Product.Unreal => _unrealStatus,
        _ => _gameStatus,
    };

    private void RenderProductStatus()
    {
        ProductStatus state = VisibleStatus;
        _status.Text = state.Text;
        _status.Visible = state.Text.Length > 0;
        _forceClose.Text = _selected == Product.Game ? "Force close all…" : "Force close and retry…";
        _forceClose.Visible = _work is null && state.CanForceClose;
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
        if (_operationProduct == Product.Game)
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
        await CheckLauncherAsync(cancel);
        if (_operationProduct == Product.Unreal) await CheckUnrealAsync(cancel);
        else await CheckEditorAsync(cancel);
    }

    /// <summary>The launcher's own updates always come from the game repository, even for editor-only or Unreal-only users.</summary>
    private async Task CheckLauncherAsync(CancellationToken cancel)
    {
        SetOperation("Checking launcher updates");
        var game = await _service.CheckAsync(_settings.IncludePrereleases, _launcherVersion, cancel);
        ShowReleaseCheck(game);
        if (game.LauncherUpdateAvailable && _swap is { HasStaged: false } swap && swap.CanWriteFolder())
        {
            try { await UpdateLauncherAsync(cancel); }
            catch (Exception ex) when (ex is IOException or System.Net.Http.HttpRequestException or UnauthorizedAccessException)
            { SetStatus("Launcher update could not be downloaded: " + ex.Message, Theme.Warning); }
        }
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
        if (!_editorSetupFailed && _editor.Installed() is not null && _editor.PrerequisiteProblem() is null
            && !(_editorCheck?.Latest is { } latest && latest.Version > _editor.Installed()!.Version))
        { OpenEditor(); return; }
        await RunAsync(SetupOrUpdateEditorAsync);
    }

    private async Task SetupOrUpdateEditorAsync(CancellationToken cancel)
    {
        try
        {
            await SetupOrUpdateEditorCoreAsync(cancel);
            _editorSetupFailed = false;
        }
        catch (Exception ex) when ((ex is InvalidOperationException or IOException) && _editor.IsRunning)
        {
            _editorSetupFailed = true;
            if (_editor.UpdateInProgress)
                SetStatus("Wait for the editor updater to finish before trying again.", Theme.Warning);
            else
                SetStatus("Save your work and close the editor and its command-line tools, then retry. Downloads will resume where they stopped.",
                    Theme.Error, canForceClose: true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _editorSetupFailed = true;
            throw;
        }
    }

    private async Task ForceCloseAndUpdateEditorAsync()
    {
        if (_work is not null || !VisibleStatus.CanForceClose) return;
        if (MessageBox.Show(this,
            "Force close the installed editor and its command-line tools, then retry editor setup or update?\n\nUnsaved work will be lost.",
            "Force close OpenSCCT Editor", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.OK)
            return;

        await RunAsync(async cancel =>
        {
            _operationCanCancel = false;
            SetOperation("Closing editor processes");
            RefreshLocal();
            try { await InstallReset.ForceCloseEditorProcessesAsync(_layout); }
            catch (Win32Exception ex)
            {
                SetStatus("Could not check running editor processes: " + ex.Message, Theme.Warning, canForceClose: true);
                return;
            }
            finally { _operationCanCancel = true; }
            RefreshLocal();
            await SetupOrUpdateEditorAsync(cancel);
        });
    }

    private async Task SetupOrUpdateEditorCoreAsync(CancellationToken cancel)
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

    private bool EditorUsesSharedContent() => _editor.SharedContentInUse(EditorLiveBridge.IsLiveMcpProcess);

    private async Task DownloadEditorContentAsync(CancellationToken cancel)
    {
        if (EditorUsesSharedContent()) throw new InvalidOperationException("Close the editor before changing shared content.");
        SetOperation("Downloading editor map assets");
        var index = await _packs.FetchIndexAsync(cancel);
        _packIndex = index;
        foreach (var map in Content.PackedContent.MapsToFetch(index, _packs, firstDownload: true))
            await _packs.InstallMapAsync(map, Maps.ContentKind.Full, PackReporter(map.DisplayTitle), cancel);
        foreach (var pack in Content.PackedContent.SharedToFetch(index, _packs))
            await _packs.InstallSharedAsync(pack, PackReporter(pack.Title), cancel);
    }

    private async Task UninstallEditorAsync(CancellationToken cancel)
    {
        SetOperation("Uninstalling editor");
        var progress = new Progress<EditorProgress>(p =>
        {
            if (IsDisposed || _work is null) return;
            _operationCanCancel = p.CanCancel;
            SetOperation(p.Phase);
            RefreshLocal();
        });
        try
        {
            EditorUninstallResult result = await _editor.UninstallAsync(progress, cancel);
            _editorCheck = null;
            string status = result.Failures.Count > 0
                ? "Editor removed from the launcher, but some files could not be deleted."
                : "Editor uninstalled. Your projects, settings, game and shared assets were kept.";
            if (result.Failures.Count > 0) status += " " + result.Failures[0];
            if (result.RetainedPath is { } kept) status += " Files kept in " + kept;
            SetStatus(status, result.Failures.Count > 0 ? Theme.Warning : Theme.Accent, result.RetainedPath);
        }
        catch (OperationCanceledException)
        {
            SetStatus("Editor uninstall cancelled.", Theme.Muted);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException)
        {
            SetStatus("Could not uninstall editor: " + ex.Message, Theme.Error);
        }
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
        if (_editor is null || _unreal is null || _mainLayout is null || IsDisposed) return;
        if (_work is null) FinishUnrealSwitch();
        RefreshLocal();
    }
}
