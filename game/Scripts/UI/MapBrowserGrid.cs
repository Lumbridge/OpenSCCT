using System;
using System.Collections.Generic;
using Godot;
using OpenScct.Server;
using OpenScctGame.Options;

namespace OpenScctGame.UI;

/// <summary>Responsive map cards, text lists and thumbnail lists, with one saved preference for every map browser.</summary>
public partial class MapBrowserGrid : GridContainer
{
    private sealed record MapCard(Button Button, VBoxContainer Content, MapThumb Picture, Label Title, HBoxContainer Row, string Id);

    private readonly List<MapCard> _cards = new();
    private readonly bool _compact;
    private float _width = -1;
    private MapBrowserView? _view;
    public OptionButton ViewToggle { get; } = new() { Name = "MapView", TooltipText = "Map display", CustomMinimumSize = new Vector2(130, 0) };

    public MapBrowserGrid(bool compact = false)
    {
        _compact = compact;
        Name = "MapGrid";
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        AddThemeConstantOverride("h_separation", 8);
        AddThemeConstantOverride("v_separation", 8);
        ViewToggle.AddItem("Picture grid");
        ViewToggle.AddItem("List view");
        ViewToggle.AddItem("Thumbnail list");
        ViewToggle.Select((int)SettingsStore.Current.MapView);
        ViewToggle.ItemSelected += index =>
        {
            SettingsStore.Current.MapView = (MapBrowserView)index;
            SettingsStore.Save(SettingsStore.Current);
        };
    }

    /// <summary>The tile's tooltip: the name and code, then what an authored map says about itself.</summary>
    private static string Tooltip(MapCatalogEntry? entry, string id, string title)
    {
        var text = new System.Text.StringBuilder(title).Append(" (").Append(id).Append(')');
        if (entry?.Description is { } description)
        {
            text.Append('\n').Append(description);
        }

        if (entry is { Authors.Count: > 0 })
        {
            text.Append("\nBy ").Append(string.Join(", ", entry.Authors));
            if (entry.Version is { } version)
            {
                text.Append(", version ").Append(version);
            }
        }

        return text.ToString();
    }

    public Button AddMap(MapCatalog maps, string id, string title)
    {
        var button = new Button
        {
            Name = "Map_" + id, TooltipText = Tooltip(MapImages.Entry(maps, id), id, title), ClipText = true,
            SizeFlagsHorizontal = SizeFlags.ExpandFill, FocusMode = FocusModeEnum.All,
            ThemeTypeVariation = UiStyle.TileVariation,
        };
        var content = new VBoxContainer { Name = "Column", MouseFilter = MouseFilterEnum.Ignore };
        var picture = new MapThumb(new Vector2(160, 160)) { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        picture.SetMap(MapImages.Preview(maps, id), title);
        if (_compact)
        {
            // Host setup gives the map browser half the page: landscape previews leave room for more choices.
            TextureRect image = picture.GetNode<TextureRect>("Picture");
            image.StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered;
            image.ClipContents = true;
        }
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

        var row = new HBoxContainer { Name = "Row", MouseFilter = MouseFilterEnum.Ignore, Visible = false };
        row.AddThemeConstantOverride("separation", 12);
        var thumbnail = new MapThumb(new Vector2(80, 45))
        {
            SizeFlagsHorizontal = SizeFlags.ShrinkBegin, SizeFlagsVertical = SizeFlags.ShrinkCenter,
        };
        thumbnail.SetMap(picture.Picture, title);
        // The adjacent title already labels a missing preview; keep the small frame from wrapping a second title.
        thumbnail.GetNode<Label>("Placeholder").Hide();
        var thumbnailImage = thumbnail.GetNode<TextureRect>("Picture");
        thumbnailImage.StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered;
        thumbnailImage.ClipContents = true;
        row.AddChild(thumbnail);
        var details = new VBoxContainer
        {
            Name = "Details", MouseFilter = MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ShrinkCenter,
        };
        details.AddThemeConstantOverride("separation", 0);
        var rowTitle = new Label
        {
            Name = "Title", Text = title, MouseFilter = MouseFilterEnum.Ignore,
            AutowrapMode = TextServer.AutowrapMode.WordSmart, MaxLinesVisible = 2,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            LabelSettings = new LabelSettings { FontSize = 14, FontColor = UiStyle.MenuText },
        };
        details.AddChild(rowTitle);
        var code = new Label
        {
            Name = "Code", Text = id, MouseFilter = MouseFilterEnum.Ignore, ClipText = true,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            LabelSettings = new LabelSettings { FontSize = 12, FontColor = UiStyle.Dim },
        };
        details.AddChild(code);
        row.AddChild(details);
        button.AddChild(row);
        row.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect, LayoutPresetMode.Minsize, 6);
        AddChild(button);
        _cards.Add(new MapCard(button, content, picture, label, row, id));
        _width = -1;
        return button;
    }

    public override void _Process(double delta)
    {
        MapBrowserView view = SettingsStore.Current.MapView;
        bool grid = view == MapBrowserView.PictureGrid;
        bool textList = view == MapBrowserView.List;
        // Reserve scrollbar space even before it appears, avoiding column-count oscillation.
        float width = GetParent() is ScrollContainer scroll ? scroll.Size.X - 18 : Size.X;
        width = Math.Max(172, width);
        if (_width == width && _view == view) return;
        _width = width;
        _view = view;
        ViewToggle.Select((int)view);
        Columns = grid ? Math.Max(1, (int)((width + 8) / 180)) : 1;
        float cardWidth = MathF.Floor((width - (Columns - 1) * 8) / Columns);
        foreach (var card in _cards)
        {
            float pictureHeight = _compact ? MathF.Floor((cardWidth - 12) * 9 / 16) : cardWidth - 12;
            card.Button.CustomMinimumSize = new Vector2(grid ? cardWidth : 172, grid ? pictureHeight + 60 : textList ? 40 : 72);
            card.Content.Visible = grid;
            card.Row.Visible = view == MapBrowserView.ThumbnailList;
            card.Button.Text = textList ? card.Title.Text + " (" + card.Id + ")" : string.Empty;
            card.Button.Alignment = HorizontalAlignment.Left;
            card.Picture.CustomMinimumSize = new Vector2(0, pictureHeight);
        }
    }
}
