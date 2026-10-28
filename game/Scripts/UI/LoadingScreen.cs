using System;
using Godot;
using OpenScct.Core;
using OpenScct.Server;
using OpenScct.Server.Online;

namespace OpenScctGame.UI;

/// <summary>
/// The loading page of an offline match (the original's GUI.loading, as <see cref="ConnectingScreen"/> shows it for a
/// network round): "Loading" and the map's name, the map's loading picture for the player's side, the mode's picture
/// and name. The play scene loads in one go on the main thread, so the page is drawn first and the load starts a few
/// frames later (<see cref="Then"/>), leaving the page on screen while the map loads.
/// </summary>
public partial class LoadingScreen : MenuPage
{
    /// <summary>Frames drawn before <see cref="Then"/> runs.</summary>
    public const int FramesBeforeLoad = 3;

    private int _frames;
    private bool _started;

    public string Map { get; init; } = string.Empty;

    public bool Merc { get; init; }

    public EGameMode Mode { get; init; } = EGameMode.GM_Multi;

    /// <summary>The catalog holding the map's pictures (the install's default catalog when null).</summary>
    public MapCatalog? Maps { get; init; }

    /// <summary>What runs once the page is on screen (entering the play scene, which replaces this page).</summary>
    public Action? Then { get; init; }

    /// <summary>Shows the loading page for <paramref name="map"/>, then runs <paramref name="then"/>.</summary>
    public static LoadingScreen Open(SceneTree tree, string map, bool merc, EGameMode mode, MapCatalog? maps, Action then) =>
        FrontEnd.Show(tree, new LoadingScreen { Map = map, Merc = merc, Mode = mode, Maps = maps, Then = then });

    public override void _Ready()
    {
        Name = "Loading";
        BuildFrame("Loading...", 760f);
        MapCatalog catalog = Maps ?? MapImages.Default;
        string title = MapTitles.Of(catalog, Map);
        Body.AddChild(new Label { Name = "Heading", Text = "Loading " + title, HorizontalAlignment = HorizontalAlignment.Center, Modulate = UiStyle.MenuFocus });
        var picture = new MapThumb(new Vector2(360f, 360f)) { Name = "MapPicture", SizeFlagsHorizontal = SizeFlags.ShrinkCenter };
        picture.SetMap(MapImages.Loading(catalog, Map, Merc), title);
        Body.AddChild(picture);
        var modeRow = new HBoxContainer { Name = "ModeRow", Alignment = BoxContainer.AlignmentMode.Center };
        modeRow.AddChild(new TextureRect
        {
            Name = "ModeImage", CustomMinimumSize = new Vector2(96f, 96f), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            Texture = UiStyle.Assets?.Texture("common", Mode switch
            {
                EGameMode.GM_DiskHunt => "L_diskhunt",
                EGameMode.GM_DeathMatch or EGameMode.GM_MercDeathMatch => "L_Death",
                _ => "L_Story",
            }),
        });
        modeRow.AddChild(new Label { Name = "Mode", Text = LobbySetup.ModeName(Mode), Modulate = UiStyle.MenuTitle, VerticalAlignment = VerticalAlignment.Center });
        Body.AddChild(modeRow);
        Body.AddChild(LoadingTip.Label(LoadingTip.Pick(Merc)));
    }

    public override void _Process(double delta)
    {
        base._Process(delta);
        if (_started || ++_frames < FramesBeforeLoad)
        {
            return;
        }

        _started = true;
        Then?.Invoke();
    }

    /// <summary>The mode of a play settings mode key (MapCatalog.ModeOfKey; Story for an unknown one).</summary>
    public static EGameMode ModeOf(string? key) => MapCatalog.ModeOfKey(key) ?? EGameMode.GM_Multi;
}
