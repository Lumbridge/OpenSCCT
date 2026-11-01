using System;
using System.Collections.Generic;
using Godot;
using OpenScct.Core;
using OpenScct.Provenance;
using OpenScct.Server;
using OpenScctGame.Hud;
using OpenScctGame.Play.Session;
using OpenScctGame.Tutorials;

namespace OpenScctGame.UI;

/// <summary>
/// Visit a Map (GUI.Visit_Map_Set): pick a map (the host list's maps that play Story and are installed, AquaE first
/// selected as the page does) and a side (Switch Team, with the side's picture), then Launch: the map alone with the
/// help paths shown (the profile's in-game help on), the offline launch rules, no bots.
/// </summary>
public partial class VisitMapScreen : MenuPage
{
    private const string Section = "Visit_Map_Set";

    [Provenance(ProvenanceKind.Decoded, "GUI.Visit_Map_Set.InitComponent: SetWithMapName(\"AquaE\")")]
    private const string FirstMap = "AquaE";

    /// <summary>The frame's title and buttons (78 each) and the map panel's caption, map name and padding above the list.</summary>
    private const float ChromeHeight = 156f + 96f;

    private static string? _lastMap;
    private static PlaySide _lastSide = PlaySide.Spy;

    private readonly ButtonGroup _group = new();
    private TextureRect? _portrait;
    private Label? _side;
    private Label? _mapName;
    private Button? _launch;
    private ScrollContainer? _scroll;
    private string? _map;

    public static VisitMapScreen Show(SceneTree tree) => FrontEnd.Show(tree, new VisitMapScreen());

    public override void _Ready()
    {
        Name = "VisitMap";
        BuildFrame(TutorialText.Caption(Section, "TitlePage", "Visit a Map"), 1100f);
        var row = new HBoxContainer { Name = "Row" };
        row.AddThemeConstantOverride("separation", 24);
        Body.AddChild(row);

        VBoxContainer sideColumn = Panel(row, "Side", null, 260f);
        _portrait = new TextureRect
        {
            Name = "Portrait", CustomMinimumSize = new Vector2(240f, 300f), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        };
        sideColumn.AddChild(_portrait);
        _side = new Label { Name = "SideName", HorizontalAlignment = HorizontalAlignment.Center };
        sideColumn.AddChild(_side);

        VBoxContainer mapColumn = Panel(row, "Maps", TutorialText.Caption(Section, "ImageMap.Caption", "Choose Map"));
        mapColumn.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        ((Control)mapColumn.GetParent()).SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _mapName = new Label { Name = "MapName", Modulate = UiStyle.MenuFocus };
        mapColumn.AddChild(_mapName);
        var scroll = _scroll = new ScrollContainer
        {
            Name = "MapScroll", CustomMinimumSize = new Vector2(0f, 360f), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        mapColumn.AddChild(scroll);
        var grid = new MapBrowserGrid();
        mapColumn.AddChild(grid.ViewToggle);
        mapColumn.MoveChild(grid.ViewToggle, 1);
        scroll.AddChild(grid);

        MapCatalog maps = MapImages.Default;
        List<MapCatalogEntry> visitable = Visitable(maps);
        foreach (MapCatalogEntry entry in visitable)
        {
            Tile(grid, maps, entry.Id);
        }

        if (visitable.Count == 0)
        {
            mapColumn.AddChild(new Label { Text = "No converted map to visit (convert the maps first).", Modulate = UiStyle.Dim });
        }

        PageButton("Back", () => TutorialsScreen.Show(GetTree()), "Back");
        PageButton(TutorialText.Caption(Section, "ButtonY", "Switch Team"), SwitchTeam, "SwitchTeam");
        _launch = PageButton(TutorialText.Caption(Section, "ButtonA", "Launch"), Launch, "Launch");

        string? pick = _lastMap is not null && visitable.Exists(e => Same(e.Id, _lastMap)) ? _lastMap
            : visitable.Exists(e => Same(e.Id, FirstMap)) ? FirstMap
            : visitable.Count > 0 ? visitable[0].Id : null;
        Select(pick);
        ShowSide();
        GetViewport().SizeChanged += Fit;
        Fit();
        _launch.GrabFocus();
    }

    public override void _ExitTree()
    {
        if (GetViewport() is { } viewport)
        {
            viewport.SizeChanged -= Fit;
        }
    }

    /// <summary>The page fills the window: the body spans most of its width and the map list the height left over.</summary>
    private void Fit()
    {
        Vector2 size = GetViewportRect().Size;
        Body.CustomMinimumSize = new Vector2(Math.Max(1100f, size.X - 160f), 0f);
        _scroll!.CustomMinimumSize = new Vector2(0f, Math.Max(360f, size.Y - ChromeHeight));
    }

    /// <summary>The maps a visit can play: the host list's maps with Story that this machine can render.</summary>
    public List<MapCatalogEntry> Visitable(MapCatalog maps)
    {
        var list = new List<MapCatalogEntry>();
        foreach (MapCatalogEntry entry in maps.GetHostMaps())
        {
            if (entry.Mode(EGameMode.GM_Multi) is { Playable: true } && TutorialPlay.CanPlay(GetTree(), entry.Id))
            {
                list.Add(entry);
            }
        }

        return list;
    }

    private Button Tile(MapBrowserGrid grid, MapCatalog maps, string id)
    {
        string title = MapTitles.Of(maps, id);
        var tile = grid.AddMap(maps, id, title);
        tile.ToggleMode = true;
        tile.ButtonGroup = _group;
        tile.Pressed += () => Select(id);
        tile.SetMeta("map", id);
        return tile;
    }

    private void Select(string? id)
    {
        _map = id;
        _lastMap = id ?? _lastMap;
        if (_mapName is not null)
        {
            _mapName.Text = id is null ? string.Empty : MapTitles.Of(MapImages.Default, id);
        }

        foreach (Node node in FindChildren("Map_*", "Button", true, false))
        {
            if (node is Button b)
            {
                b.SetPressedNoSignal(id is not null && Same(b.GetMeta("map").AsString(), id));
            }
        }

        if (_launch is not null)
        {
            _launch.Disabled = id is null;
        }
    }

    /// <summary>Visit_Map_Set's Switch Team: the profile's side flips and the side's picture changes.</summary>
    [PortOf("uc:GUI.Visit_Map_Set:InternalOnKeyEvent", Spec = "spec/game/tutorials.md#rules")]
    private void SwitchTeam()
    {
        _lastSide = _lastSide == PlaySide.Spy ? PlaySide.Merc : PlaySide.Spy;
        ShowSide();
    }

    private void ShowSide()
    {
        bool merc = _lastSide == PlaySide.Merc;
        if (_portrait is not null)
        {
            // TeamInfo.MaterialOfMesh[0]: the side's picture, the equipment page's portrait here (character art: both looks).
            _portrait.Texture = UiStyle.Assets?.Texture("GUIContent", merc ? "MN_DEFperso" : "MN_ATTperso");
        }

        if (_side is not null)
        {
            _side.Text = merc ? "Mercenary" : "Spy";
            _side.Modulate = UiStyle.SideColour(merc ? HudSide.Merc : HudSide.Spy);
        }
    }

    private void Launch()
    {
        if (_map is { } map)
        {
            TutorialPlay.Visit(GetTree(), map, _lastSide);
        }
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
