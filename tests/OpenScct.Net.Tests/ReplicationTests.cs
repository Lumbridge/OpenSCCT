using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using Xunit;

namespace OpenScct.Net.Tests;

/// <summary>
/// The replication model end to end over SimLink: map actor binding, the role swap, native and script conditions,
/// ownership, relevancy and its timeout, temporaries, the broadcast and owner calls, the frequency gate.
/// </summary>
public sealed class ReplicationTests
{
    /// <summary>A small map: LevelInfo (index 0), a music trigger (bNoDelete) and a static actor.</summary>
    private static List<Actor?> MusicMap(Level level)
    {
        var music = level.SpawnActor(new TestMusic(), new FVector(10f, 0f, 0f), default)!;
        var probe = new TestMover { bStatic = true, RemoteRole = ENetRole.ROLE_None };
        level.SpawnActor(probe, new FVector(20f, 0f, 0f), default);
        return [level.Info, music, probe];
    }

    [Fact]
    public void MapActorsBindToTheClientsOwnCopyBySvMapIndex()
    {
        using var rig = new NetRig(SimLinkProfile.B, 3, 2, MusicMap);
        rig.Step(240);
        foreach (NetRig.Client client in rig.Clients)
        {
            var music = (TestMusic)client.MapActors[1]!;
            ActorChannel channel = Assert.Single(client.Notify.Opened, c => c.Actor is TestMusic);
            Assert.True(channel.IsMapActor);
            Assert.Same(music, channel.Actor);

            // Client map setup exchanged the roles of map actors before begin play.
            Assert.Equal(ENetRole.ROLE_SimulatedProxy, music.Role);
            Assert.Equal(ENetRole.ROLE_Authority, music.RemoteRole);
            Assert.Equal(ENetRole.ROLE_None, client.MapActors[2]!.Role);

            // No second copy was spawned.
            int musics = 0;
            foreach (TestMusic _ in client.World.Level.AllActors<TestMusic>())
            {
                musics++;
            }

            Assert.Equal(1, musics);
        }
    }

    [Fact]
    public void BroadcastCallsReachEveryClientWithAnOpenChannelOnce()
    {
        using var rig = new NetRig(SimLinkProfile.C, 7, 6, MusicMap);
        rig.Step(300);
        var music = (TestMusic)rig.ServerMap[1]!;
        var args = new NetCallArgs().Bool(0, true);
        Assert.Equal(ENetCallResult.Sent, rig.Server.ProcessRemoteFunction(music, "ClientPlayStopMusic", args));

        // A client joining after the call never receives it (spec/native/net-replication.md rule 20).
        NetRig.Client late = rig.AddClient(MusicMap);
        rig.Step(600);
        for (int i = 0; i < 6; i++)
        {
            var copy = (TestMusic)rig.Clients[i].MapActors[1]!;
            Assert.Equal([(true, (Pawn?)null)], copy.Calls);
        }

        Assert.Empty(((TestMusic)late.MapActors[1]!).Calls);

        // Six connections share the actor's channel cache: the original's four-slot limit does not apply.
        Assert.Equal(7, rig.Server.FindState(music)!.Channels.Count);

        // On a client the condition (Role == ROLE_Authority) is false, so the simulated function just runs there.
        var clientCopy = (TestMusic)rig.Clients[0].MapActors[1]!;
        Assert.Equal(ENetCallResult.RunLocally, rig.Clients[0].Driver.ProcessRemoteFunction(clientCopy, "ClientPlayStopMusic", args));
    }

    [Fact]
    public void ListenServerBroadcastAlsoRunsLocally()
    {
        using var rig = new NetRig(SimLinkProfile.A, 1, 1, MusicMap, mode: ENetMode.NM_ListenServer);
        rig.Step(120);
        var music = (TestMusic)rig.ServerMap[1]!;
        Assert.Equal(ENetCallResult.SentAndRunLocally, rig.Server.ProcessRemoteFunction(music, "ClientPlayStopMusic", new NetCallArgs()));
    }

    [Fact]
    public void ClientCopiesSpawnWithExchangedRolesAndInitialPropertiesBeforePostNetBeginPlay()
    {
        using var rig = new NetRig(SimLinkProfile.B, 9, 1);
        rig.Step(60);
        TestSpawnProbe probe = rig.ServerWorld.Level.Spawn<TestSpawnProbe>(new FVector(1f, 2f, 3f))!;
        probe.bHidden = true;
        probe.bAlwaysRelevant = true;
        rig.Step(240);
        TestSpawnProbe copy = rig.Clients[0].Copy<TestSpawnProbe>(probe)!;
        Assert.NotNull(copy);
        Assert.Equal(ENetRole.ROLE_SimulatedProxy, copy.RoleAtPreBeginPlay);
        Assert.Equal(ENetRole.ROLE_Authority, copy.RemoteRoleAtPreBeginPlay);
        Assert.Equal(1, copy.PostNetBeginPlays);
        Assert.True(copy.HiddenAtPostNetBeginPlay);
        Assert.Equal(new FVector(1f, 2f, 3f), copy.Location);
    }

    [Fact]
    public void NativeListsSendHiddenOnlyInTheFirstBunchScriptAlways()
    {
        foreach (RepConditionSource source in new[] { RepConditionSource.Native, RepConditionSource.Script })
        {
            var registry = new NetClassRegistry(new UcClassIndex(CorePackage.Info, EnginePackage.Info), source);
            registry.Register<TestMover>(TestMover.Extension());
            registry.Register<TestViewer>();
            using var rig = new NetRig(SimLinkProfile.A, 2, 1, registry: registry);
            TestMover mover = rig.ServerWorld.Level.Spawn<TestMover>()!;
            rig.Step(120);
            TestMover copy = rig.Clients[0].Copy<TestMover>(mover)!;
            Assert.False(copy.bHidden);
            mover.bHidden = true;
            rig.Step(120);

            // spec/native/net-replists.md rule 7: bHidden is first-bunch only in the Actor list.
            Assert.Equal(source == RepConditionSource.Script, copy.bHidden);
        }
    }

    [Fact]
    public void OwnerGoesOnlyToTheOwningConnection()
    {
        using var rig = new NetRig(SimLinkProfile.B, 4, 2, viewer: null);
        rig.Step(120);
        TestMover mover = rig.ServerWorld.Level.Spawn<TestMover>()!;
        mover.SetOwner(rig.ServerSide(0).Actor);
        rig.Step(240);
        TestMover first = rig.Clients[0].Copy<TestMover>(mover)!;
        TestMover second = rig.Clients[1].Copy<TestMover>(mover)!;

        // The viewers do not replicate, so the owner reference cannot be mapped: it stays None on both, but only
        // the owning connection's channel keeps retrying it.
        Assert.Null(first.Owner);
        Assert.Null(second.Owner);
        ActorChannel ownerChannel = rig.ServerSide(0).FindActorChannel(mover)!;
        ActorChannel otherChannel = rig.ServerSide(1).FindActorChannel(mover)!;
        Assert.True(ownerChannel.HasDirtyProperties);
        Assert.False(otherChannel.HasDirtyProperties);
    }

    [Fact]
    public void OwnedPlayerControllerReplicatesToItsOwnerAsAutonomous()
    {
        NetRig? rig = null;
        rig = new NetRig(SimLinkProfile.B, 5, 2, viewer: c => rig!.ServerWorld.Level.Spawn<PlayerController>()!);
        using (rig)
        {
            rig.Step(240);
            for (int i = 0; i < 2; i++)
            {
                NetRig.Client client = rig.Clients[i];
                var pc = (PlayerController)rig.ServerSide(i).Actor!;
                PlayerController? copy = client.Copy<PlayerController>(pc);
                Assert.NotNull(copy);
                Assert.Equal(ENetRole.ROLE_AutonomousProxy, copy!.Role);
                Assert.Same(copy, client.World.Level.Info.LocalPlayerController);
                Assert.Same(copy, client.ToServer.Actor);

                // The other player's controller is not relevant here (APlayerController: own connection only).
                var other = (PlayerController)rig.ServerSide(1 - i).Actor!;
                Assert.Null(client.Copy<PlayerController>(other));
            }

            // An owned actor's Owner maps to the client's own controller.
            var pc0 = (PlayerController)rig.ServerSide(0).Actor!;
            TestMover mover = rig.ServerWorld.Level.Spawn<TestMover>()!;
            mover.SetOwner(pc0);
            rig.Step(240);
            Assert.Same(rig.Clients[0].Copy<PlayerController>(pc0), rig.Clients[0].Copy<TestMover>(mover)!.Owner);
            Assert.Null(rig.Clients[1].Copy<TestMover>(mover)!.Owner);
        }
    }

    [Fact]
    public void OwnerCallsGoToTheOwnerAndClientCallsOnlyFromTheOwnerAreAccepted()
    {
        NetRig? rig = null;
        rig = new NetRig(SimLinkProfile.C, 6, 2, viewer: c => rig!.ServerWorld.Level.Spawn<PlayerController>()!);
        using (rig)
        {
            var clientMessages = new List<(int Client, string Text)>();
            for (int i = 0; i < 2; i++)
            {
                int n = i;
                rig.Clients[i].Driver.Functions.Register("Engine.PlayerController", "ClientMessage",
                    (self, f, args, from) => clientMessages.Add((n, args.Values[0].AsString)));
            }

            var pings = new List<(Actor Pc, int Ping)>();
            rig.Server.Functions.Register("Engine.PlayerController", "ServerUpdatePing",
                (self, f, args, from) => pings.Add((self, args.Values[0].I)));
            rig.Step(240);

            var pc0 = (PlayerController)rig.ServerSide(0).Actor!;
            var pc1 = (PlayerController)rig.ServerSide(1).Actor!;
            NetCallArgs args = new NetCallArgs().String(0, "hello zero");
            Assert.Equal(ENetCallResult.Sent, rig.Server.ProcessRemoteFunction(pc0, "ClientMessage", args));
            rig.Step(120);
            Assert.Equal([(0, "hello zero")], clientMessages);

            // Client 1 asks for its own ping update: accepted, with the sender's controller.
            PlayerController copy1 = rig.Clients[1].Copy<PlayerController>(pc1)!;
            Assert.Equal(ENetCallResult.Sent,
                rig.Clients[1].Driver.ProcessRemoteFunction(copy1, "ServerUpdatePing", new NetCallArgs().Int(0, 77)));
            rig.Step(120);
            Assert.Equal([((Actor)pc1, 77)], pings);

            // A client that forges a call on an actor it does not own is refused (spec/native/net-replication.md rule 21).
            TestMover mover = rig.ServerWorld.Level.Spawn<TestMover>()!;
            rig.Step(240);
            NetRig.Client c0 = rig.Clients[0];
            ActorChannel forged = c0.ToServer.FindActorChannel(c0.Copy<TestMover>(mover)!)!;
            RepFunction changeServerState = forged.Layout!.FindFunction("ChangeServerState")!;
            long rejectedBefore = rig.Server.Stats.CallsRejected;
            forged.SendCall(changeServerState, new NetCallArgs().Name(0, new Name("Attacking")));
            rig.Step(120);
            Assert.Equal(rejectedBefore + 1, rig.Server.Stats.CallsRejected);

            // A client calls a replicated function whose condition holds: it goes to the server whatever the role.
            Assert.Equal(ENetCallResult.Sent,
                c0.Driver.ProcessRemoteFunction(c0.Copy<TestMover>(mover)!, "ChangeServerState", new NetCallArgs()));

            // A function that is neither replicated nor simulated is swallowed on a proxy (rule 19.2).
            UcFunctionInfo? plain = null;
            foreach (UcFunctionInfo f in Actor.UcMeta.Class.Functions)
            {
                if (!f.IsNet && !f.IsSimulated && !f.IsNative && !f.IsStatic)
                {
                    plain = f;
                    break;
                }
            }

            Assert.NotNull(plain);
            Assert.Equal(ENetCallResult.Absorbed, c0.Driver.ProcessRemoteFunction(c0.Copy<TestMover>(mover)!, plain!.Name));
            Assert.Equal(ENetCallResult.RunLocally, rig.Server.ProcessRemoteFunction(mover, plain.Name));
        }
    }

    private sealed class Wall : INetLineOfSight
    {
        public bool Blocked { get; set; } = true;

        public bool IsBlockedByWorld(FVector start, FVector end) => Blocked;
    }

    [Fact]
    public void RelevancyOpensChannelsAndClosesThemAfterTheTimeout()
    {
        using var rig = new NetRig(SimLinkProfile.A, 8, 1);
        var wall = new Wall();
        rig.Server.Relevancy.LineOfSight = wall;
        TestMover mover = rig.ServerWorld.Level.Spawn<TestMover>(new FVector(6000f, 0f, 0f))!;
        rig.Step(120);
        Assert.Null(rig.Clients[0].Copy<TestMover>(mover));

        // Within 2500 uu of the camera it is always relevant.
        mover.Location = new FVector(2000f, 0f, 0f);
        rig.Step(30);
        TestMover copy = rig.Clients[0].Copy<TestMover>(mover)!;
        Assert.NotNull(copy);

        // Far and hidden behind the wall: the channel stays for RelevantTimeout (5 s), then closes.
        mover.Location = new FVector(6000f, 0f, 0f);
        rig.Step(60 * 4);
        Assert.False(copy.bDeleteMe);
        rig.Step(60 * 3);
        Assert.True(copy.bDeleteMe);
        Assert.Null(rig.ServerSide(0).FindActorChannel(mover));

        // A clear line of sight makes it relevant again at any distance.
        wall.Blocked = false;
        rig.Step(60);
        Assert.NotNull(rig.Clients[0].Copy<TestMover>(mover));
    }

    [Fact]
    public void TemporaryActorsAreSentOnceAndKeptByTheClient()
    {
        using var rig = new NetRig(SimLinkProfile.A, 10, 1);
        rig.Step(60);
        TestTemporary grenade = rig.ServerWorld.Level.Spawn<TestTemporary>(new FVector(5f, 6f, 7f))!;
        rig.Step(60);
        TestTemporary copy = rig.Clients[0].Copy<TestTemporary>(grenade) ?? FindOnly<TestTemporary>(rig.Clients[0].World)!;
        Assert.NotNull(copy);
        Assert.False(copy.bDeleteMe);
        Assert.Equal(new FVector(5f, 6f, 7f), copy.Location);
        Assert.Null(rig.ServerSide(0).FindActorChannel(grenade));
        Assert.Contains(grenade.ActorId, rig.ServerSide(0).SentTemporaries);

        // Later changes never reach the client.
        grenade.Location = new FVector(100f, 0f, 0f);
        rig.Step(120);
        Assert.Equal(new FVector(5f, 6f, 7f), copy.Location);
    }

    private static T? FindOnly<T>(World world)
        where T : Actor
    {
        foreach (T a in world.Level.AllActors<T>())
        {
            return a;
        }

        return null;
    }

    [Fact]
    public void FrequencyGateAsDecodedStopsSlowActorsOnATickCappedServer()
    {
        foreach (NetUpdateGate gate in new[] { NetUpdateGate.Decoded, NetUpdateGate.Stochastic })
        {
            using var rig = new NetRig(SimLinkProfile.A, 11, 0, server: new NetDriverSettings { UpdateGate = gate });
            TestMover mover = rig.ServerWorld.Level.Spawn<TestMover>()!;
            mover.NetUpdateFrequency = 5f;

            // A player arriving a while after the server started gets a first pass with a long dt: everything
            // passes once.
            rig.Step(60);
            rig.AddClient();
            rig.Step(120);
            TestMover copy = rig.Clients[0].Copy<TestMover>(mover)!;
            Assert.NotNull(copy);
            Assert.Equal(20, rig.Server.GetMaxTickRate());
            mover.Rotation = new FRotator(0, 12345, 0);
            rig.Step(240);

            // 5 x 0.05 s never exceeds 1: as decoded, a 5 Hz actor gets no update on a 20 Hz dedicated server unless
            // the passes stall on the send budget (spec/native/net-replication.md, edge cases).
            Assert.Equal(gate == NetUpdateGate.Stochastic, copy.Rotation.Yaw == 12345);
        }
    }

    [Fact]
    public void ListenServersHaveNoTickCapAndPassEveryActor()
    {
        using var rig = new NetRig(SimLinkProfile.A, 12, 1, mode: ENetMode.NM_ListenServer);
        TestMover mover = rig.ServerWorld.Level.Spawn<TestMover>()!;
        mover.NetUpdateFrequency = 5f;
        rig.Step(120);
        mover.Rotation = new FRotator(0, 2222, 0);
        rig.Step(30);
        Assert.Equal(0, rig.Server.GetMaxTickRate());
        Assert.Equal(2222, rig.Clients[0].Copy<TestMover>(mover)!.Rotation.Yaw);
    }

    [Fact]
    public void TickCapFollowsTheLanSwitchAndClamps()
    {
        var network = new SimLinkNetwork(new VirtualClock(), 1, SimLinkProfile.A);
        using SimLinkTransport t = network.Bind("s");
        NetClassRegistry registry = NetRig.CreateRegistry();
        var notify = new NetRig.Notify(null!, server: true);
        using var lan = new NetDriver(t, registry, notify, true, new NetDriverSettings { LanPlay = true }) { World = new World(1, ENetMode.NM_DedicatedServer) };
        Assert.Equal(K.LanServerMaxTickRate, lan.GetMaxTickRate());
        using var fast = new NetDriver(t, registry, notify, true, new NetDriverSettings { NetServerMaxTickRate = 500 }) { World = new World(1, ENetMode.NM_DedicatedServer) };
        Assert.Equal(K.MaxServerTickRate, fast.GetMaxTickRate());
        using var slow = new NetDriver(t, registry, notify, true, new NetDriverSettings { NetServerMaxTickRate = 1 }) { World = new World(1, ENetMode.NM_DedicatedServer) };
        Assert.Equal(K.MinServerTickRate, slow.GetMaxTickRate());
    }
}
