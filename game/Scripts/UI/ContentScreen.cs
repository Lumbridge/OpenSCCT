using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Godot;
using OpenScct.Maps;
using OpenScct.Server;
using OpenScctGame.Online;

namespace OpenScctGame.UI;

/// <summary>
/// Content (OpenSCCT's own, docs/CONTENT-PACKS.md): the optional maps in OpenSCCT-Content, with what this
/// computer has. Download, Update, Stop and Remove work as the launcher's Maps window does, through
/// <see cref="ContentPacks"/>, so a download started here is the same one the lobby and the hub show.
/// </summary>
public partial class ContentScreen : MenuPage
{
    private const string AllCategories = "All categories";

    protected override Vector2 ResponsiveReference => new(1600f, 900f);

    private VBoxContainer? _maps;
    private Label? _where;
    private Label? _status;
    private LineEdit? _search;
    private OptionButton? _category;
    private CheckBox? _downloadedOnly;
    private ContentIndex? _shownIndex;
    private string? _shownError;
    private int _generation = -1;
    private int _catalogGeneration = ContentPacks.Generation;
    private readonly List<RowView> _views = [];

    public static Button MenuButton(Node from) =>
        UiStyle.MenuButton("Maps & Content", () => FrontEnd.ShowContent(from.GetTree()), "Content");

    /// <summary>One row's live parts: what it was built with, and the bits a running download updates.</summary>
    private sealed record RowView(string Key, string? BuiltWith, Label Status, UiProgress Bar, BoxContainer Row);

    private bool CompactRows => Math.Min(PreferredBodyWidth, AvailableBodyWidth) < 900f;

    private enum MapState
    {
        Available,
        Downloaded,
        UpdateAvailable,
        InGame,
        Unlisted,
    }

    public override void _Ready()
    {
        Name = "Content";
        BuildFrame("Maps & Content", 1180f, responsive: true);
        Body.AddThemeConstantOverride("separation", 12);
        PageCanvas.GetNode<ScrollContainer>("BodyScroll").AddChild(new CareerScroll());

        VBoxContainer where = CareerPanels.Card(Body, "Where", "Your library");
        CareerPanels.Text(where, "Help", OperatingSystem.IsBrowser() ? "Download maps to explore or join a session." :
            "Install extra maps alongside the maps built from your Steam game.", dim: true, size: 14);
        _where = new Label { Name = "WhereText", AutowrapMode = TextServer.AutowrapMode.WordSmart, Modulate = UiStyle.Dim };
        _where.AddThemeFontSizeOverride("font_size", 13);
        where.AddChild(_where);

        VBoxContainer maps = CareerPanels.Card(Body, "Maps", "Map catalogue");
        var filters = new HFlowContainer { Name = "Filters" };
        filters.AddThemeConstantOverride("h_separation", 8);
        filters.AddThemeConstantOverride("v_separation", 8);
        maps.AddChild(filters);
        _search = new LineEdit { Name = "Search", CustomMinimumSize = new Vector2(260f, 36f), PlaceholderText = "Search maps", SizeFlagsHorizontal = SizeFlags.ExpandFill, ClearButtonEnabled = true };
        _search.TextChanged += _ => Fill();
        filters.AddChild(_search);
        _category = new OptionButton { Name = "Category", CustomMinimumSize = new Vector2(200f, 0f), FitToLongestItem = false, ClipText = true };
        _category.AddItem(AllCategories);
        _category.ItemSelected += _ => Fill();
        filters.AddChild(_category);
        _downloadedOnly = new CheckBox { Name = "DownloadedOnly", Text = OperatingSystem.IsBrowser() ? "Installed this session" : "On this computer" };
        _downloadedOnly.Toggled += _ => Fill();
        filters.AddChild(_downloadedOnly);

        var mapScroll = new ScrollContainer { CustomMinimumSize = new Vector2(0f, 300f), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        maps.AddChild(mapScroll);
        _maps = new VBoxContainer { Name = "MapRows", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _maps.AddThemeConstantOverride("separation", 8);
        mapScroll.FollowFocus = true;
        mapScroll.AddChild(_maps);

        if (!OperatingSystem.IsBrowser()) AnimationPacks();

        _status = new Label { Name = "Status", Modulate = UiStyle.MenuFocus, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        Body.AddChild(_status);

        PageButton("Back", () => FrontEnd.ShowMainMenu(GetTree()), "Back");
        if (!OperatingSystem.IsBrowser()) PageButton("Open Folder", OpenFolder, "OpenFolder");
        PageButton("Refresh", () =>
        {
            ContentPacks.RefreshIndex(force: true);
            ShowStatus("Loading the content list...");
            _shownIndex = null;
            Fill();
        }, "Refresh");

        foreach (Button button in PageButtons) button.CustomMinimumSize = new Vector2(144f, 44f);
        ContentPacks.RefreshIndex();
        Fill();
        _search.GrabFocus();
    }

    public override void _Process(double delta)
    {
        base._Process(delta);
        ContentIndex? index = ContentPacks.Index;
        string? error = ContentPacks.IndexError;
        if (!ReferenceEquals(index, _shownIndex) || error != _shownError || ContentPacks.Generation != _generation || RowChanged())
        {
            Fill();
        }

        foreach (RowView view in _views)
        {
            if (view.Row.Vertical != CompactRows) view.Row.Vertical = CompactRows;
            if (JobOf(view) is { Running: true } job)
            {
                view.Status.Text = job.DescribeBrief();
                view.Status.TooltipText = job.Describe();
                view.Status.MouseFilter = MouseFilterEnum.Pass;
                view.Bar.Value = (float)job.Fraction;
            }
        }

        RefreshCatalog();
    }

    public override void _ExitTree() => RefreshCatalog();

    /// <summary>After a download or a removal, the game's own map list (Create a Session, the lobby) reads the folders again.</summary>
    private void RefreshCatalog()
    {
        if (ContentPacks.Generation == _catalogGeneration)
        {
            return;
        }

        _catalogGeneration = ContentPacks.Generation;
        OnlineService.Get(GetTree()).Catalog.Maps.Refresh();
    }

    // ---- the rows --------------------------------------------------------------------------------------------------

    private static ContentPacks.DownloadJob? JobOf(RowView view) =>
        ContentPacks.Job(view.Key);

    /// <summary>A download started, finished or failed since the rows were built (here, in the lobby or the hub).</summary>
    private bool RowChanged()
    {
        foreach (RowView view in _views)
        {
            if (JobState(JobOf(view)) != view.BuiltWith)
            {
                return true;
            }
        }

        return false;
    }

    private static string? JobState(ContentPacks.DownloadJob? job) =>
        job is null ? null : job.Running ? "running" : job.Done ? "done" : "error:" + job.Error;

    private void Fill()
    {
        if (_maps is null)
        {
            return;
        }

        _shownIndex = ContentPacks.Index;
        _shownError = ContentPacks.IndexError;
        _generation = ContentPacks.Generation;
        _views.Clear();
        Clear(_maps);
        ShowWhere();

        ContentStore store = ContentPacks.Store;
        IReadOnlyList<InstalledContent> installed = store.Installed();
        FillCategories(_shownIndex);
        FillMaps(store, installed);

        if (_shownIndex is null)
        {
            ShowStatus(_shownError is { } e
                ? "Could not read the content list (" + e + "). Maps already on this computer are listed; press Refresh to try again."
                : "Loading the content list...");
        }
        else if (_status?.Text.StartsWith("Loading", StringComparison.Ordinal) == true || _status?.Text.Length == 0 ||
            _status?.Text.StartsWith("Downloading", StringComparison.Ordinal) == true && _views.All(v => JobOf(v)?.Running != true))
        {
            int count = _shownIndex.PlayableMaps().Count(m => ContentPacks.CanDownloadMap(m.Code));
            int have = installed.Count(c => !c.IsShared && ContentPacks.CanDownloadMap(c.Code));
            ShowStatus($"{count} downloadable maps, {have} downloaded." + (_shownIndex.Problem is { } p ? " " + p : string.Empty));
        }
    }

    private static void Clear(Node parent)
    {
        foreach (Node child in parent.GetChildren())
        {
            parent.RemoveChild(child);
            child.QueueFree();
        }
    }

    private void ShowWhere()
    {
        if (_where is null)
        {
            return;
        }

        if (OperatingSystem.IsBrowser())
        {
            _where.Text = "Maps are installed for this browser session. Chrome may reuse cached archives on later visits; reload the page to restore bundled maps. Shared gameplay data is included with the game.";
            return;
        }

        _where.Text = $"Game files: {ContentPaths.Root}\nOptional maps: {ContentPaths.PacksFolder}";
    }

    private void FillCategories(ContentIndex? index)
    {
        if (_category is null || index is null || _category.ItemCount > 1)
        {
            return;
        }

        foreach (string c in index.PlayableMaps().Where(m => ContentPacks.CanDownloadMap(m.Code)).Select(m => m.Category).Where(c => c.Length > 0).Distinct()
            .OrderBy(ContentIndex.CategoryRank).ThenBy(c => c, StringComparer.Ordinal))
        {
            _category.AddItem(c);
        }
    }

    private void FillMaps(ContentStore store, IReadOnlyList<InstalledContent> installed)
    {
        var packs = installed.Where(p => !p.IsShared && ContentPacks.CanDownloadMap(p.Code)).ToDictionary(p => p.Code, StringComparer.OrdinalIgnoreCase);
        var rows = new List<(ContentMap? Map, InstalledContent? Pack)>();
        if (_shownIndex is not null)
        {
            foreach (ContentMap m in _shownIndex.PlayableMaps().Where(m => ContentPacks.CanDownloadMap(m.Code)).OrderBy(m => ContentIndex.CategoryRank(m.Category))
                .ThenBy(m => m.DisplayTitle, StringComparer.OrdinalIgnoreCase))
            {
                rows.Add((m, packs.GetValueOrDefault(m.Code)));
                packs.Remove(m.Code);
            }
        }

        // Downloaded maps the list no longer has (or before it has loaded), so they can still be removed.
        rows.AddRange(packs.Values.OrderBy(p => p.Code, StringComparer.OrdinalIgnoreCase).Select(p => ((ContentMap?)null, (InstalledContent?)p)));

        string query = _search?.Text.Trim() ?? string.Empty;
        string category = _category is { Selected: > 0 } c ? c.GetItemText(c.Selected) : string.Empty;
        bool onlyHere = _downloadedOnly?.ButtonPressed == true;
        int shown = 0;
        foreach ((ContentMap? map, InstalledContent? pack) in rows)
        {
            string code = map?.Code ?? pack!.Code;
            string title = map?.DisplayTitle ?? pack!.Code;
            string cat = map?.Category ?? (pack!.Id.Split('/') is [var first, _] ? first : string.Empty);
            MapState state = StateOf(store, map, pack);
            if ((query.Length > 0 && !title.Contains(query, StringComparison.OrdinalIgnoreCase) && !code.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                (category.Length > 0 && !string.Equals(cat, category, StringComparison.Ordinal)) ||
                (onlyHere && state is MapState.Available))
            {
                continue;
            }

            _maps!.AddChild(MapRow(code, title, cat, map, pack, state));
            shown++;
        }

        if (shown == 0)
        {
            _maps!.AddChild(new Label
            {
                Text = rows.Count == 0 ? "No maps yet." : "No maps match.",
                Modulate = UiStyle.Dim,
            });
        }
    }

    private static bool InGameFiles(string code) =>
        ContentPaths.SvMap is { } svmap && File.Exists(Path.Combine(svmap, code, "manifest.json"));

    private static MapState StateOf(ContentStore store, ContentMap? map, InstalledContent? pack)
    {
        if (map is null)
        {
            return MapState.Unlisted;
        }

        if (pack is not null)
        {
            return store.Has(map) ? MapState.Downloaded : MapState.UpdateAvailable;
        }

        return InGameFiles(map.Code) ? MapState.InGame : MapState.Available;
    }

    private Control MapRow(string code, string title, string category, ContentMap? map, InstalledContent? pack, MapState state)
    {
        var column = new VBoxContainer { Name = "Map" };
        column.AddThemeConstantOverride("separation", 2);
        var row = new BoxContainer { Vertical = CompactRows };
        row.AddThemeConstantOverride("separation", 12);
        column.AddChild(row);
        var summary = new VBoxContainer { Name = "Summary", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var details = new HBoxContainer { Name = "Details", SizeFlagsVertical = SizeFlags.ShrinkCenter };
        details.AddThemeConstantOverride("separation", 12);
        row.AddChild(summary);
        row.AddChild(details);

        var tooltip = new List<string> { $"Map code {code}" };
        if (map is not null)
        {
            if (map.Version.Length > 0)
            {
                tooltip.Add("Version " + map.Version);
            }

            if (map.Modes.Count > 0)
            {
                tooltip.Add("Modes: " + string.Join(", ", map.Modes));
            }
        }

        if (pack is not null)
        {
            tooltip.Add("Downloaded " + pack.InstalledAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture));
        }

        summary.AddChild(new Label
        {
            Name = "MapTitle", Text = title, SizeFlagsHorizontal = SizeFlags.ExpandFill,
            AutowrapMode = TextServer.AutowrapMode.WordSmart, TooltipText = string.Join("\n", tooltip),
            MouseFilter = MouseFilterEnum.Pass,
        });
        details.AddChild(new Label { Text = category, CustomMinimumSize = new Vector2(110f, 0f), Modulate = UiStyle.Dim, AutowrapMode = TextServer.AutowrapMode.WordSmart });
        details.AddChild(new Label
        {
            Text = map is null ? string.Empty : Mb(map.DownloadSize()), CustomMinimumSize = new Vector2(80f, 0f),
            HorizontalAlignment = HorizontalAlignment.Right, Modulate = UiStyle.Dim,
        });

        ContentPacks.DownloadJob? job = ContentPacks.Job(code);
        var status = new Label
        {
            Text = StatusText(state, map, pack), SizeFlagsHorizontal = SizeFlags.ExpandFill,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Modulate = state switch
            {
                MapState.Downloaded => UiStyle.MenuFocus,
                MapState.UpdateAvailable => Colors.Orange,
                MapState.Available => UiStyle.Text,
                _ => UiStyle.Dim,
            },
        };
        if (job is { Running: false, Error: { } error })
        {
            status.Text = error == "stopped" ? "Stopped" : "Failed: " + error;
            status.TooltipText = error;
            status.MouseFilter = MouseFilterEnum.Pass;
            status.Modulate = Colors.Orange;
        }

        summary.AddChild(status);

        var buttons = new HBoxContainer { CustomMinimumSize = new Vector2(190f, 0f), Alignment = BoxContainer.AlignmentMode.End };
        buttons.AddThemeConstantOverride("separation", 6);
        details.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        details.AddChild(buttons);
        if (job is { Running: true } running)
        {
            buttons.AddChild(SmallButton("Stop", running.Cancel));
        }
        else
        {
            if (map is not null && !ContentPacks.NeedsNativeLauncher(map) && (state is MapState.Available or MapState.UpdateAvailable ||
                OperatingSystem.IsBrowser() && (state == MapState.InGame || job?.Error is not null)))
            {
                buttons.AddChild(SmallButton(job?.Error is not null ? "Retry" : state == MapState.UpdateAvailable ? "Update" :
                    state == MapState.InGame ? "Reinstall" : ContentPacks.InstallAction(map), () =>
                {
                    ContentPacks.Download(map);
                    ShowStatus($"Downloading {title}...");
                    Fill();
                }));
            }

            if (pack is not null)
            {
                buttons.AddChild(SmallButton("Remove", () => AskRemove(code, title)));
            }
        }

        var bar = new UiProgress { Name = "Progress", Visible = job is { Running: true }, Value = (float)(job?.Fraction ?? 0) };
        column.AddChild(bar);
        _views.Add(new RowView(code, JobState(job), status, bar, row));
        return RowFrame(column);
    }

    private static string StatusText(MapState state, ContentMap? map, InstalledContent? pack) => state switch
    {
        MapState.Downloaded => "Downloaded" + Version(pack!.Version),
        MapState.UpdateAvailable => "Update available" + Version(map!.Version),
        MapState.InGame => "In your converted game files",
        MapState.Unlisted => "Downloaded (no longer listed)",
        _ => map is not null && ContentPacks.NeedsNativeLauncher(map) ? "Build from Steam in the native launcher" : "Not downloaded",
    };

    // ---- actions ---------------------------------------------------------------------------------------------------

    private void AskRemove(string code, string title)
    {
        ConfirmBox.Open(this, new ConfirmBox
        {
            Name = "ConfirmRemove",
            Question = $"Remove {title} from this computer?",
            NoCaption = "Cancel",
            YesCaption = "Remove",
            Yes = () =>
            {
                try
                {
                    ContentPacks.Remove(code);
                    ShowStatus($"Removed {title}.");
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    ShowStatus($"Could not remove {title}: {e.Message}");
                }

                Fill();
            },
        });
    }

    private void OpenFolder()
    {
        try
        {
            Directory.CreateDirectory(ContentPaths.PacksFolder);
            OS.ShellOpen(ContentPaths.PacksFolder);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            ShowStatus("Could not open the folder: " + e.Message);
        }
    }

    private static Control RowFrame(VBoxContainer column)
    {
        var frame = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var style = (StyleBox)UiStyle.ThumbBox().Duplicate();
        style.ContentMarginLeft = style.ContentMarginRight = 12f;
        style.ContentMarginTop = style.ContentMarginBottom = 10f;
        frame.AddThemeStyleboxOverride("panel", style);
        frame.AddChild(column);
        foreach (Node node in column.FindChildren("*", nameof(Label), true, false))
            if (node is Label label) label.AddThemeFontSizeOverride("font_size", label.Name.ToString() == "MapTitle" ? 16 : 14);
        return frame;
    }

    private static Button SmallButton(string text, Action pressed)
    {
        var b = new Button { Text = text, Name = text, CustomMinimumSize = new Vector2(88f, 34f) };
        b.AddThemeFontSizeOverride("font_size", 14);
        b.Pressed += pressed;
        return b;
    }

    private static string Version(string version) => version.Length > 0 ? " (" + version + ")" : string.Empty;

    private static string Mb(long bytes) => string.Create(CultureInfo.InvariantCulture, $"{bytes / 1048576.0:0.0} MB");

    private void ShowStatus(string text)
    {
        if (_status is not null)
        {
            _status.Text = text;
        }
    }
}
