using System;
using System.Collections.Generic;
using System.Text;
using Godot;
using OpenScct.Net;
using OpenScctGame.Controls;
using OpenScctGame.Hud;
using OpenScctGame.Hud.Magma;

namespace OpenScctGame.Chat;

/// <summary>Where typed chat lines go (the client session in play, a list in tests).</summary>
public interface IChatLink
{
    void Send(EChatScope scope, string text);
}

public sealed class SessionChatLink(OpenScct.Server.ClientSession client) : IChatLink
{
    public void Send(EChatScope scope, string text) => client.SendChat(scope, text);
}

/// <summary>The shared chat composer's input and gameplay-focus boundary for matches and the hub.</summary>
public partial class ChatInput : CanvasLayer, IHudOverlay
{
    public const string SayPrefix = "Say ";
    public const string TeamSayPrefix = "TeamSay ";

    private bool _ready;
    private bool _inputWasEnabled;
    private Input.MouseModeEnum _pointerWas;

    public ChatInput()
    {
        Name = "ChatInput";
        Layer = HudLayer.DefaultLayer + 1;
    }

    public ChatPanel Panel { get; } = new();
    public ChatTranscript Transcript { get => Panel.Transcript; set => Panel.Transcript = value; }
    public Func<double> Clock { get => Panel.Clock; set => Panel.Clock = value; }
    public bool AllowNearby { get => Panel.AllowNearby; set => Panel.AllowNearby = value; }
    public bool HubLayout { get => Panel.HubLayout; set => Panel.HubLayout = value; }
    public IChatLink? Link { get; set; }
    public PlayInput? PlayInput { get; set; }

    /// <summary>A hub can send through its existing channel router instead of the match link.</summary>
    public Action<ChatChannel, string>? SendMessage { get; set; }
    public Func<bool>? CanOpen { get; set; }
    public Func<bool>? VisibilityAllowed { get; set; }

    /// <summary>The hub supplies its own chat stream; do not also display the match relay's copy.</summary>
    public bool ReceiveMatchMessages { get; set; } = true;
    public bool IsOpen { get; private set; }
    public static ChatInput? Typing { get; private set; }
    public EChatScope Scope => Panel.Channel == ChatChannel.Team ? EChatScope.TeamSay : EChatScope.Say;
    public string Typed => IsOpen ? (Scope == EChatScope.TeamSay ? TeamSayPrefix : SayPrefix) + Panel.Draft : "";
    public List<(EChatScope Scope, string Text)> Sent { get; } = new();

    public override void _Ready()
    {
        Panel.Name = "ChatPanel";
        Panel.Submitted += OnSubmitted;
        AddChild(Panel);
        _ready = true;
        Panel.SetExpanded(IsOpen);
    }

    public void Open(EChatScope scope) => Open(scope == EChatScope.TeamSay ? ChatChannel.Team : ChatChannel.All);

    public void Open(ChatChannel channel)
    {
        if (IsOpen || CanOpen?.Invoke() == false || VisibilityAllowed?.Invoke() == false ||
            Typing is not null || PlayInput is { Enabled: false }) return;

        IsOpen = true;
        Typing = this;
        Panel.Channel = channel == ChatChannel.Nearby && !AllowNearby ? ChatChannel.All : channel;
        Panel.Draft = "";
        _pointerWas = Pointer.Mode;
        if (PlayInput is { } input)
        {
            _inputWasEnabled = input.Enabled;
            if (input.Target is { } target) input.ClearHeld(target);
            input.Enabled = false;
            input.ReleasePointer();
        }
        else Pointer.Release();
        if (_ready) Panel.SetExpanded(true);
    }

    public void Close() => Close(restorePointer: true);

    private void Close(bool restorePointer)
    {
        if (!IsOpen) return;
        IsOpen = false;
        if (Typing == this) Typing = null;
        Panel.Draft = "";
        if (_ready) Panel.SetExpanded(false);
        if (PlayInput is { } input)
        {
            if (input.Target is { } target) input.ClearHeld(target);
            // A transiently hidden HUD still returns gameplay input; another open overlay owns it instead.
            bool restore = restorePointer && CanOpen?.Invoke() != false;
            if (restore) input.Enabled = _inputWasEnabled;
            if (restore && input.Enabled && input.HasInputFocus && VisibilityAllowed?.Invoke() != false &&
                (Pointer.Detached || DisplayServer.GetName() != "headless"))
                Pointer.Mode = _pointerWas;
        }
        else if (restorePointer && (Pointer.Detached || DisplayServer.GetName() != "headless")) Pointer.Mode = _pointerWas;
    }

    public override void _ExitTree()
    {
        Close(restorePointer: false);
        if (Typing == this) Typing = null;
        Panel.Submitted -= OnSubmitted;
    }

    public override void _Process(double delta)
    {
        bool visible = VisibilityAllowed?.Invoke() != false;
        Visible = visible;
        if (IsOpen && (!visible || CanOpen?.Invoke() == false)) Close(restorePointer: true);
    }

    /// <summary>Convenience for scripted checks; normal editing belongs to the native LineEdit.</summary>
    public void Type(char c)
    {
        if (!IsOpen || char.IsControl(c) || Panel.Draft.Length >= ChatK.MaxChars) return;
        Panel.Draft += c;
        if (_ready) Panel.Entry.CaretColumn = Panel.Draft.Length;
    }

    public void Backspace()
    {
        if (!IsOpen || Panel.Draft.Length == 0) return;
        Panel.Draft = Panel.Draft[..^1];
        if (_ready) Panel.Entry.CaretColumn = Panel.Draft.Length;
    }

    public void Submit() => OnSubmitted(Panel.Draft);

    private void OnSubmitted(string draft)
    {
        if (!IsOpen) return;
        ChatChannel channel = Panel.Channel;
        EChatScope scope = Scope;
        string text = Clean(draft);
        Close();
        if (text.Length == 0) return;
        Sent.Add((scope, text));
        if (Sent.Count > 64) Sent.RemoveAt(0);
        if (SendMessage is { } send) send(channel, text);
        else Link?.Send(scope, text);
    }

    private static string Clean(string text)
    {
        var result = new StringBuilder(Math.Min(text.Length, ChatK.MaxChars));
        foreach (char c in text)
        {
            if (result.Length >= ChatK.MaxChars) break;
            if (!char.IsControl(c)) result.Append(c);
        }
        return result.ToString().Trim();
    }

    public override void _Input(InputEvent @event)
    {
        if (!IsOpen)
        {
            if (@event is not (InputEventKey { Pressed: true, Echo: false } or InputEventMouseButton { Pressed: true }) ||
                PlayInput is not { Enabled: true } input || !input.HasInputFocus ||
                GetViewport().GuiGetFocusOwner() is LineEdit or TextEdit) return;
            string? ik = GodotKeys.ToIk(@event);
            EChatScope? scope = ik is null ? null : CommandOf(ik);
            bool enter = @event is InputEventKey enterKey && Code(enterKey) is Key.Enter or Key.KpEnter &&
                !enterKey.AltPressed && !enterKey.CtrlPressed && !enterKey.MetaPressed &&
                ik is not null && (input.Bindings?.Actions(ik).Count ?? 0) == 0;
            if (scope is null && !enter) return;
            Open(scope ?? EChatScope.Say);
            if (IsOpen) GetViewport().SetInputAsHandled();
            return;
        }

        if (@event is not InputEventKey { Pressed: true } key) return;
        Key code = Code(key);
        if (code == Key.Escape)
        {
            Close();
            GetViewport().SetInputAsHandled();
        }
        else if (code == Key.Tab)
        {
            if (!key.Echo) Panel.CycleChannel();
            Panel.Entry.GrabFocus();
            GetViewport().SetInputAsHandled();
        }
        else if (code >= Key.F1 && code <= Key.F35)
        {
            if (!key.Echo && GodotKeys.ToIk(key) is { } ik && CommandOf(ik) is { } scope)
                Panel.Channel = scope == EChatScope.TeamSay ? ChatChannel.Team : ChatChannel.All;
            // Function keys must not open options or other gameplay overlays behind the focused editor.
            GetViewport().SetInputAsHandled();
        }
    }

    private static Key Code(InputEventKey key) => key.Keycode == Key.None ? key.PhysicalKeycode : key.Keycode;

    public override void _UnhandledInput(InputEvent @event)
    {
        if (IsOpen) GetViewport().SetInputAsHandled();
    }

    private EChatScope? CommandOf(string ik)
    {
        if (PlayInput?.Bindings is not { } bindings) return null;
        foreach (KeyAction action in bindings.Actions(ik))
        {
            if (action.Kind != KeyActionKind.Command) continue;
            if (string.Equals(action.Name, "Talk", StringComparison.OrdinalIgnoreCase)) return EChatScope.Say;
            if (string.Equals(action.Name, "TeamTalk", StringComparison.OrdinalIgnoreCase)) return EChatScope.TeamSay;
        }
        return null;
    }

    /// <summary>Compatibility with HUD overlay callers; ChatPanel now draws the native controls.</summary>
    public void Draw(MagmaDrawList into, Vector2 screen, WhiteRegion white) { }
}
