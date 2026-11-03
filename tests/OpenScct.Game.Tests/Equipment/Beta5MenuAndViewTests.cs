using System;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using Xunit;

namespace OpenScct.Game.Tests.Equipment;

public sealed class Beta5MenuAndViewTests
{
    [Theory]
    [InlineData("interaction", true)]
    [InlineData("interaction", false)]
    [InlineData("mine", true)]
    [InlineData("mine", false)]
    [InlineData("camera", true)]
    [InlineData("camera", false)]
    public void HoldingMovementWhenOpeningAMenuDoesNotMoveItsSelection(string menu, bool up)
    {
        var scene = GadgetKit.Scene();
        var (pc, pawn) = scene.Player(menu != "mine", default);
        pc.bUsing = 1;
        pc.bAltFire = 1;
        pc.BT_Up = (byte)(up ? 1 : 0);
        pc.BT_Down = (byte)(up ? 0 : 1);
        Action<SPlayerController, float> move;
        Func<int> selected;
        if (menu == "mine")
        {
            var launcher = GadgetKit.Gadget(scene, pawn, new SGadMultiMinesLauncher());
            pc.GotoState(new Name("s_PutMineOnWall_InteractionMenu"));
            move = SPlayerController.s_PutMineOnWall_InteractionMenu.Instance.PlayerMove;
            selected = () => launcher.iCurrentMineType;
        }
        else if (menu == "camera")
        {
            var camera = GadgetKit.Gadget(scene, pawn, new SGadStickyCam());
            pc.GotoState(new Name("s_StickyCam_MenuInteraction"));
            move = SPlayerController.s_StickyCam_MenuInteraction.Instance.PlayerMove;
            selected = () => camera.iCurrentSelectedMenu;
        }
        else
        {
            var manager = scene.Level.SpawnActor(new SInteractionManager(), default, default, pc)!;
            pc.IManager = manager;
            // All three entries are usable; selection behavior does not depend on an objective's state.
            manager.NbObjects = 3;
            for (int i = 0; i < 3; i++) manager.Interactions[i] = scene.Level.SpawnActor(new SLeaveInteraction(), default, default, manager);
            pc.GotoState(new Name("s_MenuInteraction"));
            move = SPlayerController.s_MenuInteraction.Instance.PlayerMove;
            selected = () => manager.SelectedInteractions;
        }

        int initial = selected();
        move(pc, Sim.DeltaTime);
        Assert.Equal(initial, selected());
        pc.BT_Up = pc.BT_Down = 0;
        move(pc, Sim.DeltaTime);
        pc.BT_Up = (byte)(up ? 1 : 0);
        pc.BT_Down = (byte)(up ? 0 : 1);
        move(pc, Sim.DeltaTime);
        Assert.NotEqual(initial, selected());
        int afterPress = selected();
        move(pc, Sim.DeltaTime);
        Assert.Equal(afterPress, selected());
        if (up) pc.eventWheelDown(); else pc.eventWheelUp();
        Assert.Equal(initial, selected());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void NormalSpyViewAndGrabViewClearResidualRoll(bool holding, bool armed)
    {
        var scene = GadgetKit.Scene();
        var (pc, pawn) = scene.Player(true, default);
        pawn.WeaponLoaded = (byte)(armed ? 2 : 0);
        pc.Rotation = new FRotator(1000, 2000, 5000);
        if (holding) SPlayerController.s_GrabbingPlayer.Instance.UpdateRotation(pc, Sim.DeltaTime, 1f);
        else pc.UpdateCamRotation(Sim.DeltaTime);
        Assert.Equal(0, pc.Rotation.Roll);
        Assert.Equal(1000, pc.Rotation.Pitch);
        Assert.Equal(2000, pc.Rotation.Yaw);
    }
}
