using System;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Net;
using OpenScct.Net.Voice;
using OpenScct.Server;

namespace OpenScctGame.Voice;

/// <summary>
/// Wiring voice chat into the game's sessions (docs/VOICE.md "Hosting"). Godot-free.
/// <list type="bullet">
/// <item>Dedicated or inline host: <c>var t = VoiceHosting.Wrap(udp)</c>, <c>new ServerHost(t, ...)</c>, then
/// <see cref="CreateRelay"/>. The relay runs inside the driver's receive pass.</item>
/// <item>Listen server with remote players: <c>new ListenServer(clock, settings, VoiceHosting.WrapListenRemote(udp))</c>,
/// then <see cref="CreateRelay"/>; the host's own player talks through <see cref="AttachLocalPlayer"/>.</item>
/// <item>Client of a remote host: <c>new ClientSession(VoiceHosting.Wrap(udp), address, ...)</c>, then
/// <see cref="VoiceChat.Connect(VoiceTransport, EndpointId, IClock)"/> with the session's server endpoint.</item>
/// </list>
/// A host whose driver does not read through a <see cref="VoiceTransport"/> calls <see cref="VoiceRelay.Tick"/> on its
/// own thread after each server tick.
/// </summary>
public static class VoiceHosting
{
    /// <summary>The transport a server or client driver uses, with voice on it.</summary>
    public static VoiceTransport Wrap(ITransport transport) => new(transport);

    /// <summary>A listen server's remote transport (index 1 of its composite transport, after loopback).</summary>
    public static VoiceTransport WrapListenRemote(ITransport transport) => new(transport, compositeIndex: 1);

    /// <summary>The server's relay, routing by the game's players and the SBase special cases.</summary>
    public static VoiceRelay CreateRelay(ServerHost host, VoiceTransport? transport, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(host);
        return new VoiceRelay(transport, new NetDriverVoiceDirectory(host.Driver, new SBaseVoiceLinks()), clock);
    }

    /// <summary>Connects a listen host's own player: the chat talks to the relay in this process.</summary>
    public static VoiceClient AttachLocalPlayer(VoiceRelay relay, VoiceChat chat, ClientSession session, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(relay);
        ArgumentNullException.ThrowIfNull(chat);
        ArgumentNullException.ThrowIfNull(session);
        var link = new VoiceLocalLink();
        relay.AttachLocal(link);
        VoiceClient client = chat.Connect(link, clock);
        Bind(chat, session.World);
        chat.ResolveLocalTalker = () => LocalTalker(session);
        return client;
    }

    /// <summary>Connects a client of a remote host whose session reads through <paramref name="transport"/>.</summary>
    public static VoiceClient AttachRemotePlayer(VoiceChat chat, ClientSession session, VoiceTransport transport, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(chat);
        ArgumentNullException.ThrowIfNull(session);
        EndpointId server = session.Connection?.RemoteEndpoint ?? EndpointId.None;
        VoiceClient client = chat.Connect(transport, server, clock);
        Bind(chat, session.World);
        chat.ResolveLocalTalker = () => LocalTalker(session);
        return client;
    }

    /// <summary>Names and teams of talkers from a client world's replicated players.</summary>
    public static void Bind(VoiceChat chat, World world)
    {
        ArgumentNullException.ThrowIfNull(chat);
        ArgumentNullException.ThrowIfNull(world);
        chat.ResolvePlayer = talker =>
        {
            GameReplicationInfo? gri = world.Level.Info.GRI;
            if (gri is null)
            {
                return null;
            }

            foreach (PlayerReplicationInfo? pri in gri.PRIArray)
            {
                if (pri is not null && VoiceK.TalkerOf(pri.PlayerID) == talker)
                {
                    return (pri.PlayerName, pri.Team?.TeamIndex ?? -1);
                }
            }

            return null;
        };
    }

    private static ushort LocalTalker(ClientSession session) =>
        session.LocalPlayer?.PlayerReplicationInfo is { } pri ? VoiceK.TalkerOf(pri.PlayerID) : (ushort)0;
}
