using System;
using System.Collections.Generic;
using System.Net;
using System.Security.Cryptography;
using OpenScct.Net;
using OpenScct.Net.Discovery;

namespace OpenScct.Master;

/// <summary>One listed (or unlisted but registered) host.</summary>
internal sealed class ServerRecord
{
    public required string Id { get; init; }

    public required ulong HostToken { get; init; }

    public required HeartbeatRequest Last { get; set; }

    public required IPAddress PublicIp { get; set; }

    public double LastSeen { get; set; }

    /// <summary>The game socket as the host reported it (internal) and as the introducer saw it (external).</summary>
    public IPEndPoint? NatInternal { get; set; }

    public IPEndPoint? NatExternal { get; set; }

    public double NatSeen { get; set; } = double.NegativeInfinity;

    /// <summary>The game socket as the relay saw it (its hello).</summary>
    public IPEndPoint? RelayEndpoint { get; set; }

    public double RelaySeen { get; set; } = double.NegativeInfinity;

    /// <summary>When the last probe went out (negative: never), whether one got through, and to where.</summary>
    public double ProbedAt { get; set; } = double.NegativeInfinity;

    public bool ProbePending { get; set; }

    public bool ProbeOk { get; set; }

    public IPEndPoint? ProbeTarget { get; set; }

    /// <summary>The address and port the last probe went to, so a change probes again.</summary>
    public string ProbedFor { get; set; } = string.Empty;

    /// <summary>The trusted server entry whose key the last heartbeat carried, or null (not one of ours).</summary>
    public string? TrustedName { get; set; }

    public bool Trusted => TrustedName is not null;
}

/// <summary>A client's punch request, valid for a minute.</summary>
internal sealed record PunchTicket(string ServerId, double Expires);

/// <summary>A relay session between one client and one host.</summary>
internal sealed class RelaySession
{
    public required string ServerId { get; init; }

    public required ulong ClientToken { get; init; }

    public required uint SessionId { get; init; }

    public IPEndPoint? Client { get; set; }

    public double LastActivity { get; set; }

    public bool AnnouncedToHost { get; set; }

    // Rate windows, one per direction.
    public double WindowStart { get; set; }

    public int UpFrames { get; set; }

    public int UpBytes { get; set; }

    public int DownFrames { get; set; }

    public int DownBytes { get; set; }

    public long ForwardedUp { get; set; }

    public long ForwardedDown { get; set; }
}

/// <summary>
/// The master's state: servers by id and host token, punch tickets and relay sessions, under one lock (the HTTP
/// handlers and the UDP services share it). Times are seconds on the master's monotonic clock.
/// </summary>
internal sealed class ServerRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<string, ServerRecord> _servers = new(StringComparer.Ordinal);
    private readonly Dictionary<ulong, ServerRecord> _byHostToken = new();
    private readonly Dictionary<ulong, PunchTicket> _punches = new();
    private readonly Dictionary<ulong, RelaySession> _relayByToken = new();
    private readonly Dictionary<uint, RelaySession> _relayBySession = new();
    private readonly TimeProvider _time;
    private readonly long _start;

    public ServerRegistry(MasterOptions options, TimeProvider? time = null)
    {
        Options = options;
        _time = time ?? TimeProvider.System;
        _start = _time.GetTimestamp();
    }

    public MasterOptions Options { get; }

    public object Gate => _gate;

    public double Now => _time.GetElapsedTime(_start).TotalSeconds;

    public int ServerCount
    {
        get
        {
            lock (_gate)
            {
                return _servers.Count;
            }
        }
    }

    public int RelaySessionCount
    {
        get
        {
            lock (_gate)
            {
                return _relayByToken.Count;
            }
        }
    }

    /// <summary>Total datagrams the relay forwarded (both ways).</summary>
    public long RelayForwarded { get; set; }

    public static ulong NewToken()
    {
        Span<byte> bytes = stackalloc byte[8];
        ulong token;
        do
        {
            RandomNumberGenerator.Fill(bytes);
            token = BitConverter.ToUInt64(bytes);
        }
        while (token == 0);
        return token;
    }

    private static string NewId()
    {
        Span<byte> bytes = stackalloc byte[6];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>
    /// Applies a heartbeat from <paramref name="from"/>: a new record (null id) or an update of the record the id and
    /// host token name. Returns null with an error when the id is unknown, the token is wrong or a limit is reached.
    /// </summary>
    public ServerRecord? Heartbeat(HeartbeatRequest request, IPAddress from, out string? error, out int status)
    {
        lock (_gate)
        {
            double now = Now;
            if (request.ServerId is { Length: > 0 } id)
            {
                if (!_servers.TryGetValue(id, out ServerRecord? known) ||
                    !RelayFrames.TryParseToken(request.HostToken, out ulong token) || token != known.HostToken)
                {
                    error = "Unknown server id or wrong host token; register again without an id.";
                    status = 404;
                    return null;
                }

                known.Last = request;
                known.PublicIp = from;
                known.LastSeen = now;
                error = null;
                status = 200;
                return known;
            }

            if (_servers.Count >= Options.MaxServers)
            {
                error = "The master's list is full.";
                status = 503;
                return null;
            }

            int fromSame = 0;
            foreach (ServerRecord r in _servers.Values)
            {
                if (r.PublicIp.Equals(from))
                {
                    fromSame++;
                }
            }

            if (fromSame >= Options.MaxServersPerAddress)
            {
                error = "Too many servers from this address.";
                status = 429;
                return null;
            }

            var record = new ServerRecord
            {
                Id = NewId(), HostToken = NewToken(), Last = request, PublicIp = from, LastSeen = now,
            };
            _servers[record.Id] = record;
            _byHostToken[record.HostToken] = record;
            error = null;
            status = 200;
            return record;
        }
    }

    public ServerRecord? Find(string id)
    {
        lock (_gate)
        {
            return _servers.GetValueOrDefault(id);
        }
    }

    public ServerRecord? FindByHostToken(ulong token)
    {
        lock (_gate)
        {
            return _byHostToken.GetValueOrDefault(token);
        }
    }

    public bool Remove(string id, ulong hostToken)
    {
        lock (_gate)
        {
            if (!_servers.TryGetValue(id, out ServerRecord? record) || record.HostToken != hostToken)
            {
                return false;
            }

            RemoveLocked(record);
            return true;
        }
    }

    private void RemoveLocked(ServerRecord record)
    {
        _servers.Remove(record.Id);
        _byHostToken.Remove(record.HostToken);
        var sessions = new List<RelaySession>();
        foreach (RelaySession s in _relayByToken.Values)
        {
            if (s.ServerId == record.Id)
            {
                sessions.Add(s);
            }
        }

        foreach (RelaySession s in sessions)
        {
            CloseSessionLocked(s);
        }
    }

    /// <summary>A copy of every live record (for the list).</summary>
    public List<ServerRecord> Snapshot()
    {
        lock (_gate)
        {
            return new List<ServerRecord>(_servers.Values);
        }
    }

    /// <summary>
    /// How the record's host can be reached now: a probe got through (direct); a fresh introducer registration whose
    /// mapping the relay saw on the same port (or no relay to compare with) means a punch should work; a relay
    /// registration alone means relay only.
    /// </summary>
    public Reachability Classify(ServerRecord r)
    {
        double now = Now;
        bool nat = r.Last.NatPunch && r.NatExternal is not null && now - r.NatSeen <= Options.RegistrationSeconds;
        bool relay = r.Last.Relay && r.RelayEndpoint is not null && now - r.RelaySeen <= Options.RegistrationSeconds;
        if (r.ProbeOk)
        {
            return Reachability.Direct;
        }

        if (nat)
        {
            bool samePort = !relay || (r.RelayEndpoint!.Port == r.NatExternal!.Port && r.RelayEndpoint.Address.Equals(r.NatExternal.Address));
            if (samePort)
            {
                return Reachability.NeedsIntro;
            }
        }

        if (relay)
        {
            return Reachability.RelayOnly;
        }

        return r.ProbePending ? Reachability.Unknown : (nat ? Reachability.NeedsIntro : Reachability.Unknown);
    }

    /// <summary>A punch ticket for one client of <paramref name="serverId"/>.</summary>
    public ulong IssuePunch(string serverId)
    {
        lock (_gate)
        {
            ulong token = NewToken();
            _punches[token] = new PunchTicket(serverId, Now + 60);
            return token;
        }
    }

    public PunchTicket? TakePunch(ulong token)
    {
        lock (_gate)
        {
            if (_punches.Remove(token, out PunchTicket? ticket) && ticket.Expires >= Now)
            {
                return ticket;
            }

            return null;
        }
    }

    /// <summary>A relay session for one client of <paramref name="serverId"/>, or null when the relay is full.</summary>
    public RelaySession? OpenRelay(string serverId)
    {
        lock (_gate)
        {
            if (_relayByToken.Count >= Options.MaxRelaySessions || !_servers.TryGetValue(serverId, out ServerRecord? record))
            {
                return null;
            }

            int forServer = 0;
            foreach (RelaySession s in _relayByToken.Values)
            {
                if (s.ServerId == serverId)
                {
                    forServer++;
                }
            }

            if (forServer >= Math.Max(4, record.Last.MaxPlayers + 4))
            {
                return null;
            }

            uint session;
            do
            {
                session = (uint)NewToken();
            }
            while (session == 0 || _relayBySession.ContainsKey(session));

            var relay = new RelaySession { ServerId = serverId, ClientToken = NewToken(), SessionId = session, LastActivity = Now };
            _relayByToken[relay.ClientToken] = relay;
            _relayBySession[session] = relay;
            return relay;
        }
    }

    public RelaySession? RelayByToken(ulong token) => _relayByToken.GetValueOrDefault(token);

    public RelaySession? RelayBySession(uint session) => _relayBySession.GetValueOrDefault(session);

    public void CloseSessionLocked(RelaySession session)
    {
        _relayByToken.Remove(session.ClientToken);
        _relayBySession.Remove(session.SessionId);
    }

    /// <summary>Drops servers past their expiry, stale punch tickets and idle relay sessions.</summary>
    public void Sweep()
    {
        lock (_gate)
        {
            double now = Now;
            var expired = new List<ServerRecord>();
            foreach (ServerRecord r in _servers.Values)
            {
                if (now - r.LastSeen > Options.ExpirySeconds)
                {
                    expired.Add(r);
                }
            }

            foreach (ServerRecord r in expired)
            {
                RemoveLocked(r);
            }

            var stale = new List<ulong>();
            foreach (KeyValuePair<ulong, PunchTicket> p in _punches)
            {
                if (p.Value.Expires < now)
                {
                    stale.Add(p.Key);
                }
            }

            foreach (ulong token in stale)
            {
                _punches.Remove(token);
            }

            var idle = new List<RelaySession>();
            foreach (RelaySession s in _relayByToken.Values)
            {
                if (now - s.LastActivity > Options.RelayIdleSeconds)
                {
                    idle.Add(s);
                }
            }

            foreach (RelaySession s in idle)
            {
                CloseSessionLocked(s);
            }
        }
    }
}
