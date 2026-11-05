using System.Collections.Generic;
using Godot;

namespace OpenScctGame.UI;

/// <summary>A place number to draw on the briefing plan, at U across and V down (0..1 over the picture).</summary>
public readonly record struct BriefingMark(float U, float V, int Number);

/// <summary>
/// The lobby's Briefing (Lobby_Create's "Briefing" button): <see cref="BriefingPages"/> over the lobby, the level
/// picture on page 1 and the briefing plan on page 2 (spec/game/lobby-and-match-creation.md rules 30 and 31), Next on
/// page 1 and Previous on page 2 (the original's A button), and Close. A plan drawn through the snapshot camera gets
/// the map's place numbers drawn over it.
/// </summary>
public partial class BriefingPanel : PanelContainer
{
    private readonly BriefingPages _pages;
    private readonly Texture2D? _picture;
    private readonly Texture2D? _plan;
    private readonly IReadOnlyList<BriefingMark> _marks;
    private readonly string _mapTitle;
    private Control? _first;
    private Control? _second;
    private Button? _flip;

    public BriefingPanel(BriefingPages pages, Texture2D? picture, Texture2D? plan, IReadOnlyList<BriefingMark> marks, string mapTitle)
    {
        _pages = pages;
        _picture = picture;
        _plan = plan;
        _marks = marks;
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

        _first = new HBoxContainer { Name = "Page1", SizeFlagsVertical = SizeFlags.ExpandFill };
        _first.AddThemeConstantOverride("separation", 16);
        column.AddChild(_first);
        _first.AddChild(Picture("Picture", _picture));
        var objective = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _first.AddChild(objective);
        objective.AddChild(new Label { Text = _pages.Title, HorizontalAlignment = HorizontalAlignment.Center, Modulate = UiStyle.MenuFocus });
        objective.AddChild(Words("Objective", _pages.Objective.Length > 0 ? _pages.Objective : "This map has no briefing for this mode."));
        if (_pages.ObjectiveName.Length > 0)
        {
            objective.AddChild(Words("ObjectiveName", _pages.ObjectiveName));
        }

        objective.AddChild(Words("MapBrief", _pages.MapBrief));

        _second = new HBoxContainer { Name = "Page2", SizeFlagsVertical = SizeFlags.ExpandFill, Visible = false };
        _second.AddThemeConstantOverride("separation", 16);
        column.AddChild(_second);
        TextureRect plan = Picture("Plan", _plan);
        plan.AddChild(new PlanMarks(_marks) { Name = "Marks" });
        _second.AddChild(plan);
        var left = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _second.AddChild(left);
        left.AddChild(Words("ModeBrief", _pages.ModeBrief));
        _second.AddChild(Words("Legend", _pages.Legend));

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

    private static TextureRect Picture(string name, Texture2D? texture) => new()
    {
        Name = name, Texture = texture, CustomMinimumSize = new Vector2(320f, 240f), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
        StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, SizeFlagsVertical = SizeFlags.ExpandFill,
        SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsStretchRatio = 0.7f,
    };

    /// <summary>The place numbers over a plan picture, which is drawn kept to its aspect and centred in the same rect.</summary>
    private sealed partial class PlanMarks : Control
    {
        private readonly IReadOnlyList<BriefingMark> _marks;

        public PlanMarks(IReadOnlyList<BriefingMark> marks)
        {
            _marks = marks;
            MouseFilter = MouseFilterEnum.Ignore;
            SetAnchorsPreset(LayoutPreset.FullRect);
        }

        public override void _Draw()
        {
            if (_marks.Count == 0 || GetParent<TextureRect>().Texture is not { } texture)
            {
                return;
            }

            Vector2 size = Size;
            float scale = Mathf.Min(size.X / texture.GetWidth(), size.Y / texture.GetHeight());
            Vector2 drawn = new(texture.GetWidth() * scale, texture.GetHeight() * scale);
            Vector2 origin = (size - drawn) / 2f;
            Font font = GetThemeDefaultFont();
            int fontSize = Mathf.Max(12, (int)(drawn.X / 24f));
            foreach (BriefingMark mark in _marks)
            {
                string text = mark.Number.ToString(System.Globalization.CultureInfo.InvariantCulture);
                Vector2 at = origin + new Vector2(mark.U * drawn.X, mark.V * drawn.Y);
                Vector2 extent = font.GetStringSize(text, HorizontalAlignment.Left, -1, fontSize);
                Vector2 baseline = at + new Vector2(-extent.X / 2f, extent.Y / 4f);
                DrawStringOutline(font, baseline, text, HorizontalAlignment.Left, -1, fontSize, 4, Colors.Black);
                DrawString(font, baseline, text, HorizontalAlignment.Left, -1, fontSize, UiStyle.MenuTitle);
            }
        }

        public override void _Notification(int what)
        {
            if (what == NotificationResized)
            {
                QueueRedraw();
            }
        }
    }

    private static Label Words(string name, string text) => new()
    {
        Name = name, Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = SizeFlags.ExpandFill,
        CustomMinimumSize = new Vector2(260f, 0f),
    };
}
