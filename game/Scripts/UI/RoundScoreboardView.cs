using System;
using System.Linq;
using Godot;
using OpenScct.Net.Cosmetics;
using OpenScct.Net.Scoreboards;
using OpenScctGame.Hud;

namespace OpenScctGame.UI;

/// <summary>Role-specific round tables shared by the live overlay, results, lobby and match library.</summary>
public partial class RoundScoreboardView : VBoxContainer
{
    private RoundScoreboard? _scoreboard;
    private long _snapshotReceived;
    private Label? _timing;
    public RoundScoreboard? Scoreboard
    {
        get => _scoreboard;
        set
        {
            if (ReferenceEquals(_scoreboard, value)) return;
            _scoreboard = value;
            _snapshotReceived = System.Diagnostics.Stopwatch.GetTimestamp();
        }
    }
    private double SnapshotAge => System.Diagnostics.Stopwatch.GetElapsedTime(_snapshotReceived).TotalSeconds;
    public int LocalParticipantId { get; set; } = -1;
    public bool Detailed { get; set; }
    /// <summary>Available screen height for a passive live board; menus use their normal scroll container.</summary>
    public float LiveHeight { get; set; } = 900;
    private bool _compact;
    private int _selected = -1;
    private VBoxContainer? _details;

    public override void _Ready()
    {
        Name = "RoundScoreboard";
        Theme = UiStyle.MenuTheme;
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        MouseFilter = MouseFilterEnum.Ignore;
        AddThemeConstantOverride("separation", 8);
        Refresh();
    }

    public override void _Process(double delta)
    {
        bool compact = Size.X < 1000 || !Detailed && LiveHeight < 700;
        if (_compact != compact) { _compact = compact; Refresh(); }
        if (_timing is not null && Scoreboard is { } board)
            _timing.Text = RoundScoreboardModel.Timing(board, SnapshotAge);
    }

    public void Refresh()
    {
        if (!IsInsideTree()) return;
        CareerPanels.Clear(this);
        _timing = null;
        _details = null;
        AddThemeConstantOverride("separation", !Detailed && _compact ? 4 : 8);
        if (Scoreboard is not { } board)
        {
            Text(this, "Waiting", "Waiting for scoreboard…", UiStyle.Dim, 16);
            return;
        }
        int font = _compact ? 12 : 15;
        var heading = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        AddChild(heading);
        string subtitle = (board.Practice ? " · Practice" : "") + (board.Partial ? " · Partial" : "");
        Text(heading, "Map", (string.IsNullOrWhiteSpace(board.Map) ? "Scoreboard" : board.Map) + subtitle, UiStyle.Text, font + 5, 2);
        Text(heading, "Time", RoundScoreboardModel.Clock(board.ElapsedSeconds), UiStyle.Dim, font, 1, HorizontalAlignment.Right);
        _timing = Text(this, "Timing", RoundScoreboardModel.Timing(board, SnapshotAge), UiStyle.Dim, font);
        int first = RoundScoreboardModel.FirstTeam(board, LocalParticipantId);
        var tableBoard = Detailed ? board : board with { Rows = RoundScoreboardModel.LiveRows(board) };
        TeamTable(tableBoard, first, font);
        TeamTable(tableBoard, 1 - first, font);
        int omitted = board.Rows.Count(p => p.Team is 0 or 1) - tableBoard.Rows.Count(p => p.Team is 0 or 1);
        if (omitted > 0) Text(this, "EarlierParticipants", $"{omitted} earlier participants are included in final details", UiStyle.Dim, font);
        var spectators = board.Rows.Where(p => p.Team is not (0 or 1)).ToArray();
        if (spectators.Length > 0)
            Text(this, "Spectators", "Spectators: " + string.Join(", ", spectators.Select(p => p.Name)), UiStyle.Dim, font);
        if (board.Rows.Count == 0) Text(this, "Empty", "No player scores were recorded.", UiStyle.Dim, font);
        if (!Detailed) return;

        Text(this, "BreakdownTitle", "Player breakdown", UiStyle.Text, 18);
        var chooser = new OptionButton { Name = "ScoreboardPlayer", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var players = board.Rows.Where(p => p.Team is 0 or 1).OrderBy(p => p.Team == first ? 0 : 1).ThenBy(p => p.ParticipantId).ToArray();
        if (!players.Any(p => p.ParticipantId == _selected))
            _selected = players.FirstOrDefault(p => p.ParticipantId == LocalParticipantId)?.ParticipantId ?? players.FirstOrDefault()?.ParticipantId ?? -1;
        foreach (ScoreboardPlayer player in players)
        {
            chooser.AddItem($"{player.Name} · {(player.Team == 0 ? "Spy" : "Mercenary")}{(player.LeftEarly ? " · Left early" : "")}", player.ParticipantId);
            if (player.ParticipantId == _selected) chooser.Selected = chooser.ItemCount - 1;
        }
        chooser.Disabled = players.Length == 0;
        chooser.ItemSelected += index => { _selected = chooser.GetItemId((int)index); RenderDetails(); };
        AddChild(chooser);
        _details = new VBoxContainer { Name = "Details", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        AddChild(_details);
        RenderDetails();
        Text(this, "CountingNote", "Categories overlap: a headshot can also be a weapon kill. Neutralisations count completed knockouts and enemy kills; stuns and flashes are separate. — means not recorded.", UiStyle.Dim, 13).AutowrapMode = TextServer.AutowrapMode.WordSmart;
    }

    private void TeamTable(RoundScoreboard board, int team, int font)
    {
        var players = RoundScoreboardModel.Team(board, team);
        Color color = team == 0 ? HudK.SpyColour : HudK.MercColour;
        Text(this, team == 0 ? "SpyHeading" : "MercHeading", $"{(team == 0 ? "SPIES" : "MERCENARIES")}  ·  {players.Count}", color, font + 1);
        var table = new VBoxContainer { Name = team == 0 ? "Spies" : "Mercenaries", MouseFilter = MouseFilterEnum.Ignore };
        table.AddThemeConstantOverride("separation", !Detailed && _compact ? 0 : 1);
        AddChild(table);
        var columns = RoundScoreboardModel.Columns(team);
        float rankWidth = GetThemeFont("font", "Label").GetStringSize("Master · 55", fontSize: font).X + font + 10;
        var headerMargin = new MarginContainer { MouseFilter = MouseFilterEnum.Ignore };
        headerMargin.AddThemeConstantOverride("margin_left", 3);
        headerMargin.AddThemeConstantOverride("margin_right", 3);
        table.AddChild(headerMargin);
        var header = Row(headerMargin);
        var rankHeading = Text(header, "Rank", "Rank", UiStyle.Dim, font);
        rankHeading.SizeFlagsHorizontal = SizeFlags.Fill;
        rankHeading.CustomMinimumSize = new Vector2(rankWidth, 0);
        Text(header, "Name", "Name", UiStyle.Dim, font, 2.8f);
        foreach (var column in columns)
            StatCell(header, column, column.Label, font, heading: true);
        foreach (ScoreboardPlayer player in players)
        {
            var panel = new PanelContainer { Name = "Player" + player.ParticipantId, MouseFilter = MouseFilterEnum.Ignore };
            var style = new StyleBoxFlat
            {
                BgColor = player.ParticipantId == LocalParticipantId ? new Color(color.R, color.G, color.B, .16f) : new Color(1, 1, 1, .025f),
                ContentMarginLeft = 3, ContentMarginRight = 3,
                ContentMarginTop = !Detailed && _compact ? 0 : 1, ContentMarginBottom = !Detailed && _compact ? 0 : 1,
            };
            panel.AddThemeStyleboxOverride("panel", style);
            table.AddChild(panel);
            var row = Row(panel);
            var rankCell = new HBoxContainer { Name = "Rank", CustomMinimumSize = new Vector2(rankWidth, 0), MouseFilter = MouseFilterEnum.Ignore };
            row.AddChild(rankCell);
            PlayerRank rank = PlayerRank.Unpack(player.Rank);
            if (!rank.IsNone)
                rankCell.AddChild(new RankBadge(font + 2, font) { MouseFilter = MouseFilterEnum.Ignore, TooltipText = RankBadge.Describe(rank) }.Show(rank));
            else Text(rankCell, "Unavailable", "—", UiStyle.Dim, font);
            string flags = player.LeftEarly ? " · left" : player.Bot ? " · bot" : "";
            var label = Text(row, "Name", player.Name + flags, player.ParticipantId == LocalParticipantId ? color : UiStyle.Text, font, 2.8f);
            label.TooltipText = player.Name + (rank.IsNone ? "" : " · " + RankBadge.Describe(rank)) + (player.LeftEarly ? " · Left early" : "");
            foreach (var column in columns)
                StatCell(row, column, column.Read(board, player), font);
        }
    }

    private void StatCell(Container row, RoundScoreboardModel.Column column, string value, int font, bool heading = false)
    {
        if (!column.Connection)
        {
            if (heading && _compact && value == "Cam sleep") value = "Cam KO";
            Text(row, heading ? "Heading" : "Value", value, heading ? UiStyle.Dim : UiStyle.Text, font, 1,
                HorizontalAlignment.Center).TooltipText = column.Description;
            return;
        }
        var ping = new PanelContainer
        {
            Name = "Ping", CustomMinimumSize = new Vector2(_compact ? 46 : 60, 0), MouseFilter = MouseFilterEnum.Ignore,
            TooltipText = column.Description,
        };
        ping.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(.10f, .18f, .24f, heading ? .8f : .55f),
            BorderColor = new Color(.32f, .48f, .58f, .7f), BorderWidthLeft = 1,
            ContentMarginLeft = 5, ContentMarginRight = 5, ContentMarginTop = 0, ContentMarginBottom = 0,
        });
        row.AddChild(ping);
        Text(ping, "Value", value, heading ? new Color(.65f, .79f, .88f) : new Color(.53f, .69f, .78f), font, 1,
            HorizontalAlignment.Right).TooltipText = column.Description;
    }

    private void RenderDetails()
    {
        if (_details is null || Scoreboard is not { } board) return;
        CareerPanels.Clear(_details);
        if (board.Rows.FirstOrDefault(p => p.ParticipantId == _selected) is not { } player) return;
        var grid = new GridContainer { Name = "StatGroups", Columns = _compact ? 1 : 2, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        grid.AddThemeConstantOverride("h_separation", 12);
        grid.AddThemeConstantOverride("v_separation", 10);
        _details.AddChild(grid);
        foreach (var group in RoundScoreboardModel.Details(board, player).GroupBy(d => d.Group))
        {
            var card = CareerPanels.Card(grid, "Group", group.Key);
            foreach (var detail in group) CareerPanels.StatRow(card, detail.Label, detail.Value);
        }
    }

    private static HBoxContainer Row(Container parent)
    {
        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        row.AddThemeConstantOverride("separation", 3);
        parent.AddChild(row);
        return row;
    }

    private static Label Text(Container parent, string name, string text, Color color, int font, float stretch = 1, HorizontalAlignment alignment = HorizontalAlignment.Left)
    {
        var label = new Label
        {
            Name = name, Text = text, Modulate = color, MouseFilter = MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsStretchRatio = stretch,
            HorizontalAlignment = alignment, VerticalAlignment = VerticalAlignment.Center,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
        };
        label.AddThemeFontSizeOverride("font_size", font);
        parent.AddChild(label);
        return label;
    }
}
