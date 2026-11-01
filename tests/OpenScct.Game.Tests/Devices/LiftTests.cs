using System;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using Xunit;

namespace OpenScct.Game.Tests.Devices;

/// <summary>Elevator door, call-switch and carriage sequences from spec/devices/lift.md.</summary>
public sealed class LiftTests
{
    [Theory]
    [InlineData(0, 2)]
    [InlineData(2, 0)]
    [InlineData(1, 2)]
    public void CallClosesDoorsCarriesCabinDoorsAndRearmsOnlyOtherFloors(int initial, int destination)
    {
        DeviceScene scene = DeviceScene.Create();
        var lift = scene.Add(new SLift { KeyNum = (byte)initial, MoveTime = .25f }, tag: "lift");
        for (int floor = 0; floor < 3; floor++)
        {
            lift.KeyPos[floor] = new FVector(0, 0, floor * 200);
            var door = scene.Add(new Mover
            {
                InitialState = "TriggerToggle", KeyNum = (byte)(floor == initial ? 0 : 1), MoveTime = .1f,
            }, new FVector(100, 0, floor * 200), tag: "landing" + floor);
            door.BasePos = door.Location;
            door.KeyPos[1] = new FVector(0, 60, 0);
            var button = scene.Add(new SInterrupteur { bTriggerOnceOnly = false }, tag: "button" + floor);
            lift.DoorsAndSwitches.Add(new FDoorsAndSwitchesForOneKey { Doors = [door], Switches = [button] });
        }

        var cabinDoor = scene.Add(new SLiftDoor { MoveTime = .1f }, new FVector(0, 50, initial * 200), tag: "cabin");
        cabinDoor.BasePos = cabinDoor.Location;
        cabinDoor.KeyPos[1] = new FVector(0, 60, 0);
        lift.LiftDoors.Add(cabinDoor);
        scene.Begin();
        Assert.Same(lift, cabinDoor.Base);
        AssertSwitches(lift, initial);

        SInterrupteur buttonToUse = lift.DoorsAndSwitches[destination].Switches[0]!;
        buttonToUse.TriggerInteraction!.HandleEventInfo();
        Assert.True(lift.IsInState("PrepareGo"));
        foreach (FDoorsAndSwitchesForOneKey floor in lift.DoorsAndSwitches)
        {
            Assert.False(floor.Switches[0]!.InteractionObj!.bUsable);
        }

        scene.StepUntil(() => lift.IsInState("Go"));
        Assert.True(lift.IsInState("Go"));
        foreach (FDoorsAndSwitchesForOneKey floor in lift.DoorsAndSwitches)
        {
            Assert.Equal(1, floor.Doors[0]!.KeyNum);
            Assert.Equal(1, floor.Doors[0]!.PrevKeyNum);
        }

        Assert.Equal(1, cabinDoor.KeyNum);
        scene.StepUntil(() => ReferenceEquals(lift.StateNode, ActorState.None));
        Assert.Same(ActorState.None, lift.StateNode);
        Assert.Equal(destination, lift.KeyNum);
        Assert.Equal(lift.BasePos + lift.KeyPos[destination], lift.Location);
        Assert.Equal(new FVector(0, 50, destination * 200), cabinDoor.Location);
        Assert.Equal(0, cabinDoor.KeyNum);
        AssertSwitches(lift, destination);
        for (int floor = 0; floor < lift.DoorsAndSwitches.Count; floor++)
        {
            Assert.Equal(floor == destination ? 0 : 1, lift.DoorsAndSwitches[floor].Doors[0]!.KeyNum);
        }
    }

    private static void AssertSwitches(SLift lift, int current)
    {
        for (int floor = 0; floor < lift.DoorsAndSwitches.Count; floor++)
        {
            SInterrupteur button = lift.DoorsAndSwitches[floor].Switches[0]!;
            Assert.Equal(lift.Tag, button.Event);
            Assert.Equal(floor != current, button.InteractionObj!.bUsable);
        }
    }

    [Fact]
    public void RotatingMoverCompletesAndReplicatesItsAuthoredRotation()
    {
        DeviceScene scene = DeviceScene.Create();
        var mover = scene.Add(new MoverRotating { InitialState = "TriggerToggle", MoveTime = .2f },
            tag: "turntable");
        mover.KeyRot[1] = new FRotator(0, 32768, 0);
        var sender = scene.Add(new Sender());
        scene.Begin();
        mover.eventTrigger(sender, null);
        scene.StepUntil(() => mover.KeyNum == 1 && mover.PrevKeyNum == 1);
        Assert.Equal(mover.BaseRot + mover.KeyRot[1], mover.Rotation);
        Assert.Equal(mover.Rotation, mover.RealRotation);
        Assert.Equal(0, mover.ClientUpdate);

        mover.eventTrigger(sender, null);
        scene.StepUntil(() => mover.KeyNum == 0 && mover.PrevKeyNum == 0);
        Assert.Equal(mover.BaseRot, mover.Rotation);
        Assert.Equal(mover.Rotation, mover.RealRotation);
        Assert.Equal(0, mover.ClientUpdate);
    }
}
