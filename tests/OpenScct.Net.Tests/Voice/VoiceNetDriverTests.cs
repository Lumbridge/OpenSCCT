using System;
using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Net.Voice;
using Xunit;

namespace OpenScct.Net.Tests.Voice;

/// <summary>
/// Voice on the game's own transport: a server and clients with real <see cref="NetDriver"/>s reading through
/// <see cref="VoiceTransport"/>, the server's relay routing by <see cref="NetDriverVoiceDirectory"/> (each connection's
/// PlayerController and PlayerReplicationInfo), and game traffic unaffected.
/// </summary>
public sealed class VoiceNetDriverTests
{
    /// <summary>Logs each Hello in as a PlayerController with a PRI on the team its name gives ("team0", "team1").</summary>
    private sealed class LoginNotify(World world, TeamInfo[] teams) : INetworkNotify
    {
        private int _nextId = 1;

        public readonly List<PlayerController> Players = new();

        public void NotifyControlMessage(NetConnection connection, in ControlMessage message)
        {
            if (message.Type != EControlMessage.Hello || !connection.Driver.IsServer)
            {
                return;
            }

            connection.SetOpen();
            PlayerController pc = world.Level.Spawn<PlayerController>()!;
            PlayerReplicationInfo pri = world.Level.Spawn<PlayerReplicationInfo>(owner: pc)!;
            pri.PlayerID = _nextId++;
            pri.Team = teams[message.Text == "team1" ? 1 : 0];
            pc.PlayerReplicationInfo = pri;
            connection.Actor = pc;
            connection.bJoined = true;
            Players.Add(pc);
        }
    }

    private sealed class ClientNotify : INetworkNotify
    {
        public void NotifyControlMessage(NetConnection connection, in ControlMessage message)
        {
        }
    }

    private sealed class Client
    {
        public required NetDriver Driver;
        public required VoiceTransport Transport;
        public required VoiceClient Voice;
        public required World World;
        public readonly List<float> Heard = new();
    }

    [Fact]
    public void VoiceRidesTheGameTransportAndRoutesByTheServersPlayers()
    {
        var clock = new VirtualClock();
        var network = new SimLinkNetwork(clock, 3, SimLinkProfile.B);
        var registry = new NetClassRegistry();
        var serverWorld = new World(1, ENetMode.NM_DedicatedServer);
        TeamInfo[] teams = [serverWorld.Level.Spawn<TeamInfo>()!, serverWorld.Level.Spawn<TeamInfo>()!];
        teams[0].TeamIndex = 0;
        teams[1].TeamIndex = 1;
        var notify = new LoginNotify(serverWorld, teams);
        using var serverTransport = new VoiceTransport(network.Bind("server"));
        using var server = new NetDriver(serverTransport, registry, notify, isServer: true) { World = serverWorld };
        VoiceRelay relay = VoiceRelay.ForDriver(serverTransport, server, clock);

        var clients = new List<Client>();
        foreach (string name in new[] { "team0", "team0", "team1" })
        {
            var world = new World(0, ENetMode.NM_Client);
            var transport = new VoiceTransport(network.Bind("client" + clients.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            var driver = new NetDriver(transport, registry, new ClientNotify(), isServer: false) { World = world };
            NetConnection connection = driver.Connect("server", clock.NowMicroseconds);
            connection.ControlChannel!.Send(ControlMessage.Hello(driver.ProtocolHash, 0, name, 10000));
            connection.SetOpen();
            var voice = new VoiceClient(new VoiceSettings { PushToTalk = true }, clock);
            voice.Connect(transport, connection.RemoteEndpoint);
            clients.Add(new Client { Driver = driver, Transport = transport, Voice = voice, World = world });
        }

        float[] tone = VoiceSignals.Tone(440, 6.0);
        var frame = new float[VoiceK.FrameSamples];
        long tick = 0;
        void Run(int frames, bool talk)
        {
            for (int f = 0; f < frames; f++)
            {
                // Three 60 Hz ticks per 20 ms voice frame.
                for (int t = 0; t < 3; t++)
                {
                    tick++;
                    clock.AdvanceTo(VirtualClock.TickToMicroseconds(tick, Sim.Hz));
                    long now = clock.NowMicroseconds;
                    server.TickDispatch(now);
                    serverWorld.Step();
                    server.TickFlush(now);
                    foreach (Client c in clients)
                    {
                        c.Driver.TickDispatch(now);
                        c.World.Step();
                        c.Driver.TickFlush(now);
                    }
                }

                Client talker = clients[0];
                talker.Voice.TalkKey = talk;
                int at = (int)(tick / 3 % 250) * VoiceK.FrameSamples;
                talker.Voice.SubmitCapture(tone.AsSpan(at, VoiceK.FrameSamples));
                foreach (Client c in clients)
                {
                    c.Voice.Update();
                    foreach (VoiceTalker heard in c.Voice.Talkers)
                    {
                        if (heard.ReadFrame(frame))
                        {
                            c.Heard.AddRange(frame);
                        }
                    }
                }
            }
        }

        // Join, then talk for 3 s.
        Run(25, talk: false);
        Assert.Equal(3, notify.Players.Count);
        Assert.Equal(3, relay.Roster.Count);
        Run(150, talk: true);

        // The game connections are healthy and the mate heard the tone; the enemy heard nothing.
        foreach (Client c in clients)
        {
            Assert.Equal(EConnectionState.USOCK_Open, c.Driver.ServerConnection!.State);
        }

        ushort talkerId = VoiceK.TalkerOf(notify.Players[0].PlayerReplicationInfo!.PlayerID);
        Assert.NotNull(clients[1].Voice.FindTalker(talkerId));
        Assert.True(clients[1].Heard.Count > 100 * VoiceK.FrameSamples, $"mate heard {clients[1].Heard.Count} samples");
        double hz = VoiceSignals.ZeroCrossingHz(clients[1].Heard.ToArray().AsSpan(clients[1].Heard.Count / 4, clients[1].Heard.Count / 2));
        Assert.InRange(hz, 425, 455);
        Assert.Empty(clients[2].Voice.Talkers);
        Assert.Equal(0, relay.Stats.UnknownSender);
        Assert.True(serverTransport.Stats.PacketsIn > 150);

        // Moving the enemy onto the talker's team routes voice to him at the next rebuild.
        notify.Players[2].PlayerReplicationInfo!.Team = teams[0];
        Run(50, talk: true);
        Assert.NotNull(clients[2].Voice.FindTalker(talkerId));

        // A player whose controller is dead (no longer in the roster when it leaves) and a spectator are covered by
        // NetDriverVoiceDirectory.LifeOf.
        notify.Players[1].PlayerReplicationInfo!.bOnlySpectator = true;
        Assert.Equal(EVoiceLife.Visitor, NetDriverVoiceDirectory.LifeOf(notify.Players[1], notify.Players[1].PlayerReplicationInfo!));
        Assert.Equal(EVoiceLife.Alive, NetDriverVoiceDirectory.LifeOf(notify.Players[0], notify.Players[0].PlayerReplicationInfo!));

        foreach (Client c in clients)
        {
            c.Voice.Dispose();
            c.Driver.Dispose();
            c.Transport.Dispose();
        }
    }
}
