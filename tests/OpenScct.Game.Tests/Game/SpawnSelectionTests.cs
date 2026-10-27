using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using Xunit;

namespace OpenScct.Game.Tests.Game;

/// <summary>spec/game/spawn-selection.md: the Priority pass, the distance pass, Merc Deathmatch starts and trigger starts.</summary>
public class SpawnSelectionTests
{
    private const string Story = MatchLifecycleTests.SteeEStory;

    // ---- the Priority pass (rule 4) ---------------------------------------------------------------------------

    [Fact]
    public void Before_the_match_begins_the_highest_priority_start_of_the_side_wins()
    {
        PlayerStart? first2 = null;
        PlayerStart? second2 = null;
        GameWorld w = new GameWorld().Load(Story, x =>
        {
            x.AddStart(new FVector(0f, 0f, 0f), 0);
            x.AddStart(new FVector(100f, 0f, 0f), 1, priority: 0);
            x.AddStart(new FVector(200f, 0f, 0f), 1, priority: 1);
            first2 = x.AddStart(new FVector(300f, 0f, 0f), 1, priority: 2);
            second2 = x.AddStart(new FVector(400f, 0f, 0f), 1, priority: 2);
            x.AddStart(new FVector(500f, 0f, 0f), 0, priority: 9);
        });
        SPlayerController merc = w.Join("?Name=M?Team=1");
        Assert.Same(first2, w.Game.FindPlayerStart(merc));

        first2!.bEnabled = false;
        Assert.Same(second2, w.Game.FindPlayerStart(merc));
    }

    [Fact]
    public void Priority_zero_never_wins_the_priority_pass()
    {
        PlayerStart? far = null;
        GameWorld w = new GameWorld().Load(Story, x =>
        {
            x.AddStart(new FVector(0f, 0f, 0f), 0);
            far = x.AddStart(new FVector(9000f, 0f, 0f), 0);
        });
        SPlayerController spy = w.Join("?Name=S?Team=0");
        w.SpawnPawn(true, new FVector(10f, 0f, 0f));
        Assert.Same(far, w.Game.FindPlayerStart(spy));
    }

    [Fact]
    public void Once_the_match_has_begun_priority_is_ignored()
    {
        PlayerStart? quiet = null;
        GameWorld w = new GameWorld().Load(Story, x =>
        {
            x.AddStart(new FVector(0f, 0f, 0f), 1, priority: 5);
            quiet = x.AddStart(new FVector(5000f, 0f, 0f), 1);
            x.AddStart(new FVector(-9000f, 0f, 0f), 0);
        });
        SPlayerController merc = w.Join("?Name=M?Team=1");
        w.Gri.bMatchHasBegun = true;
        w.SpawnPawn(true, new FVector(100f, 0f, 0f));
        Assert.Same(quiet, w.Game.FindPlayerStart(merc));
    }

    // ---- the distance pass (rules 5 to 7) ---------------------------------------------------------------------

    [Fact]
    public void RateStartByDistance_is_the_nearest_pawn_distance_capped_at_a_million()
    {
        PlayerStart? s = null;
        GameWorld w = new GameWorld().Load(Story, x => s = x.AddStart(default, 0));
        Assert.Equal(1000000f, w.Game.RateStartByDistance(s!));
        w.SpawnPawn(true, new FVector(300f, 400f, 0f));
        w.SpawnPawn(false, new FVector(0f, 0f, 1000f));
        Assert.Equal(500f, w.Game.RateStartByDistance(s!));
    }

    [Fact]
    public void RatePlayerStart_gives_zero_to_disabled_starts_and_the_other_side()
    {
        PlayerStart? spy = null;
        PlayerStart? merc = null;
        GameWorld w = new GameWorld().Load(Story, x =>
        {
            spy = x.AddStart(default, 0);
            merc = x.AddStart(default, 1);
        });
        Assert.Equal(1000000f, w.Game.RatePlayerStart(spy!, 0, null));
        Assert.Equal(0f, w.Game.RatePlayerStart(merc!, 0, null));
        spy!.bEnabled = false;
        Assert.Equal(0f, w.Game.RatePlayerStart(spy, 0, null));
    }

    [Fact]
    public void Ties_keep_the_earlier_start_in_actor_order()
    {
        PlayerStart? a = null;
        GameWorld w = new GameWorld().Load(Story, x =>
        {
            a = x.AddStart(new FVector(0f, 0f, 0f), 0);
            x.AddStart(new FVector(100f, 0f, 0f), 0);
        });
        w.Gri.bMatchHasBegun = true;
        Assert.Same(a, w.Game.FindPlayerStart(null));
    }

    [Fact]
    public void Without_a_valid_start_the_first_start_of_the_set_is_returned()
    {
        PlayerStart? first = null;
        GameWorld w = new GameWorld().Load(Story, x =>
        {
            first = x.AddStart(new FVector(0f, 0f, 0f), 1);
            x.AddStart(new FVector(900f, 0f, 0f), 0, enabled: false);
        });
        w.Gri.bMatchHasBegun = true;
        Assert.Same(first, w.Game.FindPlayerStart(null));
    }

    [Fact]
    public void A_missing_controller_reads_as_the_spy_side()
    {
        PlayerStart? spy = null;
        GameWorld w = new GameWorld().Load(Story, x =>
        {
            x.AddStart(new FVector(0f, 0f, 0f), 1, priority: 3);
            spy = x.AddStart(new FVector(10f, 0f, 0f), 0, priority: 1);
        });
        Assert.Same(spy, w.Game.FindPlayerStart(null, 1));
    }

    // ---- reuse, Merc Deathmatch and trigger starts (rules 2, 3, 8; STriggerPlayerStart) -------------------------

    [Fact]
    public void Offline_a_waiting_controller_keeps_its_start()
    {
        PlayerStart? low = null;
        GameWorld w = new GameWorld(ENetMode.NM_Standalone).Load(Story, x =>
        {
            low = x.AddStart(new FVector(0f, 0f, 0f), 0);
            x.AddStart(new FVector(100f, 0f, 0f), 0, priority: 1);
        });
        SPlayerController p = w.Join("?Name=S?Team=0", local: true);
        p.StartSpot = low;
        Assert.NotSame(low, w.Game.FindPlayerStart(p));
        p.PlayerReplicationInfo!.bWaitingPlayer = true;
        Assert.Same(low, w.Game.FindPlayerStart(p));
    }

    [Fact]
    public void Merc_deathmatch_uses_its_own_set_of_starts_when_the_map_has_one()
    {
        PlayerStart? dm = null;
        GameWorld w = new GameWorld().Load("?GameMode=7", x =>
        {
            x.AddStart(new FVector(0f, 0f, 0f), 1, priority: 2);
            dm = x.AddStart(new FVector(500f, 0f, 0f), 1, priority: 1, mercDeathMatch: true);
        });
        SPlayerController merc = w.Join("?Name=M?Team=1");
        Assert.Same(dm, w.Game.FindPlayerStart(merc));

        GameWorld story = new GameWorld().Load(Story, x =>
        {
            x.AddStart(new FVector(0f, 0f, 0f), 1, priority: 1);
            x.AddStart(new FVector(500f, 0f, 0f), 1, priority: 3, mercDeathMatch: true);
        });
        SPlayerController m2 = story.Join("?Name=M?Team=1");
        Assert.Equal(0f, story.Game.FindPlayerStart(m2)!.Location.X);
    }

    [Fact]
    public void A_trigger_start_is_switched_by_events_and_is_not_static()
    {
        STriggerPlayerStart? shop = null;
        GameWorld w = new GameWorld().Load(Story, x =>
        {
            shop = x.AddStart(new STriggerPlayerStart { Tag = "enable_shop_spawn" }, default, 1, priority: 4);
            x.AddStart(new FVector(100f, 0f, 0f), 1, priority: 1);
        });
        Assert.False(shop!.bStatic);
        SPlayerController merc = w.Join("?Name=M?Team=1");
        Assert.Same(shop, w.Game.FindPlayerStart(merc));
        w.Level.UntriggerEvent("enable_shop_spawn", null, null);
        Assert.False(shop.bEnabled);
        Assert.NotSame(shop, w.Game.FindPlayerStart(merc));
        w.Level.TriggerEvent("enable_shop_spawn", null, null);
        Assert.True(shop.bEnabled);
    }

    // ---- when each pass runs (rules 10 to 12), with the SteeE starts of the spec ------------------------------

    private static (GameWorld W, PlayerStart[] Spy, PlayerStart[] Merc) SteeE(ENetMode mode, string options)
    {
        var spies = new PlayerStart[3];
        var mercs = new PlayerStart[4];
        GameWorld w = new GameWorld(mode).Load(options, x =>
        {
            spies[0] = x.AddStart(new FVector(-1418.1f, 1751.4f, -119f), 0);
            spies[1] = x.AddStart(new FVector(-1261.4f, 1787.4f, -119f), 0);
            spies[2] = x.AddStart(new FVector(-1353.6f, 1902.5f, -119f), 0);
            mercs[0] = x.AddStart(new FVector(-1494.7f, 2106.2f, -1351f), 1);
            mercs[1] = x.AddStart(new FVector(-1547.6f, -2329.9f, -1042f), 1, priority: 1);
            mercs[2] = x.AddStart(new FVector(-1339.5f, 112.1f, -1042f), 1, priority: 2);
            mercs[3] = x.AddStart(new STriggerPlayerStart(), new FVector(-1627.5f, -1739.9f, -401f), 1);
        });
        return (w, spies, mercs);
    }

    [Fact]
    public void In_a_network_match_spawns_follow_distance_only()
    {
        (GameWorld w, PlayerStart[] spy, PlayerStart[] merc) = SteeE(ENetMode.NM_ListenServer, Story);
        SPlayerController s1 = w.Join("?Name=S1?Team=0");
        SPlayerController s2 = w.Join("?Name=S2?Team=0");
        SPlayerController m1 = w.Join("?Name=M1?Team=1");
        SPlayerController m2 = w.Join("?Name=M2?Team=1");
        w.Gri.bMatchHasBegun = true;
        foreach (SPlayerController p in new[] { s1, s2, m1, m2 })
        {
            w.Game.eventRestartPlayer(p);
        }

        Assert.Same(spy[0], s1.StartSpot);
        Assert.Same(spy[2], s2.StartSpot);
        Assert.Same(merc[1], m1.StartSpot);
        Assert.Same(merc[2], m2.StartSpot);
    }

    [Fact]
    public void In_play_level_a_merc_always_lands_on_the_priority_2_start()
    {
        (GameWorld w, _, PlayerStart[] merc) = SteeE(ENetMode.NM_Standalone, "?GameMode=5?Editeur=true");
        SPlayerController m = w.Join("?Name=M?Team=1?Editeur=true", local: true);
        Assert.Same(merc[2], m.StartSpot);
        Assert.False(w.Gri.bMatchHasBegun);

        m.UnPossess();
        w.Game.eventRestartPlayer(m);
        Assert.Same(merc[2], m.StartSpot);
    }
}
