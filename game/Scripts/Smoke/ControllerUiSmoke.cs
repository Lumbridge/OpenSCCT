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
    private SubViewport? _viewport;
    private PlayInput? _input;
    private ChatInput? _chat;
    private GamepadInput? _pad;
    private ControllerUiProbe? _probe;
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
                    _viewport = new SubViewport { Size = new Vector2I(1280, 720), Disable3D = true,
                        RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
                    context.Tree.Root.AddChild(_viewport);
                    _viewport.AddChild(new ColorRect { Size = new Vector2(1280, 720), Color = new Color(.035f, .055f, .045f), MouseFilter = Control.MouseFilterEnum.Ignore });
                    _input = new PlayInput { Enabled = false, RequireCapture = false, CapturePointer = false, Target = new LocalInput(),
                        Bindings = InputBindings.FromLayout(KeyLayout.Parse("[Engine.Input]\nIK_Q=QuickChat"), PlaySide.Spy) };
                    _viewport.AddChild(_input);
                    _pad = new GamepadInput { Settings = settings, PlayInput = _input };
                    _viewport.AddChild(_pad);
                    _pad.SetProcess(false);
                    _chat = new ChatInput { PlayInput = _input, Clock = () => _now };
                    _viewport.AddChild(_chat);
                    var column = new VBoxContainer { Position = new Vector2(40, 40), Size = new Vector2(220, 300) };
                    _viewport.AddChild(column);
                    for (int i = 0; i < 5; i++)
                    {
                        var button = new Button { Text = "Menu " + i, CustomMinimumSize = new Vector2(200, 40) };
                        button.Pressed += () => { _activated++; _chat.Open(EChatScope.Say); };
                        _buttons.Add(button);
                        column.AddChild(button);
                    }
                    _probe = new ControllerUiProbe();
                    _viewport.AddChild(_probe);
                    _viewport.AddChild(new PadMenus());
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
                    return Finish(SmokeResult.Pass("A/B gameplay isolation, D-pad/stick deduplication, menu accept, keyboard/pad quick chat, team-only phrases, back and timeout"));
            }
            return SmokeResult.Running;
        }
        catch (Exception error) { return Finish(SmokeResult.Fail(error.Message)); }
    }

    private void Button(JoyButton button, bool pressed) => _viewport!.PushInput(new InputEventJoypadButton { ButtonIndex = button, Pressed = pressed });
    private void Axis(float value) => _viewport!.PushInput(new InputEventJoypadMotion { Axis = JoyAxis.LeftY, AxisValue = value });
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
        _viewport?.Free();
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
