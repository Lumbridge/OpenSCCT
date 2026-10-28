using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Threading.Tasks;
using Godot;
using OpenScct.Core;
using OpenScct.Net;
using OpenScct.Net.Discovery;
using OpenScct.Server;
using OpenScct.Server.Online;
using OpenScctGame.Hud;
using OpenScctGame.Online;

namespace OpenScctGame.UI;

/// <summary>
/// The server browser (rl:net.master, rl:net.serverlist): the master's list, the LAN search and the saved addresses in
/// one table with ping, players, phase and how each server is reached, greyed out with the reason when this install
/// cannot join it (another build, a missing or different map, full). Join takes the selected server, or a typed
/// address, with the connect path and password chosen below. The page is the original's Sessions list
/// (LAN_Seek_Games_List: Ping, Server, Map, Mode, Players) with the rewrite's columns added (docs/MENUS.md).
/// </summary>
public partial class ServerBrowserScreen : MenuPage
{
    private sealed class Row
    {
        public required string Key { get; init; }

        public required string Source { get; init; }

        public string Name { get; set; } = string.Empty;

        public string Map { get; set; } = string.Empty;

        public int Mode { get; set; }

        public SessionPhase Phase { get; set; }

        public int Players { get; set; }

        public int MaxPlayers { get; set; }

        public int PingMs { get; set; } = -1;

        public bool Password { get; set; }

        public string Reach { get; set; } = string.Empty;

        public ulong Protocol { get; set; }

        public ulong Content { get; set; }

        public ServerEntry? Entry { get; set; }

        /// <summary>Only signed-in players may join (the master's list, or the server's own answer).</summary>
        public bool RequireAccounts { get; set; }

        /// <summary>A practice server (rl:net.practice).</summary>
        public bool Practice { get; set; }

        /// <summary>The server's id on its master (from its own answer), for a ticket; empty when not listed.</summary>
        public string MasterId { get; set; } = string.Empty;

        public IPEndPoint? Endpoint { get; set; }

        /// <summary>The players and their sides (the server's own answer, else the master's list).</summary>
        public IReadOnlyList<ServerInfoPlayer> PlayerList { get; set; } = [];
    }

    /// <summary>
    /// A join waiting for the master's answer: <see cref="Direct"/> is the address to join with the ticket (a LAN or
    /// saved server that is listed on the master), or null to join the way the answer says (a server of the list).
    /// </summary>
    private sealed record PendingJoin(Task<JoinResponse> Answer, string ServerId, string ServerName, bool RequireAccounts, JoinTarget? Direct,
        bool SignedIn);

    /// <summary>What a server for signed-in players says to a guest.</summary>
    private const string SignInToJoin = "Sign in to join this server (Account page).";

    private readonly List<Row> _rows = new();
    private OnlineService? _service;
    private ServerQuery? _query;
    private MasterClient? _master;
    private Task<ServerListResponse>? _list;
    private PendingJoin? _join;
    private string? _joinPassword;
    private LineEdit? _masterUrl;
    private LineEdit? _inviteKey;
    private LineEdit? _address;
    private LineEdit? _password;
    private OptionButton? _path;
    private Label? _status;
    private Label? _players;

    /// <summary>A listed server's master id to join as soon as the page is up (a Discord Join, docs/DISCORD.md, or a rejoin).</summary>
    public string? DiscordJoinId { get; init; }

    /// <summary>What the status line calls the server of <see cref="DiscordJoinId"/>.</summary>
    public string JoinIdName { get; init; } = "your Discord friend's game";

    /// <summary>The password to join the server of <see cref="DiscordJoinId"/> with (a rejoin keeps the one it had), or null.</summary>
    public string? JoinIdPassword { get; init; }

    public Tree? Table { get; private set; }

    public Button? JoinButton { get; private set; }

    public Button? RefreshButton { get; private set; }

    public Button? BackButton { get; private set; }

    /// <summary>Rows shown (servers found so far).</summary>
    public int Count => _rows.Count;

    public override void _Ready()
    {
        Name = "ServerBrowser";
        // The list takes the screen's whole width (less the page margins), so long server and map names fit.
        BuildFrame("Sessions", Mathf.Max(1120f, GetViewportRect().Size.X - 72f));
        _service = OnlineService.Get(GetTree());
        OnlineSettings settings = OnlineSettings.Current;
        VBoxContainer column = Body;

        var masterRow = new HBoxContainer { Name = "MasterRow" };
        _masterUrl = new LineEdit { Name = "MasterUrl", Text = settings.MasterUrl, PlaceholderText = "master server (https://...)", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _inviteKey = new LineEdit { Name = "InviteKey", Text = settings.InviteKey, PlaceholderText = "invite key", Secret = true, CustomMinimumSize = new Vector2(220f, 0f) };
        RefreshButton = UiStyle.MenuButton("Refresh", Refresh, "Refresh");
        RefreshButton.CustomMinimumSize = new Vector2(140f, 40f);
        masterRow.AddChild(_masterUrl);
        masterRow.AddChild(_inviteKey);
        masterRow.AddChild(RefreshButton);
        column.AddChild(masterRow);

        Table = new Tree { Name = "Servers", Columns = 8, HideRoot = true, ColumnTitlesVisible = true, CustomMinimumSize = new Vector2(1080f, 380f), SizeFlagsHorizontal = SizeFlags.ExpandFill, SelectMode = Tree.SelectModeEnum.Row };
        string[] titles = ["Server", "Map", "Mode", "Players", "Ping", "State", "Reached by", "Joinable"];
        for (int i = 0; i < titles.Length; i++)
        {
            Table.SetColumnTitle(i, titles[i]);
            // Server and map names share the spare width (map names such as "ENHANCED STEEL SQUAT" are long).
            Table.SetColumnExpand(i, i is 0 or 1);
            Table.SetColumnExpandRatio(i, i == 0 ? 3 : 2);
            Table.SetColumnCustomMinimumWidth(i, i switch
            {
                0 => 240,
                1 => 220,
                2 => 120,
                3 => 90,
                4 => 60,
                5 => 80,
                6 => 130,
                _ => 130,
            });
        }

        Table.ItemActivated += Join;
        Table.ItemSelected += ShowPlayers;
        column.AddChild(Table);

        // The selected session's players by side (the original's LAN list shows none; the master and the server's
        // answer carry them).
        _players = new Label { Name = "Players", Modulate = UiStyle.MenuFocus, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        column.AddChild(_players);

        var joinRow = new HBoxContainer { Name = "JoinRow" };
        _address = new LineEdit { Name = "Address", Text = settings.LastAddress, PlaceholderText = "address or address:port", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _password = new LineEdit { Name = "Password", PlaceholderText = "password (if any)", Secret = true, CustomMinimumSize = new Vector2(200f, 0f) };
        _path = new OptionButton { Name = "Path" };
        foreach (string p in new[] { "Auto", "Direct", "NAT punch", "Relay" })
        {
            _path.AddItem(p);
        }

        _path.Selected = settings.Path switch
        {
            ConnectPath.Direct => 1,
            ConnectPath.Intro => 2,
            ConnectPath.Relay => 3,
            _ => 0,
        };
        Button joinAddress = UiStyle.MenuButton("Join address", JoinAddress, "JoinAddress");
        Button save = UiStyle.MenuButton("Save address", SaveAddress, "SaveAddress");
        joinAddress.CustomMinimumSize = save.CustomMinimumSize = new Vector2(170f, 40f);
        joinRow.AddChild(_address);
        joinRow.AddChild(joinAddress);
        joinRow.AddChild(save);
        column.AddChild(joinRow);

        var buttons = new HBoxContainer { Name = "Buttons" };
        buttons.AddChild(new Label { Text = "Connect:" });
        buttons.AddChild(_path);
        buttons.AddChild(_password);
        column.AddChild(buttons);
        BackButton = PageButton("Back", () => FrontEnd.ShowMainMenu(GetTree()), "Back");
        JoinButton = PageButton("Join", Join, "Join");

        _status = new Label { Name = "Status", Modulate = UiStyle.MenuFocus, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        column.AddChild(_status);
        Refresh();
        if (DiscordJoinId is { Length: > 0 } discordJoin && AskJoinPassword)
        {
            // A Discord Join to a server with a password: the password box first, then Join.
            _passwordJoinId = discordJoin;
            ShowStatus($"{JoinIdName} has a password. Type it in the password box and press Join.");
            _password?.GrabFocus();
        }
        else if (DiscordJoinId is { Length: > 0 } discordJoin2)
        {
            JoinByMasterId(discordJoin2, JoinIdName);
            _joinPassword = JoinIdPassword;
        }

        if (_password is not null)
        {
            _password.TextSubmitted += _ =>
            {
                if (_passwordJoinId is not null)
                {
                    Join();
                }
            };
        }
    }

    /// <summary>A Discord Join to a server with a password: ask for it before joining (docs/DISCORD.md).</summary>
    public bool AskJoinPassword { get; init; }

    /// <summary>The master id of the Discord Join waiting for its password, or null.</summary>
    private string? _passwordJoinId;

    /// <summary>Asks the master how to reach a listed server by its id and joins it (no password).</summary>
    private void JoinByMasterId(string serverId, string name)
    {
        if (_master is null)
        {
            ShowStatus("Set a master server to join " + name + ".");
            return;
        }

        _joinPassword = null;
        bool signedIn = AccountSettings.Current.SignedIn && _master.SessionToken is not null;
        ShowStatus($"Asking the master how to reach {name}...");
        _join = new PendingJoin(_master.JoinAsync(serverId), serverId, name, false, null, signedIn);
    }

    public override void _ExitTree()
    {
        _query?.Dispose();
        _query = null;
        _master?.Dispose();
        _master = null;
    }

    /// <summary>A new search: the master's list (when set), the LAN and the saved addresses.</summary>
    public void Refresh()
    {
        SaveSettings();
        _rows.Clear();
        _query?.Dispose();
        _master?.Dispose();
        _master = null;
        _list = null;
        try
        {
            _query = new ServerQuery(_service!.Clock);
        }
        catch (InvalidOperationException e)
        {
            ShowStatus(e.Message);
            return;
        }

        _query.SearchLan(OnlineSettings.Current.LanPort);
        foreach (string saved in OnlineSettings.Current.ServerList)
        {
            if (TryEndpoint(saved, out IPEndPoint? endpoint))
            {
                _query.QueryServer(endpoint, "saved:" + saved);
            }
        }

        OnlineSettings s = OnlineSettings.Current;
        if (s.MasterUrl.Length > 0)
        {
            try
            {
                _master = new MasterClient(s.MasterUrl, s.InviteKey.Length > 0 ? s.InviteKey : null);
                if (AccountSettings.Current.SignedIn)
                {
                    // Signed in, the master's join answers carry the player's ticket for the server.
                    _master.SessionToken = AccountSettings.Current.Token;
                }

                _list = _master.ListAsync();
                ShowStatus("Asking the master server...");
            }
            catch (ArgumentException e)
            {
                ShowStatus(e.Message);
            }
        }
        else
        {
            ShowStatus("No master server set: searching the LAN and the saved addresses.");
        }

        Redraw();
    }

    public override void _Process(double delta)
    {
        base._Process(delta);
        bool changed = false;
        if (_query is { } query)
        {
            int before = query.Results.Count;
            query.Poll();
            changed |= MergeAnswers(query);
            changed |= query.Results.Count != before;
        }

        if (_list is { IsCompleted: true } list)
        {
            _list = null;
            if (list.IsCompletedSuccessfully)
            {
                MergeList(list.Result);
                changed = true;
            }
            else
            {
                ShowStatus(list.Exception?.InnerException?.Message ?? "The master server did not answer.");
            }
        }

        if (_join is { Answer.IsCompleted: true } join)
        {
            _join = null;
            if (Answered(join))
            {
                return;
            }
        }

        if (changed)
        {
            Redraw();
        }
    }

    /// <summary>Takes the master's join answer; true when the join started.</summary>
    private bool Answered(PendingJoin join)
    {
        Task<JoinResponse> answer = join.Answer;
        if (!answer.IsCompletedSuccessfully)
        {
            if (join.SignedIn && answer.Exception?.InnerException is MasterException { StatusCode: 401 } && _master is not null)
            {
                // The master no longer knows the sign-in: sign out here and ask again as a guest.
                AccountService.Get(GetTree()).SignOutLocally();
                _master.SessionToken = null;
                if (join.RequireAccounts)
                {
                    ShowStatus("Your sign-in ended. " + SignInToJoin);
                    Redraw();
                    return false;
                }

                _join = join with { Answer = _master.JoinAsync(join.ServerId), SignedIn = false };
                return false;
            }

            string error = answer.Exception?.InnerException?.Message ?? "The master server did not answer.";
            if (join.Direct is { } direct && !join.RequireAccounts)
            {
                // Only the ticket was asked for: join without it.
                GD.Print("SVM_BROWSER no ticket for " + join.ServerName + ": " + error);
                _service!.Join(direct, _joinPassword);
                return true;
            }

            ShowStatus(error);
            return false;
        }

        JoinResponse result = answer.Result;
        if ((result.RequireAccounts || join.RequireAccounts) && result.Ticket is null)
        {
            ShowStatus(result.TicketProblem ?? SignInToJoin);
            return false;
        }

        if (result.Ticket is null && result.TicketProblem is { } problem)
        {
            GD.Print("SVM_BROWSER no ticket for " + join.ServerName + ": " + problem);
        }

        _service!.Join(join.Direct ?? JoinTarget.FromJoin(result, OnlineSettings.Current.Path), _joinPassword, result.Ticket, join.ServerId);
        return true;
    }

    private void MergeList(ServerListResponse list)
    {
        foreach (ServerEntry e in list.Servers)
        {
            Row row = Find("master:" + e.Id) ?? Add("master:" + e.Id, "Internet");
            row.Entry = e;
            row.Name = e.Name;
            row.Map = e.Map;
            row.Mode = e.Mode;
            row.Phase = e.Phase;
            row.Players = e.Players;
            row.MaxPlayers = e.MaxPlayers;
            row.Password = e.Password;
            row.RequireAccounts = e.RequireAccounts;
            row.Practice = e.Practice;
            if (row.Endpoint is null || row.PlayerList.Count == 0)
            {
                row.PlayerList = PlayersOf(e);
            }

            row.Reach = e.Reachability switch
            {
                Reachability.Direct => "direct",
                Reachability.NeedsIntro => "NAT punch",
                Reachability.RelayOnly => "relay",
                _ => "checking",
            };
            MasterApi.TryParseHex(e.Protocol, out ulong protocol);
            MasterApi.TryParseHex(e.Content, out ulong content);
            row.Protocol = protocol;
            row.Content = content;
            if (e.Reachability == Reachability.Direct && IPAddress.TryParse(e.Address, out IPAddress? ip))
            {
                _query?.QueryServer(new IPEndPoint(ip, e.Port), "master:" + e.Id);
            }
        }

        string motd = list.Motd is { Length: > 0 } m ? m + "  " : string.Empty;
        string update = list.LatestVersion is { Length: > 0 } latest && BuildInfo.CompareVersions(BuildInfo.Version, latest) < 0
            ? $"Version {latest} is available (you have {BuildInfo.Version}). "
            : string.Empty;
        ShowStatus(update + motd + $"{list.Servers.Count} server(s) listed.");
    }

    private bool MergeAnswers(ServerQuery query)
    {
        bool changed = false;
        foreach (ServerQueryResult r in query.Results)
        {
            if (r.Info.IsHub)
            {
                // A saved address that is a hub: hubs are joined from the Hub page, not as game sessions.
                continue;
            }

            string key = r.Tag is { } tag ? tag : "lan:" + LiteNetLibTransport.FormatEndPoint(r.GameEndPoint);
            Row row = Find(key) ?? Add(key, r.FromLan ? "LAN" : "Saved");
            if (row.PingMs == r.PingMs && row.Endpoint is not null)
            {
                continue;
            }

            row.PingMs = r.PingMs;
            row.Endpoint = r.GameEndPoint;
            row.Name = r.Info.Name;
            row.Map = r.Info.Map;
            row.Mode = r.Info.Mode;
            row.Phase = r.Info.Phase;
            row.Players = r.Info.Players;
            row.MaxPlayers = r.Info.MaxPlayers;
            row.Password = r.Info.HasPassword;
            row.Protocol = r.Info.ProtocolHash;
            row.Content = r.Info.ContentHash;
            row.RequireAccounts = r.Info.RequireAccounts || (row.Entry?.RequireAccounts ?? false);
            row.Practice = r.Info.Practice;
            row.MasterId = r.Info.MasterId;
            row.PlayerList = r.Info.PlayerList;
            if (row.Reach.Length == 0 && !r.FromLan)
            {
                row.Reach = "direct";
            }

            changed = true;
        }

        return changed;
    }

    private Row? Find(string key) => _rows.Find(r => r.Key == key);

    /// <summary>A player whose side the master did not say.</summary>
    private const byte NoSide = 254;

    /// <summary>The master's names with their sides (a host that sends no sides: every name without one).</summary>
    private static List<ServerInfoPlayer> PlayersOf(ServerEntry e)
    {
        var list = new List<ServerInfoPlayer>();
        for (int i = 0; i < e.PlayerNames.Count; i++)
        {
            list.Add(new ServerInfoPlayer(e.PlayerNames[i], i < e.PlayerTeams.Count ? e.PlayerTeams[i] : NoSide));
        }

        return list;
    }

    /// <summary>The selected session's players under the list: "Spies: ...  Mercenaries: ...  Spectators: ...".</summary>
    private void ShowPlayers()
    {
        if (_players is null)
        {
            return;
        }

        if (Table?.GetSelected() is not { } item || Find(item.GetMetadata(0).AsString()) is not { } row)
        {
            _players.Text = string.Empty;
            return;
        }

        if (row.PlayerList.Count == 0)
        {
            _players.Text = row.Players > 0 ? (row.Name.Length > 0 ? row.Name : row.Key) + ": the player names are not known yet." : string.Empty;
            return;
        }

        var spies = new List<string>();
        var mercs = new List<string>();
        var spectators = new List<string>();
        var others = new List<string>();
        foreach (ServerInfoPlayer p in row.PlayerList)
        {
            List<string> side = p.Team switch
            {
                0 => spies,
                1 => mercs,
                SessionProtocol.SpectatorTeam => spectators,
                _ => others,
            };
            side.Add(p.Name);
        }

        var parts = new List<string>();
        void Part(string title, List<string> names)
        {
            if (names.Count > 0)
            {
                parts.Add(title + ": " + string.Join(", ", names));
            }
        }

        Part("Spies", spies);
        Part("Mercenaries", mercs);
        Part("Spectators", spectators);
        Part("Players", others);
        _players.Text = string.Join("     ", parts);
    }

    private Row Add(string key, string source)
    {
        var row = new Row { Key = key, Source = source };
        _rows.Add(row);
        return row;
    }

    /// <summary>Why this install cannot join a row, or null.</summary>
    private string? Problem(Row row)
    {
        if (row.Protocol != 0 && row.Protocol != _service!.ProtocolHash)
        {
            return "another build";
        }

        if (row.Map.Length > 0 && _service!.Catalog.Maps.FindMap(row.Map) is not { } map)
        {
            return "missing map " + row.Map;
        }

        if (row.Map.Length > 0 && row.Content != 0 && _service!.Catalog.Maps.FindMap(row.Map) is { } local && local.ContentHash != row.Content)
        {
            return "different version of " + row.Map;
        }

        if (row.RequireAccounts && !AccountSettings.Current.SignedIn)
        {
            return "sign in to join";
        }

        return row.MaxPlayers > 0 && row.Players >= row.MaxPlayers ? "full" : null;
    }

    private void Redraw()
    {
        if (Table is null)
        {
            return;
        }

        string? selected = Table.GetSelected()?.GetMetadata(0).AsString();
        Table.Clear();
        TreeItem root = Table.CreateItem();
        foreach (Row row in _rows)
        {
            TreeItem item = Table.CreateItem(root);
            string? problem = Problem(row);
            item.SetText(0, (row.Password ? "[locked] " : string.Empty) + (row.RequireAccounts ? "[signed-in only] " : string.Empty) +
                (row.Name.Length > 0 ? row.Name : row.Key));
            item.SetText(1, MapTitles.Of(_service!.Catalog.Maps, row.Map));
            item.SetText(2, LobbySetup.ModeName((EGameMode)(byte)row.Mode) + (row.Practice ? " (practice)" : string.Empty));
            item.SetText(3, string.Create(CultureInfo.InvariantCulture, $"{row.Players}/{row.MaxPlayers}"));
            item.SetText(4, row.PingMs >= 0 ? row.PingMs.ToString(CultureInfo.InvariantCulture) : "-");
            item.SetText(5, SessionProtocol.PhaseName(row.Phase));
            item.SetText(6, row.Reach.Length > 0 ? row.Source + " " + row.Reach : row.Source);
            item.SetText(7, problem ?? "Joinable");
            item.SetMetadata(0, row.Key);
            if (problem is not null)
            {
                for (int c = 0; c < 8; c++)
                {
                    item.SetCustomColor(c, UiStyle.MenuDisabled);
                }
            }

            if (row.Key == selected)
            {
                item.Select(0);
            }
        }

        ShowPlayers();
    }

    private void Join()
    {
        SaveSettings();
        if (_passwordJoinId is { } waiting && _join is null)
        {
            // The Discord Join waiting for its password: join that server with what was typed.
            _passwordJoinId = null;
            JoinByMasterId(waiting, JoinIdName);
            _joinPassword = _password?.Text.Length > 0 ? _password.Text : null;
            return;
        }

        if (Table?.GetSelected() is not { } item || Find(item.GetMetadata(0).AsString()) is not { } row)
        {
            ShowStatus("Pick a server, or type an address and use Join address.");
            return;
        }

        if (Problem(row) is { } problem && problem != "full")
        {
            ShowStatus(problem == "sign in to join" ? SignInToJoin : $"Cannot join {row.Name}: {problem}.");
            return;
        }

        if (_join is not null)
        {
            return;
        }

        _joinPassword = _password?.Text.Length > 0 ? _password.Text : null;
        bool signedIn = AccountSettings.Current.SignedIn && _master?.SessionToken is not null;
        if (row.Entry is { } entry && _master is not null)
        {
            ShowStatus($"Asking the master how to reach {entry.Name}...");
            _join = new PendingJoin(_master.JoinAsync(entry.Id), entry.Id, entry.Name, row.RequireAccounts, null, signedIn);
            return;
        }

        if (row.Endpoint is { } endpoint)
        {
            JoinTarget direct = JoinTarget.FromAddress(LiteNetLibTransport.FormatEndPoint(endpoint));
            if (row.MasterId.Length > 0 && signedIn && _master is not null)
            {
                // A LAN or saved server listed on the master: the master's ticket for it first, then straight there.
                ShowStatus($"Asking the master for a ticket to {row.Name}...");
                _join = new PendingJoin(_master.JoinAsync(row.MasterId), row.MasterId, row.Name, row.RequireAccounts, direct, signedIn);
                return;
            }

            if (row.RequireAccounts)
            {
                ShowStatus("This server is for signed-in players, and your master server cannot give a ticket for it.");
                return;
            }

            _service!.Join(direct, _joinPassword);
        }
    }

    private void JoinAddress()
    {
        SaveSettings();
        string address = _address?.Text.Trim() ?? string.Empty;
        if (address.Length == 0)
        {
            ShowStatus("Type the host's address first.");
            return;
        }

        _service!.Join(JoinTarget.FromAddress(address), _password?.Text.Length > 0 ? _password.Text : null);
    }

    private void SaveAddress()
    {
        string address = _address?.Text.Trim() ?? string.Empty;
        if (address.Length > 0 && !OnlineSettings.Current.ServerList.Contains(address))
        {
            OnlineSettings.Current.ServerList.Add(address);
            SaveSettings();
            Refresh();
        }
    }

    private void SaveSettings()
    {
        OnlineSettings s = OnlineSettings.Current;
        s.MasterUrl = _masterUrl?.Text.Trim() ?? s.MasterUrl;
        s.InviteKey = _inviteKey?.Text.Trim() ?? s.InviteKey;
        s.LastAddress = _address?.Text.Trim() ?? s.LastAddress;
        s.ConnectMode = (_path?.Selected ?? 0) switch
        {
            1 => "direct",
            2 => "intro",
            3 => "relay",
            _ => "auto",
        };
        s.Save();
    }

    private static bool TryEndpoint(string address, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out IPEndPoint? endpoint)
    {
        try
        {
            endpoint = LiteNetLibTransport.Resolve(JoinTarget.FromAddress(address).Attempts[0].Address);
            return true;
        }
        catch (Exception e) when (e is FormatException or System.Net.Sockets.SocketException or ArgumentException)
        {
            endpoint = null;
            return false;
        }
    }

    private void ShowStatus(string text)
    {
        if (_status is not null)
        {
            _status.Text = text;
        }
    }
}
