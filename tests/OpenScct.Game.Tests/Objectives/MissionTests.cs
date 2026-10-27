using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Objectives;
using OpenScct.Game.SBase;
using Xunit;

namespace OpenScct.Game.Tests.Objectives;

// spec/objectives/missions.md
public sealed class MissionTests
{
    private sealed class Story
    {
        public required ObjWorld W;
        public required SMission Mission;
        public required List<SObjective> Objectives;
        public required List<SComputerObjectiveTrigger> Terminals;
    }

    // A Story-like mission: n terminals, each the only trigger of one objective, of which `minimum` are needed.
    private static Story Build(int n = 6, int minimum = 4, bool chained = false, int random = 0, EGameMode mode = EGameMode.GM_Multi,
        ulong seed = 1)
    {
        var w = new ObjWorld(seed);
        var objectives = new List<SObjective>();
        var terminals = new List<SComputerObjectiveTrigger>();
        SMission? mission = null;
        w.Load(x =>
        {
            mission = new SMission
            {
                GameMode = mode,
                bChained = chained,
                MinimumObjectives = minimum,
                RandomObjectives = random,
                Description = "Accomplish the objectives",
                DescriptionDEF = "Protect the objectives",
            };
            for (int i = 0; i < n; i++)
            {
                var t = x.Place(new SComputerObjectiveTrigger { DominationCaptureTime = 10 }, new FVector(i * 1000f, 0f, 0f));
                var o = new SObjective { ObjectiveName = "Terminal " + i };
                o.Triggers.Add(t);
                x.Place(o, default);
                mission.Objectives.Add(o);
                objectives.Add(o);
                terminals.Add(t);
            }

            x.Place(mission, default);
        });
        return new Story { W = w, Mission = mission!, Objectives = objectives, Terminals = terminals };
    }

    // SGameInfo.StartMatch's mission part (the game-rules port owns it; rules 2 to 4).
    private static void StartMatch(Level level, EGameMode mode)
    {
        foreach (SMission m in level.AllActors<SMission>())
        {
            m.Desactivate();
        }

        level.Info.GRI!.NumObjectives = 0;
        foreach (SMission m in level.AllActors<SMission>())
        {
            if (mode != EGameMode.GM_MercDeathMatch && m.GameMode == mode)
            {
                m.Reset();
            }
        }
    }

    private static void Take(Story s, int i, TestPC by)
    {
        SComputerObjectiveTrigger t = s.Terminals[i];
        t.ControllerWhoTakesIt = by;
        t.eventTakeObjective();
    }

    // ---- level start (rule 38) ----------------------------------------------------------------------------------

    [Fact]
    public void Objectives_start_taken_and_link_their_triggers()
    {
        Story s = Build();

        Assert.All(s.Objectives, o => Assert.True(o.ObjectiveTaken));
        Assert.All(s.Objectives, o => Assert.Equal(1000, o.RemainingTime));
        Assert.Same(s.Objectives[2], s.Terminals[2].Objective);
        Assert.True(s.Mission.bChained == false);
        Assert.True(new SMission().bChained);
    }

    // ---- reset (rules 6 to 14) ------------------------------------------------------------------------------------

    [Fact]
    public void Story_reset_needs_four_of_six_and_announces_to_both_teams()
    {
        Story s = Build();
        TestPC spy = s.W.AddPlayer(spy: true, new FVector(0f, 5000f, 0f));
        TestPC merc = s.W.AddPlayer(spy: false, new FVector(0f, 6000f, 0f));

        StartMatch(s.W.Level, EGameMode.GM_Multi);

        Assert.False(s.Mission.ObjectiveTaken);
        Assert.True(s.Mission.activated);
        Assert.Equal(4, s.Mission.ObjectivesLeft);
        Assert.Equal(4, s.Mission.TotalObjectivesLeft);
        Assert.Equal(4, s.W.Gri.NumObjectives);
        Assert.Equal(4, MissionRules.GetObjectivesLeft(s.W.Level));
        Assert.All(s.Objectives, o => Assert.False(o.ObjectiveTaken));
        Assert.All(s.Objectives, o => Assert.Same(s.Mission, o.ParentObjective));
        Assert.All(s.Terminals, t => Assert.True(t.bActive && !t.bHidden && t.bCollideActors));
        Assert.Same(spy, Assert.Single(s.W.Net.To(K.MsgMissionSpy)));
        Assert.Same(merc, Assert.Single(s.W.Net.To(K.MsgMissionMerc)));
        Assert.Same(s.Mission, s.W.Net.Messages.Find(m => m.Switch == K.MsgMissionSpy).Obj);
    }

    [Fact]
    public void Missions_of_other_modes_stay_deactivated()
    {
        Story s = Build(mode: EGameMode.GM_DiskHunt);

        StartMatch(s.W.Level, EGameMode.GM_Multi);

        Assert.True(s.Mission.ObjectiveTaken);
        Assert.All(s.Objectives, o => Assert.True(o.ObjectiveTaken));
        Assert.All(s.Terminals, t => Assert.False(t.bActive));
        Assert.All(s.Terminals, t => Assert.True(t.bHidden));
        Assert.Equal(0, s.W.Gri.NumObjectives);
        Assert.True(SObjective.AllObjectivesTaken(s.W.Level));
    }

    [Fact]
    public void Merc_deathmatch_resets_no_mission()
    {
        Story s = Build(mode: EGameMode.GM_MercDeathMatch);

        StartMatch(s.W.Level, EGameMode.GM_MercDeathMatch);

        Assert.True(s.Mission.ObjectiveTaken);
    }

    [Theory]
    [InlineData(0, 6)]
    [InlineData(6, 6)]
    [InlineData(9, 6)]
    [InlineData(2, 2)]
    public void Minimum_clamps_the_count_only_below_the_number_of_objectives(int minimum, int expected)
    {
        Story s = Build(minimum: minimum);

        s.Mission.Reset();

        Assert.Equal(expected, s.Mission.ObjectivesLeft);
    }

    [Fact]
    public void Chained_mission_offers_one_objective_at_a_time()
    {
        Story s = Build(n: 3, minimum: 0, chained: true);
        TestPC spy = s.W.AddPlayer(spy: true, new FVector(0f, 5000f, 0f));

        s.Mission.Reset();

        Assert.False(s.Objectives[0].ObjectiveTaken);
        Assert.True(s.Objectives[1].ObjectiveTaken);
        Assert.True(s.Objectives[2].ObjectiveTaken);
        Assert.Equal(0, s.Mission.CurrentObjectiveIndex);
        Assert.Equal(3, s.Mission.ObjectivesLeft);
        Assert.Equal(2, s.W.Net.Count(K.MsgMissionSpy)); // the mission, then its first child
        Assert.Same(s.Objectives[0], s.W.Net.Messages.FindLast(m => m.Switch == K.MsgMissionSpy).Obj);

        Take(s, 0, spy);

        Assert.Equal(1, s.Mission.CurrentObjectiveIndex);
        Assert.False(s.Objectives[1].ObjectiveTaken);
        Assert.True(s.Terminals[1].bActive);
        Assert.Same(s.Objectives[1], s.W.Net.Messages.FindLast(m => m.Switch == K.MsgMissionSpy).Obj);
        Assert.False(s.Mission.ObjectiveTaken);

        Take(s, 1, spy);
        Take(s, 2, spy);

        Assert.True(s.Mission.ObjectiveTaken);
        Assert.True(SObjective.AllObjectivesTaken(s.W.Level));
    }

    [Fact]
    public void Chained_mission_ignores_the_minimum_and_ends_after_its_last_child()
    {
        // game 0x10ABFFB0: a chained mission is taken only once CurrentObjectiveIndex passes the last child;
        // MinimumObjectives is read only on the unchained path.
        Story s = Build(n: 4, minimum: 2, chained: true);
        TestPC spy = s.W.AddPlayer(spy: true, default);

        s.Mission.Reset();
        Assert.Equal(4, s.Mission.ObjectivesLeft);

        Take(s, 0, spy);
        Take(s, 1, spy);
        Assert.False(s.Mission.ObjectiveTaken);
        Assert.Equal(2, s.Mission.ObjectivesLeft);

        Take(s, 2, spy);
        Take(s, 3, spy);
        Assert.True(s.Mission.ObjectiveTaken);
        Assert.Equal(0, s.Mission.ObjectivesLeft);
    }

    [Fact]
    public void Random_mission_resets_only_the_drawn_objectives()
    {
        Story s = Build(n: 5, minimum: 0, random: 2, seed: 7);

        s.Mission.Reset();

        Assert.Equal(2, s.Mission.ObjectivesLeft);
        Assert.Equal(2, s.Objectives.FindAll(o => !o.ObjectiveTaken).Count);
        Assert.Equal(3, s.Terminals.FindAll(t => !t.bActive).Count);

        // Same seed, same draw.
        Story again = Build(n: 5, minimum: 0, random: 2, seed: 7);
        again.Mission.Reset();
        for (int i = 0; i < 5; i++)
        {
            Assert.Equal(s.Objectives[i].ObjectiveTaken, again.Objectives[i].ObjectiveTaken);
        }
    }

    [Fact]
    public void Zone_mission_with_an_activation_time_resets_late_and_a_moded_mission_never_does()
    {
        Story zone = Build(minimum: 0, mode: EGameMode.GM_Undefined);
        zone.Mission.ActivationTime = 3;

        zone.Mission.Reset();
        Assert.True(zone.Mission.bDelayedActivation);
        Assert.True(zone.Mission.ObjectiveTaken);
        Assert.Empty(zone.W.Net.Messages);

        zone.W.Run(3.1f);
        Assert.False(zone.Mission.ObjectiveTaken);
        Assert.All(zone.Objectives, o => Assert.False(o.ObjectiveTaken));
        Assert.Equal(0, zone.W.Gri.NumObjectives); // zone missions add nothing themselves

        Story moded = Build();
        moded.Mission.ActivationTime = 3;
        moded.Mission.Reset();
        Assert.Equal(0, moded.Mission.ActivationTime);
        Assert.False(moded.Mission.ObjectiveTaken);
    }

    [Fact]
    public void Reset_fires_AtResetEvent_at_the_end()
    {
        Story s = Build();
        s.Mission.AtResetEvent = "hackvolet01";
        EventCounter target = s.W.Level.Spawn<EventCounter>(tag: "hackvolet01")!;

        s.Mission.Reset();

        Assert.Equal(1, target.Triggers);
    }

    // ---- taking objectives (rules 23 to 30) ---------------------------------------------------------------------

    [Fact]
    public void Taking_an_objective_scores_counts_and_fires_its_event()
    {
        Story s = Build();
        TestPC spy = s.W.AddPlayer(spy: true, default);
        StartMatch(s.W.Level, EGameMode.GM_Multi);
        s.Objectives[0].Event = "taken0";
        EventCounter target = s.W.Level.Spawn<EventCounter>(tag: "taken0")!;
        s.Objectives[0].MinTriggerTime = 9f;

        Take(s, 0, spy);

        Assert.True(s.Objectives[0].ObjectiveTaken);
        Assert.False(s.Terminals[0].bActive);
        Assert.Contains((spy, K.AP_A_TakeObjective), s.W.Host.Points.ConvertAll(p => ((TestPC)p.Scorer!, p.Index)));
        Assert.Contains(s.Objectives[0], s.W.Host.Taken);
        Assert.Equal(1, ((STeamInfo)s.W.Gri.Teams[0]!).iNbObjectivesTaken);
        Assert.Equal(1, spy.PlayerReplicationInfo!.iNbObjectivesTaken);
        Assert.Equal(3, s.Mission.ObjectivesLeft);
        Assert.Equal(3, MissionRules.GetObjectivesLeft(s.W.Level));
        Assert.Equal(1, target.Triggers);
        Assert.Same(spy.Pawn, target.LastInstigator);
        Assert.Equal(0f, s.Objectives[0].MinTriggerTime);
    }

    [Fact]
    public void A_second_completion_is_ignored()
    {
        Story s = Build();
        TestPC spy = s.W.AddPlayer(spy: true, default);
        StartMatch(s.W.Level, EGameMode.GM_Multi);

        s.Objectives[0].NotifyTaken(spy, s.Terminals[0]);
        s.Objectives[0].NotifyTaken(spy, s.Terminals[0]);

        Assert.Equal(3, s.Mission.ObjectivesLeft);
        Assert.Equal(1, spy.PlayerReplicationInfo!.iNbObjectivesTaken);
    }

    [Fact]
    public void Untrigger_event_objectives_untrigger()
    {
        Story s = Build();
        TestPC spy = s.W.AddPlayer(spy: true, default);
        StartMatch(s.W.Level, EGameMode.GM_Multi);
        s.Objectives[1].Event = "door";
        s.Objectives[1].bUntriggerEvent = true;
        EventCounter door = s.W.Level.Spawn<EventCounter>(tag: "door")!;

        Take(s, 1, spy);

        Assert.Equal((0, 1), (door.Triggers, door.UnTriggers));
    }

    [Fact]
    public void Fourth_objective_completes_the_mission_and_deactivates_the_rest()
    {
        Story s = Build();
        TestPC spy = s.W.AddPlayer(spy: true, default);
        StartMatch(s.W.Level, EGameMode.GM_Multi);

        for (int i = 0; i < 3; i++)
        {
            Take(s, i, spy);
        }

        Assert.False(SObjective.AllObjectivesTaken(s.W.Level));
        Assert.False(s.Mission.ObjectiveTaken);

        Take(s, 3, spy);

        Assert.True(s.Mission.ObjectiveTaken);
        Assert.Equal(0, s.Mission.ObjectivesLeft);
        Assert.True(s.Objectives[4].ObjectiveTaken);
        Assert.True(s.Objectives[5].ObjectiveTaken);
        Assert.False(s.Terminals[4].bActive);
        Assert.False(s.Terminals[5].bActive);
        Assert.True(SObjective.AllObjectivesTaken(s.W.Level));
        Assert.Contains(s.Mission, s.W.Host.Taken);
        Assert.DoesNotContain(s.W.Host.Points, p => p.Index != K.AP_A_TakeObjective);
        Assert.Equal(4, s.W.Host.Points.Count); // the mission itself does not score (bScore false)
        Assert.Equal(4, spy.PlayerReplicationInfo!.iNbObjectivesTaken);
    }

    [Fact]
    public void Taking_a_trigger_without_a_taker_does_nothing()
    {
        Story s = Build();
        StartMatch(s.W.Level, EGameMode.GM_Multi);

        s.Terminals[0].ControllerWhoTakesIt = null;
        s.Terminals[0].eventTakeObjective();

        Assert.False(s.Objectives[0].ObjectiveTaken);
        Assert.False(s.Terminals[0].complete);
    }

    [Fact]
    public void Taking_records_the_time_and_the_stats()
    {
        Story s = Build();
        TestPC spy = s.W.AddPlayer(spy: true, default);
        StartMatch(s.W.Level, EGameMode.GM_Multi);
        s.W.Gri.ElapsedTime = 125;

        Take(s, 2, spy);

        Assert.Equal(125, s.Terminals[2].takenTime);
        Assert.Equal(125, s.W.Gri.LastTimeObjectif);
        Assert.Equal(new[] { 125 }, spy.PlayerReplicationInfo!.Stats!.Spy_ObjectivesTakenTime);
    }

    // ---- counters (rules 15 to 17) -----------------------------------------------------------------------------

    [Fact]
    public void Nested_mission_counts_and_totals()
    {
        var w = new ObjWorld();
        SMission? top = null;
        SMission? zone = null;
        w.Load(x =>
        {
            zone = new SMission { GameMode = EGameMode.GM_Undefined, bChained = false, MinimumObjectives = 1 };
            for (int i = 0; i < 3; i++)
            {
                var o = new SObjective();
                o.Triggers.Add(x.Place(new SComputerObjectiveTrigger(), new FVector(i * 500f, 0f, 0f)));
                x.Place(o, default);
                zone.Objectives.Add(o);
            }

            x.Place(zone, default);
            top = new SMission { GameMode = EGameMode.GM_Multi, bChained = false };
            var direct = new SObjective();
            direct.Triggers.Add(x.Place(new SComputerObjectiveTrigger(), new FVector(0f, 900f, 0f)));
            x.Place(direct, default);
            top.Objectives.Add(direct);
            top.Objectives.Add(zone);
            x.Place(top, default);
        });

        zone!.ComputeObjectivesLeft();
        Assert.Equal(1, zone.ObjectivesLeft);

        top!.Reset();
        Assert.Equal(1, top.ObjectivesLeft);
        Assert.Equal(1, zone.ObjectivesLeft);
        Assert.Equal(2, top.TotalObjectivesLeft);
        Assert.Equal(2, w.Gri.NumObjectives);

        zone.ObjectivesLeft = 0;
        zone.UpdateObjectivesLeft();
        Assert.Equal(1, top.TotalObjectivesLeft);
    }

    // ---- natives and HUD helpers (rules 19 to 22, 35 to 37) ------------------------------------------------------

    [Fact]
    public void NotifyTriggering_keeps_the_earliest_capture_end()
    {
        Story s = Build(n: 1, minimum: 0);
        StartMatch(s.W.Level, EGameMode.GM_Multi);
        s.W.Run(2f);

        s.Terminals[0].eventTriggering(true);
        Assert.Equal(s.Terminals[0].TriggerTime + 10f, s.Objectives[0].MinTriggerTime);

        s.Terminals[0].eventTriggering(false);
        Assert.Equal(0f, s.Objectives[0].MinTriggerTime);
        Assert.Contains(("ObjectiveHud", (Actor)s.Objectives[0], false), s.W.Net.Multicasts);
    }

    [Fact]
    public void SimulateBeingTook_uses_the_first_trigger_time_or_five_seconds()
    {
        Story s = Build(n: 1, minimum: 0);
        StartMatch(s.W.Level, EGameMode.GM_Multi);

        // GetTriggerTime (game 0x10AE8AF0) is 0 on a trigger that is not triggering, so nothing is added.
        s.Objectives[0].SimulateBeingTook();
        Assert.Equal(s.W.Level.TimeSeconds, s.Objectives[0].iSimulatedObjectiveIsBeingTookUntil);
        Assert.Contains(("ObjectiveHud", (Actor)s.Objectives[0], true), s.W.Net.Multicasts);

        s.Objectives[0].Triggers.Clear();
        s.Objectives[0].SimulateBeingTook();
        Assert.Equal(s.W.Level.TimeSeconds + 5f, s.Objectives[0].iSimulatedObjectiveIsBeingTookUntil);
    }

    [Fact]
    public void DoBroadcastLocalizedMessage_announces_a_child_of_a_moded_mission()
    {
        Story s = Build(n: 2, minimum: 0);
        TestPC spy = s.W.AddPlayer(spy: true, default);

        s.Mission.DoBroadcastLocalizedMessage(1);

        Assert.Same(s.Objectives[1], Assert.Single(s.W.Net.Messages, m => m.Switch == K.MsgMissionSpy).Obj);
        _ = spy;
    }

    [Fact]
    public void ScheduleReset_resets_when_the_timer_fires()
    {
        Story s = Build(n: 1, minimum: 0);

        s.Objectives[0].ScheduleReset(2);
        s.W.Run(2.1f);

        Assert.False(s.Objectives[0].ObjectiveTaken);
        Assert.True(s.Terminals[0].bActive);
    }
}
