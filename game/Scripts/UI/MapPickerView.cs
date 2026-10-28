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
/// as the arrows. Above the grid, Reloaded's playlist choice (rl:qol.lobbymaps: Standard, Extended, Classic, 3v3, Merc
/// Deathmatch, All installed maps; the arrows then step through the playlist) and a search box (map name or code).
/// Node names: <c>Playlist</c>, <c>MapSearch</c>, <c>MapGrid</c>, <c>Map_&lt;id&gt;</c>.
/// </summary>
public partial class MapPickerView : VBoxContainer
{
    public static readonly Vector2 ThumbSize = new(132f, 132f);

    private readonly Dictionary<string, (Button Tile, MapCatalogEntry? Entry)> _tiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly ButtonGroup _group = new();
    private readonly List<MapPlaylist> _playlists = new();
    private readonly HashSet<string> _inPlaylist = new(StringComparer.OrdinalIgnoreCase);
    private MapPlaylist? _playlist;
    private string _search = string.Empty;
    private string? _shownMap;

    /// <param name="installed">The maps the model steps through (id and title).</param>
    /// <param name="playlist">The playlist shown first (a catalog playlist name; an unknown one shows every map).</param>
    /// <param name="playlistChosen">Told the name when the player picks another playlist (to remember it).</param>
    public MapPickerView(CreateSessionModel model, MapCatalog maps, IReadOnlyList<(string Id, string Title)> installed, float height = 330f,
        string? playlist = null, Action<string>? playlistChosen = null)
    {
        Model = model ?? throw new ArgumentNullException(nameof(model));
        ArgumentNullException.ThrowIfNull(maps);
        ArgumentNullException.ThrowIfNull(installed);
        Name = "MapPicker";
        BuildFilterBar(maps, playlist, playlistChosen);
        var scroll = new ScrollContainer
        {
            Name = "MapScroll", CustomMinimumSize = new Vector2(0f, height), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        AddChild(scroll);
        Grid = new HFlowContainer { Name = "MapGrid", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        Grid.AddThemeConstantOverride("h_separation", 8);
        Grid.AddThemeConstantOverride("v_separation", 8);
        scroll.AddChild(Grid);

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

        model.Changed += Refresh;
        Refresh();
    }

    public CreateSessionModel Model { get; }

    public HFlowContainer Grid { get; }

    /// <summary>The host edits; others only see the list.</summary>
    public bool Editable { get; set; } = true;

    /// <summary>The tile of a map, or null (tests and the menu pilot).</summary>
    public Button? Tile(string map) => _tiles.TryGetValue(map, out var t) ? t.Tile : null;

    public override void _ExitTree() => Model.Changed -= Refresh;

    /// <summary>
    /// The Choose Map arrows' filter for a playlist name (<see cref="CreateSessionModel.MapFilter"/>): its maps, or null
    /// for "All" and names the catalog does not have.
    /// </summary>
    public static Func<string, bool>? FilterFor(MapCatalog maps, string? playlist)
    {
        ArgumentNullException.ThrowIfNull(maps);
        foreach (MapPlaylist p in maps.GetPlaylists())
        {
            if (p.Name != MapCatalog.AllMapsPlaylist && string.Equals(p.Name, playlist, StringComparison.OrdinalIgnoreCase))
            {
                var set = new HashSet<string>(p.Maps, StringComparer.OrdinalIgnoreCase);
                return set.Contains;
            }
        }

        return null;
    }

    /// <summary>The playlist shown (null: every map).</summary>
    public MapPlaylist? Playlist => _playlist;

    public OptionButton? PlaylistBox { get; private set; }

    public LineEdit? SearchBox { get; private set; }

    /// <summary>The playlist choice and the search box.</summary>
    private void BuildFilterBar(MapCatalog maps, string? playlist, Action<string>? chosen)
    {
        var bar = new HBoxContainer { Name = "MapFilter" };
        bar.AddThemeConstantOverride("separation", 12);
        AddChild(bar);
        _playlists.AddRange(maps.GetPlaylists());
        bar.AddChild(new Label { Text = "Playlist", VerticalAlignment = VerticalAlignment.Center, Modulate = UiStyle.MenuFocus });
        var box = new OptionButton { Name = "Playlist", CustomMinimumSize = new Vector2(240f, 0f), FocusMode = FocusModeEnum.All };
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
            Name = "MapSearch", PlaceholderText = "Search maps", ClearButtonEnabled = true, SizeFlagsHorizontal = SizeFlags.ExpandFill,
            MaxLength = 40,
        };
        SearchBox.TextChanged += text =>
        {
            _search = text.Trim();
            Refresh();
        };
        bar.AddChild(SearchBox);
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

        var tile = new Button
        {
            Name = "Map_" + id, ToggleMode = true, ButtonGroup = _group, TooltipText = title + " (" + id + ")",
            CustomMinimumSize = new Vector2(ThumbSize.X + 12f, ThumbSize.Y + 40f), FocusMode = FocusModeEnum.All, ThemeTypeVariation = UiStyle.TileVariation,
        };
        var column = new VBoxContainer { Name = "Column", MouseFilter = MouseFilterEnum.Ignore };
        column.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect, LayoutPresetMode.Minsize, 6);
        var thumb = new MapThumb(ThumbSize) { SizeFlagsHorizontal = SizeFlags.ShrinkCenter };
        thumb.SetMap(MapImages.Preview(maps, id), title);
        column.AddChild(thumb);
        column.AddChild(new Label
        {
            Name = "Title", Text = title, HorizontalAlignment = HorizontalAlignment.Center, ClipText = true,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, CustomMinimumSize = new Vector2(ThumbSize.X, 0f),
            MouseFilter = MouseFilterEnum.Ignore, LabelSettings = new LabelSettings { FontSize = 12, FontColor = UiStyle.MenuText },
        });
        tile.AddChild(column);
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
        Grid.AddChild(tile);
        _tiles[id] = (tile, entry);
    }

    /// <summary>Shows the maps of the selected mode and marks the selected map.</summary>
    public void Refresh()
    {
        EGameMode mode = Model.Setup.Mode;
        string map = Model.Setup.Map;
        foreach ((string id, (Button tile, MapCatalogEntry? entry)) in _tiles)
        {
            bool on = string.Equals(id, map, StringComparison.OrdinalIgnoreCase);
            tile.Visible = on || (entry?.Mode(mode) is not { Playable: false } && InFilter(id) && MatchesSearch(id, entry, tile));
            tile.SetPressedNoSignal(on);
        }

        if (!string.Equals(_shownMap, map, StringComparison.OrdinalIgnoreCase) && _tiles.TryGetValue(map, out var selected))
        {
            _shownMap = map;
            Button tile = selected.Tile;
            Callable.From(() =>
            {
                if (IsInstanceValid(tile) && tile.IsInsideTree() && Grid.GetParent() is ScrollContainer scroll)
                {
                    scroll.EnsureControlVisible(tile);
                }
            }).CallDeferred();
        }
    }
}
