using System;
using System.Collections.Generic;
using OpenScct.Net;
using OpenScct.Net.Discovery;

namespace OpenScct.Server.Online;

/// <summary>Which way a client may reach a server: what the master's reachability suggests, or one way only.</summary>
public enum ConnectPath
{
    Auto,
    Direct,
    Intro,
    Relay,
}

/// <summary>One way to try: a direct address, a NAT punch through an introducer, a relay route, or a caller's transport.</summary>
public enum ConnectKind
{
    Direct,
    Intro,
    Relay,
    Custom,
}

/// <summary>One connection attempt: the kind, the address, and for a punch the introducer and its token.</summary>
public sealed record JoinAttempt(ConnectKind Kind, string Address, string? Introducer = null, string? PunchToken = null)
{
    /// <summary>An address on the player's own network: it answers at once or not at all, so it is given less time.</summary>
    public bool IsLan { get; init; }

    public string Describe() => Kind switch
    {
        ConnectKind.Direct => (IsLan ? "LAN " : "direct ") + Address,
        ConnectKind.Intro => "NAT punch via " + Introducer,
        ConnectKind.Relay => "relay " + Address,
        _ => Address,
    };
}

/// <summary>
/// Where a client joins and the attempts to get there, in order (docs/ONLINE.md, "Joining"): a typed address or a LAN
/// answer is one direct attempt; a listed server's join answer gives, by its reachability, the host's own network
/// address when the player shares its router, the public address, a NAT punch and the relay.
/// </summary>
public sealed class JoinTarget
{
    private JoinTarget(string name, IReadOnlyList<JoinAttempt> attempts, Func<ITransport>? transport)
    {
        Name = name;
        Attempts = attempts;
        CustomTransport = transport;
    }

    /// <summary>What the join screen calls the server.</summary>
    public string Name { get; }

    public IReadOnlyList<JoinAttempt> Attempts { get; }

    /// <summary>For <see cref="ConnectKind.Custom"/>: the transport to connect with (a listen host's loopback).</summary>
    public Func<ITransport>? CustomTransport { get; }

    /// <summary>The server's content hash and protocol as the list gave them (0 when unknown).</summary>
    public ulong ContentHash { get; init; }

    public ulong ProtocolHash { get; init; }

    public string? Map { get; init; }

    /// <summary>The master's id for a listed server (from its join answer), or null for a typed address.</summary>
    public string? MasterId { get; init; }

    /// <summary>A typed "host", "host:port" or "[v6]:port" address (port 28888 when none is given).</summary>
    public static JoinTarget FromAddress(string address)
    {
        ArgumentException.ThrowIfNullOrEmpty(address);
        string a = address.Trim();
        bool hasPort = a.StartsWith('[') ? a.Contains("]:", StringComparison.Ordinal) : a.AsSpan().Count(':') == 1;
        if (!hasPort)
        {
            a = (a.Contains(':', StringComparison.Ordinal) && !a.StartsWith('[') ? "[" + a + "]" : a) + ":" +
                BuildInfo.DefaultGamePort.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return new JoinTarget(a, [new JoinAttempt(ConnectKind.Direct, a)], null);
    }

    /// <summary>A transport the caller makes (the listen host's own player over loopback).</summary>
    public static JoinTarget Custom(string name, Func<ITransport> transport, string address) =>
        new(name, [new JoinAttempt(ConnectKind.Custom, address)], transport ?? throw new ArgumentNullException(nameof(transport)));

    /// <summary>The attempts for a listed server, from the master's join answer.</summary>
    public static JoinTarget FromJoin(JoinResponse join, ConnectPath path = ConnectPath.Auto)
    {
        ArgumentNullException.ThrowIfNull(join);
        var direct = new List<JoinAttempt>();
        if (join.SameNetwork)
        {
            foreach (string lan in join.LanAddresses)
            {
                direct.Add(new JoinAttempt(ConnectKind.Direct, lan) { IsLan = true });
            }
        }

        if (join.Direct is { Length: > 0 } d)
        {
            direct.Add(new JoinAttempt(ConnectKind.Direct, d));
        }

        JoinAttempt? intro = join.Introducer is { Length: > 0 } i && join.PunchToken is { Length: > 0 } token
            ? new JoinAttempt(ConnectKind.Intro, join.Direct ?? string.Empty, i, token)
            : null;
        JoinAttempt? relay = join.Relay is { Length: > 0 } r && RelayFrames.TryParseToken(join.RelayToken, out ulong relayToken)
            ? new JoinAttempt(ConnectKind.Relay, LiteNetLibTransport.RelayAddress(r, relayToken))
            : null;

        var attempts = new List<JoinAttempt>();
        void Add(JoinAttempt? a)
        {
            if (a is not null)
            {
                attempts.Add(a);
            }
        }

        switch (path)
        {
            case ConnectPath.Direct:
                attempts.AddRange(direct);
                break;
            case ConnectPath.Intro:
                Add(intro);
                break;
            case ConnectPath.Relay:
                Add(relay);
                break;
            default:
                switch (join.Reachability)
                {
                    case Reachability.RelayOnly:
                        if (join.SameNetwork)
                        {
                            attempts.AddRange(direct);
                        }

                        Add(relay);
                        break;
                    case Reachability.NeedsIntro:
                        if (join.SameNetwork)
                        {
                            attempts.AddRange(direct);
                        }

                        Add(intro);
                        Add(relay);
                        break;
                    default:
                        attempts.AddRange(direct);
                        Add(intro);
                        Add(relay);
                        break;
                }

                break;
        }

        MasterApi.TryParseHex(join.Content, out ulong content);
        MasterApi.TryParseHex(join.Protocol, out ulong protocol);
        return new JoinTarget(join.Name, attempts, null) {
            ContentHash = content,
            ProtocolHash = protocol,
            Map = join.Map,
            MasterId = join.ServerId.Length > 0 ? join.ServerId : null,
        };
    }
}
