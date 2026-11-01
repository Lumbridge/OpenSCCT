using System;
using System.Collections.Generic;
using Godot;
using OpenScct.Server;
using OpenScctGame.Options;

namespace OpenScctGame.UI;

/// <summary>Responsive map cards and compact rows, with one saved preference for every map browser.</summary>
public partial class MapBrowserGrid : GridContainer
{
    private readonly List<(Button Button, VBoxContainer Content, MapThumb Picture, Label Title)> _cards = new();
    private float _width = -1;
    private bool? _list;
    public OptionButton ViewToggle { get; } = new() { Name = "MapView", TooltipText = "Map display", CustomMinimumSize = new Vector2(130, 0) };

    public MapBrowserGrid()
    {
        Name = "MapGrid";
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        AddThemeConstantOverride("h_separation", 8);
        AddThemeConstantOverride("v_separation", 8);
        ViewToggle.AddItem("Picture grid");
        ViewToggle.AddItem("List view");
        ViewToggle.Select(SettingsStore.Current.MapListView ? 1 : 0);
        ViewToggle.ItemSelected += index =>
        {
            SettingsStore.Current.MapListView = index == 1;
            SettingsStore.Save(SettingsStore.Current);
        };
    }

    public Button AddMap(MapCatalog maps, string id, string title)
    {
        var button = new Button
        {
            Name = "Map_" + id, TooltipText = title + " (" + id + ")", ClipText = true,
            SizeFlagsHorizontal = SizeFlags.ExpandFill, FocusMode = FocusModeEnum.All,
            ThemeTypeVariation = UiStyle.TileVariation,
        };
        var content = new VBoxContainer { Name = "Column", MouseFilter = MouseFilterEnum.Ignore };
        var picture = new MapThumb(new Vector2(160, 160)) { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        picture.SetMap(MapImages.Preview(maps, id), title);
        var label = new Label
        {
            Name = "Title", Text = title, HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, ClipText = true,
            AutowrapMode = TextServer.AutowrapMode.WordSmart, MaxLinesVisible = 2,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            CustomMinimumSize = new Vector2(0, 36), MouseFilter = MouseFilterEnum.Ignore,
            LabelSettings = new LabelSettings { FontSize = 12, FontColor = UiStyle.MenuText },
        };
        content.AddChild(picture);
        content.AddChild(label);
        button.AddChild(content);
        content.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect, LayoutPresetMode.Minsize, 6);
        AddChild(button);
        _cards.Add((button, content, picture, label));
        _width = -1;
        return button;
    }

    public override void _Process(double delta)
    {
        bool list = SettingsStore.Current.MapListView;
        // Reserve scrollbar space even before it appears, avoiding column-count oscillation.
        float width = GetParent() is ScrollContainer scroll ? scroll.Size.X - 18 : Size.X;
        width = Math.Max(172, width);
        if (_width == width && _list == list) return;
        _width = width;
        _list = list;
        ViewToggle.Select(list ? 1 : 0);
        Columns = list ? 1 : Math.Max(1, (int)((width + 8) / 180));
        float cardWidth = MathF.Floor((width - (Columns - 1) * 8) / Columns);
        foreach (var card in _cards)
        {
            card.Button.CustomMinimumSize = new Vector2(list ? 172 : cardWidth, list ? 40 : cardWidth + 48);
            card.Content.Visible = !list;
            card.Button.Text = list ? card.Title.Text : string.Empty;
            card.Button.Alignment = HorizontalAlignment.Left;
            card.Picture.CustomMinimumSize = new Vector2(0, cardWidth - 12);
        }
    }
}
