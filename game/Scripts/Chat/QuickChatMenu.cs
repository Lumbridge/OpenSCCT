using System;
using System.Collections.Generic;
using Godot;
using OpenScctGame.Controls;
using OpenScctGame.Options;
using OpenScctGame.UI;

namespace OpenScctGame.Chat;

/// <summary>Optional team quick chat: number keys or D-pad choose a category, then a phrase.</summary>
public partial class QuickChatMenu : CanvasLayer
{
    private readonly QuickChatState _state = new();
    private readonly Button[] _choices = new Button[4];
    private readonly Label _title = new();
    private PanelContainer? _panel;
    private bool _ownsInput, _inputWasEnabled;
    private Input.MouseModeEnum _pointerWas;
    private Control? _previousFocus;
    private readonly HashSet<JoyButton> _down = new();
    public ChatInput? Composer { get; set; }
    public bool IsOpen => _state.IsOpen;
    private double Now => Composer?.Clock() ?? Time.GetTicksMsec() / 1000d;

    public QuickChatMenu() { Name = "QuickChat"; Layer = Hud.HudLayer.DefaultLayer + 3; }

    public override void _Ready()
    {
        _panel = new PanelContainer { Name = "QuickChatPanel", Theme = UiStyle.Theme, Visible = false };
        _panel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterRight);
        _panel.OffsetLeft = -410;
        _panel.OffsetRight = -24;
        _panel.OffsetTop = -160;
        _panel.OffsetBottom = 160;
        _panel.AddThemeStyleboxOverride("panel", UiArt.PanelBox());
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 8);
        _title.Text = "TEAM QUICK CHAT";
        column.AddChild(_title);
        string[] directions = ["↑", "→", "↓", "←"];
        for (int i = 0; i < _choices.Length; i++)
        {
            int choice = i;
            var button = new Button { Name = "Choice" + (i + 1), Alignment = HorizontalAlignment.Left };
            button.SetMeta("hint", (i + 1) + " / D-pad " + directions[i]);
            button.Pressed += () => Choose(choice);
            _choices[i] = button;
            column.AddChild(button);
        }
        column.AddChild(new Label { Text = "1–4 / D-pad: choose   Esc / B: back", Modulate = UiStyle.Dim });
        var back = new Button { Text = "Back", Name = "Back" };
        back.Pressed += Back;
        column.AddChild(back);
        _panel.AddChild(column);
        AddChild(_panel);
    }

    public void Open()
    {
        if (Composer is not { PlayInput: { Enabled: true, HasInputFocus: true } input } chat || chat.IsOpen ||
            chat.CanOpen?.Invoke() == false || chat.VisibilityAllowed?.Invoke() == false ||
            BindingInput.Current?.CaptureSuspended == true || !_state.Open(Now)) return;
        _inputWasEnabled = input.Enabled;
        _pointerWas = Pointer.Mode;
        _previousFocus = GetViewport().GuiGetFocusOwner();
        _down.Clear();
        int device = PadMenus.CurrentDevice();
        if (device >= 0)
            foreach (JoyButton button in new[] { JoyButton.DpadUp, JoyButton.DpadRight, JoyButton.DpadDown, JoyButton.DpadLeft, JoyButton.B })
                if (Input.IsJoyButtonPressed(device, button)) _down.Add(button);
        _ownsInput = true;
        input.Enabled = false;
        input.ReleasePointer();
        Refresh();
    }

    private void Refresh()
    {
        if (_panel is null) return;
        _panel.Visible = IsOpen;
        if (!IsOpen) return;
        _title.Text = _state.Category is { } category ? "TEAM — " + QuickChatState.Categories[category].Name.ToUpperInvariant() : "TEAM QUICK CHAT";
        for (int i = 0; i < _choices.Length; i++)
        {
            string text = _state.Category is { } selected ? QuickChatState.Categories[selected].Messages[i] : QuickChatState.Categories[i].Name;
            _choices[i].Text = _choices[i].GetMeta("hint").AsString() + "   " + text;
        }
        _choices[0].GrabFocus();
    }

    private void Choose(int index)
    {
        string? message = _state.Choose(index, Now);
        if (IsOpen) { Refresh(); return; }
        Close();
        if (message is not null) Composer?.SendQuickMessage(message);
    }

    private void Back() { _state.Back(); if (IsOpen) Refresh(); else Close(); }

    public void Close(bool restore = true)
    {
        _state.Close();
        if (_panel is not null) _panel.Visible = false;
        if (!_ownsInput) return;
        _ownsInput = false;
        GetViewport().GuiReleaseFocus();
        GamepadInput.SuppressHeldButtons();
        if (Composer?.PlayInput is { } input)
        {
            if (input.Target is { } target) input.ClearHeld(target);
            if (restore && Composer.CanOpen?.Invoke() != false)
            {
                input.Enabled = _inputWasEnabled;
                if (input.Enabled && input.HasInputFocus && Composer.VisibilityAllowed?.Invoke() != false)
                    Pointer.Mode = _pointerWas;
                if (_previousFocus is { } focus && IsInstanceValid(focus) && focus.IsVisibleInTree()) focus.GrabFocus();
            }
        }
        _previousFocus = null;
    }

    public override void _Process(double delta)
    {
        if (!_ownsInput) return;
        if (_state.Expire(Now) || Composer?.CanOpen?.Invoke() == false || Composer?.VisibilityAllowed?.Invoke() == false ||
            Composer?.PlayInput?.HasInputFocus == false) Close();
    }

    public override void _ExitTree() => Close(restore: false);

    public override void _Input(InputEvent @event)
    {
        if (!IsOpen) return;
        if (@event is InputEventJoypadButton pad)
        {
            GetViewport().SetInputAsHandled();
            if (!pad.Pressed) { _down.Remove(pad.ButtonIndex); return; }
            if (!_down.Add(pad.ButtonIndex)) return;
            int index = pad.ButtonIndex switch
            {
                JoyButton.DpadUp => 0, JoyButton.DpadRight => 1, JoyButton.DpadDown => 2, JoyButton.DpadLeft => 3, _ => -1,
            };
            if (index >= 0) Choose(index);
            else if (pad.ButtonIndex is JoyButton.B or JoyButton.Back or JoyButton.Start) Back();
            return;
        }
        if (@event is InputEventJoypadMotion) { GetViewport().SetInputAsHandled(); return; }
        if (@event is not InputEventKey key) return;
        GetViewport().SetInputAsHandled();
        if (!key.Pressed || key.Echo) return;
        Key code = key.Keycode == Key.None ? key.PhysicalKeycode : key.Keycode;
        if (code is >= Key.Key1 and <= Key.Key4) Choose((int)(code - Key.Key1));
        else if (code is >= Key.Kp1 and <= Key.Kp4) Choose((int)(code - Key.Kp1));
        else if (code is Key.Escape or Key.Backspace) Back();
        else if (code is Key.Up or Key.Right or Key.Down or Key.Left)
            Choose(code == Key.Up ? 0 : code == Key.Right ? 1 : code == Key.Down ? 2 : 3);
        else if (code is Key.Enter or Key.KpEnter && GetViewport().GuiGetFocusOwner() is Button button) button.EmitSignal(Button.SignalName.Pressed);
    }
}
