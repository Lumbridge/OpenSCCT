using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using OpenScct.Net.Discovery;

namespace OpenScct.Server.Online;

/// <summary>
/// Checks the master tickets players join with: signed by the master, for this server, not expired and not used
/// before (each ticket id is taken once, until it would have expired anyway). The hub's check; the online server keeps
/// the same rules in its own CheckTicket.
/// </summary>
public sealed class TicketGate
{
    private readonly Dictionary<ulong, DateTimeOffset> _used = new();

    /// <summary>
    /// <paramref name="text"/>'s ticket when it is good for <paramref name="serverId"/> under <paramref name="key"/>,
    /// else null with the reason (no ticket, no key from the master yet, a bad or used ticket).
    /// </summary>
    public PlayerTicket? Check(string text, ECDsa? key, string? serverId, DateTimeOffset now, out string problem)
    {
        if (text.Length == 0)
        {
            problem = "no ticket";
            return null;
        }

        if (key is null || serverId is null)
        {
            problem = "no ticket key from the master yet";
            return null;
        }

        Forget(now);
        if (!PlayerTicket.TryVerify(text, key, serverId, now, out PlayerTicket? ticket, out string why) || ticket is null)
        {
            problem = why;
            return null;
        }

        if (_used.ContainsKey(ticket.TicketId))
        {
            problem = "ticket already used";
            return null;
        }

        problem = string.Empty;
        return ticket;
    }

    /// <summary>Takes the ticket for good (the player is in): it cannot be used again.</summary>
    public void Take(PlayerTicket ticket) => _used[ticket.TicketId] = ticket.Expires;

    private void Forget(DateTimeOffset now)
    {
        if (_used.Count == 0)
        {
            return;
        }

        var expired = new List<ulong>();
        foreach ((ulong id, DateTimeOffset expires) in _used)
        {
            if (expires < now)
            {
                expired.Add(id);
            }
        }

        foreach (ulong id in expired)
        {
            _used.Remove(id);
        }
    }
}
