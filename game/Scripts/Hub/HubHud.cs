using System.Collections.Generic;
using Godot;
using OpenScct.Core;
using OpenScct.Server.Hub;
using OpenScctGame.Hud;
using OpenScctGame.Play;

namespace OpenScctGame.Hub;

/// <summary>
/// The hub's own HUD over the play scene (an OpenSCCT addition): every other player's name over his head, coloured by
/// side (<see cref="HubClient.CollectNameTags"/>), the chat box (the last lines, Enter to type, Enter to send, Escape
/// to cancel; Tab while typing switches to the side's chat), the voice overlay (who is heard, the muted microphone),
/// the match pad panel (top centre, while the player stands on a pad: "Queued on Pad 2 as a merc: 3/4, waiting for 1
/// merc", in the pad's state colour, or the hub service's note about the last match), and the hub keys: J play spy,
/// K play merc, N back to the plaza, O leave the hub, Enter or F5 chat, F6 chat to your side. The activities' part (HubHud.Activities.cs) adds the course
/// clock, the range round, notices, boards, prompts and the emote menu. Self-contained: it reads the visit and the
/// play scene's camera and never writes the simulation.
/// </summary>
public partial class HubHud : CanvasLayer
{
    private const int ChatLines = 8;
    private const int MaxTags = 64;

    private readonly List<HubNameTag> _tags = new();
    private readonly List<Label> _tagLabels = new();
    private readonly List<string> _voiceLines = new();
    private Label? _voice;
    private Label? _chat;
    private Label? _help;
    private Label? _queue;
    private LineEdit? _entry;
    private bool _teamOnly;
    private int _chatCount = -1;

    public HubClient? Client { get; set; }

    public PlayRoot? Round { get; set; }

    public HubService? Service { get; set; }

    /// <summary>True while the player types a chat line (the game's input is off meanwhile).</summary>
    public bool Typing => _entry is { Visible: true };

    /// <summary>Name tags drawn last frame (tests and smokes).</summary>
    public int TagsShown { get; private set; }

    public override void _Ready()
    {
        Name = "HubHud";
        Layer = HudLayer.DefaultLayer + 3;
        _help = new Label
        {
            Name = "Help", Position = new Vector2(16f, 40f), Modulate = new Color(1f, 1f, 1f, 0.75f),
            Text = "Hub   J spy   K merc   N plaza   G emotes   U use   X stop   Enter/F5 chat   F6 team chat   O leave   Stand on a match pad to queue",
        };
        if (OpenScctGame.UI.UiArt.CaptionFont is { } caption)
        {
            _help.AddThemeFontOverride("font", caption);
            _help.AddThemeFontSizeOverride("font_size", OpenScctGame.UI.UiArt.CaptionSize);
        }

        AddChild(_help);
        _queue = new Label { Name = "Queue", HorizontalAlignment = HorizontalAlignment.Center, Visible = false };
        _queue.AddThemeColorOverride("font_shadow_color", new Color(0f, 0f, 0f, 1f));
        _queue.AddThemeConstantOverride("shadow_offset_x", 2);
        _queue.AddThemeConstantOverride("shadow_offset_y", 2);
        _queue.AddThemeFontSizeOverride("font_size", 24);
        if (OpenScctGame.UI.UiArt.BodyFont is { } font)
        {
            _queue.AddThemeFontOverride("font", font);
        }

        AddChild(_queue);
        _voice = new Label { Name = "Voice", HorizontalAlignment = HorizontalAlignment.Right };
        _voice.AddThemeColorOverride("font_shadow_color", new Color(0f, 0f, 0f, 0.9f));
        AddChild(_voice);
        _chat = new Label { Name = "Chat", AutowrapMode = TextServer.AutowrapMode.WordSmart, VerticalAlignment = VerticalAlignment.Bottom };
        _chat.AddThemeColorOverride("font_shadow_color", new Color(0f, 0f, 0f, 0.9f));
        AddChild(_chat);
        _entry = new LineEdit { Name = "ChatEntry", Visible = false, MaxLength = OpenScct.Net.ChatK.MaxChars, PlaceholderText = "Say" };
        _entry.TextSubmitted += Send;
        AddChild(_entry);
        for (int i = 0; i < MaxTags; i++)
        {
            var label = new Label { Visible = false, HorizontalAlignment = HorizontalAlignment.Center };
            label.AddThemeColorOverride("font_shadow_color", new Color(0f, 0f, 0f, 1f));
            label.AddThemeConstantOverride("shadow_offset_x", 1);
            label.AddThemeConstantOverride("shadow_offset_y", 1);
            _tagLabels.Add(label);
            AddChild(label);
        }

        ReadyActivities();
    }

    public override void _Process(double delta)
    {
        Vector2 size = GetViewport().GetVisibleRect().Size;
        if (_chat is not null && _entry is not null)
        {
            _chat.Position = new Vector2(16f, size.Y - 260f);
            _chat.Size = new Vector2(size.X * 0.45f, 200f);
            _entry.Position = new Vector2(16f, size.Y - 52f);
            _entry.Size = new Vector2(size.X * 0.45f, 32f);
        }

        if (Client is not { } client)
        {
            return;
        }

        RefreshChat(client);
        DrawQueue(client, size);
        DrawTags(client);
        DrawVoice(size);
        DrawActivities(client, size);
    }

    /// <summary>Reloaded's voice overlay, top right: who is heard (and at which volume), the muted microphone.</summary>
    private void DrawVoice(Vector2 size)
    {
        if (_voice is null)
        {
            return;
        }

        Service?.VoiceChat?.OverlayLines(_voiceLines);
        _voice.Visible = _voiceLines.Count > 0;
        if (_voice.Visible)
        {
            _voice.Text = string.Join('\n', _voiceLines);
            _voice.Size = new Vector2(360f, 0f);
            _voice.Position = new Vector2(size.X - 376f, 12f);
        }
    }

    /// <summary>The player's place in a pad's queue (or the last match's note), top centre.</summary>
    private void DrawQueue(HubClient client, Vector2 size)
    {
        if (_queue is null)
        {
            return;
        }

        string text = client.Queue.PadId is not null ? client.Queue.Text : Service?.MatchNote ?? string.Empty;
        _queue.Visible = text.Length > 0;
        if (!_queue.Visible)
        {
            return;
        }

        _queue.Text = text;
        _queue.Modulate = client.Queue.PadId is { } pad && client.Pads.TryGetValue(pad, out OpenScct.Server.Hub.HubPadInfo? info)
            ? HubPadView.StateColour(info.State)
            : new Color(1f, 1f, 1f, 0.9f);
        _queue.Size = new Vector2(size.X * 0.8f, 0f);
        _queue.Position = new Vector2(size.X * 0.1f, 84f);
    }

    private void RefreshChat(HubClient client)
    {
        if (_chat is null || client.Chat.Count == _chatCount && _chatCount < HubClient.MaxChat)
        {
            return;
        }

        _chatCount = client.Chat.Count;
        var sb = new System.Text.StringBuilder();
        IReadOnlyList<HubChatEntry> lines = client.Chat;
        for (int i = System.Math.Max(0, lines.Count - ChatLines); i < lines.Count; i++)
        {
            HubChatEntry e = lines[i];
            sb.AppendLine(e.IsSystem ? "* " + e.Text : (e.TeamOnly ? "(team) " : string.Empty) + e.Name + ": " + e.Text);
        }

        _chat.Text = sb.ToString();
    }

    private void DrawTags(HubClient client)
    {
        Camera3D? camera = Round?.View?.Camera;
        TagsShown = 0;
        if (camera is null || !Round!.IsLoaded)
        {
            HideTags(0);
            return;
        }

        FVector eye = UeSpace.ToUe(camera.GlobalPosition);
        client.NameTags(eye, _tags);
        int shown = 0;
        foreach (HubNameTag tag in _tags)
        {
            if (shown >= _tagLabels.Count)
            {
                break;
            }

            Vector3 at = UeSpace.ToGodot(tag.Location);
            if (camera.IsPositionBehind(at))
            {
                continue;
            }

            Label label = _tagLabels[shown++];
            Vector2 screen = camera.UnprojectPosition(at);
            label.Text = client.Play.EmoteOf(tag.PlayerId) is { } emote ? tag.Name + "\n" + HubEmotes.Caption(emote) : tag.Name;
            label.Modulate = tag.Team == 1 ? HudK.MercColour : HudK.SpyColour;
            float scale = Mathf.Clamp(1.4f - tag.Distance / HubK.NameTagRange, 0.6f, 1.2f);
            label.Scale = new Vector2(scale, scale);
            label.ResetSize();
            label.Position = screen - new Vector2(label.Size.X * scale / 2f, label.Size.Y * scale);
            label.Visible = true;
        }

        TagsShown = shown;
        HideTags(shown);
    }

    private void HideTags(int from)
    {
        for (int i = from; i < _tagLabels.Count; i++)
        {
            _tagLabels[i].Visible = false;
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key || Client is not { } client)
        {
            return;
        }

        if (EmoteMenuInput(key))
        {
            GetViewport().SetInputAsHandled();
            return;
        }

        if (Typing)
        {
            if (key.Keycode == Key.Escape)
            {
                CloseEntry();
                GetViewport().SetInputAsHandled();
            }
            else if (key.Keycode == Key.Tab)
            {
                _teamOnly = !_teamOnly;
                _entry!.PlaceholderText = _teamOnly ? "Say to your side" : "Say";
                GetViewport().SetInputAsHandled();
            }

            return;
        }

        switch (key.Keycode)
        {
            case Key.Enter or Key.KpEnter:
                OpenEntry();
                break;
            // Hub keys sit on keys the original's default layouts leave free; F5 and F6 open the chat line like
            // the original's Talk and TeamTalk.
            case Key.F5:
                OpenEntry();
                break;
            case Key.F6:
                OpenEntry(teamOnly: true);
                break;
            case Key.J:
                client.RequestSide(0);
                break;
            case Key.K:
                client.RequestSide(1);
                break;
            case Key.N:
                client.Respawn();
                break;
            case Key.O:
                Service?.Leave();
                break;
            case Key.G:
                OpenEmoteMenu();
                break;
            case Key.U:
                client.Use();
                break;
            case Key.X:
                client.CancelActivity();
                break;
            default:
                return;
        }

        GetViewport().SetInputAsHandled();
    }

    private void OpenEntry(bool teamOnly = false)
    {
        if (_entry is null)
        {
            return;
        }

        _teamOnly = teamOnly;
        _entry.PlaceholderText = _teamOnly ? "Say to your side" : "Say";
        _entry.Text = string.Empty;
        _entry.Visible = true;
        _entry.GrabFocus();
        if (Round?.Controls is { } input)
        {
            input.Enabled = false;
        }
    }

    private void CloseEntry()
    {
        if (_entry is null)
        {
            return;
        }

        _entry.Visible = false;
        _entry.ReleaseFocus();
        if (Round?.Controls is { } input)
        {
            input.Enabled = true;
        }
    }

    private void Send(string text)
    {
        string line = text.Trim();
        if (line.Length > 1 && line[0] == '/' && HubEmotes.IsEmote(line[1..].ToLowerInvariant()))
        {
            // "/wave" and the like play the emote instead of saying it.
            Client?.Emote(line[1..].ToLowerInvariant());
        }
        else if (line.Length > 0)
        {
            Client?.Say(line, _teamOnly);
        }

        CloseEntry();
    }
}
