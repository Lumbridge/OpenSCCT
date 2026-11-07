using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using OpenScct.Launcher.GitHub;
using OpenScct.Launcher.Install;
using OpenScct.Launcher.Update;
using OpenScct.Maps;
using OpenScct.Project;

namespace OpenScct.Launcher.Linux;

internal sealed class MainWindow : Window
{
    private readonly LauncherController _model;
    private readonly bool _smoke;
    private readonly TextBlock _heading = Ui.Text("Install OpenSCCT", 26);
    private readonly TextBlock _installed = Ui.Text(string.Empty, 13, Ui.Muted);
    private readonly TextBlock _detail = Ui.Text(string.Empty, 14, Ui.Muted);
    private readonly TextBlock _status = Ui.Text(string.Empty, 13, Ui.Muted);
    private readonly TextBlock _notesTitle = Ui.Text("Latest release", 19);
    private readonly TextBlock _notes = Ui.Text("Check for updates to load the latest changes.", 14, Ui.Muted);
    private readonly TextBlock _launcherText = Ui.Text(string.Empty, 14);
    private readonly ProgressBar _progress = new() { Minimum = 0, Maximum = 100, Height = 5, IsVisible = false };
    private readonly Button _main = Ui.Button("Install", primary: true);
    private readonly Button _playCurrent = Ui.Button("Play current version");
    private readonly Button _check = Ui.Button("Check for updates");
    private readonly Button _maps = Ui.Button("Maps");
    private readonly Button _settings = Ui.Button("Settings");
    private readonly Button _launcher = Ui.Button("Update launcher");
    private readonly Border _launcherBanner;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(3) };
    private bool _closing;
    private bool _closed;

    public MainWindow(LauncherController model, bool smoke)
    {
        _model = model;
        _smoke = smoke;
        Title = "OpenSCCT Launcher";
        Width = 840;
        Height = 560;
        MinWidth = 540;
        MinHeight = 420;
        Background = Ui.Background;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        using (Stream? icon = Assembly.GetExecutingAssembly().GetManifestResourceStream("OpenScct.Launcher.icon.ico"))
            if (icon is not null) Icon = new WindowIcon(icon);

        var title = Ui.Text("OPENSCCT", 31, Ui.Accent);
        title.FontWeight = FontWeight.Bold;
        var brand = Ui.Stack(title, Ui.Text("Splinter Cell Chaos Theory Versus, rebuilt", 13, Ui.Muted));
        var version = Ui.Text("Launcher " + (model.LauncherVersion?.ToString() ?? "dev") + "  ·  Linux", 12, Ui.Muted);
        version.HorizontalAlignment = HorizontalAlignment.Right;
        var header = Ui.Columns(brand, version);
        header.Margin = new Thickness(0, 0, 0, 16);
        _main.MinWidth = 150;
        _main.MinHeight = 46;
        _main.FontSize = 17;
        _main.HorizontalContentAlignment = HorizontalAlignment.Center;
        _playCurrent.FontSize = 12;
        _playCurrent.HorizontalAlignment = HorizontalAlignment.Right;
        var summary = Ui.Stack(_heading, _installed, _detail);
        var action = Ui.Stack(_main, _playCurrent);
        action.HorizontalAlignment = HorizontalAlignment.Right;
        var launch = Ui.Panel(Ui.Stack(Ui.Columns(summary, action), _status, _progress));
        _launcherBanner = Ui.Panel(Ui.Columns(_launcherText, _launcher));
        _launcherBanner.IsVisible = false;
        var changelog = Ui.Button("Full changelog");
        changelog.Click += async (_, _) => await ShowNotesAsync();
        var release = Ui.Panel(Ui.Stack(Ui.Columns(_notesTitle, changelog), _notes));
        var website = Ui.Button("Website");
        website.Click += (_, _) => OpenLink(ProjectLinks.Website);
        var help = Ui.Button("Help");
        help.Click += async (_, _) => await ShowHelpAsync();
        var tools = new WrapPanel { Orientation = Orientation.Horizontal, Children = { _maps, _check, website, _settings, help } };
        foreach (Control tool in tools.Children) tool.Margin = new Thickness(0, 0, 6, 6);
        var stack = Ui.Stack(header, _launcherBanner, launch, release, tools);
        stack.Spacing = 14;
        stack.Margin = new Thickness(24, 20, 24, 14);
        Content = new ScrollViewer { Content = stack, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };

        _main.Click += async (_, _) => await DoNextAsync();
        _playCurrent.Click += async (_, _) => await PlayAsync(true);
        _check.Click += async (_, _) => await model.RunAsync("Checking for updates", model.CheckAsync);
        _maps.Click += async (_, _) => await ShowMapsAsync();
        _settings.Click += async (_, _) => await ShowSettingsAsync();
        _launcher.Click += async (_, _) =>
        {
            if (model.LauncherStaged)
                await model.RunAsync("Restarting launcher", _ => { model.RestartLauncher(); return Task.CompletedTask; });
            else await model.RunAsync("Updating launcher", model.StageLauncherAsync);
        };
        model.Changed += Refresh;
        model.GameStarted += () => WindowState = WindowState.Minimized;
        model.Restarted += Close;
        _timer.Tick += (_, _) => model.Tick();
        Opened += async (_, _) =>
        {
            try
            {
                await model.InitializeAsync(!smoke);
                if (smoke) await RunUiSmokeAsync();
                else _timer.Start();
            }
            catch (Exception ex) { model.Report(ex.Message, true); }
        };
        Closing += async (_, e) =>
        {
            if (_closing || !model.Busy) return;
            e.Cancel = true;
            _closing = true;
            model.Cancel();
            await model.WaitForWorkAsync();
            Close();
        };
        Closed += (_, _) => { _closed = true; _timer.Stop(); model.Changed -= Refresh; };
        Refresh();
    }

    public void QueueJoin(string id)
    {
        _model.QueueJoin(id);
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    private void Refresh()
    {
        if (_closed) return;
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(Refresh); return; }
        LauncherAction next = _model.Next;
        string? current = _model.Store.Current()?.ToString();
        _heading.Text = _model.Busy ? _model.Operation : next switch
        {
            LauncherAction.Install => "Install OpenSCCT",
            LauncherAction.Update => "An update is available",
            LauncherAction.FinishInstall => "Finish setting up",
            LauncherAction.Waiting => "Update ready to finish",
            _ => "Ready to play",
        };
        _installed.Text = current is null ? "Native Linux · 64-bit" : "Installed version " + current;
        _detail.Text = next switch
        {
            LauncherAction.Install => "Download the game and its required files. No original installation is needed.",
            LauncherAction.Update => "Only changed game files are downloaded. Your settings and saves are kept.",
            LauncherAction.FinishInstall => "Download the remaining game files to start playing.",
            LauncherAction.Waiting => "Close the running game to activate the new build.",
            LauncherAction.Play => _model.PendingJoin is null ? "Your installation is ready. You can also play offline." : "A website Join request is waiting.",
            _ => "You can stop and continue later. Completed downloads are kept.",
        };
        _main.Content = next switch
        {
            LauncherAction.Install => "Install",
            LauncherAction.Update => "Update",
            LauncherAction.FinishInstall => "Finish install",
            LauncherAction.Cancel => "Stop",
            LauncherAction.Waiting => "Waiting for game",
            _ => _model.PendingJoin is null ? "PLAY" : "JOIN SESSION",
        };
        _main.IsEnabled = next != LauncherAction.Waiting;
        _status.Text = _model.Status;
        _status.Foreground = _model.IsError ? Ui.Error : Ui.Muted;
        _status.IsVisible = !string.IsNullOrWhiteSpace(_model.Status);
        _progress.IsVisible = _model.Busy;
        _progress.IsIndeterminate = _model.Progress is null;
        _progress.Value = (_model.Progress ?? 0) * 100;
        _playCurrent.IsVisible = !_model.Busy && _model.Playable && next == LauncherAction.Update;
        _check.IsEnabled = _maps.IsEnabled = _settings.IsEnabled = !_model.Busy;
        bool hasLauncherUpdate = _model.HasLauncherUpdate;
        bool writableLauncher = hasLauncherUpdate && _model.CanUpdateLauncher;
        _launcherBanner.IsVisible = hasLauncherUpdate;
        _launcher.Content = _model.LauncherStaged ? "Restart to finish" : "Update launcher";
        _launcher.IsEnabled = !_model.Busy && writableLauncher;
        _launcherText.Text = _model.LauncherStaged ? "A new launcher is ready. Restart when you are ready."
            : writableLauncher ? "Launcher " + _model.Check?.LatestLauncher + " is available."
            : "A launcher update is available. Download it from Releases; this folder is read only.";
        if (_model.Check?.Latest is { } latest)
        {
            _notesTitle.Text = latest.Release.Name ?? "OpenSCCT " + latest.Version;
            var highlights = ReleaseNotesPreview.Create(latest.Release.Body);
            _notes.Text = highlights.Count > 0 ? string.Join("\n\n", highlights.Select(h => "•  " + h)) : "No release notes were published for this version.";
        }
    }

    private async Task DoNextAsync()
    {
        switch (_model.Next)
        {
            case LauncherAction.Cancel: _model.Cancel(); break;
            case LauncherAction.Install:
            case LauncherAction.Update: await _model.RunAsync("Installing OpenSCCT", _model.InstallAsync); break;
            case LauncherAction.FinishInstall: await _model.RunAsync("Downloading game files", _model.InstallContentAsync); break;
            case LauncherAction.Play: await PlayAsync(false); break;
        }
    }

    private Task PlayAsync(bool current) => _model.RunAsync("Starting OpenSCCT", cancel => _model.PlayAsync(current, ChooseUpdateAsync, cancel));

    private async Task<PlayUpdateChoice> ChooseUpdateAsync(UpdateCheck check)
    {
        int choice = await Ui.ChooseAsync(this, "OpenSCCT update available", "Version " + check.Latest?.Version
            + " is available. Update now, or play your installed version and update later.", "Update now", "Play current version", "Cancel");
        return choice == 0 ? PlayUpdateChoice.Update : choice == 1 ? PlayUpdateChoice.PlayCurrent : PlayUpdateChoice.Cancel;
    }

    private Task ShowNotesAsync() => Ui.ShowTextAsync(this, _model.Check?.Latest?.Release.Name ?? "Release notes",
        _model.Check?.Latest?.Release.Body ?? "No cached release notes yet. Check for updates to load them.",
        "Website changelog", () => OpenLink(ProjectLinks.Website + "/changelog#game-latest"));

    private async Task ShowSettingsAsync()
    {
        var startup = new CheckBox { Content = "Check for updates when the launcher starts", IsChecked = _model.Settings.CheckOnStart };
        var previews = new CheckBox { Content = "Include preview releases", IsChecked = _model.Settings.IncludePrereleases };
        var data = Ui.Button("Open installation folder");
        data.Click += (_, _) => OpenFolder(_model.Layout.Root);
        var saves = Ui.Button("Open game settings and saves");
        saves.Click += (_, _) => OpenFolder(LauncherLayout.GetGameUserData(LauncherPlatform.LinuxX64));
        var rollback = Ui.Button("Use previous game build");
        rollback.IsEnabled = _model.Store.Previous() is not null && !_model.GameRunning;
        var reset = Ui.Button("Reset installation…");
        reset.IsEnabled = !_model.GameRunning;
        var content = Ui.Stack(Ui.Text("Updates", 20), startup, previews, Ui.Text("Files", 20),
            Ui.Text(_model.Layout.Root, 12, Ui.Muted), data, saves, rollback, reset,
            Ui.Text("Reset removes installed builds and downloaded content. Game settings, bindings and saves are kept.", 13, Ui.Muted));
        var dialog = Ui.Dialog("Launcher settings", content, 590, 510);
        rollback.Click += async (_, _) =>
        {
            if (await Ui.ChooseAsync(dialog, "Use previous build", "Switch back to the previous installed game version?", "Switch version", "Cancel") != 0) return;
            await _model.RunAsync("Switching version", _ => { _model.Rollback(); return Task.CompletedTask; });
            dialog.Close();
        };
        reset.Click += async (_, _) =>
        {
            if (await Ui.ChooseAsync(dialog, "Reset installation", "Remove downloaded game builds, maps and packs? You will need to download them again.", "Reset installation", "Cancel") != 0) return;
            try { _model.Reset(); dialog.Close(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            { _model.Report(ex.Message, true); }
        };
        var save = Ui.Button("Save", true);
        var cancel = Ui.Button("Cancel");
        Ui.SetButtons(dialog, save, cancel);
        save.Click += (_, _) =>
        {
            try { _model.SaveSettings(startup.IsChecked == true, previews.IsChecked == true); dialog.Close(true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _model.Report(ex.Message, true); }
        };
        cancel.Click += (_, _) => dialog.Close(false);
        bool saved = await dialog.ShowDialog<bool>(this);
        if (saved) await _model.RunAsync("Checking for updates", _model.CheckAsync);
    }

    private async Task ShowHelpAsync()
    {
        var context = new ReportContext
        {
            Source = ReportSource.Launcher,
            Version = _model.LauncherVersion?.ToString(),
            LauncherVersion = _model.LauncherVersion?.ToString(),
            InstalledGameVersion = _model.Store.Current()?.ToString() ?? "not installed",
            Platform = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
        };
        var detail = new TextBox { Text = ProjectLinks.EnvironmentDetails(context), IsReadOnly = true, AcceptsReturn = true, MinHeight = 110 };
        var link = new TextBox { IsReadOnly = true, PlaceholderText = "Selected link" };
        var status = Ui.Text(string.Empty, 12, Ui.Muted);
        var items = Ui.Stack(Ui.Text("Help and contributions", 22));
        foreach (var pair in new[]
        {
            ("Report a bug", ProjectDestination.BugReport), ("Suggest a feature", ProjectDestination.FeatureRequest),
            ("Documentation", ProjectDestination.Documentation), ("Existing issues", ProjectDestination.Issues),
            ("Contributing", ProjectDestination.Contributing), ("Pull requests", ProjectDestination.PullRequests),
        })
        {
            var button = Ui.Button(pair.Item1);
            button.Click += (_, _) => { link.Text = ProjectLinks.Url(pair.Item2, context); OpenLink(link.Text); };
            items.Children.Add(button);
        }
        items.Children.Add(link);
        items.Children.Add(detail);
        items.Children.Add(status);
        var licenses = Ui.Button("Third-party notices");
        items.Children.Add(licenses);
        var dialog = Ui.Dialog("OpenSCCT help", items, 600, 600);
        var copy = Ui.Button("Copy details");
        var close = Ui.Button("Close");
        Ui.SetButtons(dialog, copy, close);
        licenses.Click += async (_, _) =>
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            var notices = new System.Text.StringBuilder();
            foreach (string name in assembly.GetManifestResourceNames().Where(n => n.StartsWith("OpenScct.Launcher.Licenses.", StringComparison.Ordinal)).Order())
            {
                using Stream stream = assembly.GetManifestResourceStream(name)!;
                using var reader = new StreamReader(stream);
                notices.AppendLine(name["OpenScct.Launcher.Licenses.".Length..]).AppendLine().AppendLine(await reader.ReadToEndAsync()).AppendLine();
            }
            await Ui.ShowTextAsync(dialog, "Third-party notices", notices.ToString());
        };
        copy.Click += async (_, _) =>
        {
            try
            {
                if (dialog.Clipboard is not { } clipboard) throw new InvalidOperationException("No clipboard available.");
                await clipboard.SetTextAsync(detail.Text);
                status.Text = "Details copied.";
            }
            catch (Exception) { status.Text = "Select and copy the details manually."; }
        };
        close.Click += (_, _) => dialog.Close();
        await dialog.ShowDialog(this);
    }

    private async Task ShowMapsAsync()
    {
        var search = new TextBox { PlaceholderText = "Search maps by name or category" };
        var rows = new StackPanel { Spacing = 6 };
        var description = Ui.Text("Loading maps…", 13, Ui.Muted);
        var content = Ui.Stack(search, description, rows);
        var dialog = Ui.Dialog("Downloaded maps", content, 700, 570);
        var download = Ui.Button("Download / update", true);
        var remove = Ui.Button("Remove");
        var stop = Ui.Button("Stop");
        var refresh = Ui.Button("Refresh");
        var close = Ui.Button("Close");
        Ui.SetButtons(dialog, download, remove, stop, refresh, close);
        var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        ContentIndex? index = null;
        bool closed = false;
        void RefreshState()
        {
            if (closed) return;
            var maps = index?.PlayableMaps().ToList() ?? [];
            description.Text = _model.Status;
            description.Foreground = _model.IsError ? Ui.Error : Ui.Muted;
            bool mutable = !_model.Busy && !_model.GameRunning;
            download.IsEnabled = mutable && maps.Any(m => selected.Contains(m.Code) && !_model.Packs.Has(m));
            remove.IsEnabled = mutable && selected.Any(code => _model.Packs.InstalledMap(code) is not null);
            refresh.IsEnabled = !_model.Busy;
            stop.IsVisible = _model.Busy;
            foreach (Control row in rows.Children) row.IsEnabled = !_model.Busy;
        }
        void Fill()
        {
            if (closed) return;
            rows.Children.Clear();
            string query = search.Text?.Trim() ?? string.Empty;
            var installed = _model.Packs.Installed().Where(p => !p.IsShared).ToDictionary(p => p.Code, StringComparer.OrdinalIgnoreCase);
            var maps = index?.PlayableMaps().ToList() ?? [];
            foreach (ContentMap map in maps)
            {
                installed.Remove(map.Code);
                if (query.Length > 0 && !(map.DisplayTitle + " " + map.Category + " " + map.Code).Contains(query, StringComparison.OrdinalIgnoreCase)) continue;
                string state = _model.Packs.Has(map) ? "Downloaded" : _model.Packs.InstalledMap(map.Code) is null ? LauncherController.Size(map.Full.Size) : "Update available";
                AddRow(map.Code, map.DisplayTitle + "  ·  " + map.Category + "  ·  " + state);
            }
            foreach (InstalledContent pack in installed.Values)
                if (query.Length == 0 || pack.Code.Contains(query, StringComparison.OrdinalIgnoreCase)) AddRow(pack.Code, pack.Code + "  ·  Downloaded (not listed)");
            RefreshState();
        }
        void AddRow(string code, string text)
        {
            var row = new CheckBox { Content = Ui.Text(text, 13), IsChecked = selected.Contains(code), IsEnabled = !_model.Busy };
            row.IsCheckedChanged += (_, _) => { if (row.IsChecked == true) selected.Add(code); else selected.Remove(code); RefreshState(); };
            rows.Children.Add(row);
        }
        async Task Load()
        {
            await _model.RunAsync("Loading maps", async cancel => index = await _model.LoadMapsAsync(cancel));
            Fill();
        }
        search.TextChanged += (_, _) => Fill();
        _model.Changed += RefreshState;
        dialog.Opened += async (_, _) =>
        {
            if (_smoke)
            {
                index = new ContentIndex { Maps = [SmokeMap("AquaE", "Aquarius", "stock"), SmokeMap("Community", "Community map", "community")] };
                _model.Report("Select maps to download, update or remove.");
                Fill();
            }
            else await Load();
        };
        download.Click += async (_, _) =>
        {
            ContentMap[] maps = index?.PlayableMaps().Where(m => selected.Contains(m.Code) && !_model.Packs.Has(m)).ToArray() ?? [];
            await _model.RunAsync("Downloading maps", cancel => _model.DownloadMapsAsync(maps, cancel));
            Fill();
        };
        remove.Click += async (_, _) =>
        {
            string[] codes = selected.Where(c => _model.Packs.InstalledMap(c) is not null).ToArray();
            if (await Ui.ChooseAsync(dialog, "Remove maps", $"Remove {codes.Length} downloaded map(s)?", "Remove", "Cancel") != 0) return;
            try { _model.RemoveMaps(codes); } catch (Exception ex) { _model.Report(ex.Message, true); }
            Fill();
        };
        refresh.Click += async (_, _) => await Load();
        stop.Click += (_, _) => _model.Cancel();
        close.Click += (_, _) => dialog.Close();
        dialog.Closed += (_, _) => { closed = true; _model.Changed -= RefreshState; };
        await dialog.ShowDialog(this);
    }

    private void OpenFolder(string path)
    {
        try { Directory.CreateDirectory(path); Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose(); }
        catch (Exception ex) { _model.Report("Could not open the folder: " + ex.Message, true); }
    }

    private void OpenLink(string? link)
    {
        if (!Uri.TryCreate(link, UriKind.Absolute, out Uri? uri) || uri.Scheme is not ("https" or "http")) return;
        try { Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true })?.Dispose(); }
        catch (Exception ex) { _model.Report("Could not open the browser. Copy this link: " + link + "\n" + ex.Message, true); }
    }

    private async Task RunUiSmokeAsync()
    {
        try
        {
            await Task.Delay(200);
            SaveSmokeImage("launcher-install");
            Width = 560;
            Height = 430;
            _model.Report("A native Linux game build has not been published on the selected release channel yet. Check again after the Linux release is available.");
            await Task.Delay(200);
            if (_main.Bounds.Width <= 0 || _main.Bounds.Height <= 0 || _main.Content?.ToString() != "Install")
                throw new InvalidOperationException("The install action was not laid out.");
            SaveSmokeImage("launcher-compact");
            Task settings = ShowSettingsAsync();
            await Task.Delay(150);
            Window settingsWindow = OwnedWindows.Last();
            SaveSmokeImage("launcher-settings", settingsWindow);
            settingsWindow.Close(false);
            await settings;
            Task maps = ShowMapsAsync();
            await Task.Delay(150);
            Window mapsWindow = OwnedWindows.Last();
            SaveSmokeImage("launcher-maps", mapsWindow);
            mapsWindow.Close();
            await maps;
            Task busy = _model.RunAsync("Downloading OpenSCCT", cancel => Task.Delay(System.Threading.Timeout.Infinite, cancel));
            await Task.Delay(100);
            if (_main.Content?.ToString() != "Stop" || _settings.IsEnabled || _maps.IsEnabled)
                throw new InvalidOperationException("Busy controls did not expose Stop or protect settings and maps.");
            SaveSmokeImage("launcher-progress");
            _model.Cancel();
            await busy;
            Console.WriteLine("Linux launcher UI smoke passed: install action, compact layout, settings, maps, progress/cancel controls and native rendering.");
            (Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.Shutdown(0);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Linux launcher UI smoke failed: " + ex);
            (Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.Shutdown(1);
        }
    }

    private static ContentMap SmokeMap(string code, string title, string category)
    {
        var download = new ContentDownload("https://fixture.invalid/map", 64 * 1024 * 1024, new string('0', 64));
        return new ContentMap { Id = category + "/" + code, Code = code, Title = title, Category = category, Modes = ["Deathmatch"], Full = download, Sim = download };
    }

    private void SaveSmokeImage(string name, Window? window = null)
    {
        string? directory = Environment.GetEnvironmentVariable("OPENSCCT_LAUNCHER_SMOKE_OUTPUT");
        if (string.IsNullOrEmpty(directory)) return;
        Directory.CreateDirectory(directory);
        window ??= this;
        using var image = new RenderTargetBitmap(new PixelSize((int)window.Bounds.Width, (int)window.Bounds.Height), new Vector(96, 96));
        image.Render(window);
        image.Save(Path.Combine(directory, name + ".png"), PngBitmapEncoderOptions.Default);
    }
}

internal static class Ui
{
    public static readonly IBrush Background = Brush.Parse("#101510");
    public static readonly IBrush Surface = Brush.Parse("#1B231B");
    public static readonly IBrush Accent = Brush.Parse("#B5D866");
    public static readonly IBrush Muted = Brush.Parse("#ABB5A8");
    public static readonly IBrush Error = Brush.Parse("#FFB29F");
    public static TextBlock Text(string text, double size = 14, IBrush? color = null) => new()
    { Text = text, FontSize = size, Foreground = color ?? Brushes.WhiteSmoke, TextWrapping = TextWrapping.Wrap };
    public static Button Button(string text, bool primary = false)
    {
        var button = new Button { Content = text, Padding = new Thickness(13, 8), MinHeight = 34 };
        if (primary) { button.Background = Accent; button.Foreground = Background; button.FontWeight = FontWeight.SemiBold; }
        return button;
    }
    public static StackPanel Stack(params Control[] controls)
    {
        var stack = new StackPanel { Spacing = 9 };
        foreach (Control control in controls) stack.Children.Add(control);
        return stack;
    }
    public static Border Panel(Control child) => new() { Child = child, Padding = new Thickness(18), Background = Surface, CornerRadius = new CornerRadius(9) };
    public static Grid Columns(Control left, Control right)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 16 };
        grid.Children.Add(left);
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        return grid;
    }
    public static Window Dialog(string title, Control content, double width = 540, double height = 330)
    {
        var grid = new Grid { RowDefinitions = new RowDefinitions("*,Auto"), Margin = new Thickness(20), RowSpacing = 16 };
        grid.Children.Add(new ScrollViewer { Content = content, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });
        return new Window { Title = title, Width = width, Height = height, MinWidth = 420, MinHeight = 260, Background = Background,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = grid };
    }
    public static void SetButtons(Window dialog, params Button[] buttons)
    {
        var row = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Orientation = Orientation.Horizontal };
        foreach (Button button in buttons) { button.Margin = new Thickness(6, 2); row.Children.Add(button); }
        Grid.SetRow(row, 1);
        ((Grid)dialog.Content!).Children.Add(row);
    }
    public static async Task<int> ChooseAsync(Window owner, string title, string text, params string[] choices)
    {
        var dialog = Dialog(title, Text(text, 16), 570, 280);
        Button[] buttons = choices.Select((choice, i) =>
        {
            var button = Button(choice, i == 0);
            button.Click += (_, _) => dialog.Close(i + 1);
            return button;
        }).ToArray();
        SetButtons(dialog, buttons);
        return await dialog.ShowDialog<int>(owner) - 1;
    }
    public static async Task ShowTextAsync(Window owner, string title, string text, string? linkText = null, Action? link = null)
    {
        var box = new TextBox { Text = text, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, BorderThickness = new Thickness(0) };
        var dialog = Dialog(title, box, 660, 520);
        var close = Button("Close");
        close.Click += (_, _) => dialog.Close();
        if (linkText is not null && link is not null)
        {
            var button = Button(linkText);
            button.Click += (_, _) => link();
            SetButtons(dialog, button, close);
        }
        else SetButtons(dialog, close);
        await dialog.ShowDialog(owner);
    }
}
