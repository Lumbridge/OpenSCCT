using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Equipment;
using OpenScct.Game.SBase;
using OpenScct.Game.Tests.Combat;
using Xunit;

namespace OpenScct.Game.Tests.Equipment;

/// <summary>
/// The host's starting settings (spec/equipment/loadouts.md rule 7) and the player's HUD from the login
/// (SPlayerController.ClientSetHUD, spec/equipment/gadget-hud.md).
/// </summary>
public sealed class HostSettingsTests
{
    [Fact]
    public void InitGivesFourGadgetsASideWithEverythingAllowed()
    {
        USPersistentData pd = USPersistentData.CreateInitialized();
        Assert.Equal(4, pd.b_SPY_NBGadget);
        Assert.Equal(4, pd.b_MERC_NBGadget);
        Assert.Equal(255, pd.b_SPY_GadgetRestriction);
        Assert.Equal(255, pd.b_MERC_GadgetRestriction);
        Assert.True(pd.allowVoiceChatInLobby);
        Assert.False(pd.friendMustBlink);
    }

    [Fact]
    public void InitTurnsTheAbilitiesOnButTheLethalDropsAndTheCinematicCamera()
    {
        USPersistentData pd = USPersistentData.CreateInitialized();
        Assert.True(pd.bAvlb_SPY_NightVision && pd.bAvlb_SPY_ThermalVision && pd.bAvlb_SPY_Grab && pd.bAvlb_SPY_Elbow);
        Assert.True(pd.bAvlb_MERC_Elbow && pd.bAvlb_MERC_EMFVision && pd.bAvlb_MERC_MotiontrackingVision);
        Assert.True(pd.bAvlb_MERC_Torche && pd.bAvlb_MERC_Laser && pd.bAvlb_MERC_Berserk && pd.bAvlb_MERC_Charge);
        Assert.False(pd.bAvlb_SPY_LeathalDropTech || pd.bAvlb_MERC_LeathalDropTech || pd.bAvlb_CinematicCamera);
    }

    [Fact]
    public void ANewSettingsObjectKeepsTheClassDefaultsUntilInit()
    {
        var pd = new USPersistentData();
        Assert.Equal(0, pd.b_SPY_NBGadget);
        pd.Init();
        Assert.Equal(4, pd.b_SPY_NBGadget);
    }

    [Fact]
    public void RulesWithoutHostSettingsUseInitializedOnes()
    {
        var game = new SGameInfo();
        Assert.Equal(4, game.PersistentData.b_SPY_NBGadget);
        Assert.Equal(4, game.PersistentData.b_MERC_NBGadget);
    }

    [Fact]
    public void ClientSetHudGivesThePlayerAnSHudOnce()
    {
        CombatScene scene = GadgetKit.Scene();
        (SPlayerController pc, _) = scene.Player(true, new FVector(0f, 0f, 100f));
        pc.myHUD = null;
        pc.ClientSetHUD(new ClassRef("SBase.SHud"), ClassRef.None);
        SHud hud = Assert.IsType<SHud>(pc.myHUD);
        Assert.Same(pc, hud.PlayerOwner);
        Assert.Same(pc, hud.Owner);
        hud.LastSwitchGadget = 3;
        pc.ClientSetHUD(new ClassRef("SBase.SHud"), ClassRef.None);
        Assert.Same(hud, pc.myHUD);
        Assert.Equal(0, hud.LastSwitchGadget);
    }
}
