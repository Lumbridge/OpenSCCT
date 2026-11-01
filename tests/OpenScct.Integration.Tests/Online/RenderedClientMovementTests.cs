using System;
using System.Linq;
using OpenScct.Content;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Server;
using OpenScctGame.Play.Session;
using Xunit;

namespace OpenScct.Integration.Tests.Online;

public sealed class RenderedClientMovementTests
{
    [Theory]
    [InlineData(1, NetProfile.Stock)]
    [InlineData(2, NetProfile.Stock)]
    [InlineData(4, NetProfile.Stock)]
    [InlineData(1, NetProfile.Enhanced)]
    [InlineData(2, NetProfile.Enhanced)]
    [InlineData(4, NetProfile.Enhanced)]
    public void BatchedClientFramesDoNotAccumulateInputDelay(int ticksPerFrame, NetProfile profile)
        => RunBatchedClient(ticksPerFrame, profile, "TestBox", 0);

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void RealMapClientBudgetKeepsInputCurrent(int team) => RunBatchedClient(2, NetProfile.Enhanced, "AquaE", team, 5000);

    private static void RunBatchedClient(int ticksPerFrame, NetProfile profile, string map, int team, int? rate = null)
    {
        MapCatalog maps = MapCatalog.CreateDefault();
        string? folder = maps.FindFolder(map);
        Assert.SkipWhen(folder is null, $"{map} content is required.");
        var clock = new VirtualClock();
        var network = new SimLinkNetwork(clock, 119, new SimLinkProfile("rough", 200, 60, 3, 0.5f));
        using var serverTransport = network.Bind("server");
        using var clientTransport = network.Bind("client");
        using var server = new ServerHost(serverTransport, new ServerSettings
        {
            Map = map, Maps = maps, SpawnWalkers = false, Profile = profile,
            GameOptions = "?GameMode=0?TimeLimit=0?Players=1", UseStartGate = true, PlayersToStart = 1,
        });
        var loader = new PlayMapLoader(SvMap.Load(folder!), string.Empty, PlayMapLoader.FindCharsDir(folder!));
        var clientMaps = new MapCatalog(maps.Roots.ToArray());
        loader.Register(clientMaps);
        using var client = new ClientSession(clientTransport, "server", 0, new ClientSettings
        {
            Maps = clientMaps, Gameplay = true, InstallGameplayServices = false, LoginOptions = "?Team=" + team,
            Net = rate is { } limit ? new NetDriverSettings
            {
                DefaultNetSpeed = limit,
                Conf = NetSpeedConf.Default with { Client = NetSpeedConf.DefaultClient with { InternetMax = limit } },
            } : new NetDriverSettings(),
        });
        using var play = new PlaySession(new PlaySettings { Map = map, Side = team == 0 ? PlaySide.Spy : PlaySide.Merc }, clock, client, clientMaps, folder!, loader);
        if (rate is not null) play.BeforeClientTick += (scene, tick) =>
        {
            if (tick < 600) return;
            long phase = (tick - 600) % 600;
            if (phase == 0) scene.Input.Commands.Add(new InputCommand("Jump"));
            scene.Input.Set("bCrouch", phase is 240 or 420);
            scene.Input.Set("BT_Up", phase is >= 300 and < 330);
            scene.Input.Set("BT_Down", phase is >= 330 and < 360);
        };
        for (int tick = 1; tick <= 60 * 150; tick++)
        {
            clock.AdvanceTo(VirtualClock.TickToMicroseconds(tick, Sim.Hz));
            server.Tick(clock.NowMicroseconds);
            if (tick % ticksPerFrame == 0) play.Update();
            if (tick > 600 && tick % 600 == 0)
            {
                var owner = client.Movement.PeekEndpoint(play.LocalController!)!.Sequenced!;
                var source = server.Movement.PeekEndpoint(server.Players.Single().Controller!)!.Sequenced!;
                Assert.True(owner.ResynchronizationRequests == 0 && owner.NextSequence - owner.LastAcknowledged < 60,
                    $"{ticksPerFrame} ticks/frame at tick {tick}: owner [{owner.DescribePending()}]; source [{source.DescribePending()}]; clock [{source.DescribeInputClock()}]");
                if (rate is not null)
                {
                    var pawn = Assert.IsAssignableFrom<SBasePawn>(play.LocalPawn);
                    var authoritativePawn = Assert.IsAssignableFrom<SBasePawn>(server.Players.Single().Controller!.Pawn);
                    Assert.True(FVector.Dist(pawn.Location, authoritativePawn.Location) < 3f, $"Settled owner {pawn.Location} versus authority {authoritativePawn.Location}");
                    Assert.Equal(authoritativePawn.Physics, pawn.Physics);
                    Assert.Equal(authoritativePawn.bIsCrouched, pawn.bIsCrouched);
                    Assert.Equal(authoritativePawn.CollisionHeight, pawn.CollisionHeight);
                }
            }
        }
    }

    [Theory]
    [InlineData("TestBox", 0)]
    [InlineData("AquaE", 0)]
    [InlineData("AquaE", 24)]
    public void SpyLandsAfterJumpWithIndependentServerAndPlayClientLoaders(string map, int latency)
    {
        MapCatalog serverMaps = MapCatalog.CreateDefault();
        string? folder = serverMaps.FindFolder(map);
        Assert.SkipWhen(folder is null, $"{map} content is required.");
        var clock = new VirtualClock();
        var network = new SimLinkNetwork(clock, 119, SimLinkProfile.A with { OneWayLatencyMs = latency / 2 });
        using var serverTransport = network.Bind("server");
        using var clientTransport = network.Bind("client");
        using var server = new ServerHost(serverTransport, new ServerSettings
        {
            Map = map, Maps = serverMaps, SpawnWalkers = false, NetMode = ENetMode.NM_ListenServer,
            GameOptions = "?GameMode=0?TimeLimit=0?Players=1", UseStartGate = true, PlayersToStart = 1,
        });
        var loader = new PlayMapLoader(SvMap.Load(folder!), string.Empty, PlayMapLoader.FindCharsDir(folder!));
        var clientMaps = new MapCatalog(serverMaps.Roots.ToArray());
        loader.Register(clientMaps);
        using var client = new ClientSession(clientTransport, "server", 0, new ClientSettings
        {
            Maps = clientMaps, Gameplay = true, InstallGameplayServices = false, LoginOptions = "?Team=0",
        });
        using var play = new PlaySession(new PlaySettings { Map = map, Side = PlaySide.Spy }, clock, client, clientMaps, folder!, loader);
        long ticks = 0;
        void Tick(int count)
        {
            for (int i = 0; i < count; i++)
            {
                clock.AdvanceTo(VirtualClock.TickToMicroseconds(++ticks, Sim.Hz));
                server.Tick(clock.NowMicroseconds);
                play.Step(clock.NowMicroseconds);
            }
        }
        Tick(600);
        var owner = Assert.IsAssignableFrom<SPawnAttaque>(play.LocalPawn);
        var authority = Assert.IsAssignableFrom<SPawnAttaque>(server.Players.Single().Controller!.Pawn);
        string State() => $"owner {owner.Location}/{owner.Physics}/{play.LocalController!.GetStateName()}/{owner.PawnAnim[0].Sequence}; " +
            $"server {authority.Location}/{authority.Physics}/{authority.Controller!.GetStateName()}/{authority.PawnAnim[0].Sequence}";
        Assert.Equal(EPhysics.PHYS_Walking, owner.Physics);
        play.Input.Commands.Add(new InputCommand("Jump"));
        Tick(15);
        Assert.True(owner.Physics == EPhysics.PHYS_Falling, State());
        Tick(360);
        Assert.True(owner.Physics == EPhysics.PHYS_Walking && authority.Physics == EPhysics.PHYS_Walking, State());
        Assert.True(FVector.Dist(owner.Location, authority.Location) < 3f, State());
        Assert.False(play.LocalController!.StateNode is SPlayerController.s_PlayerJump, State());
    }
}
