using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Objectives;
using OpenScct.Game.SBase;
using Xunit;

namespace OpenScct.Game.Tests.Objectives;

// spec/objectives/switch-hack.md
public sealed class SwitchHackTests
{
    // A crane-like panel: 5 s hack, spies only, once, fires "blast" (synthetic values in the shape of the spec table).
    private static SInterrupteur Panel(ObjWorld w, int hack = 5, bool once = true, Name evt = default, FRotator rotation = default)
    {
        var sw = new SInterrupteur
        {
            iHackingTime = hack,
            bTriggerOnceOnly = once,
            UsableBy = Usable.U_SPAWNOFFENSE,
            UseRelativeLocation = new FVector(40f, 0f, 0f),
            fInteractionRadius = 60f,
            fInteractionHeight = 120f,
            bInteractInFront = true,
            sWarnHackingText = "Crane Control",
            sDescriptionText = "Activate",
            Event = evt.IsNone ? "blast" : evt,
        };
        return w.Place(sw, new FVector(0f, 0f, 0f), rotation);
    }

    private static (ObjWorld w, SInterrupteur sw, EventCounter target) Loaded(int hack = 5, bool once = true)
    {
        var w = new ObjWorld();
        SInterrupteur? sw = null;
        w.Load(x => sw = Panel(x, hack, once));
        EventCounter target = w.Level.Spawn<EventCounter>(tag: "blast")!;
        return (w, sw!, target);
    }

    // A spy standing on the use spot, facing the panel, with the panel's entry selected in his menu.
    private static TestPC SpyAt(ObjWorld w, SInterrupteur sw)
    {
        TestPC pc = w.AddPlayer(spy: true, sw.Location + new FVector(40f, 0f, 0f), new FRotator(0, 32768, 0));
        ObjWorld.Touch(pc, sw.InteractionObj!);
        return pc;
    }

    // ---- setup (rules 1 to 4) ---------------------------------------------------------------------------------

    [Fact]
    public void PreBeginPlay_remembers_and_clears_the_secondary_objective_flag()
    {
        var w = new ObjWorld();
        SInterrupteur? sw = null;
        w.Load(x =>
        {
            sw = Panel(x);
            sw.bSecondaryObjective = true;
        });

        Assert.True(sw!.bInitialSecondaryObjective);
        Assert.True(sw.bSecondaryObjective); // PostNetBeginPlay mirrors the entry's bUsable (true)
    }

    [Fact]
    public void PostBeginPlay_spawns_the_switch_entry_with_the_switch_settings()
    {
        (ObjWorld _, SInterrupteur sw, _) = Loaded();
        var entry = Assert.IsType<STriggerInteraction>(sw.InteractionObj);

        Assert.Equal(0, entry.iPriority); // the switch's own iPriority 0, not the class's 20 (rule 2)
        Assert.True(entry.bHackable);
        Assert.False(entry.bMaintain);
        Assert.True(entry.bInteractInFront);
        Assert.Equal(Usable.U_SPAWNOFFENSE, entry.UsableBy);
        Assert.Equal((60f, 120f), (entry.ItRadius, entry.ItHeight));
        Assert.Same(sw, entry.OwnerInterrupteur);
        Assert.Same(sw, entry.Owner);
    }

    [Fact]
    public void PostNetBeginPlay_copies_the_reuse_time_and_PostBeginPlay_the_active_state()
    {
        var w = new ObjWorld();
        SInterrupteur? sw = null;
        w.Load(x =>
        {
            sw = Panel(x);
            sw.iReusableTime = 20;
            sw.bInitialyActive = true;
        });

        Assert.Equal(20, sw!.InteractionObj!.iReusableTime);
        Assert.True(sw.TriggerInteraction!.bIsActive);
    }

    [Fact]
    public void ForceTriggerableEvenIfMoving_is_the_hold_flag()
    {
        var sw = new SInterrupteur { bMaintainsSwitch = true };

        Assert.True(sw.ForceTriggerableEvenIfMoving());
    }

    // ---- enabling by events (rules 5 and 6) -------------------------------------------------------------------

    [Fact]
    public void Trigger_toggles_usability_and_UnTrigger_clears_it()
    {
        (ObjWorld w, SInterrupteur sw, _) = Loaded();
        sw.Tag = "hackvolet01";

        w.Level.TriggerEvent("hackvolet01", null, null);
        Assert.False(sw.InteractionObj!.bUsable);
        w.Level.TriggerEvent("hackvolet01", null, null);
        Assert.True(sw.InteractionObj.bUsable);
        w.Level.UntriggerEvent("hackvolet01", null, null);
        Assert.False(sw.InteractionObj.bUsable);
    }

    [Fact]
    public void Trigger_mirrors_usability_into_a_secondary_objective()
    {
        var w = new ObjWorld();
        SInterrupteur? sw = null;
        w.Load(x =>
        {
            sw = Panel(x);
            sw.bSecondaryObjective = true;
            sw.Tag = "sec";
        });

        w.Level.TriggerEvent("sec", null, null);

        Assert.False(sw!.bSecondaryObjective);
    }

    [Fact]
    public void Switch_manager_sets_the_team_of_its_switches()
    {
        var w = new ObjWorld();
        SInterrupteur? sw = null;
        w.Load(x => sw = Panel(x));
        var manager = w.Level.Spawn<SInterrupteurManager>(tag: "teams")!;
        manager.SInterrupteurList.Add(new FInterrupteurValue
        {
            Interrupteur = sw,
            TriggeredUsability = Usable.U_SPAWNDEFENSE,
            UntriggeredUsability = Usable.U_BOTH,
        });

        w.Level.TriggerEvent("teams", null, null);
        Assert.Equal(Usable.U_SPAWNDEFENSE, sw!.InteractionObj!.UsableBy);
        w.Level.UntriggerEvent("teams", null, null);
        Assert.Equal(Usable.U_BOTH, sw.InteractionObj.UsableBy);
    }

    // ---- plain switches and HandleEventInfo (rules 7, 8 and 21) ---------------------------------------------------

    [Fact]
    public void Plain_switch_fires_its_event_on_every_use_and_plays_its_sound()
    {
        (ObjWorld w, SInterrupteur sw, EventCounter target) = Loaded(hack: 0, once: false);
        TestPC pc = SpyAt(w, sw);

        Assert.Equal(EMenuRelease.PressButton, ObjWorld.UseEntry(pc, sw.InteractionObj!));
        InteractionMenuRules.PressButtonAnimEnd(pc);
        InteractionMenuRules.PressButtonAnimEnd(pc);

        Assert.Equal(2, target.Triggers);
        Assert.Same(pc.Pawn, target.LastInstigator);
        Assert.Contains(w.World.Events.Current.ToArray(), e => e.Kind == SimEventKind.PlaySound && e.ActorId == sw.ActorId);
    }

    [Fact]
    public void TriggerControl_alternates_trigger_and_untrigger()
    {
        (ObjWorld w, SInterrupteur sw, EventCounter target) = Loaded(hack: 0, once: false);
        sw.TriggerMethode = TriggerType.TriggerControl;
        TestPC pc = SpyAt(w, sw);

        for (int i = 0; i < 3; i++)
        {
            InteractionMenuRules.InteractionInit(pc, sw.InteractionObj, 0);
        }

        Assert.Equal((2, 1), (target.Triggers, target.UnTriggers));
    }

    [Fact]
    public void UnTriggerOnly_only_untriggers()
    {
        (ObjWorld w, SInterrupteur sw, EventCounter target) = Loaded(hack: 0, once: false);
        sw.TriggerMethode = TriggerType.UnTriggerOnly;
        TestPC pc = SpyAt(w, sw);

        InteractionMenuRules.InteractionInit(pc, sw.InteractionObj, 0);

        Assert.Equal((0, 1), (target.Triggers, target.UnTriggers));
    }

    [Fact]
    public void Once_only_switch_is_unusable_after_it_fires()
    {
        (ObjWorld w, SInterrupteur sw, EventCounter target) = Loaded(hack: 0, once: true);
        TestPC pc = SpyAt(w, sw);

        InteractionMenuRules.InteractionInit(pc, sw.InteractionObj, 0);

        Assert.Equal(1, target.Triggers);
        Assert.False(sw.InteractionObj!.CanUse(pc));
    }

    // ---- the hack (rules 7, 9 to 16) --------------------------------------------------------------------------

    [Fact]
    public void Using_a_hackable_panel_sends_the_spy_to_the_use_spot_without_firing()
    {
        (ObjWorld w, SInterrupteur sw, EventCounter target) = Loaded();
        TestPC pc = SpyAt(w, sw);

        Assert.Equal(EMenuRelease.InteractionInitSent, ObjWorld.UseEntry(pc, sw.InteractionObj!));

        Assert.True(pc.IsInState(K.s_Hack_GoTo));
        Assert.Equal(0, target.Triggers);
        Assert.DoesNotContain(w.World.Events.Current.ToArray(), e => e.Kind == SimEventKind.PlaySound);
    }

    [Fact]
    public void Use_spot_is_in_front_of_the_switch_and_raised_for_a_crouched_spy()
    {
        var w = new ObjWorld();
        SInterrupteur? sw = null;
        w.Load(x => sw = Panel(x, rotation: new FRotator(0, 16384, 0)));
        TestPC pc = w.AddPlayer(spy: true, new FVector(0f, 30f, -5f));
        pc.IManager!.AddInteractionObj(sw!.InteractionObj);
        pc.IManager.SelectedInteractions = pc.IManager.IndexOf(sw.InteractionObj!);

        Assert.True(SwitchHackRules.GoToBeginState(pc, out FVector facing));
        FVector spot = ((SBasePawn)pc.Pawn!).vVandammePos;
        Assert.Equal(0f, spot.X, 3);
        Assert.Equal(40f, spot.Y, 3);
        Assert.Equal(-5f, spot.Z);
        Assert.True(facing.Y < -0.99f); // yaw 16384 + 32768: the spy faces back at the panel

        pc.Pawn!.bIsCrouched = true;
        SwitchHackRules.GoToBeginState(pc, out _);
        Assert.Equal(-5f + 27.5f, ((SBasePawn)pc.Pawn).vVandammePos.Z);
    }

    [Fact]
    public void Walk_to_the_spot_aborts_without_a_switch_selection()
    {
        var w = new ObjWorld();
        TestPC pc = w.AddPlayer(spy: true, default);

        Assert.False(SwitchHackRules.GoToBeginState(pc, out _));
    }

    [Fact]
    public void Hack_start_warns_the_mercs_tells_everyone_and_marks_the_entry_in_use()
    {
        (ObjWorld w, SInterrupteur sw, _) = Loaded();
        TestPC spy = SpyAt(w, sw);
        TestPC merc = w.AddPlayer(spy: false, new FVector(500f, 0f, 0f));
        spy.myHUD = w.Level.Spawn<SHud>(owner: spy);

        ObjWorld.UseEntry(spy, sw.InteractionObj!);
        spy.GotoState(K.s_Hack_Enter); // Interaction(entry, 2)

        STriggerInteraction entry = sw.TriggerInteraction!;
        Assert.True(entry.IsInState(K.s_Hacking));
        Assert.True(entry.bInUse);
        Assert.Same(merc, Assert.Single(w.Net.To(K.MsgWarnHacking)));
        Assert.Same(sw, w.Net.Messages.Find(m => m.Switch == K.MsgWarnHacking).Obj);
        Assert.Equal(2, w.Net.Count(K.MsgHackingInProgress));
        Assert.Contains(("ActorHud", (Actor)entry, true), w.Net.Multicasts);
        Assert.Contains(entry, ((SHud)spy.myHUD!).ActorToDraw);
        Assert.False(entry.CanUse(merc));
    }

    [Fact]
    public void No_warning_text_means_no_merc_warning()
    {
        (ObjWorld w, SInterrupteur sw, _) = Loaded();
        sw.sWarnHackingText = "";
        TestPC spy = SpyAt(w, sw);
        w.AddPlayer(spy: false, new FVector(500f, 0f, 0f));

        ObjWorld.UseEntry(spy, sw.InteractionObj!);
        spy.GotoState(K.s_Hack_Enter);

        Assert.Equal(0, w.Net.Count(K.MsgWarnHacking));
        Assert.Equal(2, w.Net.Count(K.MsgHackingInProgress));
    }

    [Fact]
    public void Full_hack_fires_the_event_once_after_the_hack_time()
    {
        (ObjWorld w, SInterrupteur sw, EventCounter target) = Loaded(hack: 5);
        TestPC spy = SpyAt(w, sw);

        ObjWorld.UseEntry(spy, sw.InteractionObj!);
        spy.GotoState(K.s_Hack_Enter);
        spy.GotoState(K.s_Hack_Use);
        Assert.Equal(5f, spy.TimerRate);
        Assert.True(spy.bTimerLoop);
        Assert.True(sw.TriggerInteraction!.MinTriggerTime > 0f);

        w.Run(4.9f);
        Assert.Equal(0, target.Triggers);
        w.Run(0.3f);

        Assert.Equal(1, target.Triggers);
        Assert.True(spy.IsInState(K.s_Hack_Leave));
        STriggerInteraction entry = sw.TriggerInteraction!;
        Assert.False(entry.IsInState(K.s_Hacking));
        Assert.False(entry.bInUse);
        Assert.Equal(0f, entry.MinTriggerTime);
        Assert.Contains(("ActorHud", (Actor)entry, false), w.Net.Multicasts);
        Assert.Null(spy.UsedInterraction);
        Assert.False(entry.CanUse(spy)); // once only
    }

    [Fact]
    public void Cancelled_hack_fires_nothing_and_can_be_retried()
    {
        (ObjWorld w, SInterrupteur sw, EventCounter target) = Loaded(hack: 5);
        TestPC spy = SpyAt(w, sw);
        ObjWorld.UseEntry(spy, sw.InteractionObj!);
        spy.GotoState(K.s_Hack_Enter);
        spy.GotoState(K.s_Hack_Use);
        w.Run(2f);

        spy.aForward = 1001f;
        Assert.True(SwitchHackRules.UseShouldCancel(spy));
        spy.GotoState(K.s_Hack_FastLeave);

        Assert.Equal(0, target.Triggers);
        Assert.False(sw.TriggerInteraction!.IsInState(K.s_Hacking));
        Assert.True(sw.InteractionObj!.CanUse(spy));
    }

    [Theory]
    [InlineData(1000f, 0f, 0, false)]
    [InlineData(-1001f, 0f, 0, true)]
    [InlineData(0f, 1001f, 0, true)]
    [InlineData(0f, 0f, 1, true)]
    public void Hack_cancel_threshold(float strafe, float forward, byte fireMode, bool cancels)
    {
        var w = new ObjWorld();
        TestPC pc = w.AddPlayer(spy: true, default);
        pc.aStrafe = strafe;
        pc.aForward = forward;
        pc.bFireMode = fireMode;

        Assert.Equal(cancels, SwitchHackRules.UseShouldCancel(pc));
        Assert.Equal(cancels && fireMode == 0, SwitchHackRules.MaintainShouldCancel(pc));
    }

    [Fact]
    public void Hack_duration_comes_from_the_first_hackable_switch_within_200()
    {
        var w = new ObjWorld();
        SInterrupteur? near = null;
        w.Load(x =>
        {
            near = Panel(x, hack: 4);
            x.Place(new SInterrupteur { iHackingTime = 9 }, new FVector(250f, 0f, 0f));
        });
        TestPC pc = w.AddPlayer(spy: true, new FVector(40f, 0f, 0f));

        Assert.Same(near, SwitchHackRules.FindHackableSwitch(pc));
        pc.GotoState(K.s_Hack_Use);
        Assert.Equal(4f, pc.TimerRate);

        pc.Pawn!.Location = new FVector(1000f, 0f, 0f);
        pc.GotoState(K.s_PlayerWalkingSt0H);
        pc.GotoState(K.s_Hack_Use);
        Assert.Equal(3f, pc.TimerRate); // nothing near: looping 3 s fallback
    }

    [Fact]
    public void Second_user_during_a_hack_is_refused()
    {
        (ObjWorld w, SInterrupteur sw, _) = Loaded();
        TestPC first = SpyAt(w, sw);
        TestPC second = SpyAt(w, sw);
        ObjWorld.UseEntry(first, sw.InteractionObj!);
        first.GotoState(K.s_Hack_Enter);

        second.PreviousState = K.s_PlayerWalkingCr0H;
        InteractionMenuRules.InteractionInit(second, sw.InteractionObj, 0);

        Assert.True(second.IsInState(K.s_PlayerWalkingCr0H));
    }

    // ---- hold switches (rule 18) ------------------------------------------------------------------------------

    [Fact]
    public void Hold_switch_fires_when_the_hold_starts_and_when_it_ends()
    {
        var w = new ObjWorld();
        SInterrupteur? sw = null;
        w.Load(x =>
        {
            sw = Panel(x, hack: 0, once: false);
            sw.bMaintainsSwitch = true;
        });
        EventCounter target = w.Level.Spawn<EventCounter>(tag: "blast")!;
        TestPC spy = SpyAt(w, sw!);

        ObjWorld.UseEntry(spy, sw!.InteractionObj!);
        Assert.True(spy.IsInState(K.s_Maintain_GoTo));
        SwitchHackRules.EnterStart(spy);
        Assert.True(sw.TriggerInteraction!.IsInState(K.s_Maintain_Use));
        SwitchHackRules.MaintainUseBeginState(spy);
        Assert.Equal(1, target.Triggers);
        Assert.True(sw.TriggerInteraction.IsInState(K.s_Maintain_Use));

        SwitchHackRules.PostAndForget(spy, success: false);

        Assert.Equal(2, target.Triggers);
        Assert.False(sw.TriggerInteraction.IsInState(K.s_Maintain_Use));
    }

    // ---- delayed switches (rule 19) ---------------------------------------------------------------------------

    [Fact]
    public void Delayed_switch_fires_after_each_delay_and_refuses_users_meanwhile()
    {
        var w = new ObjWorld();
        SInterrupteur? sw = null;
        w.Load(x =>
        {
            sw = Panel(x, hack: 0, once: false);
            sw.iAttenteAvantActivation = 2;
            sw.iAttenteAvantDesactivation = 3;
            sw.TriggerMethode = TriggerType.TriggerControl;
        });
        EventCounter target = w.Level.Spawn<EventCounter>(tag: "blast")!;
        TestPC spy = SpyAt(w, sw!);
        STriggerInteraction entry = sw!.TriggerInteraction!;

        InteractionMenuRules.InteractionInit(spy, entry, 0);
        Assert.True(entry.IsInState(K.WaitingBeforeActivation));
        Assert.Equal(0, target.Triggers);

        spy.PreviousState = K.s_PlayerWalkingCr0H;
        InteractionMenuRules.InteractionInit(spy, entry, 0);
        Assert.True(spy.IsInState(K.s_PlayerWalkingCr0H));

        w.Run(2.1f);
        Assert.Equal(1, target.Triggers);
        Assert.True(entry.IsInState(K.WaitingBeforeDesactivation));
        w.Run(3.1f);
        Assert.Equal(1, target.UnTriggers);
        Assert.False(entry.IsInState(K.WaitingBeforeDesactivation));
    }
}
