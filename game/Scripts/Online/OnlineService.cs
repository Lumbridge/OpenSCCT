using System;
using System.Collections.Generic;
using Godot;
using OpenScct.Core;
using OpenScct.Net;
using OpenScct.Net.Cosmetics;
using OpenScct.Server;
using OpenScct.Server.Online;
using OpenScctGame.Hud;
using OpenScctGame.Match;
using OpenScctGame.Options;
using OpenScctGame.Play;
using OpenScctGame.Play.Session;
using OpenScctGame.UI;
using OnlinePlayer = OpenScct.Server.Online.LobbyPlayer;

namespace OpenScctGame.Online;

/// <summary>
/// The game's online play (docs/ONLINE.md, "The game client"): one node under the scene tree's root that keeps the
/// player's <see cref="OnlineClient"/> visit (and, when the player hosts, the <see cref="ListenHost"/>) across scene
/// changes, ticks the visit at 60 Hz, and moves between the front end's screens as the visit goes: connecting, the
/// lobby, the round (the play scene on the visit's session), the results, and the main menu with the reason when it
/// ends.
/// </summary>
public partial class OnlineService : Node
{
    public const string NodeName = "OnlineService";

    private const int MaxCatchUpTicks = 5;

    private readonly StopwatchClock _clock = new();
    private readonly List<string> _notes = new();
    private PlayCatalog? _catalog;
    private NetClassRegistry? _classes;
    private long _start;
    private long _ticks;
    private OnlineClientState _shownState = (OnlineClientState)(-1);
    private PlayRoot? _round;
    private ClientSession? _roundRefused;
    private Action<string?>? _hubReturn;
    private bool _hubPlayed;

    /// <summary>The visit's server on the master and its password, remembered with a round (RejoinService).</summary>
    private string? _visitMasterId;
    private string? _visitPassword;

    /// <summary>The player's visit, or null when not online.</summary>
    public OnlineClient? Client { get; private set; }

    /// <summary>The player's own server, when hosting.</summary>
    public ListenHost? Hosting { get; private set; }

    /// <summary>The last visit's error, for the main menu.</summary>
    public string? LastError { get; private set; }

    /// <summary>Lines for the lobby that come from this machine (UPnP, hosting errors).</summary>
    public IReadOnlyList<string> Notes => _notes;

    /// <summary>The visit's voice chat (lobby and rounds), made on first use.</summary>
    public OnlineVoiceChat? VoiceChat { get; private set; }

    /// <summary>Raised when the visit or the notes change (the lobby screen redraws).</summary>
    public event Action? Changed;

    /// <summary>The maps the client plays (every installed map with its play loader).</summary>
    public PlayCatalog Catalog => _catalog ??= new PlayCatalog(App.From(this).Options.MapRoots);

    /// <summary>The replicated classes, shared by the host and the client of this process.</summary>
    public NetClassRegistry Classes => _classes ??= NetClasses.Create();

    /// <summary>This build's protocol hash (the browser greys out other builds).</summary>
    public ulong ProtocolHash => Classes.ComputeProtocolHash();

    public IClock Clock => _clock;

    /// <summary>The service of <paramref name="tree"/>, made on first use.</summary>
    public static OnlineService Get(SceneTree tree)
    {
        ArgumentNullException.ThrowIfNull(tree);
        if (tree.Root.GetNodeOrNull<OnlineService>(NodeName) is { } existing)
        {
            return existing;
        }

        var service = new OnlineService { Name = NodeName };
        tree.Root.AddChild(service);
        return service;
    }

    /// <summary>The listen host's own account: its unlocks, rank and titles as the master last told this game.</summary>
    internal static HostAccount HostAccountOf(long id, string name, OpenScct.Net.Discovery.AccountInfo? info)
    {
        int rank = info?.Progress is { } p ? PlayerRank.FromXp(p.Prestige, p.Xp).Pack() : 0;
        return new HostAccount(id, name, info?.Unlocked, rank, info?.Progress?.Titles);
    }

    /// <summary>
    /// The visit's settings: a signed-in player goes by the account's name (the server checks it against the ticket),
    /// a guest by the options' name.
    /// </summary>
    private OnlineClientSettings ClientSettings(string? password = null, string? hostKey = null, string? ticket = null) => new()
    {
        PlayerName = AccountSettings.Current.AccountName ?? SettingsStore.Current.PlayerName,
        InstallId = OnlineSettings.Current.InstallId,
        Key = OnlineSettings.Current.Key,
        Password = password,
        HostKey = hostKey,
        Ticket = ticket,
        SpyLook = SettingsStore.Current.SharedLook(CosmeticSide.Spy).ToHex(),
        MercLook = SettingsStore.Current.SharedLook(CosmeticSide.Merc).ToHex(),
        Title = SettingsStore.Current.Title,
        Maps = Catalog.Maps,
        Classes = Classes,
        InstallGameplayServices = false,
        FirePrediction = SettingsStore.Current.FirePrediction,
    };

    /// <summary>
    /// Joins a server (a browser entry, a LAN answer or a typed address) and shows the connecting screen.
    /// <paramref name="ticket"/> is the master's ticket for that server when the player is signed in;
    /// <paramref name="masterId"/> its master id when the target is an address of a listed server.
    /// </summary>
    public void Join(JoinTarget target, string? password = null, string? ticket = null, string? masterId = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        AccountService.Get(GetTree()).PrepareAppearance(() => JoinPrepared(target, password, ticket, masterId));
    }

    private void JoinPrepared(JoinTarget target, string? password, string? ticket, string? masterId)
    {
        ArgumentNullException.ThrowIfNull(target);
        EndVisit();
        LastError = null;
        _notes.Clear();
        _visitMasterId = masterId ?? target.MasterId;
        _visitPassword = password;
        StartVisit(new OnlineClient(target, ClientSettings(password, ticket: ticket), _clock.NowMicroseconds));
    }

    /// <summary>True while the visit is a hub pad's match (its end goes back to the hub, not to the main menu).</summary>
    public bool InHubMatch => _hubReturn is not null;

    /// <summary>
    /// Joins a hub pad's match (docs/HUB.md "Match pads"): the session the hub opened, on the side it gave, with its
    /// join token as the password. When the round is over (the results, after the end banner), the visit fails or the
    /// player leaves, the visit ends and <paramref name="back"/> runs with a line for the hub (the hub service joins
    /// the hub again).
    /// </summary>
    public void JoinFromHub(JoinTarget target, string password, int team, Action<string?> back, string? ticket = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(back);
        AccountService.Get(GetTree()).PrepareAppearance(() => JoinFromHubPrepared(target, password, team, back, ticket));
    }

    private void JoinFromHubPrepared(JoinTarget target, string password, int team, Action<string?> back, string? ticket)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(back);
        EndVisit();
        LastError = null;
        _notes.Clear();
        _hubReturn = back;
        _hubPlayed = false;
        // Team 255 watches as a spectator; the ticket (a match on the hub's master) brings the account's name.
        int preferred = team == SessionProtocol.SpectatorTeam ? team : team == 1 ? 1 : 0;
        StartVisit(new OnlineClient(target, ClientSettings(password, ticket: ticket) with { PreferredTeam = preferred }, _clock.NowMicroseconds));
    }

    /// <summary>Ends a hub match's visit and hands back to the hub.</summary>
    private void ReturnToHub(string? note)
    {
        Action<string?>? back = _hubReturn;
        _hubReturn = null;
        EndVisit();
        GD.Print("SVM_HUB back to the hub: " + (note ?? string.Empty));
        back?.Invoke(note);
    }

    /// <summary>The hub match's own moves between states; true when it handled the state (the visit went back to the hub).</summary>
    private bool HubMatchState(OnlineClient client, OnlineClientState state)
    {
        switch (state)
        {
            case OnlineClientState.Loading or OnlineClientState.Match:
                _hubPlayed = true;
                return false;
            case OnlineClientState.Results:
            case OnlineClientState.Lobby when _hubPlayed:
                ReturnToHub("The match is over. Back at the pads.");
                return true;
            case OnlineClientState.Failed:
                ReturnToHub(_hubPlayed
                    ? "The match connection ended: " + (client.Error ?? "the server closed.")
                    : "Could not join the match: " + (client.Error ?? "no answer from its server."));
                return true;
            case OnlineClientState.Left:
                ReturnToHub("You left the match.");
                return true;
            default:
                return false;
        }
    }

    /// <summary>Hosts a game: the listen server, then the host's own player in its lobby. False with <see cref="LastError"/> on failure.</summary>
    public bool Host(HostRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        EndVisit();
        LastError = null;
        _notes.Clear();
        AccountSettings account = AccountSettings.Current;
        if (request.HostAccount is null && account.AccountName is { } claimed && account.AccountId > 0)
        {
            request = request with
            {
                HostAccount = HostAccountOf(account.AccountId, claimed, AccountService.Get(GetTree()).Account),
            };
        }

        try
        {
            Hosting = ListenHost.Start(request, ServerMaps(), Classes);
        }
        catch (InvalidOperationException e)
        {
            LastError = "Could not host: " + e.Message;
            return false;
        }

        _notes.Add($"Hosting {request.Name} on UDP port {Hosting.Port}.");
        StartVisit(new OnlineClient(Hosting.LocalTarget, ClientSettings(request.Password, Hosting.HostKey), _clock.NowMicroseconds));
        return true;
    }

    /// <summary>The catalog the listen server loads with: the server's own loaders over the same folders.</summary>
    private MapCatalog ServerMaps() => new([.. Catalog.Maps.Roots]);

    private void StartVisit(OnlineClient client)
    {
        Client = client;
        if (VoiceChat is null)
        {
            VoiceChat = new OnlineVoiceChat { Name = OnlineVoiceChat.NodeName };
            AddChild(VoiceChat);
        }

        VoiceChat.Attach(client, _clock);
        _start = _clock.NowMicroseconds;
        _ticks = 0;
        _shownState = (OnlineClientState)(-1);
        client.Changed += OnChanged;
        client.SessionEnding += OnSessionEnding;
        FrontEnd.ShowConnecting(GetTree(), this);
    }

    /// <summary>Leaves the visit (and stops hosting), back to the main menu.</summary>
    public void Leave()
    {
        if (_hubReturn is not null)
        {
            ReturnToHub("You left the match.");
            return;
        }

        RejoinService.Get(GetTree()).LeftByChoice();
        EndVisit();
        FrontEnd.ShowMainMenu(GetTree());
    }

    private void EndVisit()
    {
        if (Client is { } client)
        {
            client.Changed -= OnChanged;
            client.SessionEnding -= OnSessionEnding;
            StopRound();
            VoiceChat?.Detach();
            client.Leave();
            client.Dispose();
            Client = null;
        }

        if (Hosting is { } host)
        {
            Hosting = null;
            host.Dispose();
        }
    }

    public override void _Process(double delta)
    {
        if (Hosting is { } host)
        {
            while (host.TryTakeNote(out string? note))
            {
                _notes.Add(note);
                Changed?.Invoke();
            }

            if (host.Failure is { } failure && Client is not null)
            {
                Fail("Your server stopped: " + failure.Message);
                return;
            }
        }

        if (Client is not { } client)
        {
            return;
        }

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

        if (Client is not null)
        {
            ShowState(client);
            if (client.State is OnlineClientState.Loading or OnlineClientState.Match && _round is null && client.Session is { } session)
            {
                EnterRound(client, session);
            }
        }
    }

    private void OnChanged() => Changed?.Invoke();

    /// <summary>Moves to the screen of the visit's state, once per change.</summary>
    private void ShowState(OnlineClient client)
    {
        OnlineClientState state = client.State;
        if (state == _shownState)
        {
            return;
        }

        _shownState = state;
        if (_hubReturn is not null && HubMatchState(client, state))
        {
            return;
        }

        SceneTree tree = GetTree();
        if (Hosting is null && _hubReturn is null)
        {
            // A round of someone else's server is remembered, so the game can offer to rejoin it after a drop.
            RejoinService.Get(tree).Follow(client, state, _visitMasterId, _visitPassword);
        }
        switch (state)
        {
            case OnlineClientState.Lobby:
                StopRound();
                FrontEnd.ShowLobby(tree, new OnlineLobby(this, client));
                break;
            case OnlineClientState.Results when client.Results is { } results:
                StopRound();
                FrontEnd.ShowResults(tree, OnlineLobby.ToMatchResult(client, results), client);
                break;
            case OnlineClientState.Failed:
                Fail(client.Error ?? "The connection ended.");
                break;
            case OnlineClientState.Left:
                Leave();
                break;
            case OnlineClientState.Travelling:
                StopRound();
                FrontEnd.ShowConnecting(tree, this);
                break;
        }
    }

    private void Fail(string error)
    {
        LastError = error;
        EndVisit();
        MainMenu menu = FrontEnd.ShowMainMenu(GetTree());
        menu.ShowStatus(error);
    }

    /// <summary>
    /// The visit is on a round's map (after a travel, or joining a running round as a spectator): the play scene shows
    /// it and ticks it with the local input.
    /// </summary>
    private void EnterRound(OnlineClient client, ClientSession session)
    {
        if (client.CurrentMap is not { } map || MapCatalog.IsMenuMap(map) || !Catalog.CanRender(map) || _roundRefused == session)
        {
            // Without render content the round still plays on, headless; the connecting page says so.
            _roundRefused = session;
            return;
        }

        OnlinePlayer? local = client.Lobby.Local;
        var settings = new PlaySettings
        {
            Map = map,
            Side = local?.Team == 1 ? PlaySide.Merc : PlaySide.Spy,
            Mode = MapCatalog.ModeKey(client.Lobby.Setup.Mode),
            PlayerName = SettingsStore.Current.PlayerName,
            MapRoots = [.. App.From(this).Options.MapRoots],
        };
        PlayMapLoader loader = Catalog.Loader(map);
        string folder = Catalog.Maps.FindFolder(map)!;
        var config = new PlayConfig
        {
            Settings = settings,
            GameRoot = App.From(this).Options.GameRoot,
            // The play scene ticks the connection on this service's clock, the one it was ticked on so far: a fresh
            // clock would start behind it, and the driver would see time go backwards.
            CreateSession = _ => new PlaySession(settings, _clock, session, Catalog.Maps, folder, loader),
        };
        client.SessionTickedElsewhere = true;
        _round = PlayRoot.Enter(GetTree(), config);

        // Loading_Menu's list: who is ready, still loading or not in yet, until the round starts.
        _round.AddChild(new RoundStartList(client));
        if (VoiceChat is not null)
        {
            VoiceChat.ShowOverlay = true;
        }
    }

    /// <summary>The visit is about to close a session: a play scene on it stops first.</summary>
    private void OnSessionEnding(ClientSession session) => StopRound();

    private void StopRound()
    {
        if (_round is { } round)
        {
            _round = null;
            round.Session?.Stop();
        }

        if (VoiceChat is not null)
        {
            VoiceChat.ShowOverlay = false;
        }

        if (Client is { } client)
        {
            client.SessionTickedElsewhere = false;
        }
    }

    public override void _ExitTree() => EndVisit();
}
