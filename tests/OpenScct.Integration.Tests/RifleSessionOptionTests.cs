using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Equipment;
using OpenScct.Game.SBase;
using OpenScct.Server;
using OpenScct.Server.Online;
using OpenScctGame.Play.Session;
using Xunit;

namespace OpenScct.Integration.Tests;

public sealed class RifleSessionOptionTests
{
    [Fact]
    public void DefaultsOnAndPreservesAnExplicitServerRule()
    {
        Assert.True(new LobbySetup().Rifles);
        Assert.True(new PlaySettings().Rifles);
        Assert.True(LobbySetup.FromOptions("?MapName=ClubE").Rifles);
        var on = new LobbySetup { Rifles = true };
        Assert.True(LobbySetup.FromOptions(on.ToOptions()).Rifles);
        Assert.Contains("?SvmRifles=1", on.ToGameOptions(2));
        Assert.Contains("?SvmRifles=0", new LobbySetup { Rifles = false }.ToGameOptions(2));
        Assert.False(LobbySetup.FromOptions("?SvmRifles=0", on).Rifles);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HostRuleReplicatesAndControlsRifleGrants(bool enabled)
    {
        Assert.SkipWhen(MapCatalog.CreateDefault().FindFolder(MapCatalog.TestBox) is null, "TestBox content required.");
        var clock = new StepClock();
        using var session = new PlaySession(new PlaySettings
        {
            Map = MapCatalog.TestBox, Side = PlaySide.Merc, Rules = PlayRules.PlayLevel,
            HostMode = PlayHostMode.Inline, Rifles = enabled,
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
        Assert.Equal(enabled, session.ServerController.GameReplicationInfo!.RiflesEnabled);
        Assert.Equal(enabled, session.LocalController!.GameReplicationInfo!.RiflesEnabled);
        PawnInventory.DeleteAllInventory(pawn);
        Loadouts.OpenGrant(pawn, EGameMode.GM_Multi);
        // Even an unrestricted player's explicit request is rejected when disabled.
        pawn.PlayerReplicationInfo!.SvmSidearmsUnrestricted = true;
        Loadouts.ServerSpawnWeapon(pawn, new OpenScct.Core.Uc.ClassRef("Svm.Galil"));
        Tick(90);
        Assert.Equal(enabled, pawn.MyWeapon is OpenScct.Game.Svm.Galil);
        Assert.Equal(enabled, session.LocalPawn!.MyWeapon is OpenScct.Game.Svm.Galil);
    }
}
