using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using OpenScct.Net.Discovery;
using OpenScct.Server;
using OpenScctGame.Online;
using OpenScctGame.Play.Demo;
using LocalDemo = OpenScctGame.Play.Demo.DemoEntry;
using OnlineDemo = OpenScct.Net.Discovery.DemoEntry;

namespace OpenScctGame.UI;

/// <summary>Browse saved recordings and recent online rounds, then watch or keep them (docs/DEMOS.md).</summary>
public partial class DemosScreen : MenuPage
{
    internal IReadOnlyList<(LocalDemo Entry, bool Cached)>? PreviewLocal { get; init; }
    internal DemoListResponse? PreviewRecent { get; init; }

    private readonly List<(LocalDemo Entry, bool Cached)> _local = new();
    private readonly List<Button> _downloadButtons = new();
    private readonly CancellationTokenSource _lifetime = new();
    private GridContainer _cards = null!;
    private Label _status = null!, _count = null!, _collectionHelp = null!, _recordingHelp = null!, _pageNumber = null!;
    private Label _libraryTitle = null!, _libraryHelp = null!;
    private LineEdit _search = null!;
    private OptionButton _mode = null!, _storage = null!, _order = null!;
    private Button _localTab = null!, _recentTab = null!, _refreshButton = null!, _previous = null!, _next = null!;
    private HFlowContainer _pager = null!;
    private ProgressBar _progress = null!;
    private Control _statusPanel = null!;
    private DemoListResponse? _online;
    private Task<DemoListResponse>? _listing;
    private string? _listingError, _localError;
    private bool _recentSelected;
    private bool? _compact;
    private ulong _protocol;
    private MapCatalog _maps = null!;

    private bool Previewing => AccountSettings.Isolated && (PreviewLocal is not null || PreviewRecent is not null);
    protected override Vector2 ResponsiveReference => new(1600f, 900f);

    public static Button MenuButton(Node from) =>
        UiStyle.MenuButton("Demos", () => FrontEnd.ShowDemos(from.GetTree()), "Demos");

    public override void _Ready()
    {
        Name = "Demos";
        BuildFrame("Demos", 1180f, responsive: true);
        _protocol = OpenScct.Server.NetClasses.Create().ComputeProtocolHash();
        _maps = new MapCatalog([.. App.From(this).Options.MapRoots, .. MapImages.Default.Roots]);
        Body.AddThemeConstantOverride("separation", 12);
        var introduction = Card(Body, "DemoLibraryHeader");
        var heading = new HBoxContainer();
        heading.AddThemeConstantOverride("separation", 12);
        introduction.AddChild(heading);
        _libraryTitle = Text(heading, "LibraryTitle", "Your match library", 22);
        var recording = new CheckBox
        {
            Name = "DemoRecording", Text = DemoRecordingText.Label, ButtonPressed = OnlineSettings.Current.RecordDemos,
            TooltipText = DemoRecordingText.Tooltip,
        };
        recording.Toggled += enabled =>
        {
            OnlineSettings.Current.RecordDemos = enabled;
            Error saved = OnlineSettings.Current.Save();
            UpdateRecordingHelp();
            if (saved != Error.Ok) ShowError("The recording preference could not be saved: " + saved);
        };
        heading.AddChild(recording);
        _libraryHelp = Text(introduction, "LibraryHelp", "Revisit a round, follow any player, or explore with the free camera.", 14, dim: true);
        _recordingHelp = Text(introduction, "RecordingHelp", string.Empty, 14, dim: true);
        UpdateRecordingHelp();
        var tabs = new HFlowContainer { Name = "DemoCollections" };
        tabs.AddThemeConstantOverride("h_separation", 8);
        Body.AddChild(tabs);
        var group = new ButtonGroup();
        _localTab = ActionButton(tabs, "On this computer", () => SelectCollection(false), "LocalTab");
        _recentTab = ActionButton(tabs, "Recent online", () => SelectCollection(true), "RecentTab");
        _localTab.TooltipText = "Saved copies stay here. Downloads expire after 7 days; choose Keep to save them.";
        _recentTab.TooltipText = "Online rounds expire after 7 days. Keep saves a copy; Watch opens playback. Filters search the current page.";
        foreach (Button tab in new[] { _localTab, _recentTab }) { tab.ToggleMode = true; tab.ButtonGroup = group; }
        _localTab.SetPressedNoSignal(true);
        var filter = Card(Body, "DemoFilters");
        var searchRow = new HBoxContainer();
        searchRow.AddThemeConstantOverride("separation", 8);
        filter.AddChild(searchRow);
        _search = new LineEdit
        {
            Name = "DemoSearch", PlaceholderText = "Search map, player or server…", ClearButtonEnabled = true,
            SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0f, 38f),
            TooltipText = "Search the current collection by map, mode, player, server or file name.",
        };
        _search.TextChanged += _ => Render();
        searchRow.AddChild(_search);
        ActionButton(searchRow, "Reset", ResetFilters, "ClearFilters");
        var filters = new HFlowContainer { Name = "DemoFilterOptions" };
        filters.AddThemeConstantOverride("h_separation", 12);
        filters.AddThemeConstantOverride("v_separation", 8);
        filter.AddChild(filters);
        _mode = Choice(filters, "DemoMode", "Game mode", ["All modes", "Story", "Disk Hunt", "Deathmatch", "Merc Deathmatch", "Extraction", "Sabotage", "Neutralization"]);
        _storage = Choice(filters, "DemoStorage", "Storage", ["All local demos", "Saved", "Downloads"]);
        _order = Choice(filters, "DemoOrder", "Sort order", ["Newest first", "Oldest first"]);
        _collectionHelp = Text(filter, "CollectionHelp", string.Empty, 14, dim: true);
        var status = Card(Body, "DemoStatusPanel");
        _statusPanel = (Control)status.GetParent();
        _status = Text(status, "Status", string.Empty, 14);
        _progress = new ProgressBar { Name = "DownloadProgress", CustomMinimumSize = new Vector2(0, 8), ShowPercentage = false, Visible = false };
        status.AddChild(_progress);
        _statusPanel.Visible = false;
        _count = Text(Body, "DemoCount", string.Empty, 14, dim: true);
        _cards = new GridContainer { Name = "DemoCards", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _cards.AddThemeConstantOverride("h_separation", 12);
        _cards.AddThemeConstantOverride("v_separation", 12);
        _cards.Resized += () => _cards.Columns = _cards.Size.X >= 1080f ? 2 : 1;
        Body.AddChild(_cards);
        _pager = new HFlowContainer { Name = "DemoPagination" };
        _pager.AddThemeConstantOverride("h_separation", 12);
        Body.AddChild(_pager);
        _previous = ActionButton(_pager, "Previous page", () => LoadRecent(Math.Max(0, (_online?.Page ?? 0) - 1)), "PreviousPage");
        _pageNumber = Text(_pager, "PageNumber", string.Empty, 14);
        _pageNumber.CustomMinimumSize = new Vector2(120f, 36f);
        _pageNumber.VerticalAlignment = VerticalAlignment.Center;
        _next = ActionButton(_pager, "Next page", () => LoadRecent((_online?.Page ?? 0) + 1), "NextPage");
        PageButton("Back", () => FrontEnd.ShowMainMenu(GetTree()), "Back").GrabFocus();
        PageButton("Open folder", OpenFolder, "OpenFolder").TooltipText = "Open your saved demos. Copy shared .scctdemo files here, then Refresh.";
        _refreshButton = PageButton("Refresh", Refresh, "Refresh");
        foreach (Button button in PageButtons) button.CustomMinimumSize = new Vector2(160f, 44f);
        if (!Previewing)
        {
            try { DemoLibrary.PruneCache(TimeSpan.FromDays(7)); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { ShowError("Could not clean the demo cache: " + e.Message); }
        }
        else _online = PreviewRecent ?? new DemoListResponse();
        ReadLocal();
        Render();
        FitLibrary();
    }

    public override void _ExitTree()
    {
        _lifetime.Cancel();
        _lifetime.Dispose();
    }

    public override void _Process(double delta)
    {
        base._Process(delta);
        FitLibrary();
        if (_listing is { IsCompleted: true } listing)
        {
            _listing = null;
            if (listing.IsCompletedSuccessfully) { _online = listing.Result; _listingError = null; }
            else _listingError = AccountService.ErrorOf(listing);
            Render();
        }
        ProcessDownload();
    }

    private void FitLibrary()
    {
        bool compact = LayoutSize.X < 900f;
        if (_compact == compact) return;
        _compact = compact;
        _libraryTitle.Visible = _libraryHelp.Visible = _recordingHelp.Visible = _collectionHelp.Visible = !compact;
        Body.AddThemeConstantOverride("separation", compact ? 8 : 12);
    }

    private void UpdateRecordingHelp() => _recordingHelp.Text = OnlineSettings.Current.RecordDemos
        ? "Recording on · Stay connected after online rounds while your demo downloads."
        : "Recording off · Save full match replays, whoever hosts. You can also change this in the lobby.";

    private void SelectCollection(bool recent)
    {
        _recentSelected = recent;
        PageCanvas.GetNode<ScrollContainer>("BodyScroll").ScrollVertical = 0;
        if (recent && _online is null && _listing is null) LoadRecent(0);
        else Render();
    }

    private void ResetFilters()
    {
        _search.Text = string.Empty;
        _mode.Select(0);
        _storage.Select(0);
        _order.Select(0);
        Render();
    }

    private void Refresh()
    {
        ReadLocal();
        if (_recentSelected) LoadRecent(_online?.Page ?? 0);
        else Render();
    }

    private void ReadLocal()
    {
        _local.Clear();
        _localError = null;
        if (Previewing) { if (PreviewLocal is { } preview) _local.AddRange(preview); return; }
        try
        {
            Directory.CreateDirectory(DemoLibrary.SavedFolder);
            var saved = DemoLibrary.List(DemoLibrary.SavedFolder);
            var names = new HashSet<string>(saved.Select(e => e.FileName), StringComparer.OrdinalIgnoreCase);
            _local.AddRange(saved.Select(e => (e, false)));
            _local.AddRange(DemoLibrary.List(DemoLibrary.CacheFolder).Where(e => !names.Contains(e.FileName)).Select(e => (e, true)));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { _localError = "Could not read the demos folder: " + e.Message; }
    }

    private void LoadRecent(int page)
    {
        if (Previewing || _listing is not null) { Render(); return; }
        _listingError = null;
        if (AccountService.HasMaster && AccountSettings.Current.SignedIn)
        {
            CancellationToken token = _lifetime.Token;
            _listing = AccountService.Call(client => client.DemosAsync(page, token));
        }
        Render();
    }

    private void Render()
    {
        Clear(_cards);
        _downloadButtons.Clear();
        _localTab.Text = $"On this computer ({_local.Count})";
        _recentTab.Text = _online is { } list ? $"Recent online ({list.Total})" : "Recent online";
        _localTab.SetPressedNoSignal(!_recentSelected);
        _recentTab.SetPressedNoSignal(_recentSelected);
        _storage.Visible = !_recentSelected;
        _refreshButton.Disabled = _listing is not null;
        _refreshButton.Text = _listing is not null ? "Refreshing…" : "Refresh";
        _collectionHelp.Text = _recentSelected
            ? "Online rounds expire after 7 days. Keep saves a copy; Watch opens playback. Filters search this page."
            : "Saved copies stay here. Downloads expire after 7 days; choose Keep to save them.";
        _pager.Visible = _recentSelected && _online is { Total: > 0 };
        int pageSize = Math.Max(1, _online?.PageSize ?? 1);
        int page = _online?.Page ?? 0;
        int pages = Math.Max(1, (int)Math.Ceiling((_online?.Total ?? 0) / (double)pageSize));
        _pageNumber.Text = $"Page {page + 1} of {pages}";
        _previous.Disabled = _listing is not null || page == 0 || Previewing;
        _next.Disabled = _listing is not null || page + 1 >= pages || Previewing;
        if (_recentSelected) RenderRecent(); else RenderLocal();
        UpdateDownloadButtons();
    }

    private void RenderLocal()
    {
        var entries = _local.Where(item => (_storage.Selected != 1 || !item.Cached) && (_storage.Selected != 2 || item.Cached) &&
            Matches(LocalMode(item.Entry), item.Entry.Header.Map, MapTitle(item.Entry.Header.Map), item.Entry.Header.ServerName, item.Entry.FileName,
                string.Join(" ", item.Entry.Index.Players.Select(p => p.Name))));
        entries = _order.Selected == 1 ? entries.OrderBy(e => e.Entry.Header.StartUnixMs) : entries.OrderByDescending(e => e.Entry.Header.StartUnixMs);
        int count = 0;
        foreach (var item in entries) { LocalCard(item.Entry, item.Cached); count++; }
        _count.Text = $"{count} of {_local.Count} local demos";
        if (_localError is { } error) Empty("Demos folder unavailable", error);
        else if (count == 0) Empty(_local.Count == 0 ? "Your next round belongs here" : "No matching demos",
            _local.Count == 0 ? "Turn on Record my matches to build your library, or browse Recent online to watch a trusted-server round."
                : "Try a different search or reset the filters to see the whole collection.", reset: _local.Count > 0);
    }

    private void RenderRecent()
    {
        _count.Text = "Recent online demos";
        if (!Previewing && !AccountService.HasMaster)
        {
            Empty("Connect to a master server", "Choose a master in Find a Session to browse recent recorded rounds.",
                "Find a Session", () => FrontEnd.ShowServerBrowser(GetTree()));
            return;
        }
        if (!Previewing && !AccountSettings.Current.SignedIn)
        {
            Empty("Sign in to browse recent rounds", "Your saved demos are always available on this computer. Sign in to download recent online recordings.",
                "Account", () => FrontEnd.ShowAccount(GetTree()));
            return;
        }
        if (_listing is not null) { Empty("Loading recent rounds…", "Retrieving the demo list from the master server."); return; }
        if (_listingError is { } error) { Empty("Could not load recent rounds", error, "Retry", () => LoadRecent(_online?.Page ?? 0)); return; }
        IEnumerable<OnlineDemo> entries = (_online?.Demos ?? []).Where(e => Matches(ModeName(e.Mode), e.Map, MapTitle(e.Map), e.ServerName,
            string.Join(" ", e.Players.Select(p => p.Name))));
        entries = _order.Selected == 1 ? entries.OrderBy(e => e.Time) : entries.OrderByDescending(e => e.Time);
        int count = 0;
        foreach (OnlineDemo entry in entries) { RecentCard(entry); count++; }
        int available = _online?.Demos.Count ?? 0;
        _count.Text = $"{count} of {available} demos on this page";
        if (count == 0) Empty(available == 0 ? "No recent recordings yet" : "No matching demos on this page",
            available == 0 ? "Recorded rounds from trusted servers will appear here. You can still watch your local demos."
                : "Reset the filters or try another page of recent rounds.", reset: available > 0);
    }

    private bool Matches(string mode, params string[] text)
    {
        if (_mode.Selected > 0 && !string.Equals(_mode.GetItemText(_mode.Selected), mode, StringComparison.OrdinalIgnoreCase)) return false;
        string query = _search.Text.Trim();
        return query.Length == 0 || mode.Contains(query, StringComparison.OrdinalIgnoreCase) || text.Any(t => t.Contains(query, StringComparison.OrdinalIgnoreCase));
    }

    private void LocalCard(LocalDemo entry, bool cached)
    {
        bool playable = entry.Header.ProtocolHash == _protocol;
        var card = DemoCard("LocalDemo", entry.Header.Map, LocalMode(entry), entry.Recorded, entry.Duration,
            entry.Header.ServerName, entry.Index.Players.Select(p => p.Name), entry.Bytes, cached ? "Temporary download" : "Saved on this computer");
        card.TooltipText = entry.FileName;
        Text(card, "FileName", entry.FileName, 13, dim: true);
        if (!playable) Text(card, "Compatibility", "Requires the recording's game protocol · recorded on " + entry.Header.GameVersion, 14).Modulate = new Color(1f, 0.76f, 0.5f);
        var actions = Actions(card);
        card.MoveChild(actions, 1);
        var watch = ActionButton(actions, "Watch", () => DemoLibrary.Watch(GetTree(), entry.Path), "Watch");
        watch.Disabled = !playable;
        watch.TooltipText = playable ? "Open playback" : "This recording uses a different network protocol from the installed game.";
        if (cached) ActionButton(actions, "Keep", () => Keep(entry), "Keep").TooltipText = "Move this download into your permanent saved demos.";
        ActionButton(actions, "Delete", () => ConfirmDelete(entry), "Delete").TooltipText = "Delete this local recording after confirmation.";
    }

    private void RecentCard(OnlineDemo demo)
    {
        bool playable = string.Equals(demo.NetVersion, MasterApi.Hex(_protocol), StringComparison.OrdinalIgnoreCase);
        double remaining = (demo.Expires - DateTimeOffset.UtcNow).TotalDays;
        string expiry = remaining <= 0 ? "Availability expired" : remaining < 1 ? "Expires in less than a day" : $"Expires in {(int)Math.Ceiling(remaining)} days";
        var card = DemoCard("RecentDemo", demo.Map, ModeName(demo.Mode), demo.Time, TimeSpan.FromSeconds(demo.DurationSeconds),
            demo.ServerName, demo.Players.Select(p => p.Name), demo.Size, expiry);
        if (!playable) Text(card, "Compatibility", "Requires the recording's game protocol · recorded on " + demo.GameVersion, 14).Modulate = new Color(1f, 0.76f, 0.5f);
        var actions = Actions(card);
        card.MoveChild(actions, 1);
        var watch = ActionButton(actions, "Watch", () => StartDownload(demo, keep: false), "Watch");
        watch.SetMeta("compatible", playable);
        watch.TooltipText = playable ? "Download and open playback" : "This recording uses a different network protocol from the installed game.";
        _downloadButtons.Add(watch);
        var keep = ActionButton(actions, "Keep", () => StartDownload(demo, keep: true), "Keep");
        keep.SetMeta("compatible", true);
        keep.TooltipText = "Download a permanent copy without opening playback.";
        _downloadButtons.Add(keep);
    }

    private VBoxContainer DemoCard(string name, string map, string mode, DateTimeOffset recorded, TimeSpan duration,
        string server, IEnumerable<string> players, long bytes, string storage)
    {
        var card = Card(_cards, name);
        string title = MapTitle(map);
        var summary = new HBoxContainer { Name = "Summary" };
        summary.AddThemeConstantOverride("separation", 12);
        card.AddChild(summary);
        var picture = new MapThumb(new Vector2(160, 100)) { SizeFlagsVertical = SizeFlags.ShrinkBegin };
        picture.SetMap(MapImages.Preview(_maps, map), title);
        summary.AddChild(picture);
        var heading = new VBoxContainer { Name = "Heading", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        heading.AddThemeConstantOverride("separation", 6);
        summary.AddChild(heading);
        Text(heading, "Storage", storage, 13).Modulate = UiStyle.MenuFocus;
        Text(heading, "Title", title, 22);
        string when = recorded.ToLocalTime().ToString("dd MMM yyyy · HH:mm", CultureInfo.InvariantCulture);
        Text(heading, "Metadata", $"{mode} · {Duration(duration)} · {when}", 14, dim: true);
        string[] names = players.ToArray();
        Text(card, "Server", string.IsNullOrWhiteSpace(server) ? "Local recording" : server, 15);
        Text(card, "Players", names.Length == 0 ? "No player names recorded" : $"{names.Length} players · {string.Join(", ", names)}", 14, dim: true);
        Text(card, "Size", Bytes(bytes), 13, dim: true);
        return card;
    }

    private void Keep(LocalDemo entry)
    {
        try { DemoLibrary.Keep(entry.Path); ShowStatus("Saved " + MapTitle(entry.Header.Map) + " to your library."); ReadLocal(); Render(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { ShowError("Could not keep the demo: " + e.Message); }
    }

    private string MapTitle(string map) => string.IsNullOrWhiteSpace(map) ? "Unknown map" : MapTitles.Of(_maps, map);

    private void ConfirmDelete(LocalDemo entry)
    {
        Control? previousFocus = GetViewport().GuiGetFocusOwner();
        string when = entry.Recorded.ToLocalTime().ToString("dd MMM yyyy · HH:mm", CultureInfo.InvariantCulture);
        ConfirmBox.Open(this, new ConfirmBox
        {
            Name = "DeleteDemoConfirmation", YesCaption = "Delete", NoCaption = "Cancel",
            Question = $"Delete this demo?\n\n{MapTitle(entry.Header.Map)} · {when}\n{entry.FileName}\n\nThis permanently removes the recording from this computer.",
            Yes = () => Delete(entry),
            No = () => { if (GodotObject.IsInstanceValid(previousFocus)) previousFocus!.GrabFocus(); },
        });
    }

    private void Delete(LocalDemo entry)
    {
        try
        {
            DemoLibrary.Delete(entry.Path);
            _local.RemoveAll(item => string.Equals(item.Entry.Path, entry.Path, StringComparison.OrdinalIgnoreCase));
            Render();
            _localTab.GrabFocus();
            ShowStatus("Deleted " + MapTitle(entry.Header.Map) + " from this computer.");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            ShowError("Could not delete the demo: " + e.Message);
        }
    }

    private void OpenFolder()
    {
        try
        {
            Directory.CreateDirectory(DemoLibrary.SavedFolder);
            Error result = OS.ShellOpen(DemoLibrary.SavedFolder);
            if (result != Error.Ok) ShowError("Could not open the demos folder: " + result);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { ShowError("Could not open the demos folder: " + e.Message); }
    }

    public void ShowStatus(string text)
    {
        _status.Text = text;
        _status.Modulate = UiStyle.MenuFocus;
        _statusPanel.Visible = text.Length > 0;
        if (text.Length > 0) Callable.From(() =>
        {
            if (IsInsideTree() && !IsQueuedForDeletion())
                PageCanvas.GetNode<ScrollContainer>("BodyScroll").EnsureControlVisible(_statusPanel);
        }).CallDeferred();
    }

    public void ShowError(string text)
    {
        ShowStatus(text);
        _status.Modulate = new Color(1f, 0.68f, 0.54f);
    }

    private void Empty(string title, string message, string? action = null, Action? pressed = null, bool reset = false)
    {
        var card = Card(_cards, "EmptyDemos");
        Text(card, "EmptyTitle", title, 20);
        Text(card, "EmptyMessage", message, 15, dim: true);
        if (reset) ActionButton(Actions(card), "Reset filters", ResetFilters, "ResetEmptyFilters");
        else if (action is not null && pressed is not null) ActionButton(Actions(card), action, pressed, "EmptyAction");
    }

    private static VBoxContainer Card(Container parent, string name)
    {
        var panel = new PanelContainer { Name = name, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var style = (StyleBox)UiStyle.MenuPanelBox().Duplicate();
        style.ContentMarginLeft = style.ContentMarginRight = 14f;
        style.ContentMarginTop = style.ContentMarginBottom = 12f;
        panel.AddThemeStyleboxOverride("panel", style);
        parent.AddChild(panel);
        var column = new VBoxContainer { Name = "Column", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        column.AddThemeConstantOverride("separation", 6);
        panel.AddChild(column);
        return column;
    }

    private static Label Text(Container parent, string name, string text, int size, bool dim = false)
    {
        var label = new Label { Name = name, Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = SizeFlags.ExpandFill, Modulate = dim ? UiStyle.Dim : UiStyle.Text, MouseFilter = MouseFilterEnum.Ignore };
        label.AddThemeFontSizeOverride("font_size", size);
        parent.AddChild(label);
        return label;
    }

    private static HFlowContainer Actions(Container parent)
    {
        var flow = new HFlowContainer { Name = "Actions" };
        flow.AddThemeConstantOverride("h_separation", 8);
        parent.AddChild(flow);
        return flow;
    }

    private static Button ActionButton(Container parent, string text, Action action, string name)
    {
        Button button = UiStyle.MenuButton(text, action, name);
        button.CustomMinimumSize = new Vector2(0, 36);
        button.AddThemeFontSizeOverride("font_size", 15);
        parent.AddChild(button);
        return button;
    }

    private OptionButton Choice(Container parent, string name, string tooltip, string[] choices)
    {
        var option = new OptionButton { Name = name, TooltipText = tooltip, CustomMinimumSize = new Vector2(166, 36) };
        option.AddThemeFontSizeOverride("font_size", 14);
        foreach (string choice in choices) option.AddItem(choice);
        option.ItemSelected += _ => Render();
        parent.AddChild(option);
        return option;
    }

    private static void Clear(Node parent)
    {
        foreach (Node child in parent.GetChildren()) { parent.RemoveChild(child); child.QueueFree(); }
    }

    private static string LocalMode(LocalDemo entry) => Play.Demo.DemoPlayer.ModeKey(entry.Header.GameOptions) switch
    {
        "DiskHunt" => "Disk Hunt", "MercDeathMatch" => "Merc Deathmatch", var mode => mode,
    };
    private static string ModeName(int mode) => mode switch
    {
        1 => "Extraction", 2 => "Sabotage", 3 => "Neutralization", 4 => "Disk Hunt", 6 => "Deathmatch", 7 => "Merc Deathmatch", _ => "Story",
    };
    private static string Duration(TimeSpan span) => span.TotalHours >= 1
        ? string.Create(CultureInfo.InvariantCulture, $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}")
        : string.Create(CultureInfo.InvariantCulture, $"{(int)span.TotalMinutes}:{span.Seconds:00}");
    private static string Bytes(long bytes) => bytes >= 1024 * 1024
        ? string.Create(CultureInfo.InvariantCulture, $"{bytes / (1024d * 1024d):0.0} MB")
        : string.Create(CultureInfo.InvariantCulture, $"{Math.Max(1, bytes / 1024)} KB");
}
