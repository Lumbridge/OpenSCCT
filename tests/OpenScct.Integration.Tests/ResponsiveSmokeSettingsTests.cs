using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using OpenScct.Net;
using OpenScct.Net.Discovery;
using OpenScct.Server;
using OpenScct.Server.Online;
using OpenScct.Integration.Tests.Online;
using Xunit;

namespace OpenScct.Integration.Tests;

public sealed class ResponsiveSmokeSettingsTests : IDisposable
{
    private readonly string _demos = Path.Combine(Path.GetTempPath(), "svm-responsive-series-" + Guid.NewGuid().ToString("N"));
    public void Dispose()
    {
        if (Directory.Exists(_demos)) Directory.Delete(_demos, recursive: true);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RevengeRoundRetainsHostRulesOnBothClientsAndInDemoMetadata(bool enabled)
    {
        Assert.SkipWhen(WalkerRig.Maps.FindFolder(MapCatalog.TestBox) is null, "The TestBox fixture is not in this checkout.");
        var clock = new VirtualClock();
        var network = new SimLinkNetwork(clock, 19, SimLinkProfile.A);
        OnlineServerSettings settings = OnlineRig.Settings(MapCatalog.TestBox, timeLimit: 1);
        settings = settings with { DemoFolder = _demos, Setup = settings.Setup with { Revenge = true, ResponsiveSmoke = enabled, Rifles = !enabled, Sidearms = enabled } };
        using var rig = new OnlineRig(new OnlineServer(network.Bind("server"), settings, clock.NowMicroseconds), clock);
        OnlineClient Join(string name, int team)
        {
            int connection = 0;
            return rig.Add(new OnlineClient(JoinTarget.Custom("server", () => network.Bind(name + (connection++).ToString(CultureInfo.InvariantCulture)), "server"),
                OnlineRig.ClientSettings(name, team), clock.NowMicroseconds));
        }
        OnlineClient spy = Join("Spy", 0), merc = Join("Merc", 1);
        Assert.True(rig.RunUntil(() => spy.Lobby.Players.Count == 2 && merc.Lobby.Players.Count == 2, 10));
        spy.SetReady(true);
        merc.SetReady(true);
        Assert.True(rig.RunUntil(() => rig.Server.Phase == SessionPhase.Match, 20));
        VerifyRule();
        Assert.True(rig.RunUntil(() => spy.Results is not null, 90));
        spy.SetReady(true);
        merc.SetReady(true);
        Assert.True(rig.RunUntil(() => rig.Server.Phase == SessionPhase.Match && rig.Server.RoundsStarted == 2 &&
            rig.Server.Players.All(p => p.Slot?.Controller?.PlayerReplicationInfo?.Team is not null), 30));
        VerifyRule();

        void VerifyRule()
        {
            bool Matches(OpenScct.Core.Engine.GameReplicationInfo? gri) =>
                gri != null && gri.ResponsiveSmokeEnabled == enabled && gri.RiflesEnabled == !enabled && gri.SidearmsEnabled == enabled;
            Assert.True(Matches(rig.Server.Host.Game!.GameReplicationInfo));
            Assert.True(rig.RunUntil(() => Matches(spy.Session?.LocalPlayer?.GameReplicationInfo) &&
                Matches(merc.Session?.LocalPlayer?.GameReplicationInfo), 10));
            foreach (var option in new[] { $"?SvmResponsiveSmoke={(enabled ? 1 : 0)}", $"?SvmRifles={(enabled ? 0 : 1)}", $"?SvmSidearms={(enabled ? 1 : 0)}" })
            {
                Assert.Contains(option, rig.Server.Host.Settings.GameOptions);
                Assert.Contains(option, rig.Server.Host.Recorder!.Writer.Header.GameOptions);
            }
        }
    }

    [Fact]
    public void SmokeRuleDefaultsOnAndRoundTripsThroughSavedLobbyAndLaunchOptions()
    {
        var enabled = new LobbySetup();
        var disabled = new LobbySetup { ResponsiveSmoke = false };
        Assert.True(enabled.ResponsiveSmoke);
        Assert.True(LobbySetup.FromOptions("?MapName=ClubE").ResponsiveSmoke);
        Assert.True(LobbySetup.FromOptions(enabled.ToOptions()).ResponsiveSmoke);
        Assert.True(LobbySetup.FromOptions("?TimeLimit=5", enabled).ResponsiveSmoke);
        Assert.False(LobbySetup.FromOptions("?SvmResponsiveSmoke=0", enabled).ResponsiveSmoke);
        Assert.Contains("?SvmResponsiveSmoke=1", enabled.ToGameOptions(2));
        Assert.False(LobbySetup.FromOptions(disabled.ToOptions()).ResponsiveSmoke);
        Assert.False(LobbySetup.FromOptions("?TimeLimit=5", disabled).ResponsiveSmoke);
        Assert.Contains("?SvmResponsiveSmoke=0", disabled.ToGameOptions(2));
        Assert.DoesNotContain("smoke", enabled.Describe());
        Assert.Contains("original smoke", disabled.Describe());
    }

    [Fact]
    public void ResponsiveSmokeDiscoveryFlagDefaultsOffAndRoundTrips()
    {
        foreach (bool enabled in new[] { false, true })
        {
            var info = new ServerInfo { ResponsiveSmoke = enabled, Practice = true, Dedicated = true };
            var buffer = new byte[1024];
            int length = info.Write(buffer);
            Assert.True(ServerInfo.TryRead(buffer.AsSpan(0, length), out ServerInfo read));
            Assert.Equal(enabled, read.ResponsiveSmoke);
            Assert.True(read.Practice && read.Dedicated);
        }
    }

    [Fact]
    public void ResponsiveSmokeSetupFitsTheControlMessageBudget()
    {
        var setup = new LobbySetup
        {
            Map = "TestRange", MaxPlayers = 24, TimeLimit = 20, ScoreLimit = 100, SpyLives = 10, MercLives = 10,
            RespawnSeconds = 15, GamesInARow = 10, Disks = 30, DisksToWin = 30, DisksMax = 30,
            BotsPerSide = 12, SpyBots = 12, MercBots = 12, ResponsiveSmoke = true,
        };
        string line = SessionProtocol.Line("setup", "ffffffffffffffff", setup.ToOptions());
        Assert.True(Encoding.UTF8.GetByteCount(line) <= ControlMessage.MaxTextBytes, line);
    }
}
