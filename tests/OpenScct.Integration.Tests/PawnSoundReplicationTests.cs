using System;
using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Server;
using Xunit;

namespace OpenScct.Integration.Tests;

public sealed class PawnSoundReplicationTests
{
    private sealed class Recorder : ISimEventSink
    {
        public readonly List<SimEvent> Heard = new(2048);
        public void OnSimEvents(long tick, ReadOnlySpan<SimEvent> events)
        {
            foreach (SimEvent e in events) if (e.Heard) Heard.Add(e);
        }
    }

    [Theory]
    [InlineData("100")]
    [InlineData("200")]
    [InlineData("300")]
    public void PawnLoopStartsAndStopsArriveOnceInOrderWithoutProxyNotifies(string link)
    {
        using var rig = WalkerRig.Create(SimLinkProfile.Get(link), 193, 1,
            new ServerSettings { SpawnWalkers = false }, new ClientSettings { Gameplay = true });
        Level level = rig.Server.World.Level;
        rig.Server.PlayerJoined += slot =>
        {
            var pc = level.Spawn<PlayerController>()!;
            pc.bIsPlayer = true;
            pc.Player = new SimNetConnection(); pc.RemoteRole = ENetRole.ROLE_AutonomousProxy;
            slot.Connection.Actor = pc;
        };
        level.SetService(new MovementService());
        NetHearSound.InstallServer(rig.Server.World, rig.Server.Driver);
        var pawn = level.Spawn<SPawnAttaque>()!;
        pawn.bAlwaysRelevant = true; pawn.RemoteRole = ENetRole.ROLE_SimulatedProxy;
        pawn.Physics = EPhysics.PHYS_None;
        pawn.PawnAnim.Play(0, PawnActions.Clip(pawn, new Name("WaitStAlFd0")), 1f, 0f, loop: true);
        var unresolved = level.Spawn<SPawnAttaque>()!;
        unresolved.RemoteRole = ENetRole.ROLE_None;
        unresolved.Physics = EPhysics.PHYS_None;
        for (int i = 0; i < 300; i++) rig.Step();
        var recorder = new Recorder();
        rig.Clients[0].World.EventSink = recorder;
        rig.Clients[0].BeforeWorldTick += client =>
            recorder.OnSimEvents(client.World.TickCount + 1, client.World.Events.Late);
        AssetRef sound = new("Armes_impacts.Gadgets.JUMloop");
        int step = 0;
        rig.Server.BeforeWorldTick += _ =>
        {
            // An actor reference that never becomes relevant must not hold up another pawn's sound stream.
            if (step == 0) unresolved.PlaySound(new AssetRef("Armes_impacts.Gadgets.VisionRUN_1"), Attenuate: false);
            // Stopping an unplayed traversal loop is idempotent and must not consume a crowd's send budget.
            pawn.StopSound(new AssetRef("Armes_impacts.Gadgets.UnplayedLoop"));
            if (step is 0 or 60) pawn.PlaySound(sound, Attenuate: false);
            if (step is 30 or 90) pawn.StopSound(sound);
            if (step == 120)
            {
                rig.Server.World.IsReplaying = true;
                try { pawn.PlaySound(sound, Attenuate: false); }
                finally { rig.Server.World.IsReplaying = false; }
            }
            step++;
        };
        for (int i = 0; i < 210; i++) rig.Step();
        Assert.Equal(5, PawnSoundReplication.Of(rig.Server.Driver).Sent);
        var kinds = new List<SimEventKind>();
        foreach (SimEvent e in recorder.Heard)
            if (e.What == new Name(sound.Path)) kinds.Add(e.Kind);
        Assert.Equal(new[] { SimEventKind.PlaySound, SimEventKind.StopSound, SimEventKind.PlaySound, SimEventKind.StopSound }, kinds);
        Assert.Equal(4, PawnSoundReplication.Of(rig.Clients[0].Driver).Presented);
        Assert.Equal(1, PawnSoundReplication.Of(rig.Clients[0].Driver).Obsolete);
        Assert.True(rig.SimLink!.Duplicated > 0);
        Assert.True(rig.SimLink.Reordered > 0);

        // Defined sound ids avoid allocating decoded strings on every repeated footstep or looping notify.
        rig.Server.BeforeWorldTick += _ => { pawn.PlaySound(sound, Attenuate: false); pawn.StopSound(sound); };
        for (int i = 0; i < 120; i++) rig.Step();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 120; i++) rig.Step();
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
    [Theory]
    [InlineData("100")]
    [InlineData("300")]
    public void PredictedSoundsRelayOnlyToOtherPlayerWithoutDuplicates(string link)
    {
        using var rig = WalkerRig.Create(SimLinkProfile.Get(link), 194, 2,
            new ServerSettings { SpawnWalkers = false }, new ClientSettings { Gameplay = true });
        Level level = rig.Server.World.Level;
        level.SetService(new MovementService());
        NetHearSound.InstallServer(rig.Server.World, rig.Server.Driver);
        var source = level.Spawn<SPawnAttaque>()!;
        source.bAlwaysRelevant = true;
        source.RemoteRole = ENetRole.ROLE_SimulatedProxy;
        source.Physics = EPhysics.PHYS_None;
        source.PawnAnim.Play(0, PawnActions.Clip(source, new Name("WaitStAlFd0")), 1f, 0f, loop: true);
        bool owned = false;
        rig.Server.PlayerJoined += slot =>
        {
            var pc = level.Spawn<PlayerController>()!;
            pc.bIsPlayer = true;
            pc.Player = new SimNetConnection();
            pc.RemoteRole = ENetRole.ROLE_AutonomousProxy;
            slot.Connection.Actor = pc;
            if (!owned) { pc.Pawn = source; source.Controller = pc; owned = true; }
        };
        for (int i = 0; i < 300; i++) rig.Step();
        var records = new[] { new Recorder(), new Recorder() };
        for (int i = 0; i < 2; i++)
        {
            Recorder record = records[i];
            rig.Clients[i].World.EventSink = record;
            rig.Clients[i].BeforeWorldTick += client =>
                record.OnSimEvents(client.World.TickCount + 1, client.World.Events.Late);
        }
        var sound = new AssetRef("Sound_Persos.FootSteps.SW_Landing");
        bool fired = false;
        rig.Server.BeforeWorldTick += _ =>
        {
            if (fired) return;
            fired = true;
            source.PlayOwnedSound(sound);
        };
        for (int i = 0; i < 150; i++) rig.Step();
        int total = 0, silent = 0;
        foreach (Recorder record in records)
        {
            int count = record.Heard.FindAll(e => e.What == new Name(sound.Path)).Count;
            Assert.InRange(count, 0, 1);
            total += count;
            if (count == 0) silent++;
        }
        Assert.Equal(1, PawnSoundReplication.Of(rig.Server.Driver).Sent);
        long obsolete = PawnSoundReplication.Of(rig.Clients[0].Driver).Obsolete +
            PawnSoundReplication.Of(rig.Clients[1].Driver).Obsolete;
        // Under loss/reordering a one-shot may arrive after its displayed action.
        // It must be dropped then, not replayed late or echoed to its owner.
        Assert.Equal(1L, total + obsolete);
        if (link == "100") Assert.Equal(1, total);
        Assert.True(silent >= 1);
    }

}
