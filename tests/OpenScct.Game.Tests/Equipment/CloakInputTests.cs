using System.Linq;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Combat;
using OpenScct.Game.Equipment;
using OpenScct.Game.SBase;
using OpenScct.Game.Tests.Combat;
using Xunit;

namespace OpenScct.Game.Tests.Equipment;

public sealed class CloakInputTests
{
    [Fact]
    public void EverySpyHasCloakWithoutInventoryAndGadgetInputDoesNotActivateIt()
    {
        var (_, pc, pawn) = Player();
        Assert.Null(pawn.MyGadget);
        pc.bHoldBreath = 1;
        pc.bAltFire = 1;
        pc.EquipmentPlayerTick(0);
        Assert.Equal(0, pawn.DesiredGlassEffect);
        Input(pc, true);
        Assert.Equal(255, pawn.DesiredGlassEffect);
        Assert.Null(pawn.MyGadget);
        Assert.Null(pawn.SelectedGadget);
    }

    [Fact]
    public void DefaultKitKeepsFourRealGadgetsAndCloakDoesNotChangeSelection()
    {
        var (_, pc, pawn) = Player();
        Loadouts.UpdateInventory(pawn, EGameMode.GM_Extraction);
        PawnInventory.SelectGagdet(pawn, 0);
        var selected = pawn.SelectedGadget;
        int count = 0;
        for (var g = pawn.MyGadget; g is not null; g = g.MyGadget)
        {
            Assert.IsNotType<SGadCloak>(g);
            count++;
        }
        Assert.Equal(4, count);
        Input(pc, true);
        Assert.Equal(255, pawn.DesiredGlassEffect);
        Assert.Same(selected, pawn.SelectedGadget);
    }

    [Fact]
    public void LegacyCloakCannotBeGrantedOrSelectedAndOldLoadoutsReplaceIt()
    {
        var (_, _, pawn) = Player();
        pawn.GearGrantUntil = 100;
        pawn.GearGrantGadgets = 4;
        Loadouts.ServerSpawnInventory(pawn, new ClassRef(SpyCloak.LegacyGadgetClass));
        Assert.Null(pawn.MyGadget);
        Assert.Equal(4, pawn.GearGrantGadgets);
        Assert.False(PawnInventory.IsSelectable(new SGadCloak()));
        var team = new STeamAttaquant { bDisallowedInv = 255 };
        ClassRef[] kit = [new(SpyCloak.LegacyGadgetClass), .. Loadouts.SpyFixedGadgets.Take(3)];
        Loadouts.ApplyRestrictions(kit, team);
        Assert.DoesNotContain(kit, c => SpyCloak.IsLegacyGadget(c.Path));
        Assert.All(kit, c => Assert.False(c.IsNone));
        Assert.Equal(4, kit.Distinct().Count());
    }

    [Fact]
    public void MercenaryCloakInputHasNoEffect()
    {
        var scene = new CombatScene();
        var (pc, pawn) = scene.Player(false, default);
        Input(pc, true);
        Assert.Equal(0, pawn.DesiredGlassEffect);
    }

    private static (CombatScene Scene, SPlayerController PC, SPawnAttaque Pawn) Player(bool hold = false)
    {
        var scene = new CombatScene();
        var (pc, pawn) = scene.Player(true, new FVector(0, 0, 100));
        var spy = (SPawnAttaque)pawn;
        spy.WeaponLoaded = 0;
        spy.EnergyLevel = spy.EnergyLevelMax;
        pc.SpyCloakHold = hold;
        return (scene, pc, spy);
    }

    private static void Input(SPlayerController pc, bool down)
    {
        pc.bCloak = down ? (byte)1 : (byte)0;
        pc.EquipmentPlayerTick(0);
    }

    [Fact]
    public void ToggleActsOnFirstPressAtTimeZeroAndSecondPressCancelsFadeIn()
    {
        var (_, pc, pawn) = Player();
        Input(pc, true);
        Assert.Equal(255, pawn.DesiredGlassEffect);
        Assert.Equal(0, pawn.GlassEffect);
        Input(pc, true);
        Input(pc, false);
        Assert.Equal(255, pawn.DesiredGlassEffect);
        Input(pc, true);
        Assert.Equal(0, pawn.DesiredGlassEffect);
        Input(pc, false);
        Assert.Equal(0, pawn.DesiredGlassEffect);
    }

    [Fact]
    public void HoldActivatesImmediatelyAndShortReleaseCancelsEvenAfterSelectionChanges()
    {
        var (_, pc, pawn) = Player(hold: true);
        Input(pc, true);
        Assert.Equal(255, pawn.DesiredGlassEffect);
        pawn.SelectedGadget = new SGadSmokeGrenade();
        Input(pc, false);
        Assert.Equal(0, pawn.DesiredGlassEffect);
    }

    [Fact]
    public void HoldingAfterForcedDecloakDoesNotReactivateUntilNextPress()
    {
        var (_, pc, pawn) = Player(hold: true);
        Input(pc, true);
        SpyCloak.SetActive(pawn, false);
        Input(pc, true);
        Assert.Equal(0, pawn.DesiredGlassEffect);
        Input(pc, false);
        Input(pc, true);
        Assert.Equal(255, pawn.DesiredGlassEffect);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ActivationStillRequiresFullEnergyAndHolsteredWeapon(bool hold)
    {
        var (_, pc, pawn) = Player(hold);
        pawn.EnergyLevel = pawn.EnergyLevelMax / 2;
        Input(pc, true);
        Assert.Equal(0, pawn.DesiredGlassEffect);
        Input(pc, false);
        pawn.EnergyLevel = pawn.EnergyLevelMax;
        pawn.WeaponLoaded = 2;
        Input(pc, true);
        Assert.Equal(0, pawn.DesiredGlassEffect);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OnlineReleaseOrSecondPressSendsOffBeforeActivationReply(bool hold)
    {
        var (scene, pc, pawn) = Player(hold);
        var net = new RecordingCombatNet();
        scene.Level.SetService<ICombatNet>(net);
        pawn.Role = ENetRole.ROLE_AutonomousProxy;
        pc.Role = ENetRole.ROLE_AutonomousProxy;
        Input(pc, true);
        Input(pc, false);
        if (!hold) Input(pc, true);
        Assert.Equal(new[] { 255, 0 }, net.ToServer
            .Where(c => c.Call.Function == new Name("SetDesiredGlassValue"))
            .Select(c => c.Call.Int1));
    }
}
