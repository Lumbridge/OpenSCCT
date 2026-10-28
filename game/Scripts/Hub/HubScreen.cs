using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Threading.Tasks;
using Godot;
using OpenScct.Net;
using OpenScct.Net.Discovery;
using OpenScctGame.Match;
using OpenScctGame.Online;
using OpenScctGame.UI;

namespace OpenScctGame.Hub;

/// <summary>
/// The hub's page (an OpenSCCT addition), laid out like the other menu pages (<see cref="MenuPage"/>: the typed title,
/// framed panels, the bottom bar): the visit (name, the side to arrive as), the master's hub list (the official hubs
/// first, each with its region and ping, then the hubs players host), a hub's address to join, and hosting a hub on
/// this PC. Join takes the hub picked in the list, or the typed address when none is picked. The hub service moves on
/// to the play scene once the hub map is in; a hub that does not answer brings the player back here with the reason.
/// </summary>
public partial class HubScreen : MenuPage
{
    private const int Columns = 5;

    private sealed class Row
    {
        public required ServerEntry Entry { get; init; }

        public int PingMs { get; set; } = -1;
    }

    private readonly List<Row> _rows = new();
    private LineEdit? _address;
    private LineEdit? _name;
    private OptionButton? _side;
    private CheckBox? _listed;
    private Label? _status;
    private Task<ServerListResponse>? _list;
    private ServerQuery? _query;
    private double _requery;
    private bool _picked;

    /// <summary>The address typed last time (this process).</summary>
    public static string LastAddress { get; set; } = "127.0.0.1:" + HubService.DefaultPort.ToString(CultureInfo.InvariantCulture);

    public Button? JoinButton { get; private set; }

    public Button? HostButton { get; private set; }

    public Button? BackButton { get; private set; }

    public Button? RefreshButton { get; private set; }

    /// <summary>The master's hubs.</summary>
    public Tree? Table { get; private set; }

    /// <summary>Shows the page as the current scene.</summary>
    public static HubScreen Show(SceneTree tree) => FrontEnd.Show(tree, new HubScreen());

    /// <summary>The main menu's Hub button.</summary>
    public static Button MenuButton(Control menu)
    {
        ArgumentNullException.ThrowIfNull(menu);
        return UiStyle.MenuButton("Hub", () => Show(menu.GetTree()), "Hub");
    }

    public override void _Ready()
    {
        Name = "HubScreen";
        BuildFrame("Hub", 760f);

        VBoxContainer visit = Panel(Body, "Visit", "Your visit");
        GridContainer visitGrid = Grid(visit);
        visitGrid.AddChild(new Label { Text = "Name" });
        _name = new LineEdit
        {
            Name = "PlayerName", Text = UiSettings.Current.PlayerName, PlaceholderText = "your name", MaxLength = 20,
            CustomMinimumSize = new Vector2(300f, 0f),
        };
        visitGrid.AddChild(_name);
        visitGrid.AddChild(new Label { Text = "Arrive as" });
        _side = new OptionButton { Name = "Side" };
        _side.AddItem("Spy", 0);
        _side.AddItem("Merc", 1);
        visitGrid.AddChild(_side);

        VBoxContainer hubs = Panel(Body, "Hubs", "Hubs");
        Table = new Tree
        {
            Name = "HubList", Columns = Columns, HideRoot = true, ColumnTitlesVisible = true, SelectMode = Tree.SelectModeEnum.Row,
            CustomMinimumSize = new Vector2(700f, 190f), SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        string[] titles = ["Hub", "Region", "Hosted by", "Players", "Ping"];
        for (int i = 0; i < titles.Length; i++)
        {
            Table.SetColumnTitle(i, titles[i]);
            Table.SetColumnExpand(i, i == 0);
            Table.SetColumnCustomMinimumWidth(i, i switch { 0 => 260, 1 => 80, 2 => 130, 3 => 90, _ => 70 });
        }

        Table.ItemSelected += () => _picked = true;
        Table.ItemActivated += Join;
        hubs.AddChild(Table);
        hubs.AddChild(Note("Official hubs are run by the OpenSCCT servers; the others are hosted by players. Pick the one with the lowest ping."));

        VBoxContainer join = Panel(Body, "Join", "Join a hub by address");
        GridContainer joinGrid = Grid(join);
        joinGrid.AddChild(new Label { Text = "Hub Address" });
        _address = new LineEdit
        {
            Name = "Address", Text = LastAddress, PlaceholderText = "address or address:port",
            CustomMinimumSize = new Vector2(300f, 0f),
        };
        _address.TextSubmitted += _ => JoinAddress();

        // Typing an address means that one: the list's pick no longer counts.
        _address.FocusEntered += Unpick;
        joinGrid.AddChild(_address);
        join.AddChild(Note("A hub on your network or at a direct IP; the port is " + HubService.DefaultPort.ToString(CultureInfo.InvariantCulture) + " unless the address names one."));

        VBoxContainer host = Panel(Body, "Host", "Host a hub");
        host.AddChild(Note("Opens a hub on this PC (UDP port " + HubService.DefaultPort.ToString(CultureInfo.InvariantCulture)
            + ") and takes you in. Others join it from the hub list or by your address; the hub closes when you leave it."));
        _listed = new CheckBox { Name = "Listed", Text = "Show it in the hub list", ButtonPressed = OnlineSettings.Current.HubListed };
        _listed.Toggled += on =>
        {
            OnlineSettings.Current.HubListed = on;
            OnlineSettings.Current.Save();
        };
        host.AddChild(_listed);

        _status = new Label { Name = "Status", Modulate = UiStyle.MenuFocus, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        Body.AddChild(_status);

        // The bottom bar in B, X, Y, A order as the other pages lay it out.
        BackButton = PageButton("Back", Back, "Back");
        HostButton = PageButton("Host", Host, "HostHub");
        RefreshButton = PageButton("Refresh", Refresh, "RefreshHubs");
        JoinButton = PageButton("Join", Join, "JoinHub");
        JoinButton.GrabFocus();
        Refresh();
        if (HubService.Get(GetTree()).LastError is { } error)
        {
            _status.Text = error;
        }
    }

    public override void _Process(double delta)
    {
        base._Process(delta);
        HubService service = HubService.Get(GetTree());
        if (_status is not null && service.Busy)
        {
            _status.Text = service.Status;
        }

        if (_list is { IsCompleted: true } list)
        {
            _list = null;
            if (list.IsCompletedSuccessfully)
            {
                Listed(list.Result);
            }
            else if (_status is not null && !service.Busy)
            {
                _status.Text = "The master server's hub list did not come: " + (list.Exception?.InnerException?.Message ?? "no answer.");
            }
        }

        if (_query is { } query)
        {
            // The pings stay current while the page is open (a first answer is often slow).
            _requery += delta;
            if (_requery >= ServerQuery.RequerySeconds)
            {
                _requery = 0;
                QueryAll(query);
            }

            query.Poll();
            if (TakePings(query))
            {
                Redraw();
            }
        }
    }

    public override void _ExitTree()
    {
        _query?.Dispose();
        _query = null;
    }

    private static GridContainer Grid(VBoxContainer panel)
    {
        var grid = new GridContainer { Columns = 2 };
        grid.AddThemeConstantOverride("h_separation", 16);
        panel.AddChild(grid);
        return grid;
    }

    private static Label Note(string text) =>
        new() { Text = text, Modulate = UiStyle.MenuFocus, AutowrapMode = TextServer.AutowrapMode.WordSmart };

    private void Refresh()
    {
        _rows.Clear();
        _picked = false;
        Redraw();
        _query?.Dispose();
        _query = null;
        _list = HubService.Get(GetTree()).ListHubs();
        if (_status is not null && !HubService.Get(GetTree()).Busy)
        {
            _status.Text = _list is null ? "No master server is set (Online settings): join a hub by its address." : "Asking the master server for hubs...";
        }
    }

    private void Listed(ServerListResponse list)
    {
        _rows.Clear();
        foreach (ServerEntry e in list.Servers)
        {
            _rows.Add(new Row { Entry = e });
        }

        Sort();
        try
        {
            _query = new ServerQuery(new StopwatchClock());
            _requery = 0;
            QueryAll(_query);
        }
        catch (InvalidOperationException)
        {
            // No pings without a query socket; the list still joins.
            _query = null;
        }

        Redraw();
        if (_status is not null && !HubService.Get(GetTree()).Busy)
        {
            _status.Text = _rows.Count == 0
                ? "No hub is listed right now. Host one, or join one by its address."
                : string.Create(CultureInfo.InvariantCulture, $"{_rows.Count} hub(s) listed.");
        }
    }

    /// <summary>Asks every listed hub for its info (the ping column).</summary>
    private void QueryAll(ServerQuery query)
    {
        foreach (Row r in _rows)
        {
            if (IPAddress.TryParse(r.Entry.Address, out IPAddress? ip) && r.Entry.Port > 0)
            {
                query.QueryServer(new IPEndPoint(ip, r.Entry.Port), r.Entry.Id);
            }
        }
    }

    private bool TakePings(ServerQuery query)
    {
        bool changed = false;
        foreach (ServerQueryResult r in query.Results)
        {
            foreach (Row row in _rows)
            {
                if (row.Entry.Id == r.Tag && row.PingMs != r.PingMs)
                {
                    row.PingMs = r.PingMs;
                    changed = true;
                }
            }
        }

        if (changed)
        {
            Sort();
        }

        return changed;
    }

    /// <summary>Official hubs first, then the nearest (lowest ping, unknown last), then the fullest.</summary>
    private void Sort() => _rows.Sort((a, b) =>
    {
        if (a.Entry.Trusted != b.Entry.Trusted)
        {
            return b.Entry.Trusted.CompareTo(a.Entry.Trusted);
        }

        int pa = a.PingMs < 0 ? int.MaxValue : a.PingMs;
        int pb = b.PingMs < 0 ? int.MaxValue : b.PingMs;
        return pa != pb ? pa.CompareTo(pb) : b.Entry.Players.CompareTo(a.Entry.Players);
    });

    private string? Problem(ServerEntry e)
    {
        HubService service = HubService.Get(GetTree());
        if (MasterApi.TryParseHex(e.Protocol, out ulong protocol) && protocol != 0 &&
            protocol != service.Classes.ComputeProtocolHash())
        {
            return "another build";
        }

        if (MasterApi.TryParseHex(e.Content, out ulong content) && content != 0 &&
            service.Catalog.Maps.FindMap(e.Map) is { } map && map.ContentHash != content)
        {
            return "another hub map";
        }

        return e.MaxPlayers > 0 && e.Players >= e.MaxPlayers ? "full" : null;
    }

    private void Redraw()
    {
        if (Table is null)
        {
            return;
        }

        string? selected = _picked ? Table.GetSelected()?.GetMetadata(0).AsString() : null;
        Table.Clear();
        TreeItem root = Table.CreateItem();
        foreach (Row row in _rows)
        {
            ServerEntry e = row.Entry;
            TreeItem item = Table.CreateItem(root);
            string? problem = Problem(e);
            item.SetText(0, e.Name + (problem is null ? string.Empty : " (" + problem + ")"));
            item.SetText(1, e.Region.Length > 0 ? e.Region : "-");
            item.SetText(2, e.Trusted ? "Official" : e.Dedicated ? "Dedicated" : "Player");
            item.SetText(3, string.Create(CultureInfo.InvariantCulture, $"{e.Players}/{e.MaxPlayers}"));
            item.SetText(4, row.PingMs >= 0 ? row.PingMs.ToString(CultureInfo.InvariantCulture) : "-");
            item.SetMetadata(0, e.Id);
            for (int c = 0; c < Columns; c++)
            {
                if (problem is not null)
                {
                    item.SetCustomColor(c, UiStyle.MenuDisabled);
                }
                else if (e.Trusted)
                {
                    item.SetCustomColor(c, UiStyle.MenuTitle);
                }
            }

            if (e.Id == selected)
            {
                item.Select(0);
            }
        }
    }

    private void Unpick()
    {
        _picked = false;
        Table?.DeselectAll();
    }

    /// <summary>The picked hub of the list, or else the typed address.</summary>
    private void Join()
    {
        if (_picked && Table?.GetSelected() is { } item && _rows.Find(r => r.Entry.Id == item.GetMetadata(0).AsString()) is { } row)
        {
            if (Problem(row.Entry) is { } problem)
            {
                if (_status is not null)
                {
                    _status.Text = $"Cannot join {row.Entry.Name}: {problem}.";
                }

                return;
            }

            HubService.Get(GetTree()).JoinListed(row.Entry.Id, row.Entry.Name, PlayerName, Team);
            return;
        }

        JoinAddress();
    }

    private void JoinAddress()
    {
        string address = _address?.Text ?? string.Empty;
        try
        {
            string target = HubService.Normalise(address);
            LastAddress = target;
            HubService.Get(GetTree()).Join(target, PlayerName, Team);
        }
        catch (ArgumentException e)
        {
            if (_status is not null)
            {
                _status.Text = e.Message;
            }
        }
    }

    private string PlayerName => _name?.Text ?? "Player";

    private int Team => _side?.Selected == 1 ? 1 : 0;

    private void Back()
    {
        // A visit still connecting (or a hub this PC opened) stops with the page.
        HubService.Get(GetTree()).Leave(showMenu: false);
        FrontEnd.ShowMainMenu(GetTree());
    }

    private void Host()
    {
        HubService service = HubService.Get(GetTree());
        if (service.HostAndJoin(HubService.DefaultPort, PlayerName, Team))
        {
            LastAddress = service.Host!.LocalAddress;
        }
        else if (_status is not null)
        {
            _status.Text = service.LastError;
        }
    }
}
