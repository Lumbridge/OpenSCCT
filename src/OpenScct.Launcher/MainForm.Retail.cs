using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using OpenScct.Launcher.Retail;

namespace OpenScct.Launcher.App;

internal sealed partial class MainForm
{
    private Lazy<RetailInstallation?> _steamInstall = new(RetailGame.FindSteam);
    private RetailInstallation? SteamInstallation => _discoverRetail ? _steamInstall.Value : null;
    private RetailInstallation? RetailBase => RetailGame.Base(_settings.Retail, SteamInstallation);
    private RetailInstallation? RetailCampaign => RetailGame.Campaign(_settings.Retail, SteamInstallation);
    private RetailInstallation? RetailVersus => RetailGame.Selected(_settings.Retail, SteamInstallation);
    private RetailInstallation? ActiveMod => _settings.Retail.UseMod ? RetailGame.Inspect(_settings.Retail.ModFolder) : null;
    private bool PrimaryIsVersus => _settings.Retail.UseMod && ActiveMod?.HasVersus != false || RetailCampaign?.CampaignExecutable is null;

    private string RetailStatus
    {
        get
        {
            var game = RetailBase;
            if (_settings.Retail.UseMod)
            {
                var mod = ActiveMod;
                if (mod is null) return "Mod folder missing";
                if (mod.CampaignExecutable is null) return "Modded Versus ready";
                return mod.HasVersus ? "Modded campaign, co-op and Versus ready" : "Modded campaign and co-op ready";
            }
            return (game?.CampaignExecutable is not null, game?.HasVersus == true) switch
            {
                (true, true) => "Campaign, co-op and Versus ready",
                (true, false) => "Campaign and co-op ready",
                (false, true) => "Versus ready",
                _ => "Choose your installation",
            };
        }
    }

    private string RetailActionText
    {
        get
        {
            if (!PrimaryIsVersus) return CampaignActionText;
            if (RetailVersus?.HasVersus == true) return ActiveMod?.HasVersus == true ? "Play modded Versus" : "Play Versus";
            return _settings.Retail.UseMod ? "Locate mod folder" : "Choose game folder";
        }
    }

    private string RetailSecondaryActionText
    {
        get
        {
            if (PrimaryIsVersus) return RetailCampaign?.CampaignExecutable is not null ? CampaignActionText : string.Empty;
            return RetailVersus?.HasVersus == true ? "Play Versus" : string.Empty;
        }
    }

    private string CampaignActionText => ActiveMod?.CampaignExecutable is not null ? "Play modded campaign / co-op" : "Play campaign / co-op";

    private void RefreshRetail() => _steamInstall = new(RetailGame.FindSteam);

    private void RetailAction()
    {
        if (!PrimaryIsVersus || RetailVersus?.HasVersus == true)
            StartRetail(versus: PrimaryIsVersus);
        else if (_settings.Retail.UseMod)
            ChooseRetailMod();
        else
            ChooseRetailFolder();
    }

    private void RetailSecondaryAction() => StartRetail(versus: !PrimaryIsVersus);

    private void StartRetail(bool versus)
    {
        try
        {
            var game = versus ? RetailVersus : RetailCampaign;
            if (game is null) throw new InvalidOperationException("Choose the installation folder again.");
            var info = versus ? RetailGame.CreateStart(game) : RetailGame.CreateCampaignStart(game, SteamInstallation);
            Process.Start(info)?.Dispose();
            SetStatus(versus ? "Started Versus." : "Started campaign / co-op.", Theme.Accent);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception or InvalidOperationException)
        {
            SetStatus("Could not launch Chaos Theory. " + ex.Message, Theme.Error);
        }
    }

    private void ChooseRetailFolder() => ChooseRetailFolder(mod: false);
    private void ChooseRetailMod() => ChooseRetailFolder(mod: true);

    private void ChooseRetailFolder(bool mod)
    {
        using var picker = new FolderBrowserDialog
        {
            UseDescriptionForTitle = true,
            Description = mod ? "Choose your separate modded Chaos Theory installation" : "Choose your Chaos Theory game folder",
            ShowNewFolderButton = false,
            InitialDirectory = mod ? _settings.Retail.ModFolder ?? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
                : RetailBase?.Folder ?? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        };
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        SaveRetailProfile(profiles =>
        {
            if (mod) RetailGame.SetMod(profiles, picker.SelectedPath, SteamInstallation);
            else RetailGame.SetBase(profiles, picker.SelectedPath);
        });
    }

    private void SaveRetailProfile(Action<RetailProfiles> edit)
    {
        var previous = _settings.Retail;
        try
        {
            var updated = previous with { };
            edit(updated);
            _settings.Retail = updated;
            _settings.Save(_layout.Settings);
            RefreshRetail();
            _detailsKey = null;
            RefreshLocal();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            _settings.Retail = previous;
            MessageBox.Show(this, ex.Message, "PC / Steam", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private Control BuildRetailMods()
    {
        var grid = Theme.Grid(Theme.Fill);
        var active = Theme.Wrapping(Theme.Label(string.Empty, Theme.Heading));
        var location = Theme.Wrapping(Theme.Label(string.Empty, Theme.Small, Theme.Muted));
        location.UseMnemonic = false;
        var toggle = Theme.Button("Use mod");
        var browse = Theme.Button("Choose mod folder");
        var forget = Theme.QuietButton("Forget mod folder", onPanel: true);
        var suggested = _discoverRetail ? FindDesktopEnhanced() : null;
        var useDetected = Theme.Button("Use detected Enhanced install");
        var source = Theme.QuietButton("Enhanced releases", onPanel: true);
        var reloaded = Theme.QuietButton("Reloaded setup", onPanel: true);

        var profiles = _settings.Retail;
        var mod = RetailGame.Inspect(profiles.ModFolder);
        active.Text = profiles.UseMod ? "Modded installation selected" : "Base installation selected";
        location.Text = profiles.ModFolder ?? suggested?.Folder ?? "Choose the game folder containing your mod.";
        toggle.Text = profiles.UseMod ? "Use base game" : "Use modded copy";
        toggle.Visible = profiles.UseMod || mod is not null;
        forget.Visible = profiles.ModFolder is not null;
        useDetected.Visible = suggested is not null && profiles.ModFolder is null;

        browse.Click += (_, _) => ChooseRetailMod();
        toggle.Click += (_, _) => SaveRetailProfile(profile => profile.UseMod = !profile.UseMod);
        forget.Click += (_, _) => SaveRetailProfile(profile => { profile.UseMod = false; profile.ModFolder = null; });
        useDetected.Click += (_, _) =>
        {
            if (suggested is not null) SaveRetailProfile(profile => RetailGame.SetMod(profile, suggested.Folder, SteamInstallation));
        };
        source.Click += (_, _) => OpenLink(RetailGame.EnhancedWebsite + "/releases");
        reloaded.Click += (_, _) => OpenLink(RetailGame.ReloadedWebsite);

        Theme.AddRow(grid, active);
        Theme.AddRow(grid, Theme.Wrapping(Theme.Label("Choose a separate copy with campaign, co-op or Versus mods. Enhanced includes Reloaded and changes Versus only.")));
        Theme.AddRow(grid, location);
        var actions = Theme.Flow(false, toggle, browse, useDetected, forget);
        actions.WrapContents = true;
        actions.Dock = DockStyle.Fill;
        Theme.AddRow(grid, actions);
        Theme.AddRow(grid, Theme.Flow(false, source, reloaded));
        return grid;
    }

    private static RetailInstallation? FindDesktopEnhanced()
    {
        string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (!Directory.Exists(desktop)) return null;
        try
        {
            // When more than one version is present, let the player pick the folder.
            var candidates = Directory.EnumerateDirectories(desktop, "Enhanced SCCT Versus*")
                .Select(RetailGame.Inspect).OfType<RetailInstallation>().Where(game => game.HasReloaded).Take(2).ToArray();
            return candidates.Length == 1 ? candidates[0] : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }
}
