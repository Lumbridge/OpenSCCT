using Godot;
using OpenScct.Server;
using OpenScctGame.UI;

namespace OpenScctGame.Hub;

/// <summary>
/// The page over the hub while it loads (an OpenSCCT addition, drawn like the match <see cref="LoadingScreen"/>):
/// "Entering" and the hub's name, the hub map's loading picture for the player's side, a tip and what is happening.
/// It sits on its own top layer under the tree's root rather than as the current scene, so it stays up while the
/// play scene replaces the hub page and until the player stands in the hub (<see cref="HubService"/> frees it).
/// </summary>
public partial class HubLoadingScreen : CanvasLayer
{
    /// <summary>Above every play and hub layer, under the console and pad notices (120).</summary>
    public const int CoverLayer = 110;

    private Label? _status;
    private Page? _page;

    public string HubName { get; init; } = string.Empty;

    public string Map { get; init; } = string.Empty;

    public bool Merc { get; init; }

    public MapCatalog? Maps { get; init; }

    /// <summary>The line under the picture (what the visit is doing).</summary>
    public string Status
    {
        get => _status?.Text ?? string.Empty;
        set
        {
            if (_status is not null)
            {
                _status.Text = value;
            }
        }
    }

    public override void _Ready()
    {
        Name = "HubLoading";
        Layer = CoverLayer;
        _page = new Page { Cover = this };
        AddChild(_page);
    }

    /// <summary>Fades the page out, then frees it.</summary>
    public void FadeOut()
    {
        if (_page is null || !IsInsideTree())
        {
            QueueFree();
            return;
        }

        Tween tween = CreateTween();
        tween.TweenProperty(_page, "modulate:a", 0f, 0.35);
        tween.TweenCallback(Callable.From(QueueFree));
    }

    private sealed partial class Page : MenuPage
    {
        public HubLoadingScreen Cover { get; init; } = null!;

        public override void _Ready()
        {
            Name = "Page";
            MouseFilter = MouseFilterEnum.Stop;
            BuildFrame("Loading...", 760f);
            MapCatalog catalog = Cover.Maps ?? MapImages.Default;
            string title = Cover.HubName.Length > 0 ? Cover.HubName : MapTitles.Of(catalog, Cover.Map);
            Body.AddChild(new Label
            {
                Name = "Heading", Text = "Entering " + title, HorizontalAlignment = HorizontalAlignment.Center, Modulate = UiStyle.MenuFocus,
            });
            var picture = new MapThumb(new Vector2(360f, 360f), scaleWithViewport: true) { Name = "MapPicture", SizeFlagsHorizontal = SizeFlags.ShrinkCenter };
            picture.SetMap(MapImages.Loading(catalog, Cover.Map, Cover.Merc), title);
            Body.AddChild(picture);
            Body.AddChild(LoadingTip.Label(LoadingTip.Pick(Cover.Merc)));
            Cover._status = new Label
            {
                Name = "Status", Text = "Loading the hub...", HorizontalAlignment = HorizontalAlignment.Center, Modulate = UiStyle.MenuTitle,
                AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(740f, 0f),
            };
            Body.AddChild(Cover._status);
        }
    }
}
