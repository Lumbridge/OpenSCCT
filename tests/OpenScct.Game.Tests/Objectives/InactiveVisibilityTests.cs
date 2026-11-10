using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using Xunit;

namespace OpenScct.Game.Tests.Objectives;

public sealed class InactiveVisibilityTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Unlinked_computers_follow_the_map_visibility_choice_but_stay_unusable(bool hide)
    {
        var world = new ObjWorld();
        SComputerObjectiveTrigger? device = null;
        world.Load(w => device = w.Place(new SComputerObjectiveTrigger { HideWhileInactive = hide }, default));
        Assert.False(device!.bActive);
        Assert.Equal(hide, device.bHidden);
        Assert.False(device.ComputerInteraction!.CanUse(world.AddPlayer(spy: true, default)));
        world.World.Step(2);
        Assert.Equal(hide, device.bHidden);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Pending_chained_objectives_are_prepared_without_overriding_visibility(bool hide)
    {
        var world = new ObjWorld();
        var first = new SObjective();
        var second = new SObjective();
        var mission = new SMission { GameMode = EGameMode.GM_Multi, bChained = true };
        SComputerObjectiveTrigger? device = null;
        world.Load(w =>
        {
            device = w.Place(new SComputerObjectiveTrigger { HideWhileInactive = hide }, default);
            second.Triggers.Add(device);
            w.Place(first, default);
            w.Place(second, default);
            mission.Objectives.Add(first);
            mission.Objectives.Add(second);
            w.Place(mission, default);
        });
        mission.Desactivate();
        mission.Reset();
        Assert.False(device!.bActive);
        Assert.Equal(hide, device.bHidden);
        first.NotifyTaken(null, null);
        Assert.True(device.bActive);
        Assert.False(device.bHidden);
        second.NotifyTaken(null, device);
        Assert.False(device.bActive);
        Assert.Equal(hide, device.bHidden);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Bomb_targets_and_disks_keep_the_same_inactive_choice_on_both_roles(bool hide)
    {
        var world = new ObjWorld();
        SBombTargetObjectiveTrigger? bomb = null;
        SDisk? disk = null;
        world.Load(w =>
        {
            bomb = w.Place(new SBombTargetObjectiveTrigger { HideWhileInactive = hide }, default);
            disk = w.Place(new SDisk { HideWhileInactive = hide }, new FVector(500, 0, 0));
        });
        bomb!.Desactivate();
        disk!.Desactivate();
        Assert.Equal(hide, bomb.bHidden);
        Assert.Equal(hide, disk.bHidden);
        disk.Role = ENetRole.ROLE_SimulatedProxy;
        world.World.Step(2);
        Assert.Equal(hide, disk.bHidden);
        Assert.False(bomb.InteractionObj!.bUsable);
        Assert.False(disk.bPickable);
    }

    [Fact]
    public void Maps_without_the_override_keep_the_original_computer_default()
    {
        var world = new ObjWorld();
        SComputerObjectiveTrigger? device = null;
        world.Load(w => device = w.Place(new SComputerObjectiveTrigger(), default));
        Assert.False(device!.HasInactiveVisibility);
        Assert.True(device.bHidden);
    }
}
