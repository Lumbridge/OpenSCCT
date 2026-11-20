using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using OpenScct.Launcher.Ps2;
using OpenScct.Launcher.Retail;
using OpenScct.Project;

namespace OpenScct.Launcher.Linux;

internal sealed partial class MainWindow
{
    private static readonly (string Id, string Title, string Group)[] Editions =
    [
        ("Ps2", "PlayStation 2", "Originals"), ("Retail", "PC / Steam", "Originals"),
        ("Game", "OpenSCCT Godot", "Modernisations"), ("Unreal", "OpenSCCT Unreal", "Modernisations"),
    ];

    private readonly StackPanel _godotPage;
    private readonly ContentControl _page = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private readonly TextBlock _feedback = Ui.Text(string.Empty, 14);
    private readonly StackPanel _sidebar = new() { Spacing = 8, Width = 185 };
    private readonly ComboBox _compactEditions = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly Dictionary<string, Button> _editionButtons = new();
    private readonly Button _games = Ui.Button("Games");
    private readonly Button _mods = Ui.Button("Mods");
    private string _section = "Games";
    private string _edition = "Ps2";
    private string? _lastJoin;
    private Ps2Edition _ps2 = null!;
    private RetailInstallation? _steam;
    private Bitmap? _artwork;

    private void InitializeLibrary()
    {
        _ps2 = Ps2Edition.Load(_model.Layout.Root);
        if (File.Exists(_model.Layout.Settings) || _model.Store.Current() is not null)
        {
            string saved = _model.Settings.SelectedProduct;
            _edition = Editions.Any(e => e.Id == saved) ? saved : "Game";
        }
        _model.Settings.SelectedProduct = _edition;
        var title = Ui.Text("OPENSCCT", 24, Ui.Accent);
        title.FontWeight = FontWeight.Bold;
        var brand = Ui.Stack(title, Ui.Text("Launcher " + (_model.LauncherVersion?.ToString() ?? "dev") + "  ·  Linux", 13, Ui.Muted));
        brand.Spacing = 0;
        var help = Ui.Button("Help");
        help.Click += async (_, _) => await ShowHelpAsync();
        var header = Ui.Columns(brand, ButtonRow(_games, _mods, _settings));
        _games.Click += (_, _) => ShowPage("Games", _edition);
        _mods.Click += (_, _) => ShowPage("Mods", _edition);
        _settings.Click += (_, _) => ShowPage("Settings", _edition);

        string? previousGroup = null;
        foreach ((string id, string label, string group) in Editions)
        {
            if (group != previousGroup)
            {
                _sidebar.Children.Add(Ui.Text(group, 14, Ui.Muted));
                previousGroup = group;
            }
            var button = Ui.Button(label);
            button.Content = Ui.Stack(Ui.Text(label, 15), Ui.Text(string.Empty, 13));
            button.HorizontalAlignment = HorizontalAlignment.Stretch;
            button.HorizontalContentAlignment = HorizontalAlignment.Left;
            button.Click += (_, _) => SelectEdition(id);
            _editionButtons.Add(id, button);
            _sidebar.Children.Add(button);
        }
        _sidebar.Children.Add(ButtonRow(Link("Website", ProjectLinks.Website), help));
        _compactEditions.ItemsSource = Editions.Select(e => e.Group + " · " + e.Title).ToArray();
        _compactEditions.SelectionChanged += (_, _) =>
        {
            int index = _compactEditions.SelectedIndex;
            if (index >= 0 && Editions[index].Id != _edition) SelectEdition(Editions[index].Id);
        };

        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        body.Children.Add(_sidebar);
        var pageStack = Ui.Stack(_compactEditions, _launcherBanner, _feedback, _page);
        pageStack.Spacing = 12;
        var scroll = new ScrollViewer
        {
            Content = pageStack,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
        };
        Grid.SetColumn(scroll, 1);
        body.Children.Add(scroll);
        void ResizeLibrary(double width)
        {
            bool compact = width < 760;
            _sidebar.IsVisible = !compact;
            _compactEditions.IsVisible = compact;
            body.ColumnSpacing = compact ? 0 : 20;
        }
        SizeChanged += (_, e) => ResizeLibrary(e.NewSize.Width);
        ResizeLibrary(Width);
        var shell = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), Margin = new Thickness(20), RowSpacing = 18 };
        shell.Children.Add(header);
        Grid.SetRow(body, 1);
        shell.Children.Add(body);
        Content = shell;
        ShowPage("Games", _edition);
    }

    private Control Ps2Page(bool mods)
    {
        bool hasDisc = File.Exists(_ps2.DiscPath);
        bool hasEmulator = File.Exists(_ps2.EmulatorPath);
        var disc = ActionButton(hasDisc ? "Change disc image" : "Choose disc image", async () =>
        {
            string? path = await PickFile("Select your Chaos Theory PS2 ISO", "*.iso");
            if (path is not null) SavePs2(_ps2.WithDisc(path));
        }, !hasDisc);
        var emulator = ActionButton(hasEmulator ? "Change PCSX2" : "Choose PCSX2", async () =>
        {
            string? path = await PickFile("Select the PCSX2 executable or AppImage");
            if (path is not null) SavePs2(_ps2.WithEmulator(path));
        }, hasDisc && !hasEmulator);
        if (mods)
        {
            var choose = ActionButton("Use patched ISO", async () =>
            {
                string? path = await PickFile("Choose a patched copy of the same disc", "*.iso");
                if (path is not null) SavePs2(_ps2.WithMod(path));
            }, true);
            choose.IsEnabled = hasDisc;
            var original = ActionButton("Use original disc", () => SavePs2(_ps2.WithoutMod()));
            original.IsEnabled = _ps2.ModPath is not null;
            return Ui.Stack(Hero("PlayStation 2", "Disc mods"),
                Card("Patched disc image", "Choose your original ISO in Games, then select a locally patched copy of the same release.",
                    Ui.Text(_ps2.ModPath ?? "Original disc selected", 14, Ui.Accent), ButtonRow(choose, original)));
        }

        var play = ActionButton("Play with PCSX2", () => Start(_ps2.CreateStartInfo()), true);
        play.IsEnabled = _ps2.NextAction == Ps2Action.Play;
        return Ui.Stack(Hero("PlayStation 2", "Splinter Cell Chaos Theory"),
            Card("Disc setup", "Choose your PlayStation 2 disc image and the PCSX2 executable installed on this computer.",
                Ui.Text(_ps2.Status, 15, Ui.Accent), ButtonRow(disc, emulator, play)),
            Card("Library", "Active disc", Ui.Text(_ps2.ModPath ?? _ps2.DiscPath ?? "Choose your ISO to get started.", 14, Ui.Muted),
                Link("PCSX2 setup guide", "https://pcsx2.net/docs/setup/")));
    }

    private void SavePs2(Ps2Edition result)
    {
        result.Save(_model.Layout.Root);
        _ps2 = result;
        ShowPage(_section, _edition);
    }

    private Control RetailPage(bool mods)
    {
        RetailProfiles profiles = _model.Settings.Retail;
        RetailInstallation? campaignInstall = RetailGame.Campaign(profiles, _steam);
        RetailInstallation? versus = RetailGame.Selected(profiles, _steam);
        if (mods)
        {
            var choose = ActionButton("Choose modded installation", async () =>
            {
                string? folder = await PickFolder("Select the modded Chaos Theory installation");
                if (folder is null) return;
                SaveRetail(updated => RetailGame.SetMod(updated, folder, _steam));
            }, true);
            var toggle = ActionButton(profiles.UseMod ? "Use stock installation" : "Use modded installation", () =>
            {
                SaveRetail(updated => updated.UseMod = !updated.UseMod);
            });
            toggle.IsEnabled = profiles.UseMod || RetailGame.Inspect(profiles.ModFolder) is not null;
            return Ui.Stack(Hero("PC / Steam", "Mod profiles"),
                Card("Modded installation", "Choose a separate PC installation with your mods already applied. Enhanced and Reloaded modify Versus. A campaign mod can use its own campaign and co-op executable.",
                    Ui.Text(profiles.UseMod ? "Selected modded profile" : "Selected stock profile", 15, Ui.Accent),
                    Ui.Text(profiles.ModFolder ?? "No modded folder selected.", 14, Ui.Muted), ButtonRow(choose, toggle)),
                ButtonRow(Link("Enhanced SCCT Versus", RetailGame.EnhancedWebsite), Link("Reloaded features", RetailGame.ReloadedWebsite + "features/")));
        }

        var locate = ActionButton("Choose stock installation", async () =>
        {
            string? folder = await PickFolder("Select the original Chaos Theory installation");
            if (folder is null) return;
            SaveRetail(updated => RetailGame.SetBase(updated, folder));
        });
        bool steamStock = RetailGame.IsSame(campaignInstall, _steam);
        var campaign = ActionButton(steamStock ? "Play in Steam" : "Play with Wine", () =>
        {
            if (steamStock) Start(RetailGame.CreateCampaignStart(campaignInstall!, _steam));
            else StartWine(campaignInstall!.CampaignExecutable!);
        }, true);
        campaign.IsEnabled = campaignInstall?.CampaignExecutable is not null;
        var play = ActionButton("Play Versus with Wine", () => StartWine(versus!.Executable), true);
        play.IsEnabled = versus?.HasVersus == true;
        return Ui.Stack(Hero("PC / Steam", "Splinter Cell Chaos Theory"),
            Card("Campaign & co-op", campaignInstall?.CampaignExecutable ?? "Locate your original PC installation or install the game through Steam.",
                ButtonRow(campaign, locate, Link("Steam store", "https://store.steampowered.com/app/13570/"))),
            Card("Versus", profiles.UseMod && RetailGame.IsSame(versus, RetailGame.Inspect(profiles.ModFolder)) ? "Modded profile" : "Stock profile",
                Ui.Text(versus?.HasVersus == true ? versus.Executable : "Locate an installation with Versus to play.", 14, Ui.Muted),
                ButtonRow(play, ActionButton("Manage mods", () => ShowPage("Mods", "Retail")))));
    }

    private void SaveRetail(Action<RetailProfiles> edit)
    {
        _model.SaveSettings(settings => edit(settings.Retail));
        ShowPage(_section, _edition);
    }

    private static void StartWine(string path)
    {
        var start = new ProcessStartInfo("wine") { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(path)! };
        start.ArgumentList.Add(path);
        try { Start(start); }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 2)
        { throw new InvalidOperationException("Install Wine and make the wine command available on PATH to launch this executable.", ex); }
    }

    private static void Start(ProcessStartInfo start)
    {
        using Process? process = Process.Start(start);
        if (process is null && !start.UseShellExecute) throw new IOException("The application could not be started.");
    }

    private Control UnrealPage(bool mods) => Ui.Stack(Hero("OpenSCCT Unreal", mods ? "Unreal mods" : "Modern Unreal edition"),
        Card(mods ? "Community maps" : "Windows x64 release",
            mods ? "Use the Windows launcher to manage maps for the Unreal edition." : "The published Unreal build uses Windows x64. OpenSCCT Godot is available natively on Linux.",
            Link("Unreal releases", "https://github.com/OpenSCCT/OpenSCCT-Unreal/releases")));

    private async Task<string?> PickFile(string title, string? pattern = null)
    {
        var options = new FilePickerOpenOptions { Title = title, AllowMultiple = false };
        if (pattern is not null) options.FileTypeFilter = [new FilePickerFileType("ISO disc image") { Patterns = [pattern, "*.ISO"] }];
        var picked = await StorageProvider.OpenFilePickerAsync(options);
        return picked.Count == 0 ? null : picked[0].TryGetLocalPath() ?? throw new IOException("Choose a local file.");
    }

    private async Task<string?> PickFolder(string title)
    {
        var picked = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title, AllowMultiple = false });
        return picked.Count == 0 ? null : picked[0].TryGetLocalPath() ?? throw new IOException("Choose a local folder.");
    }

    private Button ActionButton(string label, Action action, bool primary = false) =>
        ActionButton(label, () => { action(); return Task.CompletedTask; }, primary);

    private Button ActionButton(string label, Func<Task> action, bool primary = false)
    {
        var button = Ui.Button(label, primary);
        button.Click += async (_, _) =>
        {
            button.IsEnabled = false;
            try { await action(); }
            catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException
                or Win32Exception or NotSupportedException or System.Text.Json.JsonException)
            { SetFeedback(ex.Message, true); }
            finally { button.IsEnabled = true; }
        };
        return button;
    }

    private Button Link(string label, string url) => ActionButton(label, () => OpenLink(url));

    private Control Hero(string label, string title)
    {
        var text = Ui.Stack(Ui.Text(label, 16, Ui.Accent), Ui.Text(title, 28));
        text.Margin = new Thickness(20);
        text.VerticalAlignment = VerticalAlignment.Bottom;
        var scene = new Grid { Height = 148, ClipToBounds = true, Background = Ui.Surface };
        if (_artwork is not null) scene.Children.Add(new Image { Source = _artwork, Stretch = Stretch.UniformToFill });
        scene.Children.Add(new Border { Background = Brush.Parse("#B0101510") });
        scene.Children.Add(text);
        return scene;
    }

    private static Control Card(string title, string description, params Control[] actions)
    {
        var stack = Ui.Stack(Ui.Text(title, 20), Ui.Text(description, 15, Ui.Muted));
        foreach (Control action in actions) stack.Children.Add(action);
        return Ui.Panel(stack);
    }

    private static WrapPanel ButtonRow(params Button[] buttons)
    {
        var row = new WrapPanel();
        foreach (Button button in buttons) { button.Margin = new Thickness(0, 2, 6, 2); row.Children.Add(button); }
        return row;
    }

    private static void Highlight(Button button, bool selected)
    {
        button.Background = selected ? Ui.Accent : Ui.Surface;
        button.Foreground = selected ? Ui.Background : Brushes.WhiteSmoke;
        button.FontWeight = selected ? FontWeight.SemiBold : FontWeight.Normal;
    }

    private void SetFeedback(string text, bool error = false)
    {
        _feedback.Text = text;
        _feedback.Foreground = error ? Ui.Error : Ui.Accent;
        _feedback.IsVisible = !string.IsNullOrEmpty(text);
    }

    private void RequirePage(string section, string edition)
    {
        if (_section != section || _edition != edition || _page.Content is not Control)
            throw new InvalidOperationException($"The {edition} {section} page did not open.");
    }

    private string GodotStatus() => _model.ClientUpdateRequired ? "Update Godot"
        : _model.Playable ? "Ready to play"
        : _model.SteamSource is null ? "Steam folder needed"
        : _model.Store.Current() is null ? "Ready to install" : "Build Steam content";

    private void SelectEdition(string id)
    {
        ShowPage(_section == "Mods" ? "Mods" : "Games", id);
        try
        {
            _model.SaveSettings(settings => settings.SelectedProduct = id);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { SetFeedback(ex.Message, true); }
    }

    private void ShowPage(string section, string edition)
    {
        _section = section;
        _edition = edition;
        _compactEditions.SelectedIndex = Array.FindIndex(Editions, e => e.Id == edition);
        _steam = _smoke ? null : RetailGame.FindSteam();
        foreach ((string id, Button button) in _editionButtons)
        {
            bool selected = id == edition && section != "Settings";
            Highlight(button, selected);
            var text = (StackPanel)button.Content!;
            text.Spacing = 2;
            ((TextBlock)text.Children[0]).Foreground = selected ? Ui.Background : Brushes.WhiteSmoke;
            var state = (TextBlock)text.Children[1];
            state.Foreground = selected ? Ui.Background : Ui.Muted;
            state.Text = id switch
            {
                "Ps2" => _ps2.Status,
                "Retail" => RetailGame.Base(_model.Settings.Retail, _steam) is null ? "Locate installation" : "Installed",
                "Unreal" => "Windows x64",
                _ => GodotStatus(),
            };
        }
        Highlight(_games, section == "Games");
        Highlight(_mods, section == "Mods");
        Highlight(_settings, section == "Settings");
        SetFeedback(string.Empty);
        _page.Content = section == "Settings" ? SettingsPage() : edition switch
        {
            "Ps2" => Ps2Page(section == "Mods"),
            "Retail" => RetailPage(section == "Mods"),
            "Unreal" => UnrealPage(section == "Mods"),
            _ => section == "Games" ? _godotPage : Ui.Stack(Ui.Text("OpenSCCT Godot mods", 26),
                Card("Additional maps", "Build original maps from Steam. Download community-created maps here.",
                    ActionButton("Manage maps", ShowMapsAsync, true))),
        };
    }
}
