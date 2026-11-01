using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.SBase;
using Xunit;

namespace OpenScct.Game.Tests.Game;

/// <summary>spec/game/death-lives-respawn.md: Killed, team lives, the death timeline, resign and the respawn weapon.</summary>
public class DeathLivesRespawnTests
{
    private const string Story = MatchLifecycleTests.SteeEStory;

    /// <summary>A network match on two-side starts, begun, with the given players spawned.</summary>
    internal static (GameWorld W, SPlayerController[] Players) Match(string options, params string[] joins)
    {
        // An objective still to take keeps a network round going (without one the first end check is a spy win).
        SObjective? open = null;
        GameWorld w = new GameWorld().Load(options, x =>
        {
            MatchLifecycleTests.TwoSides(x);
            open = x.Level.SpawnActor(new SObjective(), default, default);
        });
        open!.ObjectiveTaken = false;
        var players = new SPlayerController[joins.Length];
        for (int i = 0; i < joins.Length; i++)
        {
            players[i] = w.Join(joins[i]);
        }

        w.StartNetworkMatch(players[0]);
        return (w, players);
    }

    // ---- Killed (rules 1 to 3) ----------------------------------------------------------------------------------

    [Fact]
    public void An_enemy_kill_counts_a_death_a_life_and_a_kill()
    {
        (GameWorld w, SPlayerController[] p) = Match(Story, "?Name=S?Team=0", "?Name=M?Team=1");
        SPlayerController spy = p[0];
        SPlayerController merc = p[1];
        w.Game.Killed(merc, spy, spy.Pawn, SDamageType.SDTBase);
        Assert.Equal(1, spy.PlayerReplicationInfo!.Deaths);
        Assert.Equal(3, spy.PlayerReplicationInfo!.NumLives);
        Assert.Equal(1, merc.PlayerReplicationInfo!.Kills);
        Assert.Equal(3, w.Gri.TotalTeamLives[0]);
        Assert.Equal(w.Level.TimeSeconds + 2f, w.Game.BroadcastScoreTime);

        List<(PlayerController Target, GameCall Call)> deaths = w.Host.Messages((int)SDamageType.SDTBase);
        Assert.Equal(2, deaths.Count); // everyone hears it
        GameCall death = deaths[0].Call;
        Assert.Equal("SBase.SDeathMessage", death.Class.Path);
        Assert.Same(merc.PlayerReplicationInfo, death.Pri1);
        Assert.Same(spy.PlayerReplicationInfo, death.Pri2);
    }

    [Fact]
    public void Team_kills_and_suicides_only_count_in_the_statistics()
    {
        (GameWorld w, SPlayerController[] p) = Match(Story, "?Name=A?Team=0", "?Name=B?Team=0", "?Name=M?Team=1");
        w.Game.Killed(p[0], p[1], p[1].Pawn, SDamageType.SDTBase);
        Assert.Equal(0, p[0].PlayerReplicationInfo!.Kills);
        Assert.Equal(1, p[0].PlayerReplicationInfo!.Stats!.TeamKills);
        Assert.Equal(1, p[1].PlayerReplicationInfo!.Stats!.DeathFromTeammate);

        w.Game.Killed(p[2], p[2], p[2].Pawn, SDamageType.Suicided);
        Assert.Equal(1, p[2].PlayerReplicationInfo!.Stats!.Suicides);
        Assert.Equal(0, p[2].PlayerReplicationInfo!.Kills);
        Assert.Equal(1, p[2].PlayerReplicationInfo!.Deaths);
    }

    [Fact]
    public void In_merc_deathmatch_every_kill_counts_and_a_suicide_costs_a_kill()
    {
        (GameWorld w, SPlayerController[] p) = Match("?GameMode=7?LivesDEF=5", "?Name=A", "?Name=B");
        w.Game.Killed(p[0], p[1], p[1].Pawn, SDamageType.SDTBase);
        Assert.Equal(1, p[0].PlayerReplicationInfo!.Kills);
        w.Game.Killed(p[0], p[0], p[0].Pawn, SDamageType.SFell);
        Assert.Equal(0, p[0].PlayerReplicationInfo!.Kills);
    }

    [Fact]
    public void A_death_without_a_killer_gives_no_credit()
    {
        (GameWorld w, SPlayerController[] p) = Match(Story, "?Name=S?Team=0", "?Name=M?Team=1");
        w.Game.Killed(null, p[0], p[0].Pawn, SDamageType.SFell);
        Assert.Equal(3, p[0].PlayerReplicationInfo!.NumLives);
        Assert.Equal(0, p[1].PlayerReplicationInfo!.Kills);
        Assert.All(w.Host.Messages((int)SDamageType.SFell), m => Assert.Null(m.Call.Pri1));
    }

    [Fact]
    public void The_last_life_is_announced_to_the_victims_team_only()
    {
        (GameWorld w, SPlayerController[] p) = Match("?GameMode=5?LivesATT=1?LivesDEF=3", "?Name=S?Team=0", "?Name=T?Team=0", "?Name=M?Team=1");
        w.Game.Killed(p[2], p[0], p[0].Pawn, SDamageType.SDTBase);
        List<(PlayerController Target, GameCall Call)> lastLife = w.Host.Messages(56);
        Assert.Equal(2, lastLife.Count);
        Assert.All(lastLife, m => Assert.Equal(0, SGameInfo.TeamIndexOf(m.Target)));
        Assert.Equal("SBase.SGameMessages", lastLife[0].Call.Class.Path);
        Assert.Same(p[0].PlayerReplicationInfo, lastLife[0].Call.Pri1);
    }

    [Fact]
    public void A_crushing_kill_is_environmental_and_the_flag_carrier_killer_is_recorded()
    {
        (GameWorld w, SPlayerController[] p) = Match(Story, "?Name=S?Team=0", "?Name=M?Team=1");
        var flag = new SFlag();
        p[0].SBPawn!.CarriedFlag = flag;
        w.Game.Killed(p[1], p[0], p[0].Pawn, SDamageType.Crushed);
        Assert.Equal(1, p[1].PlayerReplicationInfo!.Stats!.EnvironmentalKills);
        Assert.Equal(1, p[0].PlayerReplicationInfo!.Stats!.DeathFromEnvironmental);
        Assert.Same(p[1], flag.CarrierKiller);
    }

    [Fact]
    public void A_kill_by_the_controller_of_a_damaging_object_is_environmental()
    {
        (GameWorld w, SPlayerController[] p) = Match(Story, "?Name=S?Team=0", "?Name=M?Team=1");
        w.Game.GPODamageInstigator = p[1].Pawn;
        w.Game.Killed(p[1], p[0], p[0].Pawn, SDamageType.SDamage_Explosion);
        Assert.Equal(1, p[1].PlayerReplicationInfo!.Stats!.EnvironmentalKills);
    }

    // ---- team lives (rule 4) and their meaning (rule 5) ---------------------------------------------------------

    [Fact]
    public void UpdateTeamNumLives_sums_each_side_and_skips_spectators()
    {
        (GameWorld w, SPlayerController[] p) = Match(Story, "?Name=S?Team=0", "?Name=T?Team=0", "?Name=M?Team=1");
        p[1].PlayerReplicationInfo!.bOnlySpectator = true;
        w.Game.UpdateTeamNumLives();
        Assert.Equal(4, w.Gri.TotalTeamLives[0]);
        Assert.Equal(3, w.Gri.TotalTeamLives[1]);
    }

    [Fact]
    public void Four_lives_allow_four_deaths_and_the_fourth_is_final()
    {
        (GameWorld w, SPlayerController[] p) = Match(Story, "?Name=S?Team=0", "?Name=M?Team=1", "?Name=N?Team=1");
        for (int i = 0; i < 3; i++)
        {
            w.Game.Killed(p[1], p[0], p[0].Pawn, SDamageType.SDTBase);
            Assert.False(w.Game.bGameEnded);
        }

        w.Game.Killed(p[1], p[0], p[0].Pawn, SDamageType.SDTBase);
        Assert.Equal(0, p[0].PlayerReplicationInfo!.NumLives);
        Assert.True(w.Game.bGameEnded);
        Assert.Equal(1, w.Gri.WinningTeam);
    }

    // ---- the death timeline (rules 6 to 14) ---------------------------------------------------------------------

    private static (float Spectate, float Respawn, List<(float T, bool Hidden)> Body) Timeline(GameWorld w, SPlayerController pc)
    {
        float t0 = w.Level.TimeSeconds;
        Pawn body = pc.Pawn!;
        float spectate = -1f;
        float respawn = -1f;
        var samples = new List<(float, bool)>();
        for (int i = 0; i < 40 * Sim.Hz && respawn < 0f; i++)
        {
            w.World.Step();
            float t = w.Level.TimeSeconds - t0;
            if (!body.bDeleteMe)
            {
                samples.Add((t, body.bHidden));
            }

            if (spectate < 0f && pc.IsInState("s_MultiSpectating_WhenKilled"))
            {
                spectate = t;
            }

            if (pc.Pawn is { } p && !ReferenceEquals(p, body))
            {
                respawn = t;
            }
        }

        return (spectate, respawn, samples);
    }

    [Fact]
    public void A_body_stays_5_s_blinks_2_5_s_then_the_player_spectates_for_the_respawn_delay()
    {
        (GameWorld w, SPlayerController[] p) = Match(Story, "?Name=S?Team=0", "?Name=M?Team=1");
        SPlayerController spy = p[0];
        w.Game.Killed(p[1], spy, spy.Pawn, SDamageType.SDTBase);
        spy.GotoState("s_Dead");
        Assert.Equal(0.2f, spy.TimerRate);
        Pawn body = spy.Pawn!;

        (float spectate, float respawn, List<(float T, bool Hidden)> samples) = Timeline(w, spy);

        Assert.All(samples.FindAll(s => s.T <= 5f), s => Assert.False(s.Hidden));
        Assert.Contains(samples, s => s.T > 5f && s.T < 7.5f && s.Hidden);
        Assert.Contains(samples, s => s.T > 5.5f && s.T < 7.3f && !s.Hidden);
        Assert.InRange(spectate, 7.5f, 7.75f);
        Assert.InRange(respawn, spectate + 10f, spectate + 10.1f);
        Assert.True(body.bDeleteMe);
        Assert.False(spy.PlayerReplicationInfo!.bIsSpectator);
        Assert.NotNull(spy.Pawn);
    }

    [Fact]
    public void The_hidden_body_loses_its_collision_and_the_player_becomes_a_spectator()
    {
        (GameWorld w, SPlayerController[] p) = Match(Story, "?Name=S?Team=0", "?Name=M?Team=1");
        SPlayerController spy = p[0];
        Pawn body = spy.Pawn!;
        body.bCollideActors = true;
        spy.GotoState("s_Dead");
        w.Run(7.8f);
        Assert.True(spy.IsInState("s_MultiSpectating_WhenKilled"));
        Assert.True(body.bHidden);
        Assert.False(body.bCollideActors);
        Assert.True(spy.PlayerReplicationInfo!.bIsSpectator);
        Assert.InRange(spy.fRespawnDate, w.Level.TimeSeconds + 9.7f, w.Level.TimeSeconds + 10f); // the respawn due time
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Death_during_an_older_input_command_keeps_the_body_and_respawn_timeline(int victim)
    {
        (GameWorld w, SPlayerController[] players) = Match(Story + "?TimesRespawnsDEF=10", "?Name=S?Team=0", "?Name=M?Team=1");
        w.Run(12f);
        SPlayerController pc = players[victim];
        using (w.Level.CommandTime(60, 1f, Sim.DeltaTime))
        {
            w.Game.Killed(players[1 - victim], pc, pc.Pawn, SDamageType.DamTypeGrabBreak);
            pc.GotoState("s_Dead");
        }

        // A real remote ledge landing enters death inside its final command. Its timestamp can be
        // seconds behind the authority after loading, but the body must still finish its death clip.
        (float spectate, float respawn, List<(float T, bool Hidden)> samples) = Timeline(w, pc);
        Assert.Contains(samples, sample => sample.T > 4.9f && !sample.Hidden);
        Assert.All(samples.FindAll(sample => sample.T <= 5f), sample => Assert.False(sample.Hidden));
        Assert.InRange(spectate, 7.5f, 7.8f);
        Assert.InRange(respawn, spectate + 10f, spectate + 10.15f);
    }

    [Fact]
    public void Killed_spectator_deadline_matches_its_timer_even_inside_an_older_input_command()
    {
        (GameWorld w, SPlayerController[] players) = Match(Story, "?Name=S?Team=0", "?Name=M?Team=1");
        w.Run(12f);
        SPlayerController pc = players[0];
        float worldTime = w.Level.WorldTimeSeconds;
        using (w.Level.CommandTime(60, 1f, Sim.DeltaTime))
            pc.GotoState("s_MultiSpectating_WhenKilled");

        Assert.Equal(10f, pc.TimerRate);
        Assert.Equal(worldTime + 10f, pc.fRespawnDate);
    }

    [Fact]
    public void A_strangled_body_blinks_after_1_s()
    {
        (GameWorld w, SPlayerController[] p) = Match(Story, "?Name=S?Team=0", "?Name=M?Team=1");
        SPlayerController spy = p[0];
        spy.fTempsAvantClignote = 1f;
        spy.GotoState("s_Dead");
        (float spectate, float respawn, _) = Timeline(w, spy);
        Assert.InRange(spectate, 3.5f, 3.75f);
        Assert.InRange(respawn, 13.5f, 13.85f);
    }

    [Fact]
    public void Falling_out_of_the_world_skips_the_body_time()
    {
        (GameWorld w, SPlayerController[] p) = Match(Story, "?Name=S?Team=0", "?Name=M?Team=1");
        SPlayerController spy = p[0];
        spy.bInstantRespawn = true;
        spy.GotoState("s_Dead");
        (float spectate, _, _) = Timeline(w, spy);
        Assert.InRange(spectate, 0.15f, 0.25f);
    }

    [Fact]
    public void Without_a_respawn_delay_the_player_respawns_at_the_end_of_the_blink()
    {
        (GameWorld w, SPlayerController[] p) = Match("?GameMode=5?LivesATT=4?LivesDEF=3?TimesRespawnsATT=0", "?Name=S?Team=0", "?Name=M?Team=1");
        SPlayerController spy = p[0];
        spy.GotoState("s_Dead");
        (float spectate, float respawn, _) = Timeline(w, spy);
        Assert.Equal(-1f, spectate);
        Assert.InRange(respawn, 7.5f, 7.8f);
    }

    [Fact]
    public void A_player_without_lives_stays_spectating()
    {
        (GameWorld w, SPlayerController[] p) = Match(Story, "?Name=S?Team=0", "?Name=T?Team=0", "?Name=M?Team=1");
        SPlayerController spy = p[0];
        spy.PlayerReplicationInfo!.NumLives = 0;
        spy.GotoState("s_Dead");
        w.Run(30f);
        Assert.True(spy.IsInState("s_MultiSpectating_WhenKilled"));
        Assert.True(spy.PlayerReplicationInfo!.bIsSpectator);
    }

    [Fact]
    public void Leaving_the_death_state_restores_the_blink_delay()
    {
        (GameWorld w, SPlayerController[] p) = Match(Story, "?Name=S?Team=0", "?Name=M?Team=1");
        SPlayerController spy = p[0];
        spy.fTempsAvantClignote = 1f;
        spy.bInstantRespawn = true;
        spy.GotoState("s_Dead");
        spy.GotoState("s_WaitRespawn");
        Assert.Equal(5f, spy.fTempsAvantClignote);
        Assert.False(spy.bInstantRespawn);
        Assert.Equal(0, w.Ports.Count("EndDeath")); // the death screen belongs to the owner's machine

        SPlayerController host = w.Join("?Name=H?Team=1", local: true);
        host.GotoState("s_Dead");
        host.GotoState("s_WaitRespawn");
        Assert.Equal(1, w.Ports.Count("EndDeath"));
    }

    [Fact]
    public void The_death_entry_records_the_time_picks_a_tip_and_clears_the_damage_history()
    {
        (GameWorld w, SPlayerController[] p) = Match(Story, "?Name=S?Team=0", "?Name=M?Team=1");
        SPlayerController spy = p[0];
        spy.LastDamages[3].DamageType = SDamageType.SDTBase;
        var used = w.Level.Spawn<EventProbe>()!;
        spy.SBPawn!.UsedObject = used;
        w.Run(1f);
        spy.GotoState("s_Dead");
        Assert.Equal(w.Level.TimeSeconds, spy.fRespawnDate);
        Assert.Equal(SDamageType.NoDamageType, spy.LastDamages[3].DamageType);
        Assert.InRange(spy.PlayerReplicationInfo!.iTipNumber, 0, 14);
        Assert.True(used.bDeleteMe);
        Assert.Null(spy.SBPawn.UsedObject);
        Assert.True(spy.bBehindView);
        Assert.Equal(1, w.Ports.Count("BeginDeath"));
        Assert.Equal(1, w.Ports.Count("DeleteAllInventory"));

        SPlayerController merc = p[1];
        merc.bNewProfile = true;
        merc.PlayerReplicationInfo!.iTipNumber = 7;
        merc.GotoState("s_Dead");
        Assert.Equal(0, merc.PlayerReplicationInfo!.iTipNumber);
        Assert.False(merc.bNewProfile);
    }

    [Fact]
    public void A_client_reminds_the_server_every_second_while_dead()
    {
        var w = new GameWorld(ENetMode.NM_Client);
        var pc = w.Level.Spawn<SPlayerController>()!;
        pc.GotoState("s_Dead");
        Assert.Equal(1f, pc.TimerRate);
        w.Run(2.05f);
        Assert.Equal(2, w.Host.ServerCalls.FindAll(c => c.Call.Function == new Name("AntiBugTellServerIAmDead")).Count);
    }

    [Fact]
    public void AntiBugTellServerIAmDead_sends_the_server_copy_to_s_Dead_unless_it_waits_to_respawn()
    {
        (GameWorld _, SPlayerController[] p) = Match(Story, "?Name=S?Team=0", "?Name=M?Team=1");
        SPlayerController spy = p[0];
        spy.GotoState("s_WaitRespawn");
        spy.eventAntiBugTellServerIAmDead();
        Assert.True(spy.IsInState("s_WaitRespawn"));

        spy.GotoState(default);
        spy.eventAntiBugTellServerIAmDead();
        Assert.True(spy.IsInState("s_Dead"));
        float before = spy.fRespawnDate;
        spy.eventAntiBugTellServerIAmDead();
        Assert.Equal(before, spy.fRespawnDate);

        spy.GotoState("s_MultiSpectating_WhenKilled");
        spy.eventAntiBugTellServerIAmDead();
        Assert.True(spy.IsInState("s_MultiSpectating_WhenKilled"));
    }

    // ---- resign (rules 15 and 16) ---------------------------------------------------------------------------------

    [Fact]
    public void Resigning_alive_costs_the_last_life_through_self_damage()
    {
        GameWorld w = new GameWorld().Load(Story, MatchLifecycleTests.TwoSides);
        var spy = w.Level.Spawn<DamageRecordingController>()!;
        spy.GameReplicationInfo = w.Gri;
        w.Game.eventChangeTeam(spy, 0, false);
        spy.PawnClass = Classes.SpyPawn;
        w.Gri.bMatchHasBegun = true;
        w.Game.eventRestartPlayer(spy);
        spy.PlayerReplicationInfo!.NumLives = 3;
        spy.Pawn!.Health = 80;
        spy.ServerResign();
        Assert.True(spy.PlayerReplicationInfo!.bResigned);
        Assert.Equal(1, spy.PlayerReplicationInfo!.NumLives);
        var hit = Assert.Single(spy.Hits);
        Assert.Equal(180, hit.Damage);
        Assert.Equal(SDamageType.Suicided, hit.Type);
        Assert.Same(spy.Pawn, hit.By);
    }

    [Fact]
    public void Resigning_while_dead_drops_the_lives_and_runs_the_end_check()
    {
        (GameWorld w, SPlayerController[] p) = Match(Story, "?Name=S?Team=0", "?Name=M?Team=1");
        SPlayerController spy = p[0];
        spy.UnPossess();
        spy.ServerResign();
        Assert.Equal(0, spy.PlayerReplicationInfo!.NumLives);
        Assert.Equal(2, w.Host.Messages(24).Count); // the death message goes to everyone
        Assert.Single(w.Host.Messages(56)); // the last-life message to the spy side only
        Assert.True(w.Game.bGameEnded);
        Assert.Equal(1, w.Gri.WinningTeam);
        Assert.Equal((byte)EEndReason.END_TEAMDEATH, w.Gri.WinningReason);
    }

    [Fact]
    public void Resign_is_refused_before_the_start_for_spectators_and_without_lives()
    {
        GameWorld w = new GameWorld().Load(Story, MatchLifecycleTests.TwoSides);
        SPlayerController a = w.Join("?Name=A?Team=0");
        a.PlayerReplicationInfo!.NumLives = 2;
        a.ServerResign();
        Assert.False(a.PlayerReplicationInfo!.bResigned);

        w.Gri.bMatchHasBegun = true;
        a.PlayerReplicationInfo!.NumLives = 0;
        a.ServerResign();
        Assert.False(a.PlayerReplicationInfo!.bResigned);

        a.PlayerReplicationInfo!.NumLives = 2;
        a.PlayerReplicationInfo!.bOnlySpectator = true;
        a.Resign();
        Assert.False(a.PlayerReplicationInfo!.bResigned);
    }

    // ---- the Merc Deathmatch respawn weapon (rules 17 to 21) ----------------------------------------------------

    [Fact]
    public void The_respawn_weapon_cycles_through_the_team_list_while_dead()
    {
        (GameWorld w, SPlayerController[] p) = Match("?GameMode=7?LivesDEF=5", "?Name=A", "?Name=B");
        SPlayerController a = p[0];
        Assert.False(a.CanPickRespawnWeapon());
        a.GotoState("s_Dead");
        Assert.True(a.CanPickRespawnWeapon());
        Assert.Equal(-1, a.MercWeaponIndex(a.RespawnWeapon));

        a.ServerCycleRespawnWeapon();
        Assert.Equal(new ClassRef("SBase.SWeaponDefense"), a.RespawnWeapon);
        a.ServerCycleRespawnWeapon();
        a.ServerCycleRespawnWeapon();
        Assert.Equal(new ClassRef("SBase.SWeaponDefense_Shotgun"), a.RespawnWeapon);
        a.ServerCycleRespawnWeapon();
        Assert.Equal(new ClassRef("SBase.SWeaponDefense"), a.RespawnWeapon);
        Assert.Equal(4, w.Host.To(a, "ClientSetRespawnWeapon").Count);
    }

    [Fact]
    public void SpawnWeaponFor_keeps_the_request_outside_merc_deathmatch_and_remembers_the_choice_inside()
    {
        (GameWorld _, SPlayerController[] story) = Match(Story, "?Name=M?Team=1");
        var uzi = new ClassRef("SBase.SWeaponDefense_Uzi");
        Assert.Equal(uzi, story[0].SpawnWeaponFor(uzi));
        Assert.True(story[0].RespawnWeapon.IsNone);

        (GameWorld _, SPlayerController[] dm) = Match("?GameMode=7", "?Name=A");
        Assert.Equal(uzi, dm[0].SpawnWeaponFor(uzi));
        Assert.Equal(uzi, dm[0].SpawnWeaponFor(new ClassRef("SBase.SWeaponDefense_Shotgun")));
        Assert.Equal(new ClassRef("SBase.SWeaponDefense"), Match("?GameMode=7", "?Name=B").Players[0].SpawnWeaponFor(new ClassRef("SBase.Nothing")));
    }

    [Fact]
    public void The_use_key_edge_cycles_the_respawn_weapon()
    {
        (GameWorld _, SPlayerController[] p) = Match("?GameMode=7?LivesDEF=5", "?Name=A");
        SPlayerController a = p[0];
        a.GotoState("s_Dead");
        a.bUsing = 1;
        a.CheckRespawnWeaponKey();
        a.CheckRespawnWeaponKey();
        Assert.Equal(new ClassRef("SBase.SWeaponDefense"), a.RespawnWeapon);
        a.bUsing = 0;
        a.CheckRespawnWeaponKey();
        a.bUsing = 1;
        a.CheckRespawnWeaponKey();
        Assert.Equal(new ClassRef("SBase.SWeaponDefense_Uzi"), a.RespawnWeapon);
    }
}
