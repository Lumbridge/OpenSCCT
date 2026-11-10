using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using OpenScct.Net;
using OpenScctGame.Chat;
using OpenScctGame.Controls;
using OpenScctGame.Options;
using OpenScctGame.Play.Session;

namespace OpenScctGame.Smoke;

/// <summary>Real viewport dispatch: pad ownership, one-step navigation and optional team quick chat.</summary>
public sealed class ControllerUiSmoke : SmokeTest
{
    public const string TestName = "controller-ui";
    public override string Name => TestName;
    private Viewport? _viewport;
    private Node? _fixture;
    private PlayInput? _input;
    private ChatInput? _chat;
    private GamepadInput? _pad;
    private ControllerUiProbe? _probe;
    private PadMenus? _menus;
    private UI.MainMenu? _main;
    private readonly Vector2I[] _noticeSizes = [new(800, 600), new(1280, 720), new(3840, 2160)];
    private int _noticeSize;
    private readonly List<Button> _buttons = new();
    private GameSettings? _previous;
    private bool _detached;
    private int _step;
    private int _activated;
    private double _now = 100;

    public override SmokeResult Step(SmokeContext context)
    {
        try
        {
            switch (_step++)
            {
                case 0:
                    _previous = SettingsStore.Current;
                    _detached = Pointer.Detached;
                    Pointer.Detached = true;
                    var settings = new GameSettings();
                    settings.Pad.Enabled = true;
                    settings.Pad.SpyButtons["LS"] = "Action:quickchat";
                    SettingsStore.Reset(settings);
                    // Use the application's real window and PadMenus instance, including native input state.
                    _viewport = context.Tree.Root;
                    _menus = context.App.GetNode<PadMenus>(PadMenus.NodeName);
                    _fixture = new Node();
                    _viewport.AddChild(_fixture);
                    _fixture.AddChild(new ColorRect { Size = new Vector2(1280, 720), Color = new Color(.035f, .055f, .045f), MouseFilter = Control.MouseFilterEnum.Ignore });
                    _input = new PlayInput { Enabled = false, RequireCapture = false, CapturePointer = false, Target = new LocalInput(),
                        Bindings = InputBindings.FromLayout(KeyLayout.Parse("[Engine.Input]\nIK_Q=QuickChat"), PlaySide.Spy) };
                    _fixture.AddChild(_input);
                    _pad = new GamepadInput { Settings = settings, PlayInput = _input };
                    _fixture.AddChild(_pad);
                    _pad.SetProcess(false);
                    _chat = new ChatInput { PlayInput = _input, Clock = () => _now };
                    _fixture.AddChild(_chat);
                    var column = new VBoxContainer { Position = new Vector2(40, 40), Size = new Vector2(220, 300) };
                    _fixture.AddChild(column);
                    for (int i = 0; i < 5; i++)
                    {
                        var button = new Button { Text = "Menu " + i, CustomMinimumSize = new Vector2(200, 40) };
                        button.Pressed += () => { _activated++; _chat.Open(EChatScope.Say); };
                        _buttons.Add(button);
                        column.AddChild(button);
                    }
                    _probe = new ControllerUiProbe();
                    _fixture.AddChild(_probe);
                    return SmokeResult.Running;
                case 1:
                case 2:
                    return SmokeResult.Running;
                case 3:
                    foreach (PlaySide side in new[] { PlaySide.Spy, PlaySide.Merc })
                        Require(new UI.KeyBindingsModel(side, staged: true, defaults: true).PadChoices()
                            .Any(choice => choice.Action == "Action:quickchat"), "controller editor is missing quick chat");
                    _buttons[0].GrabFocus();
                    Button(JoyButton.DpadDown, true);
                    Button(JoyButton.DpadDown, true);
                    Button(JoyButton.DpadDown, false);
                    Require(_viewport!.GuiGetFocusOwner() == _buttons[1], "one D-pad press skipped menu rows: " + _viewport.GuiGetFocusOwner()?.GetPath());
                    _buttons[0].GrabFocus();
                    foreach (int device in new[] { 0, 1 })
                        BindingInput.Inject(new InputEventJoypadButton { Device = device, ButtonIndex = JoyButton.DpadDown, Pressed = true });
                    foreach (int device in new[] { 0, 1 })
                        BindingInput.Inject(new InputEventJoypadButton { Device = device, ButtonIndex = JoyButton.DpadDown });
                    Require(_viewport.GuiGetFocusOwner() == _buttons[1], "duplicate controller devices skipped D-pad rows: row " + _buttons.IndexOf((Button)_viewport.GuiGetFocusOwner()!));
                    _buttons[0].GrabFocus();
                    foreach (int device in new[] { 0, 1 })
                    {
                        BindingInput.Inject(new InputEventJoypadButton { Device = device, ButtonIndex = JoyButton.DpadDown, Pressed = true });
                        BindingInput.Inject(new InputEventJoypadButton { Device = device, ButtonIndex = JoyButton.DpadDown });
                    }
                    Require(_viewport.GuiGetFocusOwner() == _buttons[1], "a delayed virtual-controller tap skipped D-pad rows");
                    foreach (int device in new[] { 0, 1 })
                        BindingInput.Inject(new InputEventJoypadMotion { Device = device, Axis = JoyAxis.LeftY, AxisValue = .8f });
                    foreach (int device in new[] { 0, 1 })
                        BindingInput.Inject(new InputEventJoypadMotion { Device = device, Axis = JoyAxis.LeftY });
                    Require(_viewport.GuiGetFocusOwner() == _buttons[2], "duplicate controller devices skipped analog rows");
                    _buttons[0].GrabFocus();
                    for (int i = 0; i < 2; i++)
                    {
                        Button(JoyButton.DpadDown, true);
                        Button(JoyButton.DpadDown, false);
                    }
                    Require(_viewport.GuiGetFocusOwner() == _buttons[2], "two quick taps on one controller lost a menu step");
                    _buttons[1].GrabFocus();
                    for (int i = 0; i < 12; i++) Axis(.72f + i * .02f);
                    Axis(.45f);
                    Axis(.1f);
                    Require(_viewport.GuiGetFocusOwner() == _buttons[2], "repeated stick events skipped menu rows");
                    _input!.Enabled = true;
                    Button(JoyButton.A, true);
                    Button(JoyButton.A, false);
                    Button(JoyButton.B, true);
                    Button(JoyButton.B, false);
                    Require(!_chat!.IsOpen && _activated == 0 && _probe!.Escapes == 0, "gameplay A/B leaked into chat or Escape");
                    _input.Bindings = InputBindings.FromLayout(KeyLayout.Parse("[Engine.Input]"), PlaySide.Spy);
                    _pad!.UpdateButtons(_input, false, new HashSet<string> { "LS" }, .016);
                    Require(_chat.QuickChat.IsOpen && !_input.Enabled, "bound pad quick chat did not open independently");
                    Button(JoyButton.DpadDown, true);
                    Button(JoyButton.DpadDown, true);
                    Require(_chat.Sent.Count == 0, "duplicate category press sent a phrase");
                    Button(JoyButton.DpadDown, false);
                    Button(JoyButton.DpadUp, true);
                    Button(JoyButton.DpadUp, false);
                    Require(!_chat.QuickChat.IsOpen && _input.Enabled, "quick-chat send failed to return gameplay input");
                    Require(_chat.Sent.Count == 1 && _chat.Sent[0] == (EChatScope.TeamSay, "Enemy spotted!"), "quick chat did not use team scope");
                    _pad.UpdateButtons(_input, false, new HashSet<string>(), .016);
                    _now += 2;
                    _input.Bindings = InputBindings.FromLayout(KeyLayout.Parse("[Engine.Input]\nIK_Q=QuickChat"), PlaySide.Spy);
                    TapKey(Key.Q);
                    Require(_chat.QuickChat.IsOpen, "keyboard binding did not open quick chat");
                    TapKey(Key.Key4);
                    TapKey(Key.Escape);
                    Require(_chat.QuickChat.IsOpen, "back from a category closed the root");
                    TapKey(Key.Escape);
                    Require(!_chat.QuickChat.IsOpen && _input.Enabled && _chat.Sent.Count == 1, "cancel sent a message or kept input disabled");
                    TapKey(Key.Q);
                    TapKey(Key.Key4);
                    TapKey(Key.Key3);
                    Require(_chat.Sent.Count == 2 && _chat.Sent[1] == (EChatScope.TeamSay, "Thanks!"), "keyboard phrase sequence failed: " + string.Join(", ", _chat.Sent));
                    _now += 2;
                    TapKey(Key.Q);
                    _now += QuickChatState.TimeoutSeconds + 1;
                    return SmokeResult.Running;
                case 4:
                    Require(!_chat!.QuickChat.IsOpen && _input!.Enabled && _chat.Sent.Count == 2, "timeout failed to cancel safely");
                    _chat.QuickChat.Open();
                    return SmokeResult.Running;
                case 5:
                    return SmokeResult.Running;
                case 6:
                    if (DisplayServer.GetName() != "headless" && System.Environment.GetEnvironmentVariable("SVM_PLAY_SHOTS") is { Length: > 0 } shots)
                    {
                        System.IO.Directory.CreateDirectory(shots);
                        _viewport!.GetTexture().GetImage().SavePng(System.IO.Path.Combine(shots, "quick-chat.png"));
                    }
                    _chat!.QuickChat.Close();
                    _input!.Enabled = false;
                    _buttons[0].GrabFocus();
                    Button(JoyButton.A, true);
                    Button(JoyButton.A, false);
                    Require(_activated == 1, "menu A no longer activated the focused button");
                    _chat.Close();
                    _fixture!.Free();
                    _fixture = null;
                    _main = UI.FrontEnd.ShowMainMenu(context.Tree);
                    return SmokeResult.Running;
                case 7:
                case 8:
                    return SmokeResult.Running;
                case 9:
                    _main!.FindButton!.GrabFocus();
                    Button(JoyButton.DpadDown, true);
                    Button(JoyButton.DpadDown, false);
                    Require(_viewport!.GuiGetFocusOwner() == _main.HostButton, "main-menu D-pad down skipped Create a Session");
                    Button(JoyButton.DpadUp, true);
                    Button(JoyButton.DpadUp, false);
                    Require(_viewport.GuiGetFocusOwner() == _main.FindButton, "main-menu D-pad up skipped Find a Session");
                    Axis(.9f);
                    Axis(0);
                    Require(_viewport.GuiGetFocusOwner() == _main.HostButton, "main-menu analog down skipped Create a Session");
                    Axis(-.9f);
                    Axis(0);
                    Require(_viewport.GuiGetFocusOwner() == _main.FindButton, "main-menu analog up skipped Find a Session");
                    ShowNotice(context);
                    return SmokeResult.Running;
                case 10:
                    // Let responsive page and notice containers settle after each real-window resize.
                    return SmokeResult.Running;
                case 11:
                    CheckNotice();
                    if (DisplayServer.GetName() != "headless" && System.Environment.GetEnvironmentVariable("SVM_PLAY_SHOTS") is { Length: > 0 } noticeShots)
                    {
                        System.IO.Directory.CreateDirectory(noticeShots);
                        _viewport!.GetTexture().GetImage().SavePng(System.IO.Path.Combine(noticeShots, "controller-notice-" + _noticeSizes[_noticeSize].X + ".png"));
                    }
                    if (++_noticeSize < _noticeSizes.Length)
                    {
                        ShowNotice(context);
                        _step = 10;
                        return SmokeResult.Running;
                    }
                    _menus!.ShowNotice(PadMenus.NoticeText(false, ""));
                    return SmokeResult.Running;
                case 12:
                    Require(_menus!.GetNode<Label>("PadNotices/PadNoticePanel/PadNotice").Text == "Gamepad disconnected", "disconnect notice did not replace connection notice");
                    _menus._Process(PadMenus.NoticeSeconds + .1);
                    Require(!_menus.GetNode<Control>("PadNotices/PadNoticePanel").Visible, "controller notice did not expire");
                    return Finish(SmokeResult.Pass("real-window D-pad/analog single steps, duplicate-device isolation, gameplay A/B, quick chat, and readable controller notices at 800x600/720p/4K"));
            }
            return SmokeResult.Running;
        }
        catch (Exception error) { return Finish(SmokeResult.Fail(error.Message)); }
    }

    private void Button(JoyButton button, bool pressed)
    {
        BindingInput.Inject(new InputEventJoypadButton { ButtonIndex = button, Pressed = pressed });
    }
    private void Axis(float value)
    {
        BindingInput.Inject(new InputEventJoypadMotion { Axis = JoyAxis.LeftY, AxisValue = value });
    }
    private void ShowNotice(SmokeContext context)
    {
        context.Tree.Root.Size = _noticeSizes[_noticeSize];
        _menus!.ShowNotice(PadMenus.NoticeText(true, "Wireless Controller with a deliberately long device name to verify clipped text"));
    }
    private void CheckNotice()
    {
        var panel = _menus!.GetNode<PanelContainer>("PadNotices/PadNoticePanel");
        Rect2 bounds = panel.GetGlobalRect();
        Require(panel.IsVisibleInTree() && _viewport!.GetVisibleRect().Encloses(bounds), "controller notice left the window");
        Require(panel.MouseFilter == Control.MouseFilterEnum.Ignore && panel.FocusMode == Control.FocusModeEnum.None, "controller notice intercepted menu input");
        foreach (Control control in _main!.PageButtons.Concat<Control>([_main.GetNode<Control>("ResponsiveCanvas/BuildInfo")]))
            Require(!bounds.Intersects(control.GetGlobalRect()), "controller notice overlaps the footer: " + control.Name);
        Require(panel.GetParent<CanvasLayer>().Layer > 10, "controller notice is behind the gameplay HUD");
    }
    private void TapKey(Key key)
    {
        var press = new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true };
        var release = new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false };
        BindingInput.Current?.Resolve(press);
        _viewport!.PushInput(press);
        BindingInput.Current?.Resolve(release);
        _viewport.PushInput(release);
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private SmokeResult Finish(SmokeResult result)
    {
        _fixture?.Free();
        _fixture = null;
        _viewport = null;
        if (_previous is not null) SettingsStore.Reset(_previous);
        Pointer.Detached = _detached;
        return result;
    }
}

public partial class ControllerUiProbe : Node
{
    public int Escapes { get; private set; }
    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Keycode: Godot.Key.Escape }) Escapes++;
    }
}
