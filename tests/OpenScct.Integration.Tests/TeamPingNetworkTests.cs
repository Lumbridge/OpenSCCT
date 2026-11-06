using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using OpenScct.Bots;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Camera;
using OpenScct.Game.Equipment;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Server;
using OpenScctGame.Play.Session;
using Xunit;

namespace OpenScct.Integration.Tests;

public sealed class TeamPingNetworkTests
{
    private sealed class IdleBrain : BotBrain
    {
        public override BotInput Think(BotContext context) => BotInput.Idle;
    }

    private sealed class LegacyControlPeer(Action<NetConnection, ControlMessage> receive) : INetworkNotify
    {
        public void NotifyControlMessage(NetConnection connection, in ControlMessage message) => receive(connection, message);
    }

    [Fact]
    public void ServerRelaysOnlyToNegotiatedLivingTeammatesAndThrottlesEachSender()
    {
        var clock = new VirtualClock();
        var network = new SimLinkNetwork(clock, 1105, SimLinkProfile.A);
        using var transport = network.Bind("server");
        var settings = new BotMatchSettings
        {
            Map = MapCatalog.TestBox, Maps = WalkerRig.Maps, Classes = WalkerRig.Classes,
            Bots = BotMatchSettings.TwoVersusTwo(_ => new IdleBrain(), _ => new IdleBrain()),
        };
        using var server = new ServerHost(transport, new ServerSettings
        {
            Map = settings.Map, Maps = settings.Maps, Classes = settings.Classes, GameOptions = settings.GameOptions,
            Profile = NetProfile.Enhanced, MaxPlayers = 6, PlayersToStart = 5,
        });
        const string legacyName = "LegacySpy";
        server.PlayerJoined += slot =>
        {
            // The mock omits gameplay RPCs. Count its loaded player in the dedicated start gate.
            if (slot.Name == legacyName) Assert.IsType<SPlayerController>(slot.Controller).PlayerWaitingToPlayToServer(true);
        };
        using var match = new BotMatch(server, settings, network.Bind, "server", 0);
        using var spectator = new ClientSession(network.Bind("spectator"), "server", 0, new ClientSettings
        {
            PlayerName = "Visitor", Gameplay = true, Maps = WalkerRig.Maps, Classes = WalkerRig.Classes,
            LoginOptions = "?SpectatorOnly=true?Team=255",
        }, ownsTransport: true);
        var legacyTexts = new List<string>();
        var legacyWorld = new World(0, ENetMode.NM_Client);
        var legacyNotify = new LegacyControlPeer((connection, message) =>
        {
            if (message.Type == EControlMessage.Welcome)
            {
                Assert.True(WalkerRig.Maps.TryLoad(legacyWorld, message.Text!, isClient: true, out var map, out var error), error);
                Assert.Equal(message.ContentHash, map!.ContentHash);
                connection.Driver.MapBinding.Bind(map.MapActors);
                if (message.PawnProxies == PawnProxyMode.Snapshots) _ = new PawnStateReplication(connection.Driver);
                connection.SetOpen();
                connection.ControlChannel!.Send(ControlMessage.Join());
            }
            else if (message is { Type: EControlMessage.Text or EControlMessage.Reject, Text: { } line })
            {
                // Observe the wire for the test while ignoring unrecognized session commands like a legacy build.
                legacyTexts.Add(line);
            }
        });
        using var legacyTransport = network.Bind("legacy-spy");
        using var legacyDriver = new NetDriver(legacyTransport, WalkerRig.Classes, legacyNotify, isServer: false) { World = legacyWorld };
        NetConnection legacy = legacyDriver.Connect("server", 0);
        legacy.ControlChannel!.Send(ControlMessage.Hello(legacyDriver.ProtocolHash, 0, legacyName + "?Team=0", 10000,
            WalkerRig.Maps.CharacterContentHash));
        List<TeamPingMark>[] seen = match.Bots.Select(bot => Observe(bot.Session)).ToArray();
        List<TeamPingMark> seenBySpectator = Observe(spectator);

        long tick = 0;
        void Step(int count)
        {
            for (int i = 0; i < count; i++)
            {
                clock.AdvanceTo(VirtualClock.TickToMicroseconds(++tick, Sim.Hz));
                match.Step(clock.NowMicroseconds);
                spectator.Tick(clock.NowMicroseconds);
                legacyDriver.TickDispatch(clock.NowMicroseconds);
                legacyDriver.TickFlush(clock.NowMicroseconds, replicate: false);
            }
        }

        Step(900);
        Assert.All(match.Bots, bot => Assert.True(bot.Session.SupportsTeamPings));
        Assert.True(legacy.State == EConnectionState.USOCK_Open, $"{legacy.CloseReason} ({string.Join(", ", legacyTexts)})");
        var legacyPlayer = Assert.IsType<SPlayerController>(server.Players.Single(slot => slot.Name == legacyName).Controller);
        Assert.True(TeamPingPlacement.TryGetTeam(legacyPlayer, out byte legacyTeam));
        Assert.Equal(0, legacyTeam);
        Assert.True(Assert.IsType<SPlayerController>(spectator.LocalPlayer).PlayerReplicationInfo!.bOnlySpectator);
        BotClient spy = match.Bots[0];
        BotClient merc = match.Bots[2];
        Assert.True(TeamPingPlacement.TryGetSource(spy.Controller, out Actor spySource, out _));
        Assert.True(TeamPingPlacement.TryGetSource(merc.Controller, out Actor mercSource, out _));
        FVector spyTarget = ValidNearbyTarget(match.ServerController(spy)!);
        FVector mercTarget = ValidNearbyTarget(match.ServerController(merc)!);

        // Deliberately bypass the public client checks. The server checks the current source, finite input and role.
        uint spyRef = ActorReference(spy.Session, spySource);
        uint mercRef = ActorReference(merc.Session, mercSource);
        spy.Session.SendText(RawRequest((TeamPingKind)255, spyRef, spyTarget));
        spy.Session.SendText(RawRequest(TeamPingKind.Generic, mercRef, spyTarget));
        spy.Session.SendText(RawRequest(TeamPingKind.Generic, spyRef, new FVector(float.NaN, 0, 0)));
        merc.Session.SendText(RawRequest(TeamPingKind.Mine, mercRef, mercTarget));
        spectator.SendText(RawRequest(TeamPingKind.Generic, spyRef, spyTarget));
        Step(20);
        Assert.All(seen, marks => Assert.Empty(marks));
        Assert.Empty(seenBySpectator);

        long earliestTick = server.World.TickCount;
        spy.Session.SendTeamPing(TeamPingKind.Mine, spySource, spyTarget);
        spy.Session.SendTeamPing(TeamPingKind.CoopBoost, spySource, spyTarget);
        merc.Session.SendTeamPing(TeamPingKind.Watch, mercSource, mercTarget);
        Step(25);
        TeamPingMark spyMark = Assert.Single(seen[0]);
        TeamPingMark mercMark = Assert.Single(seen[2]);
        Assert.Equal(new TeamPingMark(spy.Session.PlayerId, 0, TeamPingKind.Mine, spyTarget, spyMark.ServerTick), spyMark);
        Assert.Equal(new TeamPingMark(merc.Session.PlayerId, 1, TeamPingKind.Watch, mercTarget, mercMark.ServerTick), mercMark);
        Assert.InRange(spyMark.ServerTick, earliestTick, server.World.TickCount);
        Assert.Equal(spyMark, Assert.Single(seen[1]));
        Assert.Equal(mercMark, Assert.Single(seen[3]));
        Assert.Empty(seenBySpectator);

        Step(Sim.Hz);
        spy.Session.SendTeamPing(TeamPingKind.Check, spySource, spyTarget);
        Step(25);
        Assert.Equal(TeamPingKind.Check, seen[0].Last().Kind);
        Assert.Equal(2, seen[1].Count);
        Assert.Single(seen[2]);
        Assert.Single(seen[3]);

        Step(Sim.Hz);
        server.Game!.bGameEnded = true;
        spy.Session.SendTeamPing(TeamPingKind.Generic, spySource, spyTarget);
        Step(25);
        Assert.Equal(2, seen[0].Count);
        Assert.Equal(EConnectionState.USOCK_Open, legacy.State);
        Assert.Equal("#svm team-ping 1 offer", Assert.Single(legacyTexts, line => line.StartsWith("#svm team-ping ", StringComparison.Ordinal)));
    }

    [Fact]
    public void NewClientJoinsALegacyServerWithoutNegotiatingTeamPings()
    {
        var clock = new VirtualClock();
        var network = new SimLinkNetwork(clock, 1107, SimLinkProfile.A);
        var received = new List<EControlMessage>();
        var notify = new LegacyControlPeer((connection, message) =>
        {
            received.Add(message.Type);
            if (message.Type == EControlMessage.Hello)
            {
                Assert.Equal(connection.Driver.ProtocolHash, message.ProtocolHash);
                connection.SetOpen();
                connection.ControlChannel!.Send(ControlMessage.Welcome(1, MapCatalog.Lobby, 0, 60, MapCatalog.LobbyContentHash));
            }
            else if (message.Type == EControlMessage.Join)
            {
                connection.bJoined = true;
            }
        });
        using var transport = network.Bind("legacy-server");
        using var legacyServer = new NetDriver(transport, WalkerRig.Classes, notify, isServer: true);
        using var client = new ClientSession(network.Bind("new-client"), "legacy-server", 0,
            new ClientSettings { Classes = WalkerRig.Classes, Maps = WalkerRig.Maps }, ownsTransport: true);
        for (int tick = 1; tick <= 90; tick++)
        {
            clock.AdvanceTo(VirtualClock.TickToMicroseconds(tick, Sim.Hz));
            legacyServer.TickDispatch(clock.NowMicroseconds);
            legacyServer.TickFlush(clock.NowMicroseconds, replicate: false);
            client.Tick(clock.NowMicroseconds);
        }

        Assert.Equal(new[] { EControlMessage.Hello, EControlMessage.Join }, received);
        Assert.Equal(EClientState.Joined, client.State);
        Assert.False(client.SupportsTeamPings);
    }

    [Fact]
    public void MercFreeForAllDoesNotNegotiatePings()
    {
        var clock = new VirtualClock();
        var network = new SimLinkNetwork(clock, 1106, SimLinkProfile.A);
        using var match = new BotMatch(new BotMatchSettings
        {
            Map = MapCatalog.TestBox, Maps = WalkerRig.Maps, Classes = WalkerRig.Classes,
            GameOptions = "?GameMode=7?TimeLimit=10",
            Bots = [new("Merc1", BotTeam.Merc, () => new IdleBrain()), new("Merc2", BotTeam.Merc, () => new IdleBrain())],
        }, network.Bind, "server", 0);
        for (int tick = 1; tick <= 900; tick++)
        {
            clock.AdvanceTo(VirtualClock.TickToMicroseconds(tick, Sim.Hz));
            match.Step(clock.NowMicroseconds);
        }

        Assert.Equal(EGameMode.GM_MercDeathMatch, match.ServerGri!.GameMode);
        Assert.All(match.Bots, bot =>
        {
            Assert.Equal(EClientState.Joined, bot.Session.State);
            Assert.False(bot.Session.SupportsTeamPings);
        });
    }

    [Fact]
    public void OfflineListenPlayerCanPingFromTheCurrentNetworkCamera()
    {
        Assert.SkipWhen(WalkerRig.Maps.FindFolder("AquaE") is null, "Aquarius content is required.");
        var clock = new StepClock();
        using var session = new PlaySession(new PlaySettings
        {
            Map = "AquaE", Side = PlaySide.Merc, Rules = PlayRules.MapSettings,
            HostMode = PlayHostMode.Inline, MatchSeed = 3,
        }, clock);
        List<TeamPingMark> seen = Observe(session.Client);

        void Step(int count)
        {
            for (int i = 0; i < count; i++)
            {
                clock.NowMicroseconds += 1_000_000 / Sim.Hz;
                session.Step(clock.NowMicroseconds);
                session.Input.EndTick();
            }
        }

        Step(360);
        Assert.True(session.Client.SupportsTeamPings);
        var player = Assert.IsType<SPlayerController>(session.LocalController);
        var authority = Assert.IsType<SPlayerController>(session.ServerController);
        var gadget = Assert.IsType<SGadCamNetwork>(PawnInventory.FindGadgetType(player.SBPawn!, new ClassRef("SBase.SGadCamNetwork")));
        player.SBPawn!.SelectedGadget = gadget;
        session.Input.Set("bAltFire", true);
        session.Input.Commands.Add(new InputCommand("AltFire"));
        Step(8);
        session.Input.Set("bAltFire", false);
        Step(120);

        Assert.True(TeamPingPlacement.TryGetSource(player, out Actor clientSource, out _));
        var clientCamera = Assert.IsType<SCamNetwork>(clientSource);
        Assert.True(TeamPingPlacement.TryGetSource(authority, out Actor serverSource, out _));
        Assert.IsType<SCamNetwork>(serverSource);
        PlayerCalcView.StickyCamView(player, false, out _, out FVector origin, out FRotator rotation);
        Assert.True(TeamPingPlacement.TryProjectTarget(player, clientCamera, origin, rotation, out FVector point));
        Assert.True(TeamPingPlacement.TryValidateTarget(authority, serverSource, point));

        // A request captured before switching into the camera cannot borrow its authoritative origin.
        session.Client.SendText(RawRequest(TeamPingKind.Generic, ActorReference(session.Client, player.SBPawn), point));
        Step(10);
        Assert.Empty(seen);
        session.Client.SendTeamPing(TeamPingKind.Generic, clientCamera, point);
        Step(30);
        Assert.Equal(point, Assert.Single(seen).Location);
    }

    private static List<TeamPingMark> Observe(ClientSession session)
    {
        var marks = new List<TeamPingMark>();
        session.TeamPingReceived += marks.Add;
        return marks;
    }

    private static FVector ValidNearbyTarget(SPlayerController player)
    {
        Assert.True(TeamPingPlacement.TryGetSource(player, out Actor source, out FVector origin));
        FVector point = origin + new FVector(0, 0, 20);
        Assert.True(TeamPingPlacement.TryValidateTarget(player, source, point));
        return point;
    }

    private static uint ActorReference(ClientSession session, Actor actor) =>
        Assert.IsType<ActorChannel>(session.Connection!.FindActorChannel(actor)).NetGuid * 2 + 2;

    private static string RawRequest(TeamPingKind kind, uint sourceRef, FVector point)
    {
        Span<byte> payload = stackalloc byte[17];
        payload[0] = (byte)kind;
        BinaryPrimitives.WriteUInt32LittleEndian(payload[1..], sourceRef);
        BinaryPrimitives.WriteSingleLittleEndian(payload[5..], point.X);
        BinaryPrimitives.WriteSingleLittleEndian(payload[9..], point.Y);
        BinaryPrimitives.WriteSingleLittleEndian(payload[13..], point.Z);
        return "#svm team-ping 1 req " + Convert.ToBase64String(payload);
    }
}
