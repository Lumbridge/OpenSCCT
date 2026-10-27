using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Godot;
using OpenScct.Net.Cosmetics;
using OpenScct.Provenance;
using OpenScctGame.Controls;
using OpenScctGame.UI;

namespace OpenScctGame.Hud;

/// <summary>
/// "Mission Status" (spec/hud/scoreboard.md rules 3 to 5): the in-match page the DisplaySessionStatus key (F8) opens
/// over the game, with the spies (objectives or kills, lives), the mercenaries (kills, lives) and the objective list,
/// refreshed once a second from the HUD snapshot. Escape, Enter or the same key close it; while it is open the
/// player's controls are stopped.
/// </summary>
public partial class GameStatusPage : Control
{
    [Provenance(ProvenanceKind.Decoded, "GUI.View_GameStatus.InitComponent: SetTimer(1.0, true)")]
    public const double RefreshSeconds = 1.0;

    private GridContainer? _spies;
    private GridContainer? _mercs;
    private Label? _objectives;
    private Label? _spyKills;
    private double _sinceRefresh;
    private bool _inputWasEnabled = true;

    public GameStatusPage()
    {
        Name = "GameStatus";
    }

    /// <summary>Where the page reads the score (the HUD's snapshot).</summary>
    public HudSnapshot? Source { get; set; }

    /// <summary>The play input to stop while the page is open.</summary>
    public PlayInput? PlayInput { get; set; }

    /// <summary>The key that opened the page (IK name); it closes it too.</summary>
    public string? ExitKey { get; set; }

    public bool IsOpen => Visible;

    public GridContainer? SpyList => _spies;

    public GridContainer? MercList => _mercs;

    public Label? ObjectiveList => _objectives;

    public override void _Ready()
    {
        Theme = UiStyle.Theme;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        Visible = false;
        var dim = new ColorRect { Name = "Dim", Color = new Color(0f, 0f, 0f, 0.6f), MouseFilter = MouseFilterEnum.Ignore };
        dim.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(dim);
        VBoxContainer column = UiStyle.CentreColumn(this, 860f);
        column.AddChild(UiStyle.Title("Mission Status", HudK.SpyColour, 32));
        var teams = new HBoxContainer { Name = "Teams", Alignment = BoxContainer.AlignmentMode.Center };
        teams.AddThemeConstantOverride("separation", 40);
        (VBoxContainer spyBox, _spies, _spyKills) = TeamList("Spies", "Spy", "Objectives", HudK.SpyColour);
        (VBoxContainer mercBox, _mercs, _) = TeamList("Mercenaries", "Mercenary", "Kills", HudK.MercColour);
        teams.AddChild(spyBox);
        teams.AddChild(mercBox);
        column.AddChild(teams);
        _objectives = new Label { Name = "Objectives", AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(820f, 0f) };
        column.AddChild(_objectives);
    }

    private static (VBoxContainer Box, GridContainer Grid, Label Second) TeamList(string title, string first, string second, Color colour)
    {
        var box = new VBoxContainer { Name = title, CustomMinimumSize = new Vector2(400f, 0f) };
        box.AddChild(new Label { Text = title, Modulate = colour, HorizontalAlignment = HorizontalAlignment.Center });
        var grid = new GridContainer { Name = "List", Columns = 3 };
        grid.AddThemeConstantOverride("h_separation", 24);
        grid.AddChild(Header(first, 200f));
        Label s = Header(second, 90f);
        grid.AddChild(s);
        grid.AddChild(Header("Lives", 70f));
        box.AddChild(grid);
        return (box, grid, s);
    }

    private static Label Header(string text, float width) =>
        new() { Text = text, Modulate = UiStyle.Dim, CustomMinimumSize = new Vector2(width, 0f), HorizontalAlignment = HorizontalAlignment.Center };

    /// <summary>Opens the page (DisplaySessionStatus) and stops the player's controls.</summary>
    [Uc("SBase.SPlayerController.DisplaySessionStatus", Sha1 = "4ebb75e426add8e62ad903c12c2d04153ef50b4a")]
    [PortOf("uc:SBase.SPlayerController:DisplaySessionStatus", Spec = "spec/hud/scoreboard.md#rules")]
    public void Open()
    {
        if (Visible)
        {
            return;
        }

        Visible = true;
        if (PlayInput is { Target: { } target } input)
        {
            _inputWasEnabled = input.Enabled;
            input.ClearHeld(target);
            input.Enabled = false;
        }

        Refresh();
    }

    public void Close()
    {
        if (!Visible)
        {
            return;
        }

        Visible = false;
        if (PlayInput is { } input)
        {
            input.Enabled = _inputWasEnabled;
        }
    }

    public override void _Process(double delta)
    {
        if (!Visible)
        {
            return;
        }

        _sinceRefresh += delta;
        if (_sinceRefresh >= RefreshSeconds)
        {
            Refresh();
        }
    }

    public override void _Input(InputEvent @event)
    {
        if (!Visible || @event is not InputEventKey { Pressed: true, Echo: false } key)
        {
            return;
        }

        if (key.Keycode is Key.Escape or Key.Enter or Key.KpEnter ||
            (ExitKey is not null && string.Equals(GodotKeys.ToIk(@event), ExitKey, StringComparison.OrdinalIgnoreCase)))
        {
            Close();
            GetViewport().SetInputAsHandled();
        }
    }

    /// <summary>Fills both lists and the objective list from <see cref="Source"/>.</summary>
    [Uc("GUI.View_GameStatus.InitMission", Sha1 = "8dccbc7776ac664ec1f1a16d64409dad46432775")]
    [Uc("GUI.Browser_StatsSpies.RefreshList", Sha1 = "940df67129df3c0f2d57906e290bb8b0d4e20a04")]
    [Uc("GUI.Browser_StatsMercs.RefreshList", Sha1 = "1aa8c465eb664614c4851b52766074b279ee7c49")]
    [PortOf("uc:GUI.View_GameStatus:InitMission", Spec = "spec/hud/scoreboard.md#rules")]
    [PortOf("uc:GUI.Browser_StatsSpies:RefreshList", Spec = "spec/hud/scoreboard.md#rules")]
    [PortOf("uc:GUI.Browser_StatsMercs:RefreshList", Spec = "spec/hud/scoreboard.md#rules")]
    public void Refresh()
    {
        _sinceRefresh = 0;
        if (Source is not { } s || _spies is null || _mercs is null || _objectives is null)
        {
            return;
        }

        if (_spyKills is not null)
        {
            _spyKills.Text = s.DeathMatch ? "Kills" : "Objectives";
        }

        Fill(_spies, s, 0);
        Fill(_mercs, s, 1);
        var text = new StringBuilder();
        foreach (HudObjectiveLine o in s.ObjectiveLines)
        {
            text.Append(o.Done ? "+ " : "- ").Append(o.Text).Append('\n');
        }

        _objectives.Text = text.ToString();
    }

    private static void Fill(GridContainer grid, HudSnapshot s, int team)
    {
        // Keep the three header cells; rebuild the rows.
        for (int i = grid.GetChildCount() - 1; i >= 3; i--)
        {
            Node c = grid.GetChild(i);
            grid.RemoveChild(c);
            c.QueueFree();
        }

        foreach (HudScoreRow r in s.Score)
        {
            if (r.Spectator || r.Team != team)
            {
                continue;
            }

            Color colour = r.Local ? (team == 0 ? HudK.SpyColour : HudK.MercColour) : UiStyle.Text;
            grid.AddChild(NameCell(r.Name, r.Rank, colour));
            grid.AddChild(Cell((team == 0 ? r.ObjOrKills : r.Kills).ToString(CultureInfo.InvariantCulture), colour));
            grid.AddChild(Cell(r.Lives < HudK.InfiniteLives ? r.Lives.ToString(CultureInfo.InvariantCulture) : "Infinite", colour));
        }
    }

    /// <summary>The name with the player's rank emblem and level before it (OpenSCCT's own; nothing for guests).</summary>
    private static Control NameCell(string name, int rank, Color colour)
    {
        var cell = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        cell.AddThemeConstantOverride("separation", 6);
        PlayerRank r = PlayerRank.Unpack(rank);
        if (!r.IsNone)
        {
            cell.AddChild(new RankBadge(20) { TooltipText = RankBadge.Describe(r) }.Show(r));
        }

        cell.AddChild(Cell(name, colour));
        return cell;
    }

    private static Label Cell(string text, Color colour) => new() { Text = text, Modulate = colour, HorizontalAlignment = HorizontalAlignment.Center };

    /// <summary>The rows shown now, per team (tests).</summary>
    public List<string> Names(int team)
    {
        var names = new List<string>();
        GridContainer? grid = team == 0 ? _spies : _mercs;
        if (grid is null)
        {
            return names;
        }

        for (int i = 3; i < grid.GetChildCount(); i += 3)
        {
            if (grid.GetChild(i) is Label l)
            {
                names.Add(l.Text);
            }
            else if (grid.GetChild(i) is HBoxContainer cell && cell.GetChild(cell.GetChildCount() - 1) is Label named)
            {
                names.Add(named.Text);
            }
        }

        return names;
    }
}
