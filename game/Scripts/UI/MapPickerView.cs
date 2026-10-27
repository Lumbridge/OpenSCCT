using System;
using System.Collections.Generic;
using Godot;
using OpenScct.Core;
using OpenScct.Server;

namespace OpenScctGame.UI;

/// <summary>
/// Create Session's map list (ours: the original picks the map with the Choose Map arrows only): every hostable map
/// (<see cref="MapCatalog.GetHostMaps"/>) that is installed and plays the selected mode, as a scrolling grid of
/// pictures with the map's name under each; a click selects it through the same <see cref="CreateSessionModel"/> as
/// the arrows. Node names: <c>MapGrid</c>, <c>Map_&lt;id&gt;</c>.
/// </summary>
public partial class MapPickerView : VBoxContainer
{
    public static readonly Vector2 ThumbSize = new(132f, 132f);

    private readonly Dictionary<string, (Button Tile, MapCatalogEntry? Entry)> _tiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly ButtonGroup _group = new();
    private string? _shownMap;

    /// <param name="installed">The maps the model steps through (id and title).</param>
    public MapPickerView(CreateSessionModel model, MapCatalog maps, IReadOnlyList<(string Id, string Title)> installed, float height = 330f)
    {
        Model = model ?? throw new ArgumentNullException(nameof(model));
        ArgumentNullException.ThrowIfNull(maps);
        ArgumentNullException.ThrowIfNull(installed);
        Name = "MapPicker";
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
            tile.Visible = on || entry?.Mode(mode) is not { Playable: false };
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
