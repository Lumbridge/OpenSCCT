using System;
using OpenScct.Core;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Server.Online;

namespace OpenScct.Server.Hub;

/// <summary>The hub's player list lines and local chat (spec/additions/hub.md): text on the control channel, as the hub's other lines.</summary>
public sealed partial class HubServer
{
    private float _rosterAt = float.NegativeInfinity;

    /// <summary>Every player's <c>hub who</c> line to everyone, every <see cref="HubK.RosterSeconds"/> (their sides and pings change).</summary>
    private void TickRoster(float now)
    {
        if (now - _rosterAt < HubK.RosterSeconds || _players.Count == 0)
        {
            return;
        }

        _rosterAt = now;
        foreach (HubPlayer p in _players)
        {
            Host.Broadcast(HubProtocol.Who(p.Slot.PlayerId, SGameInfo.TeamIndexOf(p.Slot.Controller), PingOf(p), p.Verified, p.Slot.Name));
        }
    }

    /// <summary>A local chat line: to every player whose pawn is within <see cref="HubK.LocalChatRange"/> of the speaker's (him included).</summary>
    private void SayLocal(HubPlayer speaker, string text, float now)
    {
        string clean = text.Trim();
        if (clean.Length == 0 || now - speaker.LastLocal < HubK.LocalChatCooldownSeconds ||
            speaker.Slot.Controller?.Pawn is not { bDeleteMe: false } pawn)
        {
            return;
        }

        if (clean.Length > ChatK.MaxChars)
        {
            clean = clean[..ChatK.MaxChars];
        }

        speaker.LastLocal = now;
        string line = HubProtocol.Local(speaker.Slot.PlayerId, SGameInfo.TeamIndexOf(speaker.Slot.Controller), clean);
        float r2 = HubK.LocalChatRange * HubK.LocalChatRange;
        foreach (HubPlayer p in _players)
        {
            if (ReferenceEquals(p, speaker) || (p.Slot.Controller?.Pawn is { bDeleteMe: false } other && (other.Location - pawn.Location).SizeSquared() <= r2))
            {
                p.Slot.Connection.ControlChannel?.Send(ControlMessage.TextLine(line));
            }
        }
    }
}
