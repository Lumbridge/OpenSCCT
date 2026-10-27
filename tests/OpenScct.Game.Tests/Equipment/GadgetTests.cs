using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Combat;
using OpenScct.Game.Equipment;
using OpenScct.Game.SBase;
using OpenScct.Game.Tests.Combat;
using Xunit;

namespace OpenScct.Game.Tests.Equipment;

/// <summary>Grenades, the shocker, the torch, the loadout and the combat call ownership check.</summary>
public sealed class GadgetTests
{
    [Fact]
    public void AHalfChargedThrowLaunchesTheFragAt1012Plus210AndUsesACharge()
    {
        var scene = new CombatScene(ENetMode.NM_ListenServer);
        (SPlayerController pc, SBasePawn merc) = scene.Player(false, new FVector(0f, 0f, 100f));
        // Past the 2 s refire delay from LastUsed 0 (gadget-natives.md rule 1).
        scene.Run(2.5f);
        SWeaponDefense rifle = scene.Give(new SWeaponDefense(), merc);
        rifle.GiveTo(merc);
        merc.WeaponLoaded = 2;
        SGadFragGrenade frag = scene.Give(new SGadFragGrenade(), merc);
        frag.GiveTo(merc);
        merc.SelectedGadget = frag;
        Assert.Equal(4, frag.Charge);

        pc.bAltFire = 1;
        pc.AltFire(1f);
        Assert.True(pc.bUsingNow);
        for (int i = 0; i < 45; i++)
        {
            scene.World.Step();
            pc.EquipmentPlayerTick(Sim.DeltaTime);
        }

        pc.bAltFire = 0;
        pc.EquipmentPlayerTick(Sim.DeltaTime);
        Assert.False(pc.bUsingNow);
        SProjFrag? thrown = null;
        foreach (SProjFrag p in scene.Level.DynamicActors<SProjFrag>())
        {
            Assert.Null(thrown);
            thrown = p;
        }

        Assert.NotNull(thrown);
        // Force 1012 plus the launch bonus of 210 (ASProjGrenade::execLaunch, grenade-throw.md rule 4).
        Assert.Equal(1222f, thrown.Velocity.Size(), 0);
        Assert.Equal(3, frag.Charge);
        Assert.Equal(new Name("s_FireGrenade"), rifle.GetStateName());
    }

    [Fact]
    public void AnOfflineGameRaisesGadgetChargesTo999()
    {
        var scene = new CombatScene();
        (_, SBasePawn merc) = scene.Player(false, new FVector(0f, 0f, 100f));
        SGadFragGrenade frag = scene.Give(new SGadFragGrenade(), merc);
        frag.GiveTo(merc);
        Assert.Equal(999, frag.Charge);
    }

    [Fact]
    public void AFragExplosionHurtsLessWithDistanceAndNothingBeyondItsRange()
    {
        var frag = new SProjFrag();
        Assert.Equal(334, frag.DamageAt(0f));
        Assert.Equal(167, frag.DamageAt(250f));
        Assert.Equal(0, frag.DamageAt(500f));
    }

    [Fact]
    public void AFragBlastSkipsHiddenActors()
    {
        // ASProjFrag::Explode 0x10ab4780 walks a copy of the actor list without hidden actors (bHidden, +0x2e8 bit
        // 0x1000), so a hidden pawn in range takes nothing (natives2-damage.md Data and rule 4).
        var scene = new CombatScene();
        (_, SBasePawn seen) = scene.Player(false, new FVector(300f, 0f, 100f));
        (_, SBasePawn hidden) = scene.Player(false, new FVector(-300f, 0f, 100f));
        scene.Run(0.5f);
        hidden.bHidden = true;
        int seenHealth = seen.Health;
        int hiddenHealth = hidden.Health;
        SProjFrag frag = scene.Level.SpawnActor(new SProjFrag(), new FVector(0f, 0f, 100f), default)!;
        frag.Explode(frag.Location, new FVector(0f, 0f, 1f));
        Assert.True(seen.Health < seenHealth);
        Assert.Equal(hiddenHealth, hidden.Health);
    }

    [Fact]
    public void SixSecondsOfSmokeKnockAMercDown()
    {
        var scene = new CombatScene();
        (SPlayerController pc, SBasePawn merc) = scene.Player(false, new FVector(0f, 0f, 100f));
        scene.Run(3f);
        for (int i = 0; i < (int)(6.2f * Sim.Hz); i++)
        {
            pc.eventGoInSmoke(1.5f, 6f, 6f, 0.8f);
            scene.World.Step();
            merc.SetBase(scene.Level.Info);
            pc.CombatPlayerTick(Sim.DeltaTime);
        }

        Assert.Equal(new Name("s_FallDown"), pc.GetStateName());
    }

    [Fact]
    public void SmokeKnocksAnUnbasedMercOffHisFeet()
    {
        var scene = new CombatScene();
        (SPlayerController pc, _) = scene.Player(false, new FVector(0f, 0f, 100f));
        scene.Run(3f);
        for (int i = 0; i < (int)(6.2f * Sim.Hz); i++)
        {
            pc.eventGoInSmoke(1.5f, 6f, 6f, 0.8f);
            scene.World.Step();
            pc.CombatPlayerTick(Sim.DeltaTime);
        }

        Assert.Equal(new Name("s_FallFallDropAttacked"), pc.GetStateName());
    }

    [Fact]
    public void SmokeDoesNotKnockASpyDown()
    {
        var scene = new CombatScene();
        (SPlayerController pc, _) = scene.Player(true, new FVector(0f, 0f, 100f));
        scene.Run(3f);
        Name before = pc.GetStateName();
        for (int i = 0; i < (int)(6.2f * Sim.Hz); i++)
        {
            pc.eventGoInSmoke(1.5f, 6f, 6f, 0.8f);
            scene.World.Step();
            pc.CombatPlayerTick(Sim.DeltaTime);
        }

        Assert.Equal(before, pc.GetStateName());
    }

    [Fact]
    public void TheShockerSpendsEnergyAndClicksWhenEmpty()
    {
        var scene = new CombatScene();
        (_, SBasePawn pawn) = scene.Player(true, new FVector(0f, 0f, 100f));
        scene.Run(1f);
        var spy = (SPawnAttaque)pawn;
        SStickyShocker shocker = scene.Give(new SStickyShocker(), spy);
        shocker.GiveTo(spy);
        spy.WeaponLoaded = 2;
        spy.EnergyLevel = 100f;
        shocker.eventFire(1f);
        Assert.Equal(85f, spy.EnergyLevel);
        Assert.Equal(1, shocker.MuzzleCount);
        spy.EnergyLevel = 5f;
        shocker.eventFire(1f);
        Assert.Equal(5f, spy.EnergyLevel);
        Assert.Equal(1, shocker.MuzzleCount);
    }

    [Fact]
    public void AHolsteredShockerDoesNotFire()
    {
        var scene = new CombatScene();
        (_, SBasePawn pawn) = scene.Player(true, new FVector(0f, 0f, 100f));
        var spy = (SPawnAttaque)pawn;
        SStickyShocker shocker = scene.Give(new SStickyShocker(), spy);
        shocker.GiveTo(spy);
        spy.WeaponLoaded = 0;
        spy.EnergyLevel = 100f;
        shocker.eventFire(1f);
        Assert.Equal(100f, spy.EnergyLevel);
    }

    [Fact]
    public void ATorchMalfunctionEndsAfterItsTime()
    {
        var scene = new CombatScene();
        (_, SBasePawn merc) = scene.Player(false, new FVector(0f, 0f, 100f));
        SGadTorche torch = scene.Give(new SGadTorche(), merc);
        torch.GiveTo(merc);
        torch.Malfunction(1f);
        Assert.Equal(new Name("s_Malfunction"), torch.GetStateName());
        scene.Run(1.2f);
        Assert.NotEqual(new Name("s_Malfunction"), torch.GetStateName());
    }

    [Fact]
    public void WithoutAProfileTheMercGetsTheFixedLoadoutAndThePadUpGadgets()
    {
        var scene = new CombatScene();
        (_, SBasePawn merc) = scene.Player(false, new FVector(0f, 0f, 100f));
        Loadouts.UpdateInventory(merc, EGameMode.GM_Extraction);
        Loadouts.AddDefaultGadgets(merc, EGameMode.GM_Extraction);
        Assert.IsType<SWeaponDefense>(merc.MyWeapon);
        var defender = (SPAWNDEFENSE)merc;
        Assert.NotNull(defender.Torche);
        Assert.NotNull(defender.TorcheLaser);
        int gadgets = 0;
        for (SGadget? g = merc.MyGadget; g is not null; g = g.MyGadget)
        {
            gadgets++;
        }

        Assert.Equal(6, gadgets);
    }

    [Fact]
    public void ADisallowedGadgetIsReplacedByTheFirstAllowedOneNotEquipped()
    {
        var team = new STeamInfo();
        team.ClassInventory.Clear();
        for (int i = 0; i < 8; i++)
        {
            team.ClassInventory.Add(new ClassRef("SBase.Option" + i));
        }

        team.bDisallowedInv = 0b1111_1110;
        ClassRef[] equipment = [new("SBase.Option0"), new("SBase.Option1"), new("SBase.Option2"), new("SBase.Option3")];
        Loadouts.ApplyRestrictions(equipment, team);
        Assert.Equal(new ClassRef("SBase.Option4"), equipment[0]);
        Assert.Equal(new ClassRef("SBase.Option1"), equipment[1]);
    }

    [Fact]
    public void AnOwnerCallOnSomeoneElsesActorIsRefused()
    {
        var scene = new CombatScene();
        (SPlayerController pc, SBasePawn merc) = scene.Player(false, new FVector(0f, 0f, 100f));
        (_, SBasePawn other) = scene.Player(false, new FVector(500f, 0f, 100f));
        SWeaponDefense mine = scene.Give(new SWeaponDefense(), merc);
        SWeaponDefense theirs = scene.Give(new SWeaponDefense(), other);
        pc.ReceiveCombatCall(new CombatCall(new Name("ServerSetROF")) { Target = theirs, Int1 = (int)SRateOfFireMode.ROF_Single }, true);
        Assert.Equal(SRateOfFireMode.ROF_Auto, theirs.ROFMode);
        pc.ReceiveCombatCall(new CombatCall(new Name("ServerSetROF")) { Target = mine, Int1 = (int)SRateOfFireMode.ROF_Single }, true);
        Assert.Equal(SRateOfFireMode.ROF_Single, mine.ROFMode);
    }
}
