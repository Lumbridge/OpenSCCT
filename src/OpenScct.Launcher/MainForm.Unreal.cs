using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using OpenScct.Launcher.Unreal;
using OpenScct.Launcher.Content;
using OpenScct.Launcher.GitHub;
using OpenScct.Launcher.Update;
using OpenScct.Launcher.Versioning;

namespace OpenScct.Launcher.App;

/// <summary>
/// The Unreal tab: installs, updates and starts the packaged Unreal Engine build of OpenSCCT. The game reads the
/// local Steam conversion and can load additional maps.
/// </summary>
internal sealed partial class MainForm
{
    private UnrealService _unreal = null!;
    private UnrealCheck? _unrealCheck;
    private bool _unrealSetupFailed;
    private ProductStatus _unrealStatus = new(string.Empty, StatusKind.Normal);
    private readonly LibraryEntry _unrealTab = new LibraryEntry("UE", "OpenSCCT Unreal", "Versus remake");

    internal void ShowEdition(string name)
    {
        if (name != "Unreal" || IsDisposed || Disposing) return;
        if (InvokeRequired)
        {
            try { BeginInvoke(() => ShowEdition(name)); } catch (InvalidOperationException) { }
            return;
        }
        _page = LibraryPage.Games;
        SelectProduct(Product.Unreal);
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        if (Visible) Activate();
    }

    private bool SteamBuilderUpdateAvailable()
    {
        if (_steamContentRoot is null || _steamSource.Value is null
            || !SemVersion.TryParse(ContentState.Load(_steamContentRoot)?.Version, out var built)) return false;
        var current = _store.Current();
        var offered = _lastCheck is { Feed.Source: ReleaseFeedSource.Network or ReleaseFeedSource.NotModified, Latest: { } latest } ? latest.Version : null;
        return current > built || offered > built;
    }

    private async Task RebuildWithLatestBuilderAsync(CancellationToken cancel)
    {
        if (_lastCheck?.Latest is { } latest && GodotSteamContent.SupportsClient(latest.Version)
            && (_store.Current() is not { } current || current < latest.Version))
            await UpdateGameBinaryAsync(cancel);
        if (_store.Pending() is not null)
            throw new InvalidOperationException("Close the running game to finish updating the Steam content builder.");
        await BuildSteamContentAsync(cancel);
    }

    private void RefreshUnrealPanel(bool busy)
    {
        UnrealInstallation? installed = _unreal.Installed();
        string? prerequisite = _unreal.PrerequisiteProblem();
        SemVersion? pending = _unreal.Pending;
        VersionedRelease? latest = _unrealCheck?.Latest;
        bool update = installed is not null && latest is not null && latest.Version > (pending ?? installed.Version);
        // Only a feed read without a problem shows that nothing is published; a failed read keeps Install.
        bool unreleased = installed is null && _unrealCheck is { Latest: null, Feed.Problem: null };
        // An installed update waits for the running game to close; starting another copy cannot work until then.
        bool waiting = pending is not null && _unreal.IsRunning;
        bool rebuild = !busy && installed is not null && prerequisite is null && SteamBuilderUpdateAvailable();

        _installedInfo.Text = installed is null ? "Unreal Engine build · not installed"
            : "OpenSCCT Unreal " + installed.Version + (installed.OpenscctVersion is { Length: > 0 } core ? " · OpenSCCT " + core : string.Empty);

        if (busy)
        {
            _heading.Text = VisibleOperationTitle;
        }
        else if (unreleased)
        {
            _heading.Text = "Not released yet";
            _launchDetail.Text = "Check back later.";
        }
        else if (waiting && !update)
        {
            _heading.Text = "Update ready";
            _launchDetail.Text = "Close OpenSCCT Unreal to finish the update.";
        }
        else if (NeedsSteamSource)
        {
            _heading.Text = "Steam installation needed";
            _launchDetail.Text = "Choose your Steam copy of Chaos Theory to build the original Versus maps and assets.";
        }
        else if (installed is null)
        {
            _heading.Text = "Set up OpenSCCT Unreal";
            _launchDetail.Text = "Early build of OpenSCCT on Unreal Engine. Windows only.";
        }
        else if (update)
        {
            _heading.Text = "Unreal update available";
            _launchDetail.Text = $"OpenSCCT Unreal {latest!.Version} is ready to download.";
        }
        else if (!UnrealService.SupportsSteamContent(installed.Version))
        {
            _heading.Text = "Unreal update required";
            _launchDetail.Text = $"Check for version {UnrealService.MinimumSteamVersion} or later to use your Steam game files.";
        }
        else if (prerequisite is not null)
        {
            _heading.Text = "Steam files needed";
            _launchDetail.Text = prerequisite;
        }
        else if (rebuild)
        {
            _heading.Text = "Steam content update";
            _launchDetail.Text = "Rebuild your game files from Steam with the newer content builder.";
        }
        else
        {
            _heading.Text = "Ready to play";
            _launchDetail.Text = _unrealCheck is null ? "Use Check for updates to look for a newer build." : "You're up to date.";
        }
        _launchDetail.Visible = !busy && VisibleStatus.Kind is not (StatusKind.Warning or StatusKind.Error);

        _main.Text = busy ? (_operationCanCancel ? "Cancel" : "Please wait")
            : unreleased ? "Check again"
            : waiting && !update ? "Waiting for game to close"
            : NeedsSteamSource ? "Choose Steam folder"
            : installed is null ? "Install Unreal"
            : _unrealSetupFailed ? "Retry update"
            : update ? "Update Unreal"
            : !UnrealService.SupportsSteamContent(installed.Version) ? "Check for updates"
            : prerequisite is not null ? "Build game files"
            : rebuild ? "Update Steam files"
            : "Play";
        bool waitingAction = !busy && !update && waiting;
        _main.Enabled = busy ? _operationCanCancel : !waitingAction;
        Theme.SetButtonEmphasis(_main, !busy && !waitingAction, Palette);
        _tips.SetToolTip(_main, null);
        _playCurrent.Text = "Use current version";
        _playCurrent.Visible = !busy && installed is not null && (update || rebuild || _unrealSetupFailed) && UnrealService.SupportsSteamContent(installed.Version) && prerequisite is null;
        _playCurrent.Enabled = !busy;
        _check.Visible = true;
    }

    private async Task CheckUnrealAsync(CancellationToken cancel)
    {
        SetOperation("Checking OpenSCCT Unreal updates");
        _unrealCheck = await _unreal.CheckAsync(_settings.UnrealIncludePrereleases, cancel);
        RefreshReleasePreview();
        SetStatus(_unrealCheck.Feed.Problem ?? (_unrealCheck.Latest is null ? UnrealService.NoReleaseMessage
            : _unrealCheck.UpdateAvailable ? "An OpenSCCT Unreal update is available." : _unrealCheck.Installed is null ? string.Empty
            : "OpenSCCT Unreal is up to date."),
            _unrealCheck.Feed.Problem is null ? Theme.Muted : Theme.Warning);
    }

    private async Task DoUnrealActionAsync()
    {
        if (_work is not null) { if (_operationCanCancel) _work.Cancel(); return; }
        UnrealInstallation? installed = _unreal.Installed();
        if (installed is null && _unrealCheck is { Latest: null, Feed.Problem: null })
        {
            await RunAsync(CheckSelectedAsync);
            return;
        }
        if (NeedsSteamSource) { ChooseSteamSource(); return; }
        bool update = installed is not null && _unrealCheck?.Latest is { } latest && latest.Version > (_unreal.Pending ?? installed.Version);
        if (installed is null || update || _unrealSetupFailed)
        {
            await RunAsync(SetupOrUpdateUnrealAsync);
            return;
        }
        if (!UnrealService.SupportsSteamContent(installed.Version))
        {
            await RunAsync(CheckSelectedAsync);
            return;
        }
        if (_unreal.PrerequisiteProblem() is not null)
        {
            await RunAsync(cancel => EnsureSteamContentAsync(cancel));
            return;
        }
        if (_unreal.Pending is not null && _unreal.IsRunning)
        {
            // The update becomes current once the game closes (the pending timer switches it); nothing to start yet.
            SetStatus("Close OpenSCCT Unreal to finish the update.", Theme.Muted);
            return;
        }
        if (SteamBuilderUpdateAvailable()) await RunAsync(RebuildWithLatestBuilderAsync);
        else await OpenUnrealAsync();
    }

    private async Task SetupOrUpdateUnrealAsync(CancellationToken cancel)
    {
        try
        {
            await SetupOrUpdateUnrealCoreAsync(cancel);
            if (!_closeAfterWork && _unreal.Installed() is { } installed && UnrealService.SupportsSteamContent(installed.Version)
                && !_unreal.IsRunning && _unreal.Pending is null)
            {
                if (_unreal.PrerequisiteProblem() is not null) await EnsureSteamContentAsync(cancel);
                else if (SteamBuilderUpdateAvailable()) await RebuildWithLatestBuilderAsync(cancel);
            }
            _unrealSetupFailed = false;
        }
        catch (Exception ex) when ((ex is InvalidOperationException or IOException) && _unreal.IsRunning)
        {
            _unrealSetupFailed = true;
            SetStatus("Close OpenSCCT Unreal, then retry. Downloads resume where they stopped.", Theme.Error, canForceClose: true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _unrealSetupFailed = true;
            throw;
        }
    }

    private async Task SetupOrUpdateUnrealCoreAsync(CancellationToken cancel)
    {
        await CheckUnrealAsync(cancel);
        UnrealInstallation? installed = _unreal.Installed();
        if (_unrealCheck?.Latest is not { } latest)
        {
            if (installed is null) throw new InvalidOperationException(UnrealService.NoReleaseMessage);
            return;
        }
        if (!UnrealService.SupportsSteamContent(latest.Version))
            throw new InvalidOperationException($"OpenSCCT Unreal {UnrealService.MinimumSteamVersion} or later is required for Steam assets.");
        if (installed is not null && latest.Version <= (_unreal.Pending ?? installed.Version)) return;

        SetOperation("Installing OpenSCCT Unreal");
        SetStatus($"Downloading OpenSCCT Unreal {latest.Version}...", Theme.Muted);
        var progress = new Progress<UnrealProgress>(p =>
        {
            if (IsDisposed || _work is null) return;
            _operationCanCancel = p.CanCancel;
            SetOperation(p.Phase);
            _progress.Visible = p.Total > 0;
            _progress.Fraction = p.Total > 0 ? (double)p.Received / p.Total : 0;
            _progressText.Visible = true;
            string rate = p.BytesPerSecond > 0 ? $"  {Mb(p.BytesPerSecond)}/s" : string.Empty;
            _progressText.Text = p.Total > 0 ? $"{Mb(p.Received)} of {Mb(p.Total)}{rate}" : p.Phase;
            RefreshLocal();
        });
        UnrealInstallResult result = await _unreal.InstallAsync(latest, progress, cancel);
        _operationCanCancel = true;
        RefreshLocal();
        if (_closeAfterWork) return;
        string downloaded = result.DownloadedBytes is long bytes && bytes > 0 ? $" Downloaded {Mb(bytes)}." : string.Empty;
        SetStatus(result.Outcome switch
        {
            UnrealInstallOutcome.Installed => $"OpenSCCT Unreal {result.Version} is installed.{downloaded}",
            UnrealInstallOutcome.WaitingForGameToClose => $"OpenSCCT Unreal {result.Version} is ready and starts once you close the game.",
            _ => $"OpenSCCT Unreal {result.Version} is already installed.",
        }, Theme.Accent);
    }

    private async Task ForceCloseAndUpdateUnrealAsync()
    {
        if (_work is not null || !VisibleStatus.CanForceClose) return;
        if (MessageBox.Show(this,
            "Force close OpenSCCT Unreal, then retry the update?\n\nProgress in a running match will be lost.",
            "Force close OpenSCCT Unreal", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.OK)
            return;

        await RunAsync(async cancel =>
        {
            _operationCanCancel = false;
            SetOperation("Closing OpenSCCT Unreal");
            RefreshLocal();
            try { await _unreal.ForceCloseAsync(cancel); }
            catch (Win32Exception ex)
            {
                SetStatus("Could not check whether OpenSCCT Unreal is running: " + ex.Message, Theme.Warning, canForceClose: true);
                return;
            }
            finally { _operationCanCancel = true; }
            FinishUnrealSwitch();
            RefreshLocal();
            await SetupOrUpdateUnrealAsync(cancel);
        });
    }

    private async Task UninstallUnrealAsync()
    {
        if (_work is not null) return;
        if (_unreal.IsRunning)
        {
            SetStatus("Close OpenSCCT Unreal before uninstalling it.", Theme.Warning);
            return;
        }
        if (MessageBox.Show(this,
            "Uninstall OpenSCCT Unreal?\n\nThe OpenSCCT game, its maps, the editor and your game settings are kept.",
            "Uninstall OpenSCCT Unreal", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.OK)
            return;
        bool removeMapPacks = MessageBox.Show(this,
            "Also remove the map packs OpenSCCT Unreal downloaded?\n\nIf you keep them, they are used again when you reinstall.",
            "Uninstall OpenSCCT Unreal", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes;

        await RunAsync(async cancel =>
        {
            SetOperation("Uninstalling OpenSCCT Unreal");
            var progress = new Progress<UnrealProgress>(p =>
            {
                if (IsDisposed || _work is null) return;
                _operationCanCancel = p.CanCancel;
                SetOperation(p.Phase);
                RefreshLocal();
            });
            UnrealUninstallResult result = await _unreal.UninstallAsync(removeMapPacks, progress, cancel);
            _unrealSetupFailed = false;
            string status = result.Failures.Count > 0
                ? "OpenSCCT Unreal was removed, but some files are in use and were kept: " + result.Failures[0]
                : removeMapPacks ? "OpenSCCT Unreal and its map packs were uninstalled. The OpenSCCT game was kept."
                : "OpenSCCT Unreal was uninstalled. Its map packs and the OpenSCCT game were kept.";
            SetStatus(status, result.Failures.Count > 0 ? Theme.Warning : Theme.Accent);
        });
    }

    /// <summary>Activates an installed update once OpenSCCT Unreal has closed.</summary>
    private void FinishUnrealSwitch()
    {
        try
        {
            if (_unreal.Pending is null) return;
            if (_unreal.FinishPendingSwitch())
                _unrealStatus = new($"Now on OpenSCCT Unreal {_unreal.Installed()?.Version}.", StatusKind.Success);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _unrealStatus = new("Could not switch OpenSCCT Unreal versions yet: " + ex.Message, StatusKind.Warning);
        }
    }

    private async Task OpenUnrealAsync()
    {
        if (_work is not null) return;
        FinishUnrealSwitch();
        if (_unreal.VcRuntimeMissing() && _unreal.VcRuntimeInstaller() is { } installer)
        {
            if (MessageBox.Show(this, "OpenSCCT Unreal needs the Microsoft Visual C++ runtime. Install it now?",
                "OpenSCCT Unreal", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                SetStatus("OpenSCCT Unreal needs the Microsoft Visual C++ runtime to start.", Theme.Warning);
                return;
            }
            bool installed = false;
            await RunAsync(async cancel =>
            {
                _operationCanCancel = false;
                SetOperation("Installing the Visual C++ runtime");
                RefreshLocal();
                try
                {
                    using Process? process = Process.Start(installer);
                    if (process is not null) await process.WaitForExitAsync(cancel);
                    installed = true;
                }
                catch (Win32Exception ex)
                {
                    // Also raised when the Windows permission prompt is declined.
                    SetStatus("The Visual C++ runtime was not installed: " + ex.Message, Theme.Warning);
                }
            });
            if (!installed || IsDisposed || _closeAfterWork) return;
        }

        try
        {
            using Process? game = Process.Start(_unreal.StartInfo());
            if (game is null) throw new IOException("OpenSCCT Unreal did not start.");
            SetStatus("Started OpenSCCT Unreal.", Theme.Accent);
            WindowState = FormWindowState.Minimized;
        }
        catch (Exception ex) when (ex is IOException or Win32Exception or InvalidOperationException)
        {
            SetStatus("Could not start OpenSCCT Unreal: " + ex.Message, Theme.Error);
        }
    }
}
