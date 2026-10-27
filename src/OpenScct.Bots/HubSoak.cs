using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Net.Moves;
using OpenScct.Server;
using OpenScct.Server.Hub;

namespace OpenScct.Bots;

/// <summary>A crowd of hub visitors: how many, how they join and what they do besides walking.</summary>
public sealed record HubSoakSettings
{
    public int Bots { get; init; } = 50;

    /// <summary>Share of the bots that log in as spies (the rest are mercs).</summary>
    public float SpyShare { get; init; } = 0.5f;

    public ulong Seed { get; init; } = 1;

    /// <summary>Bots that connect per second (0: all at once).</summary>
    public float JoinsPerSecond { get; init; } = 10f;

    /// <summary>Chat lines each bot says per minute, on average.</summary>
    public float ChatPerMinute { get; init; } = 1f;

    /// <summary>Side changes each bot asks for per minute, on average.</summary>
    public float SideChangesPerMinute { get; init; } = 0.2f;

    public string NamePrefix { get; init; } = "Bot";

    public MapCatalog? Maps { get; init; }

    public NetClassRegistry? Classes { get; init; }

    /// <summary>The walk graph of the hub map; null builds it (<see cref="HubSoak.BuildGrid"/>).</summary>
    public NavGrid? Grid { get; init; }

    public string Map { get; init; } = HubK.MapName;
}

/// <summary>What one bot's connection measured.</summary>
public sealed record HubBotReport(string Name, int Team, double DownBytesPerSecond, double UpBytesPerSecond, int PawnsSeen,
    long CorrectionsApplied, int ChatLines, int Arrivals, bool Joined);

/// <summary>The crowd after a run.</summary>
public sealed record HubSoakReport(double Seconds, IReadOnlyList<HubBotReport> Bots)
{
    public double MeanDown => Mean(static b => b.DownBytesPerSecond);

    public double MeanUp => Mean(static b => b.UpBytesPerSecond);

    public double MaxDown
    {
        get
        {
            double max = 0;
            foreach (HubBotReport b in Bots)
            {
                max = Math.Max(max, b.DownBytesPerSecond);
            }

            return max;
        }
    }

    public int Joined
    {
        get
        {
            int n = 0;
            foreach (HubBotReport b in Bots)
            {
                n += b.Joined ? 1 : 0;
            }

            return n;
        }
    }

    public double MeanPawnsSeen => Mean(static b => b.PawnsSeen);

    private double Mean(Func<HubBotReport, double> f)
    {
        if (Bots.Count == 0)
        {
            return 0;
        }

        double sum = 0;
        foreach (HubBotReport b in Bots)
        {
            sum += f(b);
        }

        return sum / Bots.Count;
    }

    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture,
            $"{Joined} of {Bots.Count} bots in after {Seconds:F1} s: down {MeanDown:F0} B/s mean, {MaxDown:F0} B/s max; up {MeanUp:F0} B/s mean; {MeanPawnsSeen:F1} other pawns seen on average");
        return sb.ToString();
    }
}

/// <summary>
/// A crowd of headless hub visitors (the hub's soak test, docs/HUB.md): <see cref="HubSoakSettings.Bots"/> bot clients
/// with <see cref="HubWanderBrain"/>s, each over its own transport to one hub server, joining a few a second,
/// chatting now and then and changing side now and then. Step it on one thread with the clock the transports use.
/// </summary>
public sealed class HubSoak : IDisposable
{
    private readonly Func<string, ITransport> _bind;
    private readonly string _address;
    private readonly List<Visitor> _visitors = new();
    private readonly List<ITransport> _transports = new();
    private readonly SimRandom _random;
    private readonly NavCache _nav = new();
    private readonly long _start;
    private int _joined;
    private bool _disposed;

    private sealed class Visitor(BotClient bot, HubClient hub, HubWanderBrain brain, int team)
    {
        public readonly BotClient Bot = bot;
        public readonly HubClient Hub = hub;
        public readonly HubWanderBrain Brain = brain;
        public int Team = team;
    }

    public HubSoak(HubSoakSettings settings, Func<string, ITransport> bind, string serverAddress, long nowMicros)
    {
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _bind = bind ?? throw new ArgumentNullException(nameof(bind));
        _address = serverAddress ?? throw new ArgumentNullException(nameof(serverAddress));
        Maps = settings.Maps ?? MapCatalog.CreateDefault();
        Classes = settings.Classes ?? NetClasses.Create();
        Grid = settings.Grid ?? BuildGrid(Maps, settings.Map);
        _random = new SimRandom(settings.Seed, 14, 0);
        _start = nowMicros;
        Home = HomeOf(Maps, settings.Map);
    }

    public HubSoakSettings Settings { get; }

    public MapCatalog Maps { get; }

    public NetClassRegistry Classes { get; }

    public NavGrid Grid { get; }

    /// <summary>The middle of the map's player starts (the plaza).</summary>
    public FVector Home { get; }

    public IEnumerable<BotClient> Bots
    {
        get
        {
            foreach (Visitor v in _visitors)
            {
                yield return v.Bot;
            }
        }
    }

    /// <summary>Bots created so far.</summary>
    public int Created => _visitors.Count;

    /// <summary>Chat lines the bots said.</summary>
    public int ChatSaid { get; private set; }

    /// <summary>Side changes the bots asked for.</summary>
    public int SideRequests { get; private set; }

    /// <summary>
    /// The hub's walk graph for bots: the floor every 64 uu over the box of the map's starts widened to the whole hall,
    /// with the merc's drop limit (both sides use it; spies take every drop a merc takes).
    /// </summary>
    public static NavGrid BuildGrid(MapCatalog maps, string map)
    {
        ArgumentNullException.ThrowIfNull(maps);
        var world = new World(0, ENetMode.NM_Client);
        if (!maps.TryLoad(world, map, true, out MapInfo? info, out string? error) || info?.Physics is null)
        {
            throw new InvalidOperationException($"Map {map} did not load: {error}");
        }

        FVector home = Middle(info);
        var margin = new FVector(6400f, 6400f, 700f);
        return NavGrid.Build(info.Physics.Collision, home - margin, home + margin,
            new NavGridSettings { Spacing = 64f, MaxDrop = BotK.MercMaxDrop });
    }

    private static FVector HomeOf(MapCatalog maps, string map)
    {
        var world = new World(0, ENetMode.NM_Client);
        return maps.TryLoad(world, map, true, out MapInfo? info, out _) && info is not null ? Middle(info) : default;
    }

    private static FVector Middle(MapInfo info)
    {
        if (info.PlayerStarts.Count == 0)
        {
            return default;
        }

        FVector sum = default;
        foreach (FVector p in info.PlayerStarts)
        {
            sum += p;
        }

        return sum * (1f / info.PlayerStarts.Count);
    }

    /// <summary>One frame: new bots join when due, then every bot thinks and ticks, then some chat or change side.</summary>
    public void Step(long nowMicros)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        int due = Settings.JoinsPerSecond <= 0f ? Settings.Bots
            : Math.Min(Settings.Bots, 1 + (int)((nowMicros - _start) / 1_000_000.0 * Settings.JoinsPerSecond));
        while (_visitors.Count < due)
        {
            Add(nowMicros);
        }

        float chatChance = Settings.ChatPerMinute / (60f * Sim.Hz);
        float sideChance = Settings.SideChangesPerMinute / (60f * Sim.Hz);
        foreach (Visitor v in _visitors)
        {
            v.Bot.Tick(nowMicros);
            v.Hub.Tick(nowMicros);
            if (v.Bot.Controller?.Pawn is null)
            {
                continue;
            }

            if (_random.FRand() < chatChance)
            {
                v.Hub.Say(Phrases[_random.Rand(Phrases.Length)]);
                ChatSaid++;
            }

            if (_random.FRand() < sideChance)
            {
                v.Team = 1 - v.Team;
                v.Hub.RequestSide(v.Team);
                SideRequests++;
            }
        }
    }

    private static readonly string[] Phrases =
    [
        "hi all", "anyone up for a match?", "nice climb", "meet at the fountain", "zip line is fun", "brb", "gg",
    ];

    private void Add(long nowMicros)
    {
        int i = _visitors.Count;
        bool spy = (i + 0.5f) / Settings.Bots <= Settings.SpyShare;
        string name = Settings.NamePrefix + (i + 1).ToString("D2", CultureInfo.InvariantCulture);
        ITransport transport = _bind(name);
        _transports.Add(transport);
        var brain = new HubWanderBrain(Grid, BotNavigator.Seed(name) ^ (int)Settings.Seed, spy, Home);
        var bot = new BotClient(transport, _address, nowMicros, name, spy ? BotTeam.Spy : BotTeam.Merc, brain, _nav,
            new ClientSettings { Classes = Classes, Maps = Maps });
        // The bot ticks its own session (BotClient.Tick); the hub client only reads its lines.
        _visitors.Add(new Visitor(bot, new HubClient(bot.Session) { SessionTickedElsewhere = true }, brain, spy ? 0 : 1));
        _joined++;
    }

    /// <summary>What each bot's connection measured, averaged over <paramref name="seconds"/>.</summary>
    public HubSoakReport Report(double seconds)
    {
        var list = new List<HubBotReport>();
        foreach (Visitor v in _visitors)
        {
            NetConnection? c = v.Bot.Session.Connection;
            double down = c is null ? 0 : (c.Stats.InBytes + c.Stats.InPackets * NetConnection.PacketOverheadBytes) / seconds;
            double up = c is null ? 0 : (c.Stats.OutBytes + c.Stats.OutPackets * NetConnection.PacketOverheadBytes) / seconds;
            int seen = 0;
            foreach (SBasePawn p in v.Bot.Session.World.Level.DynamicActors<SBasePawn>())
            {
                if (!p.bDeleteMe && !ReferenceEquals(p, v.Bot.Controller?.Pawn))
                {
                    seen++;
                }
            }

            ClientMoves? moves = v.Bot.Controller is { } pc ? v.Bot.Session.Movement.EndpointOf(pc)?.Client : null;
            list.Add(new HubBotReport(v.Bot.Name, v.Team, down, up, seen, moves?.CorrectionsApplied ?? 0, v.Hub.Chat.Count,
                v.Brain.Arrivals, v.Bot.Session.State == EClientState.Joined && v.Bot.Controller is not null));
        }

        return new HubSoakReport(seconds, list);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (Visitor v in _visitors)
        {
            v.Hub.Dispose();
            v.Bot.Dispose();
        }

        foreach (ITransport t in _transports)
        {
            t.Dispose();
        }
    }
}
