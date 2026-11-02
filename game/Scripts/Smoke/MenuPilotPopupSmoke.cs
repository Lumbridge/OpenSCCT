using System;
using Godot;
using OpenScctGame.Controls;
using OpenScctGame.UI;

namespace OpenScctGame.Smoke;

/// <summary>Real GUI-event slider clicks across popup viewport boundaries and ordinary scrolling.</summary>
public sealed class MenuPilotPopupSmoke : SmokeTest
{
    public const string TestName = "menu-pilot-popup";
    public override string Name => TestName;
    private SceneTree? _tree;
    private MenuPilot? _pilot;
    private CanvasLayer? _layer;
    private ScrollContainer? _scroll;
    private Control? _row;
    private HSlider? _rootSlider, _scrollSlider, _popupSlider;
    private PopupPanel? _popup;
    private bool _savedEmbed, _savedDetached, _nativeChecked;
    private int _phase, _wait, _scrollBefore, _dragEnds;
    private double _appliedVolume;

    public override SmokeResult Step(SmokeContext context)
    {
        try
        {
            if (context.ElapsedSeconds > 20) return Finish("popup slider smoke timed out");
            if (_wait-- > 0) return SmokeResult.Running;
            switch (_phase)
            {
                case 0:
                    Build(context.Tree);
                    Advance();
                    break;
                case 1:
                    Require(_pilot!.ClickSliderAt(_rootSlider, 40, "root slider"), "root slider click was refused");
                    Advance();
                    break;
                case 2:
                    Require(_rootSlider!.Value == 40, $"root slider reads {_rootSlider.Value}, expected 40");
                    _scroll!.ScrollVertical = 580;
                    OpenPopup(embedded: true);
                    Advance();
                    break;
                case 3:
                    ClickPopup();
                    Advance();
                    break;
                case 4:
                    CheckPopup();
                    ClosePopup();
                    if (DisplayServer.HasFeature(DisplayServer.Feature.Subwindows))
                    {
                        OpenPopup(embedded: false);
                        Advance();
                    }
                    else { _phase = 7; _wait = 4; }
                    break;
                case 5:
                    ClickPopup();
                    Advance();
                    break;
                case 6:
                    CheckPopup();
                    _nativeChecked = true;
                    ClosePopup();
                    Advance();
                    break;
                case 7:
                    _scrollBefore = _scroll!.ScrollVertical;
                    Require(!_pilot!.ClickSliderAt(_scrollSlider, 40, "scrolled slider"), "offscreen slider should scroll before clicking");
                    Advance();
                    break;
                case 8:
                    Require(_scroll!.ScrollVertical > _scrollBefore, "same-viewport scroll did not reveal the slider");
                    Require(_pilot!.ClickSliderAt(_scrollSlider, 40, "scrolled slider"), "revealed slider click was refused");
                    Advance();
                    break;
                case 9:
                    Require(_scrollSlider!.Value == 40, $"scrolled slider reads {_scrollSlider.Value}, expected 40");
                    return Finish(null);
            }
            return SmokeResult.Running;
        }
        catch (Exception exception) { return Finish(exception.Message); }
    }

    private void Build(SceneTree tree)
    {
        _tree = tree;
        _savedEmbed = tree.Root.GuiEmbedSubwindows;
        _savedDetached = Pointer.Detached;
        Pointer.Detached = true;
        _pilot = new MenuPilot(tree);
        _layer = new CanvasLayer { Name = "PopupPilotFixture", Layer = 150 };
        tree.Root.AddChild(_layer);
        _rootSlider = Slider("RootSlider");
        _rootSlider.Position = new Vector2(740, 80);
        _rootSlider.Size = new Vector2(280, 28);
        _layer.AddChild(_rootSlider);
        _scroll = new ScrollContainer
        {
            Name = "OuterScroll", Position = new Vector2(80, 300), Size = new Vector2(500, 220),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        _layer.AddChild(_scroll);
        var column = new VBoxContainer { CustomMinimumSize = new Vector2(460, 0), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _scroll.AddChild(column);
        column.AddChild(new Control { CustomMinimumSize = new Vector2(0, 600) });
        _row = new Control { Name = "PlayerRow", CustomMinimumSize = new Vector2(0, 40) };
        column.AddChild(_row);
        column.AddChild(new Control { CustomMinimumSize = new Vector2(0, 500) });
        _scrollSlider = Slider("ScrollSlider");
        column.AddChild(_scrollSlider);
        column.AddChild(new Control { CustomMinimumSize = new Vector2(0, 100) });
    }

    private static HSlider Slider(string name) => new()
    {
        Name = name, MinValue = 0, MaxValue = 200, Step = 5, Value = 100,
        CustomMinimumSize = new Vector2(280, 28),
    };

    private void OpenPopup(bool embedded)
    {
        _tree!.Root.GuiEmbedSubwindows = embedded;
        _dragEnds = 0;
        _appliedVolume = 1;
        _popup = new PopupPanel { Name = embedded ? "EmbeddedPlayerMenu" : "NativePlayerMenu" };
        _row!.AddChild(_popup);
        var menu = new VBoxContainer { CustomMinimumSize = new Vector2(300, 0) };
        _popup.AddChild(menu);
        menu.AddChild(new Label { Text = "Voice volume" });
        _popupSlider = Slider("PopupVolume");
        menu.AddChild(_popupSlider);
        _popupSlider.ValueChanged += value => _appliedVolume = value / 100d;
        _popupSlider.DragEnded += _ => { _dragEnds++; _appliedVolume = _popupSlider.Value / 100d; };
        _popup.PopupCentered(new Vector2I(340, 180));
    }

    private void ClickPopup()
    {
        _scrollBefore = _scroll!.ScrollVertical;
        Require(_popupSlider!.GetViewport() == _popup, "popup slider does not own a separate viewport");
        Require(_popupSlider.GetGlobalRect().Position.Y < _scroll.GetGlobalRect().Position.Y,
            "fixture must separate popup-local coordinates from outer scroll coordinates");
        Require(_pilot!.ClickSliderAt(_popupSlider, 40, "popup voice volume"), "popup click was mistaken for an outer scroll request");
    }

    private void CheckPopup()
    {
        Require(_popupSlider!.Value == 40 && Math.Abs(_appliedVolume - .4) < .001 && _dragEnds == 1,
            $"popup value={_popupSlider.Value}, applied={_appliedVolume}, drag releases={_dragEnds}; expected 40, 0.4, 1");
        Require(_scroll!.ScrollVertical == _scrollBefore, "popup click moved the unrelated outer scroll container");
    }

    private void ClosePopup()
    {
        _popup?.Hide();
        _popup?.QueueFree();
        _popup = null;
    }

    private void Advance() { _phase++; _wait = MenuPilot.SettleFrames; }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private SmokeResult Finish(string? error)
    {
        ClosePopup();
        _layer?.QueueFree();
        if (_tree is not null) _tree.Root.GuiEmbedSubwindows = _savedEmbed;
        Pointer.Detached = _savedDetached;
        return error is null
            ? SmokeResult.Pass("root and scrolled sliders; embedded popup volume 40%, drag release; " +
                (_nativeChecked ? "native popup passed" : "native popup unavailable on this display server"))
            : SmokeResult.Fail(error);
    }
}
