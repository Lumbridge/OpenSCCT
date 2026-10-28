using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Godot;
using OpenScct.Core;
using OpenScct.Server.Hub;
using OpenScctGame.UI;

namespace OpenScctGame.Hub;

/// <summary>
/// The hub HUD's activities (an OpenSCCT addition): the course clock and checkpoints, the range round's clock and
/// score (top middle), notices (under it), the nearest score board within <see cref="HubPlayK.BoardShowRange"/>
/// (right), what U does here (bottom middle) and the emote menu (G, then 1 to 8; Escape closes). Text in the menus'
/// fonts (Inter, JetBrains Mono).
/// </summary>
public partial class HubHud
{
    private readonly List<(string Text, bool Big)> _toasts = new();
    private Label? _activity;
    private Label? _notices;
    private Label? _board;
    private Label? _boardTitle;
    private Panel? _boardPanel;
    private Label? _prompt;
    private Label? _emoteMenu;
    private bool _emoteOpen;

    private void ReadyActivities()
    {
        _activity = MakeLabel("Activity", UiArt.WordBoldFont, 30, HorizontalAlignment.Center);
        _notices = MakeLabel("Notices", UiArt.BodyFont, 20, HorizontalAlignment.Center);
        _prompt = MakeLabel("Prompt", UiArt.BodyFont, 17, HorizontalAlignment.Center);
        _boardPanel = new Panel { Name = "BoardPanel", Visible = false, SelfModulate = new Color(0f, 0f, 0f, 0.55f) };
        AddChild(_boardPanel);
        _boardTitle = MakeLabel("BoardTitle", UiArt.TitleFont, 18, HorizontalAlignment.Left);
        _boardTitle.Modulate = UiArt.Accent;
        _board = MakeLabel("Board", UiArt.CaptionFont, 15, HorizontalAlignment.Left);
        _emoteMenu = MakeLabel("EmoteMenu", UiArt.CaptionFont, 16, HorizontalAlignment.Left);
        var sb = new StringBuilder("EMOTES\n");
        for (int i = 0; i < HubEmotes.All.Count; i++)
        {
            sb.Append((i + 1).ToString(CultureInfo.InvariantCulture)).Append("  ").Append(HubEmotes.All[i]).Append('\n');
        }

        sb.Append("Esc  close");
        _emoteMenu.Text = sb.ToString();
        _emoteMenu.Visible = false;
    }

    private Label MakeLabel(string name, Font? font, int size, HorizontalAlignment align)
    {
        var label = new Label { Name = name, HorizontalAlignment = align };
        if (font is not null)
        {
            label.AddThemeFontOverride("font", font);
        }

        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_shadow_color", new Color(0f, 0f, 0f, 0.9f));
        label.AddThemeConstantOverride("shadow_offset_x", 1);
        label.AddThemeConstantOverride("shadow_offset_y", 1);
        AddChild(label);
        return label;
    }

    private void DrawActivities(HubClient client, Vector2 size)
    {
        HubPlayState play = client.Play;
        if (_activity is null || _notices is null || _prompt is null || _board is null || _boardTitle is null || _boardPanel is null || _emoteMenu is null)
        {
            return;
        }

        // The clock of what the player is doing.
        string activity = string.Empty;
        if (play.Running)
        {
            activity = $"COURSE  {HubScores.Time(play.RunMs)}\ncheckpoint {play.Passed}/{play.Checkpoints}" +
                       (play.LastSplit >= 0 ? $"   split {HubScores.Time(play.LastSplit)}" : string.Empty) +
                       (play.Falls > 0 ? $"   falls {play.Falls}" : string.Empty);
        }
        else if (play.InRound)
        {
            double left = System.Math.Max(0.0, play.RoundEndsAt - play.Now);
            int accuracy = play.RangeShots > 0 ? (int)System.Math.Round(100.0 * play.RangeHits / play.RangeShots) : 0;
            activity = string.Create(CultureInfo.InvariantCulture,
                $"RANGE  {left:0.0} s   {play.RangePoints} pts\nhits {play.RangeHits}/{play.RangeShots}  {accuracy}%   headshots {play.RangeHeads}");
        }
        else if (play.Now - play.LastRunAt < 6.0)
        {
            activity = "COURSE  " + HubScores.Time(play.LastRun);
        }

        _activity.Text = activity;
        _activity.Visible = activity.Length > 0;
        _activity.Size = new Vector2(size.X, 0f);
        _activity.Position = new Vector2(0f, 24f);

        play.Toasts(_toasts);
        var sb = new StringBuilder();
        foreach (var (text, big) in _toasts)
        {
            sb.Append(big ? "> " + text + " <" : text).Append('\n');
        }

        _notices.Text = sb.ToString();
        _notices.Size = new Vector2(size.X, 0f);
        _notices.Position = new Vector2(0f, 110f);

        FVector? eye = Round?.View?.Camera is { } camera && Round.IsLoaded ? UeSpace.ToUe(camera.GlobalPosition) : null;
        DrawBoard(client, play, eye, size);
        _prompt.Text = eye is { } at ? Prompt(client, play, at) : string.Empty;
        _prompt.Size = new Vector2(size.X, 0f);
        _prompt.Position = new Vector2(0f, size.Y - 100f);

        _emoteMenu.Visible = _emoteOpen;
        _emoteMenu.Position = new Vector2(size.X * 0.5f - 90f, size.Y * 0.5f - 120f);
    }

    /// <summary>
    /// The nearest board within range, in a panel on the right, when the world does not show it: a board the hub put up
    /// as a world sign is read there, not twice.
    /// </summary>
    private void DrawBoard(HubClient client, HubPlayState play, FVector? eye, Vector2 size)
    {
        HubBoard? nearest = null;
        float best = HubPlayK.BoardShowRange;
        if (eye is { } at)
        {
            foreach ((string id, HubBoard b) in play.Boards)
            {
                float d = (b.At - at).Size();
                if (d < best && b.Lines.Length > 0 && !client.Signs.ContainsKey(id))
                {
                    best = d;
                    nearest = b;
                }
            }
        }

        bool show = nearest is not null;
        _boardPanel!.Visible = show;
        _board!.Visible = show;
        _boardTitle!.Visible = show;
        if (nearest is null)
        {
            return;
        }

        _boardTitle.Text = nearest.Lines[0];
        _board.Text = string.Join('\n', nearest.Lines, 1, nearest.Lines.Length - 1);
        float width = 360f;
        float x = size.X - width - 16f;
        float y = 200f;
        _boardTitle.Position = new Vector2(x + 14f, y + 10f);
        _board.Position = new Vector2(x + 14f, y + 42f);
        _board.ResetSize();
        _boardPanel.Position = new Vector2(x, y);
        _boardPanel.Size = new Vector2(width, 56f + _board.Size.Y);
    }

    /// <summary>What U (or walking on) does here.</summary>
    private static string Prompt(HubClient client, HubPlayState play, FVector at)
    {
        if (client.Queue.PadId is not null)
        {
            // On a match pad U readies up (the queue panel above says who is ready).
            return client.Queue.Side >= 0
                ? (client.Queue.Text.Contains("you are ready", System.StringComparison.Ordinal) ? "U  unready" : "U  ready up")
                : string.Empty;
        }

        // The hub checks the laptop's range from the pawn, not the camera.
        FVector feet = client.LocalPlayer?.Pawn is { } pawn ? pawn.Location : at;
        if (play.Nearest("laptop", feet, out float ld) is { } laptop && ld <= HubPads.LaptopUseRange)
        {
            return $"U  set the match of Pad {laptop.Id[^1]} (laptop)";
        }

        if (play.Nearest("jukebox", at, out float jd) is not null && jd <= HubPlayK.JukeboxUseRange)
        {
            return $"U  next record   (playing \"{play.TrackTitle}\")";
        }

        if (!play.InRound && play.Nearest("firingline", at, out _) is { } line &&
            at.X >= line.At.X - HubPlayK.FiringLineSlack && at.X <= line.At.X + 320f && System.Math.Abs(at.Y - line.At.Y) <= line.Radius)
        {
            return client.Team == 1 ? $"U  start a {HubPlayK.RoundSeconds:0} s round at the aim range" : "The aim range is for mercs (K to play merc)";
        }

        if (play.InRound || play.Running)
        {
            return "X  stop";
        }

        if (play.Nearest("start", at, out float sd) is { } start && sd <= start.Radius + 256f)
        {
            return client.Team == 0 ? "Spy course: step out of the green gate to start the clock" : "The movement course is for spies (J to play spy)";
        }

        return string.Empty;
    }

    private void OpenEmoteMenu()
    {
        _emoteOpen = true;
        if (Round?.Controls is { } input)
        {
            input.Enabled = false;
        }
    }

    private void CloseEmoteMenu()
    {
        _emoteOpen = false;
        if (Round?.Controls is { } input && !Typing)
        {
            input.Enabled = true;
        }
    }

    /// <summary>Keys while the emote menu is open: 1 to 8 play one, Escape or G closes. True when the key was the menu's.</summary>
    private bool EmoteMenuInput(InputEventKey key)
    {
        if (!_emoteOpen)
        {
            return false;
        }

        int index = key.Keycode switch
        {
            >= Key.Key1 and <= Key.Key9 => (int)(key.Keycode - Key.Key1),
            >= Key.Kp1 and <= Key.Kp9 => (int)(key.Keycode - Key.Kp1),
            _ => -1,
        };
        if (index >= 0 && index < HubEmotes.All.Count)
        {
            Client?.Emote(HubEmotes.All[index]);
            CloseEmoteMenu();
            return true;
        }

        if (key.Keycode is Key.Escape or Key.G)
        {
            CloseEmoteMenu();
            return true;
        }

        return false;
    }
}
