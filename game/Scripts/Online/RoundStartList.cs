using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using OpenScct.Core.Engine;
using OpenScct.Provenance;
using OpenScct.Server.Online;
using OpenScctGame.UI;
using LobbyPlayer = OpenScct.Server.Online.LobbyPlayer;

namespace OpenScctGame.Online;

/// <summary>One line of the round's waiting list: a player and how far he is.</summary>
public enum RoundStartState
{
    /// <summary>In the session but not on the round's map yet (no PRI on this client).</summary>
    Joining,

    /// <summary>On the map, still loading (his PRI has not reported waiting to play).</summary>
    Loading,

    /// <summary>Loaded and waiting for the round to start (PRI.bWaitingPlayer).</summary>
    Ready,

    /// <summary>Was in the session when the round began loading and was dropped for not loading in time.</summary>
    TimedOut,
}

/// <summary>
/// The original's Loading_Menu list (spec/game/match-lifecycle.md "Waiting for players"): while a network round waits
/// for everyone to load, "Waiting for players (n / m)" and a line per player, "is ready" once his PRI reports waiting
/// to play (bWaitingPlayer, which replicates to every client) and "is connecting" before. The rewrite adds what the
/// session knows beyond the PRIs: a player of the session who has no PRI on the map yet ("Loading..."), and one the
/// server dropped for not loading in time ("Player timed out..."); the round then starts without him. Drawn over the
/// round's view until the match begins; the captions are Loading_Menu's own from GUI.int.
/// </summary>
public partial class RoundStartList : CanvasLayer
{
    public const string NodeName = "RoundStartList";

    private const string Section = "Loading_Menu";

    private readonly OnlineClient _client;
    private readonly Dictionary<int, string> _roster = new();
    private readonly HashSet<string> _droppedNames = new(StringComparer.Ordinal);
    private Control? _box;
    private Label? _title;
    private VBoxContainer? _lines;
    private string _shown = string.Empty;

    public RoundStartList(OnlineClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        Name = NodeName;
        Layer = OpenScctGame.Hud.HudLayer.DefaultLayer + 2;
    }

    public override void _Ready()
    {
        // Loading_Menu: the title at WinTop 0.2 across 0.6 of the page, the list at 0.35 across 0.8.
        _box = new PanelContainer
        {
            Name = "Box", AnchorLeft = 0.2f, AnchorRight = 0.8f, AnchorTop = 0.2f, AnchorBottom = 0.2f, Theme = UiStyle.MenuTheme,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Visible = false,
        };
        ((PanelContainer)_box).AddThemeStyleboxOverride("panel", UiStyle.MenuPanelBox());
        var column = new VBoxContainer { Name = "Column", MouseFilter = Control.MouseFilterEnum.Ignore };
        column.AddThemeConstantOverride("separation", 6);
        _title = new Label { Name = "Title", HorizontalAlignment = HorizontalAlignment.Center, Modulate = UiStyle.MenuTitle };
        _lines = new VBoxContainer { Name = "Lines", MouseFilter = Control.MouseFilterEnum.Ignore };
        column.AddChild(_title);
        column.AddChild(_lines);
        _box.AddChild(column);
        AddChild(_box);
        foreach (LobbyPlayer p in _client.Lobby.Players)
        {
            _roster[p.Id] = p.Name;
        }

        // This layer can be drawn before its first process tick during the scene transition.
        // Populate it now so that frame never contains an empty panel.
        Refresh();
    }

    public override void _Process(double delta) => Refresh();

    private void Refresh()
    {
        GameReplicationInfo? gri = _client.Session?.LocalPlayer?.GameReplicationInfo;
        bool waiting = _client.State == OnlineClientState.Loading && !(gri?.bMatchHasBegun ?? false);
        if (!waiting)
        {
            if (_box is not null) _box.Visible = false;
            return;
        }

        // A player of the round's start who left the session while it loaded was dropped (did not load in time).
        foreach ((int id, string name) in _roster)
        {
            if (!System.Linq.Enumerable.Any(_client.Lobby.Players, p => p.Id == id))
            {
                _droppedNames.Add(name);
            }
        }

        foreach (LobbyPlayer p in _client.Lobby.Players)
        {
            _roster.TryAdd(p.Id, p.Name);
        }

        Show(Lines(_client.Lobby.Players, gri?.PRIArray, _droppedNames));
        if (_box is not null) _box.Visible = true;
    }

    /// <summary>
    /// The list's lines: every player of the session in session order, with his state from the PRI of the same name
    /// (spectators too, as Loading_Menu lists every PRI), then the dropped players.
    /// </summary>
    [PortOf("uc:GUI.Loading_Menu:s_Client@code", Spec = "spec/game/match-lifecycle.md#waiting-for-players")]
    public static List<(string Name, RoundStartState State)> Lines(IReadOnlyList<LobbyPlayer> players, IReadOnlyList<PlayerReplicationInfo?>? pris,
        IEnumerable<string> dropped)
    {
        ArgumentNullException.ThrowIfNull(players);
        ArgumentNullException.ThrowIfNull(dropped);
        var lines = new List<(string, RoundStartState)>();
        foreach (LobbyPlayer p in players)
        {
            PlayerReplicationInfo? pri = null;
            for (int i = 0; pris is not null && i < pris.Count; i++)
            {
                if (pris[i] is { bDeleteMe: false } candidate && string.Equals(candidate.PlayerName, p.Name, StringComparison.Ordinal))
                {
                    pri = candidate;
                    break;
                }
            }

            lines.Add((p.Name, pri is null ? RoundStartState.Joining : pri.bWaitingPlayer ? RoundStartState.Ready : RoundStartState.Loading));
        }

        foreach (string name in dropped)
        {
            if (!lines.Exists(l => l.Item1 == name))
            {
                lines.Add((name, RoundStartState.TimedOut));
            }
        }

        return lines;
    }

    private void Show(List<(string Name, RoundStartState State)> lines)
    {
        if (_title is null || _lines is null)
        {
            return;
        }

        int ready = lines.FindAll(l => l.State == RoundStartState.Ready).Count;
        int expected = lines.FindAll(l => l.State != RoundStartState.TimedOut).Count;
        string title = string.Create(CultureInfo.InvariantCulture,
            $"{Caption("titleText", "Waiting for players")} ({ready} / {expected})");
        var key = new System.Text.StringBuilder(title);
        foreach ((string name, RoundStartState state) in lines)
        {
            key.Append('|').Append(name).Append((int)state);
        }

        if (key.ToString() == _shown)
        {
            return;
        }

        _shown = key.ToString();
        _title.Text = title;
        foreach (Node child in _lines.GetChildren())
        {
            _lines.RemoveChild(child);
            child.QueueFree();
        }

        foreach ((string name, RoundStartState state) in lines)
        {
            string text = state switch
            {
                RoundStartState.Ready => " -" + name + " " + Caption("ReadyText", "is ready"),
                RoundStartState.Loading => " -" + name + " " + Caption("LoadingText", "is connecting"),
                RoundStartState.Joining => " -" + name + " " + Caption("ConnectingText", "Loading..."),
                _ => " -" + name + ": " + Caption("TimedOutText", "Player timed out..."),
            };
            _lines.AddChild(new Label
            {
                Text = text, Modulate = state == RoundStartState.Ready ? UiStyle.MenuFocus : state == RoundStartState.TimedOut ? UiStyle.MenuDisabled : UiStyle.MenuText,
            });
        }
    }

    private static string Caption(string key, string fallback) => OpenScctGame.Tutorials.TutorialText.Caption(Section, key, fallback);
}
