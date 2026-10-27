using System;
using System.Collections.Generic;
using Godot;
using OpenScct.Provenance;
using OpenScctGame.Hud.Magma;

namespace OpenScctGame.Hud;

/// <summary>One console line of the HUD (chat and other <c>TeamMessage</c> texts).</summary>
public readonly record struct HudConsoleLine(string Text, string Type, float Life);

/// <summary>
/// The HUD's console lines (spec/hud/chat.md rules 8 to 11): what <c>PlayerController.TeamMessage</c> and
/// <c>ClientMessage</c> hand to <c>HUD.Message</c>. Four lines at most, each living max(0.09 s a character, 5 s),
/// drawn left of the screen's lower half in the original's dim colours.
/// </summary>
public sealed class HudConsole
{
    [Provenance(ProvenanceKind.Decoded, "Engine.HUD defaults ConsoleMessageCount=4; AddTextMessage keeps four")]
    public const int Lines = 4;

    [Provenance(ProvenanceKind.Decoded, "Engine.LocalMessage defaults Lifetime=5")]
    public const int Lifetime = 5;

    [Provenance(ProvenanceKind.Decoded, "Engine.HUD.AddTextMessage: Len(M) * 0.09")]
    public const float SecondsPerCharacter = 0.09f;

    [Provenance(ProvenanceKind.Decoded, "Engine.HUD.DisplayMessages: XPos = 0.15 SizeX")]
    public const float Left = 0.15f;

    [Provenance(ProvenanceKind.Decoded, "Engine.HUD.DisplayMessages: YPos = 0.61 SizeY")]
    public const float Bottom = 0.61f;

    /// <summary>Say lines: (69, 85, 69, 200) through the canvas (<see cref="HudK.CanvasColour"/>).</summary>
    [Provenance(ProvenanceKind.Decoded, "Engine.HUD.DisplayMessages: Say (69, 85, 69, 200)")]
    public static readonly Color SayColour = HudK.CanvasColour(69, 85, 69, 200);

    /// <summary>Other lines: (69, 69, 85, 200) through the canvas.</summary>
    [Provenance(ProvenanceKind.Decoded, "Engine.HUD.DisplayMessages: other types (69, 69, 85, 200)")]
    public static readonly Color OtherColour = HudK.CanvasColour(69, 69, 85, 200);

    /// <summary>Text height in 480-line page units (the menu font's lines; not decoded, matched to the HUD texts).</summary>
    [Provenance(ProvenanceKind.Estimated, "GUIFontPC height not decoded; the HUD's h12 texts")]
    public const float TextHeight = 12f;

    private readonly List<HudConsoleLine> _lines = new();

    public IReadOnlyList<HudConsoleLine> Current => _lines;

    public MagmaDrawList DrawList { get; } = new();

    /// <summary>
    /// <c>HUD.Message(PRI, Msg, MsgType)</c>: Say and TeamSay lines get "Name: " in front; then
    /// <c>AddTextMessage</c>.
    /// </summary>
    [Uc("Engine.HUD.Message", Sha1 = "4072bb8d6fd965dfc7e66445309ddaefe8c1ccdc")]
    [PortOf("uc:Engine.HUD:Message", Spec = "spec/hud/chat.md#rules")]
    public void Message(string? senderName, string text, string type, float now)
    {
        if ((type == "Say" || type == "TeamSay") && senderName is not null)
        {
            text = senderName + ": " + text;
        }

        AddTextMessage(text, type, now);
    }

    /// <summary><c>HUD.AddTextMessage</c>: the first free of four slots, or the oldest goes.</summary>
    [Uc("Engine.HUD.AddTextMessage", Sha1 = "d301a21f484e1836adbfb4dfd1b2067e53b47404")]
    [PortOf("uc:Engine.HUD:AddTextMessage", Spec = "spec/hud/chat.md#rules")]
    public void AddTextMessage(string text, string type, float now)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (_lines.Count >= Lines)
        {
            _lines.RemoveAt(0);
        }

        float life = now + Math.Max((int)(text.Length * SecondsPerCharacter), Lifetime);
        _lines.Add(new HudConsoleLine(text, type, life));
    }

    /// <summary>
    /// <c>HUD.DisplayMessages</c>: drops the first expired line, then draws the live ones top down so the last one
    /// ends one line above 0.61 of the screen height, from 0.15 of its width.
    /// </summary>
    [Uc("Engine.HUD.DisplayMessages", Sha1 = "7cb1e367aaf9417afd57f4f7358898d0f2f38707")]
    [PortOf("uc:Engine.HUD:DisplayMessages", Spec = "spec/hud/chat.md#rules")]
    public void Draw(Vector2 screen, float now, bool hidden)
    {
        DrawList.Clear();
        int expired = _lines.FindIndex(l => l.Life < now);
        if (expired >= 0)
        {
            _lines.RemoveAt(expired);
        }

        if (hidden || _lines.Count == 0)
        {
            return;
        }

        float s = screen.Y / MagmaRenderer.PageHeight;
        float h = Mathf.Round(TextHeight * s);
        float lineH = Mathf.Round(h * MagmaCanvas.LinePitch);
        float y = Mathf.Floor(Bottom * screen.Y) - lineH * (_lines.Count + 1);
        float x = Mathf.Floor(Left * screen.X);
        for (int i = 0; i < _lines.Count; i++)
        {
            HudConsoleLine l = _lines[i];
            Color c = l.Type == "Say" ? SayColour : OtherColour;
            DrawList.Items.Add(HudText.Item("Console/" + i.ToString(System.Globalization.CultureInfo.InvariantCulture), l.Text,
                new Rect2(x, y, screen.X - x, lineH), h, MagmaAlign.Left, c, new Color(0f, 0f, 0f, 0f)));
            y += lineH;
        }
    }

    public void Clear() => _lines.Clear();
}
