using System;
using System.Collections.Generic;
using Godot;
using OpenScct.Core;
using OpenScct.Server;

namespace OpenScctGame.UI;

/// <summary>
/// Create Session's and the lobby's map list (ours: the original picks the map with the Choose Map arrows only): every
/// hostable map (<see cref="MapCatalog.GetHostMaps"/>) that is installed and plays the selected mode, as a scrolling
/// grid of pictures with the map's name under each; a click selects it through the same <see cref="CreateSessionModel"/>
/// as the arrows. Above the grid, a map group (All installed maps, Enhanced, Stock, Community or 3v3 optimised;
/// the arrows then step through that group) and a search box (map name or code).
/// Node names: <c>Playlist</c>, <c>MapSearch</c>, <c>MapGrid</c>, <c>Map_&lt;id&gt;</c>.
/// </summary>
public partial class MapPickerView : VBoxContainer
{
    public static readonly Vector2 ThumbSize = new(192f, 192f);

    private readonly Dictionary<string, (Button Tile, MapCatalogEntry? Entry)> _tiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly ButtonGroup _group = new();
    private readonly List<MapPlaylist> _playlists = new();
    private readonly HashSet<string> _inPlaylist = new(StringComparer.OrdinalIgnoreCase);
    private MapPlaylist? _playlist;
    private string _search = string.Empty;
    private string? _shownMap;
    private readonly bool _compact;
    private readonly ScrollContainer _scroll;
    private Label? _filterStatus;
    private Label? _empty;
    private Button? _resetFilters;
    private Button? _revealTile;
    private Rect2 _revealRect;
    private Vector2 _revealGridSize;
    private Vector2 _revealScrollSize;
    private int _revealStableFrames;

    /// <param name="installed">The maps the model steps through (id and title).</param>
    /// <param name="playlist">The map group shown first (an unknown name shows every map).</param>
    /// <param name="playlistChosen">Told the name when the player picks another group (to remember it).</param>
    /// <param name="downloadable">Maps the player does not have that can still be picked (the hub laptop's maps of
    /// OpenSCCT-Content), shown after the installed ones under the title given.</param>
    /// <param name="compact">Two filter rows and landscape cards for Create Session's narrower map column.</param>
    public MapPickerView(CreateSessionModel model, MapCatalog maps, IReadOnlyList<(string Id, string Title)> installed, float height = 330f,
        string? playlist = null, Action<string>? playlistChosen = null, IReadOnlyList<(string Id, string Title)>? downloadable = null,
        bool compact = false)
    {
        Model = model ?? throw new ArgumentNullException(nameof(model));
        ArgumentNullException.ThrowIfNull(maps);
        ArgumentNullException.ThrowIfNull(installed);
        Name = "MapPicker";
        _compact = compact;
        AddThemeConstantOverride("separation", compact ? 8 : 4);
        Grid = new MapBrowserGrid(compact);
        BuildFilterBar(maps, playlist, playlistChosen);
        _scroll = new ScrollContainer
        {
            // Do not propagate the grid's previous column widths as a minimum when the window shrinks.
            Name = "MapScroll", CustomMinimumSize = new Vector2(0f, height), HorizontalScrollMode = ScrollContainer.ScrollMode.ShowNever,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        AddChild(_scroll);
        _scroll.AddChild(Grid);
        if (compact)
        {
            _empty = new Label
            {
                Name = "MapEmpty", Text = "No maps match these filters.\nTry another name, map code or group.",
                CustomMinimumSize = new Vector2(0, height), HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart,
                SizeFlagsVertical = SizeFlags.ExpandFill, Modulate = UiStyle.Dim, Visible = false,
            };
            AddChild(_empty);
        }

        var have = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach ((string id, _) in installed)
        {
            have.Add(id);
        }

        foreach (MapCatalogEntry entry in maps.GetHostMaps())
        {
            if (have.Contains(entry.Id))
            {
                AddTile(entry.Id, MapTitles.Of(maps, entry.Id), entry, maps);
            }
        }

        // Without the catalog's host list (no catalog data installed): every map the arrows step through.
        if (_tiles.Count == 0)
        {
            foreach ((string id, string title) in installed)
            {
                AddTile(id, title, null, maps);
            }
        }

        foreach ((string id, string title) in downloadable ?? [])
        {
            AddTile(id, title, null, maps);
        }

        model.Changed += Refresh;
        Refresh();
    }

    public CreateSessionModel Model { get; }

    public MapBrowserGrid Grid { get; }

    /// <summary>The host edits; others only see the list.</summary>
    public bool Editable { get; set; } = true;

    /// <summary>The tile of a map, or null (tests and the menu pilot).</summary>
    public Button? Tile(string map) => _tiles.TryGetValue(map, out var t) ? t.Tile : null;

    public override void _ExitTree() => Model.Changed -= Refresh;

    /// <summary>
    /// The Choose Map arrows' filter for a group name (<see cref="CreateSessionModel.MapFilter"/>): its maps, or null
    /// for "All" and unknown names.
    /// </summary>
    public static Func<string, bool>? FilterFor(MapCatalog maps, string? playlist)
    {
        ArgumentNullException.ThrowIfNull(maps);
        foreach (MapPlaylist p in maps.GetMapFilters())
        {
            if (p.Name != MapCatalog.AllMapsPlaylist && string.Equals(p.Name, playlist, StringComparison.OrdinalIgnoreCase))
            {
                var set = new HashSet<string>(p.Maps, StringComparer.OrdinalIgnoreCase);
                return set.Contains;
            }
        }

        return null;
    }

    /// <summary>The map group shown (null: every map).</summary>
    public MapPlaylist? Playlist => _playlist;

    public OptionButton? PlaylistBox { get; private set; }

    public LineEdit? SearchBox { get; private set; }

    /// <summary>The map group choice and the search box.</summary>
    private void BuildFilterBar(MapCatalog maps, string? playlist, Action<string>? chosen)
    {
        var bar = new HBoxContainer { Name = "MapFilter" };
        bar.AddThemeConstantOverride("separation", _compact ? 8 : 12);
        if (!_compact) AddChild(bar);
        _playlists.AddRange(maps.GetMapFilters());
        if (!_compact)
            bar.AddChild(new Label { Text = "Map group", VerticalAlignment = VerticalAlignment.Center, Modulate = UiStyle.MenuFocus });
        var box = new OptionButton
        {
            Name = "Playlist", CustomMinimumSize = new Vector2(_compact ? 0f : 240f, _compact ? 36f : 0f),
            SizeFlagsHorizontal = _compact ? SizeFlags.ExpandFill : SizeFlags.Fill,
            FocusMode = FocusModeEnum.All, TooltipText = "Filter maps by group",
        };
        int selected = -1;
        for (int i = 0; i < _playlists.Count; i++)
        {
            MapPlaylist p = _playlists[i];
            box.AddItem(PlaylistTitle(p), i);
            box.SetItemTooltip(i, p.Description + (p.Missing.Count > 0 ? " Not installed: " + string.Join(", ", p.Missing) + "." : string.Empty));
            if (string.Equals(p.Name, playlist, StringComparison.OrdinalIgnoreCase))
            {
                selected = i;
            }
        }

        // An unknown or missing name (no catalog data) shows "All".
        if (selected < 0)
        {
            selected = _playlists.FindIndex(p => p.Name == MapCatalog.AllMapsPlaylist);
        }

        if (selected >= 0)
        {
            box.Select(selected);
            SetPlaylist(_playlists[selected]);
        }

        box.ItemSelected += index =>
        {
            if (index >= 0 && index < _playlists.Count)
            {
                SetPlaylist(_playlists[(int)index]);
                chosen?.Invoke(_playlists[(int)index].Name);
                Refresh();
            }
        };
        bar.AddChild(box);
        PlaylistBox = box;

        SearchBox = new LineEdit
        {
            Name = "MapSearch", PlaceholderText = _compact ? "Search by map name or code..." : "Search maps",
            ClearButtonEnabled = true, SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, _compact ? 38 : 0), MaxLength = 40,
            TooltipText = "Search installed maps by name or map code",
        };
        SearchBox.TextChanged += text =>
        {
            _search = text.Trim();
            Refresh();
        };
        if (_compact)
        {
            AddChild(SearchBox);
            AddChild(bar);
        }
        else bar.AddChild(SearchBox);
        bar.AddChild(Grid.ViewToggle);
        if (_compact)
        {
            var status = new HBoxContainer { Name = "MapFilterStatus" };
            AddChild(status);
            _filterStatus = new Label
            {
                Name = "MapCount", SizeFlagsHorizontal = SizeFlags.ExpandFill,
                VerticalAlignment = VerticalAlignment.Center, Modulate = UiStyle.Dim,
            };
            _filterStatus.AddThemeFontSizeOverride("font_size", 13);
            status.AddChild(_filterStatus);
            _resetFilters = new Button
            {
                Name = "ResetMapFilters", Text = "Reset filters", Flat = true,
                TooltipText = "Clear the search and show all installed maps",
            };
            _resetFilters.AddThemeFontSizeOverride("font_size", 13);
            _resetFilters.Pressed += () =>
            {
                SearchBox.Text = string.Empty;
                _search = string.Empty;
                int all = _playlists.FindIndex(p => p.Name == MapCatalog.AllMapsPlaylist);
                if (all >= 0)
                {
                    box.Select(all);
                    SetPlaylist(_playlists[all]);
                    chosen?.Invoke(_playlists[all].Name);
                }
                _shownMap = null;
                Refresh();
            };
            status.AddChild(_resetFilters);
        }
    }

    /// <summary>The playlist's name as the list shows it ("All" is every installed map).</summary>
    public static string PlaylistTitle(MapPlaylist p) => p.Name == MapCatalog.AllMapsPlaylist ? "All installed maps" : p.Name;

    private void SetPlaylist(MapPlaylist playlist)
    {
        _playlist = playlist.Name == MapCatalog.AllMapsPlaylist ? null : playlist;
        _inPlaylist.Clear();
        foreach (string m in playlist.Maps)
        {
            _inPlaylist.Add(m);
        }

        // A copy: the arrows keep the playlist after the list closes (the lobby's map panel).
        var set = new HashSet<string>(_inPlaylist, StringComparer.OrdinalIgnoreCase);
        Model.MapFilter = _playlist is null ? null : set.Contains;
    }

    private bool InFilter(string map) => _playlist is null || _inPlaylist.Contains(map);

    private bool MatchesSearch(string id, MapCatalogEntry? entry, Button tile) =>
        _search.Length == 0 || id.Contains(_search, StringComparison.OrdinalIgnoreCase) ||
        tile.TooltipText.Contains(_search, StringComparison.OrdinalIgnoreCase) ||
        (entry is not null && (entry.Name.Contains(_search, StringComparison.OrdinalIgnoreCase) ||
            entry.SortName.Contains(_search, StringComparison.OrdinalIgnoreCase)));

    private void AddTile(string id, string title, MapCatalogEntry? entry, MapCatalog maps)
    {
        if (_tiles.ContainsKey(id))
        {
            return;
        }

        var tile = Grid.AddMap(maps, id, title);
        tile.ToggleMode = true;
        tile.ButtonGroup = _group;
        tile.Pressed += () =>
        {
            if (Editable && !string.Equals(Model.Setup.Map, id, StringComparison.OrdinalIgnoreCase))
            {
                Model.SelectMap(id);
            }
            else
            {
                Refresh();
            }
        };
        _tiles[id] = (tile, entry);
    }

    /// <summary>Shows the maps of the selected mode and marks the selected map.</summary>
    public void Refresh()
    {
        EGameMode mode = Model.Setup.Mode;
        string map = Model.Setup.Map;
        bool selectedWasVisible = _tiles.TryGetValue(map, out var previous) && previous.Tile.Visible;
        int visible = 0;
        foreach ((string id, (Button tile, MapCatalogEntry? entry)) in _tiles)
        {
            bool on = string.Equals(id, map, StringComparison.OrdinalIgnoreCase);
            tile.Visible = entry?.Mode(mode) is not { Playable: false } && InFilter(id) && MatchesSearch(id, entry, tile);
            tile.SetPressedNoSignal(on);
            if (tile.Visible) visible++;
        }

        if (_filterStatus is not null)
        {
            _filterStatus.Text = visible == 1 ? "1 map available" : $"{visible} maps available";
            _resetFilters!.Visible = _search.Length > 0 || _playlist is not null;
            _empty!.Visible = visible == 0;
            _scroll.Visible = visible > 0;
        }

        if ((!string.Equals(_shownMap, map, StringComparison.OrdinalIgnoreCase) || (_compact && !selectedWasVisible)) &&
            _tiles.TryGetValue(map, out var selected))
        {
            _shownMap = map;
            Button tile = selected.Tile;
            if (_compact)
            {
                // A single deferred call runs before the responsive grid has found its final columns and card sizes.
                // Reveal once its geometry settles, then leave the user's scrolling alone until selection changes.
                _revealTile = tile;
                _revealStableFrames = 0;
                return;
            }
            Callable.From(() =>
            {
                if (IsInstanceValid(tile) && tile.IsInsideTree() && tile.Visible && Grid.GetParent() is ScrollContainer scroll)
                {
                    scroll.EnsureControlVisible(tile);
                }
            }).CallDeferred();
        }
    }

    public override void _Process(double delta)
    {
        if (_revealTile is not { } tile) return;
        if (!IsInstanceValid(tile) || !tile.Visible)
        {
            _revealTile = null;
            return;
        }
        if (!tile.IsVisibleInTree()) return;

        Rect2 rect = tile.GetRect();
        if (rect != _revealRect || Grid.Size != _revealGridSize || _scroll.Size != _revealScrollSize)
        {
            _revealRect = rect;
            _revealGridSize = Grid.Size;
            _revealScrollSize = _scroll.Size;
            _revealStableFrames = 0;
            return;
        }
        if (++_revealStableFrames < 3) return;
        _scroll.EnsureControlVisible(tile);
        _revealTile = null;
    }
}
