using System;
using System.IO;
using System.Linq;
using OpenScct.Bots;
using OpenScct.Game.AI;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Server;
using OpenScct.Server.Online;
using Xunit;

namespace OpenScct.Integration.Tests.Online;

public sealed class RecordingLaunchTests
{
    [Fact]
    public void RecordingDoesNotPreventListenHostFromLoadingABotMatch()
    {
        Assert.SkipWhen(WalkerRig.Maps.FindFolder(MapCatalog.TestBox) is null, "TestBox fixture is unavailable.");
        string folder = Path.Combine(Path.GetTempPath(), "openscct-record-launch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var settings = OnlineRig.Settings(MapCatalog.TestBox) with
            {
                Dedicated = false, HostKey = "k", DemoFolder = folder,
                Bots = OnlineServerBots.Factory(new AiNavCache(string.Empty)),
            };
            settings = settings with { Setup = settings.Setup with { Bots = BotFill.Fill, BotSkill = AiSkill.Expert } };
            var clock = new VirtualClock();
            var network = new SimLinkNetwork(clock, 5, SimLinkProfile.A);
            using (var rig = new OnlineRig(new OnlineServer(network.Bind("server"), settings, clock.NowMicroseconds), clock))
            {
                rig.Server.Logged += rig.Log.Add;
                int visit = 0;
                var target = JoinTarget.Custom("Recorded server", () => network.Bind("host-" + visit++), "server");
                var host = rig.Add(new OnlineClient(target, OnlineRig.ClientSettings("Host", 0) with { HostKey = "k" }, rig.Now));
                string State() => rig.Server.Status() + " | " + host.State + " " + host.Error + " | " + string.Join(";", rig.Log.TakeLast(12));

                Assert.True(rig.RunUntil(() => host.State == OnlineClientState.Lobby && host.Lobby.Players.Count == 4, 15), State());
                host.Launch();
                Assert.True(rig.RunUntil(() => host.State == OnlineClientState.Match && host.Session?.LocalPlayer?.Pawn is not null, 15), State());

                // The recorder neither consumes one of the four player places nor waits for a loading screen.
                var recorder = Assert.IsType<DemoRecorder>(rig.Server.Host.Recorder);
                Assert.True(recorder.Controller?.PlayerReplicationInfo?.bOnlySpectator);
                Assert.True(recorder.Controller?.PlayerReplicationInfo?.bWaitingPlayer);
                Assert.Null(recorder.Controller?.Pawn);
                Assert.Equal(4, rig.Server.Host.Game?.NumPlayers);
                Assert.Single(rig.Server.Host.Players);
                rig.RunUntil(() => false, 2);
                rig.Server.Host.StopRecording();
            }

            string file = Assert.Single(Directory.GetFiles(folder, "*.scctdemo"));
            var recording = DemoFile.Read(file);
            Assert.True(recording.PacketCount > 0);
            Assert.Equal(4, recording.Index.Players.Count);
            Assert.Contains(recording.Index.Players, player => player.Name == "Host");
            Assert.DoesNotContain(recording.Index.Players, player => player.Name == "Demo");
            Assert.Contains(recording.Index.Markers, marker => marker.Kind == DemoMarkerKind.RoundStart);

            using var playback = new DemoPlaybackTransport(recording);
            using var replay = new ClientSession(playback, "demo", 0, new ClientSettings
            {
                PlayerName = "Demo", Gameplay = true, Maps = WalkerRig.Maps, Classes = WalkerRig.Classes,
            });
            bool sawSpectator = false;
            int maxPlayingPawns = 0;
            for (uint tick = recording.Index.FirstTick; tick <= recording.Index.LastTick; tick++)
            {
                playback.ReleaseUpTo(tick);
                replay.Tick(VirtualClock.TickToMicroseconds(tick - recording.Index.FirstTick + 1, recording.Header.TickRate));
                sawSpectator |= replay.LocalPlayer?.PlayerReplicationInfo?.bOnlySpectator == true;
                int playingPawns = 0;
                foreach (var pawn in replay.World.Level.DynamicActors<SBasePawn>())
                    if (!pawn.bDeleteMe && pawn.PlayerReplicationInfo is { bOnlySpectator: false }) playingPawns++;
                maxPlayingPawns = Math.Max(maxPlayingPawns, playingPawns);
            }

            Assert.True(playback.AtEnd);
            Assert.True(sawSpectator);
            Assert.Equal(4, maxPlayingPawns);
        }
        finally { Directory.Delete(folder, recursive: true); }
    }
}
