using System.Globalization;
using Godot;
using OpenScct.Net.Voice;

namespace OpenScctGame.Voice;

/// <summary>
/// A plain voice overlay (Reloaded's speaking list, FEATURES rl:voice.mix) drawn from <see cref="VoiceChat.Speakers"/>:
/// one line per speaking or muted player, team coloured, with "team" for team talk and "muted" for muted players, and
/// "Your microphone is muted" while self mute is on. Top right, under the HUD's layer. The Magma HUD can replace it by
/// subscribing to <see cref="VoiceChat.SpeakerChanged"/> itself.
/// </summary>
public partial class VoiceIndicator : CanvasLayer
{
    public const int DefaultLayer = 11;

    private static readonly Color SpyColour = new(0.55f, 0.85f, 0.45f);
    private static readonly Color MercColour = new(0.95f, 0.55f, 0.35f);
    private static readonly Color OtherColour = new(0.85f, 0.85f, 0.85f);

    private VBoxContainer? _rows;
    private bool _dirty = true;
    private bool _shownMuted;

    public VoiceIndicator() => Layer = DefaultLayer;

    public VoiceChat? Chat { get; set; }

    /// <summary>The lines shown, for tests.</summary>
    public string Text { get; private set; } = string.Empty;

    public override void _Ready()
    {
        _rows = new VBoxContainer { Name = "VoiceRows" };
        _rows.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        _rows.Position = new Vector2(-260, 12);
        _rows.CustomMinimumSize = new Vector2(250, 0);
        AddChild(_rows);
        if (Chat is not null)
        {
            Chat.SpeakerChanged += _ => _dirty = true;
        }
    }

    public override void _Process(double delta)
    {
        if (Chat is null || _rows is null)
        {
            return;
        }

        bool selfMuted = Chat.Settings.MicMuted;
        if (!_dirty && selfMuted == _shownMuted)
        {
            return;
        }

        _dirty = false;
        _shownMuted = selfMuted;
        foreach (Node child in _rows.GetChildren())
        {
            _rows.RemoveChild(child);
            child.QueueFree();
        }

        var text = new System.Text.StringBuilder();
        if (selfMuted)
        {
            AddRow("Your microphone is muted", OtherColour, text);
        }

        foreach (VoiceSpeaker s in Chat.Speakers)
        {
            if (s.IsLocal && !s.Speaking)
            {
                continue;
            }

            string line = s.Name + (s.Channel == EVoiceChannel.Team ? "  (team)" : string.Empty) + (s.Muted && !s.IsLocal ? "  muted" : string.Empty);
            if (!s.IsLocal && s.Volume != 1f)
            {
                line += "  " + (s.Volume * 100f).ToString("0", CultureInfo.InvariantCulture) + "%";
            }

            AddRow(line, s.Team switch { 0 => SpyColour, 1 => MercColour, _ => OtherColour }, text);
        }

        Text = text.ToString();
    }

    private void AddRow(string line, Color colour, System.Text.StringBuilder text)
    {
        var label = new Label { Text = line, HorizontalAlignment = HorizontalAlignment.Right };
        label.AddThemeColorOverride("font_color", colour);
        label.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.8f));
        _rows!.AddChild(label);
        text.AppendLine(line);
    }
}
