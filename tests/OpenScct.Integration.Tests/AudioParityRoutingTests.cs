using System;
using System.Collections.Generic;
using System.Linq;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Server;
using Xunit;

namespace OpenScct.Integration.Tests;

public sealed class AudioParityRoutingTests
{
    private sealed class Recorder : ISimEventSink
    {
        public readonly List<SimEvent> Events = [];
        public void OnSimEvents(long tick, ReadOnlySpan<SimEvent> events)
        {
            foreach (SimEvent e in events) Events.Add(e);
        }
    }

    [Fact]
    public void ReinitClearsControllerSoundsOnBothSidesEvenWithoutALivePawn()
    {
        var world = new World(1);
        var pc = world.Level.Spawn<SPlayerController>()!;
        var moves = new SPlayerControllerMoves(pc);
        moves.ReinitPawnBecauseOfDivergences();
        moves.ClientReinitDone(0);
        SimEvent[] stops = world.Events.Late.ToArray().Where(e => e.Kind == SimEventKind.StopSound).ToArray();
        Assert.Equal(2, stops.Length);
        Assert.All(stops, e => { Assert.Equal(pc.ActorId, e.ActorId); Assert.True(e.What.IsNone); });
    }

    [Fact]
    public void AuthoritativeSpyStepsReachRemoteHudWithoutOwnerProxyOrReplayEcho()
    {
        using var rig = WalkerRig.Create(SimLinkProfile.Ping200, 829, 2,
            new ServerSettings { SpawnWalkers = false }, new ClientSettings { Gameplay = true });
        Level level = rig.Server.World.Level;
        level.SetService(new MovementService());
        var spy = level.Spawn<SPawnAttaque>(new FVector(300f, 0f, 0f))!;
        var merc = level.Spawn<SPAWNDEFENSE>()!;
        foreach (SBasePawn pawn in new SBasePawn[] { spy, merc })
        {
            pawn.bAlwaysRelevant = true; pawn.RemoteRole = ENetRole.ROLE_SimulatedProxy; pawn.Physics = EPhysics.PHYS_None;
            pawn.PawnAnim.Play(0, PawnActions.Clip(pawn, new Name("WaitStAlFd0")), 1f, 0f, loop: true);
        }
        int joined = 0;
        rig.Server.PlayerJoined += slot =>
        {
            var pc = level.Spawn<SPlayerController>()!;
            pc.bIsPlayer = true; pc.Player = new SimNetConnection(); pc.RemoteRole = ENetRole.ROLE_AutonomousProxy;
            slot.Connection.Actor = pc;
            pc.Pawn = joined++ == 0 ? spy : merc;
            pc.Pawn.Controller = pc;
        };
        for (int i = 0; i < 300; i++) rig.Step();
        var records = new[] { new Recorder(), new Recorder() };
        var hud = new SHud[2];
        var copies = new SPawnAttaque[2];
        for (int i = 0; i < 2; i++)
        {
            ClientSession client = rig.Clients[i];
            var pc = Assert.IsType<SPlayerController>(client.Driver.ServerConnection!.Actor);
            client.World.Level.Info.LocalPlayerController = pc;
            copies[i] = Assert.IsAssignableFrom<SPawnAttaque>(client.Driver.FindActorByNetGuid((uint)spy.ActorId));
            hud[i] = client.World.Level.Spawn<SHud>(owner: pc)!;
            hud[i].PlayerOwner = pc; pc.myHUD = hud[i];
            client.World.EventSink = records[i];
            Recorder record = records[i];
            client.BeforeWorldTick += c => record.OnSimEvents(c.World.TickCount + 1, c.World.Events.Late);
        }
        spy.Velocity = new FVector(300f, 0f, 0f);
        copies[0].Velocity = spy.Velocity;
        copies[0].PlayFootStepSound(true);
        spy.PlayFootStepSound(true);
        rig.Server.World.IsReplaying = true;
        spy.PlayFootStepSound(true);
        rig.Server.World.IsReplaying = false;
        for (int i = 0; i < 90; i++) rig.Step();
        Assert.Equal(0f, hud[0].ForwardArrowInt);
        Assert.InRange(hud[1].ForwardArrowInt, 0.8f, 0.83f);
        Assert.DoesNotContain(records[0].Events, e => e.What.ToString().Contains("echo_indic1", StringComparison.Ordinal));
        Assert.Single(records[1].Events, e => e.What.ToString().Contains("echo_indic1", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false, ENetMode.NM_DedicatedServer)]
    [InlineData(true, ENetMode.NM_DedicatedServer)]
    [InlineData(false, ENetMode.NM_ListenServer)]
    [InlineData(true, ENetMode.NM_ListenServer)]
    public void MusicEvaluatesInstigatorAndToggleOnEachClient(bool instigatorOnly, ENetMode mode)
    {
        using var rig = WalkerRig.Create(SimLinkProfile.Ping200, 827, 2,
            new ServerSettings { SpawnWalkers = false, NetMode = mode }, new ClientSettings { Gameplay = true });
        Level level = rig.Server.World.Level;
        level.SetService(new MovementService());
        rig.Server.PlayerJoined += slot =>
        {
            var pc = level.Spawn<PlayerController>()!;
            pc.bIsPlayer = true; pc.Player = new SimNetConnection(); pc.RemoteRole = ENetRole.ROLE_AutonomousProxy;
            slot.Connection.Actor = pc;
        };
        var instigator = level.Spawn<SPawnAttaque>()!;
        instigator.bAlwaysRelevant = true; instigator.RemoteRole = ENetRole.ROLE_SimulatedProxy;
        instigator.Physics = EPhysics.PHYS_None;
        NetDeviceEvents relay = NetDeviceEvents.InstallServer(level, rig.Server.Driver);
        Step(300);
        var trigger = level.SpawnActor(new MusicTrigger
        {
            bNoDelete = false, bAlwaysRelevant = true, bToggle = true,
            RemoteRole = ENetRole.ROLE_SimulatedProxy,
            bInstigatorOnly = instigatorOnly, Song = new AssetRef("ParitySong"),
        }, default, default)!;
        trigger.bNoDelete = true;
        rig.Server.Driver.MapBinding.Bind([.. rig.Server.Map.MapActors, trigger]);
        var records = new[] { new Recorder(), new Recorder() };
        for (int i = 0; i < 2; i++)
        {
            ClientSession client = rig.Clients[i];
            var pc = Assert.IsAssignableFrom<PlayerController>(client.Driver.ServerConnection!.Actor);
            client.World.Level.Info.LocalPlayerController = pc;
            pc.Pawn = i == 0 ? Assert.IsAssignableFrom<Pawn>(client.Driver.FindActorByNetGuid((uint)instigator.ActorId)) : null;
            // Bind a synthetic placed trigger on both sides, just as the map loader does.
            var copy = client.World.Level.SpawnActor(new MusicTrigger
            {
                bNoDelete = false, bAlwaysRelevant = true, RemoteRole = ENetRole.ROLE_SimulatedProxy,
                Song = trigger.Song, bInstigatorOnly = instigatorOnly, bToggle = true,
            }, default, default)!;
            copy.bNoDelete = true;
            NetMapBinding.PrepareClientMap(client.World.Level, [copy]);
            client.Driver.MapBinding.Bind([.. client.Map!.MapActors, copy]);
            client.World.EventSink = records[i];
            Recorder record = records[i];
            client.BeforeWorldTick += c => record.OnSimEvents(c.World.TickCount + 1, c.World.Events.Late);
        }
        Step(90);
        trigger.ClientPlayStopMusic(true, instigator);
        Step(90);
        trigger.ClientPlayStopMusic(true, instigator);
        Step(90);
        for (int i = 0; i < 2; i++)
        {
            SimEventKind[] music = records[i].Events.Where(e => e.What.ToString() == "ParitySong").Select(e => e.Kind).ToArray();
            Assert.Equal(instigatorOnly && i == 1 ? [] : new[] { SimEventKind.PlayMusic, SimEventKind.StopMusic }, music);
        }
        Assert.DoesNotContain(rig.Server.World.Events.Current.ToArray(), e => e.Kind is SimEventKind.PlayMusic or SimEventKind.StopMusic);

        void Step(int count)
        {
            for (int i = 0; i < count; i++)
            {
                relay.RelayLate(rig.Server.World);
                rig.Step(afterServerTick: () => relay.RelayTick(rig.Server.World));
            }
        }
    }

    [Fact]
    public void WildcardPawnStopSurvivesWithoutPriorNamedPlayAndDeviceStopNeverBecomesGlobal()
    {
        using var rig = WalkerRig.Create(SimLinkProfile.Ping200, 828, 1,
            new ServerSettings { SpawnWalkers = false }, new ClientSettings { Gameplay = true });
        Level level = rig.Server.World.Level;
        level.SetService(new MovementService());
        rig.Server.PlayerJoined += slot =>
        {
            var pc = level.Spawn<PlayerController>()!;
            pc.bIsPlayer = true; pc.Player = new SimNetConnection(); pc.RemoteRole = ENetRole.ROLE_AutonomousProxy;
            slot.Connection.Actor = pc;
        };
        var source = level.Spawn<SPawnAttaque>()!;
        source.bAlwaysRelevant = true; source.RemoteRole = ENetRole.ROLE_SimulatedProxy; source.Physics = EPhysics.PHYS_None;
        source.PawnAnim.Play(0, PawnActions.Clip(source, new Name("WaitStAlFd0")), 1f, 0f, loop: true);
        var invisible = level.Spawn<SoundTrigger>()!;
        invisible.RemoteRole = ENetRole.ROLE_None;
        NetHearSound.InstallServer(rig.Server.World, rig.Server.Driver);
        NetDeviceEvents relay = NetDeviceEvents.InstallServer(level, rig.Server.Driver);
        Step(300);
        var record = new Recorder();
        ClientSession client = rig.Clients[0];
        client.World.EventSink = record;
        client.BeforeWorldTick += c => record.OnSimEvents(c.World.TickCount + 1, c.World.Events.Late);
        bool once = false;
        rig.Server.BeforeWorldTick += _ =>
        {
            if (once) return;
            once = true;
            source.StopSound(AssetRef.None);
            invisible.StopSound(AssetRef.None);
            rig.Server.World.Events.StopSound(null);
        };
        Step(120);
        SimEvent[] stops = record.Events.Where(e => e.Kind == SimEventKind.StopSound && e.What.IsNone).ToArray();
        Assert.Equal(2, stops.Length);
        Assert.Single(stops, e => e.ActorId == 0);
        Assert.Single(stops, e => e.ActorId != 0);

        void Step(int count)
        {
            for (int i = 0; i < count; i++) rig.Step(afterServerTick: () => relay.RelayTick(rig.Server.World));
        }
    }
}
