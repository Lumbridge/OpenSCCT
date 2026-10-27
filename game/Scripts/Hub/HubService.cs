using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using OpenScct.Core;
using OpenScct.Net;
using OpenScct.Net.Discovery;
using OpenScct.Server;
using OpenScct.Server.Hub;
using OpenScct.Server.Online;
using OpenScctGame.Match;
using OpenScctGame.Online;
using OpenScctGame.Play;
using OpenScctGame.Play.Session;
using OpenScctGame.UI;

namespace OpenScctGame.Hub;

/// <summary>
/// The game's hub visit (an OpenSCCT addition, docs/HUB.md): one node under the scene tree's root that keeps the
/// player's <see cref="HubClient"/> across scenes, ticks it at 60 Hz until the hub map and the player's controller
/// are in, then shows the play scene on the visit's session with the hub's HUD (names over heads, chat box, side and
/// respawn keys). A refusal or a lost connection goes back to the main menu with the reason.
/// </summary>
public partial class HubService : Node
{
    public const string NodeName = "HubService";

    /// <summary>The hub's default game port (the dedicated server's).</summary>
    public const int DefaultPort = HubK.DefaultPort;

    private const int MaxCatchUpTicks = 5;

    private readonly StopwatchClock _clock = new();
    private PlayCatalog? _catalog;
    private NetClassRegistry? _classes;
    private long _start;
    private long _ticks;
    private PlayRoot? _round;
    private HubHud? _hud;
    private HubVoiceChat? _voice;
    private string _target = string.Empty;
    private string _name = "Player";
    private int _team;
    private HubSignsView? _signs;
    private HubPadView? _padView;
    private long _noteUntil;

    // A hub from the master's list: its id, the ways in still to try, and the master's answer being waited for.
    private readonly Queue<JoinAttempt> _attempts = new();
    private string? _masterId;
    private string? _masterName;
    private string? _matchHost;
    private string? _returnPad;
    private MasterClient? _master;
    private string? _ticket;
    private (Task<JoinResponse> Answer, OpenScct.Server.Hub.HubMatchOffer Offer, string Address)? _matchJoin;
    private Task<JoinResponse>? _pendingJoin;

    /// <summary>The visit, or null when not in a hub.</summary>
    public HubClient? Client { get; private set; }

    /// <summary>The visit's voice chat (proximity voice), or null before the first visit.</summary>
    public HubVoiceChat? VoiceChat => _voice;

    /// <summary>What is happening, for the hub screen.</summary>
    public string Status { get; private set; } = string.Empty;

    /// <summary>A line about the last pad match for the hub HUD ("Joining the match...", "The match is over"), or null.</summary>
    public string? MatchNote => _clock.NowMicroseconds < _noteUntil ? _matchNote : null;

    private string? _matchNote;

    /// <summary>Why the last visit ended, for the main menu, or null.</summary>
    public string? LastError { get; private set; }

    public PlayCatalog Catalog => _catalog ??= new PlayCatalog(App.From(this).Options.MapRoots);

    /// <summary>The replicated classes (the online service's when it made them, so one process has one protocol).</summary>
    public NetClassRegistry Classes => _classes ??= OnlineService.Get(GetTree()).Classes;

    public static HubService Get(SceneTree tree)
    {
        ArgumentNullException.ThrowIfNull(tree);
        if (tree.Root.GetNodeOrNull<HubService>(NodeName) is { } existing)
        {
            return existing;
        }

        var service = new HubService { Name = NodeName };
        tree.Root.AddChild(service);
        return service;
    }

    /// <summary>"host" or "host:port" with the default port added.</summary>
    public static string Normalise(string address)
    {
        string a = (address ?? string.Empty).Trim();
        if (a.Length == 0)
        {
            throw new ArgumentException("Enter the hub's address.");
        }

        int colon = a.LastIndexOf(':');
        bool hasPort = colon > 0 && a.IndexOf(':') == colon && int.TryParse(a[(colon + 1)..], out _);
        return hasPort ? a : a + ":" + DefaultPort.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>The "host:port" the visit connects to (empty when not in a hub).</summary>
    public string Address => Client is null ? string.Empty : _target;

    /// <summary>The hub this game hosts (Host a hub on this PC), or null.</summary>
    public HubHost? Host { get; private set; }

    /// <summary>Joins the hub at <paramref name="address"/> as <paramref name="name"/> on <paramref name="team"/> (0 spies, 1 mercs).</summary>
    public void Join(string address, string name, int team)
    {
        Leave(showMenu: false);
        LastError = null;
        ForgetListed();
        Connect(Normalise(address), name, team);
    }

    /// <summary>
    /// Joins a hub from the master's hub list (<paramref name="serverId"/>): asks the master how to reach it, then
    /// tries its own network address (same router), its public address and the master's relay in turn, as the game
    /// browser does (a NAT punch is left out: the relay covers those hosts).
    /// </summary>
    public void JoinListed(string serverId, string displayName, string name, int team)
    {
        ArgumentException.ThrowIfNullOrEmpty(serverId);
        Leave(showMenu: false);
        LastError = null;
        ForgetListed();
        _masterId = serverId;
        _masterName = displayName;
        _name = name;
        _team = team;
        AskMaster(returnPad: null);
    }

    /// <summary>A visit is connecting or in, or the master is being asked the way to a listed hub.</summary>
    public bool Busy => Client is not null || _pendingJoin is not null;

    /// <summary>The master's hub list (game servers are listed apart), or null with no master set.</summary>
    public Task<ServerListResponse>? ListHubs() => Master()?.ListAsync(kind: ServerKind.Hub);

    private MasterClient? Master()
    {
        if (_master is not null)
        {
            return _master;
        }

        OnlineSettings s = OnlineSettings.Current;
        if (s.MasterUrl.Length == 0)
        {
            return null;
        }

        try
        {
            _master = new MasterClient(s.MasterUrl, s.InviteKey.Length > 0 ? s.InviteKey : null);
        }
        catch (ArgumentException e)
        {
            LastError = "Hub: the master server address is not valid: " + e.Message;
        }

        return _master;
    }

    private void AskMaster(string? returnPad)
    {
        if (Master() is not { } master || _masterId is null)
        {
            LastError ??= "Hub: no master server is set (Online settings).";
            Leave();
            return;
        }

        _returnPad = returnPad;
        // Signed in, the master's answer carries the player's ticket for the hub: he comes in under his account's name.
        master.SessionToken = AccountSettings.Current.SignedIn ? AccountSettings.Current.Token : null;
        _ticket = null;
        _pendingJoin = master.JoinAsync(_masterId);
        Status = $"Asking the master server the way to {_masterName}...";
    }

    private void Answered(Task<JoinResponse> answer)
    {
        if (!answer.IsCompletedSuccessfully)
        {
            LastError = "Hub: " + (answer.Exception?.InnerException?.Message ?? "the master server did not answer.");
            Leave();
            return;
        }

        JoinResponse join = answer.Result;
        _ticket = join.Ticket is { Length: > 0 } ticket ? ticket : null;
        if (_ticket is null && join.TicketProblem is { Length: > 0 } problem && AccountSettings.Current.SignedIn)
        {
            GD.Print("SVM_HUB no ticket: " + problem);
        }

        _attempts.Clear();
        foreach (JoinAttempt a in JoinTarget.FromJoin(join, OnlineSettings.Current.Path).Attempts)
        {
            if (a.Kind is ConnectKind.Direct or ConnectKind.Relay)
            {
                _attempts.Enqueue(a);
            }
        }

        // The pads' matches run on the hub's own machine: at its public address, not the relay's.
        _matchHost = join.Direct is { Length: > 0 } direct ? HostOf(direct) : null;
        if (!TryNextAttempt())
        {
            LastError = $"Hub: the master server knows no way to reach {_masterName}.";
            Leave();
        }
    }

    /// <summary>Connects with the next way in of a listed hub; false when none is left.</summary>
    private bool TryNextAttempt()
    {
        if (!_attempts.TryDequeue(out JoinAttempt? attempt))
        {
            return false;
        }

        if (Client is { } old)
        {
            Client = null;
            _voice?.Detach();
            old.Session.Disconnect();
            old.Dispose();
        }

        GD.Print("SVM_HUB trying " + attempt.Describe());
        Connect(attempt.Address, _name, _team, _returnPad);
        Status = $"Connecting to {_masterName} ({attempt.Describe()})...";
        return true;
    }

    private void ForgetListed()
    {
        _masterId = null;
        _masterName = null;
        _matchHost = null;
        _returnPad = null;
        _ticket = null;
        _attempts.Clear();
    }

    /// <summary>
    /// Opens a hub on this PC (UDP <paramref name="port"/>, every interface, so LAN and direct-IP players can join) and
    /// joins it. False with <see cref="LastError"/> set when it could not (the port is in use, the hub map is missing).
    /// </summary>
    public bool HostAndJoin(int port, string name, int team)
    {
        Leave(showMenu: false);
        LastError = null;
        ForgetListed();
        try
        {
            // Listed in the master's hub list unless the player turned that off (others then join by address).
            OnlineSettings s = OnlineSettings.Current;
            MasterLinkSettings? master = s.HubListed && s.MasterUrl.Length > 0
                ? new MasterLinkSettings { Url = s.MasterUrl, InviteKey = s.InviteKey.Length > 0 ? s.InviteKey : null }
                : null;
            Host = HubHost.Start(port, string.IsNullOrWhiteSpace(name) ? "OpenSCCT Hub" : name.Trim() + "'s hub",
                new MapCatalog([.. Catalog.Maps.Roots]), Classes, master);
        }
        catch (InvalidOperationException e)
        {
            LastError = "Could not host a hub: " + e.Message;
            return false;
        }

        Connect(Host.LocalAddress, name, team);
        return true;
    }

    private void Connect(string target, string name, int team, string? returnPad = null)
    {
        _name = name;
        _team = team;
        var transport = LiteNetLibTransport.CreateClient();
        _start = _clock.NowMicroseconds;
        _ticks = 0;
        _target = target;
        Client = new HubClient(transport, target, _start, new HubClientSettings
        {
            PlayerName = name,
            Team = team,
            Maps = Catalog.Maps,
            Classes = Classes,
            InstallGameplayServices = false,
            ReturnPad = returnPad,
            // The ticket is for the listed hub only (a hosted or typed-in hub gets none).
            Ticket = _masterId is not null ? _ticket : null,
            InstallId = OnlineSettings.Current.InstallId,
            Title = OpenScctGame.Options.SettingsStore.Current.Title,
        }, ownsTransport: true);
        Client.Play.Clock = () => _clock.NowMicroseconds / 1_000_000.0;
        if (_voice is null)
        {
            _voice = new HubVoiceChat { Name = HubVoiceChat.NodeName };
            AddChild(_voice);
        }

        _voice.Ear = Ear;
        // The HUD goes with the play scene; voice chat can outlive it by a frame after leaving.
        _voice.KeysBlocked = () => _hud is { } hud && GodotObject.IsInstanceValid(hud) && hud.Typing;
        _voice.Attach(Client, _clock);
        Status = $"Connecting to {target}...";
        GD.Print($"SVM_HUB connecting to {target}");
    }

    /// <summary>
    /// Leaves the hub (and closes the hub this game hosts): back to the main menu from the hub, or to the hub page
    /// with the reason when the visit never got in.
    /// </summary>
    public void Leave(bool showMenu = true)
    {
        bool wasIn = _round is not null;
        _pendingJoin = null;
        _matchJoin = null;
        _attempts.Clear();
        if (_round is { } round)
        {
            _round = null;
            round.Session?.Stop();
        }

        _hud = null;
        _signs = null;
        _padView = null;
        _voice?.Detach();
        if (Client is { } client)
        {
            Client = null;
            client.Session.Disconnect();
            client.Dispose();
        }

        if (Host is { } host)
        {
            Host = null;
            host.Dispose();
        }

        if (!showMenu)
        {
            return;
        }

        if (!wasIn && LastError is not null)
        {
            HubScreen.Show(GetTree());
            return;
        }

        MainMenu menu = FrontEnd.ShowMainMenu(GetTree());
        if (LastError is { } error)
        {
            menu.ShowStatus(error);
        }
    }

    public override void _Process(double delta)
    {
        if (_pendingJoin is { IsCompleted: true } answer)
        {
            _pendingJoin = null;
            Answered(answer);
        }

        if (_matchJoin is { Answer.IsCompleted: true } m)
        {
            _matchJoin = null;
            MatchAnswered(m.Answer, m.Offer, m.Address);
        }

        if (Client is not { } client)
        {
            return;
        }

        if (Host is { Failure: { } failure })
        {
            LastError = "Hub: the hub this PC hosts stopped: " + failure.Message;
            GD.PushError(LastError);
            Leave();
            return;
        }

        if (client.Error is { } failed && !client.Welcomed && client.Session.State != EClientState.Rejected && TryNextAttempt())
        {
            GD.Print($"SVM_HUB that way failed ({failed}), trying the next");
            return;
        }

        if (client.Error is { } error)
        {
            // A hub that never answered: the connection timed out before the welcome (nothing listens there).
            LastError = client.Welcomed || client.Session.State == EClientState.Rejected
                ? "Hub: " + error
                : $"No hub answered at {_masterName ?? _target}. Check that a hub is running there (svm-server --hub) and that its UDP port "
                  + "(default " + DefaultPort.ToString(System.Globalization.CultureInfo.InvariantCulture) + ") is open, or press \"Host a hub on this PC\".";
            GD.Print("SVM_HUB " + LastError);
            Leave();
            return;
        }

        if (client.Offer is { } offer)
        {
            GoToMatch(client, offer);
            return;
        }

        if (!client.SessionTickedElsewhere)
        {
            long now = _clock.NowMicroseconds;
            long due = (now - _start) * Sim.Hz / 1_000_000;
            if (due - _ticks > MaxCatchUpTicks)
            {
                _ticks = due - MaxCatchUpTicks;
            }

            while (_ticks < due && Client is not null)
            {
                client.Tick(_start + (_ticks + 1) * 1_000_000 / Sim.Hz);
                _ticks++;
            }
        }

        if (_round is null && client.Session.Map is { } map && client.LocalPlayer is not null)
        {
            Enter(client, map.Name);
        }
        else if (_round is null)
        {
            long waited = (_clock.NowMicroseconds - _start) / 1_000_000;
            Status = client.Welcomed
                ? $"Loading {client.ServerName}..."
                : string.Create(System.Globalization.CultureInfo.InvariantCulture,
                    $"Connecting to {_target}... {waited} s (gives up after {(int)OpenScct.Net.K.InitialConnectTimeout} s)");
        }
    }

    private void Enter(HubClient client, string map)
    {
        if (!Catalog.CanRender(map))
        {
            Status = $"{map} has no render content in this install.";
            return;
        }

        var settings = new PlaySettings
        {
            Map = map,
            Side = client.Team == 1 ? PlaySide.Merc : PlaySide.Spy,
            Mode = "Story",
            PlayerName = client.Settings.PlayerName,
            MapRoots = [.. App.From(this).Options.MapRoots],
        };
        PlayMapLoader loader = Catalog.Loader(map);
        string folder = Catalog.Maps.FindFolder(map)!;
        ClientSession session = client.Session;
        var config = new PlayConfig
        {
            Settings = settings,
            GameRoot = App.From(this).Options.GameRoot,
            CreateSession = clock => new PlaySession(settings, clock, session, Catalog.Maps, folder, loader),
        };
        client.SessionTickedElsewhere = true;
        _round = PlayRoot.Enter(GetTree(), config);
        _hud = new HubHud { Client = client, Round = _round, Service = this };
        _round.AddChild(_hud);

        // The activities' world props and sounds (the course gates, targets, ball, jukebox music, chimes).
        _round.AddChild(new HubProps { Client = client, Round = _round });
        var audio = new HubAudio { Play = client.Play, Ear = Ear };
        _round.AddChild(audio);
        client.Play.Cue += audio.Cue;
        client.Play.Fanfare += _ => audio.Cue("fanfare");
        // Emotes posed on the pawns (the overlay reads the visit's play state, so it does nothing once the visit ends).
        OpenScctGame.Views.SimPawnView.Overlay = new HubEmotePoses(() => Client?.Play);
        _signs = new HubSignsView();
        _round.AddChild(_signs);
        _signs.Client = client;
        _padView = new HubPadView { Maps = Catalog.Maps, Signs = _signs };
        _round.AddChild(_padView);
        _padView.Client = client;
        Status = Host is { } host
            ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"In {client.ServerName}, hosted on this PC (UDP port {host.Port})")
            : $"In {client.ServerName}";
    }

    /// <summary>
    /// The hub sent this player to a pad's match: the hub visit ends (a hub this PC hosts keeps running, with its
    /// matches), and the online service joins the match session at the hub's address (or the one the hub named) on the
    /// session's port, with the side and token the hub gave. At the match's end the player comes back here, beside
    /// the pad (<see cref="ReturnFromMatch"/>).
    /// </summary>
    private void GoToMatch(HubClient client, OpenScct.Server.Hub.HubMatchOffer offer)
    {
        string host = offer.Host ?? _matchHost ?? HostOf(_target);
        string address = (host.Contains(':', StringComparison.Ordinal) && !host.StartsWith('[') ? "[" + host + "]" : host) + ":" +
            offer.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        string role = offer.Spectator ? "spectator" : offer.Team == 1 ? "merc" : "spy";
        GD.Print($"SVM_HUB match of {offer.PadId}: joining {address} as {role}{(offer.MasterId is { } mid ? " (master entry " + mid + ")" : string.Empty)}");
        if (_round is { } round)
        {
            _round = null;
            round.Session?.Stop();
        }

        _hud = null;
        _signs = null;
        _padView = null;
        _voice?.Detach();
        Client = null;
        client.Session.Disconnect();
        client.Dispose();
        string what = offer.Spectator ? "Watching" : "Joining";
        Note($"{what} the match of {offer.PadId}...");
        Status = $"{what} the match of {offer.PadId} at {address}...";
        string padId = offer.PadId;
        if (offer.MasterId is { } matchId && Master() is { } master)
        {
            // The match has its own master entry: asked like a listed server (its address, its punch and relay
            // routes, the player's ticket), with the hub's address to fall back on.
            master.SessionToken = AccountSettings.Current.SignedIn ? AccountSettings.Current.Token : null;
            _matchJoin = (master.JoinAsync(matchId), offer, address);
            return;
        }

        OnlineService.Get(GetTree()).JoinFromHub(OpenScct.Server.Online.JoinTarget.FromAddress(address), offer.Password, offer.Team,
            note => ReturnFromMatch(padId, note));
    }

    /// <summary>The master answered the way into a pad's match: joins by its routes, or at the hub's address when it did not.</summary>
    private void MatchAnswered(Task<JoinResponse> answer, OpenScct.Server.Hub.HubMatchOffer offer, string address)
    {
        string padId = offer.PadId;
        OpenScct.Server.Online.JoinTarget target = OpenScct.Server.Online.JoinTarget.FromAddress(address);
        string? ticket = null;
        if (answer.IsCompletedSuccessfully && answer.Result is { } join)
        {
            target = OpenScct.Server.Online.JoinTarget.FromJoin(join, OnlineSettings.Current.Path);
            ticket = join.Ticket is { Length: > 0 } t ? t : null;
        }
        else
        {
            GD.Print("SVM_HUB the master did not answer for the match; joining at " + address);
        }

        OnlineService.Get(GetTree()).JoinFromHub(target, offer.Password, offer.Team, note => ReturnFromMatch(padId, note), ticket);
    }

    /// <summary>Back from a pad's match: joins the hub again (spawned beside the pad) with a note on the HUD.</summary>
    public void ReturnFromMatch(string padId, string? note)
    {
        Note(note ?? "Back from the match.");
        if (_masterId is not null)
        {
            // A listed hub: ask the master again (a relay route does not outlive the match).
            HubScreen.Show(GetTree());
            AskMaster(padId);
            return;
        }

        if (_target.Length == 0)
        {
            FrontEnd.ShowMainMenu(GetTree());
            return;
        }

        Connect(_target, _name, _team, padId);
        HubScreen.Show(GetTree());
    }

    private void Note(string text)
    {
        _matchNote = text;
        _noteUntil = _clock.NowMicroseconds + 20_000_000;
    }

    /// <summary>The host part of "host:port" or "[v6]:port".</summary>
    private static string HostOf(string target)
    {
        if (target.StartsWith('[') && target.IndexOf(']', StringComparison.Ordinal) is > 0 and int end)
        {
            return target[1..end];
        }

        int colon = target.LastIndexOf(':');
        return colon > 0 ? target[..colon] : target;
    }

    /// <summary>The listener's ear for proximity voice: the play scene's camera, or null before it is in.</summary>
    private FVector? Ear() =>
        _round is { IsLoaded: true, View.Camera: { } camera } ? UeSpace.ToUe(camera.GlobalPosition) : null;

    public override void _ExitTree()
    {
        Leave(showMenu: false);
        _master?.Dispose();
        _master = null;
    }
}
