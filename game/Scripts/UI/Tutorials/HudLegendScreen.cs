using System;
using Godot;
using OpenScct.Provenance;
using OpenScctGame.Tutorials;

namespace OpenScctGame.UI;

/// <summary>The picture pages of the tutorials: the two HUD legends and the two keyboard pictures.</summary>
public enum HudLegendPage
{
    /// <summary>GUI.View_Hud_Spy.</summary>
    SpyHud,

    /// <summary>GUI.View_Hud_Sentry.</summary>
    MercHud,

    /// <summary>GUI.View_KeyboardConfigOne.</summary>
    Keyboard1,

    /// <summary>GUI.View_KeyboardConfigTwo.</summary>
    Keyboard2,
}

/// <summary>
/// View HUD (GUI.View_Hud_Spy, View_Hud_Sentry) and the keyboard pictures (View_KeyboardConfigOne, Two): the page's
/// picture (Tutorials_PC SPY_ / SENTRY_Tutorial2_User_Interface, clavier) with the page's labels laid over it at their
/// places in the 640 x 480 page, each on a dark box (GUILabelPad). The A button switches to the other page of the pair
/// (Merc HUD / Spy HUD, 2nd Config / 1st Config). The keyboard pages are not reachable in the original's menus; here
/// a Keyboard / HUD button on the bar switches between the pairs.
/// </summary>
public partial class HudLegendScreen : MenuPage
{
    /// <summary>A label of a picture page: its caption key, fallback, alignment and place (fractions of the 640 x 480 page).</summary>
    private readonly record struct Spot(string Control, string Fallback, HorizontalAlignment Align, float Top, float Left, float Width);

    [Provenance(ProvenanceKind.Decoded, "GUILabelPad defaults: TXTA_Center, BackColor (0, 0, 0, 100); the pages' WinHeight 55")]
    private const float SpotHeight = 55f;

    private const HorizontalAlignment L = HorizontalAlignment.Left;
    private const HorizontalAlignment C = HorizontalAlignment.Center;
    private const HorizontalAlignment R = HorizontalAlignment.Right;

    [Provenance(ProvenanceKind.Decoded, "GUI.View_Hud_Spy defaults: the labels' TextAlign, WinTop, WinLeft, WinWidth")]
    private static readonly Spot[] SpySpots =
    [
        new("LBLSystemMessage", "System Messages", L, 0.165f, 0.556f, 0.3f),
        new("LBLInteractionMenu", "Interaction|Menu", L, 0.378f, 0.78f, 0.3f),
        new("LBLRadar", "Tactical|Radar", R, 0.242f, 0f, 0.23f),
        new("LBLGameMessage", "Game Status|Messages", C, 0.773f, 0.292f, 0.3f),
        new("LBLGadget", "Current|Gadget", L, 0.775f, 0.675f, 0.3f),
        new("LBLStealth", "Enhanced Reality", C, 0.165f, 0.23f, 0.3f),
        new("LBLLife", "Life|Indicator", L, 0.483f, 0.78f, 0.3f),
        new("LBLEnergy", "Energy|Indicator", L, 0.585f, 0.78f, 0.3f),
        new("LBLTimer", "Timers", R, 0.485f, 0f, 0.23f),
        new("LBLTime", "Remaining|Time and|Objectives", L, 0.23f, 0.78f, 0.3f),
        new("LBLAlarm", "Coop Move|Indicator", R, 0.768f, 0.006f, 0.23f),
    ];

    [Provenance(ProvenanceKind.Decoded, "GUI.View_Hud_Sentry defaults: the labels' TextAlign, WinTop, WinLeft, WinWidth")]
    private static readonly Spot[] MercSpots =
    [
        new("LBLReticule", "Reticle", C, 0.77f, 0.459f, 0.3f),
        new("LBLInteraction", "Interaction|Menu", L, 0.673f, 0.77f, 0.2f),
        new("LBLTimer", "Timers", L, 0.5f, 0.78f, 0.2f),
        new("LBLRObj", "Remaining|Objectives", R, 0.206f, 0.026f, 0.2f),
        new("LBLTime", "Remaining|Time", R, 0.29f, 0.026f, 0.2f),
        new("LBLLocalMsg", "Local|Messages", R, 0.364583f, 0.05625f, 0.18f),
        new("LBLGameMessage", "Game Status|Messages", C, 0.77f, 0.258f, 0.3f),
        new("LBLER", "Enhanced|Reality", R, 0.402f, 0.026f, 0.2f),
        new("LBLAmmo", "Position|Indicator", R, 0.58f, 0.026f, 0.2f),
        new("LBLAlarm", "Alarm Indicator", C, 0.164f, 0.475f, 0.3f),
        new("LBLLife", "Life|Indicator", R, 0.495f, 0.026f, 0.2f),
        new("LBLGadget", "Current|Gadget", R, 0.683f, 0.026f, 0.2f),
        new("LBLRadar", "Tactical|Radar", L, 0.26f, 0.77f, 0.2f),
        new("LBLSystemMessage", "System Messages", C, 0.164f, 0.18f, 0.3f),
    ];

    [Provenance(ProvenanceKind.Decoded, "GUI.View_KeyboardConfigOne defaults: the labels' TextAlign, WinTop, WinLeft, WinWidth (Controls[0] is LBLUse)")]
    private static readonly Spot[] KeyboardSpots =
    [
        new("LBLUse", "Use", C, 0.586f, 0f, 0.1f),
        new("LBLBTW", "Back to Wall", R, 0.66f, 0f, 0.2f),
        new("LBLMove", "Move", R, 0.7f, 0f, 0.2f),
        new("LBLCrouch", "Crouch", R, 0.74f, 0.06844f, 0.2f),
        new("LBLCoop", "Coop", R, 0.78f, 0.085936f, 0.2f),
        new("LBLJump", "Jump", R, 0.66f, 0.135f, 0.2f),
        new("LBLReload", "Reload", R, 0.7f, 0.178126f, 0.2f),
        new("LBLZoom", "Zoom", R, 0.74f, 0.2f, 0.2f),
        new("LBLHack", "Hack", R, 0.78f, 0.23f, 0.2f),
        new("LBLLaser", "Laser", R, 0.66f, 0.347f, 0.2f),
        new("LBLPrevStickyCam", "Previous Sticky Cam", C, 0.658335f, 0.525001f, 0.2f),
        new("LBLAction1", "Action 1", R, 0.41f, 0f, 0.13f),
        new("LBLAction2", "Action 2", R, 0.46f, 0f, 0.13f),
        new("LBLGadget", "Gadget Selection", C, 0.35f, 0.1f, 0.3f),
        new("LBLMenu", "In-Game Menu", L, 0.21f, 0.01f, 0.15f),
        new("LBLSwitchVision", "Switch Vision", L, 0.297079f, 0.04f, 0.3f),
        new("LBLHelp", "Help", C, 0.3f, 0.223438f, 0.2f),
        new("LBLTalk", "Talk", C, 0.279166f, 0.292188f, 0.2f),
        new("LBLER", "Enhanced Reality", R, 0.308333f, 0.373439f, 0.3f),
        new("LBLStatus", "Game Status", C, 0.341666f, 0.459376f, 0.2f),
        new("LBLFire", "Fire", R, 0.36f, 0.63f, 0.2f),
        new("LBLUseGadget", "Use Gadget", L, 0.36f, 0.83f, 0.17f),
        new("LBLSlowMode", "Click: Slow Mode|Roll: Zoom in/out", C, 0.25f, 0.7f, 0.3f),
    ];

    /// <summary>View_KeyboardConfigTwo.InitComponent: Controls[0] (Use) moves to the page's middle.</summary>
    [Provenance(ProvenanceKind.Decoded, "GUI.View_KeyboardConfigTwo.InitComponent: Controls[0].WinLeft = 0.5, WinTop = 0.5")]
    private static readonly Vector2 ConfigTwoUse = new(0.5f, 0.5f);

    public HudLegendPage Page { get; init; }

    public static HudLegendScreen Show(SceneTree tree, HudLegendPage page) => FrontEnd.Show(tree, new HudLegendScreen { Page = page });

    [PortOf("uc:GUI.View_Hud_Spy:InternalOnKeyEvent", Spec = "spec/game/tutorials.md#rules")]
    [PortOf("uc:GUI.View_Hud_Sentry:InternalOnKeyEvent", Spec = "spec/game/tutorials.md#rules")]
    [PortOf("uc:GUI.View_KeyboardConfigOne:InternalOnKeyEvent", Spec = "spec/game/tutorials.md#rules")]
    [PortOf("uc:GUI.View_KeyboardConfigTwo:InitComponent", Spec = "spec/game/tutorials.md#rules")]
    public override void _Ready()
    {
        Name = "HudLegend";
        (string section, string title, string texture, Spot[] spots, HudLegendPage next, string nextCaption) = Page switch
        {
            HudLegendPage.MercHud => ("View_Hud_Sentry", "Mercenary HUD", "SENTRY_Tutorial2_User_Interface", MercSpots, HudLegendPage.SpyHud, "Spy HUD"),
            HudLegendPage.Keyboard1 => ("View_KeyboardConfigOne", "Keyboard Default Configuration 1", "clavier", KeyboardSpots, HudLegendPage.Keyboard2, "2nd Config"),
            HudLegendPage.Keyboard2 => ("View_KeyboardConfigTwo", "Keyboard Default Configuration 2", "clavier", KeyboardSpots, HudLegendPage.Keyboard1, "1st Config"),
            _ => ("View_Hud_Spy", "Spy HUD", "SPY_Tutorial2_User_Interface", SpySpots, HudLegendPage.MercHud, "Merc HUD"),
        };
        bool keyboard = Page is HudLegendPage.Keyboard1 or HudLegendPage.Keyboard2;
        BuildFrame(TutorialText.Caption(section, "TitlePage", title), 400f);

        var picture = new LegendPicture
        {
            Name = "Picture", Texture = UiStyle.Assets?.Texture("Tutorials_PC", texture),
            AnchorRight = 1f, AnchorBottom = 1f, OffsetTop = 78f, OffsetBottom = -78f, OffsetLeft = 24f, OffsetRight = -24f,
        };
        AddChild(picture);
        for (int i = 0; i < spots.Length; i++)
        {
            Spot s = spots[i];
            // The labels of the keyboard pages are in View_KeyboardConfigOne's section only (Two inherits them).
            string caption = TutorialText.Caption(keyboard ? "View_KeyboardConfigOne" : section, s.Control + ".Caption", s.Fallback);
            Vector2 at = Page == HudLegendPage.Keyboard2 && i == 0 ? ConfigTwoUse : new Vector2(s.Left, s.Top);
            picture.AddSpot(TutorialText.Lines(caption), s.Align, new Rect2(at.X, at.Y, s.Width, SpotHeight / LegendPicture.PageHeight));
        }

        if (picture.Texture is null)
        {
            Body.AddChild(new Label
            {
                Text = "The picture needs the converted tutorial textures (the ui content step).", Modulate = UiStyle.Dim,
                HorizontalAlignment = HorizontalAlignment.Center,
            });
        }

        PageButton("Back", () => TutorialsScreen.Show(GetTree()), "Back");
        PageButton(keyboard ? "HUD" : "Keyboard", () => Show(GetTree(), keyboard ? HudLegendPage.SpyHud : HudLegendPage.Keyboard1), "Pair");
        PageButton(TutorialText.Caption(section, "ButtonA", nextCaption), () => Show(GetTree(), next), "Next").GrabFocus();
    }
}

/// <summary>
/// A picture page's picture fitted in 4:3 (the 640 x 480 page it filled) with labels placed on it by page fractions,
/// each on a translucent black box.
/// </summary>
public partial class LegendPicture : Control
{
    public const float PageWidth = 640f;
    public const float PageHeight = 480f;

    [Provenance(ProvenanceKind.Decoded, "GUILabelPad defaults: BackColor (0, 0, 0, 100)")]
    private static readonly Color SpotBack = new(0f, 0f, 0f, 100f / 255f);

    private readonly System.Collections.Generic.List<(Label Label, Rect2 Place)> _spots = [];
    private bool _layoutPending;

    public Texture2D? Texture { get; init; }

    public LegendPicture()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        Resized += () =>
        {
            Layout();
            // Label text shaping and its minimum-height changes finish after the resize notification.
            _layoutPending = true;
        };
    }

    public override void _Process(double delta)
    {
        if (!_layoutPending) return;
        _layoutPending = false;
        Layout();
    }

    /// <summary>Adds a label at <paramref name="place"/> (fractions of the page).</summary>
    public void AddSpot(string text, HorizontalAlignment align, Rect2 place)
    {
        var box = new StyleBoxFlat { BgColor = SpotBack };
        var label = new Label
        {
            Text = text, HorizontalAlignment = align, VerticalAlignment = VerticalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            LabelSettings = new LabelSettings { FontColor = UiStyle.MenuFocus, FontSize = 12 },
        };
        label.AddThemeStyleboxOverride("normal", box);
        AddChild(label);
        _spots.Add((label, place));
        Layout();
        _layoutPending = true;
    }

    private Rect2 Fitted()
    {
        float scale = Math.Min(Size.X / PageWidth, Size.Y / PageHeight);
        var size = new Vector2(PageWidth, PageHeight) * scale;
        return new Rect2((Size - size) * 0.5f, size);
    }

    private void Layout()
    {
        Rect2 page = Fitted();
        int font = Math.Max(9, (int)(page.Size.Y / PageHeight * 11f));
        foreach ((Label label, Rect2 place) in _spots)
        {
            // Drop the old font's minimum width before fitting a previously large label to a smaller window.
            label.LabelSettings.FontSize = font;
            label.UpdateMinimumSize();
            label.Position = page.Position + (place.Position * page.Size);
            label.Size = place.Size * page.Size;
        }

        QueueRedraw();
    }

    public override void _Draw()
    {
        if (Texture is not null)
        {
            DrawTextureRect(Texture, Fitted(), false);
        }
    }
}
