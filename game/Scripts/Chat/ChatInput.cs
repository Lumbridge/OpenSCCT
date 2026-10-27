using System;
using System.Collections.Generic;
using Godot;
using OpenScct.Net;
using OpenScct.Provenance;
using OpenScctGame.Controls;
using OpenScctGame.Hud;
using OpenScctGame.Hud.Magma;

namespace OpenScctGame.Chat;

/// <summary>Where typed chat lines go (the client session in play, a list in tests).</summary>
public interface IChatLink
{
    /// <summary>Sends what the player typed; the server relays it to its recipients.</summary>
    void Send(EChatScope scope, string text);
}

/// <summary>The chat link of a client session (<see cref="OpenScct.Server.ClientSession.SendChat"/>).</summary>
public sealed class SessionChatLink(OpenScct.Server.ClientSession client) : IChatLink
{
    public void Send(EChatScope scope, string text) => client.SendChat(scope, text);
}

/// <summary>
/// The chat line (spec/hud/chat.md rules 1 to 3): the Talk key (F5) opens it with <c>Say </c>, TeamTalk (F6) with
/// <c>TeamSay </c>; printable keys type while the line is shorter than 100 characters, Backspace deletes back to the
/// prefix, Escape drops the line and Enter sends it. While it is open no key reaches the player. It is drawn across
/// the bottom of the screen in the HUD font.
/// </summary>
public partial class ChatInput : Node, IHudOverlay
{
    public const string SayPrefix = "Say ";
    public const string TeamSayPrefix = "TeamSay ";

    [Provenance(ProvenanceKind.Decoded, "Engine.Console s_ChatTyping.PostRender: text (255, 255, 255, 128)")]
    public static readonly Color TextColour = HudK.CanvasColour(255, 255, 255, 128);

    [Provenance(ProvenanceKind.Decoded, "Engine.Console s_ChatTyping.PostRender: borders (69, 85, 69, 255)")]
    public static readonly Color EdgeColour = HudK.CanvasColour(69, 85, 69);

    /// <summary>The dark band behind the line (Engine.ConsoleBK; its colour is not decoded).</summary>
    [Provenance(ProvenanceKind.Estimated, "Engine.ConsoleBK drawn at (255, 255, 255, 128) in style 5; the texture's colour is not decoded")]
    public static readonly Color BandColour = new(0f, 0f, 0f, 0.5f);

    private string _typed = "";
    private EChatScope _scope;

    public ChatInput()
    {
        Name = "ChatInput";
    }

    /// <summary>Where the lines go.</summary>
    public IChatLink? Link { get; set; }

    /// <summary>The play input's key layout (which keys are Talk and TeamTalk) and its held keys.</summary>
    public PlayInput? PlayInput { get; set; }

    /// <summary>The line is open.</summary>
    public bool IsOpen { get; private set; }

    /// <summary>What the console line holds now, prefix included (<c>Say hello</c>).</summary>
    public string Typed => _typed;

    public EChatScope Scope => _scope;

    /// <summary>Lines sent so far (tests).</summary>
    public List<(EChatScope Scope, string Text)> Sent { get; } = new();

    /// <summary>Opens the line (the Talk and TeamTalk execs). Nothing happens while it is already open.</summary>
    [OpenScct.Provenance.Uc("SBase.SPlayerController.Talk", Sha1 = "7d0da07a32dbe6e748a319537552fba21165f6c1")]
    [OpenScct.Provenance.Uc("SBase.SPlayerController.TeamTalk", Sha1 = "19793d8992e86ded76cb4055d19c35d0831f339f")]
    [OpenScct.Provenance.PortOf("uc:SBase.SPlayerController:Talk", Spec = "spec/hud/chat.md#rules")]
    [OpenScct.Provenance.PortOf("uc:SBase.SPlayerController:TeamTalk", Spec = "spec/hud/chat.md#rules")]
    public void Open(EChatScope scope)
    {
        if (IsOpen)
        {
            return;
        }

        IsOpen = true;
        _scope = scope;
        _typed = scope == EChatScope.TeamSay ? TeamSayPrefix : SayPrefix;
        if (PlayInput is { Target: { } target } input)
        {
            input.ClearHeld(target);
        }
    }

    /// <summary>Closes the line without sending.</summary>
    public void Close()
    {
        IsOpen = false;
        _typed = "";
    }

    private int PrefixLength => _scope == EChatScope.TeamSay ? TeamSayPrefix.Length : SayPrefix.Length;

    /// <summary>A typed character (KeyType): printable, not ~ or `, while the line is shorter than 100.</summary>
    public void Type(char c)
    {
        if (!IsOpen || c < 32 || c == '~' || c == '`' || char.IsControl(c))
        {
            return;
        }

        if (_typed.Length < ChatK.MaxChars)
        {
            _typed += c;
        }
    }

    /// <summary>Backspace: deletes the last character, never the prefix.</summary>
    public void Backspace()
    {
        if (IsOpen && _typed.Length > PrefixLength)
        {
            _typed = _typed[..^1];
        }
    }

    /// <summary>Enter: sends the line when something follows the prefix, then closes.</summary>
    public void Submit()
    {
        if (!IsOpen)
        {
            return;
        }

        string text = _typed.Length > PrefixLength ? _typed[PrefixLength..] : "";
        EChatScope scope = _scope;
        Close();
        if (text.Trim().Length > 0)
        {
            Sent.Add((scope, text));
            Link?.Send(scope, text);
        }
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is not InputEventKey key)
        {
            return;
        }

        if (!IsOpen)
        {
            if (key.Pressed && !key.Echo && PlayInput is { Enabled: true } && GodotKeys.ToIk(@event) is { } ik && CommandOf(ik) is { } scope)
            {
                Open(scope);
                GetViewport().SetInputAsHandled();
            }

            return;
        }

        GetViewport().SetInputAsHandled();
        if (!key.Pressed)
        {
            return;
        }

        switch (key.Keycode)
        {
            case Key.Escape:
                Close();
                return;
            case Key.Enter:
            case Key.KpEnter:
                Submit();
                return;
            case Key.Backspace:
            case Key.Left:
                Backspace();
                return;
        }

        if (key.Unicode != 0)
        {
            Type((char)key.Unicode);
        }
    }

    /// <summary>The chat scope a key opens (its layout binds Talk or TeamTalk), or null.</summary>
    private EChatScope? CommandOf(string ik)
    {
        if (PlayInput?.Bindings is not { } bindings)
        {
            return null;
        }

        foreach (KeyAction a in bindings.Actions(ik))
        {
            if (a.Kind != KeyActionKind.Command)
            {
                continue;
            }

            if (string.Equals(a.Name, "Talk", StringComparison.OrdinalIgnoreCase))
            {
                return EChatScope.Say;
            }

            if (string.Equals(a.Name, "TeamTalk", StringComparison.OrdinalIgnoreCase))
            {
                return EChatScope.TeamSay;
            }
        }

        return null;
    }

    /// <summary>
    /// The open line as the console draws it: a band of the line's height plus 6 pixels at <c>ClipY - 22 - YL</c>,
    /// 2-pixel edges at <c>ClipY - 24 - YL</c> and <c>ClipY - 18</c>, and <c>"> " + TypedStr + "_"</c> at
    /// <c>ClipY - 19 - YL</c> from the left edge.
    /// </summary>
    public void Draw(MagmaDrawList into, Vector2 screen, WhiteRegion white)
    {
        ArgumentNullException.ThrowIfNull(into);
        if (!IsOpen)
        {
            return;
        }

        float s = screen.Y / MagmaRenderer.PageHeight;
        float yl = Mathf.Round(HudConsole.TextHeight * s);
        into.Items.Add(white.Quad("Chat/band", new Vector2(0f, screen.Y - 22f - yl), new Vector2(screen.X, yl + 6f), BandColour));
        into.Items.Add(white.Quad("Chat/top", new Vector2(0f, screen.Y - 24f - yl), new Vector2(screen.X, 2f), EdgeColour));
        into.Items.Add(white.Quad("Chat/bottom", new Vector2(0f, screen.Y - 18f), new Vector2(screen.X, 2f), EdgeColour));
        into.Items.Add(HudText.Item("Chat/line", "> " + _typed + "_", new Rect2(4f, screen.Y - 19f - yl, screen.X - 8f, yl), yl,
            MagmaAlign.Left, TextColour, new Color(0f, 0f, 0f, 0f)));
    }
}
