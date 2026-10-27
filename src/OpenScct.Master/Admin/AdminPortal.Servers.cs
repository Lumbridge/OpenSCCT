using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using Microsoft.AspNetCore.Http;
using OpenScct.Net.Discovery;
using static OpenScct.Master.Admin.AdminHtml;

namespace OpenScct.Master.Admin;

/// <summary>A registered server as the portal shows it (copied out of the registry under its lock).</summary>
internal sealed record ServerView(
    string Id, string Name, ServerKind Kind, bool Listed, bool Trusted, string Map, int Mode, SessionPhase Phase, int People, int MaxPlayers,
    string Version, string Ip, string Region, double Age, double SinceHeartbeat, bool Password, bool RequireAccounts,
    IReadOnlyList<HeartbeatPlayer> Players, int RoundSecondsLeft, int Pending,
    IReadOnlyList<(MasterCommand Command, string Output, double At)> Log);

// The portal's servers: the live list, one server with its players, the admin command queue that trusted servers
// pick up with their heartbeats, and server blocks.
internal sealed partial class AdminPortal
{
    /// <summary>Heartbeat interval while commands wait or an admin watches a server (the host's minimum).</summary>
    public const int FastHeartbeatSeconds = 5;

    /// <summary>Seconds a server page keeps its server on fast heartbeats after it was last loaded.</summary>
    private const double WatchSeconds = 45;

    /// <summary>Seconds a queued command waits for its server before it is dropped.</summary>
    private const double CommandLifetime = 300;

    private readonly Dictionary<long, double> _queuedAt = new();

    private List<ServerView> Snapshot()
    {
        var views = new List<ServerView>();
        lock (_registry.Gate)
        {
            double now = _registry.Now;
            foreach (ServerRecord r in _registry.Snapshot())
            {
                HeartbeatRequest h = r.Last;
                IReadOnlyList<HeartbeatPlayer> players = h.PlayerDetails.Count > 0
                    ? h.PlayerDetails
                    : h.PlayerNames.Select((n, i) => new HeartbeatPlayer { Id = -1, Name = n, Team = -1 }).ToList();
                views.Add(new ServerView(r.Id, h.Name, h.Kind, h.Listed, r.Trusted, h.Map, h.Mode, h.Phase, h.Players, h.MaxPlayers, h.Version,
                    r.PublicIp.ToString(), h.Region, now - r.FirstSeen, now - r.LastSeen, h.Password, h.RequireAccounts, players,
                    h.RoundSecondsLeft, r.PendingCommands.Count, [.. r.CommandLog]));
            }
        }

        views.Sort((a, b) => a.Trusted != b.Trusted ? b.Trusted.CompareTo(a.Trusted) : b.People != a.People ? b.People.CompareTo(a.People) : string.CompareOrdinal(a.Name, b.Name));
        return views;
    }

    /// <summary>Accounts on any server now (from the heartbeats).</summary>
    private HashSet<long> OnlineAccounts()
    {
        var online = new HashSet<long>();
        lock (_registry.Gate)
        {
            foreach (ServerRecord r in _registry.Snapshot())
            {
                online.UnionWith(r.Last.AccountIds);
            }
        }

        return online;
    }

    private static string PhaseText(ServerView s) => s.Phase switch
    {
        SessionPhase.Match => s.RoundSecondsLeft >= 0 ? $"playing ({Duration(s.RoundSecondsLeft)} left)" : "playing",
        SessionPhase.Loading => "loading",
        SessionPhase.Results => "results",
        _ => "lobby",
    };

    private static string ServerTable(IEnumerable<ServerView> servers) => Table(
        ["Server", "Kind", "Map", "Phase", "Players", "Version", "Address", "Up", ""],
        servers.Select(s => new[]
        {
            Link($"/admin/servers/{s.Id}", s.Name), H(s.Kind == ServerKind.Hub ? "hub" : "game") + (s.Region.Length > 0 ? " " + H(s.Region) : string.Empty),
            H(s.Map.Length > 0 ? $"{s.Map} {ModeName(s.Mode)}" : "-"), H(PhaseText(s)), H($"{s.People} / {s.MaxPlayers}"), H(s.Version), H(s.Ip),
            H(Duration(s.Age)),
            (s.Trusted ? Badge("trusted", "ok") : string.Empty) + (s.Listed ? string.Empty : Badge("unlisted")) +
                (s.Password ? Badge("password") : string.Empty) + (s.RequireAccounts ? Badge("signed-in only") : string.Empty),
        }), "No servers are registered.");

    private IResult Servers(HttpContext c, AdminSessionRow admin)
    {
        List<ServerView> servers = Snapshot();
        return Page(c, "Servers", "/admin/servers", admin.Name,
            $"<p class=\"muted\">{servers.Count} registered. Only trusted servers (with a server key in the master's config) take commands.</p>" +
            ServerTable(servers), refresh: true);
    }

    private IResult Server(HttpContext c, AdminSessionRow admin, string id, string? notice, bool bad)
    {
        lock (_registry.Gate)
        {
            if (_registry.Find(id) is { } record)
            {
                record.WatchedUntil = _registry.Now + WatchSeconds;
            }
        }

        if (Snapshot().FirstOrDefault(s => s.Id == id) is not { } s)
        {
            return Page(c, "Server gone", "/admin/servers", admin.Name, "<p>That server is no longer registered (it stopped or missed its heartbeats).</p>",
                notice, bad, status: 404);
        }

        string self = "/admin/servers/" + U(id);
        var body = new StringBuilder();
        body.Append(Tiles(
            ("Map", s.Map.Length > 0 ? s.Map : "-", ModeName(s.Mode)), ("Phase", PhaseText(s), null),
            ("Players", $"{s.People} / {s.MaxPlayers}", null), ("Version", s.Version, s.Trusted ? "trusted" : "not trusted"),
            ("Address", s.Ip, s.Region.Length > 0 ? s.Region : null),
            ("Up", Duration(s.Age), $"last heartbeat {Duration(s.SinceHeartbeat)} ago")));
        HashSet<long> known = s.Players.Where(p => p.AccountId is not null).Select(p => p.AccountId!.Value).ToHashSet();
        Dictionary<long, string> names = known.ToDictionary(a => a, a => _store.FindById(a)?.Name ?? "#" + a);
        body.Append(Section("Players", Table(["Player", "Side", "Ping", "Account", ""], s.Players.Select(p => new[]
        {
            H(p.Name) + (p.Bot ? " " + Badge("bot") : string.Empty) + (p.Host ? " " + Badge("host") : string.Empty) +
                (p.Admin ? " " + Badge("admin") : string.Empty),
            H(p.Team switch { 0 => "Spy", 1 => "Merc", 255 => "Spectator", _ => "-" }), H(p.Id >= 0 ? $"{p.PingMs} ms" : "-"),
            p.AccountId is { } a ? Link($"/admin/players/{a}", names[a]) : H(p.Bot ? "-" : "guest"),
            s.Trusted && p.Id >= 0 && !p.Bot
                ? Form(c, self, $"<input type=\"hidden\" name=\"player\" value=\"{p.Id}\">" + Button("Kick", "do", "kick"),
                    $"Kick {p.Name}?", "inline") +
                  Form(c, self, $"<input type=\"hidden\" name=\"player\" value=\"{p.Id}\">" + Input("minutes", "Minutes", type: "number") +
                    Button("Ban here", "do", "ban", "danger"), $"Ban {p.Name} from this server?", "inline")
                : string.Empty,
        }), "Nobody is on this server.")));

        if (s.Trusted)
        {
            body.Append(Section("Control", "<div class=\"actions\">" +
                Form(c, self, Input("text", "Message to everyone", maxLength: 150, required: true) + Button("Say", "do", "say")) +
                Form(c, self, Input("map", "Map code (e.g. SteeE)", maxLength: 16, required: true) +
                    Select("mode", [("", "Its default mode"), ("story", "Story"), ("diskhunt", "Disk Hunt"), ("deathmatch", "Deathmatch"),
                        ("mercdeathmatch", "Merc Deathmatch"), ("extraction", "Extraction"), ("sabotage", "Sabotage"),
                        ("neutralization", "Neutralization")]) + Button("Change map", "do", "map"), "Change the map?") +
                Form(c, self, Button("Start round", "do", "launch") + Button("Next map", "do", "next") + Button("Restart round", "do", "restart") +
                    Button("Back to lobby", "do", "lobby"), "Do this on the server now?") +
                Form(c, self, Select("option", [("time", "Time limit (min)"), ("spylives", "Spy lives"), ("merclives", "Merc lives"),
                        ("respawn", "Respawn (s)"), ("score", "Score / kill limit"), ("games", "Games in a row"), ("maxplayers", "Max players"),
                        ("bots", "Bots (off, side, fill)"), ("botsperside", "Bots per side"), ("botskill", "Bot skill")]) +
                    Input("value", "Value", maxLength: 16, required: true) + Button("Set", "do", "set")) +
                Form(c, self, Input("line", "Console command (as the server's console takes it)", maxLength: 200, required: true) +
                    Button("Run", "do", "console"), "Run this console command on the server?") +
                "</div>" +
                (s.Pending > 0 ? $"<p class=\"muted\">{s.Pending} command(s) waiting for the server's next heartbeat (a few seconds).</p>" : string.Empty)));
            body.Append(Section("Command results", Table(["When", "Command", "Output"], s.Log.AsEnumerable().Reverse().Take(20).Select(l => new[]
            {
                H(Duration(_registryNow() - l.At) + " ago"), "<code>" + H(l.Command.Line) + "</code>", "<pre>" + H(l.Output) + "</pre>",
            }), "No commands yet.")));
        }
        else
        {
            body.Append("<p class=\"muted\">This server is not trusted, so it takes no commands. Kicks still reach it through bans " +
                "(banned accounts are dropped on its next heartbeat).</p>");
        }

        body.Append(Section("Block", Form(c, "/admin/servers/blocks",
            $"<input type=\"hidden\" name=\"kind\" value=\"ip\"><input type=\"hidden\" name=\"value\" value=\"{H(s.Ip)}\">" +
            Input("reason", "Reason") + Button($"Block {s.Ip}", "do", "block", "danger"),
            "Refuse every heartbeat from this address? Its servers leave the list.", "bar")));
        return Page(c, s.Name, "/admin/servers", admin.Name, body.ToString(), notice, bad, refresh: true);
    }

    private double _registryNow()
    {
        lock (_registry.Gate)
        {
            return _registry.Now;
        }
    }

    private IResult ServerAction(HttpContext c, AdminSessionRow admin, string id, IFormCollection form)
    {
        string player = Field(form, "player", 8);
        bool playerOk = int.TryParse(player, NumberStyles.None, CultureInfo.InvariantCulture, out int playerId);
        int.TryParse(Field(form, "minutes", 8), NumberStyles.None, CultureInfo.InvariantCulture, out int minutes);
        string? line = Field(form, "do") switch
        {
            "say" when Field(form, "text", 150).Length > 0 => "say " + Field(form, "text", 150),
            "kick" when playerOk => $"kick {playerId} Kicked by an admin",
            "ban" when playerOk => $"ban {playerId} {(minutes > 0 ? minutes.ToString(CultureInfo.InvariantCulture) + " " : string.Empty)}Banned by an admin",
            "map" when IsWord(Field(form, "map", 16)) => ("map " + Field(form, "map", 16) + " " + Field(form, "mode", 20)).Trim(),
            "launch" => "launch",
            "next" => "next",
            "lobby" => "lobby",
            "restart" => "restart",
            "set" when IsWord(Field(form, "option", 16)) && IsWord(Field(form, "value", 16)) => $"set {Field(form, "option", 16)} {Field(form, "value", 16)}",
            "console" when Field(form, "line", 200).Length > 0 => Field(form, "line", 200),
            _ => null,
        };
        if (line is null)
        {
            return Server(c, admin, id, "That command is missing a value.", true);
        }

        bool queued = Queue(id, line);
        if (queued)
        {
            _store.Audit("server.command", $"{id}: {line}");
        }

        return Server(c, admin, id, queued ? $"Sent: {line}. The server runs it within a few seconds." : "The server is gone or not trusted.", !queued);
    }

    private static bool IsWord(string text) =>
        text.Length > 0 && text.All(ch => char.IsLetterOrDigit(ch) || ch is '.' or '_' or '-');

    private IResult BlockAction(HttpContext c, AdminSessionRow admin, IFormCollection form)
    {
        string action = Field(form, "do");
        if (action == "unblock" && long.TryParse(Field(form, "id"), NumberStyles.None, CultureInfo.InvariantCulture, out long blockId))
        {
            _store.RemoveBlock(blockId);
            return Settings(c, admin, "Block removed.", false);
        }

        string kind = Field(form, "kind") == "id" ? "id" : "ip";
        string value = Field(form, "value", 64);
        if (kind == "ip" && AccountStore.NormalizeIp(value) is { } ip)
        {
            value = ip;
        }
        else if (kind == "ip" || value.Length == 0)
        {
            return Settings(c, admin, $"'{value}' is not an address.", true);
        }

        _store.AddBlock(kind, value, Field(form, "reason"));
        lock (_registry.Gate)
        {
            foreach (ServerRecord r in _registry.Snapshot())
            {
                if (IsBlocked(r.Id, r.PublicIp))
                {
                    r.LastSeen = double.NegativeInfinity; // dropped by the next sweep
                }
            }
        }

        return Settings(c, admin, $"Blocked {kind} {value}.", false);
    }

    // ---- The command queue -------------------------------------------------------------------------------------

    /// <summary>Queues a console line for a trusted server; false when it is gone or not trusted.</summary>
    public bool Queue(string serverId, string line)
    {
        lock (_registry.Gate)
        {
            if (_registry.Find(serverId) is not { Trusted: true } record)
            {
                return false;
            }

            var command = new MasterCommand { Id = _nextCommandId++, Line = line };
            record.PendingCommands.Add(command);
            _queuedAt[command.Id] = _registry.Now;
            return true;
        }
    }

    /// <summary>Queues a line for every trusted game server and hub; returns how many.</summary>
    public int QueueForAll(string line)
    {
        List<string> ids;
        lock (_registry.Gate)
        {
            ids = _registry.Snapshot().Where(r => r.Trusted).Select(r => r.Id).ToList();
        }

        return ids.Count(id => Queue(id, line));
    }

    /// <summary>Kicks an account from every trusted server it is on.</summary>
    private AdminResult KickAccount(long accountId, string reason)
    {
        var lines = new List<(string Server, string Line)>();
        lock (_registry.Gate)
        {
            foreach (ServerRecord r in _registry.Snapshot())
            {
                foreach (HeartbeatPlayer p in r.Last.PlayerDetails)
                {
                    if (p.AccountId == accountId && r.Trusted)
                    {
                        lines.Add((r.Id, $"kick {p.Id} {reason}"));
                    }
                }
            }
        }

        int sent = lines.Count(l => Queue(l.Server, l.Line));
        if (sent > 0)
        {
            _store.Audit("server.kick", $"account {accountId} from {sent} server(s)");
        }

        return sent > 0 ? AdminResult.Done($"Kick sent to {sent} server(s).") : AdminResult.Missing("The player is not on a trusted server now.");
    }

    /// <summary>
    /// The registry's part of a heartbeat answer (called under the registry lock): the record's waiting commands, the
    /// heartbeat interval (fast while commands wait or an admin watches), and the results the server sent.
    /// </summary>
    public (IReadOnlyList<MasterCommand> Commands, int? HeartbeatSeconds) OnHeartbeat(ServerRecord record, HeartbeatRequest request)
    {
        double now = _registry.Now;
        foreach (MasterCommandResult result in request.CommandResults.Take(20))
        {
            string output = result.Output.Length > 2000 ? result.Output[..2000] : result.Output;
            record.CommandLog.Add((new MasterCommand { Id = result.Id, Line = SentLine(record, result.Id) }, output, now));
        }

        if (record.CommandLog.Count > 50)
        {
            record.CommandLog.RemoveRange(0, record.CommandLog.Count - 50);
        }

        if (!record.Trusted)
        {
            record.PendingCommands.Clear();
            return ([], null);
        }

        record.PendingCommands.RemoveAll(cmd => !_queuedAt.TryGetValue(cmd.Id, out double at) || now - at > CommandLifetime);
        List<MasterCommand> commands = [.. record.PendingCommands];
        foreach (MasterCommand cmd in commands)
        {
            _sent[cmd.Id] = cmd.Line;
            _queuedAt.Remove(cmd.Id);
        }

        record.PendingCommands.Clear();
        bool fast = commands.Count > 0 || record.WatchedUntil > now;
        if (_sent.Count > 500)
        {
            foreach (long old in _sent.Keys.OrderBy(k => k).Take(_sent.Count - 500).ToList())
            {
                _sent.Remove(old);
            }
        }

        return (commands, fast ? FastHeartbeatSeconds : null);
    }

    private readonly Dictionary<long, string> _sent = new();

    private string SentLine(ServerRecord record, long id) => _sent.TryGetValue(id, out string? line) ? line : $"#{id}";

    /// <summary>True when a block refuses this server id or address.</summary>
    public bool IsBlocked(string? serverId, IPAddress from)
    {
        string ip = (from.IsIPv4MappedToIPv6 ? from.MapToIPv4() : from).ToString();
        foreach (BlockRow b in _blocks)
        {
            if ((b.Kind == "ip" && b.Value == ip) || (b.Kind == "id" && serverId is not null && b.Value == serverId))
            {
                return true;
            }
        }

        return false;
    }

    private List<BlockRow> _blocks = [];
    private double _blocksRead = double.NegativeInfinity;

    /// <summary>Re-reads the blocks every few seconds (called from the master's sweep).</summary>
    public void RefreshBlocks(double now)
    {
        if (now - _blocksRead >= 5)
        {
            _blocksRead = now;
            _blocks = _store.Blocks();
        }
    }

    private double _lastSample = double.NegativeInfinity;

    /// <summary>Keeps one players/servers sample a minute for the history (called from the master's sweep).</summary>
    public void Sample(double now)
    {
        RefreshBlocks(now);
        if (now - _lastSample < 60)
        {
            return;
        }

        _lastSample = now;
        int players = 0, servers = 0, hubs = 0;
        lock (_registry.Gate)
        {
            foreach (ServerRecord r in _registry.Snapshot())
            {
                players += r.Last.Players;
                if (r.Last.Kind == ServerKind.Hub)
                {
                    hubs++;
                }
                else
                {
                    servers++;
                }
            }
        }

        _store.AddMetric(players, servers, hubs, _registry.RelaySessionCount);
        _store.ExpireDemos();
    }
}
