using System.Linq;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Net.Discovery;
using OpenScct.Server.Online;
using Xunit;

namespace OpenScct.Integration.Tests.Online;

public sealed class PracticeMineDeathTests
{
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public void ShootingYourOwnMineInAquaPracticeRespawnsWithoutReturningToLobby(bool victimIsHost, bool bothMercs)
        => CheckMineDeath("AquaE", victimIsHost, bothMercs, solo: false);

    [Fact]
    public void SoloAquaDPracticeHostRespawnsAfterShootingOwnMine()
        => CheckMineDeath("AquaD", victimIsHost: true, bothMercs: false, solo: true);

    private static void CheckMineDeath(string map, bool victimIsHost, bool bothMercs, bool solo)
    {
        Assert.SkipWhen(WalkerRig.Maps.FindFolder(map) is null, $"{map} content is required.");
        var settings = OnlineRig.Settings(map) with { AutoLaunch = false, Dedicated = false, HostKey = "practice-test" };
        settings = settings with { Setup = settings.Setup with { Practice = true } };
        var clock = new VirtualClock();
        var network = new SimLinkNetwork(clock, 852, SimLinkProfile.A);
        using var rig = new OnlineRig(new OnlineServer(network.Bind("server"), settings, 0), clock);
        OnlineClient Join(string name, int team, bool host) => rig.Add(new OnlineClient(
            JoinTarget.Custom("Practice", () => network.Bind(name), "server"),
            OnlineRig.ClientSettings(name, team) with { HostKey = host ? "practice-test" : null }, rig.Now));
        var merc = Join("Merc", 1, victimIsHost);
        var other = solo ? null : Join("Other", bothMercs ? 1 : 0, !victimIsHost);
        Assert.True(rig.RunUntil(() => rig.Clients.All(c => c.State == OnlineClientState.Lobby && c.Lobby.Players.Count == (solo ? 1 : 2)), 10));
        foreach (var client in rig.Clients) client.SetReady(true);
        Assert.True(rig.RunUntil(() => merc.Lobby.Players.All(p => p.Ready), 10));
        (victimIsHost ? merc : other!).Launch();
        Assert.True(rig.RunUntil(() => rig.Clients.All(c => c.State == OnlineClientState.Match && c.Session?.LocalPlayer?.Pawn is not null), 20));
        rig.RunUntil(() => false, 15); // Spawn protection expires before the blast.
        var pc = (SPlayerController)rig.Server.Host.Players.Single(p => p.Controller?.PlayerReplicationInfo?.PlayerName == "Merc").Controller!;
        var body = Assert.IsAssignableFrom<SBasePawn>(pc.Pawn);
        body.Health = 10;
        var mine = rig.Server.Host.World.Level.SpawnActor(new SMineProx { bNoDelete = false, PcOwner = pc },
            body.Location + new FVector(45, 0, 0), default, body)!;
        mine.eventTakeDamage(1, body, mine.Location, default, SDamageType.SDTBase);
        Assert.True(body.Health <= 0, "Shooting the nearby mine must kill its injured owner.");
        Assert.True(rig.RunUntil(() => pc.Pawn is { Health: > 0 } replacement && !ReferenceEquals(body, replacement), 15));
        Assert.False(rig.Server.Host.Game!.bGameEnded);
        Assert.Equal(OnlineClientState.Match, merc.State);
        Assert.False(merc.Kicked);
    }
}
