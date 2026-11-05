using System;
using System.IO;
using System.Linq;
using OpenScct.Core;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Net.Discovery;
using OpenScct.Server;
using OpenScct.Server.Online;
using OpenScctGame.Play.Session;
using Xunit;

namespace OpenScct.Integration.Tests.Online;

public sealed class RecordedViewTests
{
    [Fact]
    public void ASpyAimingTheShockerKeepsTheRecordedCameraPoseInReplay()
    {
        Assert.SkipWhen(WalkerRig.Maps.FindFolder(MapCatalog.TestBox) is null, "The TestBox fixture is unavailable.");
        string folder = Path.Combine(Path.GetTempPath(), "openscct-spy-camera-" + Guid.NewGuid().ToString("N"));
        try
        {
            var clock = new VirtualClock();
            using var play = new PlaySession(new PlaySettings
            {
                Map = MapCatalog.TestBox, Side = PlaySide.Spy, HostMode = PlayHostMode.Inline,
                Rules = PlayRules.PlayLevel, DemoFolder = folder,
            }, clock);
            long frame = 0;
            void Step(int count)
            {
                for (int i = 0; i < count; i++)
                {
                    clock.AdvanceTo(VirtualClock.TickToMicroseconds(++frame, Sim.Hz));
                    play.Step(clock.NowMicroseconds);
                }
            }

            Step(180);
            Assert.IsAssignableFrom<SPawnAttaque>(play.LocalPawn);
            Assert.True(play.Client.RecordingPlayerViews);
            Assert.True(play.View.Valid);
            Assert.False(play.View.SpyFirstPerson);
            int pawnId = Assert.IsAssignableFrom<SPawnAttaque>(play.ServerController?.Pawn).ActorId;
            uint beforeTick = (uint)play.Host.World.TickCount - Sim.Hz / 4;
            ViewSample before = play.View;
            play.Input.Set("bFireMode", true);
            Step(4);
            play.Input.Set("bFireMode", false);
            Step(90);
            Assert.Equal(2, play.LocalPawn!.WeaponLoaded);
            play.Input.MouseX = 1000f;
            play.Input.MouseY = -150f;
            Step(90);
            uint afterTick = (uint)play.Host.World.TickCount;
            ViewSample after = play.View;
            Assert.False(before.Rotation.EqualsWrapped(after.Rotation));
            Step(12);
            play.Dispose();

            DemoRecording recording = DemoFile.Read(Assert.IsType<string>(play.DemoPath));
            int playerId = Assert.Single(recording.Index.Players).PlayerId;
            using var playback = new DemoPlaybackTransport(recording);
            using var replay = new ClientSession(playback, "demo", 0, new ClientSettings
            {
                PlayerName = "Demo", Gameplay = true, Maps = WalkerRig.Maps, Classes = WalkerRig.Classes,
            });
            for (uint tick = recording.Index.FirstTick; tick <= afterTick; tick++)
            {
                playback.ReleaseUpTo(tick);
                replay.Tick(VirtualClock.TickToMicroseconds(tick - recording.Index.FirstTick + 1, Sim.Hz));
                if (tick != beforeTick && tick != afterTick) continue;

                var spy = Assert.IsAssignableFrom<SPawnAttaque>(replay.Driver.FindActorByNetGuid((uint)pawnId));
                Assert.Equal(playerId, spy.PlayerReplicationInfo?.PlayerID);
                Assert.True(replay.TryRecordedDemoView(recording, spy, tick, out DemoViewFrame recorded),
                    $"No view for player {playerId}, pawn {pawnId} at {tick} in {recording.Index.ViewFrames} samples (found {recorded.PawnId} at {recorded.Tick}).");
                ViewSample expected = tick == beforeTick ? before : after;
                Assert.True(recorded.Rotation.EqualsWrapped(expected.Rotation),
                    $"At {tick} expected {expected.Rotation}, recorded {recorded.Rotation} from {recorded.Tick}.");
                Assert.InRange((expected.Location - recorded.Location).Size(), 0f, 1f);
                Assert.Equal(expected.Fov, recorded.Fov, precision: 2);
                Assert.False(recorded.FirstPersonBody);
            }
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void ActiveRecordingRequestsAndStoresOnlyTheSubmittingPlayersView()
    {
        Assert.SkipWhen(WalkerRig.Maps.FindFolder(MapCatalog.TestBox) is null, "The TestBox fixture is unavailable.");
        string path = Path.Combine(Path.GetTempPath(), "openscct-recorded-view-" + Guid.NewGuid().ToString("N") + DemoFile.Extension);
        try
        {
            var settings = OnlineRig.Settings(MapCatalog.TestBox) with { AutoLaunch = false };
            var clock = new VirtualClock();
            var network = new SimLinkNetwork(clock, 854, SimLinkProfile.A);
            using var rig = new OnlineRig(new OnlineServer(network.Bind("server"), settings, clock.NowMicroseconds), clock);
            OnlineClient Join(string name, int team) => rig.Add(new OnlineClient(
                JoinTarget.Custom("View server", () => network.Bind(name), "server"), OnlineRig.ClientSettings(name, team), rig.Now));
            OnlineClient spy = Join("Spy", 0), merc = Join("Merc", 1);
            Assert.True(rig.RunUntil(() => rig.Clients.All(c => c.State == OnlineClientState.Lobby && c.Lobby.Players.Count == 2), 10));
            spy.SetReady(true); merc.SetReady(true);
            Assert.True(rig.RunUntil(() => spy.Lobby.Players.All(p => p.Ready), 10));
            spy.Launch();
            Assert.True(rig.RunUntil(() => rig.Clients.All(c => c.State == OnlineClientState.Match && c.Session?.LocalPlayer?.Pawn is not null), 20));
            Assert.False(spy.Session!.RecordingPlayerViews);
            var frame = new DemoViewFrame(0, 0, 0, new FVector(100, 200, 300), new FRotator(100, 200, 0), 27f,
                new FRotator(100, 200, 0), .25f, .05f, false);
            spy.Session.SubmitDemoView(frame); // no recording request: nothing is queued
            rig.RunUntil(() => false, .2);
            using var writer = new DemoWriter(path, new DemoHeader());
            rig.Server.Host.StartRecording(writer, rig.Now);
            Assert.True(rig.RunUntil(() => spy.Session.RecordingPlayerViews && merc.Session!.RecordingPlayerViews, 5));
            Assert.Equal(0, writer.Index.ViewFrames);
            uint before = (uint)rig.Server.Host.World.TickCount;
            spy.Session.SubmitDemoView(frame);
            spy.Session.SubmitDemoView(frame); // duplicate calls in one client tick do not add more samples
            Assert.True(rig.RunUntil(() => writer.Index.ViewFrames == 1, 5));
            uint recordedAt = (uint)rig.Server.Host.World.TickCount;
            var controller = rig.Server.Host.Players.Single(p => p.Name == "Spy").Controller!;
            int playerId = controller.PlayerReplicationInfo!.PlayerID;
            int pawnId = controller.Pawn!.ActorId;
            rig.Server.Host.StopRecording();
            Assert.True(rig.RunUntil(() => !spy.Session.RecordingPlayerViews && !merc.Session!.RecordingPlayerViews, 5));
            writer.Dispose();
            var recording = DemoFile.Read(path);
            Assert.True(recording.TryView(playerId, recordedAt, out var recorded));
            Assert.InRange(recorded.Tick, before, recordedAt);
            Assert.Equal(pawnId, recorded.PawnId);
            Assert.Equal(frame.Location, recorded.Location);
            Assert.Equal(frame.Rotation, recorded.Rotation);
            Assert.Equal(27f, recorded.Fov);
            Assert.Equal(1, recording.Index.ViewFrames);
            var currentPawn = spy.Session.LocalPlayer!.Pawn!;
            Assert.True(spy.Session.TryRecordedDemoView(recording, currentPawn, recordedAt, out _));
            // A fresh pawn can already have the same player identity before its first camera sample arrives.
            // Matching PRI alone would apply the previous life's position and remote-camera body visibility.
            var replacement = spy.Session.World.Level.Spawn<OpenScct.Game.SBase.SPawnAttaque>()!;
            replacement.PlayerReplicationInfo = currentPawn.PlayerReplicationInfo;
            Assert.False(spy.Session.TryRecordedDemoView(recording, replacement, recordedAt, out _));
        }
        finally { File.Delete(path); }
    }
}
