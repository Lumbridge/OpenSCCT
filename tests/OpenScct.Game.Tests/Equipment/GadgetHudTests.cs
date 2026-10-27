using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Equipment;
using OpenScct.Game.SBase;
using OpenScct.Game.Tests.Combat;
using Xunit;

namespace OpenScct.Game.Tests.Equipment;

/// <summary>The gadget HUD views: the SHud view states and what the client pages read (spec/equipment/gadget-hud.md).</summary>
public sealed class GadgetHudTests
{
    private static (CombatScene Scene, SPlayerController PC, SPawnAttaque Spy, SGadStickyCam Gad) StickySpy()
    {
        CombatScene scene = GadgetKit.Scene();
        GadgetKit.Walls(scene, GadgetKit.WallAt(300f));
        (SPlayerController pc, SBasePawn pawn) = scene.Player(true, new FVector(0f, 0f, 100f));
        pc.myHUD = scene.Level.SpawnActor(new SHud(), default, default);
        var spy = (SPawnAttaque)pawn;
        GadgetKit.Shocker(scene, spy);
        SGadStickyCam gad = GadgetKit.Gadget(scene, spy, new SGadStickyCam());
        pc.MyDesiredRotation = default;
        scene.Run(1.5f);
        return (scene, pc, spy, gad);
    }

    private static (CombatScene Scene, SPlayerController PC, SBasePawn Merc, SGadCamNetwork Gad, SCamNetwork A, SCamNetwork B) Network()
    {
        CombatScene scene = GadgetKit.Scene();
        scene.Level.BeginLoad();
        SCamNetwork a = scene.Level.SpawnActor(new SCamNetwork(), new FVector(500f, 0f, 300f), default)!;
        SCamNetwork b = scene.Level.SpawnActor(new SCamNetwork(), new FVector(-500f, 0f, 300f), default)!;
        scene.Level.EndLoad();
        (SPlayerController pc, SBasePawn merc) = GadgetKit.Local(scene, false, new FVector(0f, 0f, 100f));
        pc.myHUD = scene.Level.SpawnActor(new SHud(), default, default);
        merc.SetPhysics(EPhysics.PHYS_Walking);
        a.bFirstCam = true;
        a.NextCam = b;
        a.PrevCam = b;
        b.NextCam = a;
        b.PrevCam = a;
        SGadCamNetwork gad = GadgetKit.Gadget(scene, merc, new SGadCamNetwork());
        scene.Run(1f);
        return (scene, pc, merc, gad, a, b);
    }

    // ---- the SHud view states and the page switch ---------------------------------------------------------------

    [Fact]
    public void FiringACamPutsTheSpysHudInTheStickyViewAndShowsTheStickyPage()
    {
        (_, SPlayerController pc, _, SGadStickyCam gad) = StickySpy();
        Assert.Equal(GadgetView.None, GadgetHudRules.ViewOf(pc));
        gad.Use(1f);
        var hud = (SHud)pc.myHUD!;
        Assert.True(hud.IsInSticky());
        Assert.False(hud.IsInCamNetwork());
        Assert.Equal(GadgetView.StickyCam, GadgetHudRules.ViewOf(pc));
    }

    [Fact]
    public void TheCameraNetworkPutsTheMercsHudInItsViewAndLeavingStoresThePreviousState()
    {
        (_, SPlayerController pc, _, SGadCamNetwork gad, _, _) = Network();
        gad.Use(1f);
        var hud = (SHud)pc.myHUD!;
        Assert.True(hud.IsInCamNetwork());
        Assert.Equal(GadgetView.CamNetwork, GadgetHudRules.ViewOf(pc));
        pc.UseCamNetwork(0, true);
        pc.eventChangeState(new Name("s_PlayerWalkingSt0H"));
        Assert.False(hud.IsInCamNetwork());
        Assert.Equal(GadgetView.None, GadgetHudRules.ViewOf(pc));
        Assert.Equal(GadgetStates.s_Hud_UseObject_CamNetwork, hud.PreviousState);
    }

    [Fact]
    public void ASpyHudInTheCameraStateShowsNoMercPage()
    {
        (_, SPlayerController pc, _, _) = StickySpy();
        ((SHud)pc.myHUD!).GotoState(GadgetStates.s_Hud_UseObject_CamNetwork);
        Assert.Equal(GadgetView.None, GadgetHudRules.ViewOf(pc));
    }

    // ---- the camera network page ------------------------------------------------------------------------------

    [Fact]
    public void TheCameraPageShowsTheCameraNameOrThatItIsUnderControl()
    {
        Assert.SkipUnless(UcText.HasText, "the game text (shared/classes.json) is not in .content");
        (CombatScene scene, SPlayerController pc, SBasePawn merc, SGadCamNetwork gad, SCamNetwork a, _) = Network();
        Assert.Null(GadgetHudRules.CamNetworkName(pc));
        gad.Use(1f);
        a.CamName = "Roof";
        Assert.Equal("Roof", GadgetHudRules.CamNetworkName(pc));
        a.CamName = "";
        Assert.Null(GadgetHudRules.CamNetworkName(pc));
        (_, SBasePawn other) = scene.Player(false, new FVector(0f, 300f, 100f));
        a.aUsedByPawn = other;
        Assert.Equal("Camera already under control", GadgetHudRules.CamNetworkName(pc));
        Assert.Same(a, merc.UsedObject);
    }

    [Fact]
    public void TheAlertShowsWhileAnObjectiveToDrawHasATriggerTime()
    {
        var hud = new SHud();
        Assert.False(GadgetHudRules.CamNetworkAlert(hud));
        var objective = new SObjective();
        hud.ActorToDraw.Add(objective);
        Assert.False(GadgetHudRules.CamNetworkAlert(hud));
        objective.MinTriggerTime = 12f;
        Assert.True(GadgetHudRules.CamNetworkAlert(hud));
    }

    // ---- the sticky cam menu ----------------------------------------------------------------------------------

    [Fact]
    public void TheStickyMenuShowsItsLinesWithTheSelectionAndGreysWhatCannotBeUsed()
    {
        Assert.SkipUnless(UcText.HasText, "the game text (shared/classes.json) is not in .content");
        (_, SPlayerController pc, SPawnAttaque spy, SGadStickyCam gad) = StickySpy();
        Assert.Null(GadgetHudRules.StickyCamMenu(pc));
        pc.GotoStickyInteractionMenu();
        gad.iCanUse[0] = 1;
        gad.iCanUse[1] = 0;
        gad.iCurrentSelectedMenu = 0;
        GadgetMenu menu = Assert.IsType<GadgetMenu>(GadgetHudRules.StickyCamMenu(pc));
        Assert.Equal(["New Cam", "Previous Cam", "Exit"], [menu.Lines[0].Text, menu.Lines[1].Text, menu.Lines[2].Text]);
        Assert.Equal(GadgetMenuLineLook.Selected, menu.Lines[0].Look);
        Assert.Equal(GadgetMenuLineLook.Untouched, menu.Lines[1].Look);
        Assert.Equal(GadgetMenuLineLook.Normal, menu.Lines[2].Look);

        // With a cam out, line 1 is driven; a placement that cannot be made greys line 0, never Exit.
        spy.UsedObject = new SStickyCam();
        gad.iCanUse[0] = 0;
        gad.iCanUse[1] = 1;
        gad.iCurrentSelectedMenu = 2;
        menu = GadgetHudRules.StickyCamMenu(pc)!;
        Assert.Equal(GadgetMenuLineLook.Unavailable, menu.Lines[0].Look);
        Assert.Equal(GadgetMenuLineLook.Normal, menu.Lines[1].Look);
        Assert.Equal(GadgetMenuLineLook.Selected, menu.Lines[2].Look);
    }

    [Fact]
    public void TheStickyMenuNeedsTheStickyCamSelected()
    {
        (CombatScene scene, SPlayerController pc, SPawnAttaque spy, _) = StickySpy();
        pc.GotoStickyInteractionMenu();
        spy.SelectedGadget = GadgetKit.Gadget(scene, spy, new SGadHeartBeatSensor());
        Assert.Null(GadgetHudRules.StickyCamMenu(pc));
    }

    // ---- the mine menu ----------------------------------------------------------------------------------------

    [Fact]
    public void TheMineMenuListsFiveEntriesWithTheSelectedTypeAndGreysTheUnavailableOnes()
    {
        Assert.SkipUnless(UcText.HasText, "the game text (shared/classes.json) is not in .content");
        CombatScene scene = GadgetKit.Scene();
        (SPlayerController pc, SBasePawn merc) = GadgetKit.Local(scene, false, new FVector(0f, 0f, 100f));
        SGadMultiMinesLauncher gad = GadgetKit.Gadget(scene, merc, new SGadMultiMinesLauncher());
        Assert.Null(GadgetHudRules.MineMenu(pc));
        pc.eventChangeState(GadgetStates.s_PutMineOnWall_InteractionMenu);
        gad.iCurrentMineType = 1;
        gad.CanUse[0] = 1;
        gad.CanUse[1] = 1;
        gad.CanUse[2] = 0;
        Assert.Equal(GadgetStates.s_PutMineOnWall_InteractionMenu, pc.GetStateName());
        GadgetMenu? menu = GadgetHudRules.MineMenu(pc);
        Assert.NotNull(menu);
        Assert.Equal(5, menu.Lines.Count);
        Assert.Equal("Laser Mine", menu.Lines[1].Text);
        Assert.Equal(GadgetMenuLineLook.Selected, menu.Lines[1].Look);
        Assert.Equal(GadgetMenuLineLook.Normal, menu.Lines[0].Look);
        Assert.Equal(GadgetMenuLineLook.Unavailable, menu.Lines[2].Look);
        Assert.Equal(GadgetMenuLineLook.Normal, menu.Lines[4].Look);
    }

    // ---- the gas mask overlay ---------------------------------------------------------------------------------

    [Fact]
    public void TheMaskFrameShowsOnSelectionWithTheAirGauge()
    {
        CombatScene scene = GadgetKit.Scene();
        (SPlayerController pc, SBasePawn merc) = GadgetKit.Local(scene, false, new FVector(0f, 0f, 100f));
        Assert.False(GadgetHudRules.GasMask(pc).Shown);
        SGadGasMask mask = GadgetKit.Gadget(scene, merc, new SGadGasMask());
        GasMaskView v = GadgetHudRules.GasMask(pc);
        Assert.True(v.Shown);
        Assert.False(v.Worn);
        Assert.Equal(30, v.AirSeconds);
        Assert.Equal(0, v.GaugeFrame);
        Assert.False(v.LowAir);

        mask.MaskDuration = 14.9f;
        merc.bGasMask = true;
        v = GadgetHudRules.GasMask(pc);
        Assert.True(v.Worn);
        Assert.Equal(14, v.AirSeconds);
        Assert.Equal(32, v.GaugeFrame);
        Assert.True(v.LowAir);

        mask.MaskDuration = 0f;
        Assert.Equal(60, GadgetHudRules.GasMask(pc).GaugeFrame);
    }

    [Fact]
    public void TheMaskFrameHidesWhileTheMercUsesAnObject()
    {
        CombatScene scene = GadgetKit.Scene();
        (SPlayerController pc, SBasePawn merc) = GadgetKit.Local(scene, false, new FVector(0f, 0f, 100f));
        GadgetKit.Gadget(scene, merc, new SGadGasMask());
        merc.UsedObject = new SCamNetwork();
        Assert.False(GadgetHudRules.GasMask(pc).Shown);
    }

    // ---- the heart sensor cone --------------------------------------------------------------------------------

    [Fact]
    public void TheHeartConeFollowsTheSensorState()
    {
        CombatScene scene = GadgetKit.Scene();
        (SPlayerController pc, SBasePawn pawn) = scene.Player(true, new FVector(0f, 0f, 100f));
        var spy = (SPawnAttaque)pawn;
        Assert.Equal(HeartCone.Hidden, GadgetHudRules.Heart(pc));
        GadgetKit.Shocker(scene, spy, Drawn: false);
        SGadHeartBeatSensor sensor = GadgetKit.Gadget(scene, spy, new SGadHeartBeatSensor());
        scene.Run(0.2f);
        Assert.Equal(HeartCone.Idle, GadgetHudRules.Heart(pc));

        spy.WeaponLoaded = 2;
        scene.Run(0.2f);
        Assert.Equal(2, sensor.StateOfDetect);
        Assert.Equal(HeartCone.Scanning, GadgetHudRules.Heart(pc));

        scene.Player(false, new FVector(1000f, 0f, 100f));
        scene.Run(0.2f);
        Assert.Equal(1, sensor.StateOfDetect);
        Assert.Equal(HeartCone.Detected, GadgetHudRules.Heart(pc));
    }

    [Fact]
    public void TheDetectedSoundPlaysAtMostEveryOneAndAHalfSeconds()
    {
        Assert.False(GadgetHudRules.HeartDetectedSoundDue(11.5f, 10f));
        Assert.True(GadgetHudRules.HeartDetectedSoundDue(11.51f, 10f));
    }
}
