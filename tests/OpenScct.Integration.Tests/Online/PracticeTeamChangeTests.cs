using System.Linq;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Server;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Net.Discovery;
using OpenScct.Server.Online;
using Xunit;

namespace OpenScct.Integration.Tests.Online;

public sealed class PracticeTeamChangeTests
{
    [Theory]
    [InlineData(true, 1)]
    [InlineData(true, 2)]
    [InlineData(true, 10)]
    [InlineData(false, 2)]
    public void TeamRequestsDuringMatchOnlyWorkInPractice(bool practice, int visionKey)
    {
        Assert.SkipWhen(WalkerRig.Maps.FindFolder(MapCatalog.TestBox) is null, "TestBox content required.");
        var settings = OnlineRig.Settings(MapCatalog.TestBox) with { AutoLaunch = false, Dedicated = false, HostKey = "k" };
        settings = settings with { Setup = settings.Setup with { Practice = practice, AutoBalance = true } };
        var clock = new VirtualClock();
        var network = new SimLinkNetwork(clock, 853, SimLinkProfile.A);
        using var rig = new OnlineRig(new OnlineServer(network.Bind("server"), settings, 0), clock);
        OnlineClient Join(string name, int team, bool host) => rig.Add(new OnlineClient(
            JoinTarget.Custom("Test", () => network.Bind(name), "server"),
            OnlineRig.ClientSettings(name, team) with { HostKey = host ? "k" : null }, rig.Now));
        var host = Join("Host", 0, true);
        var guest = Join("Guest", 1, false);
        Assert.True(rig.RunUntil(() => rig.Clients.All(c => c.State == OnlineClientState.Lobby && c.Lobby.Players.Count == 2), 10));
        guest.SetReady(true);
        Assert.True(rig.RunUntil(() => host.Lobby.Players.Single(p => p.Name == "Guest").Ready, 10));
        host.Launch();
        Assert.True(rig.RunUntil(() => rig.Clients.All(c => c.State == OnlineClientState.Match && c.Session?.LocalPlayer?.Pawn is not null), 20));
        foreach (var client in new[] { host, guest })
        {
            var pc = (SPlayerController)rig.Server.Host.Players.Single(p => p.Controller?.PlayerReplicationInfo?.PlayerName == client.Lobby.Local!.Name).Controller!;
            int original = SGameInfo.TeamIndexOf(pc);
            var old = pc.Pawn;
            var owner = Assert.IsType<SPlayerController>(client.Session!.LocalPlayer);
            var goggles = Assert.IsType<SGoggle>(owner.EnsureGoggle());
            int vision = original == 0
                ? visionKey == 1 ? SGoggle.REN_NightVision : visionKey == 10 ? SGoggle.REN_Binoculars : SGoggle.REN_ThermalVision
                : visionKey == 1 ? SGoggle.REN_MotionTrackingVision : SGoggle.REN_EMFVision;
            if (owner.Pawn is SBasePawn before) before.bBinocularsOn = vision == SGoggle.REN_Binoculars;
            goggles.CurrentMode = vision;
            goggles.SaveMode = vision;
            goggles.Activate();
            Assert.Equal(vision, owner.RendMap);
            client.SetTeam(1 - original);
            if (practice)
            {
                Assert.True(rig.RunUntil(() => SGameInfo.TeamIndexOf(pc) == 1 - original && pc.Pawn is { Health: > 0 } &&
                    !ReferenceEquals(old, pc.Pawn) && client.Session?.LocalPlayer?.Pawn is { bDeleteMe: false } pawn &&
                    pawn.GetType() == pc.Pawn.GetType(), 10));
                Assert.True(rig.RunUntil(() => goggles.CurrentMode == SGoggle.REN_DynLight && owner.RendMap == SGoggle.REN_DynLight, 5));
                Assert.Equal(SGoggle.REN_DynLight, goggles.SaveMode);
                Assert.True(goggles.IsInState(new Name("GoggleUp")));
                Assert.Same(owner.Pawn, goggles.Base);
                Assert.False(Assert.IsAssignableFrom<SBasePawn>(owner.Pawn).bBinocularsOn);
                if (goggles.MotionTrackingVision is { } tracker)
                    Assert.True(tracker.IsInState(new Name("s_Inactive")));
                Assert.Equal(1 - original, client.Lobby.Local!.Team);
                Assert.Equal(original == 0 ? PawnType.SPAWNDEFENSE : PawnType.SPAWNOFFENSE, pc.SPawnType);
            }
            else
            {
                rig.RunUntil(() => false, 2);
                Assert.Equal(original, SGameInfo.TeamIndexOf(pc));
                Assert.Same(old, pc.Pawn);
                Assert.Equal(vision, goggles.CurrentMode);
                Assert.Equal(vision, owner.RendMap);
            }
            Assert.Equal(OnlineClientState.Match, client.State);
            Assert.False(rig.Server.Host.Game!.bGameEnded);
        }
    }
}
