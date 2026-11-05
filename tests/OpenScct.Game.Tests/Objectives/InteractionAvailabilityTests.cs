using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using Xunit;

namespace OpenScct.Game.Tests.Objectives;

public sealed class InteractionAvailabilityTests
{
    [Theory]
    [InlineData("s_Maintain_Use")]
    [InlineData("s_Hacking")]
    [InlineData("WaitingBeforeActivation")]
    [InlineData("WaitingBeforeDesactivation")]
    public void BusySwitchStatesStayUnavailableEvenWhenTheBusyFlagIsClear(string state)
    {
        var w = new ObjWorld();
        TestPC pc = w.AddPlayer(spy: true, default);
        var entry = w.Level.Spawn<STriggerInteraction>()!;
        pc.IManager!.AddInteractionObj(entry);
        Assert.True(pc.IManager.HasUsableInteractions());
        entry.GotoState(new Name(state));
        entry.bInUse = false; // The native checks the state independently of this replicated flag.
        Assert.False(entry.CanUse(pc));
        Assert.False(pc.IManager.HasUsableInteractions());
        entry.GotoState(Name.None);
        Assert.True(pc.IManager.HasUsableInteractions());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BusyEntriesStayOutOfTheMenuAfterTheirReuseDelayExpires(bool presenceWarning)
    {
        var w = new ObjWorld();
        TestPC pc = w.AddPlayer(spy: false, default);
        var entry = w.Level.Spawn<SInteractObject>()!;
        entry.iReusableTime = 2;
        pc.IManager!.AddInteractionObj(entry);
        Assert.False(pc.IManager.HasUsableInteractions());

        entry.bPresenceDetected = presenceWarning;
        entry.bInUse = !presenceWarning;
        w.Run(2.1f);
        Assert.False(entry.CanUse(pc));
        Assert.False(pc.IManager.HasUsableInteractions());

        entry.ChangePresenceDetected(false);
        entry.bInUse = false;
        Assert.True(entry.CanUse(pc));
        Assert.True(pc.IManager.HasUsableInteractions());
    }

    [Fact]
    public void HoldSwitchAllowsSpiesButIsAbsentFromTheMercMenu()
    {
        var w = new ObjWorld();
        SInterrupteur? panel = null;
        w.Load(x => panel = x.Place(new SInterrupteur { bMaintainsSwitch = true, UsableBy = Usable.U_BOTH }, default));
        STriggerInteraction entry = Assert.IsType<STriggerInteraction>(panel!.InteractionObj);
        TestPC merc = w.AddPlayer(spy: false, default);
        TestPC spy = w.AddPlayer(spy: true, new FVector(1000, 0, 0));
        merc.IManager!.AddInteractionObj(entry);
        Assert.True(entry.CanUse(spy));
        Assert.False(entry.CanUse(merc));
        Assert.False(merc.IManager.HasUsableInteractions());

        entry.bMaintain = false;
        Assert.True(entry.CanUse(merc));
        Assert.True(merc.IManager.HasUsableInteractions());
    }
}
