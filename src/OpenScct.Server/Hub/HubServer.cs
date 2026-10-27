using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Modes;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Net.Discovery;
using OpenScct.Net.Voice;
using OpenScct.Net.Voice.Proximity;
using OpenScct.Server.Online;

namespace OpenScct.Server.Hub;

/// <summary>Settings of a hub server.</summary>
public sealed record HubSettings
{
    public string Name { get; init; } = "OpenSCCT Hub";

    public string Map { get; init; } = HubK.MapName;

    public int MaxPlayers { get; init; } = HubK.DefaultMaxPlayers;

    /// <summary>Replication passes per second (20 internet, 35 LAN), as for a match.</summary>
    public int NetRate { get; init; } = OpenScct.Net.K.NetServerMaxTickRate;

    public ulong Seed { get; init; } = 1;

    /// <summary>Shown to each player who joins, in the chat box.</summary>
    public string? Motd { get; init; }

    public MapCatalog? Maps { get; init; }

    public NetClassRegistry? Classes { get; init; }

    /// <summary>Distance-based relevancy and update rates (docs/HUB.md); off replicates as a normal match does.</summary>
    public bool InterestManagement { get; init; } = true;

    public DistanceInterestSettings Interest { get; init; } = new();

    public NetUpdateGate UpdateGate { get; init; } = NetUpdateGate.DecodedGuarded;

    /// <summary>The original's -lanplay (LAN tick cap and net speed rows).</summary>
    public bool LanPlay { get; init; }

    /// <summary>Relay voice chat on the hub's transport (docs/HUB.md): Open mode, each talker heard only in his range.</summary>
    public bool Voice { get; init; } = true;

    /// <summary>The proximity voice distances (falloff, hearing range, relay hysteresis).</summary>
    public ProximityVoiceSettings ProximityVoice { get; init; } = new();

    /// <summary>The activities (course, aim range, emotes, jukebox, plaza ball; <see cref="HubActivities"/>).</summary>
    public bool Activities { get; init; } = true;

    /// <summary>The scores file the boards are kept in (JSON); null keeps them for the session only.</summary>
    public string? ScoresPath { get; init; }

    /// <summary>The hub map's area manifest; null looks for <c>areas.json</c> by the map's folder, else the built-in layout.</summary>
    public string? AreasPath { get; init; }
    /// <summary>The pads' matches and places; null loads <see cref="HubConfig.Load"/> (fixtures/hubmap/hub.json, else the built-in pads).</summary>
    public HubConfig? Config { get; init; }

    /// <summary>A hub.json to load instead of the one found beside the hub map.</summary>
    public string? ConfigPath { get; init; }

    /// <summary>Run the match pads (spec/additions/hub.md "Match pads").</summary>
    public bool MatchPads { get; init; } = true;

    /// <summary>Starts the pads' matches; null: <see cref="HubMatchLauncher"/> in this process (needs <see cref="Maps"/>).</summary>
    public IHubMatchLauncher? MatchLauncher { get; init; }

    /// <summary>The bots of the pads' matches (OpenScct.Bots' <c>OnlineServerBots.Factory</c>); null: matches have no bots.</summary>
    public OnlineBotFactory? MatchBots { get; init; }

    /// <summary>Makes the wall clock of each pad match the default launcher runs (the host's own clock); null: no default launcher.</summary>
    public Func<IClock>? MatchClock { get; init; }

    /// <summary>The pads' matches listen on loopback only (tests and local diagnostics).</summary>
    public bool MatchLoopbackOnly { get; init; }

    /// <summary>
    /// The master to list the hub on (as a hub, apart from game servers), or null: players join by address. Needs the
    /// hub's transport to be a <see cref="LiteNetLibTransport"/> (the heartbeat, punches and relay go from its socket).
    /// </summary>
    public MasterLinkSettings? Master { get; init; }

    /// <summary>A dedicated hub (svm-server), not one a game hosts: the list says so.</summary>
    public bool Dedicated { get; init; }
}

/// <summary>One hub player's own state, kept on its <see cref="PlayerSlot.Tag"/>.</summary>
public sealed class HubPlayer
{
    internal HubPlayer(PlayerSlot slot) => Slot = slot;

    public PlayerSlot Slot { get; }

    /// <summary>Level time of the last side change or respawn the player asked for.</summary>
    public float LastRequest { get; internal set; } = float.NegativeInfinity;

    /// <summary>Level time since when the player has had no living pawn, or -1.</summary>
    public float PawnlessSince { get; internal set; } = -1f;

    /// <summary>Spawns the hub gave this player (the first one, side changes, respawns).</summary>
    public int Spawns { get; internal set; }

    /// <summary>The pad the player stands on (and is queued at), or null.</summary>
    public HubPad? Pad { get; internal set; }

    /// <summary>Level time the player was sent to a match, or -1 (he is not queued again until he leaves or a while passed).</summary>
    public float DepartedAt { get; internal set; } = -1f;

    /// <summary>Back from this pad's match: put beside it once his pawn is in on his client too, or null.</summary>
    internal HubPad? ReturnPad { get; set; }

    internal float ReturnAt { get; set; }

    /// <summary>
    /// The pawn a side change or respawn took away, until his client has let go of it (the new pawn is spawned
    /// then), or null.
    /// </summary>
    internal Pawn? Releasing { get; set; }

    internal float ReleasingSince { get; set; }

    /// <summary>True while the player waits for his new pawn after a side change or respawn.</summary>
    public bool AwaitingSpawn => Releasing is not null;

    /// <summary>The last <c>hub queue</c> line he was sent.</summary>
    internal string QueueLine { get; set; } = string.Empty;
}

/// <summary>
/// A hub server: an OpenSCCT addition (spec/additions/hub.md). A <see cref="ServerHost"/> on the hub map with
/// <see cref="SHubGameInfo"/>'s never-ending match, the hub <see cref="PlayRules"/> (no damage, no attacks on pawns)
/// and, by default, interest management on the driver (<see cref="DistanceInterest"/>); players join directly (no
/// lobby), pick a side at login or later, chat, and respawn at the plaza. Single-threaded: call <see cref="Tick"/> or
/// <see cref="RunRealtime"/> from one thread.
/// </summary>
public sealed class HubServer : IDisposable
{
    private readonly List<HubPlayer> _players = new();
    private readonly List<HubPlayer> _respawn = new();
    private readonly List<HubPlayer> _spawn = new();
    private readonly Dictionary<NetConnection, string> _returning = new();

    /// <summary>Seconds after a returning player joined before he is moved beside his pad.</summary>
    private const float ReturnDelaySeconds = 1.5f;
    private readonly LiteNetLibTransport? _udp;
    private readonly AnswerLimiter _answers = new();
    private readonly byte[] _infoBuffer = new byte[DiscoveryPackets.MaxBytes];
    private readonly byte[] _probeBuffer = new byte[DiscoveryPackets.HeaderBytes];
    private int _signTick;
    private IClock? _realtime;
    private long _now;
    private bool _disposed;

    /// <summary>The hub's time as the voice relay reads it: the current tick's, or the wall clock of <see cref="RunRealtime"/>.</summary>
    private sealed class HubClock(HubServer hub) : IClock
    {
        public long NowMicroseconds => hub._realtime?.NowMicroseconds ?? hub._now;
    }

    public HubServer(ITransport transport, HubSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(transport);
        Settings = settings ?? new HubSettings();
        if (Settings.MaxPlayers < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(settings), Settings.MaxPlayers, "MaxPlayers must be at least 1.");
        }

        Interest = Settings.InterestManagement ? new DistanceInterest(Settings.Interest) : null;
        _udp = transport as LiteNetLibTransport;

        // Voice rides on the hub's own transport, as in a match (docs/VOICE.md): the relay works in the driver's receive pass.
        VoiceTransport? voice = Settings.Voice ? new VoiceTransport(transport) : null;
        ITransport carrier = voice ?? transport;
        string options = HubK.GameOptions + "?MaxPlayers=" + Settings.MaxPlayers.ToString(CultureInfo.InvariantCulture);
        Host = new ServerHost(carrier, new ServerSettings
        {
            Map = Settings.Map,
            MaxPlayers = Settings.MaxPlayers,
            NetRate = Settings.NetRate,
            MatchSeed = Settings.Seed,
            Maps = Settings.Maps,
            Classes = Settings.Classes,
            Net = new NetDriverSettings
            {
                UpdateGate = Settings.UpdateGate,
                LanPlay = Settings.LanPlay,
                IsLan = Settings.LanPlay,
                Interest = Interest,
                // The game's one shared send budget would split one client's rate between fifty clients: each
                // connection pays for its own sends, as stock UE2 does.
                Budget = SendBudgetMode.PerConnection,
                MaxConnections = Settings.MaxPlayers + 8,
            },
            GameOptions = options,
            PlayersToStart = 1,
            UseStartGate = false,
            GameFactory = static (level, url) =>
            {
                string error = string.Empty;
                return GameInfo.SpawnGameInfo<SHubGameInfo>(level, url, ref error);
            },
        });
        Game = (SHubGameInfo)Host.Game!;
        Host.World.Level.SetService(PlayRules.Hub);
        Host.LoginFilter = FilterLogin;
        Host.PlayerJoined += OnJoined;
        Host.PlayerLeft += OnLeft;
        Host.SessionLineReceived += OnSessionLine;
        Game.StartHub();
        if (Settings.Activities)
        {
            string? areas = Settings.AreasPath ?? HubLayout.FindManifest(Settings.Maps?.FindFolder(Settings.Map));
            Activities = new HubActivities(this, HubLayout.Load(areas), HubScores.Open(Settings.ScoresPath),
                () => _realtime?.NowMicroseconds ?? _now, Settings.Seed);
        }

        Signs = new HubSigns(Settings.Interest.CullDistance, Settings.Interest.KeepDistance);
        if (Activities is { } activities)
        {
            // The leaderboards stand in the world as signs at their anchors, for everyone in range to read.
            activities.BoardPublished += (id, text) => Signs.Set(id, activities.BoardAt(id), text, face: activities.BoardFace(id));
            foreach (string id in HubActivities.BoardIds)
            {
                if (activities.BoardText(id) is { Length: > 0 } text)
                {
                    Signs.Set(id, activities.BoardAt(id), text, face: activities.BoardFace(id));
                }
            }
        }

        if (Settings.MatchPads)
        {
            HubConfig config = Settings.Config is { } given
                ? (Settings.Maps is { } m ? given.Checked(m, LogLine) : given)
                : HubConfig.Load(Settings.Maps, Settings.ConfigPath, LogLine);
            IHubMatchLauncher? launcher = Settings.MatchLauncher ??
                (Settings.Maps is { } maps && Settings.MatchClock is { } clock
                    ? new HubMatchLauncher(maps, Settings.Classes, Settings.MatchBots, config, clock, Settings.MatchLoopbackOnly)
                    : null);
            Pads = new HubPads(this, config, launcher);
            PadsSummary = $"match pads from {config.Source}: {Pads.Pads.Count} pad(s){(launcher is null ? ", no match launcher (pads off)" : string.Empty)}";
        }
        if (_udp is not null)
        {
            // Browsers' queries and the master's reachability probes arrive on the game socket, as on a game server.
            _udp.Unconnected = OnUnconnected;
            if (Settings.Master is { Url.Length: > 0 } master)
            {
                Master = new MasterLink(master with { Kind = ServerKind.Hub }, _udp);
                Master.Notice += LogLine;
            }
        }

        if (voice is not null)
        {
            VoicePositions = new HubVoicePositions(Host.World);
            Proximity = new ProximityRelayFilter(VoicePositions, Settings.ProximityVoice);
            VoiceRelay = new VoiceRelay(voice, new NetDriverVoiceDirectory(Host.Driver, new SessionVoiceLinks()), new HubClock(this))
            {
                Proximity = Proximity,
            };
        }
    }

    public HubSettings Settings { get; }

    /// <summary>The hub's registration with the master, or null when it is not listed.</summary>
    public MasterLink? Master { get; }

    public ServerHost Host { get; }

    public SHubGameInfo Game { get; }

    /// <summary>The driver's interest layer, or null when interest management is off.</summary>
    public DistanceInterest? Interest { get; }

    public World World => Host.World;

    /// <summary>The voice relay (Open mode, proximity filtered), or null with voice off.</summary>
    public VoiceRelay? VoiceRelay { get; }

    /// <summary>Who is in whose hearing range (the relay's filter), or null with voice off.</summary>
    public ProximityRelayFilter? Proximity { get; }

    /// <summary>The talkers' view points the filter reads, or null with voice off.</summary>
    public HubVoicePositions? VoicePositions { get; }

    public IReadOnlyList<HubPlayer> Players => _players;

    /// <summary>The course, the aim range, emotes, the jukebox and the plaza ball, or null with them off.</summary>
    public HubActivities? Activities { get; }
    /// <summary>The hub's world texts (pad signs, leaderboards): see <see cref="HubSigns"/>.</summary>
    public HubSigns Signs { get; }

    /// <summary>The match pads, or null with <see cref="HubSettings.MatchPads"/> off.</summary>
    public HubPads? Pads { get; }

    /// <summary>Where the pads came from and how many there are (for the host's log).</summary>
    public string PadsSummary { get; } = "match pads off";

    /// <summary>Raised for each line the server logs (joins, leaves, side changes).</summary>
    public event Action<string>? Logged;

    /// <summary>One 60 Hz step of the host, then the hub's own rules (respawns).</summary>
    public void Tick(long nowMicros)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _now = nowMicros;
        Host.Tick(nowMicros);
        Watch();
    }

    /// <summary>Runs the hub on the wall clock until cancelled (see <see cref="ServerHost.RunRealtime"/>).</summary>
    public void RunRealtime(IClock clock, CancellationToken cancellationToken, Action<HubServer>? afterTick = null)
    {
        _realtime = clock;
        Host.RunRealtime(clock, cancellationToken, _ =>
        {
            Watch();
            afterTick?.Invoke(this);
        });
    }

    /// <summary>Sends a system line to every player's chat box.</summary>
    public void Say(string text) => Host.Broadcast(HubProtocol.Message(text));

    /// <summary>
    /// Changes a player's side now (as his own request would, without the cooldown): his pawn goes at once, the new
    /// side's pawn comes once his client has let go of the old one (<see cref="Respawn"/>).
    /// </summary>
    public bool SwitchSide(PlayerSlot slot, int team)
    {
        ArgumentNullException.ThrowIfNull(slot);
        if (slot.Controller is not { } pc || slot.Tag is not HubPlayer player)
        {
            return false;
        }

        Pawn? old = pc.Pawn;
        if (!Game.SwitchSide(pc, team))
        {
            return false;
        }

        slot.Connection.ControlChannel?.Send(ControlMessage.TextLine(HubProtocol.Side(SGameInfo.TeamIndexOf(pc))));
        Log($"{slot.Name} plays {(team == 0 ? "spy" : "merc")}");
        SpawnWhenReleased(player, pc, old);
        return true;
    }

    /// <summary>Takes a player's pawn away and puts him back at the plaza once his client has let go of it.</summary>
    private void Respawn(HubPlayer player, PlayerController pc)
    {
        Pawn? old = pc.Pawn;
        Game.TakePawn(pc);
        SpawnWhenReleased(player, pc, old);
    }

    /// <summary>
    /// The second half of a side change or respawn: the new pawn is spawned once the player's client has destroyed
    /// <paramref name="old"/> (its channel's close is acknowledged, so the client's controller has no pawn and its
    /// restart waits for the new one), at once when there was none, or after
    /// <see cref="HubK.PawnReleaseTimeoutSeconds"/>. Spawning in the same tick restarted the old pawn on the client:
    /// no view of the new pawn, its first-person weapon missing and shots leaving from the old pawn's place.
    /// </summary>
    private void SpawnWhenReleased(HubPlayer player, PlayerController pc, Pawn? old)
    {
        // A second change while the client still holds an earlier pawn keeps waiting for that one.
        if (old is not null || player.Releasing is null)
        {
            player.Releasing = old;
            player.ReleasingSince = World.TimeSeconds;
        }

        player.PawnlessSince = -1f;
        if (Released(player))
        {
            FinishSpawn(player, pc);
        }
    }

    private static bool Released(HubPlayer player) =>
        player.Releasing is not { } old || player.Slot.Connection.FindActorChannel(old) is null;

    private void FinishSpawn(HubPlayer player, PlayerController pc)
    {
        player.Releasing = null;
        if (Game.Spawn(pc))
        {
            Count(player.Slot);
        }
    }

    /// <summary>A short status line (players per side, bytes sent per player, interest counters).</summary>
    public string Status()
    {
        int spies = 0;
        int mercs = 0;
        long outBytes = 0;
        foreach (HubPlayer p in _players)
        {
            if (p.Slot.Controller is { } pc)
            {
                if (SGameInfo.TeamIndexOf(pc) == 0)
                {
                    spies++;
                }
                else
                {
                    mercs++;
                }
            }

            outBytes += p.Slot.Connection.Stats.OutBytes;
        }

        NetDriverStats s = Host.Driver.Stats;
        string voice = VoiceRelay is { } relay
            ? string.Create(CultureInfo.InvariantCulture, $", voice up {relay.Stats.UpPackets} down {relay.Stats.DownPackets} out of range {relay.Stats.OutOfRange}")
            : string.Empty;
        return string.Create(CultureInfo.InvariantCulture,
            $"{_players.Count} player(s), {spies} spies and {mercs} mercs, {Host.World.TimeSeconds:F0} s, sent {outBytes / 1024} KB, culled {s.InterestCulled} deferred {s.InterestDeferred}") + voice;
    }

    private LoginDecision FilterLogin(NetConnection connection, string name, string options)
    {
        if (Host.Players.Count >= Settings.MaxPlayers)
        {
            return LoginDecision.Refuse("The hub is full");
        }

        int team = ReadInt(options, "Team", 0) == 1 ? 1 : 0;
        int mesh = Math.Clamp(ReadInt(options, "Mesh", 0), 0, 3);
        string back = ReadText(options, HubProtocol.ReturnOption);
        if (back.Length > 0)
        {
            _returning[connection] = back;
        }

        string clean = UniqueName(CleanName(name));
        return LoginDecision.Allow(clean, string.Create(CultureInfo.InvariantCulture, $"?Team={team}?Mesh={mesh}"));
    }

    private void OnJoined(PlayerSlot slot)
    {
        var player = new HubPlayer(slot);
        slot.Tag = player;
        _players.Add(player);
        if (slot.Controller is { } pc && Game.Spawn(pc))
        {
            player.Spawns++;
        }

        if (_returning.Remove(slot.Connection, out string? padId) && Pads?.Find(padId) is { } pad)
        {
            // Back from a pad's match: beside the pad he played from, not on it (that would queue him again), once his
            // client has the pawn (a teleport before that is lost to the client's first moves).
            player.ReturnPad = pad;
            player.ReturnAt = World.TimeSeconds + ReturnDelaySeconds;
            Log($"{slot.Name} is back from the match of {pad.Config.Id}");
        }

        ControlChannel? control = slot.Connection.ControlChannel;
        control?.Send(ControlMessage.TextLine(HubProtocol.Welcome(slot.PlayerId, Settings.MaxPlayers, Settings.Name)));
        control?.Send(ControlMessage.TextLine(HubProtocol.Side(SGameInfo.TeamIndexOf(slot.Controller))));
        if (Settings.Motd is { Length: > 0 } motd)
        {
            control?.Send(ControlMessage.TextLine(HubProtocol.Message(motd)));
        }

        Pads?.SendAll(slot);

        Say($"{slot.Name} joined the hub.");
        Log($"{slot.Name} joined ({_players.Count} in the hub)");
        Master?.HeartbeatSoon();
    }

    private void OnLeft(PlayerSlot slot)
    {
        if (slot.Tag is HubPlayer player)
        {
            _players.Remove(player);
            Pads?.Left(player);
            Signs.Forget(slot);
            Say($"{slot.Name} left the hub.");
            Log($"{slot.Name} left ({_players.Count} in the hub)");
            Master?.HeartbeatSoon();
        }
    }

    private void OnSessionLine(NetConnection connection, PlayerSlot? slot, string line)
    {
        if (slot?.Tag is not HubPlayer player || slot.Controller is not { } pc ||
            !HubProtocol.TryParse(line, out string verb, out List<string> fields))
        {
            return;
        }

        float now = World.TimeSeconds;
        if (verb is "side" or "respawn" && now - player.LastRequest < HubK.SideChangeCooldownSeconds)
        {
            connection.ControlChannel?.Send(ControlMessage.TextLine(HubProtocol.Message("Wait a moment before changing again.")));
            return;
        }

        switch (verb)
        {
            case "side":
                player.LastRequest = now;
                SwitchSide(slot, SessionProtocol.Int(fields, 0) == 1 ? 1 : 0);
                break;
            case "respawn":
                player.LastRequest = now;
                if (!player.AwaitingSpawn)
                {
                    Respawn(player, pc);
                }

                break;
        }
    }

    /// <summary>Puts back at the plaza every player whose pawn is gone, dead or out of the hall for long enough.</summary>
    /// <summary>The hub's info for browsers and the master: a hub on the hub map, always "playing".</summary>
    public ServerInfo Info()
    {
        var list = new List<ServerInfoPlayer>(_players.Count);
        foreach (HubPlayer p in _players)
        {
            list.Add(new ServerInfoPlayer(p.Slot.Name, (byte)Math.Max(0, SGameInfo.TeamIndexOf(p.Slot.Controller))));
        }

        return new ServerInfo
        {
            Name = Settings.Name, Map = Settings.Map, Phase = SessionPhase.Match, Players = _players.Count,
            MaxPlayers = Settings.MaxPlayers, GamePort = _udp?.LocalPort ?? 0, ProtocolHash = Host.Driver.ProtocolHash,
            ContentHash = Settings.Maps?.FindMap(Settings.Map)?.ContentHash ?? 0, Dedicated = Settings.Dedicated,
            PlayerList = list, MasterId = Master?.ServerId ?? string.Empty, IsHub = true,
        };
    }

    private void OnUnconnected(System.Net.IPEndPoint from, ReadOnlySpan<byte> data, bool broadcast)
    {
        long nowMs = (_realtime?.NowMicroseconds ?? _now) / 1000;
        switch (DiscoveryPackets.TypeOf(data))
        {
            case DiscoveryPackets.Query when DiscoveryPackets.IsQuery(data) && _answers.Allow(from.Address, nowMs):
            {
                int n = DiscoveryPackets.WriteInfo(_infoBuffer, DiscoveryPackets.NonceOf(data), Info());
                if (n > 0 && n <= _infoBuffer.Length)
                {
                    _udp!.SendUnconnected(from, _infoBuffer.AsSpan(0, n));
                }

                break;
            }

            case DiscoveryPackets.Probe:
            {
                int n = DiscoveryPackets.WriteHeader(_probeBuffer, DiscoveryPackets.ProbeReply, DiscoveryPackets.NonceOf(data));
                _udp!.SendUnconnected(from, _probeBuffer.AsSpan(0, n));
                break;
            }
        }
    }

    private void Watch()
    {
        Master?.Tick(_realtime?.NowMicroseconds ?? _now, Info);
        float now = World.TimeSeconds;
        _respawn.Clear();
        _spawn.Clear();
        foreach (HubPlayer p in _players)
        {
            if (p.Slot.Controller is not { bDeleteMe: false } pc)
            {
                continue;
            }

            if (p.AwaitingSpawn)
            {
                if (Released(p) || now - p.ReleasingSince >= HubK.PawnReleaseTimeoutSeconds)
                {
                    _spawn.Add(p);
                }

                continue;
            }

            bool alive = pc.Pawn is { bDeleteMe: false, Health: > 0 } pawn && pawn.Location.Z > HubK.KillZ;
            if (alive && p.ReturnPad is { } back && now >= p.ReturnAt)
            {
                p.ReturnPad = null;
                pc.Pawn!.SetLocation(back.Area.ReturnPoint(_players.IndexOf(p)));
            }

            if (alive)
            {
                p.PawnlessSince = -1f;
                continue;
            }

            if (p.PawnlessSince < 0f)
            {
                p.PawnlessSince = now;
            }
            else if (now - p.PawnlessSince >= HubK.RespawnSeconds)
            {
                _respawn.Add(p);
            }
        }

        Pads?.Tick(now, _players);

        // Signs four times a second: what each player should have from where his pawn is.
        if (++_signTick >= Sim.Hz / 4)
        {
            _signTick = 0;
            foreach (HubPlayer p in _players)
            {
                Signs.Update(p.Slot, p.Slot.Controller?.Pawn is { bDeleteMe: false } pawn ? pawn.Location : null);
            }
        }

        foreach (HubPlayer p in _spawn)
        {
            if (p.Slot.Controller is { } pc)
            {
                FinishSpawn(p, pc);
            }
        }

        foreach (HubPlayer p in _respawn)
        {
            if (p.Slot.Controller is { } pc)
            {
                Respawn(p, pc);
            }
        }

        Activities?.Tick();
    }

    private static void Count(PlayerSlot slot)
    {
        if (slot.Tag is HubPlayer p)
        {
            p.Spawns++;
            p.PawnlessSince = -1f;
        }
    }

    private void Log(string line) => Logged?.Invoke(line);

    internal void LogLine(string line) => Log(line);

    /// <summary>A name as the hub shows it: printable, at most <see cref="HubK.MaxNameLength"/> characters, never empty.</summary>
    public static string CleanName(string? name)
    {
        var sb = new StringBuilder();
        foreach (char c in name ?? string.Empty)
        {
            if (sb.Length >= HubK.MaxNameLength)
            {
                break;
            }

            if (!char.IsControl(c) && c != '?' && c != '#')
            {
                sb.Append(c);
            }
        }

        string clean = sb.ToString().Trim();
        return clean.Length == 0 ? "Player" : clean;
    }

    private string UniqueName(string name)
    {
        string candidate = name;
        for (int n = 2; Taken(candidate); n++)
        {
            string suffix = n.ToString(CultureInfo.InvariantCulture);
            candidate = name[..Math.Min(name.Length, HubK.MaxNameLength - suffix.Length)] + suffix;
        }

        return candidate;
    }

    private bool Taken(string name)
    {
        foreach (PlayerSlot p in Host.Players)
        {
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static int ReadInt(string options, string key, int fallback)
    {
        string marker = "?" + key + "=";
        int at = options.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (at < 0)
        {
            return fallback;
        }

        int start = at + marker.Length;
        int end = options.IndexOf('?', start);
        string value = end < 0 ? options[start..] : options[start..end];
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : fallback;
    }

    private static string ReadText(string options, string key)
    {
        string marker = "?" + key + "=";
        int at = options.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (at < 0)
        {
            return string.Empty;
        }

        int start = at + marker.Length;
        int end = options.IndexOf('?', start);
        return (end < 0 ? options[start..] : options[start..end]).Trim();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (Master is { } master)
        {
            master.Unlist();
            master.Dispose();
        }

        if (_udp is not null && _udp.Unconnected == OnUnconnected)
        {
            _udp.Unconnected = null;
        }

        if (Pads is not null)
        {
            foreach (IHubMatch m in Pads.Matches)
            {
                m.Dispose();
            }
        }

        Host.Dispose();
    }
}
