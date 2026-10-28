using System;
using System.Collections.Generic;
using Godot;
using OpenScct.Provenance;

namespace OpenScctGame.UI;

/// <summary>Where the players are now, for the strategic map: the local player (null without a pawn) and his team mates.</summary>
public readonly record struct StrategicMapLive(StrategicMapPosition? You, IReadOnlyList<StrategicMapPosition> Mates)
{
    public static readonly StrategicMapLive Nobody = new(null, Array.Empty<StrategicMapPosition>());
}

/// <summary>
/// The Strategic map page (<c>GUI.StrategicMap</c>, spec/hud/strategic-map.md; docs/MENUS.md): the shown floor's
/// picture with the "you are here" marker and up to two team mates blinking every half second, Up and Down for maps
/// with more than one floor, the map's legend beside it, and Back. In a match the In-Game Menu (or the
/// DisplayStrategicMap key) opens it with <see cref="Live"/> set; the Lobby Room opens it without, as a plain map
/// overview. A map with no floors, or whose pictures are not converted yet, shows the page with "No map picture".
/// </summary>
public partial class StrategicMapScreen : MenuPage
{
    [Provenance(ProvenanceKind.Decoded, "GUI.StrategicMap defaults TitlePage")]
    public const string PageTitle = "Strategic map";

    [Provenance(ProvenanceKind.Decoded, "GUI.StrategicMap defaults ButtonUp, ButtonDown captions")]
    public const string UpCaption = "Up", DownCaption = "Down";

    public const string NoPictureText = "No map picture";

    private readonly StrategicMapModel _model;
    private StrategicMapView? _view;
    private Label? _floor;
    private double _timer;

    public StrategicMapScreen(StrategicMapData data)
    {
        Name = "StrategicMap";
        _model = new StrategicMapModel(data ?? StrategicMapData.Empty);
    }

    /// <summary>Where the players are now (asked every half second); null in the lobby, where no marker is drawn.</summary>
    public Func<StrategicMapLive>? Live { get; init; }

    /// <summary>The page closes itself on Escape and the right mouse button (the lobby); in a match the menu's keys do it.</summary>
    public bool OwnKeys { get; init; }

    public StrategicMapModel Model => _model;

    /// <summary>Back was pressed (or, with <see cref="OwnKeys"/>, Escape).</summary>
    public event Action? Closed;

    public override void _Ready()
    {
        BuildFrame(PageTitle, 1000f);
        float side = Math.Clamp(GetViewportRect().Size.Y * 0.58f, 256f, 760f);

        var row = new HBoxContainer { Name = "Row", Alignment = BoxContainer.AlignmentMode.Center };
        row.AddThemeConstantOverride("separation", 24);
        Body.AddChild(row);

        // GUI.StrategicMap: Up above Down, at the picture's lower left; shown only for a map with more than one floor.
        var floors = new VBoxContainer { Name = "Floors", CustomMinimumSize = new Vector2(150f, 0f), SizeFlagsVertical = SizeFlags.ShrinkEnd };
        floors.AddThemeConstantOverride("separation", 8);
        row.AddChild(floors);
        if (_model.HasFloorButtons)
        {
            _floor = new Label { Name = "FloorCaption", HorizontalAlignment = HorizontalAlignment.Center, Modulate = UiStyle.MenuFocus };
            floors.AddChild(_floor);
            floors.AddChild(FloorButton(UpCaption, () => _model.Up(Now().You, Now().Mates)));
            floors.AddChild(FloorButton(DownCaption, () => _model.Down(Now().You, Now().Mates)));
        }

        _view = new StrategicMapView(_model) { Name = "Map", CustomMinimumSize = new Vector2(side, side) };
        row.AddChild(_view);

        VBoxContainer legend = Panel(row, "Legend", null, 300f);
        legend.GetParent<Control>().SizeFlagsVertical = SizeFlags.ShrinkBegin;
        legend.AddChild(new Label
        {
            Name = "LegendText", Text = _model.Data.Legend, AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(300f, 0f), Modulate = UiStyle.MenuText,
        });

        PageButton("Back", Close, "Back").CallDeferred(Control.MethodName.GrabFocus);

        StrategicMapLive live = Now();
        _model.Open(live.You, live.Mates);
        Refresh();
    }

    public override void _Process(double delta)
    {
        base._Process(delta);
        _timer += delta;
        if (_timer < StrategicMapModel.TimerSeconds)
        {
            return;
        }

        _timer -= StrategicMapModel.TimerSeconds;
        StrategicMapLive live = Now();
        _model.Tick(live.You, live.Mates);
        Refresh();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!OwnKeys)
        {
            return;
        }

        if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape } or InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right })
        {
            GetViewport().SetInputAsHandled();
            Close();
        }
    }

    private void Close() => Closed?.Invoke();

    /// <summary>The live positions, never failing: a page without a source, or a source that throws, shows no markers.</summary>
    private StrategicMapLive Now()
    {
        if (Live is null)
        {
            return StrategicMapLive.Nobody;
        }

        try
        {
            StrategicMapLive live = Live();
            return live.Mates is null ? live with { Mates = Array.Empty<StrategicMapPosition>() } : live;
        }
        catch (Exception e) when (e is NullReferenceException or InvalidOperationException or ObjectDisposedException)
        {
            return StrategicMapLive.Nobody;
        }
    }

    private Button FloorButton(string caption, Func<bool> press)
    {
        Button b = UiStyle.MenuButton(caption, () =>
        {
            if (press())
            {
                Refresh();
            }
        }, caption);
        b.CustomMinimumSize = new Vector2(150f, 40f);
        return b;
    }

    private void Refresh()
    {
        if (_floor is not null)
        {
            _floor.Text = $"Floor {_model.FloorIndex + 1} / {_model.Data.Floors.Count}";
        }

        _view?.QueueRedraw();
    }
}

/// <summary>
/// The map picture of a <see cref="StrategicMapModel"/> with its markers. The original look draws the original's
/// markers (Menu_PC YouAreHereTexPC and ButtonWhite, turned to the player's yaw); the modern look draws its own
/// (a view cone and a dot) in the same colours.
/// </summary>
public partial class StrategicMapView : Control
{
    // GUI.StrategicMap defaults: the picture is 256 units square, the "you are here" marker 20 and a mate 32.
    private const float PictureUnits = 256f, YouUnits = 20f, MateUnits = 32f;

    [Provenance(ProvenanceKind.Decoded, "GUI.StrategicMap defaults ShowSpotColor, HideSpotColor, ShowTeamMateColor, HideTeamMateColor")]
    private static readonly Color ShowSpot = Rgb(255, 255, 255), HideSpot = Rgb(49, 63, 49), ShowMate = Rgb(254, 183, 75), HideMate = Rgb(99, 60, 1);

    private static readonly Dictionary<string, Texture2D?> Pictures = new(StringComparer.OrdinalIgnoreCase);

    private readonly StrategicMapModel _model;

    public StrategicMapView(StrategicMapModel model)
    {
        _model = model;
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Draw()
    {
        var rect = new Rect2(Vector2.Zero, Size);
        DrawStyleBox(UiStyle.ThumbBox(), rect);
        Rect2 inner = rect.Grow(-2f);
        if (Load(_model.Picture) is { } picture)
        {
            DrawTextureRect(picture, inner, false);
        }
        else
        {
            Font font = GetThemeDefaultFont();
            int size = GetThemeDefaultFontSize();
            DrawString(font, new Vector2(inner.Position.X, inner.GetCenter().Y + size * 0.35f), StrategicMapScreen.NoPictureText, HorizontalAlignment.Center, inner.Size.X, size, UiStyle.Dim);
        }

        float scale = inner.Size.X / PictureUnits;
        if (_model.You is { } you)
        {
            DrawYou(inner, you, YouUnits * scale, _model.Bright ? ShowSpot : HideSpot);
        }

        foreach (StrategicMapMarker? mate in _model.Mates)
        {
            if (mate is { } m)
            {
                DrawMate(inner, m, MateUnits * scale, _model.Bright ? ShowMate : HideMate);
            }
        }
    }

    private void DrawYou(Rect2 inner, StrategicMapMarker m, float size, Color colour)
    {
        Vector2 at = inner.Position + new Vector2(m.U, m.V) * inner.Size;
        DrawSetTransform(at, m.Angle, Vector2.One);
        if (UiStyle.IsOriginal && UiStyle.Assets?.Texture("Menu_PC", "YouAreHereTexPC") is { } tex)
        {
            DrawTextureRect(tex, new Rect2(new Vector2(-size, -size) * 0.5f, new Vector2(size, size)), false, colour);
        }
        else
        {
            // A dot with a view cone opening the way the player faces (0: right).
            float r = size * 0.5f;
            DrawColoredPolygon([new Vector2(0f, 0f), new Vector2(r * 1.6f, -r * 0.9f), new Vector2(r * 1.6f, r * 0.9f)], colour with { A = colour.A * 0.45f });
            DrawCircle(Vector2.Zero, r * 0.45f, colour);
        }

        DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
    }

    private void DrawMate(Rect2 inner, StrategicMapMarker m, float size, Color colour)
    {
        Vector2 at = inner.Position + new Vector2(m.U, m.V) * inner.Size;
        if (UiStyle.IsOriginal && UiStyle.Assets?.Texture("Menu_PC", "ButtonWhite") is { } tex)
        {
            DrawSetTransform(at, m.Angle, Vector2.One);
            DrawTextureRect(tex, new Rect2(new Vector2(-size, -size) * 0.5f, new Vector2(size, size)), false, colour);
            DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
            return;
        }

        DrawCircle(at, size * 0.16f, colour);
        DrawArc(at, size * 0.26f, 0f, Mathf.Tau, 24, colour, 1.5f);
    }

    private static Texture2D? Load(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        if (Pictures.TryGetValue(path, out Texture2D? cached))
        {
            return cached;
        }

        var image = new Image();
        Texture2D? texture = image.Load(path) == Error.Ok ? ImageTexture.CreateFromImage(image) : null;
        Pictures[path] = texture;
        return texture;
    }

    private static Color Rgb(int r, int g, int b) => new(r / 255f, g / 255f, b / 255f, 1f);
}
