using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using Xunit;

namespace OpenScct.Game.Tests.Devices;

public sealed class AlarmStatisticsTests
{
    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 0)]
    public void ADecoyRaisesTheAlarmWithoutCountingARealSpyDetection(bool decoy, int detections)
    {
        DeviceScene scene = DeviceScene.Create();
        SAlarm alarm = scene.Add(new SAlarm());
        scene.Begin();
        TestSpy spy = scene.Player<TestSpy>(default);
        var pc = (SPlayerController)spy.Controller!;
        pc.PlayerReplicationInfo = scene.Spawn(new PlayerReplicationInfo { Stats = new USPlayerStats() });
        if (decoy) alarm.eventSimulateActivated(null, spy);
        else alarm.eventTrigger(null, spy);
        Assert.True(alarm.bAlarmOn);
        Assert.Equal(new Name("s_Active"), alarm.GetStateName());
        Assert.Equal(detections, pc.PlayerReplicationInfo.Stats!.Spy_AlarmsTriggered);
    }
}
