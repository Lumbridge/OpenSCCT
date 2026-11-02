using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Objectives;
using OpenScct.Game.SBase;
using Xunit;

namespace OpenScct.Game.Tests.Objectives;

// spec/objectives/terminal-hack.md
public sealed class TerminalHackTests
{
    private sealed class Setup
    {
        public required ObjWorld W;
        public required SComputerObjectiveTrigger Terminal;
        public required SObjective Objective;

        public SComputerInteraction Entry => Terminal.ComputerInteraction!;

        // A spy standing on the use spot, facing the terminal, with the terminal's "Use" in his menu.
        public TestPC Spy()
        {
            Terminal.GetUseSpot(new SPawnAttaque { Location = Terminal.Location }, out FVector spot, out FVector facing);
            TestPC pc = W.AddPlayer(spy: true, spot, facing.Rotation());
            ObjWorld.Touch(pc, Entry);
            return pc;
        }
    }

    // A terminal (DominationCaptureTime 10, as in SteeE) in a one-objective mission, reset as the mission does.
    private static Setup Make(int capture = 10, bool reset = true)
    {
        var w = new ObjWorld();
        SComputerObjectiveTrigger? terminal = null;
        SObjective? objective = null;
        w.Load(x =>
        {
            terminal = x.Place(new SComputerObjectiveTrigger { DominationCaptureTime = capture, TriggerName = "Laboratory" },
                new FVector(0f, 0f, 0f), new FRotator(0, 0, 0));
            objective = new SObjective { ObjectiveName = "Laboratory (2nd floor)" };
            objective.Triggers.Add(terminal);
            x.Place(objective, default);
        });
        if (reset)
        {
            objective!.Reset();
        }

        return new Setup { W = w, Terminal = terminal!, Objective = objective! };
    }

    // ---- setup (rules 1 to 5) ---------------------------------------------------------------------------------

    [Fact]
    public void PostBeginPlay_spawns_the_use_entry_in_front_and_turns_collision_off()
    {
        Setup s = Make(reset: false);

        Assert.False(s.Terminal.bCollideActors);
        Assert.True(s.Terminal.bHidden);
        Assert.Equal(new FVector(39f, 0f, 0f), s.Entry.Location);
        Assert.Equal(10, s.Entry.iPriority);
        Assert.True(s.Entry.bInteractInFront);
        Assert.Equal(Usable.U_SPAWNOFFENSE, s.Entry.UsableBy);
        Assert.Equal((50f, 50f), (s.Entry.ItRadius, s.Entry.ItHeight));
        Assert.Same(s.Terminal, s.Entry.ObjectiveTrigger);
        Assert.Same(s.Terminal, s.Entry.Base);
        Assert.Equal(SComputerObjectiveTrigger.SKIN_Idle, s.Terminal.SkinState);
    }

    [Fact]
    public void Reset_shows_the_terminal_with_the_domination_capture_time()
    {
        Setup s = Make();

        Assert.Equal(10, s.Terminal.CaptureTime);
        Assert.False(s.Terminal.bHidden);
        Assert.True(s.Terminal.bCollideActors);
        Assert.Equal(SComputerObjectiveTrigger.BlocksPlayers, s.Terminal.bBlockPlayers); // off in the port: the converted collider covers the use spot
        Assert.True(s.Terminal.bActive);
        Assert.False(s.Terminal.bLocked);
        Assert.True(s.Terminal.IsInState(K.s_Initial));
        Assert.Same(s.Objective, s.Terminal.Objective);
    }

    [Fact]
    public void Prepare_shows_the_terminal_idle()
    {
        Setup s = Make(reset: false);

        s.Terminal.Prepare();

        Assert.False(s.Terminal.bHidden);
        Assert.True(s.Terminal.bCollideActors);
        Assert.Equal(SComputerObjectiveTrigger.SKIN_Idle, s.Terminal.SkinState);
    }

    [Fact]
    public void Use_spot_is_64_in_front_facing_back_at_the_terminal()
    {
        var w = new ObjWorld();
        SComputerObjectiveTrigger? t = null;
        w.Load(x => t = x.Place(new SComputerObjectiveTrigger(), new FVector(100f, 200f, 50f), new FRotator(0, 16384, 0)));
        var pawn = new SPawnAttaque { Location = new FVector(0f, 0f, 7f) };

        t!.GetUseSpot(pawn, out FVector spot, out FVector facing);

        Assert.Equal(100f, spot.X, 3);
        Assert.Equal(264f, spot.Y, 3);
        Assert.Equal(7f, spot.Z);
        Assert.True(facing.Y < -0.99f);
    }

    // ---- starting a hack (rules 6 to 14) ------------------------------------------------------------------------

    [Fact]
    public void Use_on_an_active_terminal_records_the_taker_and_sends_the_spy_to_the_spot()
    {
        Setup s = Make();
        TestPC spy = s.Spy();

        Assert.Equal(EMenuRelease.InteractionInitSent, ObjWorld.UseEntry(spy, s.Entry));

        Assert.Same(spy, s.Terminal.ControllerWhoTakesIt);
        Assert.True(spy.IsInState(K.s_Computer_GoTo));
    }

    [Fact]
    public void A_zero_capture_time_becomes_one_second()
    {
        Setup s = Make();
        s.Terminal.CaptureTime = 0;
        TestPC spy = s.Spy();

        InteractionMenuRules.InteractionInit(spy, s.Entry, 0);

        Assert.Equal(1, s.Terminal.CaptureTime);
        Assert.Equal(10, s.Terminal.DominationCaptureTime);
    }

    [Fact]
    public void A_merc_never_starts_a_terminal_hack()
    {
        Setup s = Make();
        s.Entry.UsableBy = Usable.U_BOTH;
        TestPC merc = s.W.AddPlayer(spy: false, s.Entry.Location);

        InteractionMenuRules.InteractionInit(merc, s.Entry, 0);

        Assert.False(merc.IsInState(K.s_Computer_GoTo));
    }

    [Fact]
    public void An_inactive_or_locked_terminal_sends_a_player_in_the_menu_back()
    {
        Setup s = Make();
        TestPC spy = s.Spy();
        spy.bUsing = 1;
        InteractionMenuRules.DoInteraction(spy, canInteract: true);
        Assert.True(spy.IsInState(K.s_MenuInteraction));

        s.Terminal.bLocked = true;
        InteractionMenuRules.InteractionInit(spy, s.Entry, 0);

        Assert.True(spy.IsInState(K.s_PlayerWalkingSt0H));
        Assert.Null(s.Terminal.ControllerWhoTakesIt);
        Assert.Contains(spy.Mirrors, m => m.State == K.s_PlayerWalkingSt0H && m.DontSave);
        Assert.False(s.Entry.CanUse(spy));
    }

    [Fact]
    public void Walk_to_the_spot_needs_a_terminal_within_300_and_aborts_on_other_bumps()
    {
        Setup s = Make();
        TestPC spy = s.Spy();

        Assert.True(TerminalHackRules.GoToPlayerTick(spy, out _));
        Assert.Equal(new FVector(64f, 0f, spy.Pawn!.Location.Z), ((SBasePawn)spy.Pawn).vVandammePos);
        Assert.False(TerminalHackRules.GoToAbortsOn(s.Terminal));
        Assert.True(TerminalHackRules.GoToAbortsOn(spy.Pawn));
        Assert.True(TerminalHackRules.GoToAbortsOn(null));

        spy.Pawn.Location = new FVector(301f, 0f, 0f);
        Assert.False(TerminalHackRules.GoToPlayerTick(spy, out _));
    }

    // ---- Triggering (rules 15 to 19) --------------------------------------------------------------------------

    [Fact]
    public void Triggering_true_locks_the_entry_and_tells_everyone()
    {
        Setup s = Make();
        TestPC spy = s.Spy();
        TestPC merc = s.W.AddPlayer(spy: false, new FVector(800f, 0f, 0f));
        s.W.Run(1f);

        s.Terminal.eventTriggering(true);

        Assert.True(s.Terminal.bTriggering);
        Assert.False(s.Entry.bUsable);
        Assert.Equal(SComputerObjectiveTrigger.SKIN_Triggering, s.Terminal.SkinState);
        Assert.Equal(s.W.Level.TimeSeconds, s.Terminal.fHackStartTime);
        Assert.Equal(2, s.W.Net.Count(K.MsgNeutralizationInProgress));
        Assert.Contains(("Radar", (Actor)s.Terminal, true), s.W.Net.Multicasts);
        Assert.Contains(("ObjectiveHud", (Actor)s.Objective, true), s.W.Net.Multicasts);
        Assert.True(s.Terminal.TriggerTime > 0f);
        Assert.False(s.Entry.CanUse(merc));
    }

    [Fact]
    public void Stopping_keeps_the_whole_seconds_hacked()
    {
        Setup s = Make();
        TestPC spy = s.Spy();
        s.Terminal.ControllerWhoTakesIt = spy;
        s.Terminal.eventTriggering(true);

        s.W.Run(3.5f);
        s.Terminal.eventTriggering(false);

        Assert.Equal(7, s.Terminal.CaptureTime);
        Assert.Equal(3, spy.PlayerReplicationInfo!.Stats!.Spy_ObjectiveTimeCaptured);
        Assert.True(s.Entry.bUsable);
        Assert.Null(s.Terminal.ControllerWhoTakesIt);
        Assert.False(s.Terminal.bTriggering);
        Assert.Equal(0f, s.Terminal.TriggerTime);
        Assert.Contains(("Radar", (Actor)s.Terminal, false), s.W.Net.Multicasts);
    }

    [Fact]
    public void A_hack_broken_in_its_last_second_still_needs_one_second()
    {
        Setup s = Make();
        s.Terminal.eventTriggering(true);
        s.W.Run(9.9f);
        s.Terminal.eventTriggering(false);
        Assert.Equal(1, s.Terminal.CaptureTime);

        s.Terminal.eventTriggering(true);
        s.W.Run(5f);
        s.Terminal.eventTriggering(false);
        Assert.Equal(1, s.Terminal.CaptureTime);
    }

    [Fact]
    public void The_screen_goes_idle_ten_seconds_after_a_hack_stops()
    {
        Setup s = Make();
        s.Terminal.eventTriggering(true);
        s.W.Run(1f);
        s.Terminal.eventTriggering(false);

        s.W.Run(9.9f);
        Assert.Equal(SComputerObjectiveTrigger.SKIN_Triggering, s.Terminal.SkinState);
        s.W.Run(0.2f);
        Assert.Equal(SComputerObjectiveTrigger.SKIN_Idle, s.Terminal.SkinState);
    }

    [Fact]
    public void Hud_percent_grows_during_a_hack_from_the_kept_progress()
    {
        Setup s = Make();
        s.Terminal.eventTriggering(true);
        s.W.Run(3.5f);
        s.Terminal.eventTriggering(false);
        Assert.Equal(30, s.Terminal.GetHackPercent());

        s.Terminal.eventTriggering(true);
        s.W.Run(2.5f);
        Assert.InRange(s.Terminal.GetHackPercent(), 54, 55);
        s.W.Run(20f);
        Assert.True(s.Terminal.GetHackPercent() >= 100); // the sim percent is not clamped (rule 32), the HUD shows 99
    }

    // ---- the hack through the spy's states (rules 14, 20 to 25) ------------------------------------------------------

    [Fact]
    public void Full_hack_takes_the_objective_after_the_capture_time()
    {
        Setup s = Make();
        TestPC spy = s.Spy();
        TestPC merc = s.W.AddPlayer(spy: false, new FVector(800f, 0f, 0f));
        ObjWorld.UseEntry(spy, s.Entry);
        spy.GotoState(K.s_Computer_Enter);
        spy.GotoState(K.s_Computer_Use);

        Assert.Equal(10f, spy.TimerRate);
        Assert.True(spy.bTimerLoop);
        Assert.True(s.Terminal.bTriggering);
        s.W.Run(9.9f);
        Assert.False(s.Objective.ObjectiveTaken);
        s.W.Run(0.2f);

        Assert.True(s.Objective.ObjectiveTaken);
        Assert.True(s.Terminal.complete);
        Assert.Equal(SComputerObjectiveTrigger.SKIN_Hacked, s.Terminal.SkinState);
        Assert.Equal(2, s.W.Net.Count(K.MsgNeutralized));
        Assert.Same(s.Terminal, s.W.Net.Messages.Find(m => m.Switch == K.MsgNeutralized).Obj);
        Assert.True(spy.IsInState(K.s_Computer_Leave));
        Assert.False(s.Terminal.bTriggering);
        Assert.False(s.Terminal.bActive);
        Assert.Contains(s.Objective, s.W.Host.Taken);
        Assert.Single(spy.PlayerReplicationInfo!.Stats!.Spy_ObjectivesTaken);
        _ = merc;
    }

    // Rule 36 (game 0x10ABDEB0): Desactivate first sends the taker MirrorState('s_PlayerWalkingSt0H'), so on a
    // completed hack the taker goes to walking inside the take, then s_Computer_Use.Timer sends him to the leave.
    [Fact]
    public void Completed_hack_sends_the_taker_to_walking_before_the_leave()
    {
        Setup s = Make();
        TestPC spy = s.Spy();
        ObjWorld.UseEntry(spy, s.Entry);
        spy.GotoState(K.s_Computer_Enter);
        spy.GotoState(K.s_Computer_Use);
        spy.Mirrors.Clear();
        s.W.Run(10.1f);

        int walk = spy.Mirrors.FindIndex(m => m.State == K.s_PlayerWalkingSt0H);
        int leave = spy.Mirrors.FindIndex(m => m.State == K.s_Computer_Leave);
        Assert.True(walk >= 0);
        Assert.True(leave > walk);
        Assert.True(spy.IsInState(K.s_Computer_Leave));
    }

    // Rule 36: a terminal deactivated while hacked (its objective leaves play, e.g. the mission completed elsewhere)
    // kicks the hacker back to walking.
    [Fact]
    public void Desactivate_while_hacked_kicks_the_hacker_to_walking()
    {
        Setup s = Make();
        TestPC spy = s.Spy();
        ObjWorld.UseEntry(spy, s.Entry);
        spy.GotoState(K.s_Computer_Enter);
        spy.GotoState(K.s_Computer_Use);
        s.W.Run(2f);

        s.Objective.Desactivate();

        Assert.True(spy.IsInState(K.s_PlayerWalkingSt0H));
        Assert.Contains(spy.Mirrors, m => m.State == K.s_PlayerWalkingSt0H);
        Assert.False(s.Terminal.bTriggering);
        Assert.False(s.Terminal.bActive);
        Assert.Null(s.Terminal.ControllerWhoTakesIt);
    }

    [Fact]
    public void Moving_cancels_with_a_message_and_keeps_progress()
    {
        Setup s = Make();
        TestPC spy = s.Spy();
        ObjWorld.UseEntry(spy, s.Entry);
        spy.GotoState(K.s_Computer_Use);
        s.W.Run(4.2f);

        spy.aStrafe = -1001f;
        Assert.True(TerminalHackRules.UseShouldCancel(spy));
        spy.GotoState(K.s_Computer_FastLeave);

        Assert.Equal(1, s.W.Net.Count(K.MsgInterrupted));
        Assert.False(s.Terminal.bTriggering);
        Assert.Equal(6, s.Terminal.CaptureTime);
        Assert.False(s.Objective.ObjectiveTaken);
        Assert.True(s.Entry.bUsable);
    }

    [Fact]
    public void A_cancelled_hack_can_resume_immediately_with_kept_progress()
    {
        Setup s = Make();
        TestPC spy = s.Spy();
        ObjWorld.UseEntry(spy, s.Entry);
        spy.GotoState(K.s_Computer_Use);
        s.W.Run(3.5f);

        spy.GotoState(K.s_Computer_FastLeave);
        spy.GotoState(K.s_PlayerWalkingSt0H);

        Assert.Equal(7, s.Terminal.CaptureTime);
        Assert.True(s.Entry.CanUse(spy));
        Assert.Equal(SComputerObjectiveTrigger.SKIN_Triggering, s.Terminal.SkinState);
        Assert.Equal(EMenuRelease.InteractionInitSent, ObjWorld.UseEntry(spy, s.Entry));
        spy.GotoState(K.s_Computer_Use);

        Assert.Same(spy, s.Terminal.ControllerWhoTakesIt);
        Assert.True(s.Terminal.bTriggering);
        Assert.Equal(7f, spy.TimerRate);
        Assert.False(s.Entry.CanUse(spy));
    }

    [Theory]
    [InlineData(1000f, 1000f, 0, false)]
    [InlineData(1000.5f, 0f, 0, true)]
    [InlineData(0f, -1000.5f, 0, true)]
    [InlineData(0f, 0f, 1, true)]
    public void Terminal_cancel_threshold(float strafe, float forward, byte fireMode, bool cancels)
    {
        var w = new ObjWorld();
        TestPC pc = w.AddPlayer(spy: true, default);
        pc.aStrafe = strafe;
        pc.aForward = forward;
        pc.bFireMode = fireMode;

        Assert.Equal(cancels, TerminalHackRules.UseShouldCancel(pc));
    }

    [Fact]
    public void Leaving_the_use_state_stops_every_terminal_within_200()
    {
        var w = new ObjWorld();
        SComputerObjectiveTrigger? a = null;
        SComputerObjectiveTrigger? b = null;
        SComputerObjectiveTrigger? far = null;
        w.Load(x =>
        {
            a = x.Place(new SComputerObjectiveTrigger(), new FVector(0f, 0f, 0f));
            b = x.Place(new SComputerObjectiveTrigger(), new FVector(0f, 150f, 0f));
            far = x.Place(new SComputerObjectiveTrigger(), new FVector(0f, 400f, 0f));
        });
        foreach (SComputerObjectiveTrigger t in new[] { a!, b!, far! })
        {
            t.eventReset();
            t.eventTriggering(true);
        }

        TestPC spy = w.AddPlayer(spy: true, new FVector(64f, 0f, 0f));
        spy.GotoState(K.s_Computer_Use);
        spy.GotoState(K.s_PlayerWalkingSt0H);

        Assert.False(a!.bTriggering);
        Assert.False(b!.bTriggering);
        Assert.True(far!.bTriggering);
    }

    [Fact]
    public void Use_state_without_a_terminal_only_sets_the_fallback_timer()
    {
        var w = new ObjWorld();
        TestPC spy = w.AddPlayer(spy: true, default);

        spy.GotoState(K.s_Computer_Use);

        Assert.Equal(3f, spy.TimerRate);
        Assert.True(spy.bTimerLoop);
    }

    [Fact]
    public void Damage_while_entering_leaves_and_damage_while_hacking_always_warns()
    {
        Setup s = Make();
        TestPC spy = s.Spy();

        TerminalHackRules.EnterTakeDamage(spy);
        Assert.True(spy.IsInState(K.s_Computer_Leave));
        Assert.Equal(1, s.W.Net.Count(K.MsgInterrupted));

        TerminalHackRules.UseTakeDamageMessage(spy);
        Assert.Equal(2, s.W.Net.Count(K.MsgInterrupted));
    }

    [Fact]
    public void Timer_for_a_dead_spy_takes_nothing()
    {
        Setup s = Make();
        TestPC spy = s.Spy();
        ObjWorld.UseEntry(spy, s.Entry);
        spy.GotoState(K.s_Computer_Use);
        spy.Pawn!.Health = 0;

        s.W.Run(10.5f);

        Assert.False(s.Objective.ObjectiveTaken);
    }

    [Fact]
    public void UseComputer_needs_a_living_spy()
    {
        Setup s = Make();
        TestPC spy = s.Spy();
        spy.Pawn!.Health = 0;

        TerminalHackRules.UseComputer(spy);

        Assert.False(spy.IsInState(K.s_Computer_GoTo));
    }

    // ---- locks (rules 28 and 29) --------------------------------------------------------------------------------

    [Fact]
    public void Lock_kicks_the_hacker_and_UnLock_restores_an_open_terminal()
    {
        Setup s = Make();
        TestPC spy = s.Spy();
        s.Terminal.ControllerWhoTakesIt = spy;
        s.Terminal.eventTriggering(true);
        spy.GotoState(K.s_Computer_Use);

        s.Terminal.eventLock();

        Assert.True(s.Terminal.IsInState(K.s_TemporaryInactive));
        Assert.True(s.Terminal.bLocked);
        Assert.False(s.Terminal.bActive);
        Assert.False(s.Terminal.bTriggering);
        Assert.Equal(SComputerObjectiveTrigger.SKIN_Locked, s.Terminal.SkinState);
        Assert.True(spy.IsInState(K.s_PlayerWalkingSt0H));

        s.Terminal.eventLock();
        s.Terminal.eventTriggering(true);
        Assert.False(s.Terminal.bTriggering);

        s.Terminal.eventUnLock();

        Assert.True(s.Terminal.IsInState(K.s_Initial));
        Assert.True(s.Terminal.bActive);
        Assert.False(s.Terminal.bLocked);
        Assert.Equal(SComputerObjectiveTrigger.SKIN_Idle, s.Terminal.SkinState);
    }

    [Fact]
    public void UnLock_of_a_taken_objective_deactivates_with_the_hacked_skin()
    {
        Setup s = Make();
        s.Terminal.eventLock();
        s.Objective.ObjectiveTaken = true;

        s.Terminal.eventUnLock();

        // The native Desactivate (rule 36) only clears the flags in s_TemporaryInactive, so the terminal stays there.
        Assert.Equal(SComputerObjectiveTrigger.SKIN_Hacked, s.Terminal.SkinState);
        Assert.True(s.Terminal.IsInState(K.s_TemporaryInactive));
        Assert.False(s.Terminal.bActive);
        Assert.False(s.Terminal.bLocked);
    }

    [Fact]
    public void Lock_of_a_taken_objective_does_nothing_and_reset_while_locked_keeps_the_lock()
    {
        Setup s = Make();
        s.Objective.ObjectiveTaken = true;
        s.Terminal.eventLock();
        Assert.False(s.Terminal.bLocked);

        s.Objective.ObjectiveTaken = false;
        s.Terminal.eventLock();
        s.Terminal.CaptureTime = 3;
        s.Terminal.eventReset();

        Assert.True(s.Terminal.bLocked);
        Assert.Equal(10, s.Terminal.CaptureTime);
        Assert.True(s.Terminal.IsInState(K.s_TemporaryInactive));
    }
}
