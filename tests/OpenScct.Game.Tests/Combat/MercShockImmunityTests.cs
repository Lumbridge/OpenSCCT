using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Combat;
using OpenScct.Game.Modes;
using Xunit;

namespace OpenScct.Game.Tests.Combat;

public sealed class MercShockImmunityTests
{
    [Theory]
    [InlineData(false, 12.5f)]
    [InlineData(true, 13.55f)]
    public void TimerMatchesAcceptedShockAndOutlastsTheElectricEffect(bool wet, float duration)
    {
        var scene = new CombatScene();
        var (pc, pawn) = scene.Player(false, default);
        pc.TimeShock = -100f;
        pawn.bWet = wet;
        pc.Checkshock(2);
        ShockImmunityState status = MercShockImmunity.Capture(pawn);
        Assert.True(status.TimerBlocked);
        Assert.Equal(duration, status.Duration, 3);
        duration = pc.RealTimeInvulSticky;
        pawn.bStickyShocking = pawn.bStickySkinned = false;
        Assert.True(MercShockImmunity.Capture(pawn).Active);
        using (scene.Level.CommandTime(1000, duration, Sim.DeltaTime))
        {
            Assert.True(MercShockImmunity.TimerBlocks(pc));
            Assert.Equal(0f, MercShockImmunity.Capture(pawn).At(duration).RemainingSeconds);
            float before = pc.TimeShock;
            pc.Checkshock(2);
            Assert.Equal(before, pc.TimeShock);
        }
        using (scene.Level.CommandTime(1001, duration + 0.01f, Sim.DeltaTime))
        {
            Assert.False(MercShockImmunity.Capture(pawn).Active);
            pc.Checkshock(2);
            Assert.Equal(scene.Time, pc.TimeShock);
            Assert.True(MercShockImmunity.Capture(pawn).Active);
        }
    }

    [Theory]
    [InlineData("s_Blast", true)]
    [InlineData("s_BlastFallAfterObstacle", true)]
    [InlineData("s_FallDown", true)]
    [InlineData("s_BlastFloor", true)]
    [InlineData("s_GrabbedByPlayer_Begin", true)]
    [InlineData("s_GrabbedByPlayer_Released_KO", true)]
    [InlineData("s_GrabbedByPlayer", false)]
    [InlineData("s_GrabbedFromLedge_Begin", false)]
    [InlineData("s_PlayerCharging", false)]
    [InlineData("s_Berserk360", false)]
    [InlineData("s_Binoculars_Def", false)]
    [InlineData("s_UseObject_CamNetwork", false)]
    public void CueAgreesWithWhetherTheActualDamageHandlerAcceptsAnOrdinaryShock(string state, bool blocked)
    {
        var scene = new CombatScene(ENetMode.NM_DedicatedServer);
        var (pc, pawn) = scene.Player(false, default);
        pc.GotoState(new Name(state));
        pc.TimeShock = -100f;
        pc.RealTimeInvulSticky = 5f;
        ShockImmunityState status = MercShockImmunity.Capture(pawn);
        Assert.Equal(blocked, status.StateProtected);
        Assert.Equal(blocked, status.Active);
        pc.PawnTakeDamage(0, null, pawn.Location, default, SDamageType.SDTStickyShocker, 2);
        Assert.Equal(blocked ? -100f : scene.Time, pc.TimeShock);
    }

    [Fact]
    public void StateProtectionCanOverlapAndOutlastTheTimerWithoutInventingATime()
    {
        var scene = new CombatScene();
        var (pc, pawn) = scene.Player(false, default);
        var practice = new PracticeRules { On = true };
        scene.Level.SetService(practice);
        practice.SetTank(pc, true);
        pc.TimeShock = 0f; pc.RealTimeInvulSticky = 5f;
        Assert.True(MercShockImmunity.Capture(pawn).TimerBlocked);
        Assert.True(MercShockImmunity.Capture(pawn).StateProtected);
        using (scene.Level.CommandTime(600, 10f, Sim.DeltaTime))
        {
            ShockImmunityState status = MercShockImmunity.Capture(pawn);
            Assert.True(status.Active);
            Assert.False(status.TimerBlocked);
            Assert.Equal(0f, status.At(10f).RemainingSeconds);
            practice.SetTank(pc, false);
            Assert.False(MercShockImmunity.Capture(pawn).Active);
        }
    }

    [Fact]
    public void PlayLevelShowsProtectionBeforeTheMatchFlagButWaitingPlayersDoNot()
    {
        var scene = new CombatScene();
        var (pc, pawn) = scene.Player(false, default);
        pc.GameReplicationInfo = scene.Level.Spawn<GameReplicationInfo>()!;
        Assert.False(pc.GameReplicationInfo.bMatchHasBegun);
        pc.TimeShock = -100f;
        pc.Checkshock(2);
        Assert.True(MercShockImmunity.Capture(pawn).TimerBlocked);
        pc.GotoState(new Name("s_PlayerWaiting"));
        Assert.False(MercShockImmunity.Capture(pawn).Active);
    }

    [Fact]
    public void TimerResetUpdatesTheOriginalDurationAndVisibilityGatesHideNonGameplayPawns()
    {
        var scene = new CombatScene();
        var (pc, pawn) = scene.Player(false, default);
        pc.RealTimeInvulSticky = 12.5f;
        using var time = scene.Level.CommandTime(6000, 100f, Sim.DeltaTime);
        pc.TimeShock = 92f;
        Assert.Equal(4.5f, MercShockImmunity.Capture(pawn).At(100f).RemainingSeconds);
        pc.TimeShock = 100f;
        Assert.Equal(1f, MercShockImmunity.Capture(pawn).At(100f).Fraction);
        pawn.Health = 0;
        Assert.False(MercShockImmunity.Capture(pawn).Active);
        pawn.Health = 100;
        scene.Level.SetService(PlayRules.Hub);
        Assert.False(MercShockImmunity.Capture(pawn).Active);
        scene.Level.SetService(PlayRules.Original);
        pawn.bHidden = true;
        Assert.False(MercShockImmunity.Capture(pawn).Active);
    }
}
