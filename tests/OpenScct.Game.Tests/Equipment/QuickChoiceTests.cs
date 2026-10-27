using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Equipment;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using OpenScct.Game.Tests.Movement;
using Xunit;

namespace OpenScct.Game.Tests.Equipment;

/// <summary>
/// Gadget selection and the spy's quick choice on the full controller (the movement rig with the equipment ports):
/// the slot keys, the wheel, and the quick throw of a grenade from the unarmed walk (spec/equipment/grenade-throw.md
/// rules 5 and 6, inventory-selection.md).
/// </summary>
public sealed class QuickChoiceTests
{
    private static readonly PlayerInputFrame Still = new();

    private static PawnRig Kit(PawnTeam team, bool crouched = false)
    {
        PawnRig rig = MovementTestKit.Rig(team, crouched: crouched);
        CombatEquipment.Install(rig.Level);
        Loadouts.UpdateInventory(rig.Pawn, EGameMode.GM_Neutralization);

        // A quick throw is refused in the first two seconds of play (the last launch date starts at 0).
        rig.Hold(Still, (int)(K.QuickLaunchCooldown * Sim.Hz) + 10);
        return rig;
    }

    private static List<string> Slots(SBasePawn pawn)
    {
        var names = new List<string>();
        for (SGadget? g = pawn.MyGadget; g is not null && names.Count < 10; g = g.MyGadget)
        {
            if (PawnInventory.IsSelectable(g))
            {
                names.Add(g.GetType().Name);
            }
        }

        return names;
    }

    private static int Count<T>(PawnRig rig)
        where T : Actor
    {
        int n = 0;
        foreach (T a in rig.Level.AllActors<T>())
        {
            if (!a.bDeleteMe)
            {
                n++;
            }
        }

        return n;
    }

    [Fact]
    public void ASpyGetsTheProfileKitInSlotOrderWithTheShocker()
    {
        PawnRig rig = Kit(PawnTeam.Spy);
        Assert.Equal(["SGadSmokeGrenade", "SGadChaffeGrenade", "SGadStickyCam", "SGadHeartBeatSensor"], Slots(rig.Pawn));
        Assert.IsType<SStickyShocker>(rig.Pawn.MyWeapon);
    }

    [Fact]
    public void AMercGetsTheProfileKitInSlotOrderWithTheRifle()
    {
        PawnRig rig = Kit(PawnTeam.Merc);
        Assert.Equal(["SGadFragGrenade", "SGadMultiMinesLauncher", "SGadCamNetwork", "SGadGasMask"], Slots(rig.Pawn));
        Assert.IsType<SWeaponDefense>(rig.Pawn.MyWeapon);
    }

    [Fact]
    public void TheSlotKeysAndTheWheelSelectGadgets()
    {
        PawnRig rig = Kit(PawnTeam.Spy);
        rig.Controller.SelectGadget(2);
        Assert.IsType<SGadStickyCam>(rig.Pawn.SelectedGadget);
        rig.Controller.SelectGadget(0);
        Assert.IsType<SGadSmokeGrenade>(rig.Pawn.SelectedGadget);
        rig.Controller.SelectGadget(7);
        Assert.IsType<SGadSmokeGrenade>(rig.Pawn.SelectedGadget);
        PawnInventory.NextGadget(rig.Pawn);
        Assert.IsType<SGadChaffeGrenade>(rig.Pawn.SelectedGadget);
        PawnInventory.PrevGadget(rig.Pawn);
        PawnInventory.PrevGadget(rig.Pawn);
        Assert.IsType<SGadHeartBeatSensor>(rig.Pawn.SelectedGadget);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheQuickChoiceKeyThrowsTheSlotsGrenadeFromTheUnarmedWalk(bool crouched)
    {
        PawnRig rig = Kit(PawnTeam.Spy, crouched);
        var seen = new List<Name>();
        rig.Controller.eventGadgetQuickChoice(0);
        Assert.IsType<SGadSmokeGrenade>(rig.Pawn.SelectedGadget);
        bool thrown = false;
        for (int i = 0; i < 300 && !thrown; i++)
        {
            rig.Step(Still);
            Name s = rig.Controller.GetStateName();
            if (seen.Count == 0 || seen[^1] != s)
            {
                seen.Add(s);
            }

            thrown = Count<SProjSmoke>(rig) > 0;
        }

        Assert.True(thrown, "states: " + string.Join(", ", seen));
        Assert.True(rig.Controller.fDateLaunchedLastQuickGadget > 0f);
    }

    [Fact]
    public void NoQuickThrowInTheFirstTwoSecondsOfPlay()
    {
        PawnRig rig = MovementTestKit.Rig(PawnTeam.Spy);
        CombatEquipment.Install(rig.Level);
        Loadouts.UpdateInventory(rig.Pawn, EGameMode.GM_Neutralization);
        Assert.True(rig.Level.TimeSeconds < K.QuickLaunchCooldown);
        rig.Controller.eventGadgetQuickChoice(0);
        Assert.False(rig.Controller.bQuickGadgetQuery);
    }

    [Fact]
    public void ASecondQuickThrowWaitsTwoSeconds()
    {
        PawnRig rig = Kit(PawnTeam.Spy);
        rig.Controller.eventGadgetQuickChoice(0);
        for (int i = 0; i < 300 && Count<SProjSmoke>(rig) == 0; i++)
        {
            rig.Step(Still);
        }

        Assert.Equal(1, Count<SProjSmoke>(rig));
        rig.Controller.eventGadgetQuickChoice(1);
        rig.Hold(Still, 30);
        Assert.Equal(0, Count<SProjChaffe>(rig));
        rig.Hold(Still, (int)(K.QuickLaunchCooldown * Sim.Hz));
        rig.Controller.eventGadgetQuickChoice(1);
        for (int i = 0; i < 300 && Count<SProjChaffe>(rig) == 0; i++)
        {
            rig.Step(Still);
        }

        Assert.Equal(1, Count<SProjChaffe>(rig));
    }

    [Fact]
    public void AMercHasNoQuickThrow()
    {
        PawnRig rig = Kit(PawnTeam.Merc);
        rig.Controller.eventGadgetQuickChoice(0);
        rig.Hold(Still, 120);
        Assert.Equal(0, Count<SProjFrag>(rig));
    }

    [Fact]
    public void AMercSwitchesTheGasMaskWithAltFire()
    {
        PawnRig rig = Kit(PawnTeam.Merc);
        rig.Controller.SelectGadget(3);
        Assert.IsType<SGadGasMask>(rig.Pawn.SelectedGadget);
        rig.Hold(Still, 20);
        rig.Controller.eventAltFire(0f);
        Assert.True(rig.Pawn.bGasMask, $"state {rig.Controller.GetStateName()} transit={rig.Pawn.bWeaponTransit} pad={rig.Controller.iDontUpdatePadInput} inv={rig.Controller.bIsInInventoryMode} canFire={rig.Controller.CheckIfCanFire()}");
        rig.Hold(Still, 20);
        rig.Controller.eventAltFire(0f);
        Assert.False(rig.Pawn.bGasMask);
    }
}
