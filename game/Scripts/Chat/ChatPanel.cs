using System;
using System.Collections.Generic;
using Godot;
using OpenScct.Net;
using OpenScctGame.Hud;
using OpenScctGame.UI;

namespace OpenScctGame.Chat;

/// <summary>The shared match/hub chat surface. Transport, shortcuts and gameplay focus belong to ChatInput.</summary>
public partial class ChatPanel : Control
{
    public const int PassiveLines = 6;
    private ChatTranscript _transcript = new();
    private readonly PanelContainer _expandedPanel = new() { Name = "Conversation", Visible = false };
    private readonly Panel _passivePanel = new() { Name = "Recent", ClipContents = true, MouseFilter = MouseFilterEnum.Ignore };
    private readonly Control _rows = new() { Name = "Messages", MouseFilter = MouseFilterEnum.Pass };
    private readonly List<(ChatEntry Entry, RichTextLabel Label)> _history = [];
    private readonly List<(ChatEntry Entry, RichTextLabel Label)> _recent = [];
    private readonly Dictionary<long, (float Height, float[] Offsets)> _recentMetrics = [];
    private readonly Button _channelButton = new() { Name = "Channel", FocusMode = FocusModeEnum.None };
    private readonly Label _hint = new() { Text = "Enter send   Tab channel   Esc close", MouseFilter = MouseFilterEnum.Ignore };
    private readonly Label _count = new() { HorizontalAlignment = HorizontalAlignment.Right, MouseFilter = MouseFilterEnum.Ignore };
    private readonly Label _title = new() { Text = "CHAT", SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };
    private ChatChannel _channel;
    private bool _allowNearby, _expanded, _ready, _followLatest = true;
    private Vector2 _viewport;
    private float _scale = 1f, _width, _historyWidth;
    private bool _wasHubLayout;
    private HudSide _wasSide;
    private int _layoutFrames;
    private long _anchorId;
    private float _anchorOffset;

    public ChatPanel()
    {
        Name = "ModernChat";
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public ChatTranscript Transcript
    {
        get => _transcript;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (ReferenceEquals(_transcript, value)) return;
            if (_ready) _transcript.Changed -= OnMessagesChanged;
            _transcript = value;
            if (_ready)
            {
                _transcript.Changed += OnMessagesChanged;
                _followLatest = true;
                ClearRows();
                RebuildMessages();
            }
        }
    }

    public Func<double> Clock { get; set; } = () => Time.GetTicksMsec() / 1000d;
    public bool HubLayout { get; set; }
    public HudSide Side { get; set; } = HudSide.Spy;
    public bool AllowNearby
    {
        get => _allowNearby;
        set { _allowNearby = value; if (!value && Channel == ChatChannel.Nearby) Channel = ChatChannel.All; }
    }
    public ChatChannel Channel
    {
        get => _channel;
        set
        {
            _channel = value == ChatChannel.System || value == ChatChannel.Nearby && !AllowNearby ? ChatChannel.All : value;
            UpdateChannel();
        }
    }
    public bool Expanded { get => _expanded; set => SetExpanded(value); }
    public string Draft { get => Entry.Text; set { Entry.Text = value; DraftChanged(); } }
    public LineEdit Entry { get; } = new()
    {
        Name = "ChatEntry", MaxLength = ChatK.MaxChars, SizeFlagsHorizontal = SizeFlags.ExpandFill,
        PlaceholderText = "Message everyone…", ContextMenuEnabled = false,
    };
    public event Action<string>? Submitted;
    internal ScrollContainer HistoryScroll { get; } = new()
    {
        Name = "History", HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        VerticalScrollMode = ScrollContainer.ScrollMode.ShowAlways, SizeFlagsVertical = SizeFlags.ExpandFill,
        SizeFlagsHorizontal = SizeFlags.ExpandFill, FollowFocus = false,
    };
    internal Button NewMessagesButton { get; } = new()
    {
        Name = "NewMessages", Text = "New messages ↓", Visible = false, FocusMode = FocusModeEnum.None,
    };
    internal int PassiveLineCount { get; private set; }

    public override void _Ready()
    {
        Theme = UiStyle.Theme;
        _expandedPanel.AddThemeStyleboxOverride("panel", UiArt.PanelBox());
        _passivePanel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.012f, 0.024f, 0.018f, 0.72f),
            BorderColor = UiArt.Accent with { A = 0.38f }, BorderWidthLeft = 2,
        });
        AddChild(_passivePanel);
        AddChild(_expandedPanel);
        var column = new VBoxContainer { Name = "Column" };
        column.AddThemeConstantOverride("separation", 6);
        _expandedPanel.AddChild(column);
        var header = new HBoxContainer();
        header.AddChild(_title);
        header.AddChild(_count);
        column.AddChild(header);
        HistoryScroll.AddChild(_rows);
        column.AddChild(HistoryScroll);
        NewMessagesButton.Pressed += JumpToLatest;
        column.AddChild(NewMessagesButton);
        var composer = new HBoxContainer();
        _channelButton.Pressed += () => { CycleChannel(); Entry.GrabFocus(); };
        composer.AddChild(_channelButton);
        Entry.AddThemeStyleboxOverride("normal", UiArt.FieldBox(false));
        Entry.AddThemeStyleboxOverride("focus", UiArt.FieldBox(true));
        Entry.TextSubmitted += text => Submitted?.Invoke(text);
        Entry.TextChanged += _ => DraftChanged();
        composer.AddChild(Entry);
        column.AddChild(composer);
        column.AddChild(_hint);
        _ready = true;
        _transcript.Changed += OnMessagesChanged;
        UpdateChannel();
        UpdateCounter();
        ResizeSurface();
        RebuildMessages();
        _expandedPanel.Visible = Expanded;
        if (Expanded) Entry.GrabFocus();
    }

    public override void _ExitTree()
    {
        _transcript.Changed -= OnMessagesChanged;
        _ready = false;
    }

    public void SetExpanded(bool expanded)
    {
        if (_expanded == expanded) return;
        _expanded = expanded;
        if (!_ready) return;
        _expandedPanel.Visible = expanded;
        _passivePanel.Visible = !expanded && PassiveLineCount > 0;
        if (expanded)
        {
            _followLatest = true;
            NewMessagesButton.Hide();
            _layoutFrames = 3;
            Entry.GrabFocus();
            Entry.CaretColumn = Entry.Text.Length;
        }
        else Entry.ReleaseFocus();
    }

    public void CycleChannel() => Channel = Channel switch
    {
        ChatChannel.All => ChatChannel.Team,
        ChatChannel.Team when AllowNearby => ChatChannel.Nearby,
        _ => ChatChannel.All,
    };

    private void UpdateChannel()
    {
        _channelButton.Text = Channel.ToString().ToUpperInvariant();
        _channelButton.Modulate = ChannelColour(Channel);
        Entry.PlaceholderText = Channel switch
        {
            ChatChannel.Team => "Message your team…",
            ChatChannel.Nearby => "Message nearby players…",
            _ => "Message everyone…",
        };
    }

    private void UpdateCounter() => _count.Text = $"{Entry.Text.Length}/{ChatK.MaxChars}";

    private void DraftChanged()
    {
        // Godot counts Unicode code points, while the existing relay limits UTF-16 units.
        // Match that payload budget without splitting a surrogate pair at the boundary.
        if (Entry.Text.Length > ChatK.MaxChars)
        {
            int caret = Entry.CaretColumn;
            int length = ChatK.MaxChars;
            if (char.IsHighSurrogate(Entry.Text[length - 1])) length--;
            Entry.Text = Entry.Text[..length];
            Entry.CaretColumn = caret;
        }
        UpdateCounter();
    }

    public override void _Process(double delta)
    {
        if (_viewport != GetViewportRect().Size || _wasHubLayout != HubLayout || _wasSide != Side) ResizeSurface();
        if (Expanded)
        {
            if (_layoutFrames > 0)
            {
                LayoutHistory();
                RestoreScroll();
                _layoutFrames--;
            }
            else
            {
                _followLatest = AtLatest();
                if (_followLatest) NewMessagesButton.Hide();
            }
        }
        if (!Expanded) LayoutRecent();
    }

    private bool AtLatest()
    {
        VScrollBar bar = HistoryScroll.GetVScrollBar();
        return bar.Value >= bar.MaxValue - bar.Page - 3f * _scale;
    }

    private void RememberScroll()
    {
        if (_layoutFrames > 0) return;
        _followLatest = AtLatest();
        _anchorId = 0;
        foreach ((ChatEntry entry, RichTextLabel label) in _history)
        {
            if (label.Position.Y + label.Size.Y <= HistoryScroll.ScrollVertical) continue;
            _anchorId = entry.Id;
            _anchorOffset = HistoryScroll.ScrollVertical - label.Position.Y;
            break;
        }
    }

    private void RestoreScroll()
    {
        if (_followLatest)
        {
            HistoryScroll.ScrollVertical = (int)Math.Max(0d, HistoryScroll.GetVScrollBar().MaxValue - HistoryScroll.GetVScrollBar().Page);
            return;
        }
        foreach ((ChatEntry entry, RichTextLabel label) in _history)
        {
            if (entry.Id != _anchorId) continue;
            HistoryScroll.ScrollVertical = (int)Math.Max(0f, label.Position.Y + _anchorOffset);
            return;
        }
        // The viewed oldest message may have left the bounded transcript.
        HistoryScroll.ScrollVertical = 0;
    }

    private void JumpToLatest()
    {
        _followLatest = true;
        NewMessagesButton.Hide();
        _layoutFrames = 3;
        Entry.GrabFocus();
    }

    private void OnMessagesChanged()
    {
        RememberScroll();
        if (Expanded && !_followLatest) NewMessagesButton.Show();
        RebuildMessages();
    }

    private void RebuildMessages()
    {
        long first = Transcript.Entries.Count > 0 ? Transcript.Entries[0].Id : long.MaxValue;
        while (_history.Count > 0 && _history[0].Entry.Id < first)
        {
            RichTextLabel label = _history[0].Label;
            _rows.RemoveChild(label);
            label.QueueFree();
            _history.RemoveAt(0);
        }
        long last = _history.Count > 0 ? _history[^1].Entry.Id : 0;
        foreach (ChatEntry entry in Transcript.Entries)
        {
            if (entry.Id <= last)
            {
                RefreshEntry(_history, entry);
                continue;
            }
            RichTextLabel row = CreateRow(entry, selectable: true);
            _rows.AddChild(row);
            _history.Add((entry, row));
        }
        int recentStart = Math.Max(0, Transcript.Entries.Count - PassiveLines);
        long firstRecent = Transcript.Entries.Count > 0 ? Transcript.Entries[recentStart].Id : long.MaxValue;
        while (_recent.Count > 0 && _recent[0].Entry.Id < firstRecent)
        {
            (ChatEntry entry, RichTextLabel label) = _recent[0];
            _passivePanel.RemoveChild(label);
            label.QueueFree();
            _recentMetrics.Remove(entry.Id);
            _recent.RemoveAt(0);
        }
        long lastRecent = _recent.Count > 0 ? _recent[^1].Entry.Id : 0;
        for (int i = Math.Max(0, Transcript.Entries.Count - PassiveLines); i < Transcript.Entries.Count; i++)
        {
            ChatEntry entry = Transcript.Entries[i];
            if (entry.Id <= lastRecent)
            {
                if (RefreshEntry(_recent, entry)) _recentMetrics.Remove(entry.Id);
                continue;
            }
            RichTextLabel row = CreateRow(entry, selectable: false);
            _passivePanel.AddChild(row);
            _recent.Add((entry, row));
        }
        _layoutFrames = 3;
        LayoutHistory();
        LayoutRecent();
    }

    private void ClearRows()
    {
        foreach ((_, RichTextLabel label) in _history) { _rows.RemoveChild(label); label.QueueFree(); }
        foreach ((_, RichTextLabel label) in _recent) { _passivePanel.RemoveChild(label); label.QueueFree(); }
        _history.Clear();
        _recent.Clear();
        _recentMetrics.Clear();
    }

    private static bool RefreshEntry(List<(ChatEntry Entry, RichTextLabel Label)> rows, ChatEntry entry)
    {
        int index = rows.FindIndex(row => row.Entry.Id == entry.Id);
        if (index < 0 || ReferenceEquals(rows[index].Entry, entry)) return false;
        RichTextLabel label = rows[index].Label;
        WriteRow(label, entry);
        rows[index] = (entry, label);
        return true;
    }

    private RichTextLabel CreateRow(ChatEntry entry, bool selectable)
    {
        var row = new RichTextLabel
        {
            Name = "Message" + entry.Id, BbcodeEnabled = false, ScrollActive = false, FitContent = false,
            AutowrapMode = TextServer.AutowrapMode.WordSmart, SelectionEnabled = selectable,
            MouseFilter = selectable ? MouseFilterEnum.Pass : MouseFilterEnum.Ignore,
        };
        row.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
        row.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        ApplyRowFont(row);
        WriteRow(row, entry);
        return row;
    }

    private static void WriteRow(RichTextLabel row, ChatEntry entry)
    {
        row.Clear();
        row.PushColor(ChannelColour(entry.Channel));
        row.AddText("[" + entry.Channel.ToString().ToUpperInvariant() + "] ");
        row.Pop();
        if (entry.SenderName.Length > 0 && entry.Channel != ChatChannel.System)
        {
            row.PushColor(entry.SenderTeam switch { 0 => HudK.SpyColour, 1 => HudK.MercColour, _ => UiArt.Text });
            row.AddText(entry.SenderName + ": ");
            row.Pop();
        }
        row.PushColor(entry.Channel == ChatChannel.System ? UiArt.TextDim : UiArt.Text);
        row.AddText(entry.Text);
        row.Pop();
    }

    private static Color ChannelColour(ChatChannel channel) => channel switch
    {
        ChatChannel.Team => UiArt.Accent,
        ChatChannel.Nearby => new Color(0.55f, 0.8f, 1f),
        ChatChannel.System => UiArt.TextDim,
        _ => new Color(0.69f, 0.78f, 0.72f),
    };

    private void ApplyRowFont(RichTextLabel row)
    {
        if (UiArt.BodyFont is { } font) row.AddThemeFontOverride("normal_font", font);
        row.AddThemeFontSizeOverride("normal_font_size", Mathf.RoundToInt(16f * _scale));
        row.AddThemeConstantOverride("line_separation", Mathf.RoundToInt(2f * _scale));
    }

    private void ResizeSurface()
    {
        if (!_ready) return;
        RememberScroll();
        _viewport = GetViewportRect().Size;
        _wasHubLayout = HubLayout;
        _wasSide = Side;
        _scale = Math.Clamp(_viewport.Y / 720f, 0.75f, 3f);
        _width = Math.Min(440f * _scale, _viewport.X * 0.46f);
        float left = 16f * _scale;
        float bottom = Bottom();
        float height = Math.Min(310f * _scale, bottom - 16f * _scale);
        _expandedPanel.Position = new Vector2(left, bottom - height);
        _expandedPanel.Size = new Vector2(_width, height);
        _passivePanel.Position = new Vector2(left, bottom);
        _passivePanel.Size = new Vector2(_width, 0f);
        foreach (Control control in new Control[] { Entry, _channelButton, NewMessagesButton })
            control.AddThemeFontSizeOverride("font_size", Mathf.RoundToInt(16f * _scale));
        foreach (Label caption in new[] { _hint, _count, _title })
        {
            if (UiArt.CaptionFont is { } font) caption.AddThemeFontOverride("font", font);
            caption.AddThemeFontSizeOverride("font_size", Mathf.RoundToInt(11f * _scale));
            caption.Modulate = UiArt.TextDim;
        }
        _hint.ClipText = true;
        foreach ((_, RichTextLabel row) in _history) ApplyRowFont(row);
        foreach ((_, RichTextLabel row) in _recent) ApplyRowFont(row);
        _layoutFrames = 3;
        _recentMetrics.Clear();
        LayoutHistory();
    }

    // The spy's coop icons occupy the bottom-left; merc gauges end above the bottom gutter.
    private float Bottom() => _viewport.Y - (HubLayout ? 20f : Side == HudSide.Merc ? 16f : 120f) * _scale;

    private void LayoutHistory()
    {
        _historyWidth = Math.Max(40f, HistoryScroll.Size.X - HistoryScroll.GetVScrollBar().Size.X - 6f * _scale);
        float y = 0f;
        foreach ((_, RichTextLabel row) in _history)
        {
            row.Size = new Vector2(_historyWidth, 1f);
            float height = Math.Max(20f * _scale, row.GetContentHeight());
            row.Position = new Vector2(0f, y);
            row.Size = new Vector2(_historyWidth, height);
            y += height + 5f * _scale;
        }
        _rows.CustomMinimumSize = new Vector2(0f, y);
        _rows.Size = new Vector2(_historyWidth, y);
    }

    private void LayoutRecent()
    {
        double now = Clock();
        int linesLeft = PassiveLines;
        float height = 0f, opacity = 0f;
        // The clip rectangle trims only the oldest partial row, keeping precisely six wrapped lines.
        for (int i = _recent.Count - 1; i >= 0; i--)
        {
            (ChatEntry entry, RichTextLabel row) = _recent[i];
            float alpha = entry.Opacity(now);
            row.Visible = alpha > 0f && linesLeft > 0;
            if (!row.Visible) continue;
            row.Modulate = Colors.White with { A = alpha };
            if (!_recentMetrics.TryGetValue(entry.Id, out var metrics))
            {
                row.Size = new Vector2(_width - 20f * _scale, 1f);
                int count = Math.Max(1, row.GetLineCount());
                float[] offsets = new float[count];
                for (int line = 0; line < count; line++) offsets[line] = row.GetLineOffset(line);
                metrics = (Math.Max(20f * _scale, row.GetContentHeight()), offsets);
                _recentMetrics.Add(entry.Id, metrics);
                row.Size = new Vector2(_width - 20f * _scale, metrics.Height);
            }
            int lines = metrics.Offsets.Length;
            int shown = Math.Min(lines, linesLeft);
            float fullHeight = metrics.Height;
            float clipped = lines > shown ? metrics.Offsets[lines - shown] : 0f;
            row.Position = new Vector2(10f * _scale, -height - fullHeight);
            height += fullHeight - clipped;
            linesLeft -= shown;
            opacity = Math.Max(opacity, alpha);
        }
        PassiveLineCount = PassiveLines - linesLeft;
        // Move rows from bottom-relative coordinates into the sized clip rectangle.
        foreach ((_, RichTextLabel row) in _recent)
            if (row.Visible) row.Position += new Vector2(0f, height);
        float bottom = Bottom();
        _passivePanel.Position = new Vector2(16f * _scale, bottom - height);
        _passivePanel.Size = new Vector2(_width, height);
        _passivePanel.Visible = !Expanded && PassiveLineCount > 0;
        // Fade the backing as the last message expires, without multiplying each message's alpha.
        if (_passivePanel.GetThemeStylebox("panel") is StyleBoxFlat style)
        {
            style.BgColor = new Color(0.012f, 0.024f, 0.018f, 0.72f * opacity);
            style.BorderColor = UiArt.Accent with { A = 0.38f * opacity };
        }
    }
}
