using System;
using System.Diagnostics;
using System.Threading;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Net;
using OpenScct.Server;
using Xunit;

namespace OpenScct.Integration.Tests;

/// <summary>Handshake outcomes, leaving, listen mode and a real UDP session.</summary>
public sealed class SessionTests
{
    private sealed class StopwatchClock : IClock
    {
        private readonly Stopwatch _watch = Stopwatch.StartNew();

        public long NowMicroseconds => _watch.ElapsedTicks * 1_000_000 / Stopwatch.Frequency;
    }

    [Theory]
    [InlineData("SvmMoveCommands", "move-commands-9")]
    [InlineData("SvmMoveCommands", "move-commands-10")]
    [InlineData("SvmMoveCommands", "move-commands-11")]
    [InlineData("SvmMoveCommands", "move-commands-12")]
    [InlineData("SvmMoveCommands", "move-commands-13")]
    [InlineData("SvmCombatCall", "combat2")]
    [InlineData("SvmCombatCall", "combat3-authoritative-ammo")]
    [InlineData("SvmCombatCall", "combat4-shot-clock")]
    public void PreviousNetworkContractIsRejectedBeforeJoining(string functionName, string previousSignature)
    {
        var clock = new VirtualClock();
        var network = new SimLinkNetwork(clock, 5, SimLinkProfile.A);
        using var serverTransport = network.Bind("server");
        using var server = new ServerHost(serverTransport, new ServerSettings { Classes = NetClasses.Create(), Maps = WalkerRig.Maps });
        var oldClasses = NetClasses.Create();
        RepExtension extension = oldClasses.Find(typeof(OpenScct.Game.SBase.SPlayerController))!.Extension!;
        int command = extension.Functions.FindIndex(f => f.Name == functionName);
        Assert.True(command >= 0);
        extension.Functions[command] = extension.Functions[command] with { Signature = previousSignature, AcceptReordered = false };
        using var clientTransport = network.Bind("client");
        using var client = new ClientSession(clientTransport, "server", 0, new ClientSettings { Classes = oldClasses, Maps = WalkerRig.Maps });
        Assert.NotEqual(server.Driver.ProtocolHash, client.Driver.ProtocolHash);
        for (int frame = 1; frame <= 300 && client.State == EClientState.Connecting; frame++)
        {
            clock.AdvanceTo(VirtualClock.TickToMicroseconds(frame, Sim.Hz));
            server.Tick(clock.NowMicroseconds);
            client.Tick(clock.NowMicroseconds);
        }
        Assert.Equal(EClientState.Rejected, client.State);
        Assert.Equal(ControlMessage.VersionMismatchText, client.RejectReason);
        Assert.Equal("OpenScct.Net", client.RejectFile);
        Assert.Empty(server.Players);
    }

    [Fact]
    public void ContentMismatchIsRejectedWithTheOriginalWording()
    {
        var clock = new VirtualClock();
        var network = new SimLinkNetwork(clock, 5, SimLinkProfile.C);
        using SimLinkTransport serverTransport = network.Bind("server");
        using var server = new ServerHost(serverTransport, new ServerSettings { Classes = WalkerRig.Classes, Maps = WalkerRig.Maps });
        using SimLinkTransport clientTransport = network.Bind("client");
        using var client = new ClientSession(clientTransport, "server", 0,
            new ClientSettings { ContentHash = 12345, Classes = WalkerRig.Classes, Maps = WalkerRig.Maps });

        for (int frame = 1; frame <= 300 && client.State == EClientState.Connecting; frame++)
        {
            clock.AdvanceTo(VirtualClock.TickToMicroseconds(frame, Sim.Hz));
            server.Tick(clock.NowMicroseconds);
            client.Tick(clock.NowMicroseconds);
        }

        Assert.Equal(EClientState.Rejected, client.State);
        Assert.Equal(ControlMessage.VersionMismatchText, client.RejectReason);
        Assert.Equal(server.Map.ContentFile, client.RejectFile);
        Assert.Empty(server.Players);

        // The refused connection goes away on the server too.
        for (int frame = 301; frame <= 600; frame++)
        {
            clock.AdvanceTo(VirtualClock.TickToMicroseconds(frame, Sim.Hz));
            server.Tick(clock.NowMicroseconds);
            client.Tick(clock.NowMicroseconds);
        }

        Assert.Empty(server.Driver.ClientConnections);
    }

    [Fact]
    public void FifthPlayerIsToldTheServerIsFull()
    {
        using WalkerRig rig = WalkerRig.Create(null, 0, 5, new ServerSettings { MaxPlayers = 4 });
        for (int i = 0; i < 120; i++)
        {
            rig.Step(WalkPatterns.For);
        }

        int joined = 0;
        int full = 0;
        foreach (ClientSession client in rig.Clients)
        {
            if (client.State == EClientState.Joined)
            {
                joined++;
            }
            else if (client.State == EClientState.Rejected && client.RejectReason == "The server is full")
            {
                full++;
            }
        }

        Assert.Equal(4, joined);
        Assert.Equal(1, full);
        Assert.Equal(4, rig.Server.Players.Count);
    }

    [Fact]
    public void LeavingRemovesTheWalkerEverywhere()
    {
        using WalkerRig rig = WalkerRig.Create(SimLinkProfile.B, 12UL, 4);
        rig.WaitForAllWalkers(4, input: WalkPatterns.For);
        ClientSession leaving = rig.Clients[3];
        leaving.Disconnect();
        Assert.Equal(EClientState.Disconnected, leaving.State);

        for (int i = 0; i < 120; i++)
        {
            rig.Step(WalkPatterns.For);
        }

        Assert.Equal(3, rig.Server.Players.Count);

        // Late or duplicated packets of the closed connection must not open a new one.
        Assert.Equal(3, rig.Server.Driver.ClientConnections.Count);
        for (int c = 0; c < 3; c++)
        {
            Assert.Equal(3, WalkerRig.LiveWalkers(rig.Clients[c]));
        }

        int serverWalkers = 0;
        foreach (CubeWalker _ in rig.Server.World.Level.AllActors<CubeWalker>())
        {
            serverWalkers++;
        }

        Assert.Equal(3, serverWalkers);
    }

    [Fact]
    public void ClientRolesFollowOwnership()
    {
        using WalkerRig rig = WalkerRig.Create(null, 0, 2);
        rig.WaitForAllWalkers(2, input: WalkPatterns.For);
        foreach (ClientSession client in rig.Clients)
        {
            int autonomous = 0;
            int simulated = 0;
            foreach (ActorChannel channel in client.ActorChannels)
            {
                Actor actor = channel.Actor!;
                Assert.Equal(ENetRole.ROLE_Authority, actor.RemoteRole);
                if (actor is not CubeWalker)
                {
                    continue;
                }

                if (actor.Role == ENetRole.ROLE_AutonomousProxy)
                {
                    autonomous++;
                    Assert.Same(client.LocalWalker, actor);
                }
                else if (actor.Role == ENetRole.ROLE_SimulatedProxy)
                {
                    simulated++;
                }
            }

            Assert.Equal(1, autonomous);
            Assert.Equal(1, simulated);
            Assert.Equal(ENetMode.NM_Client, client.World.NetMode);
        }
    }

    [Fact]
    public void ListenServerRunsOnItsOwnThreadWithALoopbackPlayer()
    {
        var clock = new StopwatchClock();
        using var listen = new ListenServer(clock,
            new ServerSettings { NetRate = K.LanServerMaxTickRate, Classes = WalkerRig.Classes, Maps = WalkerRig.Maps });
        using ClientSession local = listen.ConnectLocalPlayer(clock.NowMicroseconds,
            new ClientSettings { PlayerName = "Host", Classes = WalkerRig.Classes, Maps = WalkerRig.Maps });

        long start = clock.NowMicroseconds;
        long next = start;
        FVector firstSeen = default;
        bool seen = false;
        while (clock.NowMicroseconds - start < 3_000_000)
        {
            long now = clock.NowMicroseconds;
            if (now < next)
            {
                Thread.Sleep(1);
                continue;
            }

            next += 1_000_000 / Sim.Hz;
            local.Input = new CubeInput(127, 0, (ushort)(local.LocalTick * 150));
            local.Tick(now);
            if (!seen && local.LocalWalker is { } walker && local.RenderTimeFixed >= 0)
            {
                seen = true;
                firstSeen = walker.Location;
            }
        }

        Assert.Null(listen.Failure);
        Assert.True(listen.IsRunning);
        Assert.Equal(EClientState.Joined, local.State);
        Assert.Equal(K.LanServerMaxTickRate, local.ServerNetRate);
        Assert.True(seen, "The local walker never replicated.");
        Assert.True(FVector.Dist(local.LocalWalker!.Location, firstSeen) > 100f, "The local walker did not move.");

        listen.Stop();
        Assert.Equal(ENetMode.NM_ListenServer, listen.Host.World.NetMode);
        Assert.Single(listen.Host.Players);
        Assert.InRange(listen.Host.World.TickCount, 120, 240);
    }

    [Fact]
    public void UdpSessionOnLocalhost()
    {
        var clock = new StopwatchClock();
        using LiteNetLibTransport serverTransport = LiteNetLibTransport.Listen(0, 8, loopbackOnly: true);
        using var server = new ServerHost(serverTransport, new ServerSettings { Classes = WalkerRig.Classes, Maps = WalkerRig.Maps });
        using LiteNetLibTransport clientTransport = LiteNetLibTransport.CreateClient(loopbackOnly: true);
        using var client = new ClientSession(clientTransport, "127.0.0.1:" + serverTransport.LocalPort.ToString(System.Globalization.CultureInfo.InvariantCulture),
            clock.NowMicroseconds, new ClientSettings { Classes = WalkerRig.Classes, Maps = WalkerRig.Maps });

        long start = clock.NowMicroseconds;
        long next = start;
        while (clock.NowMicroseconds - start < 5_000_000)
        {
            long now = clock.NowMicroseconds;
            if (now < next)
            {
                Thread.Sleep(1);
                continue;
            }

            next += 1_000_000 / Sim.Hz;
            server.Tick(now);
            client.Input = new CubeInput(127, 0, 16384);
            client.Tick(now);
            if (client.LocalWalker is { } w && w.Location.Y > 900f)
            {
                break;
            }
        }

        Assert.Equal(EClientState.Joined, client.State);
        Assert.NotNull(client.LocalWalker);
        Assert.True(client.LocalWalker!.Location.Y > 900f, $"Walker at {client.LocalWalker.Location} did not walk far enough.");
        Assert.Single(server.Players);
    }
}
