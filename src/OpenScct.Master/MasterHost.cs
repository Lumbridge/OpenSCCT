using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenScct.Net;
using OpenScct.Net.Discovery;

namespace OpenScct.Master;

/// <summary>
/// The master: the HTTP list API (minimal API on Kestrel) and the UDP introducer, relay and prober, with one shared
/// registry. <see cref="StartAsync"/> runs it in process (tests, tools); <see cref="Program"/> runs it as a service.
/// It answers only our own hosts and clients and never talks to any other master.
/// </summary>
public sealed class MasterHost : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly Introducer? _introducer;
    private readonly Relay? _relay;
    private readonly Prober? _prober;
    private readonly Timer _sweeper;
    private readonly RateLimiter _limiter;
    private readonly RateLimiter _webLimiter;
    private readonly AccountStore? _store;
    private readonly TicketSigner? _signer;
    private readonly AccountApi? _accounts;
    private readonly Admin.AdminPortal? _portal;
    private readonly Player.PlayerPortal? _players;
    private readonly DemoStorage? _demos;
    private readonly Web.ReleaseChangelog _releases;
    private bool _disposed;

    private MasterHost(WebApplication app, MasterOptions options, ServerRegistry registry, Introducer? introducer, Relay? relay, Prober? prober,
        AccountStore? store, TicketSigner? signer, TimeProvider time, Web.ReleaseChangelog releases)
    {
        _app = app;
        Options = options;
        Registry = registry;
        _introducer = introducer;
        _relay = relay;
        _prober = prober;
        _store = store;
        _signer = signer;
        _releases = releases;
        _demos = store is not null && options.DemosEnabled ? new DemoStorage(options.Demos, time) : null;
        _accounts = store is not null && signer is not null ? new AccountApi(options, store, signer, registry, ClientIp, time, _demos) : null;
        if (_accounts is not null && store is not null)
        {
            _portal = new Admin.AdminPortal(options, store, registry, ClientIp, () => (RelayForwarded, Introductions), _demos);
            _accounts.AdminCallback = _portal.CallbackAsync;
            _players = new Player.PlayerPortal(options, store, registry, ClientIp, releases);
            _accounts.PlayerCallback = _players.CallbackAsync;
        }
        _limiter = new RateLimiter(options.RequestsPerMinute);
        _webLimiter = new RateLimiter(options.RequestsPerMinute);
        _sweeper = new Timer(_ => Sweep(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public MasterOptions Options { get; }

    internal ServerRegistry Registry { get; }

    /// <summary>The HTTP base address the API listens on (with the real port when the options asked for port 0).</summary>
    public string HttpAddress { get; private set; } = string.Empty;

    public int IntroducerPort => _introducer?.Port ?? 0;

    public int RelayPort => _relay?.Port ?? 0;

    public int ProbePort => _prober?.Port ?? 0;

    /// <summary>"host:port" of the introducer as hosts and clients are told, or null.</summary>
    public string? IntroducerAddress => _introducer is null ? null : Options.PublicHost + ":" + IntroducerPort.ToString(CultureInfo.InvariantCulture);

    public string? RelayAddress => _relay is null ? null : Options.PublicHost + ":" + RelayPort.ToString(CultureInfo.InvariantCulture);

    public long RelayForwarded => Registry.RelayForwarded;

    public long Introductions => _introducer?.Introductions ?? 0;

    /// <summary>True when players can sign in here.</summary>
    public bool AccountsEnabled => _accounts is not null;

    /// <summary>
    /// Builds and starts a master with <paramref name="options"/>. With Discord set up it opens the account database
    /// and the ticket key (<see cref="MasterOptions.Database"/>, <see cref="MasterOptions.TicketKeyFile"/>).
    /// </summary>
    public static Task<MasterHost> StartAsync(MasterOptions options, string[]? args = null, CancellationToken cancellationToken = default,
        TimeProvider? time = null) => StartCoreAsync(options, args, cancellationToken, time, null);

    /// <summary>Starts with a supplied release source so integration tests never need the public GitHub feed.</summary>
    internal static Task<MasterHost> StartAsync(MasterOptions options, Web.ReleaseChangelog releases,
        CancellationToken cancellationToken = default, TimeProvider? time = null) =>
        StartCoreAsync(options, null, cancellationToken, time, releases);

    private static async Task<MasterHost> StartCoreAsync(MasterOptions options, string[]? args, CancellationToken cancellationToken,
        TimeProvider? time, Web.ReleaseChangelog? releases)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        time ??= TimeProvider.System;
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder(args ?? []);
        builder.WebHost.UseUrls(options.HttpUrl);
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.Limits.MaxRequestBodySize = 1024 * 1024);
        builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
        builder.Services.ConfigureHttpJsonOptions(json =>
        {
            json.SerializerOptions.PropertyNamingPolicy = MasterApi.Json.PropertyNamingPolicy;
            json.SerializerOptions.DefaultIgnoreCondition = MasterApi.Json.DefaultIgnoreCondition;
            foreach (System.Text.Json.Serialization.JsonConverter c in MasterApi.Json.Converters)
            {
                json.SerializerOptions.Converters.Add(c);
            }
        });
        WebApplication app = builder.Build();
        ILoggerFactory logs = app.Services.GetRequiredService<ILoggerFactory>();
        var registry = new ServerRegistry(options, time);
        AccountStore? store = null;
        TicketSigner? signer = null;
        if (options.AccountsEnabled)
        {
            try
            {
                store = AccountStore.Open(options.ResolvePath(options.Database), time);
                signer = TicketSigner.LoadOrCreate(options.ResolvePath(options.TicketKeyFile));
                MasterSettings.Apply(store, options);
            }
            catch
            {
                store?.Dispose();
                await app.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        Introducer? introducer = options.Introducer ? new Introducer(registry, logs.CreateLogger<Introducer>()) : null;
        Relay? relay = options.Relay ? new Relay(registry, logs.CreateLogger<Relay>()) : null;
        Prober? prober = options.Probe ? new Prober(registry, logs.CreateLogger<Prober>()) : null;
        var master = new MasterHost(app, options, registry, introducer, relay, prober, store, signer, time,
            releases ?? new Web.ReleaseChangelog(time: time));
        try
        {
            introducer?.Start(options.UdpBind, options.IntroducerPort);
            relay?.Start(options.UdpBind, options.RelayPort);
            prober?.Start(options.UdpBind, options.ProbePort);
            master.MapEndpoints();
            await app.StartAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await master.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        IServerAddressesFeature? addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>();
        master.HttpAddress = addresses?.Addresses.FirstOrDefault() ?? options.HttpUrl;
        master._sweeper.Change(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        ILogger log = logs.CreateLogger("OpenScct.Master");
        log.LogInformation("svm-master {Version} (game {GameVersion}): HTTP {Http}, introducer {Introducer}, relay {Relay}, probes {Probe}, {Access}, accounts {Accounts}, demos {Demos}",
            MasterBuildInfo.Text, BuildInfo.Version, master.HttpAddress, master.IntroducerAddress ?? "off", master.RelayAddress ?? "off",
            prober is null ? "off" : "on", options.InviteKeys.Count > 0 ? "invite only" : "public",
            store is null ? "off" : options.ResolvePath(options.Database),
            master._demos is null ? "off" : $"s3://{options.Demos.Bucket} ({options.Demos.Region}, {options.Demos.KeepDays} days)");
        return master;
    }

    /// <summary>Runs until the process is asked to stop (Ctrl+C, SIGTERM).</summary>
    public Task WaitForShutdownAsync(CancellationToken cancellationToken = default) => _app.WaitForShutdownAsync(cancellationToken);

    private void Sweep()
    {
        Registry.Sweep();
        _prober?.Sweep();
        _limiter.Sweep();
        _webLimiter.Sweep();
        _accounts?.Links.Sweep();
        _portal?.Sample(Registry.Now);
    }

    // ---- HTTP API ----------------------------------------------------------------------------------------------

    private void MapEndpoints()
    {
        _app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase) &&
                !context.Request.Path.Equals(MasterApi.StatusPath, StringComparison.OrdinalIgnoreCase))
            {
                if (Options.InviteKeys.Count > 0)
                {
                    string key = context.Request.Headers[MasterApi.KeyHeader].ToString();
                    if (key.Length == 0)
                    {
                        await Fail(context, StatusCodes.Status401Unauthorized, "This master is private; set its invite key.").ConfigureAwait(false);
                        return;
                    }

                    if (!Options.InviteKeys.Contains(key, StringComparer.Ordinal))
                    {
                        await Fail(context, StatusCodes.Status403Forbidden, "The invite key was refused.").ConfigureAwait(false);
                        return;
                    }
                }

                if (!_limiter.Allow(ClientIp(context)))
                {
                    await Fail(context, StatusCodes.Status429TooManyRequests, "Too many requests.").ConfigureAwait(false);
                    return;
                }
            }
            else if (!context.Request.Path.StartsWithSegments("/web/assets", StringComparison.OrdinalIgnoreCase) &&
                !context.Request.Path.StartsWithSegments("/admin/assets", StringComparison.OrdinalIgnoreCase) &&
                !context.Request.Path.Equals(MasterApi.StatusPath, StringComparison.OrdinalIgnoreCase))
            {
                // Browsers never receive the game invite key. Their independent budget cannot starve game heartbeats.
                if (!_webLimiter.Allow(ClientIp(context)))
                {
                    context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                    await context.Response.WriteAsync("Too many requests; wait a minute.").ConfigureAwait(false);
                    return;
                }
            }

            await next(context).ConfigureAwait(false);
        });

        Web.WebHtml.MapAssets(_app);
        _app.MapPost(MasterApi.HeartbeatPath, (HttpContext context, HeartbeatRequest request) => Heartbeat(context, request));
        _app.MapGet(MasterApi.ListPath, (HttpContext context) => List(context));
        _app.MapGet(MasterApi.ListPath + "/{id}", (HttpContext context, string id) => Get(context, id));
        _app.MapPost(MasterApi.ListPath + "/{id}/join", (HttpContext context, string id) => Join(context, id));
        _app.MapDelete(MasterApi.ListPath + "/{id}", (string id, string? hostToken) =>
            RelayFrames.TryParseToken(hostToken, out ulong token) && Registry.Remove(id, token)
                ? Results.Ok()
                : Error(StatusCodes.Status404NotFound, "Unknown server id or wrong host token."));
        _app.MapGet(MasterApi.StatusPath, () => Results.Json(new MasterStatus
        {
            MasterVersion = MasterBuildInfo.Version, MasterBuild = MasterBuildInfo.BuildNumber,
            MasterCommit = MasterBuildInfo.Commit.Length > 0 ? MasterBuildInfo.Commit : null,
            Motd = Options.Motd, Announcement = Announcement(), LatestVersion = Options.LatestVersion, MinVersion = Options.MinVersion,
            Servers = Registry.ServerCount, RelaySessions = Registry.RelaySessionCount, InviteOnly = Options.InviteKeys.Count > 0,
            TicketKey = _accounts?.TicketKey, Accounts = _accounts is not null,
        }, MasterApi.Json));
        _app.MapGet(MasterApi.ChangelogPath, (Func<HttpContext, Task<IResult>>)(async context =>
            Results.Json(await _releases.GetAsync(context.RequestAborted).ConfigureAwait(false), MasterApi.Json)));
        if (_accounts is not null)
        {
            _accounts.Map(_app);
            _portal?.Map(_app);
            _players?.Map(_app);
        }
        else
        {
            AccountApi.MapDisabled(_app);
            _app.MapGet("/admin", () => Results.Content("The admin portal needs Discord sign-in set up on this master.", "text/plain"));
        }
    }

    private string? Announcement()
    {
        string? text = _store?.Settings().GetValueOrDefault("site.announcement");
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static Task Fail(HttpContext context, int status, string message)
    {
        context.Response.StatusCode = status;
        return context.Response.WriteAsJsonAsync(new MasterError { Error = message }, MasterApi.Json);
    }

    private static IResult Error(int status, string message) => Results.Json(new MasterError { Error = message }, MasterApi.Json, statusCode: status);

    private IPAddress ClientIp(HttpContext context)
    {
        IPAddress ip = context.Connection.RemoteIpAddress ?? IPAddress.Loopback;
        if (Options.TrustForwardedFor && IPAddress.IsLoopback(ip))
        {
            string forwarded = context.Request.Headers["X-Forwarded-For"].ToString();
            string first = forwarded.Split(',')[0].Trim();
            if (first.Length > 0 && IPAddress.TryParse(first, out IPAddress? real))
            {
                ip = real;
            }
        }

        return ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip;
    }

    private IResult Heartbeat(HttpContext context, HeartbeatRequest request)
    {
        if (Validate(request) is { } problem)
        {
            return Error(StatusCodes.Status400BadRequest, problem);
        }

        if (Options.MinVersion is { Length: > 0 } min && BuildInfo.CompareVersions(request.Version, min) < 0)
        {
            return Error(StatusCodes.Status426UpgradeRequired,
                $"This server runs version {request.Version}; the master lists {min} or newer. Please update.");
        }

        TrustedServerOptions? trusted = null;
        string serverKey = context.Request.Headers[MasterApi.ServerKeyHeader].ToString();
        if (serverKey.Length > 0 && (trusted = Options.TrustedServer(serverKey)) is null)
        {
            return Error(StatusCodes.Status403Forbidden, "Unknown server key.");
        }

        IPAddress from = ClientIp(context);
        if (_portal is not null && _portal.IsBlocked(request.ServerId, from))
        {
            return Error(StatusCodes.Status403Forbidden, "This server is blocked by the master's admins.");
        }

        ServerRecord? record = Registry.Heartbeat(request, from, out string? error, out int status);
        if (record is null)
        {
            return Error(status, error ?? "Refused.");
        }

        _prober?.MaybeProbe(record);
        string? message = null;
        if (Options.LatestVersion is { Length: > 0 } latest && BuildInfo.CompareVersions(request.Version, latest) < 0)
        {
            message = $"Version {latest} is available (this server runs {request.Version}).";
        }

        Reachability reach;
        IReadOnlyList<MasterCommand> commands = [];
        int? fast = null;
        lock (Registry.Gate)
        {
            record.TrustedName = trusted?.Name;
            reach = Registry.Classify(record);
            if (_portal is not null)
            {
                (commands, fast) = _portal.OnHeartbeat(record, request);
            }
        }

        return Results.Json(new HeartbeatResponse
        {
            ServerId = record.Id, HostToken = RelayFrames.TokenText(record.HostToken), PublicAddress = from.ToString(),
            Reachability = reach, Introducer = IntroducerAddress, Relay = RelayAddress,
            HeartbeatSeconds = fast is { } f ? Math.Min(f, Options.HeartbeatSeconds) : Options.HeartbeatSeconds,
            Message = message, LatestVersion = Options.LatestVersion, MinVersion = Options.MinVersion, Listed = request.Listed,
            Trusted = trusted is not null, TicketKey = _accounts?.TicketKey,
            KickAccounts = _accounts is not null && request.AccountIds.Count > 0 ? _accounts.Kicks(request.AccountIds) : [],
            Commands = commands,
        }, MasterApi.Json);
    }

    private static string? Validate(HeartbeatRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.Name) || r.Name.Length > 64)
        {
            return "The server name must be 1 to 64 characters.";
        }

        if (r.GamePort is < 1 or > 65535)
        {
            return "The game port is out of range.";
        }

        if (r.MaxPlayers is < 1 or > 64 || r.Players < 0 || r.Players > 64)
        {
            return "The player counts are out of range.";
        }

        if (!MasterApi.TryParseHex(r.Protocol, out _) || (r.Content.Length > 0 && !MasterApi.TryParseHex(r.Content, out _)))
        {
            return "The protocol or content hash is not hex.";
        }

        if (r.Version.Length > 32 || r.Region.Length > 16 || r.Map.Length > 64 || r.LanAddresses.Count > 8 || r.PlayerNames.Count > 64 || r.PlayerTeams.Count > 64 || r.AccountIds.Count > 64 ||
            r.RejoinAccounts.Count > 64 ||
            r.PlayerDetails.Count > 64 || r.CommandResults.Count > 32 || r.PlayerDetails.Any(p => p.Name.Length > 64))
        {
            return "The heartbeat is too large.";
        }

        foreach (string lan in r.LanAddresses)
        {
            if (lan.Length > 64)
            {
                return "A LAN address is too long.";
            }
        }

        foreach (string name in r.PlayerNames)
        {
            if (name.Length > 64)
            {
                return "A player name is too long.";
            }
        }

        return null;
    }

    private ServerEntry Entry(ServerRecord r, IPAddress asker)
    {
        HeartbeatRequest h = r.Last;
        return new ServerEntry
        {
            Id = r.Id, Name = h.Name, Address = r.PublicIp.ToString(), Port = h.GamePort,
            LanAddresses = r.PublicIp.Equals(asker) ? h.LanAddresses : [], Map = h.Map, Mode = h.Mode, Phase = h.Phase,
            Players = h.Players, MaxPlayers = h.MaxPlayers, Password = h.Password, Dedicated = h.Dedicated, Protocol = h.Protocol,
            Content = h.Content, Version = h.Version, Reachability = Registry.Classify(r),
            AgeSeconds = (int)Math.Max(0, Registry.Now - r.LastSeen), PlayerNames = h.PlayerNames,
            PlayerTeams = h.PlayerTeams.Count == h.PlayerNames.Count ? h.PlayerTeams : [],
            RequireAccounts = h.RequireAccounts, Trusted = r.Trusted, Kind = h.Kind, Region = h.Region, Practice = h.Practice,
        };
    }

    private IResult List(HttpContext context)
    {
        IPAddress asker = ClientIp(context);
        ulong? protocol = MasterApi.TryParseHex(context.Request.Query["protocol"].ToString(), out ulong p) ? p : null;
        var content = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string? c in context.Request.Query["content"])
        {
            if (MasterApi.TryParseHex(c, out ulong v))
            {
                content.Add(MasterApi.Hex(v));
            }
        }

        // Game servers unless the asker wants hubs: older games send no kind and must not see hubs as games.
        ServerKind kind = string.Equals(context.Request.Query["kind"].ToString(), MasterApi.KindText(ServerKind.Hub), StringComparison.OrdinalIgnoreCase)
            ? ServerKind.Hub
            : ServerKind.Game;
        var entries = new List<ServerEntry>();
        lock (Registry.Gate)
        {
            foreach (ServerRecord r in Registry.Snapshot())
            {
                if (!r.Last.Listed || r.Last.Kind != kind || (protocol is { } wanted && !string.Equals(r.Last.Protocol, MasterApi.Hex(wanted), StringComparison.OrdinalIgnoreCase)) ||
                    (content.Count > 0 && !content.Contains(r.Last.Content)))
                {
                    continue;
                }

                entries.Add(Entry(r, asker));
            }
        }

        // The master's own (trusted) servers first: the official hub heads the hub list.
        entries.Sort((a, b) => a.Trusted != b.Trusted ? b.Trusted.CompareTo(a.Trusted) : b.Players != a.Players ? b.Players.CompareTo(a.Players) : string.CompareOrdinal(a.Name, b.Name));
        return Results.Json(new ServerListResponse
        {
            Servers = entries, YourAddress = asker.ToString(), Motd = Options.Motd, LatestVersion = Options.LatestVersion,
            MinVersion = Options.MinVersion,
        }, MasterApi.Json);
    }

    private IResult Get(HttpContext context, string id)
    {
        lock (Registry.Gate)
        {
            return Registry.Find(id) is { } r ? Results.Json(Entry(r, ClientIp(context)), MasterApi.Json) : Error(404, "The server is no longer listed.");
        }
    }

    private IResult Join(HttpContext context, string id)
    {
        if (JoinAnswer(id, ClientIp(context)) is not { } answer)
        {
            return Error(StatusCodes.Status404NotFound, "The server is no longer listed.");
        }

        if (_accounts is not null)
        {
            // Outside the registry lock: the ticket reads the account database.
            (string? ticket, string? problem) = _accounts.Ticket(context, answer.ServerId);
            answer = answer with { Ticket = ticket, TicketProblem = problem };
        }

        return Results.Json(answer, MasterApi.Json);
    }

    private JoinResponse? JoinAnswer(string id, IPAddress asker)
    {
        lock (Registry.Gate)
        {
            if (Registry.Find(id) is not { } r)
            {
                return null;
            }

            double now = Registry.Now;
            bool natFresh = r.Last.NatPunch && r.NatExternal is not null && now - r.NatSeen <= Options.RegistrationSeconds;
            bool relayFresh = r.Last.Relay && r.RelayEndpoint is not null && now - r.RelaySeen <= Options.RegistrationSeconds;
            string? punch = _introducer is not null && natFresh ? RelayFrames.TokenText(Registry.IssuePunch(r.Id)) : null;
            RelaySession? session = _relay is not null && relayFresh ? Registry.OpenRelay(r.Id) : null;
            bool same = r.PublicIp.Equals(asker);
            return new JoinResponse
            {
                ServerId = r.Id,
                Direct = LiteNetLibTransport.FormatEndPoint(new IPEndPoint(r.PublicIp, r.Last.GamePort)),
                LanAddresses = same ? r.Last.LanAddresses : [],
                SameNetwork = same,
                Reachability = Registry.Classify(r),
                Introducer = punch is null ? null : IntroducerAddress,
                PunchToken = punch,
                Relay = session is null ? null : RelayAddress,
                RelayToken = session is null ? null : RelayFrames.TokenText(session.ClientToken),
                Name = r.Last.Name, Protocol = r.Last.Protocol, Content = r.Last.Content, Map = r.Last.Map,
                RequireAccounts = r.Last.RequireAccounts,
            };
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await _sweeper.DisposeAsync().ConfigureAwait(false);
        try
        {
            await _app.StopAsync().ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            // Not started.
        }

        await _app.DisposeAsync().ConfigureAwait(false);
        _introducer?.Dispose();
        _relay?.Dispose();
        _prober?.Dispose();
        _store?.Dispose();
        _signer?.Dispose();
        _demos?.Dispose();
    }

    /// <summary>A fixed one-minute window of requests per address.</summary>
    private sealed class RateLimiter(int perMinute)
    {
        private readonly Dictionary<IPAddress, (long Window, int Count)> _counts = new();
        private readonly int _perMinute = perMinute;

        public bool Allow(IPAddress ip)
        {
            if (_perMinute <= 0)
            {
                return true;
            }

            long window = Environment.TickCount64 / 60_000;
            lock (_counts)
            {
                (long w, int count) = _counts.GetValueOrDefault(ip);
                count = w == window ? count + 1 : 1;
                _counts[ip] = (window, count);
                return count <= _perMinute;
            }
        }

        public void Sweep()
        {
            long window = Environment.TickCount64 / 60_000;
            lock (_counts)
            {
                var old = new List<IPAddress>();
                foreach (KeyValuePair<IPAddress, (long Window, int Count)> e in _counts)
                {
                    if (e.Value.Window < window)
                    {
                        old.Add(e.Key);
                    }
                }

                foreach (IPAddress ip in old)
                {
                    _counts.Remove(ip);
                }
            }
        }
    }
}
