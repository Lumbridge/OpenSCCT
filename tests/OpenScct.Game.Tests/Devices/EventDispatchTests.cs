using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using Xunit;

namespace OpenScct.Game.Tests.Devices;

/// <summary>spec/devices/events.md: TriggerEvent / UntriggerEvent dispatch and the Actor hooks devices rely on.</summary>
public sealed class EventDispatchTests
{
    private sealed class Receiver : Actor
    {
        public string Label = "";
        public List<string>? Log;
        public System.Action<Receiver, Actor?, Pawn?>? OnTrigger;

        protected override void Trigger(Actor? Other, Pawn? EventInstigator)
        {
            Log?.Add(Label);
            OnTrigger?.Invoke(this, Other, EventInstigator);
        }

        protected override void UnTrigger(Actor? Other, Pawn? EventInstigator) => Log?.Add("un:" + Label);
    }

    private static (DeviceScene S, List<string> Log, Receiver A, Receiver B, Receiver C) ThreeReceivers()
    {
        DeviceScene s = DeviceScene.Create();
        var log = new List<string>();
        Receiver a = s.Add(new Receiver { Label = "a", Log = log }, tag: "go");
        s.Add(new Receiver { Label = "other", Log = log }, tag: "other");
        Receiver b = s.Add(new Receiver { Label = "b", Log = log }, tag: "go");
        Receiver c = s.Add(new Receiver { Label = "c", Log = log }, tag: "go");
        s.Begin();
        return (s, log, a, b, c);
    }

    [Fact]
    public void TriggerEventReachesEveryTaggedActorInListOrderWithItsArguments()
    {
        (DeviceScene s, List<string> log, Receiver a, _, _) = ThreeReceivers();
        Sender sender = s.Spawn(new Sender());
        TestSpy spy = s.Player<TestSpy>(default);
        Actor? seenOther = null;
        Pawn? seenInstigator = null;
        a.OnTrigger = (_, o, i) => (seenOther, seenInstigator) = (o, i);
        sender.TriggerEvent("go", sender, spy);
        Assert.Equal(["a", "b", "c"], log);
        Assert.Same(sender, seenOther);
        Assert.Same(spy, seenInstigator);
    }

    [Fact]
    public void UntriggerEventCallsUnTrigger()
    {
        (DeviceScene s, List<string> log, _, _, _) = ThreeReceivers();
        s.Level.UntriggerEvent("go", null, null);
        Assert.Equal(["un:a", "un:b", "un:c"], log);
    }

    [Fact]
    public void NoneEventAndUnknownNamesDoNothing()
    {
        (DeviceScene s, List<string> log, _, _, _) = ThreeReceivers();
        s.Level.TriggerEvent(Name.None, null, null);
        s.Level.TriggerEvent("nobody", null, null);
        Assert.Empty(log);
    }

    [Fact]
    public void StaticActorsAreNeverTriggered()
    {
        DeviceScene s = DeviceScene.Create();
        var log = new List<string>();
        s.Add(new Receiver { Label = "static", Log = log, bStatic = true }, tag: "go");
        s.Add(new Receiver { Label = "dynamic", Log = log }, tag: "go");
        s.Begin();
        s.Level.TriggerEvent("go", null, null);
        Assert.Equal(["dynamic"], log);
    }

    [Fact]
    public void AnActorFiringItsOwnTagTriggersItself()
    {
        DeviceScene s = DeviceScene.Create();
        var log = new List<string>();
        Receiver self = s.Add(new Receiver { Label = "self", Log = log }, tag: "loop");
        s.Begin();
        self.TriggerEvent("loop", self, null);
        Assert.Equal(["self"], log);
    }

    [Fact]
    public void NestedDispatchCompletesBeforeTheNextReceiver()
    {
        (DeviceScene s, List<string> log, Receiver a, _, _) = ThreeReceivers();
        s.Spawn(new Receiver { Label = "inner", Log = log }, tag: "inner");
        a.OnTrigger = (r, _, _) => r.TriggerEvent("inner", r, null);
        s.Level.TriggerEvent("go", null, null);
        Assert.Equal(["a", "inner", "b", "c"], log);
    }

    [Fact]
    public void ActorsDestroyedDuringDispatchAreSkipped()
    {
        (DeviceScene s, List<string> log, Receiver a, Receiver b, _) = ThreeReceivers();
        a.OnTrigger = (_, _, _) => b.Destroy();
        s.Level.TriggerEvent("go", null, null);
        Assert.Equal(["a", "c"], log);
    }

    [Fact]
    public void ForceTriggerableEvenIfMovingIsFalseOnActor()
    {
        DeviceScene s = DeviceScene.Create().Begin();
        Assert.False(s.Spawn(new Sender()).ForceTriggerableEvenIfMoving());
    }

    [Fact]
    public void NotifyNoiseReachesEveryPlayerControllerAndZeroMeansOne()
    {
        DeviceScene s = DeviceScene.Create().Begin();
        TestSpy spy = s.Player<TestSpy>(default);
        TestMerc merc = s.Player<TestMerc>(default);
        Sender source = s.Spawn(new Sender());
        source.NotifyNoise();
        source.NotifyNoise(0.25f);
        foreach (Pawn p in new Pawn[] { spy, merc })
        {
            var pc = (TestController)p.Controller!;
            Assert.Equal([(source, 1f), (source, 0.25f)], pc.Noises);
        }
    }

    [Fact]
    public void AnimNotifyTriggerFiresFromItsOwnerWithAPawnInstigatorOnlyForPawns()
    {
        DeviceScene s = DeviceScene.Create();
        EventProbe probe = s.Add(new EventProbe(), tag: "notify");
        s.Begin();
        Sender prop = s.Spawn(new Sender());
        TestSpy spy = s.Player<TestSpy>(default);
        var notify = new UAnimNotify_Trigger { EventName = "notify" };
        notify.Notify(prop);
        notify.Notify(spy);
        Assert.Equal(2, probe.Calls.Count);
        Assert.Same(prop, probe.Calls[0].Other);
        Assert.Null(probe.Calls[0].Instigator);
        Assert.Same(spy, probe.Calls[1].Other);
        Assert.Same(spy, probe.Calls[1].Instigator);
    }

    [Fact]
    public void AnimNotifyEventsAreAbsorbedOnAClientsProxy()
    {
        DeviceScene s = DeviceScene.Create(ENetMode.NM_Client);
        EventProbe probe = s.Add(new EventProbe(), tag: "notify");
        s.Begin();
        Sender proxy = s.Spawn(new Sender { Role = ENetRole.ROLE_SimulatedProxy });
        new UAnimNotify_Trigger { EventName = "notify" }.Notify(proxy);
        Assert.Empty(probe.Calls);
    }
}
