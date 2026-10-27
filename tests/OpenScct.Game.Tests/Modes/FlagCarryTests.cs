using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Game.Tests.Game;
using Xunit;
using OK = OpenScct.Game.Objectives.K;

namespace OpenScct.Game.Tests.Modes;

/// <summary>
/// spec/objectives/disk-flag.md rules 11, 16 and 19 through the controller: the Story disk taken from the menu, and
/// dropped where its carrier dies (the s_Dead entry).
/// </summary>
public class FlagCarryTests
{
    private static (GameWorld W, SFlag Flag, SObjective Objective, SPlayerController Spy) Story()
    {
        SFlag? flag = null;
        SObjective? objective = null;
        GameWorld w = new GameWorld().Load("?GameMode=5?TimeLimit=10?LivesATT=4?LivesDEF=3", x =>
        {
            MatchLifecycleTests.TwoSides(x);
            flag = x.Level.SpawnActor(new SFlag { ResetTime = 15 }, new FVector(0f, 1500f, 0f), default)!;
            objective = x.Level.SpawnActor(new SObjective(), default, default)!;
            objective.Triggers.Add(flag);
            var mission = x.Level.SpawnActor(new SMission { GameMode = EGameMode.GM_Multi, bChained = false }, default, default)!;
            mission.Objectives.Add(objective);
        });
        SPlayerController spy = w.Join("?Name=S?Team=0");
        w.Join("?Name=M?Team=1");
        w.StartNetworkMatch(spy);
        w.Gri.bMatchHasBegun = true;
        return (w, flag!, objective!, spy);
    }

    [Fact]
    public void The_menu_pickup_makes_the_spy_the_carrier()
    {
        (GameWorld w, SFlag flag, _, SPlayerController spy) = Story();
        spy.SetHandItemFromInteraction(flag.PickupInteraction);
        var pawn = (SBasePawn)spy.Pawn!;
        Assert.Same(flag, pawn.CarriedFlag);
        Assert.Null(pawn.HandItem);
        Assert.Same(pawn, flag.FlagCarrier);
        Assert.Equal(2, w.Host.Messages(OK.MsgDataStolen).Count);
    }

    [Fact]
    public void A_dying_carrier_drops_the_disk_where_he_is_and_everyone_hears_it()
    {
        (GameWorld w, SFlag flag, SObjective objective, SPlayerController spy) = Story();
        spy.SetHandItemFromInteraction(flag.PickupInteraction);
        var pawn = (SBasePawn)spy.Pawn!;
        pawn.SetLocation(new FVector(600f, 1500f, 0f));

        spy.GotoState(new Name("s_Dead"));
        Assert.Null(pawn.CarriedFlag);
        Assert.Null(flag.FlagCarrier);
        Assert.False(flag.FlagTaken);
        Assert.Equal(new FVector(600f, 1500f, 0f), flag.Location);
        Assert.Equal(2, w.Host.Messages(OK.MsgDataDropped).Count);
        Assert.False(objective.ObjectiveTaken);

        // At rest away from home it can be touched, not taken from the menu, and goes home after ResetTime.
        flag.eventStoppedMoving();
        Assert.True(flag.bActive);
        Assert.False(flag.bPickable);
        w.Run(15.5f);
        Assert.Equal(new FVector(0f, 1500f, 0f), flag.Location);
        Assert.True(flag.bPickable);
    }
}
