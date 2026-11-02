using System;
using System.IO;
using System.Linq;
using OpenScct.Bots;
using OpenScct.Game.AI;
using OpenScct.Net;
using OpenScct.Server;
using OpenScct.Server.Online;
using Xunit;

namespace OpenScct.Integration.Tests.Online;

public sealed class PersonalDemoTests
{
    [Fact]
    public void GuestCanRequestAFullDemoWithoutHostRecordingAndDownloadAcrossTravel()
    {
        Assert.SkipWhen(WalkerRig.Maps.FindFolder(MapCatalog.TestBox) is null, "TestBox fixture is unavailable.");
        string folder = Path.Combine(Path.GetTempPath(), "openscct-personal-demo-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var settings = OnlineRig.Settings(MapCatalog.TestBox, timeLimit: 1) with
            {
                Dedicated = false, HostKey = "k", Bots = OnlineServerBots.Factory(new AiNavCache(string.Empty)),
            };
            settings = settings with { Setup = settings.Setup with { Bots = BotFill.Fill, BotSkill = AiSkill.Easy } };
            var clock = new VirtualClock();
            var network = new SimLinkNetwork(clock, 27, SimLinkProfile.A);
            using var rig = new OnlineRig(new OnlineServer(network.Bind("server"), settings, clock.NowMicroseconds), clock);
            rig.Server.Logged += rig.Log.Add;
            int visit = 0, recorded = 0;
            rig.Server.DemoRecorded += (_, _) => recorded++;
            OnlineClient Join(string name, int team, bool record, string? hostKey = null) => rig.Add(new OnlineClient(
                JoinTarget.Custom("Personal demos", () => network.Bind(name + "-" + visit++), "server"),
                OnlineRig.ClientSettings(name, team) with { HostKey = hostKey, RecordDemos = record,
                    DemoFolder = Path.Combine(folder, name) }, rig.Now));
            var host = Join("Host", 0, false, "k");
            var guest = Join("Guest", 1, true);
            string State() => rig.Server.Status() + " | " + host.State + " / " + guest.State + " | " + guest.DemoRecordingStatus + " | " + string.Join(";", rig.Log.TakeLast(8));
            Assert.True(rig.RunUntil(() => guest.State == OnlineClientState.Lobby && guest.Lobby.Players.Count == 4 &&
                guest.DemoRecordingStatus.Contains("next match", StringComparison.Ordinal), 15), State());
            Assert.False(host.RecordDemos);
            Assert.Null(settings.DemoFolder);
            guest.SetReady(true);
            Assert.True(rig.RunUntil(() => host.Lobby.Players.Any(player => player.Name == "Guest" && player.Ready), 5));
            host.Launch();
            Assert.True(rig.RunUntil(() => guest.State == OnlineClientState.Match && host.State == OnlineClientState.Match, 45), State());
            Assert.NotNull(rig.Server.Host.Recorder);
            Assert.True(rig.RunUntil(() => guest.Results is not null && host.Results is not null, 90), State());
            host.SetReady(true);
            guest.SetReady(true);
            Assert.True(rig.RunUntil(() => guest.LastDemoPath is not null && guest.State == OnlineClientState.Lobby, 45), State());
            Assert.Equal(1, recorded);
            Assert.Null(host.LastDemoPath);
            Assert.False(Directory.Exists(Path.Combine(folder, "Host")));
            Assert.Empty(Directory.GetFiles(Path.Combine(folder, "Guest"), "*.partial"));
            var recording = DemoFile.Read(guest.LastDemoPath!);
            Assert.True(recording.PacketCount > 0);
            Assert.Equal(MapCatalog.TestBox, recording.Header.Map);
            Assert.Contains(recording.Index.Players, player => player.Name == "Host");
            Assert.Contains(recording.Index.Players, player => player.Name == "Guest");
            Assert.Contains(recording.Index.Players, player => player.Name.StartsWith("SpyBot", StringComparison.Ordinal));
            Assert.Contains(recording.Index.Players, player => player.Name.StartsWith("MercBot", StringComparison.Ordinal));
            Assert.Contains(recording.Index.Markers, marker => marker.Kind == DemoMarkerKind.RoundStart);
            string firstGuestDemo = guest.LastDemoPath!;

            // Each player can change their own preference without control permission or changing match rules.
            guest.SetRecordDemos(false);
            host.SetRecordDemos(true);
            Assert.True(rig.RunUntil(() => host.DemoRecordingStatus.Contains("next match", StringComparison.Ordinal), 5), State());
            Assert.False(guest.RecordDemos);
            Assert.True(host.RecordDemos);
            Assert.Equal(settings.Setup.Map, guest.Lobby.Setup.Map);
            guest.SetReady(true);
            Assert.True(rig.RunUntil(() => host.Lobby.Players.Any(player => player.Name == "Guest" && player.Ready), 5));
            host.Launch();
            Assert.True(rig.RunUntil(() => guest.State == OnlineClientState.Match && host.State == OnlineClientState.Match, 45), State());
            Assert.True(rig.RunUntil(() => host.LastDemoPath is not null, 110), State());
            Assert.Equal(2, recorded); // One server recording per round, rather than a recorder for each requesting client.
            Assert.Equal(firstGuestDemo, guest.LastDemoPath);
            Assert.Single(Directory.GetFiles(Path.Combine(folder, "Guest"), "*.scctdemo"));
            Assert.Single(Directory.GetFiles(Path.Combine(folder, "Host"), "*.scctdemo"));
            Assert.Contains(DemoFile.Read(host.LastDemoPath!).Index.Players, player => player.Name == "Guest");
        }
        finally { Directory.Delete(folder, recursive: true); }
    }
}
