using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using OpenScct.Net;
using OpenScct.Net.Discovery;
using OpenScct.Provenance;

namespace OpenScct.Server.Online;

/// <summary>Where and how a host lists itself.</summary>
public sealed record MasterLinkSettings
{
    /// <summary>The master's HTTP address, for example <c>https://master.example.org</c>. Never another game's master.</summary>
    public string Url { get; init; } = string.Empty;

    /// <summary>The invite key of a private master.</summary>
    public string? InviteKey { get; init; }

    /// <summary>False keeps the server off the public list (players join by address or server id).</summary>
    public bool Listed { get; init; } = true;

    /// <summary>Register with the introducer for NAT punches.</summary>
    public bool NatPunch { get; init; } = true;

    /// <summary>Take relay sessions (players who reach the host only through the relay).</summary>
    public bool Relay { get; init; } = true;

    /// <summary>
    /// The trusted-server key the master gave this server (one of the master's own servers): with it, the master's
    /// admins are admins here and finished rounds count for the players' stats. Null for everyone else's servers.
    /// </summary>
    public string? ServerKey { get; init; }

    /// <summary>Tell the master only signed-in players may join (the browser shows it).</summary>
    public bool RequireAccounts { get; init; }

    /// <summary>A game server or a hub: the master lists hubs apart.</summary>
    public ServerKind Kind { get; init; }

    /// <summary>Where the server stands (<c>EU</c>, <c>US</c>...), shown in the lists; empty for none.</summary>
    public string Region { get; init; } = string.Empty;

    /// <summary>Upload round demos to the master's bucket (trusted servers only; docs/ONLINE.md, "Demos").</summary>
    public bool UploadDemos { get; init; } = true;
}

/// <summary>What the master lists about a demo besides its round (the recorder fills it in).</summary>
public sealed record DemoMeta
{
    /// <summary>How long the recording runs, seconds.</summary>
    public int DurationSeconds { get; init; }

    /// <summary>The game version that recorded it.</summary>
    public string GameVersion { get; init; } = BuildInfo.Version;

    /// <summary>The net protocol hash the stream is in.</summary>
    public ulong NetProtocol { get; init; }

    /// <summary>The map's content hash.</summary>
    public ulong ContentHash { get; init; }
}

/// <summary>
/// A host's registration with the master (docs/ONLINE.md, "Master"): the heartbeat with the server's info every
/// 30 s, the NAT punch registration with the introducer every 10 s and the relay hello every 5 s, all from the game
/// socket, so the introducer and the relay see the mapping players will use. HTTP calls run as tasks; their results
/// are taken on the host's thread in <see cref="Tick"/>. Notices for the host (listed, reachability, errors, update
/// notices) come out of <see cref="Notice"/>.
/// </summary>
[PortOf("rl:net.master", Spec = "spec/native/reloaded-features-from-code.md#2-master-server")]
public sealed class MasterLink : IDisposable
{
    private const long RelayHelloMicros = 5_000_000;
    private const long IntroducerMicros = 10_000_000;
    private const long RetryMicros = 15_000_000;

    private readonly MasterClient _client;
    private readonly LiteNetLibTransport _udp;
    private Task<HeartbeatResponse>? _pending;
    private long _nextHeartbeat;
    private long _nextRelayHello;
    private long _nextIntroduction;
    private IPEndPoint? _introducer;
    private IPEndPoint? _relay;
    private ulong _hostToken;
    private Reachability _lastReach = (Reachability)(-1);
    private string? _ticketKeyText;
    private string? _resultsError;
    private readonly ConcurrentQueue<string> _taskNotices = new();
    private bool _disposed;

    /// <summary>PUTs demo files to the signed S3 links (no master headers; long timeout for big files on slow links).</summary>
    private static readonly HttpClient s_upload = new() { Timeout = TimeSpan.FromMinutes(10) };

    public MasterLink(MasterLinkSettings settings, LiteNetLibTransport udp)
    {
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _udp = udp ?? throw new ArgumentNullException(nameof(udp));
        _client = new MasterClient(settings.Url, settings.InviteKey)
        {
            ServerKey = string.IsNullOrEmpty(settings.ServerKey) ? null : settings.ServerKey,
        };
    }

    public MasterLinkSettings Settings { get; }

    public string? ServerId { get; private set; }

    public Reachability Reachability { get; private set; }

    public string? PublicAddress { get; private set; }

    /// <summary>The last error, or null after a good heartbeat.</summary>
    public string? LastError { get; private set; }

    public bool Registered => ServerId is not null;

    /// <summary>The master knows this server's key (<see cref="MasterLinkSettings.ServerKey"/>) as one of its own.</summary>
    public bool Trusted { get; private set; }

    /// <summary>The master's public key for player tickets, or null until it sent one (or when it has no accounts).</summary>
    public ECDsa? TicketKey { get; private set; }

    /// <summary>Signed-in players present on the server, sent with each heartbeat (the master answers with the banned ones).</summary>
    public Func<IReadOnlyList<long>>? AccountIds { get; set; }

    /// <summary>Every player with team, ping and account, sent with each heartbeat for the master's admin portal.</summary>
    public Func<IReadOnlyList<HeartbeatPlayer>>? PlayerDetails { get; set; }

    /// <summary>Seconds left in the round being played (-1: none), sent with each heartbeat.</summary>
    public Func<int>? RoundSecondsLeft { get; set; }

    /// <summary>Signed-in players who dropped out of the round and may rejoin it, sent with each heartbeat.</summary>
    public Func<IReadOnlyList<long>>? RejoinAccounts { get; set; }

    /// <summary>Admin commands from the master (trusted servers only): raised on the host's thread, in <see cref="Tick"/>.</summary>
    public event Action<IReadOnlyList<MasterCommand>>? Commands;

    private readonly List<MasterCommandResult> _commandResults = [];

    /// <summary>What a master command printed: sent with the next heartbeat, which goes out at once.</summary>
    public void AddCommandResult(long id, string output)
    {
        if (_commandResults.Count < 32)
        {
            _commandResults.Add(new MasterCommandResult { Id = id, Output = output.Length > 2000 ? output[..2000] : output });
        }

        HeartbeatSoon();
    }

    /// <summary>Lines for the host's lobby and log.</summary>
    public event Action<string>? Notice;

    /// <summary>Accounts the master banned that are on the server: raised on the host's thread, in <see cref="Tick"/>.</summary>
    public event Action<IReadOnlyList<long>>? KickAccounts;

    /// <summary>
    /// Posts a finished round to the master (trusted servers only), off the host's thread: three tries ten seconds
    /// apart. A post that failed every try comes out of <see cref="Notice"/> at a later tick. The task ends with the
    /// stored round's id (for <see cref="UploadDemo"/>), or null when the round was not taken or the master is older.
    /// </summary>
    private readonly ConcurrentQueue<IReadOnlyDictionary<long, EquipmentAccountState>> _equipment = new();
    public bool TryTakeEquipment(out IReadOnlyDictionary<long, EquipmentAccountState> state) => _equipment.TryDequeue(out state!);

    public Task<long?> ReportResults(ResultsReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (_disposed || !Trusted || _client.ServerKey is null)
        {
            return Task.FromResult<long?>(null);
        }

        return PostResults(report);
    }

    private async Task<long?> PostResults(ResultsReport report)
    {
        string? error = null;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            if (_disposed)
            {
                return null;
            }

            try
            {
                ResultsResponse answer = await _client.ReportResultsAsync(report).ConfigureAwait(false);
                if (answer.Equipment.Count > 0) _equipment.Enqueue(answer.Equipment);
                return answer.MatchId;
            }
            catch (Exception e) when (e is MasterException or System.Net.Http.HttpRequestException or TaskCanceledException or ObjectDisposedException)
            {
                error = e.Message;
                if (e is MasterException { StatusCode: >= 400 and < 500 })
                {
                    break;
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        }

        if (error is not null)
        {
            Interlocked.Exchange(ref _resultsError, error);
        }

        return null;
    }

    /// <summary>
    /// Uploads the demo file of round <paramref name="matchId"/> (trusted servers with
    /// <see cref="MasterLinkSettings.UploadDemos"/>), off the host's thread: asks the master for a signed link, PUTs
    /// the file straight to the bucket, then confirms; three tries ten seconds apart. The outcome comes out of
    /// <see cref="Notice"/> at a later tick. The task ends true once the master lists the demo.
    /// </summary>
    public Task<bool> UploadDemo(long matchId, string filePath, DemoMeta meta)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);
        ArgumentNullException.ThrowIfNull(meta);
        if (_disposed || !Trusted || _client.ServerKey is null || !Settings.UploadDemos)
        {
            return Task.FromResult(false);
        }

        return Task.Run(() => PutDemo(matchId, filePath, meta));
    }

    private async Task<bool> PutDemo(long matchId, string filePath, DemoMeta meta)
    {
        long size;
        string sha;
        try
        {
            await using FileStream file = File.OpenRead(filePath);
            size = file.Length;
            sha = Convert.ToHexStringLower(await SHA256.HashDataAsync(file).ConfigureAwait(false));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _taskNotices.Enqueue($"Master server: the demo of round {matchId} could not be read: {e.Message}");
            return false;
        }

        var request = new DemoUploadRequest
        {
            MatchId = matchId, Size = size, Sha256 = sha, DurationSeconds = meta.DurationSeconds, GameVersion = meta.GameVersion,
            NetVersion = MasterApi.Hex(meta.NetProtocol), ContentHash = MasterApi.Hex(meta.ContentHash),
        };
        string? error = null;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            if (_disposed)
            {
                return false;
            }

            try
            {
                DemoUploadResponse link = await _client.RequestDemoUploadAsync(request).ConfigureAwait(false);
                await using (FileStream file = File.OpenRead(filePath))
                {
                    using var content = new StreamContent(file);
                    content.Headers.ContentType = new MediaTypeHeaderValue(link.ContentType);
                    content.Headers.ContentLength = size;
                    using HttpResponseMessage put = await s_upload.PutAsync(new Uri(link.UploadUrl), content).ConfigureAwait(false);
                    if (!put.IsSuccessStatusCode)
                    {
                        throw new HttpRequestException($"the bucket answered {(int)put.StatusCode}", null, put.StatusCode);
                    }
                }

                await _client.DemoDoneAsync(link.DemoId).ConfigureAwait(false);
                _taskNotices.Enqueue($"Master server: the demo of round {matchId} was uploaded ({size / 1024} KB).");
                return true;
            }
            catch (Exception e) when (e is MasterException or HttpRequestException or TaskCanceledException or ObjectDisposedException or IOException)
            {
                error = e.Message;
                // The master refused (too big, not our round, demos off): another try would be refused too.
                if (e is MasterException { StatusCode: (>= 400 and < 500) or 503 })
                {
                    break;
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        }

        _taskNotices.Enqueue($"Master server: the demo of round {matchId} was not uploaded: {error}");
        return false;
    }

    /// <summary>Called after every server tick with the server's current info.</summary>
    public void Tick(long nowMicros, Func<ServerInfo> info)
    {
        ArgumentNullException.ThrowIfNull(info);
        if (_disposed)
        {
            return;
        }

        if (Interlocked.Exchange(ref _resultsError, null) is { } resultsError)
        {
            Notice?.Invoke("Master server: the round's results were not taken: " + resultsError);
        }

        while (_taskNotices.TryDequeue(out string? line))
        {
            Notice?.Invoke(line);
        }

        if (_pending is { IsCompleted: true } done)
        {
            _pending = null;
            Completed(done, nowMicros);
        }

        if (_pending is null && nowMicros >= _nextHeartbeat)
        {
            _nextHeartbeat = nowMicros + RetryMicros;
            _pending = _client.HeartbeatAsync(Request(info()));
        }

        if (_relay is not null && nowMicros >= _nextRelayHello)
        {
            _nextRelayHello = nowMicros + RelayHelloMicros;
            _udp.SendRelayHello();
        }

        if (_introducer is not null && _hostToken != 0 && nowMicros >= _nextIntroduction)
        {
            _nextIntroduction = nowMicros + IntroducerMicros;
            _udp.RequestIntroduction(_introducer, "h:" + RelayFrames.TokenText(_hostToken));
        }
    }

    /// <summary>Asks for a heartbeat at the next tick (the player count or the phase changed).</summary>
    public void HeartbeatSoon()
    {
        _nextHeartbeat = Math.Min(_nextHeartbeat, 0);
    }

    private HeartbeatRequest Request(ServerInfo info)
    {
        var names = new List<string>();
        var teams = new List<byte>();
        foreach (ServerInfoPlayer p in info.PlayerList)
        {
            names.Add(p.Name);
            teams.Add(p.Team);
        }

        return new HeartbeatRequest
        {
            ServerId = ServerId,
            HostToken = _hostToken == 0 ? null : RelayFrames.TokenText(_hostToken),
            Name = info.Name, GamePort = _udp.LocalPort, LanAddresses = _udp.LoopbackOnly ? [] : LanAddresses(_udp.LocalPort),
            Map = info.Map, Mode = info.Mode,
            Phase = info.Phase, Players = info.Players, MaxPlayers = Math.Max(1, info.MaxPlayers), Password = info.HasPassword,
            Dedicated = info.Dedicated, Protocol = MasterApi.Hex(info.ProtocolHash), Content = MasterApi.Hex(info.ContentHash),
            Version = info.Version, Listed = Settings.Listed, NatPunch = Settings.NatPunch, Relay = Settings.Relay, PlayerNames = names, PlayerTeams = teams,
            RequireAccounts = Settings.RequireAccounts, Practice = info.Practice, AccountIds = AccountIds?.Invoke() ?? [], Kind = Settings.Kind, Region = Settings.Region ?? string.Empty,
            PlayerDetails = PlayerDetails?.Invoke() ?? [], RoundSecondsLeft = RoundSecondsLeft?.Invoke() ?? -1, CommandResults = TakeCommandResults(),
            RejoinAccounts = RejoinAccounts?.Invoke() ?? [],
        };
    }

    private List<MasterCommandResult> TakeCommandResults()
    {
        List<MasterCommandResult> results = [.. _commandResults];
        _commandResults.Clear();
        return results;
    }

    private void Completed(Task<HeartbeatResponse> task, long now)
    {
        if (!task.IsCompletedSuccessfully)
        {
            string message = task.Exception?.InnerException?.Message ?? "the master did not answer";
            if (task.Exception?.InnerException is MasterException { StatusCode: 404 })
            {
                // The master forgot this server (restarted, or the listing lapsed): register again.
                ServerId = null;
                _hostToken = 0;
                _nextHeartbeat = now;
            }

            if (message != LastError)
            {
                LastError = message;
                Notice?.Invoke("Master server: " + message);
            }

            return;
        }

        HeartbeatResponse r = task.Result;
        bool first = ServerId is null;
        ServerId = r.ServerId;
        if (RelayFrames.TryParseToken(r.HostToken, out ulong token))
        {
            _hostToken = token;
        }

        PublicAddress = r.PublicAddress;
        LastError = null;
        if (r.Trusted != Trusted || (first && _client.ServerKey is not null && !r.Trusted))
        {
            Trusted = r.Trusted;
            if (_client.ServerKey is not null)
            {
                Notice?.Invoke(Trusted
                    ? "The master server trusts this server: its admins are admins here and rounds count for stats."
                    : "The master server does not know this server's key: no master admins, rounds do not count.");
            }
        }

        if (!string.Equals(r.TicketKey, _ticketKeyText, StringComparison.Ordinal))
        {
            ECDsa? key = PlayerTicket.ImportPublicKey(r.TicketKey);
            if (key is not null || string.IsNullOrEmpty(r.TicketKey))
            {
                ECDsa? old = TicketKey;
                TicketKey = key;
                _ticketKeyText = r.TicketKey;
                old?.Dispose();
            }
        }

        if (r.KickAccounts is { Count: > 0 } kicks)
        {
            KickAccounts?.Invoke(kicks);
        }

        // Only a master that trusts this server's key sends commands; ignore any from one that does not.
        if (r.Trusted && r.Commands is { Count: > 0 } commands)
        {
            Commands?.Invoke(commands);
        }

        // While the master's probes still run, ask again soon so the host learns how players reach it.
        _nextHeartbeat = now + (r.Reachability == Reachability.Unknown ? 5 : Math.Clamp(r.HeartbeatSeconds, 5, 300)) * 1_000_000L;
        if (Settings.NatPunch && r.Introducer is { } introducer && _introducer is null)
        {
            _introducer = TryResolve(introducer);
            _nextIntroduction = now;
        }

        if (Settings.Relay && r.Relay is { } relay && _relay is null && _hostToken != 0)
        {
            _relay = TryResolve(relay);
            if (_relay is not null)
            {
                _udp.AttachRelay(_relay, _hostToken);
                _nextRelayHello = now + RelayHelloMicros;
            }
        }

        if (first)
        {
            Notice?.Invoke(Settings.Listed
                ? $"Listed on the master server as {r.ServerId} ({r.PublicAddress})."
                : $"Registered with the master server as {r.ServerId}, not listed: players join with the server id.");
            if (r.Message is { Length: > 0 } message)
            {
                Notice?.Invoke(message);
            }
        }

        if (r.Reachability != _lastReach)
        {
            _lastReach = r.Reachability;
            Reachability = r.Reachability;
            string? text = r.Reachability switch
            {
                Reachability.Direct => "Players reach this server directly.",
                Reachability.NeedsIntro => "Players reach this server through a NAT punch (no port forwarding found).",
                Reachability.RelayOnly => "Players reach this server through the relay only (strict NAT or carrier-grade NAT).",
                _ => null,
            };
            if (text is not null)
            {
                Notice?.Invoke(text);
            }
        }
    }

    private static IPEndPoint? TryResolve(string address)
    {
        try
        {
            return LiteNetLibTransport.Resolve(address);
        }
        catch (Exception e) when (e is FormatException or SocketException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// The host's own addresses with the game port (for players behind the same router): its IPv4 addresses, then up
    /// to two IPv6 ones (global or unique local, the stable ones before temporary privacy addresses; never link-local,
    /// which needs the interface's scope). The game socket listens on both, so a player whose network has IPv6 can
    /// reach the host on it without a NAT in the way.
    /// </summary>
    public static IReadOnlyList<string> LanAddresses(int port)
    {
        var list = new List<string>();
        var v6 = new List<(IPAddress Address, bool Temporary)>();
        try
        {
            foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                {
                    continue;
                }

                foreach (UnicastIPAddressInformation a in nic.GetIPProperties().UnicastAddresses)
                {
                    if (a.Address.AddressFamily == AddressFamily.InterNetwork && list.Count < 6)
                    {
                        list.Add(LiteNetLibTransport.FormatEndPoint(new IPEndPoint(a.Address, port)));
                    }
                    else if (a.Address.AddressFamily == AddressFamily.InterNetworkV6 && !a.Address.IsIPv6LinkLocal &&
                        !a.Address.IsIPv6SiteLocal && !a.Address.IsIPv6Multicast && !a.Address.IsIPv4MappedToIPv6 &&
                        !IPAddress.IPv6Loopback.Equals(a.Address))
                    {
                        v6.Add((a.Address, OperatingSystem.IsWindows() && a.SuffixOrigin == SuffixOrigin.Random));
                    }
                }
            }
        }
        catch (NetworkInformationException)
        {
        }

        v6.Sort((x, y) => x.Temporary.CompareTo(y.Temporary));
        for (int i = 0; i < v6.Count && i < 2; i++)
        {
            list.Add(LiteNetLibTransport.FormatEndPoint(new IPEndPoint(v6[i].Address, port)));
        }

        return list;
    }

    /// <summary>Takes the server off the list (waits at most two seconds).</summary>
    public void Unlist()
    {
        if (ServerId is { } id && _hostToken != 0)
        {
            try
            {
                _client.UnlistAsync(id, RelayFrames.TokenText(_hostToken)).Wait(TimeSpan.FromSeconds(2));
            }
            catch (AggregateException)
            {
                // The listing lapses on its own 90 s after the last heartbeat.
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_relay is not null)
        {
            _udp.DetachRelay(_relay);
        }

        _client.Dispose();
        TicketKey?.Dispose();
        TicketKey = null;
    }
}
