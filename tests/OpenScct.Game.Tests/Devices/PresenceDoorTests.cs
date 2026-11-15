using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using Xunit;

namespace OpenScct.Game.Tests.Devices;

public sealed class PresenceDoorTests
{
    private static (DeviceScene Scene, SPresenceTrigger Detector, Mover Door, EventProbe Opened) MakeDoor(
        string state = "TriggerOpenTimed", bool relay = false, int keys = 2)
    {
        DeviceScene scene = DeviceScene.Create(ENetMode.NM_DedicatedServer);
        var detector = scene.Add(new SPresenceTrigger { Event = relay ? "relay" : "door" });
        var door = scene.Add(new Mover
        {
            InitialState = state, MoveTime = 1f, StayOpenTime = 2f, DelayTime = 0.25f,
            DelayTimeLastKey = 0.5f, OpenedEvent = "opened", NumKeys = (byte)keys,
        }, tag: "door");
        for (int i = 1; i < keys; i++) door.KeyPos[i] = V.At(0, 0, 100 * i);
        if (relay)
        {
            var magic = scene.Add(new SMagicEvent(), tag: "relay");
            var group = new FEventGroup();
            group.EventGroup.Add(new FSuperEvent { Event = "door", Type = EEventType.EVT_Trigger });
            group.EventGroup.Add(new FSuperEvent { Event = "door", Delay = 2f, Type = EEventType.EVT_Untrigger });
            magic.Groups.Add(group);
        }

        EventProbe opened = scene.Add(new EventProbe(), tag: "opened");
        scene.Begin();
        return (scene, detector, door, opened);
    }

    [Fact]
    public void StationaryMercHoldsDoorOpenThenAuthoredHoldAndCloseDelayRunAfterExit()
    {
        var (scene, detector, door, opened) = MakeDoor();
        TestMerc merc = scene.Player<TestMerc>(default);
        detector.BeginTouch(merc);
        scene.StepSeconds(0.2f);
        Assert.Equal(0f, door.Location.Z);
        scene.StepSeconds(8f);
        Assert.Equal(100f, door.Location.Z);
        Assert.False(door.bInterpolating);
        Assert.Single(opened.Calls);
        detector.EndTouch(merc);
        scene.StepSeconds(2.4f);
        Assert.Equal(100f, door.Location.Z);
        scene.StepSeconds(1.7f);
        Assert.Equal(0f, door.Location.Z);
        Assert.False(door.bInterpolating);
        Assert.Equal(1f, door.MoveTime);
        Assert.Equal(2f, door.StayOpenTime);
        Assert.Equal(0.25f, door.DelayTime);
        Assert.Equal(0.5f, door.DelayTimeLastKey);
    }

    [Fact]
    public void LastEligibleOccupantAcrossDetectorsControlsTheHold()
    {
        var (scene, detector, door, _) = MakeDoor();
        var second = scene.Spawn(new SPresenceTrigger { Event = "door" });
        TestMerc firstMerc = scene.Player<TestMerc>(default);
        TestMerc secondMerc = scene.Player<TestMerc>(default);
        TestSpy spy = scene.Player<TestSpy>(default);
        detector.BeginTouch(firstMerc);
        scene.StepSeconds(0.5f);
        // This entry is dropped by the old moving-door filter, but must still participate in occupancy.
        second.BeginTouch(secondMerc);
        detector.BeginTouch(spy);
        detector.EndTouch(firstMerc);
        scene.StepSeconds(8f);
        Assert.Equal(100f, door.Location.Z);
        second.EndTouch(secondMerc);
        scene.StepSeconds(4f);
        Assert.Equal(0f, door.Location.Z);
        Assert.False(detector.IsPresenceActive);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    public void EnteringDuringClosingReversesImmediatelyFromCurrentPosition(int keys)
    {
        var (scene, detector, door, _) = MakeDoor(keys: keys);
        TestMerc merc = scene.Player<TestMerc>(default);
        detector.BeginTouch(merc);
        scene.StepSeconds(keys + 1f);
        detector.EndTouch(merc);
        scene.StepUntil(() => door.bInterpolating && !door.bOpening);
        scene.Step(20);
        float before = door.Location.Z;
        detector.BeginTouch(merc);
        scene.Step();
        Assert.True(door.bOpening);
        Assert.True(door.Location.Z > before, $"before={before}, after={door.Location.Z}");
        Assert.InRange(door.Location.Z - before, 0, 5);
        scene.StepSeconds(8f);
        Assert.Equal(100f * (keys - 1), door.Location.Z);
        Assert.False(door.bInterpolating);
    }

    [Fact]
    public void AuthoredRelayReleaseWaitsForPresenceWithoutRepeatingTheRelay()
    {
        var (scene, detector, door, opened) = MakeDoor("TriggerControl", relay: true);
        TestMerc merc = scene.Player<TestMerc>(default);
        detector.BeginTouch(merc);
        scene.StepSeconds(8f);
        Assert.Equal(100f, door.Location.Z);
        Assert.Single(opened.Calls);
        Assert.Equal(0, door.numTriggerEvents);
        detector.EndTouch(merc);
        scene.StepSeconds(2f);
        Assert.Equal(0f, door.Location.Z);
    }

    [Fact]
    public void ReenteringRelayControlledDoorWhileClosingReopensIt()
    {
        var (scene, detector, door, _) = MakeDoor("TriggerControl", relay: true);
        TestMerc merc = scene.Player<TestMerc>(default);
        detector.BeginTouch(merc);
        scene.StepSeconds(3f);
        detector.EndTouch(merc);
        scene.StepUntil(() => door.bInterpolating && !door.bOpening);
        scene.Step(20);
        float before = door.Location.Z;
        detector.BeginTouch(merc);
        scene.Step();
        Assert.True(door.Location.Z > before);
        scene.StepSeconds(8f);
        Assert.Equal(100f, door.Location.Z);
        detector.EndTouch(merc);
        scene.StepSeconds(2f);
        Assert.Equal(0f, door.Location.Z);
    }

    [Fact]
    public void RelayReleaseInTheSameTickAsClosingReentryCannotCancelTheReversal()
    {
        var (scene, detector, door, _) = MakeDoor("TriggerControl", relay: true);
        TestMerc merc = scene.Player<TestMerc>(default);
        detector.BeginTouch(merc);
        scene.StepSeconds(3f);
        detector.EndTouch(merc);
        scene.StepUntil(() => door.bInterpolating && !door.bOpening);
        scene.Step(20);
        float before = door.Location.Z;
        detector.BeginTouch(merc);
        // A second detector/earlier activation can have a queued release due in this same world tick.
        door.eventUnTrigger(detector, merc);
        scene.Step();
        Assert.True(door.bOpening && door.Location.Z > before);
        scene.StepSeconds(4f);
        Assert.Equal(100f, door.Location.Z);
        detector.EndTouch(merc);
        scene.StepSeconds(2f);
        Assert.Equal(0f, door.Location.Z);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeadOrDeletedOccupantDoesNotHoldDoorForever(bool deleted)
    {
        var (scene, detector, door, _) = MakeDoor();
        TestMerc merc = scene.Player<TestMerc>(default);
        detector.BeginTouch(merc);
        scene.StepSeconds(4f);
        if (deleted) Assert.True(merc.Destroy());
        else merc.Health = 0;
        scene.StepSeconds(4f);
        Assert.Equal(0f, door.Location.Z);
    }

    [Fact]
    public void DisabledDetectorReleasesItsDoor()
    {
        var (scene, detector, door, _) = MakeDoor();
        detector.BeginTouch(scene.Player<TestMerc>(default));
        scene.StepSeconds(4f);
        detector.SetCollision(false);
        scene.StepSeconds(4f);
        Assert.Equal(0f, door.Location.Z);
    }

    [Fact]
    public void SpecialBehaviorKeepsItsAuthoredTiming()
    {
        var (scene, detector, door, _) = MakeDoor();
        door.SpecialBehaviors.Add(new FSpecialBehavior
        {
            EventFromActor = detector, InitialState = "TriggerOpenTimed", MoveTime = 0.5f,
            StayOpenTime = 4f, DelayTime = 0.5f, DelayTimeLastKey = 0.25f,
        });
        TestMerc merc = scene.Player<TestMerc>(default);
        detector.BeginTouch(merc);
        scene.StepSeconds(8f);
        Assert.Equal(100f, door.Location.Z);
        detector.EndTouch(merc);
        scene.StepSeconds(4.1f);
        Assert.Equal(100f, door.Location.Z);
        scene.StepSeconds(1f);
        Assert.Equal(0f, door.Location.Z);
        Assert.Equal(4f, door.StayOpenTime);
        Assert.Equal(0.5f, door.MoveTime);
    }

    [Fact]
    public void DetectorSpecialBehaviorCanReverseADoorClosingUnderItsSwitchBehavior()
    {
        var (scene, detector, door, _) = MakeDoor("TriggerToggle");
        door.SpecialBehaviors.Add(new FSpecialBehavior
        {
            EventFromActor = detector, InitialState = "TriggerOpenTimed", MoveTime = 0.5f, StayOpenTime = 3f,
        });
        Sender button = scene.Spawn(new Sender());
        door.eventTrigger(button, null);
        scene.StepSeconds(2f);
        door.eventTrigger(button, null);
        scene.StepSeconds(0.8f);
        Assert.True(door.bInterpolating && !door.bOpening);
        float before = door.Location.Z;
        detector.BeginTouch(scene.Player<TestMerc>(default));
        scene.Step();
        Assert.True(door.bOpening && door.Location.Z > before);
        scene.StepSeconds(8f);
        Assert.Equal(100f, door.Location.Z);
        Assert.Equal(0.5f, door.MoveTime);
        Assert.Equal(3f, door.StayOpenTime);
    }
}
