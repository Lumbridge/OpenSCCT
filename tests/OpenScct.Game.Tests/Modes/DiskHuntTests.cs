using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Modes;
using OpenScct.Game.SBase;
using Xunit;

namespace OpenScct.Game.Tests.Modes;

/// <summary>spec/objectives/disk-hunt.md and spec/game/game-modes.md: Disk Hunt with SteeE's own settings.</summary>
public class DiskHuntTests
{
    private static readonly Name s_Dead = new("s_Dead");

    private static (ModeWorld M, SPlayerController Spy, SPlayerController Merc) Match(MatchSetup? setup = null, int disks = 30)
    {
        var m = new ModeWorld(EGameMode.GM_DiskHunt, disks, setup: setup);
        SPlayerController[] p = m.Start("?Name=S?Team=0", "?Name=M?Team=1");
        return (m, p[0], p[1]);
    }

    private static void PickUp(SPlayerController pc, SDisk disk) => pc.SetHandItemFromInteraction(disk.PickupInteraction);

    /// <summary>The pickup as the animation ends it: the hand freed (AnimNotifyPickup) and the disk to the thigh.</summary>
    private static void FinishPickup(SPlayerController pc)
    {
        pc.GotoState(new Name("s_Pickup"));
        pc.GotoState(new Name("s_PlayerWalkingSt0H"));
    }

    // ---- settings (game-modes.md rules 2 to 5, disk-hunt.md rule 17) ------------------------------------------

    [Fact]
    public void SteeE_untouched_menu_puts_20_disks_in_play_and_asks_for_10()
    {
        MatchSetup setup = SteeE.Setup(EGameMode.GM_DiskHunt);
        Assert.Equal(20, setup.NumDisks);
        Assert.Equal(10, setup.NumDisksToWin);
        Assert.Equal(30, setup.NumDisksMax);
        Assert.Equal(
            "?MapName=SteeE?MaxPlayers=4?TimeLimit=10?ScoreLimit=10?GameMode=4?LivesATT=4?TimesRespawnsATT=10?LivesDEF=3" +
            "?BalanceTeams=False?Revenge=False?GamesInARow=1?TimesReinitFlag=0?TimeToExplode=0?GameWithRanking=False?LevelGap=0" +
            "?NBPlayers=2",
            setup.GameOptions(2));
    }

    [Theory]
    [InlineData(0, 0, 1, 1)]
    [InlineData(40, 10, 30, 10)]
    [InlineData(20, 25, 20, 20)]
    [InlineData(5, 0, 5, 1)]
    public void Disk_counts_are_clamped_as_the_menu_does(int handicap, int scoreLimit, int active, int toTake)
    {
        var settings = new USMapSettings();
        settings.Modes.Add(SteeE.DiskHunt with { Handicap = handicap, ScoreLimit = scoreLimit });
        MatchSetup setup = MatchSetup.Defaults(settings, EGameMode.GM_DiskHunt, "Map");
        Assert.Equal(active, setup.NumDisks);
        Assert.Equal(toTake, setup.NumDisksToWin);
        Assert.Equal(toTake, setup.Match.ScoreLimit);
    }

    [Fact]
    public void The_host_settings_reach_the_replicated_game_info()
    {
        (ModeWorld m, _, _) = Match();
        Assert.Equal(20, m.Game.PersistentData.NumDisks);
        Assert.Equal(20, m.Gri.NumDisks);
        Assert.Equal(10, m.Gri.NumDisksToWin);
        Assert.Equal(EGameMode.GM_DiskHunt, m.Gri.GameMode);
    }

    // ---- match start and ActivateDisks (rules 1 to 3) -----------------------------------------------------------

    [Fact]
    public void Match_start_puts_exactly_the_active_count_in_play_and_hides_the_rest()
    {
        (ModeWorld m, _, _) = Match();
        List<SDisk> active = m.Active();
        Assert.Equal(20, active.Count);
        Assert.Equal(20, m.DiskHunt.MinimumObjectives);
        Assert.Equal(20, m.DiskHunt.TotalObjectivesLeft);
        Assert.Equal(20, m.Gri.NumObjectives);
        Assert.False(m.DiskHunt.ObjectiveTaken);
        foreach (SDisk d in m.Disks)
        {
            Assert.Equal(!d.bActive, d.bHidden);
            Assert.Equal(d.bActive, d.PickupInteraction!.bCanPickup);
        }

        // The other modes' missions are out of play: their objectives count as taken.
        Assert.True(m.Story.ObjectiveTaken);
        Assert.True(m.StoryObjective.ObjectiveTaken);
        Assert.True(m.Deathmatch.ObjectiveTaken);
    }

    [Fact]
    public void ActivateDisks_spreads_the_disks_over_the_groups_one_at_a_time()
    {
        var m = new ModeWorld(EGameMode.GM_DiskHunt, 12, i => (byte)(i < 9 ? 0 : 1 + i % 2));
        m.Start("?Name=S?Team=0", "?Name=M?Team=1");
        m.DiskHunt.ActivateDisks(7);
        int g0 = 0, g1 = 0, g2 = 0;
        foreach (SDisk d in m.Active())
        {
            if (d.Group == 0)
            {
                g0++;
            }
            else if (d.Group == 1)
            {
                g1++;
            }
            else
            {
                g2++;
            }
        }

        // Groups 0, 2 and 1 (first appearance) with 9, 2 and 1 disks: one per group per round, an empty group skipped.
        Assert.Equal(7, m.Active().Count);
        Assert.Equal(4, g0);
        Assert.Equal(1, g1);
        Assert.Equal(2, g2);
    }

    [Fact]
    public void ActivateDisks_is_capped_by_the_number_of_disks_and_zero_puts_every_disk_in_play()
    {
        var setup = MatchSetup.Defaults(null, EGameMode.GM_DiskHunt, "Map");
        var m = new ModeWorld(EGameMode.GM_DiskHunt, 6, setup: setup);
        m.Game.PersistentData.NumDisks = 0;
        m.Start("?Name=S?Team=0", "?Name=M?Team=1");
        Assert.Equal(6, m.Active().Count);

        m.DiskHunt.ActivateDisks(50);
        Assert.Equal(6, m.Active().Count);
    }

    // ---- taking and holding disks (rules 7 to 10) ----------------------------------------------------------------

    [Fact]
    public void A_spy_picks_a_disk_up_and_keeps_his_hand_free_for_the_next()
    {
        (ModeWorld m, SPlayerController spy, _) = Match();
        SDisk first = m.Active()[0];
        PickUp(spy, first);
        var pawn = (SBasePawn)spy.Pawn!;
        Assert.False(first.bActive);
        Assert.Same(spy, first.ControllerWhoTakesIt);
        Assert.Same(first, pawn.HandItem);
        Assert.Equal(new List<SDisk?> { first }, pawn.Disks);
        Assert.Equal(1, spy.PlayerReplicationInfo!.iNbObjectivesTaken);
        Assert.Equal(19, m.DiskHunt.TotalObjectivesLeft);
        Assert.False(first.PickupInteraction!.bCanPickup);

        FinishPickup(spy);
        Assert.Null(pawn.HandItem);
        Assert.Same(pawn, first.Base);
        Assert.Equal(new Name("B L Thigh"), first.AttachmentBone);
        Assert.Equal(K.DiskThighOffset, first.RelativeLocation);

        SDisk second = m.Active()[0];
        PickUp(spy, second);
        Assert.Equal(new List<SDisk?> { second, first }, pawn.Disks);
        Assert.Equal(2, spy.PlayerReplicationInfo!.iNbObjectivesTaken);
    }

    [Fact]
    public void Spies_win_when_they_hold_the_disks_to_take_and_not_before()
    {
        (ModeWorld m, SPlayerController spy, _) = Match();
        for (int i = 0; i < 9; i++)
        {
            PickUp(spy, m.Active()[0]);
            FinishPickup(spy);
        }

        Assert.False(m.DiskHunt.ObjectiveTaken);
        Assert.Equal(255, m.Gri.WinningTeam);

        PickUp(spy, m.Active()[0]);
        Assert.True(m.DiskHunt.ObjectiveTaken);
        Assert.Equal(0, m.Gri.WinningTeam);
        Assert.Equal((byte)EEndReason.END_STORY, m.Gri.WinningReason);
        Assert.True(m.Game.bGameEnded);
    }

    [Fact]
    public void Without_disks_to_take_the_spies_need_every_disk_in_play()
    {
        MatchSetup setup = MatchSetup.Defaults(null, EGameMode.GM_DiskHunt, "Map");
        var m = new ModeWorld(EGameMode.GM_DiskHunt, 3, setup: setup);
        m.Game.PersistentData.NumDisks = 0;
        m.Game.PersistentData.NumDisksToWin = 0;
        SPlayerController spy = m.Start("?Name=S?Team=0", "?Name=M?Team=1")[0];
        PickUp(spy, m.Disks[0]);
        FinishPickup(spy);
        PickUp(spy, m.Disks[1]);
        FinishPickup(spy);
        Assert.False(m.DiskHunt.ObjectiveTaken);
        PickUp(spy, m.Disks[2]);
        Assert.True(m.DiskHunt.ObjectiveTaken);
    }

    [Fact]
    public void The_hand_must_be_free_to_take_a_disk()
    {
        (ModeWorld m, SPlayerController spy, _) = Match();
        SDisk first = m.Active()[0];
        PickUp(spy, first);
        SDisk second = m.Active()[0];
        PickUp(spy, second);
        Assert.True(second.bActive);
        Assert.Single(((SBasePawn)spy.Pawn!).Disks);
    }

    // ---- dropping and going home (rules 11 to 16) ----------------------------------------------------------------

    [Fact]
    public void A_dying_carrier_throws_every_disk_back_into_play()
    {
        (ModeWorld m, SPlayerController spy, _) = Match();
        SDisk a = m.Active()[0];
        PickUp(spy, a);
        FinishPickup(spy);
        SDisk b = m.Active()[0];
        PickUp(spy, b);
        FinishPickup(spy);
        Assert.Equal(18, m.DiskHunt.TotalObjectivesLeft);

        spy.GotoState(s_Dead);
        var pawn = (SBasePawn)spy.Pawn!;
        Assert.Empty(pawn.Disks);
        Assert.Equal(0, spy.PlayerReplicationInfo!.iNbObjectivesTaken);
        Assert.True(a.bActive);
        Assert.True(b.bActive);
        Assert.False(a.bHidden);
        Assert.Equal(20, m.DiskHunt.TotalObjectivesLeft);

        // Newest first: b at speed 0, a at 30 on each axis.
        Assert.Equal(FVector.Zero, b.Velocity);
        Assert.Equal(new FVector(30f, 30f, 30f), a.Velocity);
        Assert.Equal(2, m.W.Host.Messages(K.MsgDisksDroppedSpy).Count);
    }

    [Fact]
    public void A_disk_at_rest_goes_home_after_ResetTime_and_can_be_picked_up_meanwhile()
    {
        (ModeWorld m, SPlayerController spy, _) = Match();
        SDisk disk = m.Active()[0];
        FVector home = disk.Location;
        PickUp(spy, disk);
        FinishPickup(spy);
        spy.Pawn!.SetLocation(home + new FVector(400f, 0f, 0f));
        spy.GotoState(s_Dead);
        disk.eventStoppedMoving();
        Assert.True(disk.PickupInteraction!.bCanPickup);
        Assert.NotEqual(home, disk.Location);

        m.W.Run(disk.ResetTime - 1f);
        Assert.NotEqual(home, disk.Location);
        m.W.Run(1.5f);
        Assert.Equal(home, disk.Location);
        Assert.True(disk.bActive);
    }

    [Fact]
    public void A_merc_walking_over_a_dropped_disk_puts_it_home_without_a_reset()
    {
        (ModeWorld m, SPlayerController spy, SPlayerController merc) = Match();
        SDisk disk = m.Active()[0];
        FVector home = disk.Location;
        PickUp(spy, disk);
        spy.Pawn!.SetLocation(home + new FVector(400f, 0f, 0f));
        spy.GotoState(s_Dead);
        disk.eventStoppedMoving();
        int before = m.W.Host.Calls.Count;

        disk.eventTouch(spy.Pawn!);
        Assert.NotEqual(home, disk.Location);
        disk.eventTouch(merc.Pawn!);
        Assert.Equal(home, disk.Location);
        Assert.True(disk.bActive);
        Assert.Equal(before, m.W.Host.Calls.Count);
    }

    [Fact]
    public void A_client_hides_disks_out_of_play_that_nobody_carries()
    {
        (ModeWorld m, SPlayerController spy, _) = Match();
        SDisk idle = m.Disks.Find(d => !d.bActive)!;
        SDisk carried = m.Active()[0];
        PickUp(spy, carried);
        FinishPickup(spy);
        idle.bHidden = false;
        carried.bHidden = true;
        idle.Role = ENetRole.ROLE_SimulatedProxy;
        carried.Role = ENetRole.ROLE_SimulatedProxy;
        m.W.Run(0.1f);
        Assert.True(idle.bHidden);
        Assert.False(carried.bHidden);
    }

    [Fact]
    public void Desactivate_hides_a_disk_and_closes_its_pickup()
    {
        (ModeWorld m, _, _) = Match();
        SDisk disk = m.Active()[0];
        disk.Desactivate();
        Assert.True(disk.bHidden);
        Assert.False(disk.bActive);
        Assert.False(disk.bPickable);
        Assert.False(disk.PickupInteraction!.bCanPickup);
    }
}
