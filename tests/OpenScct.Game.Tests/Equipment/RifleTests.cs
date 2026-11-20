using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Game.Equipment;
using OpenScct.Game.Svm;
using OpenScct.Game.Tests.Combat;
using Xunit;

namespace OpenScct.Game.Tests.Equipment;

/// <summary>The merc rifle's trigger, rate of fire, clip and reload (spec/equipment/merc-rifle.md).</summary>
public sealed class RifleTests
{
    private static readonly Name Selected = new("s_Selected");
    private static readonly Name Firing = new("s_Firing");
    private static readonly Name Reloading = new("s_Reloading");
    private static readonly Name FullReloading = new("s_FullReloading");

    private static (CombatScene Scene, SPlayerController PC, SWeaponDefense Rifle) Merc(SWeaponDefense? weapon = null)
    {
        var scene = new CombatScene();
        (SPlayerController pc, SBasePawn merc) = scene.Player(false, new FVector(0f, 0f, 100f));
        SWeaponDefense rifle = scene.Give(weapon ?? new SWeaponDefense(), merc);
        rifle.GiveTo(merc);
        merc.WeaponLoaded = 2;
        scene.Run(0.1f);
        return (scene, pc, rifle);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void LandingDipSurvivesWeaponTicksAndRecoversOnlyAfterTheClip(bool high, bool moving)
    {
        (CombatScene scene, SPlayerController pc, SWeaponDefense rifle) = Merc();
        pc.Pawn!.SetPhysics(EPhysics.PHYS_None);
        pc.Pawn.Velocity = moving ? new FVector(100f, 0f, 0f) : FVector.Zero;
        rifle.AnimateFall();
        if (high) rifle.AnimateLandHigh(); else rifle.AnimateLand();
        Name landing = high ? ViewModelClips.FirstLandStHi2 : ViewModelClips.FirstLandStFd2;
        float seconds = 0.2f + (high ? 20f / 15f : 10f / 15f);
        Assert.Equal(seconds, rifle.ViewAnim.Remaining(SWeaponDefense.MVTCHANNEL), 5);
        Assert.Equal(0.2f, rifle.ViewAnim.TweenTime(SWeaponDefense.MVTCHANNEL));

        scene.Run(0.1f);
        Assert.Equal(landing, rifle.ViewAnim.Sequence(SWeaponDefense.MVTCHANNEL));
        Assert.True(rifle.ViewAnim.IsAnimating(SWeaponDefense.MVTCHANNEL));
        Assert.InRange(rifle.ViewAnim.Remaining(SWeaponDefense.MVTCHANNEL), seconds - 0.12f, seconds - 0.08f);
        Assert.Equal(0f, rifle.ViewAnim.Elapsed(SWeaponDefense.MVTCHANNEL, ViewModelClips.DataOf(landing)));
        scene.Run(seconds - 0.2f);
        Assert.Equal(landing, rifle.ViewAnim.Sequence(SWeaponDefense.MVTCHANNEL));
        scene.Run(0.2f);
        Assert.Equal(moving ? ViewModelClips.FirstWalkStFd2 : ViewModelClips.FirstWaitStFd2,
            rifle.ViewAnim.Sequence(SWeaponDefense.MVTCHANNEL));
    }

    [Fact]
    public void TheRifleStartsIdleWithAFullClip()
    {
        (_, _, SWeaponDefense rifle) = Merc();
        Assert.Equal(Selected, rifle.GetStateName());
        Assert.Equal(20, rifle.ClipAmmo);
        Assert.Equal(120, rifle.Ammo);
        Assert.Equal(SRateOfFireMode.ROF_Auto, rifle.ROFMode);
    }

    private sealed class CountingRifle : SWeaponDefense
    {
        public int Fired;
        public override void FireWeapon() { base.FireWeapon(); Fired++; }
    }

    [Theory]
    [InlineData(SRateOfFireMode.ROF_Auto)]
    [InlineData(SRateOfFireMode.ROF_Burst)]
    [InlineData(SRateOfFireMode.ROF_Single)]
    public void InfiniteAmmoKeepsAutoFiringButPreservesBurstAndSingle(SRateOfFireMode mode)
    {
        var counter = new CountingRifle();
        (CombatScene scene, SPlayerController pc, SWeaponDefense rifle) = Merc(counter);
        rifle.XLevel.SetService(new OpenScct.Game.Modes.PracticeRules { On = true });
        rifle.ROFMode = mode;
        int clip = rifle.ClipAmmo, ammo = rifle.Ammo, shots = counter.Fired;
        pc.bFire = 1;
        rifle.eventFire(1f);
        scene.Run(rifle.RateOfFire * (clip * 2 + 2));
        Assert.Equal(clip, rifle.ClipAmmo);
        Assert.Equal(ammo, rifle.Ammo);
        if (mode == SRateOfFireMode.ROF_Auto)
        {
            Assert.True(counter.Fired - shots > clip * 2);
            Assert.Equal(Firing, rifle.GetStateName());
            pc.bFire = 0;
            scene.Run(rifle.RateOfFire + .1f);
            Assert.Equal(Selected, rifle.GetStateName());
        }
        else
        {
            Assert.Equal(mode == SRateOfFireMode.ROF_Burst ? 3 : 1, counter.Fired - shots);
            Assert.Equal(Selected, rifle.GetStateName());
        }
    }

    [Fact]
    public void AutoFireShootsEveryRateOfFireWhileHeldThenStops()
    {
        (CombatScene scene, SPlayerController pc, SWeaponDefense rifle) = Merc();
        pc.bFire = 1;
        rifle.eventFire(1f);
        Assert.Equal(Firing, rifle.GetStateName());
        Assert.Equal(19, rifle.ClipAmmo);
        Assert.Equal(119, rifle.Ammo);
        scene.Run(0.45f);
        Assert.Equal(17, rifle.ClipAmmo);
        pc.bFire = 0;
        scene.Run(0.25f);
        Assert.Equal(Selected, rifle.GetStateName());
        Assert.Equal(17, rifle.ClipAmmo);
    }

    [Fact]
    public void M4KeepsFullAutoOnModeInputAndRemoteModeRequests()
    {
        (CombatScene scene, SPlayerController pc, SWeaponDefense rifle) = Merc(new M4A1S());
        rifle.SwitchROF();
        rifle.ServerSetROF(SRateOfFireMode.ROF_Burst);
        rifle.ServerSetROF(SRateOfFireMode.ROF_Single);
        Assert.Equal(SRateOfFireMode.ROF_Auto, rifle.ROFMode);
        Assert.Equal("Full Auto", rifle.GetROFName());
        pc.bFire = 1;
        rifle.eventFire(1f);
        pc.bFire = 0;
        scene.Run(.2f);
        Assert.Equal(19, rifle.ClipAmmo);
    }

    [Theory]
    [InlineData(typeof(M4A1S), false)]
    [InlineData(typeof(M4A1S), true)]
    [InlineData(typeof(Famas), false)]
    [InlineData(typeof(Famas), true)]
    [InlineData(typeof(Galil), false)]
    [InlineData(typeof(Galil), true)]
    public void RecoilRaisesTheNextShotAndRetainsAnOpposingLookOnRelease(System.Type type, bool counter)
    {
        var (scene, pc, rifle) = Merc((SWeaponDefense)System.Activator.CreateInstance(type)!);
        if (rifle.ROFMode == SRateOfFireMode.ROF_Burst) rifle.SwitchROF();
        pc.bFire = 1;
        rifle.eventFire(1f);
        scene.World.Step(10);
        int raised = pc.LocalMyDesiredRotation.Pitch;
        Assert.InRange(raised, 2 * 24, 2 * 39);
        Assert.InRange(rifle.AccurateRotation.Pitch, 24, 39);
        if (counter)
        {
            var look = pc.LocalMyDesiredRotation;
            look.Pitch -= raised + 20;
            pc.Rotation = pc.MyDesiredRotation = pc.LocalMyDesiredRotation = look;
        }
        pc.bFire = 0;
        scene.Run(1f);
        Assert.Equal(counter ? -20 : 0, pc.LocalMyDesiredRotation.Pitch);
        Assert.Equal(0, pc.LocalMyDesiredRotation.Yaw);
    }

    [Theory]
    [InlineData(typeof(M4A1S), false)]
    [InlineData(typeof(Famas), false)]
    [InlineData(typeof(Galil), false)]
    [InlineData(typeof(M4A1S), true)]
    [InlineData(typeof(Famas), true)]
    [InlineData(typeof(Galil), true)]
    public void RifleRecoilRecoversAcrossReloadAndWeaponSwap(System.Type type, bool swap)
    {
        var (scene, pc, rifle) = Merc((SWeaponDefense)System.Activator.CreateInstance(type)!);
        var pawn = pc.SBPawn!;
        if (rifle.ROFMode == SRateOfFireMode.ROF_Burst) rifle.SwitchROF();
        SidearmEquipment.Give(pawn, SidearmKind.Unsuppressed);
        pc.GotoState(new Name("s_PlayerWalkingSt2H"));
        pc.bFire = 1;
        pc.eventFire(1f);
        scene.World.Step(3);
        Assert.True(pc.LocalMyDesiredRotation.Pitch > 0);
        pc.bFire = 0;
        if (swap)
        {
            pc.SwapWeapon();
            Assert.Same(pawn.SidearmWeapon, pawn.MyWeapon);
        }
        else
        {
            scene.World.Step(4);
            Assert.True(rifle.eventReload());
            Assert.Equal(Reloading, rifle.GetStateName());
        }
        scene.Run(3.5f);
        Assert.Equal(0, pc.LocalMyDesiredRotation.Pitch);
        Assert.Equal(0, pc.LocalMyDesiredRotation.Yaw);
        if (swap)
        {
            pc.SwapWeapon();
            scene.Run(.6f);
        }
        Assert.Same(rifle, pawn.MyWeapon);
        Assert.Equal(swap ? 19 : 20, rifle.ClipAmmo);
        pc.bFire = 1;
        pc.eventFire(1f);
        scene.World.Step(4);
        Assert.InRange(pc.LocalMyDesiredRotation.Pitch, 24, 39);
    }

    [Fact]
    public void ReleasedFamasTriggerKeepsRecoilUntilBothQueuedShotsFire()
    {
        var (scene, pc, rifle) = Merc(new Famas());
        Assert.Equal(SRateOfFireMode.ROF_Burst, rifle.ROFMode);
        pc.bFire = 1;
        rifle.eventFire(1f);
        Assert.Equal(19, rifle.ClipAmmo);
        pc.bFire = 0;
        int previousPitch = 0;
        for (int tick = 0; tick < 12 && rifle.ClipAmmo > 17; tick++)
        {
            scene.World.Step();
            int currentPitch = pc.LocalMyDesiredRotation.Pitch;
            Assert.True(currentPitch >= previousPitch, $"Queued burst recovered early on tick {tick + 1}");
            previousPitch = currentPitch;
        }
        Assert.Equal(17, rifle.ClipAmmo);
        Assert.InRange(rifle.AccurateRotation.Pitch, 48, 78);
        scene.Run(1f);
        Assert.Equal(17, rifle.ClipAmmo);
        Assert.Equal(Selected, rifle.GetStateName());
        Assert.Equal(0, pc.LocalMyDesiredRotation.Pitch);
        Assert.Equal(0, pc.LocalMyDesiredRotation.Yaw);
    }

    [Theory]
    [InlineData(false, .075f, 3)]
    [InlineData(true, .09f, 5)]
    public void FamasKeepsItsBurstAndAutomaticCadences(bool automatic, float interval, int shots)
    {
        var (scene, pc, rifle) = Merc(new Famas());
        Assert.Equal(SRateOfFireMode.ROF_Burst, rifle.ROFMode);
        if (automatic) rifle.SwitchROF();
        Assert.Equal(automatic ? SRateOfFireMode.ROF_Auto : SRateOfFireMode.ROF_Burst, rifle.ROFMode);
        Assert.Equal(interval, rifle.ShotInterval, 5);
        pc.bFire = 1;
        rifle.eventFire(1f);
        Assert.Equal(19, rifle.ClipAmmo);
        int clip = rifle.ClipAmmo;
        for (int tick = 1; tick <= 24; tick++)
        {
            scene.World.Step();
            if (clip == rifle.ClipAmmo) continue;
            Assert.Equal(--clip, rifle.ClipAmmo);
            float elapsed = tick * Sim.DeltaTime;
            Assert.InRange(elapsed - (19 - clip) * interval, -.00001f, Sim.DeltaTime + .00001f);
        }
        Assert.Equal(20 - shots, rifle.ClipAmmo);
        pc.bFire = 0;
        scene.Run(.6f);
        Assert.Equal(Selected, rifle.GetStateName());
        rifle.SwitchROF();
        Assert.Equal(automatic ? SRateOfFireMode.ROF_Burst : SRateOfFireMode.ROF_Auto, rifle.ROFMode);
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(WeaponAttachments.ExtendedMagazine, 30)]
    public void M4KeepsItsNinetyMillisecondScheduleAcrossTheMagazine(int attachments, int magazine)
    {
        (CombatScene scene, SPlayerController pc, SWeaponDefense rifle) = Merc(new M4A1S());
        WeaponAttachments.Apply(rifle, attachments);
        Assert.Equal(magazine, rifle.ClipAmmo);
        Assert.Equal(magazine, rifle.ClipMaxAmmo);
        Assert.Equal(80, rifle.Ammo - rifle.ClipAmmo);
        Assert.Equal(14, rifle.BaseDamage);
        Assert.Equal(18, WeaponRules.DistanceDamage(rifle, 0));
        pc.bFire = 1;
        rifle.eventFire(1f);
        int lastClip = rifle.ClipAmmo;
        Assert.Equal(magazine - 1, lastClip);
        int ticksToLastShot = (int)System.MathF.Ceiling((magazine - 1) * .09f * Sim.Hz);
        for (int tick = 1; tick <= ticksToLastShot + 1; tick++)
        {
            scene.World.Step(1);
            if (rifle.ClipAmmo == lastClip) continue;
            Assert.Equal(lastClip - 1, rifle.ClipAmmo);
            // Each shot arrives within one 60-Hz tick of its 90-ms deadline, including exact tick boundaries.
            int repeat = magazine - 1 - rifle.ClipAmmo;
            Assert.InRange(tick * Sim.DeltaTime - repeat * .09f, -.00001f, Sim.DeltaTime + .00001f);
            lastClip = rifle.ClipAmmo;
        }
        Assert.Equal(0, lastClip);
        pc.bFire = 0;
        scene.Run(rifle.RateOfFire + .2f);
        scene.Run(((M4A1S)rifle).Definition.ReloadDuration(0) + .2f);
        Assert.Equal(magazine, rifle.ClipAmmo);
        Assert.Equal(80 - magazine, rifle.Ammo - rifle.ClipAmmo);
    }

    [Fact]
    public void BurstFiresThreeShotsAndNeedsANewPress()
    {
        (CombatScene scene, SPlayerController pc, SWeaponDefense rifle) = Merc();
        rifle.SwitchROF();
        Assert.Equal(SRateOfFireMode.ROF_Burst, rifle.ROFMode);
        pc.bFire = 1;
        rifle.eventFire(1f);
        scene.Run(1f);
        Assert.Equal(17, rifle.ClipAmmo);
        Assert.Equal(Selected, rifle.GetStateName());
    }

    [Fact]
    public void FireIsIgnoredWhileFiring()
    {
        (_, _, SWeaponDefense rifle) = Merc();
        rifle.SwitchROF();
        rifle.SwitchROF();
        rifle.ROF_Single(true);
        rifle.eventFire(1f);
        rifle.eventFire(1f);
        Assert.Equal(19, rifle.ClipAmmo);
        Assert.Equal(SRateOfFireMode.ROF_Auto, rifle.PreviousROFMode);
    }

    [Fact]
    public void ReloadFillsTheClipFromTheReserveAtTheEndOfTheClip()
    {
        (CombatScene scene, _, SWeaponDefense rifle) = Merc();
        rifle.ClipAmmo = 10;
        rifle.Ammo = 110;
        Assert.True(rifle.eventReload());
        Assert.Equal(Reloading, rifle.GetStateName());
        rifle.eventFire(1f);
        Assert.Equal(10, rifle.ClipAmmo);
        scene.Run(2.2f);
        Assert.Equal(20, rifle.ClipAmmo);
        Assert.Equal(110, rifle.Ammo);
        Assert.Equal(Selected, rifle.GetStateName());
    }

    [Fact]
    public void AFullClipDoesNotReload()
    {
        (_, _, SWeaponDefense rifle) = Merc();
        Assert.False(rifle.eventReload());
        Assert.Equal(Selected, rifle.GetStateName());
    }

    [Fact]
    public void AnEmptyAutoClipReloadsByItself()
    {
        (CombatScene scene, _, SWeaponDefense rifle) = Merc();
        rifle.ClipAmmo = 0;
        rifle.Ammo = 50;
        scene.Run(0.1f);
        Assert.Equal(Reloading, rifle.GetStateName());
    }

    [Fact]
    public void TheAmmoCrateRefillsReserveAndClip()
    {
        (CombatScene scene, _, SWeaponDefense rifle) = Merc();
        rifle.ClipAmmo = 3;
        rifle.Ammo = 40;
        Assert.True(rifle.eventFullReload());
        Assert.Equal(FullReloading, rifle.GetStateName());
        scene.Run(2.2f);
        Assert.Equal(120, rifle.Ammo);
        Assert.Equal(20, rifle.ClipAmmo);
    }

    [Fact]
    public void SpreadDecaysAfterTheBurst()
    {
        (CombatScene scene, SPlayerController pc, SWeaponDefense rifle) = Merc();
        pc.bFire = 1;
        rifle.eventFire(1f);
        scene.Run(0.65f);
        pc.bFire = 0;
        Assert.True(rifle.BulletInARow >= 3);
        scene.Run(1f);
        Assert.Equal(0, rifle.BulletInARow);
        Assert.Equal(0.15f, rifle.GetAccuracy(), 4);
    }
}
