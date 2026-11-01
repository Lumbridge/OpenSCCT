using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.SBase;
using OpenScct.Game.Tests.Combat;
using Xunit;

namespace OpenScct.Game.Tests.Devices;

/// <summary>spec/devices/extincteur.md and spec/devices/damage-volume.md.</summary>
public sealed class ExtincteurAndDamageVolumeTests
{
    [Fact]
    public void AShotExtinguisherSpraysMakesANoiseAndReArmsAfterTenSeconds()
    {
        DeviceScene s = DeviceScene.Create();
        SExtincteur ext = s.Add(new SExtincteur());
        s.Begin();
        Assert.True(ext.IsInState("s_Waiting"));
        Assert.Equal(new AssetRef("Armes_impacts.Impacts.RA_Explo_Extincteur"), ext.sndImpact);
        TestMerc shooter = s.Player<TestMerc>(default);
        TestSpy other = s.Player<TestSpy>(default);

        ext.eventTakeDamage(0, shooter, V.At(10f, 0f, 50f), V.At(-1f, 0f, 0f), SDamageType.SDTBase);
        Assert.True(ext.IsInState("s_Used"));
        SExtincteurEmitter cloud = Assert.IsType<SExtincteurEmitter>(ext.Emitter);
        Assert.Equal(V.At(10f, 0f, 50f), cloud.Location);
        Assert.Equal(0, cloud.Rotation.Yaw & 65535);
        foreach (Pawn p in new Pawn[] { shooter, other })
        {
            Assert.Equal([(ext, 1f)], ((TestController)p.Controller!).Noises);
        }

        Assert.Contains(s.World.Events.Current.ToArray(), e => e.Kind == SimEventKind.PlaySound && e.What == new Name(ext.sndImpact.Path));

        // Further shots do nothing while it sprays.
        ext.eventTakeDamage(5, shooter, default, V.At(1f, 0f, 0f), SDamageType.SDTBase);
        Assert.Same(cloud, ext.Emitter);
        s.StepSeconds(9.9f);
        Assert.True(ext.IsInState("s_Used"));
        s.StepSeconds(0.2f);
        Assert.True(ext.IsInState("s_Waiting"));

        // Killed, the cloud's puffs live out their 1.6 s and it goes.
        s.StepSeconds(1.7f);
        Assert.True(cloud.bDeleteMe);
    }

    [Fact]
    public void ResetStopsTheCloudAndReArms()
    {
        DeviceScene s = DeviceScene.Create();
        SExtincteur ext = s.Add(new SExtincteur());
        s.Begin();
        ext.eventTakeDamage(1, null, default, V.At(0f, 1f, 0f), SDamageType.SDTBase);
        ext.Reset();
        Assert.True(ext.IsInState("s_Waiting"));
        Assert.True(ext.Emitter!.LifeSpan > 0f);
    }

    private static (DeviceScene S, SDamageVolume V) Volume(System.Action<SDamageVolume> setup)
    {
        DeviceScene s = DeviceScene.Create();
        SDamageVolume v = s.Add(new SDamageVolume(), tag: "moloaie");
        setup(v);
        s.Begin();
        return (s, v);
    }

    [Fact]
    public void AnActiveVolumeHurtsItsClassEveryPeriodStartingOnEntry()
    {
        (DeviceScene s, SDamageVolume v) = Volume(v =>
        {
            v.bActive = true;
            v.HitPoints = 10;
        });
        Assert.Equal(1f, v.Period);
        Assert.Equal(SDamageType.SDTBase_NoAnim, v.DamageType);
        s.StepSeconds(2f);
        TestSpy spy = s.Player<TestSpy>(default);
        TestController controller = (TestController)spy.Controller!;
        Sender crate = s.Spawn(new Sender());
        v.BeginTouch(spy);
        v.BeginTouch(crate);
        s.Step();
        Assert.Equal([(10, (Pawn?)null, SDamageType.SDTBase_NoAnim)], controller.PawnDamage);
        Assert.Empty(spy.Damage);
        s.StepSeconds(2.05f);
        Assert.Equal(3, controller.PawnDamage.Count);
        v.EndTouch(spy);
        s.StepSeconds(2f);
        Assert.Equal(3, controller.PawnDamage.Count);
    }

    [Fact]
    public void ATogglingVolumeSwitchesWithEachTriggerAndCreditsItsInstigator()
    {
        (DeviceScene s, SDamageVolume v) = Volume(v =>
        {
            v.bToggle = true;
            v.HitPoints = 30;
        });
        TestMerc shooter = s.Player<TestMerc>(default);
        TestSpy victim = s.Player<TestSpy>(default);
        TestController controller = (TestController)victim.Controller!;
        Sender bottle = s.Spawn(new Sender());
        bottle.Instigator = shooter;
        v.BeginTouch(victim);
        s.StepSeconds(2f);
        Assert.Empty(controller.PawnDamage);

        // The bottle's instigator stands in when the event carries none.
        s.Level.TriggerEvent("moloaie", bottle, null);
        Assert.True(v.bActive);
        Assert.Same(shooter.Controller, v.TriggerController);
        s.StepSeconds(1.5f);
        Assert.Equal(2, controller.PawnDamage.Count);
        s.Level.TriggerEvent("moloaie", bottle, null);
        Assert.False(v.bActive);
        Assert.Null(v.TriggerController);
        s.StepSeconds(2f);
        Assert.Equal(2, controller.PawnDamage.Count);
    }

    [Fact]
    public void ANonTogglingVolumeOnlyTurnsOnAndAnUnTriggerTurnsItOff()
    {
        (DeviceScene s, SDamageVolume v) = Volume(_ => { });
        s.Level.TriggerEvent("moloaie", null, null);
        s.Level.TriggerEvent("moloaie", null, null);
        Assert.True(v.bActive);
        Assert.Null(v.TriggerController);
        s.Level.UntriggerEvent("moloaie", null, null);
        Assert.False(v.bActive);
    }

    [Fact]
    public void WithoutAnyInstigatorTheGameplayObjectDamageInstigatorIsCredited()
    {
        (DeviceScene s, SDamageVolume v) = Volume(v => v.bToggle = true);
        var game = new SGameInfo();
        s.Level.Info.Game = game;
        TestMerc shooter = s.Player<TestMerc>(default);
        game.GPODamageInstigator = shooter;
        s.Level.TriggerEvent("moloaie", null, null);
        Assert.Same(shooter.Controller, v.TriggerController);
    }

    [Fact]
    public void ANewToucherRestartsThePeriod()
    {
        (DeviceScene s, SDamageVolume v) = Volume(v => v.bActive = true);
        TestSpy a = s.Player<TestSpy>(default);
        TestSpy b = s.Player<TestSpy>(default);
        s.StepSeconds(3f);
        v.BeginTouch(a);
        s.Step();
        Assert.Single(((TestController)a.Controller!).PawnDamage);
        s.StepSeconds(0.5f);
        v.BeginTouch(b);
        Assert.Same(b, v.LastTouched);
        Assert.Equal(0f, v.LastDamageTime);
        s.Step();
        Assert.Equal(2, ((TestController)a.Controller!).PawnDamage.Count);
        Assert.Single(((TestController)b.Controller!).PawnDamage);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FireDamagesARealPlayerEverySecondAndStopsWhenUntriggered(bool spy)
    {
        var s = new CombatScene();
        (_, SBasePawn victim) = s.Player(spy, V.At(0f, 0f, 100f));
        s.Run(2f);
        SDamageVolume fire = s.Level.SpawnActor(new SDamageVolume { HitPoints = 30, bNoDelete = false }, victim.Location, default)!;
        fire.BeginTouch(victim);
        fire.eventTrigger(null, null);
        int health = victim.Health;
        s.World.Step();
        Assert.Equal(health - 30, victim.Health);
        s.Run(0.5f);
        Assert.Equal(health - 30, victim.Health);
        s.Run(0.55f);
        Assert.Equal(health - 60, victim.Health);
        fire.eventUnTrigger(null, null);
        s.Run(1.1f);
        Assert.Equal(health - 60, victim.Health);
    }

    [Theory]
    [InlineData("s_GrabbedFromLedge_Begin")]
    [InlineData("s_FallAfterGrabbedFromLedge")]
    [InlineData("s_Dead")]
    [InlineData("GameEnded")]
    public void VolumeIgnoresProtectedControllerStates(string state)
    {
        (DeviceScene s, SDamageVolume v) = Volume(v => v.bActive = true);
        TestMerc victim = s.Player<TestMerc>(default);
        var controller = (TestController)victim.Controller!;
        controller.GotoState(state);
        Assert.True(controller.IsInState(state));
        Assert.False(controller.bIsDamageVolumeSensitive());
        v.BeginTouch(victim);
        s.StepSeconds(1.1f);
        Assert.Empty(controller.PawnDamage);
    }

    [Fact]
    public void VolumeOnAClientNeverAppliesDamage()
    {
        (DeviceScene s, SDamageVolume v) = Volume(v => v.bActive = true);
        TestSpy victim = s.Player<TestSpy>(default);
        v.Role = ENetRole.ROLE_SimulatedProxy;
        v.BeginTouch(victim);
        s.StepSeconds(2f);
        Assert.Empty(((TestController)victim.Controller!).PawnDamage);
        Assert.Empty(victim.Damage);
    }

    [Fact]
    public void MercOnlyVolumesKeepTheirAuthoredClassRestriction()
    {
        (DeviceScene s, SDamageVolume v) = Volume(v =>
        {
            v.bActive = true;
            v.DamagedClass = new ClassRef("SBase.SPAWNDEFENSE");
        });
        TestSpy spy = s.Player<TestSpy>(default);
        TestMerc merc = s.Player<TestMerc>(default);
        v.BeginTouch(spy);
        v.BeginTouch(merc);
        s.StepSeconds(1.1f);
        Assert.Empty(((TestController)spy.Controller!).PawnDamage);
        Assert.Single(((TestController)merc.Controller!).PawnDamage);
    }

    [Fact]
    public void ExtinguisherVolumesStillDamageTheirNonPawnTargets()
    {
        DeviceScene s = DeviceScene.Create();
        var v = s.Add(new SDamageVolume
        {
            bActive = true,
            DamagedClass = new ClassRef("SBase.SExtincteur"),
        });
        SExtincteur extinguisher = s.Add(new SExtincteur());
        s.Begin();
        v.BeginTouch(extinguisher);
        s.StepSeconds(1.1f);
        Assert.True(extinguisher.IsInState("s_Used"));
        Assert.IsType<SExtincteurEmitter>(extinguisher.Emitter);
    }
}
