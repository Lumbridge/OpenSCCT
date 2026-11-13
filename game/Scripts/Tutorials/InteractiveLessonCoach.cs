using System;
using System.Linq;
using Godot;
using OpenScctGame.Hud;
using OpenScctGame.Play;
using OpenScctGame.Play.Session;
using OpenScctGame.UI;

namespace OpenScctGame.Tutorials;

/// <summary>Native desktop coaching, measurements and safe target controls for the shared TestRange drills.</summary>
public partial class InteractiveLessonCoach : CanvasLayer
{
    public const string NodeName = "InteractiveLessonCoach";
    private BrowserLesson? _lesson;
    private PlayRoot? _play;
    private Label? _feedback, _results;
    private VBoxContainer? _actions;
    private Button? _moving, _debug;
    private PanelContainer? _panel;
    private bool _overlayReady;

    public string Kind { get; init; } = "vision-cancelling";
    public BrowserLesson? Lesson => _lesson;

    public InteractiveLessonCoach() { Name = NodeName; Layer = HudLayer.DefaultLayer + 5; }

    public override void _Ready()
    {
        _play = GetParent<PlayRoot>();
        if (_play.Session is { } session) _lesson = new BrowserLesson(session, Kind);
        var canvas = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Theme = UiStyle.MenuTheme };
        AddChild(canvas);
        canvas.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _panel = new PanelContainer { Position = new Vector2(24, 104), MouseFilter = Control.MouseFilterEnum.Ignore };
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.025f, 0.045f, 0.035f, 0.94f), BorderColor = UiStyle.MenuFocus with { A = 0.5f },
            ContentMarginLeft = 16, ContentMarginRight = 16, ContentMarginTop = 12, ContentMarginBottom = 12,
        };
        style.SetBorderWidthAll(1);
        style.SetCornerRadiusAll(6);
        _panel.AddThemeStyleboxOverride("panel", style);
        canvas.AddChild(_panel);
        var column = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        column.AddThemeConstantOverride("separation", 8);
        _panel.AddChild(column);
        column.AddChild(new Label
        {
            Text = Kind == "spy-grab" ? "MERCENARY GRABBING" : "VISION CANCELLING", Modulate = UiStyle.MenuFocus,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        });
        _feedback = Text(column, "Preparing practice…");
        _results = Text(column, "");
        Label hint = Text(column, "Use your normal bindings. Escape opens practice controls.");
        hint.Modulate = UiStyle.Dim;
        if (_play.Menu?.Menu is { } menu) menu.MainPageLeftInset = () => _panel.Size.X + 48;
        _actions = new VBoxContainer();
        column.AddChild(_actions);
        AddAction("Retry", Kind == "spy-grab" ? "Reset positions" : "Return to ledge", () =>
        {
            if (Kind == "spy-grab") InteractiveLessonPlay.Start(GetTree(), Kind);
            else _lesson?.Action("reset");
        });
        if (Kind == "spy-grab")
        {
            _moving = AddAction("MovingTarget", "Target: stopped", () => _lesson?.Action("moving"));
            _debug = AddAction("GrabDebug", "Hide grab area", () =>
            {
                if (_play is not null) _play.ShowGrabArea(!_play.GrabAreaShown);
            });
        }
        AddAction("Tutorials", "Back to Tutorials", () =>
        {
            _play?.Session?.Stop();
            TutorialsScreen.Show(GetTree());
        });
    }

    private static Label Text(Node column, string text)
    {
        var label = new Label
        {
            Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        label.AddThemeFontSizeOverride("font_size", 16);
        column.AddChild(label);
        return label;
    }

    private Button AddAction(string name, string caption, Action action)
    {
        Button button = UiStyle.MenuButton(caption, action, name);
        button.CustomMinimumSize = new Vector2(0, 36);
        _actions!.AddChild(button);
        return button;
    }

    public override void _Process(double delta)
    {
        if (_lesson is null || _play is null || _feedback is null || _results is null || _actions is null || _panel is null) return;
        _panel.CustomMinimumSize = new Vector2(Math.Clamp(GetViewport().GetVisibleRect().Size.X * 0.3f, 300, 440), 0);
        _panel.Size = new Vector2(_panel.CustomMinimumSize.X, 0);
        _feedback.Text = _lesson.Feedback;
        _actions.Visible = _play.Menu?.IsOpen == true && _play.Menu.Model.Page == InGameMenuPage.Main;
        _panel.Visible = _play.Menu?.IsOpen != true || _actions.Visible;
        if (Kind == "spy-grab")
        {
            if (!_overlayReady && _lesson.Ready) _overlayReady = _play.ShowGrabArea(true);
            _lesson.Debug = _play.GrabAreaShown;
            if (_moving is not null) _moving.Text = _lesson.Moving ? "Target: walking (click to stop)" : "Target: stopped (click to walk)";
            if (_debug is not null) _debug.Text = _play.GrabAreaShown ? "Hide grab area" : "Show grab area";
            _results.Text = $"Successful grabs: {_lesson.Grabs}" + (_lesson.Distance is { } distance ? $" · Distance: {distance:0} units" : "") +
                "\nThe target cannot attack or kill you. Use Interact or your Grab only binding from behind.";
        }
        else
        {
            if (_lesson.Ready && _play.GetNodeOrNull<Node3D>("ReturnPortal") is null) _play.AddChild(new BrowserReturnPortal());
            string baseline = _lesson.BaselineMs is { } ms ? $"Baseline {ms:0} ms" : "First drop: measure a baseline without switching";
            string best = _lesson.BestMs is { } fastest ? $" · Best {fastest:0} ms" : "";
            _results.Text = baseline + best + "\n" + string.Join("\n", _lesson.History.TakeLast(3).Select(a =>
                $"#{a.Number} {a.Result}: {a.RecoveryMs:0} ms" + (a.SavedMs is { } saved ? $" · saved {saved:0} ms" : "")));
        }
    }

    public override void _ExitTree() => _lesson?.Dispose();
}
