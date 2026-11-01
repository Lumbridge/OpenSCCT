using System;
using System.Collections.Generic;
using System.Linq;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Server;
using Xunit;

namespace OpenScct.Integration.Tests;

public sealed class DestructibleEffectNetworkTests
{
    [Fact]
    public void SteeE_destroyed_prop_effects_reach_every_client_once_and_in_order_after_packet_loss()
    {
        Assert.SkipWhen(WalkerRig.Maps.FindFolder("SteeE") is null, "SteeE content has not been converted.");
        using WalkerRig rig = WalkerRig.Create(SimLinkProfile.A, 8821, 3,
            new ServerSettings { Map = "SteeE", GameOptions = "?GameMode=5?TimeLimit=0", UseStartGate = false },
            new ClientSettings { Gameplay = true });
        for (int i = 0; i < 600 && rig.Clients.Any(c => c.State != EClientState.Joined); i++) rig.Step();
        Assert.All(rig.Clients, client => Assert.Equal(EClientState.Joined, client.State));
        Assert.NotNull(rig.Server.DeviceEvents);
        for (int i = 0; i < 180; i++) rig.Step();
        Assert.All(rig.Server.Driver.ClientConnections, connection =>
        {
            Assert.True(connection.bJoined);
            Assert.NotNull(connection.FindActorChannel(rig.Server.World.Level.Info));
        });
        var effects = new List<string>[rig.Clients.Length];
        for (int i = 0; i < rig.Clients.Length; i++)
        {
            effects[i] = [];
            rig.Clients[i].World.EventSink = new EffectSink(rig.Clients[i].World.Level, effects[i]);
        }

        SGamePlayObject gas = rig.Server.Map.MapActors.OfType<SGamePlayObject>().Single(p =>
            p.DamagedMeshes.Any(stage => stage.Event == "boomgaz3"));
        SGamePlayObject bottle = rig.Server.Map.MapActors.OfType<SGamePlayObject>().Single(p =>
            p.DamagedMeshes.Any(stage => stage.Event == "molotov"));
        SGamePlayObject can = rig.Server.Map.MapActors.OfType<SGamePlayObject>().Single(p =>
            p.DamagedMeshes.Any(stage => stage.Event == "canette5"));
        SGamePlayObject[] props = [gas, bottle, can];

        // The three observers start in different rooms. Keep all target props relevant while checking their
        // replicated disappearance; the effect notify itself broadcasts regardless of the viewer's room.
        foreach (SGamePlayObject prop in props) prop.bAlwaysRelevant = true;
        bool AllPropsOpen() => rig.Server.Driver.ClientConnections.All(connection =>
            props.All(prop => connection.FindActorChannel(prop) is { Closing: false }));
        for (int i = 0; i < 600 && !AllPropsOpen(); i++) rig.Step();
        Assert.True(AllPropsOpen(), "Target prop channels did not open before the outage.");
        for (int i = 0; i < 30; i++) rig.Step();

        // A short outage guarantees the original one-shot datagrams disappear. Another observer receives
        // duplicated/reordered traffic, which must not toggle the same particle effect twice.
        rig.SimLink!.PauseLink("server", "client0", rig.Clock.NowMicroseconds + 500_000);
        rig.SimLink.SetLinkProfile("server", "client1", new SimLinkProfile("effects", 120, 30, 10, 50, 50, 80));

        // Weapon RPCs break props between ticks. RelayLate must preserve these events through World.Step.
        gas.eventTakeDamage(1000, null, gas.Location, default, gas.TakeDamageTypes[0]);
        bottle.eventTakeDamage(1000, null, bottle.Location, default, bottle.TakeDamageTypes[0]);
        can.eventTakeDamage(1000, null, can.Location, default, can.TakeDamageTypes[0]);
        Assert.True(gas.bAlreadyDestroyed);
        Assert.True(bottle.bAlreadyDestroyed);
        Assert.True(can.bAlreadyDestroyed);
        SimEvent[] raised = rig.Server.World.Events.Late.ToArray().Where(e => e.What == Emitter.NAME_ReplicateTrigger).ToArray();
        Assert.Equal(3, raised.Length);
        Assert.All(raised, e => Assert.True(NetDeviceEvents.IsRelayed(rig.Server.World.Level.FindActor(e.ActorId), e)));
        for (int i = 0; i < 8 * Sim.Hz; i++)
        {
            rig.Step();
        }

        Assert.True(rig.SimLink.Lost > 0);
        Assert.True(rig.SimLink.Duplicated > 0);
        Assert.True(rig.SimLink.Reordered > 0);
        foreach (List<string> observed in effects)
        {
            Assert.True(observed.Count > 0, $"server sent {rig.Server.DeviceEvents.Count}; clients received {string.Join(',', rig.Clients.Select(c => c.DeviceEvents?.Count))}; seen {string.Join('|', effects.Select(list => string.Join(',', list)))}");
            Assert.Equal(["boomgaz3", "molotov", "canette5"], observed);
        }

        // The effect must accompany the replicated disappearance of the intact prop.
        foreach (SGamePlayObject prop in props)
        {
            int index = rig.Server.Driver.MapBinding.IndexOf(prop);
            Assert.True(index > 0);
            foreach (ClientSession client in rig.Clients)
            {
                SGamePlayObject copy = Assert.IsType<SGamePlayObject>(client.Map!.MapActors[index]);
                Assert.Equal(EDrawType.DT_None, copy.DrawType);
                // Blocking flags need not replicate: PostNetReceive disables the hidden mesh's colliders.
                var collision = client.Map.Physics!.Collision;
                var colliders = new List<int>();
                collision.GetActorColliders(copy, colliders);
                Assert.NotEmpty(colliders);
                Assert.All(colliders, collider => Assert.False(collision.GetCollider(collider).Enabled));
            }
        }
    }

    private sealed class EffectSink(Level level, List<string> observed) : ISimEventSink
    {
        public void OnSimEvents(long tick, ReadOnlySpan<SimEvent> events)
        {
            foreach (SimEvent e in events)
            {
                if (e.Kind == SimEventKind.Notify && e.What == Emitter.NAME_ReplicateTrigger &&
                    level.FindActor(e.ActorId) is Emitter emitter && emitter.Tag is var tag &&
                    (tag == "boomgaz3" || tag == "molotov" || tag == "canette5"))
                {
                    observed.Add(tag.ToString());
                }
            }
        }
    }
}
