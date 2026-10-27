using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.SBase;
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
        Sender crate = s.Spawn(new Sender());
        v.BeginTouch(spy);
        v.BeginTouch(crate);
        s.Step();
        Assert.Equal([(10, (Pawn?)null, SDamageType.SDTBase_NoAnim)], spy.Damage);
        s.StepSeconds(2.05f);
        Assert.Equal(3, spy.Damage.Count);
        v.EndTouch(spy);
        s.StepSeconds(2f);
        Assert.Equal(3, spy.Damage.Count);
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
        Sender bottle = s.Spawn(new Sender());
        bottle.Instigator = shooter;
        v.BeginTouch(victim);
        s.StepSeconds(2f);
        Assert.Empty(victim.Damage);

        // The bottle's instigator stands in when the event carries none.
        s.Level.TriggerEvent("moloaie", bottle, null);
        Assert.True(v.bActive);
        Assert.Same(shooter.Controller, v.TriggerController);
        s.StepSeconds(1.5f);
        Assert.Equal(2, victim.Damage.Count);
        s.Level.TriggerEvent("moloaie", bottle, null);
        Assert.False(v.bActive);
        Assert.Null(v.TriggerController);
        s.StepSeconds(2f);
        Assert.Equal(2, victim.Damage.Count);
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
        Assert.Single(a.Damage);
        s.StepSeconds(0.5f);
        v.BeginTouch(b);
        Assert.Same(b, v.LastTouched);
        Assert.Equal(0f, v.LastDamageTime);
        s.Step();
        Assert.Equal(2, a.Damage.Count);
        Assert.Single(b.Damage);
    }
}
