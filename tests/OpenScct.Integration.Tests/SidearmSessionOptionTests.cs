using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Equipment;
using OpenScct.Game.SBase;
using OpenScct.Server;
using OpenScct.Server.Online;
using OpenScctGame.Play.Session;
using Xunit;

namespace OpenScct.Integration.Tests;

public sealed class SidearmSessionOptionTests
{
    [Fact]
    public void DefaultsOffAndRoundTripsThroughLobbyAndLaunchOptions()
    {
        Assert.False(new LobbySetup().Sidearms);
        Assert.False(LobbySetup.FromOptions("?MapName=ClubE").Sidearms);
        var on = new LobbySetup { Sidearms = true };
        Assert.True(LobbySetup.FromOptions(on.ToOptions()).Sidearms);
        Assert.Contains("?SvmSidearms=1", on.ToGameOptions(2));
        Assert.Contains("?SvmSidearms=0", new LobbySetup().ToGameOptions(2));
        Assert.False(LobbySetup.FromOptions("?SvmSidearms=0", on).Sidearms);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HostRuleReplicatesAndControlsSidearmGrants(bool enabled)
    {
        Assert.SkipWhen(MapCatalog.CreateDefault().FindFolder("TestRange") is null, "TestRange content required.");
        var clock = new StepClock();
        using var session = new PlaySession(new PlaySettings
        {
            Map = "TestRange", Side = PlaySide.Merc, Rules = PlayRules.PlayLevel,
            HostMode = PlayHostMode.Inline, Sidearms = enabled,
        }, clock);
        void Tick(int n = 1)
        {
            for (int i = 0; i < n; i++)
            {
                clock.NowMicroseconds += 1_000_000 / Sim.Hz;
                session.Step(clock.NowMicroseconds);
                session.Input.EndTick();
            }
        }
        for (int i = 0; i < 1200 && (session.LocalPawn is null || session.ServerController?.Pawn is null ||
            session.LocalController!.IsInState("WaitingForPawn")); i++) Tick();
        Tick(90);
        var pawn = Assert.IsAssignableFrom<SBasePawn>(session.ServerController!.Pawn);
        Assert.Equal(enabled, session.ServerController.GameReplicationInfo!.SidearmsEnabled);
        Assert.Equal(enabled, session.LocalController!.GameReplicationInfo!.SidearmsEnabled);
        Loadouts.OpenGrant(pawn, EGameMode.GM_Multi);
        // Even an unrestricted player's explicit request is rejected when disabled.
        pawn.PlayerReplicationInfo!.SvmSidearmsUnrestricted = true;
        Loadouts.ServerSpawnInventory(pawn, new OpenScct.Core.Uc.ClassRef(Sidearms.Get(SidearmKind.Unsuppressed).CarryId));
        Tick(90);
        Assert.Equal(enabled, pawn.SidearmWeapon is not null);
        Assert.Equal(enabled, session.LocalPawn!.SidearmWeapon is not null);
    }
}
