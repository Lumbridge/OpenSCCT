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

    private static string? _lastMap;
    private static PlaySide _lastSide = PlaySide.Spy;

    private readonly ButtonGroup _group = new();
    private TextureRect? _portrait;
    private Label? _side;
    private Label? _mapName, _mapLegend;
    private Button? _launch;
    private ScrollContainer? _scroll;
    private string? _map;
    private BoxContainer? _row, _sideContent;

    protected override bool CenterBodyVertically => true;
    protected override Vector2 ResponsiveReference => new(1600f, 900f);

    public static VisitMapScreen Show(SceneTree tree) => FrontEnd.Show(tree, new VisitMapScreen());

    public override void _Ready()
    {
        Name = "VisitMap";
        BuildFrame(TutorialText.Caption(Section, "TitlePage", "Visit a Map"), 1180f, responsive: true);
        Body.AddThemeConstantOverride("separation", 14);
        var row = _row = new BoxContainer { Name = "Row" };
        row.AddThemeConstantOverride("separation", 14);
        Body.AddChild(row);

        VBoxContainer sideColumn = Panel(row, "Side", "Your role");
        sideColumn.AddThemeConstantOverride("separation", 10);
        _sideContent = new BoxContainer { Name = "SideContent", Vertical = true };
        _sideContent.AddThemeConstantOverride("separation", 14);
        sideColumn.AddChild(_sideContent);
        _portrait = new TextureRect
        {
            Name = "Portrait", CustomMinimumSize = new Vector2(192f, 240f), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        };
        _sideContent.AddChild(_portrait);
        var role = new VBoxContainer { Name = "Role", SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ShrinkCenter };
        role.AddThemeConstantOverride("separation", 10);
        _sideContent.AddChild(role);
        _side = new Label { Name = "SideName", HorizontalAlignment = HorizontalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        role.AddChild(_side);
        role.AddChild(new Label
        {
            Name = "PracticeHelp", Text = "Explore alone with help paths and unlimited equipment.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart, Modulate = UiStyle.Dim,
        });

        VBoxContainer mapColumn = Panel(row, "Maps", TutorialText.Caption(Section, "ImageMap.Caption", "Choose Map"));
        mapColumn.AddThemeConstantOverride("separation", 10);
        mapColumn.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        ((Control)mapColumn.GetParent()).SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _mapName = new Label { Name = "MapName", Modulate = UiStyle.MenuFocus, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        mapColumn.AddChild(_mapName);
        _mapLegend = MapLegend.Create();
        mapColumn.AddChild(_mapLegend);
        var scroll = _scroll = new ScrollContainer
        {
            Name = "MapScroll", FollowFocus = true, CustomMinimumSize = new Vector2(0f, 360f), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
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
            mapColumn.AddChild(new Label { Text = "No converted map to visit (convert the maps first).", Modulate = UiStyle.Dim, AutowrapMode = TextServer.AutowrapMode.WordSmart });
        }

        PageButton("Back", () => TutorialsScreen.Show(GetTree()), "Back");
        PageButton(TutorialText.Caption(Section, "ButtonY", "Switch Team"), SwitchTeam, "SwitchTeam");
        _launch = PageButton(TutorialText.Caption(Section, "ButtonA", "Launch"), Launch, "Launch");

        string? pick = _lastMap is not null && visitable.Exists(e => Same(e.Id, _lastMap)) ? _lastMap
            : visitable.Exists(e => Same(e.Id, FirstMap)) ? FirstMap
            : visitable.Count > 0 ? visitable[0].Id : null;
        Select(pick);
        ShowSide();
        Fit();
        _launch.GrabFocus();
    }

    public override void _Process(double delta)
    {
        base._Process(delta);
        Fit();
    }

    /// <summary>Fit the role card and map list in the same logical canvas as the other menu pages.</summary>
    private void Fit()
    {
        bool stacked = Math.Min(PreferredBodyWidth, AvailableBodyWidth) < 940f;
        _row!.Vertical = stacked;
        _sideContent!.Vertical = !stacked;
        _portrait!.CustomMinimumSize = stacked ? new Vector2(80f, 100f) : new Vector2(192f, 240f);
        ((Control)_sideContent.GetParent().GetParent()).CustomMinimumSize = new Vector2(stacked ? 0f : 260f, 0f);
        _scroll!.CustomMinimumSize = new Vector2(0f, Math.Clamp(LayoutSize.Y - (stacked ? 420f : 280f), 220f, 560f));
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

        if (_mapLegend is not null)
        {
            MapLegend.Show(_mapLegend, MapImages.Default, id);
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
