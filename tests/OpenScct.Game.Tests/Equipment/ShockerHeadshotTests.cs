using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Combat;
using OpenScct.Game.SBase;
using OpenScct.Game.Tests.Combat;
using Xunit;

namespace OpenScct.Game.Tests.Equipment;

public sealed class ShockerHeadshotTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void FriendlySpyShotStunsWithoutDamageOrMercHudShutdown(int pill)
    {
        var scene = new CombatScene(ENetMode.NM_ListenServer);
        var (victimPc, victim) = scene.Player(true, new FVector(200f, 0f, 100f));
        var (shooterPc, shooter) = scene.Player(true, new FVector(0f, 0f, 100f));
        var team = scene.Level.Spawn<TeamInfo>()!;
        victim.PlayerReplicationInfo!.Team = team;
        shooter.PlayerReplicationInfo!.Team = team;
        Assert.Equal(SGameInfo.TeamIndexOf(victimPc), SGameInfo.TeamIndexOf(shooterPc));
        victimPc.TimeShock = -100f;
        scene.Level.SetService<IHitZones>(new HitPawn(victim, pill));
        SStickyShocker pistol = scene.Give(new SStickyShocker(), shooter);
        pistol.GiveTo(shooter);
        int health = victim.Health;

        pistol.TraceFireWithPos(shooter.Location, default, 0f, 0f, 0f);

        Assert.Equal(health, victim.Health);
        Assert.True(victim.bStickyShocking);
        Assert.Equal(1, victimPc.iDontUpdatePadInput);
        Assert.Equal(3, victimPc.iTimeToStopStickyShock);
        Assert.Equal(3, victimPc.iTimeElecStickyShock);
        Assert.Equal(11f, victimPc.RealTimeInvulSticky);
        Assert.True(MercShockImmunity.TimerBlocks(victimPc));
        Assert.False(MercShockImmunity.Capture(victim).Active);
        Assert.DoesNotContain(scene.World.Events.Current.ToArray(), e => e.What == CombatFx.HudShutDown);
    }

    private sealed class HitPawn(SBasePawn victim, int pill) : IHitZones
    {
        public BoneTraceResult TraceBone(Actor shooter, FVector end, FVector start, bool realFire) => new()
        {
            bHit = true, Actor = victim, PillTag = pill,
            HitLocation = victim.Location, HitNormal = new FVector(-1f, 0f, 0f),
        };
    }

    // Original SPlayerController.ClientStickyShock and SPAWNDEFENSE defaults:
    // head shots add HUD shutdown, not a separate movement-freeze multiplier.
    [Theory]
    [InlineData(1, false, 3.5f, 4.5f, 12.5f)]
    [InlineData(2, false, 3.5f, 4.5f, 12.5f)]
    [InlineData(1, true, 4.55f, 5.55f, 13.55f)]
    [InlineData(2, true, 4.55f, 5.55f, 13.55f)]
    public void ShotPreservesOriginalHeadHudEffectAndAuthoritativeImmunity(
        int pill, bool wet, float freeze, float electric, float immunity)
    {
        var scene = new CombatScene(ENetMode.NM_ListenServer);
        var (pc, merc) = scene.Player(false, new FVector(200f, 0f, 100f));
        var (_, spy) = scene.Player(true, new FVector(0f, 0f, 100f));
        var hud = scene.Level.Spawn<SHud>()!;
        pc.myHUD = hud;
        pc.TimeShock = -100f;
        merc.bWet = wet;
        scene.Level.SetService<IHitZones>(new HitPawn(merc, pill));
        SStickyShocker pistol = scene.Give(new SStickyShocker(), spy);
        pistol.GiveTo(spy);

        pistol.TraceFireWithPos(spy.Location, default, 0f, 0f, 0f);

        Assert.Equal(pill == 1, merc.bStickyShockedInTheHead);
        Assert.True(merc.bStickyShocking);
        Assert.Equal(1, pc.iDontUpdatePadInput);
        Assert.Equal((int)freeze, pc.iTimeToStopStickyShock);
        Assert.Equal((int)electric, pc.iTimeElecStickyShock);
        Assert.Equal(immunity, pc.RealTimeInvulSticky, 3);
        ShockImmunityView view = MercShockImmunity.Capture(merc).At(scene.Time);
        Assert.True(view.Active);
        Assert.Equal(immunity, view.RemainingSeconds, 3);
        Assert.Equal(1f, view.Fraction);
        Assert.Equal(pill == 1, hud.bShutDown);
        if (pill == 1)
        {
            Assert.Equal(1f, hud.TimeToCloseShut);
            Assert.Equal(freeze + 9f, hud.HudShutDownLength, 3);
            Assert.Contains(scene.World.Events.Current.ToArray(), e =>
                e.What == CombatFx.HudShutDown && e.ActorId == pc.ActorId);
        }
        else
        {
            Assert.DoesNotContain(scene.World.Events.Current.ToArray(), e => e.What == CombatFx.HudShutDown);
        }
    }
}
