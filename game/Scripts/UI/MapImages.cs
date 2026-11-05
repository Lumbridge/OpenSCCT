using System;
using System.Collections.Generic;
using Godot;
using OpenScct.Server;

namespace OpenScctGame.UI;

/// <summary>
/// The map pictures the menus show, from the map catalog's entries (docs/MAPS.md "Map identity": the map's own
/// <c>ui/pictures</c>, else the maps step's <c>shared/maps/images</c> made from each map's SMapSettings textures, else a
/// downloaded map's <c>catalog/</c>): the host list's picture, each side's loading pictures and the lobby's briefing
/// plan. Each picture is read once. A map without one gets the <see cref="MapThumb"/> placeholder with its name.
/// </summary>
public static class MapImages
{
    private static readonly Dictionary<string, Texture2D?> Textures = new(StringComparer.OrdinalIgnoreCase);
    private static MapCatalog? _default;

    /// <summary>The catalog pages use when they have none of their own (the default roots of this install).</summary>
    public static MapCatalog Default => _default ??= MapCatalog.CreateDefault();

    /// <summary>The map's host list picture, or null.</summary>
    public static Texture2D? Preview(MapCatalog? maps, string? map) => First(maps, map, e => Load(e.PreviewImage));

    /// <summary>The width banners are kept at: map browsers show them at a few hundred pixels at most.</summary>
    private const int BannerWidth = 640;

    /// <summary>The map's picture for a wide frame: its 16:9 banner (scaled down to <see cref="BannerWidth"/>, with
    /// mipmaps for the small thumbnails), else its host list picture, or null.</summary>
    public static Texture2D? Wide(MapCatalog? maps, string? map) =>
        First(maps, map, e => Load(e.BannerImage, BannerWidth)) ?? Preview(maps, map);

    /// <summary>
    /// The map's loading picture for the side (the other side's, then the host list's, when it lacks one), or null. A
    /// map with several pictures for a side gives the one <paramref name="seed"/> picks (an online session's, so every
    /// player sees the same), or a random one on each call without a seed: a loading page asks once per load.
    /// </summary>
    public static Texture2D? Loading(MapCatalog? maps, string? map, bool merc, int? seed = null) =>
        First(maps, map, e => Load(Pick(merc ? e.LoadingMercImages : e.LoadingSpyImages, seed))) ??
        First(maps, map, e => Load(Pick(merc ? e.LoadingSpyImages : e.LoadingMercImages, seed))) ??
        Preview(maps, map);

    /// <summary>
    /// The lobby briefing's plan of the map (GetLevelBrief), else the defenders' (mercs') loading picture as the
    /// original does (spec/game/lobby-and-match-creation.md rule 31), or null.
    /// </summary>
    public static Texture2D? Briefing(MapCatalog? maps, string? map) =>
        First(maps, map, e => Load(e.BriefingImage)) ?? Loading(maps, map, merc: true);

    /// <summary>The catalog entry of a map in the given catalog, else in the install's default one.</summary>
    public static MapCatalogEntry? Entry(MapCatalog? maps, string? map)
    {
        if (string.IsNullOrEmpty(map))
        {
            return null;
        }

        MapCatalogEntry? entry = (maps ?? Default).GetCatalogEntry(map);
        if (entry is null && maps is not null && !ReferenceEquals(maps, Default))
        {
            entry = Default.GetCatalogEntry(map);
        }

        return entry;
    }

    /// <summary>A picture from the map's entry in the given catalog, else (picture by picture) from the install's
    /// default catalog, which holds the shared pictures a server's or hub's own catalog may lack.</summary>
    private static Texture2D? First(MapCatalog? maps, string? map, Func<MapCatalogEntry, Texture2D?> picture)
    {
        if (string.IsNullOrEmpty(map))
        {
            return null;
        }

        MapCatalog catalog = maps ?? Default;
        Texture2D? found = catalog.GetCatalogEntry(map) is { } entry ? picture(entry) : null;
        if (found is null && !ReferenceEquals(catalog, Default) && Default.GetCatalogEntry(map) is { } shared)
        {
            found = picture(shared);
        }

        return found;
    }

    private static string? Pick(IReadOnlyList<string> pictures, int? seed) => pictures.Count switch
    {
        0 => null,
        1 => pictures[0],
        _ => pictures[seed is { } s ? (int)((uint)s % (uint)pictures.Count) : System.Random.Shared.Next(pictures.Count)],
    };

    /// <summary>The picture at <paramref name="path"/>, read once; with <paramref name="maxWidth"/>, scaled down to it
    /// and given mipmaps.</summary>
    private static Texture2D? Load(string? path, int maxWidth = 0)
    {
        if (path is null)
        {
            return null;
        }

        string key = maxWidth > 0 ? path + "|" + maxWidth : path;
        if (Textures.TryGetValue(key, out Texture2D? cached))
        {
            return cached;
        }

        var image = new Image();
        Texture2D? texture = null;
        if (image.Load(path) == Error.Ok)
        {
            if (maxWidth > 0)
            {
                if (image.GetWidth() > maxWidth)
                {
                    image.Resize(maxWidth, Math.Max(1, image.GetHeight() * maxWidth / image.GetWidth()), Image.Interpolation.Lanczos);
                }

                image.GenerateMipmaps();
            }

            texture = ImageTexture.CreateFromImage(image);
        }

        Textures[key] = texture;
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
