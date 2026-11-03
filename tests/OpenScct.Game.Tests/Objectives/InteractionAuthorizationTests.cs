using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Objectives;
using OpenScct.Game.SBase;
using Xunit;

namespace OpenScct.Game.Tests.Objectives;

public sealed class InteractionAuthorizationTests
{
    private static (ObjWorld World, TestPC Player, STriggerInteraction Entry, EventCounter Target) Panel(int seconds = 0)
    {
        var w = new ObjWorld();
        SInterrupteur? panel = null;
        w.Load(x => panel = x.Place(new SInterrupteur
        {
            iHackingTime = seconds, fInteractionRadius = 80f, fInteractionHeight = 100f,
            bInteractInFront = false, UsableBy = Usable.U_SPAWNOFFENSE, Event = "panel",
        }, default));
        TestPC pc = w.AddPlayer(true, new FVector(40f, 0f, 0f));
        ObjWorld.Touch(pc, panel!.InteractionObj!);
        return (w, pc, panel.TriggerInteraction!, w.Level.Spawn<EventCounter>(tag: "panel")!);
    }

    [Theory]
    [InlineData("far")]
    [InlineData("dead")]
    [InlineData("wrong-team")]
    [InlineData("disabled")]
    [InlineData("index")]
    [InlineData("cooldown")]
    [InlineData("knocked-out")]
    [InlineData("invalid-state")]
    public void Invalid_initial_requests_cannot_fire_or_stamp_an_object(string reason)
    {
        var (_, pc, entry, target) = Panel();
        int index = 0;
        if (reason == "far") pc.Pawn!.Location = new FVector(10000f, 0f, 0f);
        if (reason == "dead") pc.Pawn!.Health = 0;
        if (reason == "wrong-team") entry.UsableBy = Usable.U_SPAWNDEFENSE;
        if (reason == "disabled") entry.bUsable = false;
        if (reason == "index") index = 2;
        if (reason == "cooldown") entry.iReusableTime = 20;
        if (reason == "knocked-out") ((SBasePawn)pc.Pawn!).bLyingOnGround = true;
        if (reason == "invalid-state") pc.GotoState("s_Hack_Use");
        entry.iLastUsedTime = -1;
        InteractionMenuRules.InteractionInit(pc, entry, index);
        Assert.Equal(0, target.Triggers);
        Assert.Equal(-1, entry.iLastUsedTime);
    }

    [Fact]
    public void Hack_requires_start_ownership_server_duration_and_a_single_completion()
    {
        var (w, pc, entry, target) = Panel(5);
        var attacker = w.AddPlayer(true, pc.Pawn!.Location);
        InteractionMenuRules.Interaction(pc, entry, K.SubStart);
        InteractionMenuRules.InteractionPost(pc, entry, K.SubSuccess);
        Assert.False(entry.bInUse);
        InteractionMenuRules.InteractionInit(pc, entry, 0);
        InteractionMenuRules.Interaction(attacker, entry, K.SubStart);
        Assert.False(entry.bInUse);
        InteractionMenuRules.Interaction(pc, entry, K.SubStart);
        Assert.True(entry.bInUse);
        pc.GotoState("s_Hack_Use");
        pc.SetTimer(0f, false); // this test drives the success request directly, independently of the state timer
        InteractionMenuRules.InteractionPost(attacker, entry, K.SubFailed);
        InteractionMenuRules.InteractionPost(pc, entry, K.SubSuccess);
        Assert.True(entry.bInUse);
        Assert.Equal(0, target.Triggers);
        w.Run(5.1f);
        InteractionMenuRules.InteractionPost(attacker, entry, K.SubSuccess);
        Assert.Equal(0, target.Triggers);
        InteractionMenuRules.InteractionPost(pc, entry, K.SubSuccess);
        InteractionMenuRules.InteractionPost(pc, entry, K.SubSuccess);
        Assert.Equal(1, target.Triggers);
    }

    [Fact]
    public void Moving_away_or_replacing_the_pawn_invalidates_a_hack()
    {
        var (w, pc, entry, target) = Panel(1);
        InteractionMenuRules.InteractionInit(pc, entry, 0);
        InteractionMenuRules.Interaction(pc, entry, K.SubStart);
        pc.GotoState("s_Hack_Use");
        pc.SetTimer(0f, false);
        w.Run(1.1f);
        FVector original = pc.Pawn!.Location;
        pc.Pawn.Location = new FVector(10000f, 0f, 0f);
        InteractionMenuRules.InteractionPost(pc, entry, K.SubSuccess);
        Assert.Equal(0, target.Triggers);
        pc.Pawn = w.AddPlayer(true, original).Pawn;
        pc.Pawn!.Controller = pc;
        InteractionMenuRules.InteractionPost(pc, entry, K.SubSuccess);
        Assert.Equal(0, target.Triggers);
        InteractionMenuRules.InteractionPost(pc, entry, K.SubFailed);
        Assert.False(entry.bInUse);
    }

    [Fact]
    public void A_pending_panel_is_reserved_before_the_enter_animation_finishes()
    {
        var (w, pc, entry, _) = Panel(5);
        var other = w.AddPlayer(true, pc.Pawn!.Location);
        InteractionMenuRules.InteractionInit(pc, entry, 0);
        InteractionMenuRules.InteractionInit(other, entry, 0);
        Assert.Same(pc, entry.InstigatorTempo);
        InteractionMenuRules.Interaction(other, entry, K.SubStart);
        Assert.False(entry.bInUse);
        InteractionMenuRules.Interaction(pc, entry, K.SubStart);
        Assert.True(entry.bInUse);
    }

    [Fact]
    public void A_forged_mate_is_replaced_by_the_authoritative_search()
    {
        var (w, pc, _, _) = Panel();
        var distant = w.AddPlayer(true, new FVector(10000f, 0f, 0f));
        distant.Pawn!.Health = 10;
        var heal = (SHealMateInteraction)System.Array.Find(pc.IManager!.Interactions, static e => e is SHealMateInteraction)!;
        heal.ReplicateMate((SBasePawn)distant.Pawn);
        InteractionMenuRules.InteractionInit(pc, heal, 0);
        Assert.Equal(10, distant.Pawn.Health);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LeavingTheInteractionPhaseInvalidatesAReservedOrStartedHack(bool started)
    {
        var (w, pc, entry, target) = Panel(1);
        InteractionMenuRules.InteractionInit(pc, entry, 0);
        if (started) InteractionMenuRules.Interaction(pc, entry, K.SubStart);
        pc.GotoState("s_PlayerWalkingSt0H");
        w.Run(1.1f);
        InteractionMenuRules.Interaction(pc, entry, K.SubStart);
        InteractionMenuRules.InteractionPost(pc, entry, K.SubSuccess);
        Assert.Equal(0, target.Triggers);
        if (started)
        {
            // A new legitimate user can replace the stale session instead of leaving the panel locked.
            var other = w.AddPlayer(true, pc.Pawn!.Location);
            ObjWorld.Touch(other, entry);
            InteractionMenuRules.InteractionInit(other, entry, 0);
            Assert.Same(other, entry.InstigatorTempo);
            Assert.False(entry.bInUse);
        }
    }

    [Fact]
    public void ARemoteHoldMayStartWhileItsAuthoritativeControllerRemainsInEnter()
    {
        var (_, pc, entry, target) = Panel();
        entry.bMaintain = true;
        ((SInterrupteur)entry.Owner!).bMaintainsSwitch = true;
        pc.RemoteRole = ENetRole.ROLE_AutonomousProxy;
        InteractionMenuRules.InteractionInit(pc, entry, 0);
        pc.GotoState("s_Maintain_Enter");
        InteractionMenuRules.Interaction(pc, entry, K.SubStart);
        InteractionMenuRules.InteractionPost(pc, entry, K.SubHoldStarted);
        Assert.True(entry.bInUse);
        Assert.Equal(1, target.Triggers);
        InteractionMenuRules.InteractionPost(pc, entry, K.SubFailed);
        Assert.False(entry.bInUse);
    }

    [Theory]
    [InlineData("s_Hack_GoTo")]
    [InlineData("s_Hack_Enter")]
    public void ElapsedTimeDoesNotCompleteAHackBeforeTheUsePhase(string state)
    {
        var (w, pc, entry, target) = Panel(1);
        pc.RemoteRole = ENetRole.ROLE_AutonomousProxy;
        InteractionMenuRules.InteractionInit(pc, entry, 0);
        pc.GotoState(state);
        InteractionMenuRules.Interaction(pc, entry, K.SubStart);
        w.Run(1.1f);
        Assert.True(pc.IsInState(state));
        InteractionMenuRules.InteractionPost(pc, entry, K.SubSuccess);
        Assert.Equal(0, target.Triggers);
        Assert.True(entry.bInUse);
        InteractionMenuRules.InteractionPost(pc, entry, K.SubFailed);
        Assert.False(entry.bInUse);
    }

    [Fact]
    public void AHoldCannotFireWhileWalkingToThePanelOrAfterLeavingIt()
    {
        var (_, pc, entry, target) = Panel();
        entry.bMaintain = true;
        ((SInterrupteur)entry.Owner!).bMaintainsSwitch = true;
        InteractionMenuRules.InteractionInit(pc, entry, 0);
        InteractionMenuRules.Interaction(pc, entry, K.SubStart);
        Assert.True(pc.IsInState("s_Maintain_GoTo"));
        InteractionMenuRules.InteractionPost(pc, entry, K.SubHoldStarted);
        Assert.Equal(0, target.Triggers);
        pc.GotoState("s_PlayerWalkingSt0H");
        InteractionMenuRules.InteractionPost(pc, entry, K.SubHoldStarted);
        Assert.Equal(0, target.Triggers);
        InteractionMenuRules.InteractionPost(pc, entry, K.SubFailed);
        Assert.False(entry.bInUse);
    }
}
