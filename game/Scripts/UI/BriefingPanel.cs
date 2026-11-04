using Godot;

namespace OpenScctGame.UI;

/// <summary>
/// The lobby's Briefing (Lobby_Create's "Briefing" button): <see cref="BriefingPages"/> over the lobby, laid out as the
/// original's two pages, with Next on page 1 and Previous on page 2 (the original's A button), and Close. Page 1 is
/// GUI.Briefing: the level picture (RefreshText: GetLevelPicture, <c>&lt;Map&gt;-i.Menu</c>) beside the main objective
/// and the mode picture (InitComponent: GUIController.ImageMode) with the objective's name, the map's words below.
/// Page 2 is GUI.BriefingDeux: the briefing picture (InitComponent: GetLevelBrief, <c>&lt;Map&gt;-i.Briefing</c>) beside
/// the map legend, the mode's words below. The caller passes GUIContent's defenseur for a map without a picture.
/// All three pictures are ISTY_Scaled, stretched to fill their boxes; the boxes keep the shapes they have on the
/// original's 4:3 screen (BriefingImage 0.375 x 0.4, BriefingDeux's 0.45 x 0.475, ModeImage 0.12 x 0.17).
/// </summary>
public partial class BriefingPanel : PanelContainer
{
    private readonly BriefingPages _pages;
    private readonly Texture2D? _picture;
    private readonly Texture2D? _plan;
    private readonly Texture2D? _modePicture;
    private readonly string _mapTitle;
    private Control? _first;
    private Control? _second;
    private Button? _flip;

    /// <summary>Width over height of GUI.Briefing's BriefingImage (0.375 x 0.4 of a 4:3 screen).</summary>
    private const float PictureAspect = 0.375f * 4f / (0.4f * 3f);

    /// <summary>Width over height of GUI.BriefingDeux's BriefingImage (0.45 x 0.475 of a 4:3 screen).</summary>
    private const float PlanAspect = 0.45f * 4f / (0.475f * 3f);

    /// <summary>Width over height of GUI.Briefing's ModeImage (0.12 x 0.17 of a 4:3 screen).</summary>
    private const float ModeAspect = 0.12f * 4f / (0.17f * 3f);

    public BriefingPanel(BriefingPages pages, Texture2D? picture, Texture2D? plan, Texture2D? modePicture, string mapTitle)
    {
        _pages = pages;
        _picture = picture;
        _plan = plan;
        _modePicture = modePicture;
        _mapTitle = mapTitle;
        Name = "Briefing";
    }

    /// <summary>0 for the main objective page, 1 for the map page.</summary>
    public int Page { get; private set; }

    public override void _Ready()
    {
        AnchorLeft = 0.15f;
        AnchorRight = 0.85f;
        AnchorTop = 0.1f;
        AnchorBottom = 0.9f;
        AddThemeStyleboxOverride("panel", UiStyle.MenuPanelBox());
        var column = new VBoxContainer { Name = "Column" };
        column.AddThemeConstantOverride("separation", 10);
        AddChild(column);
        column.AddChild(new Label { Name = "Map", Text = _mapTitle, HorizontalAlignment = HorizontalAlignment.Center, Modulate = UiStyle.MenuTitle });

        _first = NewPage("Page1", visible: true);
        column.AddChild(_first);
        var firstTop = Top(_first);
        firstTop.AddChild(Picture("Picture", _picture, PictureAspect));
        var objective = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        firstTop.AddChild(objective);
        objective.AddChild(new Label { Text = _pages.Title, HorizontalAlignment = HorizontalAlignment.Center, Modulate = UiStyle.MenuFocus });
        objective.AddChild(Words("Objective", _pages.Objective.Length > 0 ? _pages.Objective : "This map has no briefing for this mode."));
        if (_modePicture is not null || _pages.ObjectiveName.Length > 0)
        {
            var mode = new HBoxContainer { Name = "ModeRow" };
            mode.AddThemeConstantOverride("separation", 12);
            objective.AddChild(mode);
            if (_modePicture is not null)
            {
                mode.AddChild(new TextureRect
                {
                    Name = "ModeImage", Texture = _modePicture, CustomMinimumSize = new Vector2(96f, 96f / ModeAspect),
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale,
                });
            }

            if (_pages.ObjectiveName.Length > 0)
            {
                Label name = Words("ObjectiveName", _pages.ObjectiveName);
                name.CustomMinimumSize = new Vector2(160f, 0f);
                name.VerticalAlignment = VerticalAlignment.Center;
                name.SizeFlagsVertical = SizeFlags.ShrinkCenter;
                mode.AddChild(name);
            }
        }

        _first.AddChild(Words("MapBrief", _pages.MapBrief));

        _second = NewPage("Page2", visible: false);
        column.AddChild(_second);
        var secondTop = Top(_second);
        secondTop.AddChild(Picture("Plan", _plan, PlanAspect));
        secondTop.AddChild(Words("Legend", _pages.Legend));
        _second.AddChild(Words("ModeBrief", _pages.ModeBrief));

        var buttons = new HBoxContainer { Name = "Buttons", Alignment = BoxContainer.AlignmentMode.Center };
        buttons.AddThemeConstantOverride("separation", 16);
        _flip = UiStyle.MenuButton("Next", Flip, "BriefingFlip");
        Button close = UiStyle.MenuButton("Close", QueueFree, "BriefingClose");
        _flip.CustomMinimumSize = close.CustomMinimumSize = new Vector2(160f, 40f);
        buttons.AddChild(_flip);
        buttons.AddChild(close);
        column.AddChild(buttons);
        _flip.GrabFocus();
    }

    /// <summary>Next / Previous: the other page.</summary>
    public void Flip()
    {
        Page = 1 - Page;
        _first!.Visible = Page == 0;
        _second!.Visible = Page == 1;
        _flip!.Text = Page == 0 ? "Next" : "Previous";
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape }) return;
        GetViewport().SetInputAsHandled();
        QueueFree();
    }

    private static VBoxContainer NewPage(string name, bool visible)
    {
        var page = new VBoxContainer { Name = name, SizeFlagsVertical = SizeFlags.ExpandFill, Visible = visible };
        page.AddThemeConstantOverride("separation", 12);
        return page;
    }

    private static HBoxContainer Top(Control page)
    {
        var top = new HBoxContainer { Name = "Top", SizeFlagsVertical = SizeFlags.ExpandFill };
        top.AddThemeConstantOverride("separation", 16);
        page.AddChild(top);
        return top;
    }

    /// <summary>A picture stretched to fill a box of the original's shape (ISTY_Scaled), the box as large as fits.</summary>
    private static AspectRatioContainer Picture(string name, Texture2D? texture, float aspect)
    {
        var box = new AspectRatioContainer
        {
            Name = name + "Box", Ratio = aspect, StretchMode = AspectRatioContainer.StretchModeEnum.Fit,
            CustomMinimumSize = new Vector2(300f, 300f / aspect), SizeFlagsVertical = SizeFlags.ExpandFill,
            SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsStretchRatio = 0.9f,
        };
        box.AddChild(new TextureRect
        {
            Name = name, Texture = texture, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale,
        });
        return box;
    }

    private static Label Words(string name, string text) => new()
    {
        Name = name, Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = SizeFlags.ExpandFill,
        CustomMinimumSize = new Vector2(260f, 0f),
    };
}
