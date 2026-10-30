using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Devices;
using OpenScct.Game.SBase;
using Xunit;

namespace OpenScct.Game.Tests.Devices;

/// <summary>spec/devices/gameplay-object.md and spec/devices/vitre.md: swaps, damage, stages, blasts, credit, reset, chaff.</summary>
public sealed class GameplayObjectTests
{
    private static readonly AssetRef Before = new("Test.before");
    private static readonly AssetRef After = new("Test.after");
    private static readonly AssetRef Broken = new("Test.broken");

    /// <summary>A light that records its switching.</summary>
    private sealed class TestLight : STriggerLight, IObjectLight
    {
        public readonly List<string> Calls = new();

        public TestLight()
        {
            bStatic = false;
            bNoDelete = false;
        }

        public override void LightOn()
        {
            Calls.Add("on");
            CurrentLightType = ELightType.LT_Steady;
        }

        public override void LightOff(bool bMalfunction = false)
        {
            Calls.Add(bMalfunction ? "off-malfunction" : "off");
            CurrentLightType = ELightType.LT_None;
        }

        protected override void Trigger(Actor? Other, Pawn? EventInstigator) => Calls.Add("trigger");

        protected override void UnTrigger(Actor? Other, Pawn? EventInstigator) => Calls.Add("untrigger");
    }

    /// <summary>
    /// An actor recording the GPODamageInstigator its damage arrives under (a pawn would take the blast through its
    /// player controller's PawnTakeDamage, natives2-damage.md rule 1.3.4).
    /// </summary>
    private sealed class CreditWitness : Actor
    {
        public readonly List<(int Damage, Pawn? Instigator, SDamageType Type, Pawn? GpoInstigator)> Hits = new();

        protected override void TakeDamage(int Damage, Pawn? EventInstigator, FVector HitLocation, FVector Momentum,
            SDamageType DamageType, int PillTag = 0) =>
            Hits.Add((Damage, EventInstigator, DamageType, (Level.Game as SGameInfo)?.GPODamageInstigator));
    }

    [Fact]
    public void ATriggerTogglesTheTriggeredMeshAndTheCollisionFollows()
    {
        DeviceScene s = DeviceScene.Create();
        SGamePlayObject wall = s.Add(new SGamePlayObject { StaticMesh = Before, TriggeredMesh = After, HitPoints = 10 }, tag: "swap");
        s.Collider(wall, V.At(-10f, -10f, -10f), V.At(10f, 10f, 10f), variants: 1);
        TestLight light = s.Add(new TestLight());
        wall.ObjectLights.Add(light);
        s.Begin();
        Assert.True(wall.bWorldGeometry);
        Assert.False(wall.bSkipActorPropertyReplication);
        Assert.True(wall.bCanBeChaffed);
        var colliders = new List<int>();
        s.Collision!.GetActorColliders(wall, colliders);
        int ci = Assert.Single(colliders);

        s.Level.TriggerEvent("swap", null, null);
        Assert.Equal(After, wall.StaticMesh);
        Assert.False(s.Collision.GetCollider(ci).Enabled);
        s.Level.TriggerEvent("swap", null, null);
        Assert.Equal(Before, wall.StaticMesh);
        Assert.True(s.Collision.GetCollider(ci).Enabled);
        s.Level.TriggerEvent("swap", null, null);
        s.Level.UntriggerEvent("swap", null, null);
        Assert.Equal(Before, wall.StaticMesh);
        Assert.Equal(["trigger", "trigger", "trigger", "untrigger"], light.Calls);
    }

    [Fact]
    public void ATriggeredSkinTogglesSkinZero()
    {
        DeviceScene s = DeviceScene.Create();
        SGamePlayObject tv = s.Add(new SGamePlayObject { TriggeredSkin = new AssetRef("Test.off") }, tag: "tv");
        tv.Skins.Add(new AssetRef("Test.on"));
        s.Begin();
        s.Level.TriggerEvent("tv", null, null);
        Assert.Equal(new AssetRef("Test.off"), tv.Skins[0]);
        Assert.Equal(new AssetRef("Test.off"), tv.RepSkin);
        s.Level.TriggerEvent("tv", null, null);
        Assert.Equal(new AssetRef("Test.on"), tv.Skins[0]);
    }

    private static SGamePlayObject Extinguisher(string stageEvent = "doorblast2") => new()
    {
        StaticMesh = Before,
        bDamageable = true,
        HitPoints = 1,
        bCanDamageActor = true,
        DamageActorPoints = 600,
        DamageActorRange = 400,
        DamageActorType = SDamageType.SDamage_Charged,
        DestroySound = new AssetRef("boom"),
        DamagedMeshes = { new FDamagedMesh { StaticMesh = Broken, Percent = 0, Event = stageEvent } },
    };

    [Fact]
    public void AnExtinguisherBreaksBlastsCreditsTheShooterAndOpensItsDoor()
    {
        DeviceScene s = DeviceScene.Create();
        s.Level.Info.Game = new SGameInfo();
        SGamePlayObject extinguisher = s.Add(Extinguisher(), V.At(0f, 0f, 0f));
        SGamePlayObject door = s.Add(new SGamePlayObject
        {
            StaticMesh = Before,
            bDamageable = true,
            HitPoints = 1,
            Event = "PORTE1",
            DamagedMeshes = { new FDamagedMesh { Percent = 100, Event = "PORTE1" } },
            TakeDamageTypes = { SDamageType.NoDamageType },
        }, V.At(100f, 0f, 0f), tag: "doorblast2");
        door.DestroyOnEventFrom.Add(extinguisher);
        EventProbe dust = s.Add(new EventProbe(), tag: "PORTE1");
        s.Begin();
        TestSpy spy = s.Player<TestSpy>(V.At(2000f, 0f, 0f));
        CreditWitness near = s.Spawn(new CreditWitness(), V.At(300f, 0f, 0f));
        CreditWitness far = s.Spawn(new CreditWitness(), V.At(401f, 0f, 0f));

        // Bullets cannot break the door.
        door.eventTakeDamage(50, spy, default, V.At(1f, 0f, 0f), SDamageType.SDTBase);
        Assert.Equal(1, door.HitPoints);

        extinguisher.eventTakeDamage(30, spy, default, V.At(1f, 0f, 0f), SDamageType.SDTBase);
        Assert.Equal(0, extinguisher.HitPoints);
        Assert.True(extinguisher.bAlreadyDestroyed);
        Assert.Equal(Broken, extinguisher.StaticMesh);
        Assert.Same(spy, extinguisher.Instigator);

        // The blast, inside the credit wrapper: int((400 - 300) 600 / 400) = 150 with no instigator of its own
        // (ProcessDamage 0x10a8fecb), the shooter credited through GPODamageInstigator.
        (int damage, Pawn? by, SDamageType type, Pawn? gpo) = Assert.Single(near.Hits);
        Assert.Equal(150, damage);
        Assert.Null(by);
        Assert.Equal(SDamageType.SDamage_Charged, type);
        Assert.Same(spy, gpo);
        Assert.Empty(far.Hits);
        Assert.Null(((SGameInfo)s.Level.Info.Game).GPODamageInstigator);

        // The stage event destroyed the door (credited through Other.Instigator), whose own stage fired its dust.
        Assert.Equal(0, door.HitPoints);
        Assert.Same(spy, door.Instigator);
        Assert.Equal(EDrawType.DT_None, door.DrawType);
        Assert.Single(dust.Calls);
        Assert.Same(door, dust.Calls[0].Other);

        // Dead objects ignore triggers and further damage.
        s.Level.TriggerEvent("doorblast2", extinguisher, spy);
        Assert.Single(dust.Calls);
        extinguisher.eventTakeDamage(30, spy, default, default, SDamageType.SDTBase);
        Assert.Single(near.Hits);
        Assert.Contains(s.World.Events.Current.ToArray(), e => e.Kind == SimEventKind.PlaySound && e.What == new Name("boom"));
    }

    [Fact]
    public void FilteredDamageTypesDoNothing()
    {
        DeviceScene s = DeviceScene.Create();
        SGamePlayObject gas = s.Add(new SGamePlayObject
        {
            bDamageable = true,
            HitPoints = 20,
            TakeDamageSound = new AssetRef("hit"),
            TakeDamageTypes = { SDamageType.SDTBase, SDamageType.SDamage_Explosion },
        });
        s.Begin();
        gas.eventTakeDamage(5, null, default, default, SDamageType.SDamage_Charged);
        Assert.Equal(20, gas.HitPoints);
        Assert.Empty(s.World.Events.Current.ToArray());
        gas.eventTakeDamage(5, null, default, default, SDamageType.SDTBase);
        Assert.Equal(15, gas.HitPoints);
    }

    [Fact]
    public void StagesApplyInOrderAreReappliedOnEveryHitAndTheirEventsAlternate()
    {
        DeviceScene s = DeviceScene.Create();
        SGamePlayObject gpo = s.Add(new SGamePlayObject
        {
            StaticMesh = Before,
            bDamageable = true,
            HitPoints = 4,
            DamagedMeshes =
            {
                new FDamagedMesh { StaticMesh = After, Percent = 50, Event = "half", EventType = EEventType.EVT_TriggerUntrigger },
                new FDamagedMesh { StaticMesh = Broken, Percent = 75, Event = "three" },
            },
        });
        EventProbe half = s.Add(new EventProbe(), tag: "half");
        EventProbe three = s.Add(new EventProbe(), tag: "three");
        s.Begin();
        gpo.eventTakeDamage(1, null, default, default, SDamageType.SDTBase);
        Assert.Equal(Before, gpo.StaticMesh);
        gpo.eventTakeDamage(1, null, default, default, SDamageType.SDTBase);
        Assert.Equal(After, gpo.StaticMesh);
        Assert.True(Assert.Single(half.Calls).Trigger);
        Assert.Equal(EEventType.EVT_UntriggerTrigger, gpo.DamagedMeshes[0].EventType);
        gpo.eventTakeDamage(1, null, default, default, SDamageType.SDTBase);
        Assert.Equal(Broken, gpo.StaticMesh);
        Assert.Single(three.Calls);

        // The native re-applies the current stage on every call and fires its event again (natives2-damage.md rule 1.6).
        gpo.eventTakeDamage(1, null, default, default, SDamageType.SDTBase);
        Assert.Equal(2, three.Calls.Count);
        Assert.Single(half.Calls);
    }

    [Fact]
    public void ALampKeepsItsMeshGoesDarkAndRelightsAfterItsResetTime()
    {
        DeviceScene s = DeviceScene.Create();
        SGamePlayObject lamp = s.Add(new SGamePlayObject
        {
            StaticMesh = Before,
            bDamageable = true,
            HitPoints = 1,
            ResetDamageTime = 60,
            OnResetEvent = "relit",
            DamagedMeshes = { new FDamagedMesh { Percent = 100, Skin = new AssetRef("Test.dark") } },
            SpawnableObjects = { new FSpawnInfo { SpawnClass = new ClassRef("SBase.SSparkShocker"), MinPercent = 100, MaxPercent = 100 } },
        });
        lamp.Skins.Add(new AssetRef("Test.lit"));
        TestLight light = s.Add(new TestLight { bInitialyOn = true });
        lamp.ObjectLights.Add(light);
        EventProbe relit = s.Add(new EventProbe(), tag: "relit");
        s.Begin();
        Assert.Single(relit.Calls);

        lamp.eventTakeDamage(1, null, default, default, SDamageType.SDTBase);
        Assert.Equal(Before, lamp.StaticMesh);
        Assert.Equal(new AssetRef("Test.dark"), lamp.Skins[0]);
        Assert.Equal(["off"], light.Calls);
        Assert.True(lamp.IsInState("s_WaitForDamageReset"));
        Actor? spark = lamp.SpawnableObjects[0].SpawnedActor;
        Assert.NotNull(spark);

        // Waiting: triggers and damage are ignored.
        lamp.eventTrigger(null, null);
        lamp.eventTakeDamage(1, null, default, default, SDamageType.SDTBase);
        s.StepSeconds(59f);
        Assert.Equal(0, lamp.HitPoints);
        s.StepSeconds(1.1f);
        Assert.Equal(1, lamp.HitPoints);
        Assert.False(lamp.bAlreadyDestroyed);
        Assert.Equal(new AssetRef("Test.lit"), lamp.Skins[0]);
        Assert.Equal(["off", "on"], light.Calls);
        Assert.Equal(2, relit.Calls.Count);
        Assert.True(spark!.bDeleteMe || lamp.SpawnableObjects[0].SpawnedActor is null);
        Assert.Equal(Name.None, lamp.StateNode.Name);
    }

    [Fact]
    public void AMomentumIsNormalisedAndPushesAPushableProp()
    {
        DeviceScene s = DeviceScene.Create();
        SGamePlayObject can = s.Add(new SGamePlayObject { bPushable = true, HitPoints = 100, bDamageable = true });
        s.Begin();
        Assert.False(can.bWorldGeometry);
        Assert.False(can.AcceptStickableGPO());
        can.eventTakeDamage(1, null, default, V.At(1000f, 0f, 0f), SDamageType.SDTBase);
        Assert.True(can.IsInState("s_Flying"));
        Assert.Equal(EPhysics.PHYS_Falling, can.Physics);

        // 300 along X with +-25 jitter, Z raised to 300, times 5 / Mass 8.
        Assert.InRange(can.Velocity.X, 275f * 5f / 8f, 325f * 5f / 8f);
        Assert.InRange(can.Velocity.Y, -25f * 5f / 8f, 25f * 5f / 8f);
        Assert.Equal(300f * 5f / 8f, can.Velocity.Z, 3);
        Assert.Equal(10000, can.RotationRate.Pitch);
    }

    [Fact]
    public void AHeavyPawnWalkingIntoAPushablePropKicksIt()
    {
        DeviceScene s = DeviceScene.Create();
        SGamePlayObject can = s.Add(new SGamePlayObject { bPushable = true });
        s.Begin();
        TestMerc merc = s.Player<TestMerc>(default);
        merc.Mass = 100f;
        merc.Velocity = V.At(200f, 0f, 0f);
        can.BeginTouch(merc);
        Assert.True(can.IsInState("s_Flying"));
        Assert.Equal(V.At(200f, 0f, 50f), can.Velocity);
    }

    [Fact]
    public void AChaffLockMalfunctionsLightsOffAndComesBack()
    {
        DeviceScene s = DeviceScene.Create();
        SGamePlayObject lamp = s.Add(new SGamePlayObject { StaticMesh = Before, TriggeredMesh = After });
        TestLight light = s.Add(new TestLight { CurrentLightType = ELightType.LT_Steady });
        lamp.ObjectLights.Add(light);
        s.Begin();
        lamp.eventSetLockedByChaff(2f);
        Assert.True(lamp.IsInState("s_LockedByChaff"));
        Assert.Equal(After, lamp.StaticMesh);
        Assert.Equal(["off-malfunction"], light.Calls);
        Assert.Equal((1, 21, (Actor?)null), Assert.Single(s.Hooks.TeamMessages));

        // A new lock restarts the timer.
        s.StepSeconds(1.5f);
        lamp.eventSetLockedByChaff(2f);
        s.StepSeconds(1.5f);
        Assert.True(lamp.IsInState("s_LockedByChaff"));
        s.StepSeconds(0.6f);
        Assert.False(lamp.IsInState("s_LockedByChaff"));
        Assert.Equal(Before, lamp.StaticMesh);
        Assert.Equal(["off-malfunction", "on"], light.Calls);
        Assert.Equal(Name.None, lamp.ChaffLockPreviousState);

        // With every light already off, a lock does nothing.
        light.CurrentLightType = ELightType.LT_None;
        lamp.eventSetLockedByChaff(2f);
        Assert.False(lamp.IsInState("s_LockedByChaff"));
    }

    [Fact]
    public void AStickyShotOnAStickyAsChaffObjectLocksInsteadOfDamaging()
    {
        DeviceScene s = DeviceScene.Create();
        SGamePlayObject obj = s.Add(new SGamePlayObject { bDamageable = true, HitPoints = 5, bCanBeChaffed = true, bStickyDamageIsLikeChaff = true });
        s.Begin();
        obj.eventTakeDamage(5, null, default, default, SDamageType.SDTStickyShocker);
        Assert.Equal(5, obj.HitPoints);
        Assert.True(obj.IsInState("s_LockedByChaff"));
        Assert.Contains(s.Level.Actors, a => a is SSparkShocker);
    }

    [Fact]
    public void ABasicObjectSpawnsItsInteractionAndKeepsItsHitPoints()
    {
        DeviceScene s = DeviceScene.Create();
        SInfirmary kit = s.Add(new SInfirmary { iPriority = 7, bInteractInFront = true, bInitialyUsable = false, bDamageable = true }, V.At(5f, 6f, 7f));
        s.Begin();
        SInteractObject entry = Assert.IsType<SInfirmaryInteraction>(kit.InteractionObj);
        Assert.Same(kit, entry.Owner);
        Assert.Equal(V.At(5f, 6f, 7f), entry.Location);
        Assert.Equal(7, entry.iPriority);
        Assert.True(entry.bInteractInFront);
        Assert.False(entry.bUsable);
        Assert.Equal(1, kit.HitPoints);
        Assert.False(kit.AcceptStickableGPO());

        // SBasicGamePlayObject damage: down to 0, the destroy sound, and a bNoDelete object stays.
        kit.DestroySound = new AssetRef("crash");
        kit.eventTakeDamage(3, null, default, default, SDamageType.SDTBase);
        Assert.Equal(0, kit.HitPoints);
        Assert.False(kit.bDeleteMe);
    }

    [Fact]
    public void HighPriorityObjectsPutMalfunctionMarkersOnEveryPlayersHud()
    {
        DeviceScene s = DeviceScene.Create();
        SGamePlayObject low = s.Add(new SGamePlayObject { iPriority = 4 });
        SGamePlayObject high = s.Add(new SGamePlayObject { iPriority = 5 }, V.At(1f, 2f, 3f));
        s.Begin();
        s.Player<TestSpy>(default);
        s.Player<TestMerc>(default);
        low.AddOnHud();
        Assert.Empty(s.Hooks.GpoMarkers);
        high.AddOnHud();
        Assert.Equal(2, s.Hooks.GpoMarkers.Count);
        Assert.All(s.Hooks.GpoMarkers, m => Assert.Equal(V.At(1f, 2f, 3f), m.Location));
        high.RemoveFromHud();
        Assert.Equal(4, s.Hooks.GpoMarkers.Count);
        Assert.False(s.Hooks.GpoMarkers[3].Added);
    }

    [Fact]
    public void TheMercChargeDamagesAGameplayObjectFromThePlayersPawn()
    {
        DeviceScene s = DeviceScene.Create();
        SGamePlayObject gpo = s.Add(new SGamePlayObject { bDamageable = true, HitPoints = 10 });
        s.Begin();
        TestMerc merc = s.Player<TestMerc>(default);
        ((SPlayerController)merc.Controller!).DestroyGPO(gpo, 4, SDamageType.SDamage_Charged);
        Assert.Equal(6, gpo.HitPoints);
        Assert.Same(merc, gpo.Instigator);
    }

    [Fact]
    public void AGlassPaneBreaksOnAnyHitAndLetsEverythingThrough()
    {
        DeviceScene s = DeviceScene.Create();
        SVitre pane = s.Add(new SVitre(), tag: "pane");
        pane.DamagedMeshes[0] = pane.DamagedMeshes[0] with { Event = "vitre_brisee" };
        s.Collider(pane, V.At(-5f, -60f, -50f), V.At(5f, 60f, 50f));
        SAmbientSoundTrigger outside = s.Add(new SAmbientSoundTrigger { AmbientSound = new AssetRef("Squ_Fenetre_ouverte1") }, tag: "vitre_brisee");
        s.Begin();
        Assert.Equal(new AssetRef("SGameplayObjects.Objects.F_vitreSolo01"), pane.StaticMesh);
        Assert.True(pane.bWorldGeometry);
        var colliders = new List<int>();
        s.Collision!.GetActorColliders(pane, colliders);
        Assert.True(s.Collision.GetCollider(colliders[0]).Enabled);

        pane.eventTakeDamage(1, null, default, default, SDamageType.SDTFlash);
        Assert.Equal(0, pane.HitPoints);
        Assert.Equal(EDrawType.DT_None, pane.DrawType);
        Assert.False(s.Collision.GetCollider(colliders[0]).Enabled);
        Assert.IsType<SVitreEmitter>(pane.SpawnableObjects[0].SpawnedActor);
        Assert.Equal(new AssetRef("Squ_Fenetre_ouverte1"), outside.AmbientSound);

        // A broken pane stays broken: triggers do nothing.
        s.Level.TriggerEvent("pane", null, null);
        Assert.Equal(EDrawType.DT_None, pane.DrawType);
    }

    [Fact]
    public void AFlyingPropStopsWhereItLandsAndHurtsAPawnItHitsFast()
    {
        DeviceScene s = DeviceScene.Create();
        SGamePlayObject can = s.Add(new SGamePlayObject { bPushable = true, Mass = 100f });
        s.Begin();
        TestSpy thrower = s.Player<TestSpy>(default);
        TestMerc target = s.Player<TestMerc>(default);
        can.SetOwner(thrower);
        can.GotoState("s_Flying");
        can.Velocity = V.At(100f, 0f, 0f);
        can.eventBump(target);

        // |V| x Mass / 500 = 20 SDTBase from the owner, through the pawn's controller.
        Assert.Equal([(20, (Pawn?)thrower, SDamageType.SDTBase)], ((TestController)target.Controller!).PawnDamage);
        can.Velocity = V.At(10f, 0f, 0f);
        can.eventBump(target);
        Assert.Single(((TestController)target.Controller!).PawnDamage);

        can.eventLanded(V.At(0f, 0f, 1f));
        Assert.Equal(Name.None, can.StateNode.Name);
        Assert.Equal(EPhysics.PHYS_None, can.Physics);
        Assert.Null(can.Owner);
        Assert.True(can.bCollideActors);
    }

    [Fact]
    public void ADamageEmitterRidesTheObjectUntilKilled()
    {
        DeviceScene s = DeviceScene.Create();
        SGamePlayObject gpo = s.Add(new SGamePlayObject
        {
            bDamageable = true,
            HitPoints = 2,
            SpawnableObjects = { new FSpawnInfo { SpawnParticle = new AssetRef("Test.smoke"), MinPercent = 50, MaxPercent = 100 } },
        });
        s.Begin();
        gpo.eventTakeDamage(1, null, default, default, SDamageType.SDTBase);
        SSpawnableEmitter smoke = Assert.IsType<SSpawnableEmitter>(gpo.SpawnableObjects[0].SpawnedActor);
        Assert.Same(gpo, smoke.Base);
        Assert.Equal([new AssetRef("Test.smoke")], smoke.Emitters);
        gpo.eventTakeDamage(1, null, default, default, SDamageType.SDTBase);
        Assert.Same(smoke, gpo.SpawnableObjects[0].SpawnedActor);
        gpo.KillDamageEmitter(0);
        Assert.True(smoke.bDeleteMe);
        Assert.Null(gpo.SpawnableObjects[0].SpawnedActor);
    }

    [Fact]
    public void AnExplosionVisualFlashesForHalfASecond()
    {
        DeviceScene s = DeviceScene.Create().Begin();
        SGrenadeExplosion boom = s.Spawn(new SGrenadeExplosion(), V.At(1f, 2f, 3f));
        var flashes = new List<SFlashLight_FragGrenade>();
        foreach (SFlashLight_FragGrenade f in s.Level.AllActors<SFlashLight_FragGrenade>())
        {
            flashes.Add(f);
        }

        SFlashLight_FragGrenade flash = Assert.Single(flashes);
        Assert.Equal(0.5f, flash.LifeSpan);
        Assert.Equal(V.At(1f, 2f, 3f), flash.Location);
        Assert.Equal(23f, boom.LifeSpan);
        s.StepSeconds(0.6f);
        Assert.True(flash.bDeleteMe);
    }
}
