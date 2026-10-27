using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using Xunit;

namespace OpenScct.Game.Tests.Devices;

/// <summary>spec/devices/magic-event.md: SMagicEvent groups, delays, types, sequences and repeats.</summary>
public sealed class MagicEventTests
{
    private const float Tick = 1f / 60f;

    private static FSuperEvent Entry(string name, float delay, EEventType type = EEventType.EVT_Trigger,
        EEventType validOn = EEventType.EVT_Trigger) =>
        new() { Event = name, Delay = delay, Type = type, ValidOn = validOn };

    private static FEventGroup Group(int repeat, bool sequence, params FSuperEvent[] entries)
    {
        var g = new FEventGroup { Repeat = repeat, Sequence = sequence };
        g.EventGroup.AddRange(entries);
        return g;
    }

    private static (DeviceScene S, SMagicEvent M, Dictionary<string, EventProbe> Probes, Sender Sender) Magic(
        params FEventGroup[] groups)
    {
        DeviceScene s = DeviceScene.Create();
        Sender sender = s.Add(new Sender());
        SMagicEvent m = s.Add(new SMagicEvent(), tag: "magic");
        m.Groups.AddRange(groups);
        var probes = new Dictionary<string, EventProbe>();
        foreach (string name in new[] { "a", "b", "c", "moloaie" })
        {
            probes[name] = s.Add(new EventProbe(), tag: name);
        }

        s.Begin();
        return (s, m, probes, sender);
    }

    [Fact]
    public void TheMolotovGroupFiresTwiceAtItsDelaysWithTheAuthoredType()
    {
        (DeviceScene s, SMagicEvent m, Dictionary<string, EventProbe> p, Sender sender) = Magic(Group(0, false,
            Entry("moloaie", 1f, EEventType.EVT_TriggerUntrigger),
            Entry("moloaie", 32f, EEventType.EVT_TriggerUntrigger)));
        TestMerc shooter = s.Player<TestMerc>(default);
        s.Step(30);
        float t0 = s.Time;
        s.Level.TriggerEvent("magic", sender, shooter);
        s.StepSeconds(33f);
        List<(float Time, long Tick, bool Trigger, Actor? Other, Pawn? Instigator)> calls = p["moloaie"].Calls;
        Assert.Equal(2, calls.Count);

        // Each queued copy flips its own type; the authored entries keep theirs, so both fire as Trigger.
        Assert.True(calls[0].Trigger && calls[1].Trigger);
        V.Near(1f, calls[0].Time - t0, 0.05f + Tick);
        V.Near(32f, calls[1].Time - t0, 0.05f + Tick);
        Assert.True(calls[0].Time - t0 >= 1f - 1e-4f);
        Assert.Same(sender, calls[0].Other);
        Assert.Same(shooter, calls[0].Instigator);
        Assert.Equal(EEventType.EVT_TriggerUntrigger, m.Groups[0].EventGroup[0].Type);
        Assert.Empty(m.EventQueue);
    }

    [Fact]
    public void AnImmediateAlternatingEntryFlipsOnTheAuthoredEntry()
    {
        (DeviceScene s, SMagicEvent m, Dictionary<string, EventProbe> p, _) = Magic(Group(0, false,
            Entry("a", 0f, EEventType.EVT_TriggerUntrigger)));
        s.Level.TriggerEvent("magic", null, null);
        s.Level.TriggerEvent("magic", null, null);
        s.Level.TriggerEvent("magic", null, null);
        Assert.Equal([true, false, true], p["a"].Calls.ConvertAll(c => c.Trigger));
        Assert.Equal(EEventType.EVT_UntriggerTrigger, m.Groups[0].EventGroup[0].Type);
    }

    [Fact]
    public void EntriesAnswerTheActivationTheyAreValidOn()
    {
        (DeviceScene s, _, Dictionary<string, EventProbe> p, _) = Magic(Group(0, false,
            Entry("a", 0f),
            Entry("b", 0f, EEventType.EVT_Untrigger, EEventType.EVT_Untrigger),
            Entry("c", 0f, EEventType.EVT_Trigger, EEventType.EVT_TriggerUntrigger)));
        s.Level.UntriggerEvent("magic", null, null);
        Assert.Empty(p["a"].Calls);
        Assert.False(Assert.Single(p["b"].Calls).Trigger);
        Assert.Single(p["c"].Calls);
        s.Level.TriggerEvent("magic", null, null);
        Assert.Single(p["a"].Calls);
        Assert.Single(p["b"].Calls);
        Assert.Equal(2, p["c"].Calls.Count);
    }

    [Fact]
    public void ASequenceGroupStepsOncePerActivationAndCountsWrapsAsCycles()
    {
        (DeviceScene s, SMagicEvent m, Dictionary<string, EventProbe> p, _) = Magic(Group(1, true,
            Entry("a", 0f), Entry("b", 0f)));
        s.Level.TriggerEvent("magic", null, null);
        Assert.Single(p["a"].Calls);
        Assert.Empty(p["b"].Calls);
        Assert.Equal(1, m.Groups[0].SequenceIndex);
        s.Level.TriggerEvent("magic", null, null);
        Assert.Single(p["b"].Calls);
        Assert.Equal(0, m.Groups[0].SequenceIndex);

        // Repeat 1: the wrap was the last cycle.
        Assert.Equal(-1, m.Groups[0].Repeat);
        s.Level.TriggerEvent("magic", null, null);
        Assert.Single(p["a"].Calls);
    }

    [Fact]
    public void ANonAnsweringSequenceStepDoesNotAdvance()
    {
        (DeviceScene s, SMagicEvent m, Dictionary<string, EventProbe> p, _) = Magic(Group(0, true,
            Entry("a", 0f), Entry("b", 0f)));
        s.Level.UntriggerEvent("magic", null, null);
        Assert.Equal(0, m.Groups[0].SequenceIndex);
        Assert.Empty(p["a"].Calls);
    }

    [Fact]
    public void RepeatCountsActivationsOfANonSequenceGroup()
    {
        (DeviceScene s, SMagicEvent m, Dictionary<string, EventProbe> p, _) = Magic(Group(2, false, Entry("a", 0f)));
        for (int i = 0; i < 4; i++)
        {
            s.Level.TriggerEvent("magic", null, null);
        }

        Assert.Equal(2, p["a"].Calls.Count);
        Assert.Equal(-1, m.Groups[0].Repeat);
    }

    [Fact]
    public void AnEmptyGroupIsDisabledAtStart()
    {
        (_, SMagicEvent m, _, _) = Magic(Group(0, false), Group(3, false, Entry("a", 0f)));
        Assert.Equal(-1, m.Groups[0].Repeat);
        Assert.Equal(3, m.Groups[1].Repeat);
    }

    [Fact]
    public void ATriggerFromTheStopActorClearsTheQueue()
    {
        (DeviceScene s, SMagicEvent m, Dictionary<string, EventProbe> p, Sender sender) = Magic(Group(0, false, Entry("a", 1f)));
        Sender stopper = s.Spawn(new Sender());
        m.StopActor = stopper;
        s.Level.TriggerEvent("magic", sender, null);
        Assert.Single(m.EventQueue);
        s.Level.UntriggerEvent("magic", stopper, null);
        Assert.Empty(m.EventQueue);
        s.StepSeconds(2f);
        Assert.Empty(p["a"].Calls);
    }

    [Fact]
    public void QueuedEntriesWithTheSameDueTimeRunNewestFirst()
    {
        DeviceScene s = DeviceScene.Create();
        var order = new List<string>();
        SMagicEvent m = s.Add(new SMagicEvent(), tag: "magic");
        m.Groups.Add(Group(0, false, Entry("a", 0.5f), Entry("b", 0.5f)));
        foreach (string name in new[] { "a", "b" })
        {
            s.Add(new Recorder { Label = name, Order = order }, tag: name);
        }

        s.Begin();
        s.Level.TriggerEvent("magic", null, null);
        s.StepSeconds(1f);
        Assert.Equal(["b", "a"], order);
    }

    private sealed class Recorder : Actor
    {
        public string Label = "";
        public List<string> Order = null!;

        protected override void Trigger(Actor? Other, Pawn? EventInstigator) => Order.Add(Label);
    }
}
