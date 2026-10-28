using System;
using Godot;
using OpenScctGame.UI;

namespace OpenScctGame.Options;

/// <summary>
/// The brightness test pattern (GUI.Mire, "Brightness Configuration", opened from the original's Video Options): on
/// black, three dark squares; the left one carries a check mark just above black, the other two marks darker still.
/// The player turns the screen's brightness until the left mark shows and the other two stay black. The original look
/// shows the page's own picture (GUIContent calibrage, converted under the ui step's Options group) over the whole
/// page; the modern look draws the same pattern at the picture's levels. Back, Escape or a click closes it.
/// </summary>
public partial class BrightnessTest : Control
{
    private const string Section = "Mire";

    public BrightnessTest()
    {
        Name = "BrightnessTest";
    }

    /// <summary>Raised when the page closes.</summary>
    public event Action? Closed;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        Theme = UiStyle.MenuTheme;
        MouseFilter = MouseFilterEnum.Stop;
        AddChild(new ColorRect { Name = "Black", Color = Colors.Black, AnchorRight = 1f, AnchorBottom = 1f, MouseFilter = MouseFilterEnum.Ignore });
        Texture2D? picture = UiStyle.IsOriginal ? UiStyle.Assets?.Texture("Options", "calibrage") : null;
        if (picture is not null)
        {
            // Mire.Background: the picture over the whole page.
            AddChild(new TextureRect
            {
                Name = "Calibrage", Texture = picture, AnchorRight = 1f, AnchorBottom = 1f, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.Scale, MouseFilter = MouseFilterEnum.Ignore,
            });
        }
        else
        {
            AddChild(new BrightnessPattern { Name = "Pattern", AnchorLeft = 0.1f, AnchorRight = 0.9f, AnchorTop = 0.35f, AnchorBottom = 0.8f, MouseFilter = MouseFilterEnum.Ignore });
        }

        var title = UiStyle.Title(OpenScctGame.Tutorials.TutorialText.Caption(Section, "TitlePage", "Brightness Configuration"), UiStyle.MenuTitle, 30);
        title.AnchorRight = 1f;
        title.OffsetTop = 24f;
        AddChild(title);

        // Mire.TextVideo: WinTop 0.2, WinLeft 0.1, WinWidth 0.8.
        AddChild(new Label
        {
            Name = "Text", AnchorLeft = 0.1f, AnchorRight = 0.9f, AnchorTop = 0.2f, AnchorBottom = 0.3f, HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart, Modulate = UiStyle.MenuText,
            Text = OpenScctGame.Tutorials.TutorialText.Lines(OpenScctGame.Tutorials.TutorialText.Caption(Section, "TextVideo.Caption",
                "Adjust your screen brightness so that you can see|the left square checked.|The other two squares should stay black.")),
        });
        Button back = UiStyle.MenuButton("Back", Close, "Back");
        back.AnchorLeft = back.AnchorRight = 0.5f;
        back.AnchorTop = back.AnchorBottom = 1f;
        back.OffsetLeft = -110f;
        back.OffsetRight = 110f;
        back.OffsetTop = -66f;
        back.OffsetBottom = -22f;
        AddChild(back);
        back.GrabFocus();
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
        {
            GetViewport().SetInputAsHandled();
            Close();
        }
    }

    private void Close()
    {
        QueueFree();
        Closed?.Invoke();
    }
}

/// <summary>
/// The modern look's brightness pattern (<see cref="BrightnessTest"/>): three framed squares, a check in the left one
/// and crosses in the others, at the original picture's levels.
/// </summary>
public partial class BrightnessPattern : Control
{
    [OpenScct.Provenance.Provenance(OpenScct.Provenance.ProvenanceKind.Decoded,
        "GUIContent.New_Menu.calibrage pixels: squares (0, 4, 0) framed (8, 12, 8); check about 13; middle cross 5; right cross 2")]
    private static readonly (float Fill, float Frame, float Mark)[] Levels = [(4f, 12f, 13f), (4f, 12f, 5f), (4f, 12f, 2f)];

    public override void _Draw()
    {
        float side = Math.Min(Size.Y, Size.X / 3.6f);
        float gap = (Size.X - side * 3f) / 4f;
        for (int i = 0; i < 3; i++)
        {
            (float fill, float frame, float mark) = Levels[i];
            var r = new Rect2(gap + i * (side + gap), (Size.Y - side) * 0.5f, side, side);
            DrawRect(r, Grey(fill));
            DrawRect(r, Grey(frame), false, Math.Max(2f, side * 0.02f));
            float w = Math.Max(4f, side * 0.12f);
            Vector2 c = r.GetCenter();
            float h = side * 0.28f;
            if (i == 0)
            {
                DrawPolyline([c + new Vector2(-h, 0f), c + new Vector2(-h * 0.3f, h * 0.8f), c + new Vector2(h, -h)], Grey(mark), w, true);
            }
            else
            {
                DrawLine(c + new Vector2(-h, -h), c + new Vector2(h, h), Grey(mark), w, true);
                DrawLine(c + new Vector2(-h, h), c + new Vector2(h, -h), Grey(mark), w, true);
            }
        }
    }

    private static Color Grey(float level) => new(level / 255f, level / 255f, level / 255f);
}
