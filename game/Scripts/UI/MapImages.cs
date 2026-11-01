using System;
using System.Collections.Generic;
using Godot;
using OpenScct.Server;

namespace OpenScctGame.UI;

/// <summary>
/// The map pictures the menus show, from the map catalog's images (the maps step's <c>shared/maps/images</c>, made from
/// each map's SMapSettings textures: <c>menu</c> is the host list's picture, <c>loading-spy</c> and <c>loading-merc</c>
/// the loading page's pictures for each side). Each picture is read once. A map without one gets the
/// <see cref="MapThumb"/> placeholder with its name.
/// </summary>
public static class MapImages
{
    private static readonly Dictionary<string, Texture2D?> Textures = new(StringComparer.OrdinalIgnoreCase);
    private static MapCatalog? _default;

    /// <summary>The catalog pages use when they have none of their own (the default roots of this install).</summary>
    public static MapCatalog Default => _default ??= MapCatalog.CreateDefault();

    /// <summary>The map's host list picture, or null.</summary>
    public static Texture2D? Preview(MapCatalog? maps, string? map) => Load(maps, map, "menu");

    /// <summary>The map's loading picture for the side (the other side's, then the host list's, when it lacks one), or null.</summary>
    public static Texture2D? Loading(MapCatalog? maps, string? map, bool merc) =>
        Load(maps, map, merc ? "loading-merc" : "loading-spy") ?? Load(maps, map, merc ? "loading-spy" : "loading-merc") ?? Preview(maps, map);

    private static Texture2D? Load(MapCatalog? maps, string? map, string kind)
    {
        if (string.IsNullOrEmpty(map))
        {
            return null;
        }

        string? path = (maps ?? Default).CatalogData.Image(map, kind);
        if (path is null && maps is not null && !ReferenceEquals(maps, Default))
        {
            path = Default.CatalogData.Image(map, kind);
        }

        if (path is null)
        {
            return null;
        }

        if (Textures.TryGetValue(path, out Texture2D? cached))
        {
            return cached;
        }

        var image = new Image();
        Texture2D? texture = image.Load(path) == Error.Ok ? ImageTexture.CreateFromImage(image) : null;
        Textures[path] = texture;
        return texture;
    }
}

/// <summary>
/// A map's picture in a frame: the texture kept to its aspect, or, without one, a dark panel with the map's name.
/// </summary>
public partial class MapThumb : PanelContainer
{
    private readonly TextureRect _picture;
    private readonly Label _placeholder;
    private readonly Vector2 _baseSize;
    private readonly bool _scaleWithViewport;

    public MapThumb(Vector2 size, bool scaleWithViewport = false)
    {
        _baseSize = size;
        _scaleWithViewport = scaleWithViewport;
        Name = "MapThumb";
        CustomMinimumSize = size;
        MouseFilter = MouseFilterEnum.Ignore;
        AddThemeStyleboxOverride("panel", UiStyle.ThumbBox());
        _picture = new TextureRect
        {
            Name = "Picture", ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore, SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        _placeholder = new Label
        {
            Name = "Placeholder", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart, Modulate = UiStyle.MenuFocus, MouseFilter = MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        AddChild(_picture);
        AddChild(_placeholder);
    }

    public override void _Ready()
    {
        if (!_scaleWithViewport) return;
        GetViewport().SizeChanged += ResizePreview;
        ResizePreview();
    }

    public override void _ExitTree()
    {
        if (_scaleWithViewport) GetViewport().SizeChanged -= ResizePreview;
    }

    private void ResizePreview()
    {
        Vector2 viewport = GetViewportRect().Size;
        float scale = Mathf.Clamp(Mathf.Min(viewport.X / 1280f, viewport.Y / 720f), 1f, 2f);
        CustomMinimumSize = _baseSize * scale;
    }

    /// <summary>The picture shown, or null for the placeholder.</summary>
    public Texture2D? Picture => _picture.Texture;

    /// <summary>Shows <paramref name="texture"/>, or the placeholder with <paramref name="title"/> when it is null.</summary>
    public void SetMap(Texture2D? texture, string title)
    {
        _picture.Texture = texture;
        _picture.Visible = texture is not null;
        _placeholder.Visible = texture is null;
        _placeholder.Text = title;
        TooltipText = title;
    }
}
