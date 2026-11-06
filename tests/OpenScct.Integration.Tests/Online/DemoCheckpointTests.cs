using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Server;
using OpenScctGame.Play.Session;
using Xunit;

namespace OpenScct.Integration.Tests.Online;

public sealed class DemoCheckpointTests
{
    private static readonly Name Music = new("Armes_impacts.Gadgets.Emf_fond");
    private static readonly Name Cue = new("Sound_Persos.onomatopees.breath_masqGaz");
    private static readonly Name PawnSound = new("Armes_impacts.Gadgets.Hack_try1");
    private static readonly Name Burst = new("SBase.SSparkShocker");
    private static readonly FVector BurstLocation = new(1500, 500, 120);

    [Fact]
    public void SeekingFromTheMinuteCheckpointRestoresWorldAndContinuesTheRecording()
    {
        Assert.SkipWhen(WalkerRig.Maps.FindFolder(MapCatalog.TestBox) is null, "The TestBox fixture is unavailable.");
        string folder = Path.Combine(Path.GetTempPath(), "openscct-demo-checkpoint-" + Guid.NewGuid().ToString("N"));
        try
        {
            var clock = new VirtualClock();
            using var play = new PlaySession(new PlaySettings
            {
                Map = MapCatalog.TestBox, Side = PlaySide.Spy, HostMode = PlayHostMode.Inline,
                OfflineRules = true, DemoFolder = folder, NetRate = 35,
            }, clock);
            long frame = 0;
            void StepTo(uint tick)
            {
                while (play.Host.World.TickCount < tick)
                {
                    clock.AdvanceTo(VirtualClock.TickToMicroseconds(++frame, Sim.Hz));
                    play.Step(clock.NowMicroseconds);
                }
            }

            StepTo(180);
            var recorder = Assert.IsType<DemoRecorder>(play.Host.Recorder);
            uint roundStart = Assert.Single(recorder.Writer.Index.Markers, mark => mark.Kind == DemoMarkerKind.RoundStart).Tick;
            uint expectedCheckpoint = roundStart + 60 * Sim.Hz;
            var game = Assert.IsAssignableFrom<SGameInfo>(play.Loader.Game);
            int spectators = game.NumSpectators;
            var spy = Assert.IsAssignableFrom<SPawnAttaque>(play.ServerController?.Pawn);
            int spyId = spy.ActorId;
            int playerId = Assert.IsAssignableFrom<PlayerReplicationInfo>(spy.PlayerReplicationInfo).PlayerID;
            var mover = Assert.Single(play.Host.Map.MapActors.OfType<Mover>());
            int moverIndex = play.Host.Driver.MapBinding.IndexOf(mover);
            SObjective objective = play.Host.Map.MapActors.OfType<SObjective>().First(o => o.ObjectiveName == "Terminal B");
            int objectiveIndex = play.Host.Driver.MapBinding.IndexOf(objective);

            play.Input.MouseX = 1000f;
            play.Input.MouseY = -150f;
            play.Input.Set("BT_Up", true);
            StepTo(220);
            play.Input.Set("BT_Up", false);
            SimEvents events = play.Host.World.Events;
            events.Add(new SimEvent { Kind = SimEventKind.PlayMusic, What = Music });
            events.PlaySound(spy, PawnSound, attenuate: false);
            StepTo(expectedCheckpoint - 90);
            var grenadeOrigin = new FVector(1800, 500, 260);
            var oldGrenade = Assert.IsType<SProjFrag>(play.Host.World.Level.Spawn<SProjFrag>(grenadeOrigin, default));
            oldGrenade.Launch(0, bQuickLaunch: false);
            oldGrenade.SetTimer(oldGrenade.TempsExplosion, bLoop: false);
            StepTo(expectedCheckpoint - 30);
            FRotator before = play.View.Rotation;
            mover.InterpolateTo(1, 1f);
            objective.ObjectiveTaken = true;
            events.PlaySound(null, Cue, attenuate: false);
            events.SpawnEmitter(null, Burst, BurstLocation, FRotator.Zero);
            var bomb = Assert.IsType<SMineProxEmitter>(play.Host.World.Level.Spawn<SMineProxEmitter>(spy.Location, default));

            StepTo(expectedCheckpoint);
            if (recorder.Writer.Index.CheckpointCount == 0) StepTo(expectedCheckpoint + 1);
            Assert.Equal(1, recorder.Writer.Index.CheckpointCount);
            uint capturedAt = (uint)play.Host.World.TickCount;
            FVector grenadeLocation = oldGrenade.Location;
            float grenadeFuse = oldGrenade.TimerRate - oldGrenade.TimerCounter;
            float bombLife = bomb.LifeSpan;
            Assert.True(FVector.Dist(grenadeOrigin, grenadeLocation) > 80f);
            Assert.InRange(grenadeFuse, 0.4f, 0.6f);
            Assert.True(Assert.Single(play.Client.World.Level.Actors.OfType<SMineProxEmitter>()).bNetTemporary);
            Assert.Null(Assert.Single(play.Host.Players).Connection.FindActorChannel(bomb));
            Assert.NotNull(recorder.Connection.FindActorChannel(bomb));

            StepTo(expectedCheckpoint + 20);
            Assert.False(oldGrenade.bDeleteMe);
            var newGrenade = Assert.IsType<SProjFrag>(play.Host.World.Level.Spawn<SProjFrag>(new FVector(600, 1000, 100), default));
            newGrenade.SetPhysics(EPhysics.PHYS_None);
            objective.ObjectiveTaken = false;
            // The recorded spectator already knows this sound's wire name from tick 220.
            events.PlaySound(spy, PawnSound, attenuate: false);
            play.Input.MouseX = -1700f;
            play.Input.MouseY = 200f;
            uint endTick = expectedCheckpoint + 90;
            StepTo(endTick);
            Assert.True(oldGrenade.bDeleteMe);
            FRotator after = play.View.Rotation;
            Assert.False(before.EqualsWrapped(after));
            uint secondCheckpoint = expectedCheckpoint + 60 * Sim.Hz;
            StepTo(secondCheckpoint + 12);
            Assert.Same(spy, play.ServerController?.Pawn);
            Assert.Equal(1, game.NumPlayers);
            Assert.Equal(spectators, game.NumSpectators);
            Assert.Single(play.Host.Players);
            play.Dispose();

            DemoRecording recording = DemoFile.Read(Assert.IsType<string>(play.DemoPath));
            Assert.Equal(playerId, Assert.Single(recording.Index.Players).PlayerId);
            Assert.Equal(roundStart, Assert.Single(recording.Index.Markers, mark => mark.Kind == DemoMarkerKind.RoundStart).Tick);
            using var checkpoint = new DemoPlaybackTransport(recording, endTick);
            Assert.InRange(checkpoint.StartTick, expectedCheckpoint, expectedCheckpoint + 1);
            Assert.Equal(capturedAt, checkpoint.StartTick);
            using var early = new DemoPlaybackTransport(recording, expectedCheckpoint - 1);
            Assert.Equal(recording.Index.FirstTick, early.StartTick);
            using var later = new DemoPlaybackTransport(recording, secondCheckpoint + 2);
            Assert.InRange(later.StartTick, secondCheckpoint, secondCheckpoint + 1);
            using var linear = new DemoPlaybackTransport(recording);
            var settings = new ClientSettings { PlayerName = "Demo", Gameplay = true, Maps = WalkerRig.Maps, Classes = WalkerRig.Classes };
            using var restarted = new ClientSession(checkpoint, "demo", 0, settings);
            using var uninterrupted = new ClientSession(linear, "demo", 0, settings);
            var resumedSounds = new List<SimEvent>();
            restarted.BeforeWorldTick += client =>
            {
                foreach (SimEvent e in client.World.Events.Late)
                    if (e.Kind == SimEventKind.PlaySound && e.What == PawnSound) resumedSounds.Add(e);
            };

            void CheckWorld(uint tick, FRotator rotation, bool afterCheckpoint)
            {
                var baselinePawn = Assert.IsAssignableFrom<SPawnAttaque>(uninterrupted.Driver.FindActorByNetGuid((uint)spyId));
                var restoredPawn = Assert.IsAssignableFrom<SPawnAttaque>(restarted.Driver.FindActorByNetGuid((uint)spyId));
                Assert.Equal(playerId, restoredPawn.PlayerReplicationInfo?.PlayerID);
                Assert.InRange(FVector.Dist(restoredPawn.Location, baselinePawn.Location), 0f, 3f);
                Assert.True(restarted.TryRecordedDemoView(recording, restoredPawn, tick, out DemoViewFrame view));
                Assert.True(rotation.EqualsWrapped(view.Rotation));

                var baselineMover = Assert.IsAssignableFrom<Mover>(uninterrupted.Map!.MapActors[moverIndex]);
                var restoredMover = Assert.IsAssignableFrom<Mover>(restarted.Map!.MapActors[moverIndex]);
                Assert.InRange(FVector.Dist(restoredMover.Location, baselineMover.Location), 0f, 10f);
                Assert.Equal(!afterCheckpoint, restoredMover.bInterpolating);
                var restoredObjective = Assert.IsAssignableFrom<SObjective>(restarted.Map.MapActors[objectiveIndex]);
                Assert.Equal(!afterCheckpoint, restoredObjective.ObjectiveTaken);
                Assert.IsType<SProjFrag>(restarted.Driver.FindActorByNetGuid((uint)(afterCheckpoint ? newGrenade : oldGrenade).ActorId));
                Assert.Null(restarted.Driver.FindActorByNetGuid((uint)(afterCheckpoint ? oldGrenade : newGrenade).ActorId));
                Assert.True(restarted.LocalPlayer?.PlayerReplicationInfo?.bOnlySpectator);
                var restoredClock = Assert.IsAssignableFrom<GameReplicationInfo>(restarted.LocalPlayer?.GameReplicationInfo);
                var baselineClock = Assert.IsAssignableFrom<GameReplicationInfo>(uninterrupted.LocalPlayer?.GameReplicationInfo);
                Assert.True(restoredClock.bMatchHasBegun);
                Assert.InRange(MathF.Abs(restoredClock.PreciseElapsedTime() - baselineClock.PreciseElapsedTime()), 0f, 1.25f);
                Assert.Equal(0, restarted.Connection!.Stats.InWrongSalt);
                Assert.Equal(0, restarted.Connection.Stats.OutReliableResends);
            }

            for (uint tick = linear.StartTick; tick <= endTick; tick++)
            {
                Advance(linear, uninterrupted, tick);
                if (tick < checkpoint.StartTick) continue;
                Advance(checkpoint, restarted, tick);
                if (tick == checkpoint.StartTick)
                {
                    CheckWorld(tick, before, afterCheckpoint: false);
                    var restoredBomb = Assert.IsType<SMineProxEmitter>(restarted.Driver.FindActorByNetGuid((uint)bomb.ActorId));
                    Assert.True(restoredBomb.bNetTemporary);
                    Assert.InRange(MathF.Abs(restoredBomb.LifeSpan - bombLife), 0f, 2 * Sim.DeltaTime);
                    CheckPresentation(uninterrupted, restarted, tick, restoredBomb);
                    var restoredGrenade = Assert.IsType<SProjFrag>(restarted.Driver.FindActorByNetGuid((uint)oldGrenade.ActorId));
                    Assert.InRange(FVector.Dist(restoredGrenade.Location, grenadeLocation), 0f, 12f);
                    Assert.InRange(MathF.Abs((restoredGrenade.TimerRate - restoredGrenade.TimerCounter) - grenadeFuse), 0f, 0.04f);
                }
                if (tick == expectedCheckpoint + 20)
                    Assert.IsType<SProjFrag>(restarted.Driver.FindActorByNetGuid((uint)oldGrenade.ActorId));
                if (tick == endTick) CheckWorld(tick, after, afterCheckpoint: true);
            }
            Assert.True(restarted.Connection!.Stats.InPackets < uninterrupted.Connection!.Stats.InPackets);
            SimEvent resumed = Assert.Single(resumedSounds);
            Assert.True(resumed.Heard);
            Assert.Equal(restarted.Driver.FindActorByNetGuid((uint)spyId)?.ActorId, resumed.ActorId);
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }

    private static void CheckPresentation(ClientSession linear, ClientSession restarted, uint tick, SMineProxEmitter bomb)
    {
        var samples = new List<DemoPresentationSample>();
        linear.TakeDemoCheckpointPresentation(samples);
        Assert.Empty(samples);
        restarted.TakeDemoCheckpointPresentation(samples);
        var music = Assert.Single(samples, sample => sample.Event.Kind == SimEventKind.PlayMusic && sample.Event.What == Music);
        Assert.InRange(music.Tick, 220u, 224u);
        Assert.Equal(Music.ToString(), MusicState.Of(restarted.World.Level).Current.Path);
        var cue = Assert.Single(samples, sample => sample.Event.Kind == SimEventKind.PlaySound && sample.Event.What == Cue);
        Assert.InRange(tick - cue.Tick, 25u, 32u);
        var burst = Assert.Single(samples, sample => sample.Event.Kind == SimEventKind.SpawnEmitter && sample.Event.What == Burst);
        Assert.Equal(cue.Tick, burst.Tick);
        Assert.Equal(BurstLocation, burst.Event.Location);
        var liveBomb = Assert.Single(samples, sample => sample.IsLiveEmitter && sample.Event.ActorId == bomb.ActorId);
        Assert.InRange(tick - liveBomb.Tick, 20u, 32u);
        samples.Clear();
        restarted.TakeDemoCheckpointPresentation(samples);
        Assert.Empty(samples);
    }

    [Fact]
    public void EditorPlayRecordsCheckpointsWithoutARoundStartMarker()
    {
        Assert.SkipWhen(WalkerRig.Maps.FindFolder(MapCatalog.TestBox) is null, "The TestBox fixture is unavailable.");
        string folder = Path.Combine(Path.GetTempPath(), "openscct-editor-checkpoint-" + Guid.NewGuid().ToString("N"));
        try
        {
            var clock = new VirtualClock();
            using var play = new PlaySession(new PlaySettings
            {
                Map = MapCatalog.TestBox, Side = PlaySide.Spy, HostMode = PlayHostMode.Inline,
                Rules = PlayRules.PlayLevel, DemoFolder = folder,
            }, clock);
            for (int tick = 1; tick <= 61 * Sim.Hz; tick++)
            {
                clock.AdvanceTo(VirtualClock.TickToMicroseconds(tick, Sim.Hz));
                play.Step(clock.NowMicroseconds);
            }

            int pawnId = Assert.IsAssignableFrom<SPawnAttaque>(play.ServerController?.Pawn).ActorId;
            play.Dispose();
            DemoRecording recording = DemoFile.Read(Assert.IsType<string>(play.DemoPath));
            Assert.DoesNotContain(recording.Index.Markers, mark => mark.Kind == DemoMarkerKind.RoundStart);
            using var playback = new DemoPlaybackTransport(recording, recording.Index.LastTick);
            Assert.Equal(recording.Index.FirstTick + 60 * Sim.Hz, playback.StartTick);
            using var replay = new ClientSession(playback, "demo", 0, new ClientSettings
            {
                PlayerName = "Demo", Gameplay = true, Maps = WalkerRig.Maps, Classes = WalkerRig.Classes,
            });
            Advance(playback, replay, playback.StartTick);
            Assert.IsAssignableFrom<SPawnAttaque>(replay.Driver.FindActorByNetGuid((uint)pawnId));
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }

    private static void Advance(DemoPlaybackTransport transport, ClientSession client, uint tick)
    {
        transport.ReleaseUpTo(tick);
        client.Tick(VirtualClock.TickToMicroseconds(tick - transport.StartTick + 1, Sim.Hz));
    }
}
