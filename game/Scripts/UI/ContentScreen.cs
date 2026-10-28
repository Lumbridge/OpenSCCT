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
/// Content (OpenSCCT's own, docs/CONTENT-PACKS.md): the maps and shared packs of OpenSCCT-Content, with what this
/// computer has. Download, Update, Stop and Remove work as the launcher's Maps window does, through
/// <see cref="ContentPacks"/>, so a download started here is the same one the lobby and the hub show.
/// </summary>
public partial class ContentScreen : MenuPage
{
    private const string AllCategories = "All categories";

    private VBoxContainer? _maps;
    private VBoxContainer? _shared;
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
        UiStyle.MenuButton("Content", () => FrontEnd.ShowContent(from.GetTree()), "Content");

    /// <summary>One row's live parts: what it was built with, and the bits a running download updates.</summary>
    private sealed record RowView(string Key, bool Shared, string? BuiltWith, Label Status, UiProgress Bar);

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
        BuildFrame("Content", 980f);

        VBoxContainer where = Panel(Body, "Where", "Your content");
        _where = new Label { Name = "WhereText", AutowrapMode = TextServer.AutowrapMode.WordSmart, Modulate = UiStyle.Dim };
        where.AddChild(_where);

        VBoxContainer maps = Panel(Body, "Maps", "Maps");
        var filters = new HBoxContainer { Name = "Filters" };
        filters.AddThemeConstantOverride("separation", 12);
        maps.AddChild(filters);
        _search = new LineEdit { Name = "Search", PlaceholderText = "Search maps", SizeFlagsHorizontal = SizeFlags.ExpandFill, ClearButtonEnabled = true };
        _search.TextChanged += _ => Fill();
        filters.AddChild(_search);
        _category = new OptionButton { Name = "Category", CustomMinimumSize = new Vector2(200f, 0f) };
        _category.AddItem(AllCategories);
        _category.ItemSelected += _ => Fill();
        filters.AddChild(_category);
        _downloadedOnly = new CheckBox { Name = "DownloadedOnly", Text = "On this computer" };
        _downloadedOnly.Toggled += _ => Fill();
        filters.AddChild(_downloadedOnly);

        var mapScroll = new ScrollContainer { CustomMinimumSize = new Vector2(0f, 300f), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        maps.AddChild(mapScroll);
        _maps = new VBoxContainer { Name = "MapRows", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        mapScroll.AddChild(_maps);

        VBoxContainer shared = Panel(Body, "Shared", "Shared packs (characters, sounds, the game's common files)");
        _shared = new VBoxContainer { Name = "SharedRows", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        shared.AddChild(_shared);

        _status = new Label { Name = "Status", Modulate = UiStyle.MenuFocus, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        Body.AddChild(_status);

        PageButton("Back", () => FrontEnd.ShowMainMenu(GetTree()), "Back");
        PageButton("Open Folder", OpenFolder, "OpenFolder");
        PageButton("Refresh", () =>
        {
            ContentPacks.RefreshIndex(force: true);
            ShowStatus("Loading the content list...");
            _shownIndex = null;
            Fill();
        }, "Refresh");

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
            if (JobOf(view) is { Running: true } job)
            {
                view.Status.Text = job.Describe();
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
        view.Shared ? ContentPacks.SharedJob(view.Key) : ContentPacks.Job(view.Key);

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
        if (_maps is null || _shared is null)
        {
            return;
        }

        _shownIndex = ContentPacks.Index;
        _shownError = ContentPacks.IndexError;
        _generation = ContentPacks.Generation;
        _views.Clear();
        Clear(_maps);
        Clear(_shared);
        ShowWhere();

        ContentStore store = ContentPacks.Store;
        IReadOnlyList<InstalledContent> installed = store.Installed();
        FillCategories(_shownIndex);
        FillMaps(store, installed);
        FillShared(store, installed);

        if (_shownIndex is null)
        {
            ShowStatus(_shownError is { } e
                ? "Could not read the content list (" + e + "). Maps already on this computer are listed; press Refresh to try again."
                : "Loading the content list...");
        }
        else if (_status?.Text.StartsWith("Loading", StringComparison.Ordinal) == true || _status?.Text.Length == 0)
        {
            int count = _shownIndex.PlayableMaps().Count();
            int have = installed.Count(c => !c.IsShared);
            ShowStatus($"{count} maps on OpenSCCT-Content, {have} downloaded." + (_shownIndex.Problem is { } p ? " " + p : string.Empty));
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

        string own = ContentPaths.Root is { } root
            ? "Converted from your game: " + root
            : ContentPacks.UsesOwnSharedContent
                ? "Converted from your game: this checkout's .content"
                : "No converted game files: the shared packs below supply what maps need.";
        _where.Text = own + "\nDownloads: " + ContentPaths.PacksFolder;
    }

    private void FillCategories(ContentIndex? index)
    {
        if (_category is null || index is null || _category.ItemCount > 1)
        {
            return;
        }

        foreach (string c in index.PlayableMaps().Select(m => m.Category).Where(c => c.Length > 0).Distinct()
            .OrderBy(ContentIndex.CategoryRank).ThenBy(c => c, StringComparer.Ordinal))
        {
            _category.AddItem(c);
        }
    }

    private void FillMaps(ContentStore store, IReadOnlyList<InstalledContent> installed)
    {
        var packs = installed.Where(p => !p.IsShared).ToDictionary(p => p.Code, StringComparer.OrdinalIgnoreCase);
        var rows = new List<(ContentMap? Map, InstalledContent? Pack)>();
        if (_shownIndex is not null)
        {
            foreach (ContentMap m in _shownIndex.PlayableMaps().OrderBy(m => ContentIndex.CategoryRank(m.Category))
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
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);
        column.AddChild(row);

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

        row.AddChild(new Label
        {
            Text = title, SizeFlagsHorizontal = SizeFlags.ExpandFill, ClipText = true, TooltipText = string.Join("\n", tooltip),
            MouseFilter = MouseFilterEnum.Pass,
        });
        row.AddChild(new Label { Text = category, CustomMinimumSize = new Vector2(110f, 0f), Modulate = UiStyle.Dim });
        row.AddChild(new Label
        {
            Text = map is null ? string.Empty : Mb(map.Full.Size), CustomMinimumSize = new Vector2(80f, 0f),
            HorizontalAlignment = HorizontalAlignment.Right, Modulate = UiStyle.Dim,
        });

        ContentPacks.DownloadJob? job = ContentPacks.Job(code);
        var status = new Label
        {
            Text = StatusText(state, map, pack), CustomMinimumSize = new Vector2(260f, 0f), ClipText = true,
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

        row.AddChild(status);

        var buttons = new HBoxContainer { CustomMinimumSize = new Vector2(190f, 0f), Alignment = BoxContainer.AlignmentMode.End };
        buttons.AddThemeConstantOverride("separation", 6);
        row.AddChild(buttons);
        if (job is { Running: true } running)
        {
            buttons.AddChild(SmallButton("Stop", running.Cancel));
        }
        else
        {
            if (map is not null && state is MapState.Available or MapState.UpdateAvailable)
            {
                buttons.AddChild(SmallButton(state == MapState.UpdateAvailable ? "Update" : "Download", () =>
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
        _views.Add(new RowView(code, false, JobState(job), status, bar));
        return column;
    }

    private static string StatusText(MapState state, ContentMap? map, InstalledContent? pack) => state switch
    {
        MapState.Downloaded => "Downloaded" + Version(pack!.Version),
        MapState.UpdateAvailable => "Update available" + Version(map!.Version),
        MapState.InGame => "In your converted game files",
        MapState.Unlisted => "Downloaded (no longer listed)",
        _ => "Not downloaded",
    };

    private void FillShared(ContentStore store, IReadOnlyList<InstalledContent> installed)
    {
        bool own = ContentPacks.UsesOwnSharedContent;
        if (_shownIndex is null || _shownIndex.Shared.Count == 0)
        {
            int have = installed.Count(c => c.IsShared);
            _shared!.AddChild(new Label
            {
                Text = (have > 0 ? $"{have} shared packs downloaded." : "No shared packs downloaded.") +
                    (own ? " Your converted game files supply the shared content." : string.Empty),
                Modulate = UiStyle.Dim,
            });
            return;
        }

        foreach (ContentSharedPack pack in _shownIndex.Shared)
        {
            _shared!.AddChild(SharedRow(store, installed, pack, own));
        }
    }

    private Control SharedRow(ContentStore store, IReadOnlyList<InstalledContent> installed, ContentSharedPack pack, bool own)
    {
        var column = new VBoxContainer { Name = "Pack" };
        column.AddThemeConstantOverride("separation", 2);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);
        column.AddChild(row);

        string title = pack.Title.Length > 0 ? pack.Title : pack.Id;
        bool has = store.HasShared(pack);
        bool older = !has && installed.Any(c => c.IsShared && string.Equals(c.Code, pack.Id, StringComparison.OrdinalIgnoreCase));
        row.AddChild(new Label
        {
            Text = title, SizeFlagsHorizontal = SizeFlags.ExpandFill, ClipText = true, MouseFilter = MouseFilterEnum.Pass,
            TooltipText = pack.Contents.Count > 0 ? "Holds " + string.Join(", ", pack.Contents) : pack.Id,
        });
        row.AddChild(new Label
        {
            Text = Mb(pack.Download.Size), CustomMinimumSize = new Vector2(80f, 0f), HorizontalAlignment = HorizontalAlignment.Right,
            Modulate = UiStyle.Dim,
        });

        ContentPacks.DownloadJob? job = ContentPacks.SharedJob(pack.Id);
        var status = new Label
        {
            CustomMinimumSize = new Vector2(260f, 0f), ClipText = true,
            Text = has ? "Downloaded" + Version(pack.Version)
                : older ? "Update available" + Version(pack.Version)
                : own ? "Not needed (your game files have it)"
                : "Not downloaded (maps download it when needed)",
            Modulate = has ? UiStyle.MenuFocus : older ? Colors.Orange : UiStyle.Dim,
        };
        if (job is { Running: false, Error: { } error })
        {
            status.Text = error == "stopped" ? "Stopped" : "Failed: " + error;
            status.TooltipText = error;
            status.MouseFilter = MouseFilterEnum.Pass;
            status.Modulate = Colors.Orange;
        }

        row.AddChild(status);
        var buttons = new HBoxContainer { CustomMinimumSize = new Vector2(190f, 0f), Alignment = BoxContainer.AlignmentMode.End };
        row.AddChild(buttons);
        if (job is { Running: true } running)
        {
            buttons.AddChild(SmallButton("Stop", running.Cancel));
        }
        else if (!has)
        {
            buttons.AddChild(SmallButton(older ? "Update" : "Download", () =>
            {
                ContentPacks.DownloadShared(pack);
                ShowStatus($"Downloading {title}...");
                Fill();
            }));
        }

        var bar = new UiProgress { Name = "Progress", Visible = job is { Running: true }, Value = (float)(job?.Fraction ?? 0) };
        column.AddChild(bar);
        _views.Add(new RowView(pack.Id, true, JobState(job), status, bar));
        return column;
    }

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

    private static Button SmallButton(string text, Action pressed)
    {
        var b = new Button { Text = text, Name = text };
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
