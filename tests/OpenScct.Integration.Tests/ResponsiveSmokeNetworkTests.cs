using System;
using System.Linq;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Equipment;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Server;
using Xunit;

namespace OpenScct.Integration.Tests;

public sealed partial class ResponsiveSmokeNetworkTests
{
    [Theory]
    [InlineData("A")]
    [InlineData("100")]
    [InlineData("D")]
    public void TwoClientsReceiveTheSameCloudAndShortLivedHoleAndLateJoinKeepsItsAge(string profile)
    {
        using var rig = Create(profile);
        Level level = rig.Server.World.Level;
        var rules = level.Spawn<GameReplicationInfo>()!;
        rules.ResponsiveSmokeEnabled = true;
        Step(rig, 180);
        SSmokeEmitter cloud = SpawnCloud(level);
        Step(rig, 90);
        ResponsiveSmokeWorld authority = ResponsiveSmokeWorld.For(level);
        Assert.True(authority.TryGet(cloud, out ResponsiveSmokeField source));
        Assert.True(source.SampleDensity(cloud.Location, authority.CurrentTime) > .9f);
        foreach (ClientSession client in rig.Clients) AssertCloud(client, cloud, source, authority.CurrentTime);

        ResponsiveSmokeWorld.Bullet(cloud, cloud.Location - new FVector(400, 0, 0), cloud.Location + new FVector(400, 0, 0));
        uint shot = Assert.Single(source.Carves).Sequence;
        var observed = new bool[rig.Clients.Length];
        for (int i = 0; i < 27; i++)
        {
            rig.Step();
            for (int p = 0; p < rig.Clients.Length; p++)
            {
                ClientSession client = rig.Clients[p];
                var remote = Assert.IsType<SSmokeEmitter>(client.Driver.FindActorByNetGuid((uint)cloud.ActorId));
                ResponsiveSmokeWorld replica = ResponsiveSmokeWorld.For(client.World.Level);
                Assert.True(replica.TryGet(remote, out ResponsiveSmokeField field));
                if (field.Carves.Any(c => c.Sequence == shot))
                {
                    observed[p] = true;
                    Assert.InRange(MathF.Abs(field.SampleDensity(cloud.Location, replica.CurrentTime) -
                        source.SampleDensity(cloud.Location, replica.CurrentTime)), 0f, .001f);
                }
            }
        }
        Assert.All(observed, Assert.True);
        Step(rig, 45);
        Assert.True(source.SampleDensity(cloud.Location, authority.CurrentTime) > .9f);

        ClientSession late = rig.AddClient();
        Step(rig, 120);
        AssertCloud(late, cloud, source, authority.CurrentTime);
        var lateCloud = Assert.IsType<SSmokeEmitter>(late.Driver.FindActorByNetGuid((uint)cloud.ActorId));
        Assert.True(ResponsiveSmokeWorld.For(late.World.Level).TryGet(lateCloud, out ResponsiveSmokeField lateField));
        Assert.True(ResponsiveSmokeWorld.For(late.World.Level).CurrentTime - lateField.CreatedAt > 3f);
        Assert.All(rig.Clients.Append(late), client =>
        {
            Assert.Equal(EClientState.Joined, client.State);
            ResponsiveSmokeReplication stream = ResponsiveSmokeReplication.For(client.Driver);
            Assert.True(stream.BaselinesReceived > 0);
            Assert.Equal(0, stream.InvalidPayloads);
        });
        cloud.Destroy();
        Step(rig, 90);
        Assert.All(rig.Clients.Append(late), client =>
            Assert.True(client.Driver.FindActorByNetGuid((uint)cloud.ActorId) is null or { bDeleteMe: true }));
    }

    [Fact]
    public void ChunkedGeometryBaselineAndSparseChangesConvergeAcrossLoss()
    {
        using var rig = Create("100");
        Level level = rig.Server.World.Level;
        level.Spawn<GameReplicationInfo>()!.ResponsiveSmokeEnabled = true;
        Step(rig, 120);
        SSmokeEmitter cloud = SpawnCloud(level);
        ResponsiveSmokeWorld world = ResponsiveSmokeWorld.For(level);
        ResponsiveSmokeSnapshot first = world.Capture(cloud);
        var activation = new ushort[ResponsiveSmokeField.CellCount];
        uint random = 173;
        for (int i = 0; i < activation.Length; i++)
        {
            random = random * 1664525u + 1013904223u;
            activation[i] = (ushort)(random >> 24);
        }
        first = first with { Revision = first.Revision + 1, ActivationTicks = activation };
        Assert.True(ResponsiveSmokeCodec.Encode(first).Length > 3000);
        world.ApplySnapshot(cloud, first);
        Step(rig, 180);
        foreach (ClientSession client in rig.Clients)
        {
            var remote = Assert.IsType<SSmokeEmitter>(client.Driver.FindActorByNetGuid((uint)cloud.ActorId));
            Assert.Equal(activation, ResponsiveSmokeWorld.For(client.World.Level).Capture(remote).ActivationTicks);
        }
        long before = ResponsiveSmokeReplication.For(rig.Server.Driver).StateBytesSent;
        ResponsiveSmokeSnapshot next = world.Capture(cloud);
        var cells = (byte[])next.Cells.Clone();
        for (int i = 100; i < 140; i++) cells[i] = 0;
        next = next with { Revision = next.Revision + 1, Cells = cells };
        world.ApplySnapshot(cloud, next);
        rig.SimLink!.PauseLink("server", "client0", rig.Clock.NowMicroseconds + 150_000);
        Step(rig, 120);
        foreach (ClientSession client in rig.Clients)
        {
            var remote = Assert.IsType<SSmokeEmitter>(client.Driver.FindActorByNetGuid((uint)cloud.ActorId));
            ResponsiveSmokeSnapshot current = ResponsiveSmokeWorld.For(client.World.Level).Capture(remote);
            Assert.Equal(cells, current.Cells);
            Assert.Equal(activation, current.ActivationTicks);
            Assert.True(ResponsiveSmokeReplication.For(client.Driver).DeltasReceived > 0);
        }
        Assert.InRange(ResponsiveSmokeReplication.For(rig.Server.Driver).StateBytesSent - before, 1, 1500);
    }

    [Fact]
    public void SustainedFireAcrossSixCloudsDoesNotQueueReliableDensityTraffic()
    {
        using var rig = Create("100");
        Level level = rig.Server.World.Level;
        level.Spawn<GameReplicationInfo>()!.ResponsiveSmokeEnabled = true;
        Step(rig, 120);
        SSmokeEmitter[] clouds = Enumerable.Range(0, 6).Select(_ => SpawnCloud(level)).ToArray();
        Step(rig, 120);
        ResponsiveSmokeReplication sender = ResponsiveSmokeReplication.For(rig.Server.Driver);
        long reliableBefore = sender.StateBytesSent;
        int[] seen = new int[rig.Clients.Length];
        for (int frame = 0; frame < 180; frame++)
        {
            if (frame % 6 == 0)
                for (int shooter = 0; shooter < 3; shooter++)
                {
                    float angle = shooter * 1.1f + frame * .007f;
                    FVector ray = new FVector(MathF.Cos(angle), MathF.Sin(angle), .01f) * 400;
                    ResponsiveSmokeWorld.Bullet(clouds[0], clouds[0].Location - ray, clouds[0].Location + ray);
                }
            rig.Step();
            for (int p = 0; p < rig.Clients.Length; p++)
            {
                ClientSession client = rig.Clients[p];
                ResponsiveSmokeWorld world = ResponsiveSmokeWorld.For(client.World.Level);
                if (clouds.All(cloud => client.Driver.FindActorByNetGuid((uint)cloud.ActorId) is SSmokeEmitter remote &&
                    world.TryGet(remote, out ResponsiveSmokeField field) && field.Carves.Any(c => world.CurrentTime - c.CreatedAt < .4f)))
                    seen[p]++;
                Assert.Equal(EClientState.Joined, client.State);
            }
        }
        Assert.Equal(reliableBefore, sender.StateBytesSent);
        Assert.All(seen, ticks => Assert.True(ticks >= 120, $"Only {ticks}/180 ticks showed recent holes in all six clouds."));
        Assert.All(rig.Clients, client => Assert.Equal(0, ResponsiveSmokeReplication.For(client.Driver).InvalidPayloads));
        Step(rig, 90);
        Assert.All(rig.Clients, client =>
        {
            ResponsiveSmokeWorld world = ResponsiveSmokeWorld.For(client.World.Level);
            Assert.All(clouds, cloud =>
            {
                var remote = Assert.IsType<SSmokeEmitter>(client.Driver.FindActorByNetGuid((uint)cloud.ActorId));
                Assert.True(world.TryGet(remote, out ResponsiveSmokeField field));
                Assert.True(field.SampleDensity(cloud.Location, world.CurrentTime) > .9f);
            });
        });
    }

    [Fact]
    public void RepeatedResyncRequestsPreserveProgressOfASlowFullBaseline()
    {
        using var rig = Create("A", new NetDriverSettings { Budget = SendBudgetMode.PerConnection, UpdateGate = NetUpdateGate.DecodedGuarded });
        Level level = rig.Server.World.Level;
        level.Spawn<GameReplicationInfo>()!.ResponsiveSmokeEnabled = true;
        Step(rig, 120);
        SSmokeEmitter cloud = SpawnCloud(level);
        Step(rig, 90);
        ResponsiveSmokeWorld world = ResponsiveSmokeWorld.For(level);
        ResponsiveSmokeSnapshot snapshot = world.Capture(cloud);
        var activation = new ushort[ResponsiveSmokeField.CellCount];
        uint random = 2941;
        for (int i = 0; i < activation.Length; i++)
        {
            random = random * 1664525u + 1013904223u;
            activation[i] = (ushort)(random >> 24);
        }
        snapshot = snapshot with { Revision = snapshot.Revision + 1, ActivationTicks = activation };
        Assert.True(ResponsiveSmokeCodec.Encode(snapshot).Length > 12000);
        world.ApplySnapshot(cloud, snapshot);
        NetConnection connection = rig.Server.Driver.ClientConnections.First();
        ActorChannel channel = connection.FindActorChannel(cloud)!;
        int completed = -1;
        for (int tick = 0; tick < 360; tick++)
        {
            connection.CurrentNetSpeed = 6000;
            // The receiver retries at this cadence while a missing-baseline resync is outstanding.
            if (tick % 30 == 0)
            {
                var writer = new FBitWriter(64);
                writer.WriteIntPacked(channel.NetGuid);
                var reader = new FBitReader(writer.Buffer, writer.NumBits);
                ResponsiveSmokeReplication.RequestExtension().Receive(connection.Actor!, reader, connection);
                Assert.False(reader.IsError);
                Assert.True(reader.AtEnd);
            }
            rig.Step();
            ClientSession client = rig.Clients[0];
            var remote = Assert.IsType<SSmokeEmitter>(client.Driver.FindActorByNetGuid((uint)cloud.ActorId));
            if (ResponsiveSmokeWorld.For(client.World.Level).Capture(remote).ActivationTicks.AsSpan().SequenceEqual(activation))
            { completed = tick; break; }
        }
        Assert.InRange(completed, 31, 359);
        Assert.Equal(0, ResponsiveSmokeReplication.For(rig.Clients[0].Driver).InvalidPayloads);
    }

    [Fact]
    public void DisconnectAndDestructionDuringTransferDoNotLeaveStaleClouds()
    {
        using var rig = Create("100");
        Level level = rig.Server.World.Level;
        level.Spawn<GameReplicationInfo>()!.ResponsiveSmokeEnabled = true;
        Step(rig, 120);
        SSmokeEmitter cloud = SpawnCloud(level);
        Step(rig, 90);
        ClientSession viewer = rig.Clients[1];
        var remote = Assert.IsType<SSmokeEmitter>(viewer.Driver.FindActorByNetGuid((uint)cloud.ActorId));
        ResponsiveSmokeWorld replica = ResponsiveSmokeWorld.For(viewer.World.Level);
        ResponsiveSmokeSnapshot before = replica.Capture(remote);
        byte[] pending = ResponsiveSmokeCodec.Encode(before with { Revision = before.Revision + 10 });
        Inject(remote, viewer, 100000, before.Revision + 10, pending, 0, pending.Length / 2, fast: false);

        rig.Clients[0].Disconnect();
        ClientSession reconnected = rig.AddClient("P0");
        ResponsiveSmokeWorld.Blast(cloud, cloud.Location, 300);
        Step(rig, 90);
        ResponsiveSmokeWorld authority = ResponsiveSmokeWorld.For(level);
        Assert.True(authority.TryGet(cloud, out ResponsiveSmokeField source));
        AssertCloud(reconnected, cloud, source, authority.CurrentTime);
        var renewed = Assert.IsType<SSmokeEmitter>(reconnected.Driver.FindActorByNetGuid((uint)cloud.ActorId));
        ResponsiveSmokeWorld renewedWorld = ResponsiveSmokeWorld.For(reconnected.World.Level);
        Assert.True(renewedWorld.TryGet(renewed, out ResponsiveSmokeField renewedField));
        Assert.Contains(renewedField.Carves, carve => carve.Kind == ResponsiveSmokeCarveKind.Blast);
        Assert.InRange(MathF.Abs(source.SampleDensity(cloud.Location, renewedWorld.CurrentTime) -
            renewedField.SampleDensity(cloud.Location, renewedWorld.CurrentTime)), 0, .001f);

        cloud.Destroy();
        Step(rig, 90);
        Assert.Empty(replica.Fields);
        Assert.Empty(renewedWorld.Fields);
        SSmokeEmitter replacement = SpawnCloud(level);
        Step(rig, 90);
        Assert.True(authority.TryGet(replacement, out ResponsiveSmokeField replacementField));
        AssertCloud(viewer, replacement, replacementField, authority.CurrentTime);
        AssertCloud(reconnected, replacement, replacementField, authority.CurrentTime);
        Assert.Equal(0, ResponsiveSmokeReplication.For(viewer.Driver).InvalidPayloads);
        Assert.Equal(0, ResponsiveSmokeReplication.For(reconnected.Driver).InvalidPayloads);
    }

    [Fact]
    public void DisabledMatchAllocatesNeitherSmokeFieldsNorReplicationStream()
    {
        using var rig = Create("A");
        rig.Server.World.Level.Spawn<GameReplicationInfo>();
        Step(rig, 90);
        rig.Server.World.Level.Spawn<SSmokeEmitter>(new FVector(100, 100, 700));
        Step(rig, 90);
        Assert.Null(rig.Server.World.Level.GetService<ResponsiveSmokeWorld>());
        Assert.Null(rig.Server.World.Level.GetService<ResponsiveSmokeReplication>());
        Assert.All(rig.Clients, client =>
        {
            Assert.Null(client.World.Level.GetService<ResponsiveSmokeWorld>());
            Assert.Null(client.World.Level.GetService<ResponsiveSmokeReplication>());
        });
    }

    [Fact]
    public void PartialBaselineDoesNotReplaceVisibleStateAndLateBaselineDoesNotUndoNewerCarves()
    {
        using var rig = Create("A");
        Level level = rig.Server.World.Level;
        level.Spawn<GameReplicationInfo>()!.ResponsiveSmokeEnabled = true;
        Step(rig, 90);
        SSmokeEmitter cloud = SpawnCloud(level);
        Step(rig, 60);
        ClientSession client = rig.Clients[0];
        var remote = Assert.IsType<SSmokeEmitter>(client.Driver.FindActorByNetGuid((uint)cloud.ActorId));
        ResponsiveSmokeWorld world = ResponsiveSmokeWorld.For(client.World.Level);
        Assert.True(world.TryGet(remote, out ResponsiveSmokeField field));
        ResponsiveSmokeSnapshot before = world.Capture(remote);
        var cells = (byte[])before.Cells.Clone();
        cells[field.CellIndex(cloud.Location)] = 0;
        ResponsiveSmokeSnapshot updated = before with { Revision = before.Revision + 10, Cells = cells };
        byte[] bytes = ResponsiveSmokeCodec.Encode(updated);
        Assert.True(bytes.Length > 1);
        int split = bytes.Length / 2;
        Inject(remote, client, 100000, updated.Revision, bytes, 0, split, fast: false);
        Assert.Equal(before.Revision, field.Revision);
        Assert.True(field.SampleDensity(cloud.Location, world.CurrentTime) > .9f);

        var shot = new ResponsiveSmokeCarve(500, ResponsiveSmokeCarveKind.Bullet,
            cloud.Location - new FVector(400, 0, 0), cloud.Location + new FVector(400, 0, 0), 12, 6,
            world.CurrentTime, .1f, .4f, null);
        byte[] carves = ResponsiveSmokeCodec.EncodeCarves([shot]);
        Inject(remote, client, 100001, updated.Revision + 1, carves, 0, carves.Length, fast: true);
        Assert.Equal(500u, Assert.Single(field.Carves).Sequence);
        Inject(remote, client, 100000, updated.Revision, bytes, split, bytes.Length - split, fast: false);
        Assert.Equal(updated.Revision + 1, field.Revision);
        Assert.Equal(0, field.Cells[field.CellIndex(cloud.Location)]);
        Assert.Equal(500u, Assert.Single(field.Carves).Sequence);
    }

    private static void Inject(SSmokeEmitter actor, ClientSession client, uint sequence, uint revision,
        byte[] payload, int offset, int count, bool fast)
    {
        int end = offset + count;
        while (offset < end)
        {
            int chunk = Math.Min(768, end - offset);
            InjectChunk(actor, client, sequence, revision, payload, offset, chunk, fast);
            offset += chunk;
        }
    }

    private static void InjectChunk(SSmokeEmitter actor, ClientSession client, uint sequence, uint revision,
        byte[] payload, int offset, int count, bool fast)
    {
        var writer = new FBitWriter((count + 128) * 8);
        writer.WriteIntPacked(sequence); writer.WriteByte(fast ? (byte)2 : (byte)0);
        writer.WriteIntPacked(revision); writer.WriteIntPacked(0);
        writer.WriteIntPacked((uint)client.Driver.NewestServerTick);
        writer.WriteFloat(ResponsiveSmokeWorld.For(client.World.Level).CurrentTime); writer.WriteFloat(Sim.DeltaTime);
        writer.WriteIntPacked((uint)payload.Length); writer.WriteIntPacked((uint)offset); writer.WriteIntPacked((uint)count);
        writer.WriteBytes(payload.AsSpan(offset, count));
        Assert.False(writer.IsError);
        var reader = new FBitReader(writer.Buffer, writer.NumBits);
        ResponsiveSmokeReplication.CreateExtension().Functions[fast ? 1 : 0].Receive(actor, reader, client.Driver.ServerConnection!);
        Assert.False(reader.IsError);
        Assert.True(reader.AtEnd);
    }

    private static WalkerRig Create(string profile, NetDriverSettings? net = null)
    {
        var settings = new ServerSettings { SpawnWalkers = false };
        if (net is not null) settings = settings with { Net = net };
        var rig = WalkerRig.Create(SimLinkProfile.Get(profile), 10176, 2,
            settings, new ClientSettings { Gameplay = true });
        rig.Server.PlayerJoined += slot =>
        {
            var viewer = rig.Server.World.Level.Spawn<PlayerController>()!;
            viewer.Player = new SimNetConnection(); viewer.RemoteRole = ENetRole.ROLE_AutonomousProxy;
            slot.Connection.Actor = viewer;
        };
        return rig;
    }

    private static SSmokeEmitter SpawnCloud(Level level)
    {
        var cloud = level.Spawn<SSmokeEmitter>(new FVector(100, 100, 700))!;
        cloud.bAlwaysRelevant = true;
        ResponsiveSmokeWorld world = ResponsiveSmokeWorld.For(level);
        world.Register(cloud);
        ResponsiveSmokeSnapshot initial = world.Capture(cloud);
        var cells = new byte[ResponsiveSmokeField.CellCount]; Array.Fill(cells, (byte)1);
        world.ApplySnapshot(cloud, initial with { Revision = initial.Revision + 1, Cells = cells,
            ActivationTicks = new ushort[ResponsiveSmokeField.CellCount] });
        return cloud;
    }

    private static void AssertCloud(ClientSession client, SSmokeEmitter sourceActor, ResponsiveSmokeField source, float serverTime)
    {
        var actor = Assert.IsType<SSmokeEmitter>(client.Driver.FindActorByNetGuid((uint)sourceActor.ActorId));
        ResponsiveSmokeWorld world = ResponsiveSmokeWorld.For(client.World.Level);
        Assert.True(world.TryGet(actor, out ResponsiveSmokeField field));
        Assert.Equal(source.CreatedAt, field.CreatedAt);
        Assert.Equal(source.Origin, field.Origin);
        Assert.Equal(source.Cells, field.Cells);
        Assert.Equal(source.ActivationTicks, field.ActivationTicks);
        Assert.InRange(MathF.Abs(world.CurrentTime - serverTime), 0, .25f);
    }

    private static void Step(WalkerRig rig, int ticks) { for (int i = 0; i < ticks; i++) rig.Step(); }
}
