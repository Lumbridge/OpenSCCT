using System;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Devices;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Server;
using Xunit;

namespace OpenScct.Integration.Tests;

public sealed class PresenceDoorNetworkTests
{
    private static MapCatalog Maps()
    {
        var maps = new MapCatalog();
        maps.Register(MapCatalog.TestBox, (world, client) =>
        {
            Level level = world.Level;
            level.BeginLoad();
            var door = level.SpawnActor(new Mover
            {
                InitialState = "TriggerOpenTimed", MoveTime = 2f, StayOpenTime = 2f,
                DelayTimeLastKey = 0.25f, bAlwaysRelevant = true, Tag = "door",
            }, default, default)!;
            door.KeyPos[1] = new FVector(0, 0, 100);
            var detector = level.SpawnActor(new SPresenceTrigger
            {
                Event = "door", bNoDelete = true,
            }, default, default)!;
            if (client)
            {
                foreach (Actor actor in new Actor[] { door, detector })
                {
                    actor.Role = actor.RemoteRole;
                    actor.RemoteRole = ENetRole.ROLE_Authority;
                }
            }

            DeviceNatives.Install(level);
            level.EndLoad();
            return new MapInfo(MapCatalog.TestBox, "presence-door", MapCatalog.StubContentHash,
                "presence-door", [], [new MapStart(default, default, 0)], [level.Info, door, detector]);
        });
        return maps;
    }

    [Theory]
    [InlineData("A", ENetMode.NM_DedicatedServer)]
    [InlineData("C", ENetMode.NM_DedicatedServer)]
    [InlineData("300", ENetMode.NM_ListenServer)]
    public void OccupancyReversalAndExitReplicateToTwoClientsAndALateJoiner(string profile, ENetMode mode)
    {
        MapCatalog maps = Maps();
        using var rig = WalkerRig.Create(SimLinkProfile.Get(profile), 329, 2,
            new ServerSettings { SpawnWalkers = false, Maps = maps, NetMode = mode },
            new ClientSettings { Gameplay = true, Maps = maps });
        Level level = rig.Server.World.Level;
        rig.Server.PlayerJoined += slot =>
        {
            var controller = level.Spawn<PlayerController>()!;
            controller.bIsPlayer = true;
            controller.Player = new SimNetConnection();
            controller.RemoteRole = ENetRole.ROLE_AutonomousProxy;
            slot.Connection.Actor = controller;
        };
        Steps(300);
        var door = (Mover)rig.Server.Map.MapActors[1]!;
        var detector = (SPresenceTrigger)rig.Server.Map.MapActors[2]!;
        var merc = level.Spawn<SPAWNDEFENSE>()!;
        merc.Physics = EPhysics.PHYS_None;
        detector.BeginTouch(merc);
        Steps(480);
        AssertOpen();

        ClientSession late = rig.AddClient();
        Steps(300);
        AssertOpen();
        Assert.InRange(MathF.Abs(Copy(late).Location.Z - 100f), 0, 0.1f);

        detector.EndTouch(merc);
        Steps(160);
        Assert.True(door.bInterpolating && !door.bOpening);
        float before = door.Location.Z;
        detector.BeginTouch(merc);
        Steps(1);
        Assert.True(door.bOpening && door.Location.Z > before);
        Steps(300);
        AssertOpen();
        Assert.InRange(MathF.Abs(Copy(late).Location.Z - 100f), 0, 0.1f);
        detector.EndTouch(merc);
        Steps(360);
        Assert.Equal(0f, door.Location.Z);
        foreach (ClientSession client in rig.Clients) AssertClosed(client);
        AssertClosed(late);

        void Steps(int count)
        {
            for (int i = 0; i < count; i++) rig.Step();
        }

        static Mover Copy(ClientSession client) => (Mover)client.Map!.MapActors[1]!;

        void AssertOpen()
        {
            Assert.Equal(100f, door.Location.Z);
            Assert.False(door.bInterpolating);
            foreach (ClientSession client in rig.Clients)
            {
                Mover copy = Copy(client);
                Assert.InRange(MathF.Abs(copy.Location.Z - 100f), 0, 0.1f);
                Assert.Equal(LatentFrame.NoCode, copy.StateFrame.Pc);
                Assert.False(((SPresenceTrigger)client.Map!.MapActors[2]!).IsPresenceActive);
            }
        }

        static void AssertClosed(ClientSession client)
        {
            Mover copy = Copy(client);
            Assert.InRange(MathF.Abs(copy.Location.Z), 0, 0.1f);
            Assert.False(copy.bInterpolating);
            Assert.Equal(LatentFrame.NoCode, copy.StateFrame.Pc);
        }
    }
}
