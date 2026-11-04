using System;
using System.Linq;
using Godot;
using OpenScct.Net.Scoreboards;
using OpenScct.Server;
using OpenScctGame.Controls;
using OpenScctGame.UI;

namespace OpenScctGame.Hud;

/// <summary>A passive scoreboard: opening it never changes pointer capture or gameplay input.</summary>
public partial class ScoreboardOverlay : Control
{
    public ClientSession? Session { get; set; }
    public HudSnapshot? Fallback { get; set; }
    public string Map { get; set; } = "";
    public ScoreboardInputState State { get; } = new();
    public bool IsOpen => State.Visible;
    public RoundScoreboardView? View { get; private set; }
    private RoundScoreboard? _shown;
    private string? _round;
    private double _fallbackAt;
    private int _local = -1;
    private PanelContainer? _panel;

    public override void _Ready()
    {
        Name = "ScoreboardOverlay";
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        FocusMode = FocusModeEnum.None;
        Visible = false;
        var dim = new ColorRect { Color = new Color(0, 0, 0, .35f), MouseFilter = MouseFilterEnum.Ignore };
        dim.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(dim);
        _panel = new PanelContainer { Name = "ScoreboardPanel", MouseFilter = MouseFilterEnum.Ignore };
        _panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(.025f, .035f, .045f, .96f), BorderColor = new Color(.25f, .35f, .4f, .7f),
            BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1, BorderWidthBottom = 1,
            ContentMarginLeft = 12, ContentMarginRight = 12, ContentMarginTop = 10, ContentMarginBottom = 10,
        });
        AddChild(_panel);
        View = new RoundScoreboardView();
        _panel.AddChild(View);
        Present();
    }

    public void Toggle() { State.Toggle(); Present(); }
    public void Hold(string source, bool pressed) { if (State.Hold(source, pressed)) Present(); }
    public void Clear() { State.Clear(); Present(); }

    public override void _Process(double delta)
    {
        if (Session?.Scoreboard is { } current && !string.Equals(_round, current.RoundId, StringComparison.Ordinal))
        {
            if (_round is not null) Clear();
            _round = current.RoundId;
        }
        if (!Visible || View is null) return;
        ArrangePanel();
        _fallbackAt -= delta;
        var board = Session?.Scoreboard;
        int local = Session?.ScoreboardLocalParticipantId ?? -1;
        if (board is null && _fallbackAt <= 0)
        {
            board = LegacyBoard();
            local = Fallback?.Score.FindIndex(p => p.Local) ?? -1;
            _fallbackAt = 1;
        }
        if (board is not null && (!ReferenceEquals(_shown, board) || _local != local))
        {
            _shown = board;
            _local = local;
            View.Scoreboard = board;
            View.LocalParticipantId = local;
            View.Refresh();
            ArrangePanel();
        }
    }

    private void ArrangePanel()
    {
        if (_panel is null || View is null) return;
        // Use a readable desktop-sized card instead of stretching the columns across the viewport.
        // Scale that card on high-resolution displays; smaller screens use the existing dense layout.
        float scale = Math.Max(1, Size.Y / 1080f);
        float preferredWidth = Math.Clamp(Size.X * .67f / scale, 960, 1200);
        float width = Math.Min(preferredWidth, Math.Max(0, Size.X - 32) / scale);
        View.LiveHeight = Math.Max(0, (Size.Y - 32) / scale - 20);
        _panel.Scale = Vector2.One * scale;
        _panel.Size = new Vector2(width, _panel.GetCombinedMinimumSize().Y);
        _panel.Position = (Size - _panel.Size * scale) / 2;
    }

    private void Present()
    {
        Visible = State.Visible;
        if (Visible) { _shown = null; _fallbackAt = 0; _Process(0); }
    }

    private RoundScoreboard? LegacyBoard() => Fallback is not { } source ? null : new RoundScoreboard
    {
        SchemaVersion = 0, RoundId = "legacy", Map = Map,
        Rows = source.Score.Select((row, index) => new ScoreboardPlayer
        {
            ParticipantId = index, Name = row.Name, Team = row.Spectator ? 255 : row.Team,
            Kills = row.Kills, Deaths = row.Deaths, Objectives = source.DeathMatch ? 0 : row.ObjOrKills, Lives = row.Lives, Rank = row.Rank,
            PingMs = row.Ping * HudK.PingUnit, StatsVersion = 0,
        }).ToArray(),
    };
}
