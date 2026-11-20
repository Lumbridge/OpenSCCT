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
    private readonly StackPanel _sidebar = new() { Spacing = 4, Width = 208 };
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
        var title = Ui.Text("OpenSCCT", 25);
        title.FontWeight = FontWeight.Bold;
        title.VerticalAlignment = VerticalAlignment.Center;
        var mark = new Image { Width = 28, Height = 28, VerticalAlignment = VerticalAlignment.Center };
        using (Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("OpenScct.Launcher.icon.png"))
        {
            if (stream is not null)
            {
                var logo = new Bitmap(stream);
                mark.Source = logo;
                Closed += (_, _) => logo.Dispose();
            }
        }
        var brand = Ui.Row(8, mark, title, new Border
        {
            Child = Ui.Text(_model.LauncherVersion?.ToString() ?? "dev", 11, Ui.Accent),
            Background = Ui.Surface, Padding = new Thickness(6, 3), CornerRadius = new CornerRadius(4),
            VerticalAlignment = VerticalAlignment.Center,
        });
        var nav = Ui.Row(6, Nav(_games, "Games", "games"), Nav(_mods, "Mods", "mods"), Nav(_settings, "Settings", "settings"));
        var web = IconButton(Link("Website", ProjectLinks.Website), "web", "OpenSCCT website");
        var help = IconButton(ActionButton("Help", ShowHelpAsync), "help", "Help and contributions");
        var utilities = Ui.Row(5, web, help);
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 22, Margin = new Thickness(20, 9) };
        header.Children.Add(brand);
        Grid.SetColumn(nav, 1); header.Children.Add(nav);
        Grid.SetColumn(utilities, 2); header.Children.Add(utilities);
        _games.Click += (_, _) => ShowPage("Games", _edition);
        _mods.Click += (_, _) => ShowPage("Mods", _edition);
        _settings.Click += (_, _) => ShowPage("Settings", _edition);

        string? previousGroup = null;
        foreach ((string id, string label, string group) in Editions)
        {
            if (group != previousGroup)
            {
                var heading = Caption(group);
                heading.Margin = new Thickness(9, previousGroup is null ? 2 : 17, 0, 5);
                _sidebar.Children.Add(heading);
                previousGroup = group;
            }
            var button = EditionRow(id, label);
            _editionButtons.Add(id, button);
            _sidebar.Children.Add(button);
        }
        _compactEditions.ItemsSource = Editions.Select(e => e.Group + " · " + e.Title).ToArray();
        _compactEditions.SelectionChanged += (_, _) =>
        {
            int index = _compactEditions.SelectedIndex;
            if (index >= 0 && Editions[index].Id != _edition) SelectEdition(Editions[index].Id);
        };

        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Thickness(13, 19, 18, 0) };
        body.Children.Add(_sidebar);
        var pageStack = Ui.Stack(_compactEditions, _feedback, _page);
        pageStack.Spacing = 13;
        pageStack.Margin = new Thickness(0, 0, 0, 20);
        _libraryScroll.Content = pageStack;
        _libraryScroll.HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled;
        Grid.SetColumn(_libraryScroll, 1);
        body.Children.Add(_libraryScroll);
        void ResizeLibrary(double width)
        {
            bool compact = width < 780;
            _sidebar.IsVisible = !compact;
            _compactEditions.IsVisible = compact && _section != "Settings";
            body.ColumnSpacing = compact ? 0 : 18;
            header.ColumnSpacing = width < 650 ? 8 : 22;
            utilities.IsVisible = width >= 650;
            mark.IsVisible = width >= 650;
            header.Margin = new Thickness(width < 650 ? 12 : 20, 9);
        }
        SizeChanged += (_, e) => ResizeLibrary(e.NewSize.Width);
        ResizeLibrary(Width);
        var shell = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*") };
        shell.Children.Add(new Border { Child = header, BorderBrush = Ui.Edge, BorderThickness = new Thickness(0, 0, 0, 1),
            Background = Shade("#20291F", "#161C17") });
        Grid.SetRow(_launcherBanner, 1); shell.Children.Add(_launcherBanner);
        Grid.SetRow(body, 2); shell.Children.Add(body);
        Content = shell;
        ShowPage("Games", _edition);
    }

    private Control Ps2Page(bool mods)
    {
        bool hasDisc = File.Exists(_ps2.DiscPath);
        bool hasEmulator = File.Exists(_ps2.EmulatorPath);
        async Task SelectDisc()
        {
            string? path = await PickFile("Select your Chaos Theory PS2 ISO", "*.iso");
            if (path is not null) SavePs2(_ps2.WithDisc(path));
        }
        async Task SelectEmulator()
        {
            string? path = await PickFile("Select the PCSX2 executable or AppImage");
            if (path is not null) SavePs2(_ps2.WithEmulator(path));
        }
        async Task SelectPatch()
        {
            string? path = await PickFile("Choose a patched copy of your Chaos Theory disc", "*.iso");
            if (path is not null) SavePs2(_ps2.WithMod(path));
        }
        if (mods)
        {
            var choose = ActionButton(hasDisc ? "Choose patched ISO" : "Set up original ISO",
                hasDisc ? SelectPatch : () => { ShowPage("Games", "Ps2"); return Task.CompletedTask; }, true);
            var original = ActionButton("Use original disc", () => SavePs2(_ps2.WithoutMod()));
            original.IsVisible = _ps2.ModPath is not null;
            return Ui.Stack(GameHero("Ps2", "PlayStation 2 · Mods", "Disc mods", "Use a patched copy alongside your original disc.", choose,
                    Steps(("Original ISO", hasDisc), ("Patched ISO", File.Exists(_ps2.ModPath)))),
                Card("Active disc", _ps2.ModPath ?? _ps2.DiscPath ?? "Choose your original ISO before adding a patched copy.", original));
        }

        var primary = _ps2.NextAction switch
        {
            Ps2Action.ChooseDisc => ActionButton("Choose disc image", SelectDisc, true),
            Ps2Action.ChooseEmulator => ActionButton("Choose PCSX2", SelectEmulator, true),
            Ps2Action.ChooseMod => ActionButton("Locate patched ISO", SelectPatch, true),
            _ => ActionButton("Play in PCSX2", () => Start(_ps2.CreateStartInfo()), true),
        };
        var page = Ui.Stack();
        if (!hasDisc) page.Children.Add(SourceStrip("Where is your PlayStation 2 disc image?",
            "Find Chaos Theory in a folder of your own ISO files.", ActionButton("Choose folder", FindPs2DiscAsync)));
        page.Children.Add(GameHero("Ps2", "PlayStation 2 · 2005", "Splinter Cell Chaos Theory",
            "Campaign, co-op and Versus on the original console edition.", primary,
            Steps(("Disc image", hasDisc), ("PCSX2", hasEmulator), ("Ready to play", _ps2.NextAction == Ps2Action.Play))));
        var settings = ButtonRow(ActionButton(hasEmulator ? "Change PCSX2" : "Choose PCSX2", SelectEmulator),
            Link("PCSX2 setup guide", "https://pcsx2.net/docs/setup/"));
        if (hasDisc) settings.Children.Add(ActionButton("Change original disc", SelectDisc));
        page.Children.Add(Card("Emulator", _ps2.EmulatorPath ?? "Select your PCSX2 executable or AppImage. PCSX2 manages its BIOS, memory cards and controller settings.", settings));
        page.Children.Add(Card("Disc mods", _ps2.ModPath ?? "Switch between the original disc and a locally patched copy.",
            ActionButton("Manage disc mods", () => ShowPage("Mods", "Ps2"))));
        return page;
    }

    private async Task FindPs2DiscAsync()
    {
        string? folder = await PickFolder("Find Chaos Theory ISO files in this folder");
        if (folder is null) return;
        Ps2Edition[] matches = await Task.Run(() =>
        {
            var found = new List<Ps2Edition>();
            foreach (string file in Directory.EnumerateFiles(folder).Where(file => Path.GetExtension(file).Equals(".iso", StringComparison.OrdinalIgnoreCase)))
            {
                try { found.Add(_ps2.WithDisc(file)); }
                catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) { }
            }
            return found.OrderBy(edition => edition.DiscPath, StringComparer.OrdinalIgnoreCase).ToArray();
        });
        if (matches.Length == 0) { SetFeedback("No supported Chaos Theory ISO was found in " + folder + ".", true); return; }
        if (matches.Length == 1) { SavePs2(matches[0]); return; }
        var list = new ListBox { ItemsSource = matches.Select(m => Path.GetFileName(m.DiscPath)).ToArray(), SelectedIndex = 0 };
        var picker = Ui.Dialog("Choose a Chaos Theory disc", list, 510, 360);
        var choose = Ui.Button("Use selected ISO", true);
        var cancel = Ui.Button("Cancel");
        choose.Click += (_, _) => picker.Close(list.SelectedIndex);
        cancel.Click += (_, _) => picker.Close(-1);
        Ui.SetButtons(picker, choose, cancel);
        int? picked = await picker.ShowDialog<int?>(this);
        if (picked is >= 0) SavePs2(matches[picked.Value]);
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
                if (folder is not null) SaveRetail(updated => RetailGame.SetMod(updated, folder, _steam));
            }, true);
            var toggle = ActionButton(profiles.UseMod ? "Use stock installation" : "Use modded installation",
                () => SaveRetail(updated => updated.UseMod = !updated.UseMod));
            toggle.IsEnabled = profiles.UseMod || RetailGame.Inspect(profiles.ModFolder) is not null;
            return Ui.Stack(GameHero("Retail", "PC / Steam · Mods", "Mod profiles", "Keep a separate installation for Enhanced or Reloaded Versus.", choose,
                    Steps(("Original installation", RetailGame.Base(profiles, _steam) is not null), ("Modded installation", RetailGame.Inspect(profiles.ModFolder) is not null))),
                Card("Active profile", profiles.UseMod ? profiles.ModFolder ?? "Modded installation" : "Original PC installation", toggle),
                Card("Enhanced and Reloaded", "Both mods extend Versus. Campaign and co-op use the selected campaign installation.",
                    ButtonRow(Link("Enhanced SCCT Versus", RetailGame.EnhancedWebsite), Link("Reloaded features", RetailGame.ReloadedWebsite + "features/"))));
        }

        async Task Locate()
        {
            string? folder = await PickFolder("Select the original Chaos Theory installation");
            if (folder is not null) SaveRetail(updated => RetailGame.SetBase(updated, folder));
        }
        bool steamStock = RetailGame.IsSame(campaignInstall, _steam);
        var campaign = ActionButton(steamStock ? "Play in Steam" : "Play with Wine", () =>
        {
            if (steamStock) Start(RetailGame.CreateCampaignStart(campaignInstall!, _steam));
            else StartWine(campaignInstall!.CampaignExecutable!);
        }, true);
        bool hasCampaign = campaignInstall?.CampaignExecutable is not null;
        bool hasVersus = versus?.HasVersus == true;
        var page = Ui.Stack();
        if (!hasCampaign) page.Children.Add(SourceStrip("Where is Chaos Theory installed?", "Select the game folder or install the original PC edition through Steam.",
            Link("Steam store", "https://store.steampowered.com/app/13570/")));
        page.Children.Add(GameHero("Retail", "PC / Steam · 2005", "Splinter Cell Chaos Theory",
            "Launch the original campaign and co-op using your PC installation.", hasCampaign ? campaign : ActionButton("Choose game folder", Locate, true),
            Steps(("Installation", RetailGame.Base(profiles, _steam) is not null), ("Campaign & co-op", hasCampaign), ("Versus", hasVersus))));
        var versusPlay = ActionButton("Play Versus with Wine", () => StartWine(versus!.Executable));
        versusPlay.IsVisible = hasVersus;
        page.Children.Add(Card("Versus", profiles.UseMod ? "Modded profile selected." : "Original multiplayer profile. Enhanced and Reloaded are optional.",
            ButtonRow(versusPlay, ActionButton("Manage mods", () => ShowPage("Mods", "Retail")))));
        if (hasCampaign) page.Children.Add(Card("Game files", campaignInstall!.Folder, ActionButton("Change game folder", Locate)));
        return page;
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

    private Control UnrealPage(bool mods) => Ui.Stack(
        GameHero("Unreal", "Modernisations · Unreal Engine", mods ? "Unreal community maps" : "OpenSCCT Unreal",
            mods ? "Manage maps using the Windows launcher." : "The Unreal modernisation is available for Windows x64.",
            Link("Unreal releases", "https://github.com/OpenSCCT/OpenSCCT-Unreal/releases"),
            Ui.Text("Windows x64", 13, Ui.Muted)),
        Card("Native Linux edition", "OpenSCCT Godot runs on Linux and builds the original Versus content from your Steam installation.",
            ActionButton("Open Godot edition", () => SelectEdition("Game"))));

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

    private void Highlight(Button button, bool selected)
    {
        button.Background = selected ? Ui.Accent : Ui.Surface;
        button.BorderBrush = selected ? Ui.Accent : Ui.Edge;
        _navLabels[button].Foreground = selected ? Ui.Background : Ui.Primary;
        _navIcons[button].Stroke = selected ? Ui.Background : Ui.Accent;
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
            button.Background = selected ? SelectedRow : Brushes.Transparent;
            button.BorderBrush = selected ? SelectedEdge : Brushes.Transparent;
            _editionLabels[id].Foreground = selected ? Ui.Accent : Ui.Primary;
            var state = _editionStates[id];
            state.Foreground = Ui.Muted;
            _editionLights[id].Background = id switch
            {
                "Ps2" => _ps2.NextAction == Ps2Action.Play ? Ui.Accent : Pending,
                "Retail" => RetailGame.Base(_model.Settings.Retail, _steam) is null ? Pending : Ui.Accent,
                "Game" => _model.Playable ? Ui.Accent : Pending,
                _ => Ui.Muted,
            };
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
        _compactEditions.IsVisible = !_sidebar.IsVisible && section != "Settings";
        _libraryScroll.Offset = default;
        _page.Content = section == "Settings" ? SettingsPage() : edition switch
        {
            "Ps2" => Ps2Page(section == "Mods"),
            "Retail" => RetailPage(section == "Mods"),
            "Unreal" => UnrealPage(section == "Mods"),
            _ => section == "Games" ? _godotPage : Ui.Stack(
                GameHero("Game", "OpenSCCT Godot · Mods", "Community maps", "Browse and install additional Versus maps.",
                    ActionButton("Manage maps", ShowMapsAsync, true), Ui.Text("Native Linux", 13, Ui.Muted)),
                Card("Original maps", "The original Versus maps are built from the installed Steam edition.",
                    ActionButton("Open Godot setup", () => ShowPage("Games", "Game")))),
        };
    }
}
