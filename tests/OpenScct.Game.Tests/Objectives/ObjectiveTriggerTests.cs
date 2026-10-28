using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Objectives;
using OpenScct.Game.SBase;
using Xunit;

namespace OpenScct.Game.Tests.Objectives;

// spec/objectives/objective-trigger.md, plus the small rules of the other objective specs not covered elsewhere.
public sealed class ObjectiveTriggerTests
{
    private static (ObjWorld w, SObjectiveTrigger t, SObjective o) Plain()
    {
        var w = new ObjWorld();
        SObjectiveTrigger? t = null;
        SObjective? o = null;
        w.Load(x =>
        {
            t = x.Place(new SObjectiveTrigger { CaptureTime = 1500 }, default);
            o = new SObjective();
            o.Triggers.Add(t);
            x.Place(o, default);
        });
        return (w, t!, o!);
    }

    [Fact]
    public void Triggers_are_never_world_geometry_and_take_no_sticky_gadgets()
    {
        (_, SObjectiveTrigger t, _) = Plain();

        Assert.False(t.bWorldGeometry);
        Assert.False(t.AcceptStickableGPO());
        Assert.False(new SInterrupteur().AcceptStickableGPO());
    }

    [Fact]
    public void PostBeginPlay_raises_the_objective_time_unless_the_objective_begins_play_later()
    {
        var w = new ObjWorld();
        SObjective? first = null;
        w.Load(x =>
        {
            first = new SObjective();
            x.Place(first, default);
            first.Triggers.Add(x.Place(new SObjectiveTrigger { CaptureTime = 1500 }, default));
        });

        Assert.Equal(1500, first!.RemainingTime);

        // Listed after its trigger, the objective's own PostBeginPlay puts 1000 back.
        (_, _, SObjective later) = Plain();
        Assert.Equal(1000, later.RemainingTime);
    }

    [Fact]
    public void Reset_activates_and_Desactivate_retires_the_trigger()
    {
        (_, SObjectiveTrigger t, SObjective o) = Plain();

        o.Reset();
        Assert.True(t.bActive);
        Assert.False(t.IsActive()); // game 0x10AE8B30 returns NOT bActive
        Assert.True(t.IsInState(K.s_Initial));
        Assert.Equal(0f, t.GetTriggerTime()); // game 0x10AE8AF0: 0 unless triggering

        t.eventTriggering(true);
        Assert.Equal(t.TriggerTime + t.CaptureTime, t.GetTriggerTime());
        t.Desactivate();

        Assert.False(t.bActive);
        Assert.False(t.bTriggering);
        Assert.True(t.IsActive());
        Assert.True(t.IsInState(K.s_Inactive));
    }

    [Fact]
    public void Base_lock_ignores_completion_and_unlock_restores()
    {
        (ObjWorld w, SObjectiveTrigger t, SObjective o) = Plain();
        o.Reset();
        TestPC spy = w.AddPlayer(spy: true, default);
        t.ControllerWhoTakesIt = spy;

        t.eventLock();
        Assert.True(t.IsInState(K.s_TemporaryInactive));
        Assert.True(t.bLocked);
        Assert.False(t.bActive);
        Assert.True(t.IsActive()); // NOT bActive (game 0x10AE8B30)

        t.eventTakeObjective();
        Assert.False(o.ObjectiveTaken);

        t.eventReset();
        Assert.True(t.bLocked);

        t.eventUnLock();
        Assert.False(t.bLocked);
        Assert.True(t.bActive);
        Assert.True(t.IsInState(K.s_Initial));

        t.eventTakeObjective();
        Assert.True(o.ObjectiveTaken);
    }

    [Fact]
    public void Base_lock_needs_objective_time_left()
    {
        (_, SObjectiveTrigger t, SObjective o) = Plain();
        o.Reset();
        o.RemainingTime = 0;

        t.eventLock();

        Assert.False(t.IsInState(K.s_TemporaryInactive));
    }

    [Fact]
    public void A_terminal_destroys_its_entry_with_it()
    {
        var w = new ObjWorld();
        SComputerObjectiveTrigger? t = null;
        w.Load(x => t = x.Place(new SComputerObjectiveTrigger(), default));
        SInteractObject entry = t!.InteractionObj!;
        t.bNoDelete = false;

        t.Destroy();

        Assert.True(entry.bDeleteMe);
    }

    [Fact]
    public void Disk_prepare_shows_it()
    {
        var w = new ObjWorld();
        SFlag? flag = null;
        w.Load(x => flag = x.Place(new SFlag { bHidden = true }, default));

        flag!.Prepare();

        Assert.False(flag.bHidden);
    }

    [Fact]
    public void Give_entry_returns_to_the_previous_state_on_release()
    {
        // No selected gadget, so the menu's CanUse finds no mate to give to and clears the target: the release
        // reports Leave, but the player still returns to the previous state.
        var w = new ObjWorld();
        TestPC pc = w.AddPlayer(spy: true, default);
        TestPC mate = w.AddPlayer(spy: true, default);
        var owner = w.Level.Spawn<SBasicGamePlayObject>()!;
        pc.IManager!.AddInteractionObj(ObjectiveNatives.Spawn<STriggerInteraction>(owner, owner, default, default));
        // The touched entry went in ahead of the built-in ones, so the give entry is one slot further down.
        var give = (SShareObjectInteraction)pc.IManager.Interactions[4]!;
        give.aActorToInteractWith = (SBasePawn)mate.Pawn!;
        // The HUD list calls CanUse on every entry each frame; the selection walk stops at the first usable slot.
        give.CanUse(pc);

        Assert.Equal(EMenuRelease.Leave, ObjWorld.UseEntry(pc, give));
        Assert.Null(give.aActorToInteractWith);
        Assert.True(pc.IsInState(K.s_PlayerWalkingSt0H));
    }

    [Fact]
    public void Panel_hack_interrupted_while_entering_leaves_with_the_failed_label()
    {
        var w = new ObjWorld();
        TestPC spy = w.AddPlayer(spy: true, default);

        SwitchHackRules.EnterTakeDamage(spy);

        Assert.True(spy.IsInState(K.s_Hack_Leave));
        Assert.Contains(spy.Mirrors, m => m.State == K.s_Hack_Leave && m.Label == K.TriggerHack_Failed);
    }

    [Fact]
    public void DiskHunt_mission_counts_active_disks_and_needs_all_without_a_target()
    {
        var w = new ObjWorld();
        SMission? hunt = null;
        var disks = new SObjectiveTrigger[3];
        w.Load(x =>
        {
            hunt = new SMission { GameMode = EGameMode.GM_DiskHunt, bChained = false };
            for (int i = 0; i < 3; i++)
            {
                disks[i] = x.Place(new SObjectiveTrigger(), new FVector(i * 300f, 0f, 0f));
                hunt.Triggers.Add(disks[i]);
            }

            x.Place(hunt, default);
        });

        hunt!.Reset();
        Assert.Equal(3, hunt.TotalObjectivesLeft);
        Assert.Equal(3, w.Gri.NumObjectives);
        Assert.False(hunt.ObjectiveTaken);

        disks[0].Desactivate();
        hunt.NotifyTaken(null, disks[0]);
        Assert.False(hunt.ObjectiveTaken);
        Assert.Equal(2, hunt.TotalObjectivesLeft);

        disks[1].Desactivate();
        disks[2].Desactivate();
        hunt.NotifyTaken(null, disks[2]);
        Assert.True(hunt.ObjectiveTaken);
    }

    [Fact]
    public void DiskHunt_minimum_activates_that_many_disks()
    {
        var w = new ObjWorld(seed: 3);
        SMission? hunt = null;
        w.Load(x =>
        {
            hunt = new SMission { GameMode = EGameMode.GM_DiskHunt, MinimumObjectives = 2 };
            for (int i = 0; i < 5; i++)
            {
                hunt.Triggers.Add(x.Place(new SObjectiveTrigger(), new FVector(i * 300f, 0f, 0f)));
            }

            x.Place(hunt, default);
        });

        hunt!.Reset();

        Assert.Equal(2, hunt.Triggers.FindAll(t => t!.bActive).Count);
        Assert.Equal(2, hunt.ObjectivesLeft);
    }
}
