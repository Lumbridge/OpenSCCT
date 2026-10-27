using System.Collections.Generic;
using OpenScct.Content;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using OpenScct.Physics.Collision;
using Xunit;
using static OpenScct.Game.Tests.Movement.MovementTestKit;
using static OpenScct.Game.Tests.Movement.MovesetTestKit;

namespace OpenScct.Game.Tests.Movement;

/// <summary>
/// The co-op moves with two spies: the short ladder (boost) and the hoist climb, and their natives
/// (spec/movement/coop.md, spec/native/ANSWERS-moveset.md M1 to M6). The wall faces -Y at y = 100.
/// </summary>
public sealed class CoopTests
{
    private static readonly PlayerInputFrame CoopKey = new() { CoopMove = true };

    /// <summary>A wall from y = 100 with its top at <paramref name="top"/> and an LG along its front edge.</summary>
    private static (CollisionDataBuilder Level, TraversalDoc Ges) Wall(float top)
    {
        CollisionDataBuilder b = Floor();
        b.AddBox(V(-500f, 100f, 0f), V(500f, 700f, top));
        return (b, Doc(Ge(0, GEKind.LG, V(-500f, 100f, top), V(500f, 100f, top))));
    }

    private static void StepBoth(PawnRig rig, int frames, PlayerInputFrame input = default)
    {
        for (int i = 0; i < frames; i++)
        {
            rig.Step(input);
        }
    }

    /// <summary>Steps until <paramref name="pc"/> is in <paramref name="state"/>, recording the states it passes.</summary>
    private static bool StepUntilState(PawnRig rig, SPlayerController pc, Name state, int frames, List<Name> seen)
    {
        for (int i = 0; i < frames; i++)
        {
            rig.Step(NoInput);
            Name s = pc.GetStateName();
            if (seen.Count == 0 || seen[^1] != s)
            {
                seen.Add(s);
            }

            if (s == state)
            {
                return true;
            }
        }

        return false;
    }

    // ---- short ladder -------------------------------------------------------------------------------------------

    [Fact]
    public void UnderALedge512UpTheShortLadderSpotIsInFrontOfTheWall()
    {
        (CollisionDataBuilder level, TraversalDoc ges) = Wall(512f);
        PawnRig rig = GeRig(PawnTeam.Spy, level, ges, V(0f, 40f, 0f));
        Assert.True(rig.Pawn.CheckIfCanStartSHL(out FVector loc, out FVector dir));
        // R + 10 out from the closest point of the LG, 510 under it (the standing centre 2 above this floor), facing
        // away from the wall.
        Assert.Equal(0f, loc.X, 1);
        Assert.Equal(100f - 35f - 10f, loc.Y, 1);
        Assert.Equal(512f - 510f + 87.5f, loc.Z, 1);
        Assert.Equal(V(0f, -1f, 0f), dir);
        Assert.Equal((byte)SBasePawn.SHL_Standard, rig.Driver.FindHoistClimbType(rig.Pawn, ges.Ges[0], loc, dir, false, false));
    }

    [Fact]
    public void ALedgeOutOfTheBoostWindowHasNoSpot()
    {
        (CollisionDataBuilder level, TraversalDoc ges) = Wall(400f);
        PawnRig rig = GeRig(PawnTeam.Spy, level, ges, V(0f, 40f, 0f));
        Assert.False(rig.Pawn.CheckIfCanStartSHL(out _, out _));
    }

    [Fact]
    public void TheCoopKeyAloneMakesAShortLadderHelperWhoGetsReady()
    {
        (CollisionDataBuilder level, TraversalDoc ges) = Wall(512f);
        PawnRig rig = GeRig(PawnTeam.Spy, level, ges, V(0f, 40f, 0f));
        var seen = new List<Name>();
        Assert.True(StepUntil(rig, CoopKey, MovementStateNames.s_ShortLadder_P_Waiting, 200, seen), Seen(seen));
        Assert.Contains(MovementStateNames.s_ShortLadder_P_GotoDest, seen);
        for (int i = 0; i < 200 && !rig.Pawn.bReadyForShortLadder; i++)
        {
            rig.Step(NoInput);
        }

        Assert.True(rig.Pawn.bReadyForShortLadder);
        Assert.True(rig.Pawn.bIsCrouched);
        // Facing away from the wall, at the spot.
        Assert.Equal(-16384 & 65535, rig.Pawn.Rotation.Yaw & 65535);
        Assert.Equal(55f, rig.Pawn.Location.Y, 0);
    }

    [Fact]
    public void TheHelperStandsDownWithTheKeyOrTheStick()
    {
        (CollisionDataBuilder level, TraversalDoc ges) = Wall(512f);
        PawnRig rig = GeRig(PawnTeam.Spy, level, ges, V(0f, 55f, 0f), yaw: -16384);
        rig.Step(CoopKey);
        for (int i = 0; i < 200 && !rig.Pawn.bReadyForShortLadder; i++)
        {
            rig.Step(NoInput);
        }

        Assert.True(rig.Pawn.bReadyForShortLadder);
        // The stick cancels.
        rig.Step(Up);
        Assert.False(rig.Controller.IsInState(MovementStateNames.s_ShortLadder_P_Waiting));
        Assert.False(rig.Pawn.bReadyForShortLadder);
    }

    [Fact]
    public void AClimberFacingAReadyHelperIsBoostedIntoALedgeGrab()
    {
        (CollisionDataBuilder level, TraversalDoc ges) = Wall(512f);
        PawnRig rig = GeRig(PawnTeam.Spy, level, ges, V(0f, -150f, 0f));
        RigMate helper = rig.AddMate(PawnTeam.Spy, V(0f, 55f, 0f), -16384);
        StepBoth(rig, 30);
        helper.Input = CoopKey;
        for (int i = 0; i < 300 && !helper.Pawn.bReadyForShortLadder; i++)
        {
            rig.Step(NoInput);
        }

        Assert.True(helper.Pawn.bReadyForShortLadder, helper.Controller.GetStateName().ToString());
        Assert.Equal(SPlayerController.CA_ShortLadder, rig.Controller.CheckIfCanInteractWithMate(out Actor? mate));
        Assert.Same(helper.Pawn, mate);
        // The climber's ZipP are the ends of the ledge the boost reaches.
        Assert.Equal(512f, rig.Pawn.ZipP1.Z, 1);
        Assert.Equal(512f, rig.Pawn.ZipP2.Z, 1);

        var seen = new List<Name>();
        Assert.True(StepUntil(rig, CoopKey, MovementStateNames.s_ShortLadder_S_Jump, 300, seen), Seen(seen));
        Assert.Contains(MovementStateNames.s_ShortLadder_S_GotoPos, seen);
        Assert.Same(helper.Pawn, rig.Pawn.m_InteractionTarget);
        // The server started both halves: the helper waits for the climber and plays the boost.
        Assert.True(helper.Controller.IsInState(MovementStateNames.s_ShortLadder_P_WaitForShortLadder));
        // X(m_InteractionTarget, 254 SA_SHL_P_PERFORM) plays the boost clip; it does not write sAction.
        Assert.Equal(new Name("CoopCrAlHpEdP"), helper.Pawn.PawnAnim[0].Sequence);
        Assert.False(helper.Pawn.bReadyForShortLadder);
        // The boost clip ends in a ledge grab (m_LastSpecialMoveTime stamped). The built-in clips carry no root
        // motion, so the climber is still on the floor: physLedge finds no LG in his hands and NoGE drops him into a
        // jump in the same frame. The helper stands up.
        float before = rig.Controller.m_LastSpecialMoveTime;
        Assert.True(StepUntil(rig, NoInput, MovementStateNames.s_PlayerJump, 300, seen), Seen(seen));
        Assert.True(rig.Controller.m_LastSpecialMoveTime > before);
        var helperSeen = new List<Name>();
        Assert.True(StepUntilState(rig, helper.Controller, MovementStateNames.s_PlayerWalkingSt0H, 300, helperSeen), Seen(helperSeen));
    }

    /// <summary>
    /// A rig with the converted characters (the clips' root tracks), standing at <paramref name="at"/> (floor height),
    /// or null when .content has no characters.
    /// </summary>
    private static PawnRig? ContentRig(CollisionDataBuilder level, TraversalDoc ges, FVector at, int yaw = 16384)
    {
        if (ContentOptions() is not { } options)
        {
            return null;
        }

        PawnRig rig = PawnRig.Create(PawnTeam.Spy, level.Build(), ges, options);
        rig.Place(V(at.X, at.Y, at.Z + 87.5f + 2f), new FRotator(0, yaw, 0), new FRotator(0, yaw, 0), false);
        rig.SetFastSpeed(true);
        rig.WaitStill(120);
        return rig;
    }

    /// <summary>Boosts the climber of <paramref name="rig"/> by <paramref name="helper"/> (both from the T key) up to the boost clip.</summary>
    private static void BoostToJump(PawnRig rig, RigMate helper, List<Name> seen)
    {
        StepBoth(rig, 30);
        helper.Input = CoopKey;
        rig.Step(NoInput);
        helper.Input = NoInput;
        for (int i = 0; i < 300 && !helper.Pawn.bReadyForShortLadder; i++)
        {
            rig.Step(NoInput);
        }

        Assert.True(helper.Pawn.bReadyForShortLadder, helper.Controller.GetStateName().ToString());
        Assert.True(StepUntil(rig, CoopKey, MovementStateNames.s_ShortLadder_S_Jump, 300, seen), Seen(seen));
    }

    [Fact]
    public void WithTheRealClipsTheBoostedClimberHangsOnTheLedge()
    {
        Assert.SkipWhen(ContentOptions() is null, "the converted characters (root tracks) are not in .content");
        (CollisionDataBuilder level, TraversalDoc ges) = Wall(512f);
        PawnRig rig = ContentRig(level, ges, V(0f, -150f, 0f))!;
        RigMate helper = rig.AddMate(PawnTeam.Spy, V(0f, 55f, 0f), -16384);
        var seen = new List<Name>();
        BoostToJump(rig, helper, seen);
        Assert.Equal(new Name("CoopBtwAlHpEdS"), rig.Pawn.PawnAnim[0].Sequence);
        Assert.Equal(new Name("CoopCrAlHpEdP"), helper.Pawn.PawnAnim[0].Sequence);

        // Rule 10 to 12: the clip ends in GrabLedge, the hold keeps the hands on the LG, then OnLedge.
        Assert.True(StepUntil(rig, NoInput, MovementStateNames.OnLedge, 400, seen), Seen(seen) + " at " + rig.Pawn.Location);
        Assert.Contains(MovementStateNames.GrabLedge, seen);
        Assert.Equal(EPhysics.PHYS_Ledge, rig.Pawn.Physics);
        // The hang: 39 out from the LG line (y 100) and 117 under it.
        Assert.Equal(100f - 39f, rig.Pawn.Location.Y, 1);
        Assert.Equal(512f - 117f, rig.Pawn.Location.Z, 1);
        // Still hanging a second later.
        StepBoth(rig, 60);
        Assert.True(rig.Controller.IsInState(MovementStateNames.OnLedge), rig.Controller.GetStateName().ToString());
    }

    [Fact]
    public void OnSteeEThePlayersBoostPutsTheClimberOnTheLedge()
    {
        // SteeE's LG GE63 at y 2496, z 344 over the floor at -168 (coop.md evidence): the smoke's boost spot.
        string? dir = OpenScct.Game.Tests.AI.AiTestKit.SteeEFolder;
        Assert.SkipWhen(dir is null || ContentOptions() is null, "SteeE or the characters are not in .content");
        PawnRig rig = PawnRig.ForMap(PawnTeam.Spy, dir!);
        rig.Place(V(-3340f, 2451f - 150f, -168f + 89.5f), new FRotator(0, 16384, 0), new FRotator(0, 16384, 0), false);
        rig.SetFastSpeed(true);
        RigMate helper = rig.AddMate(PawnTeam.Spy, V(-3340f, 2451f, -168f), -16384);
        rig.WaitStill(120);
        var seen = new List<Name>();
        BoostToJump(rig, helper, seen);
        Assert.True(StepUntil(rig, NoInput, MovementStateNames.OnLedge, 400, seen), Seen(seen) + " at " + rig.Pawn.Location);
        StepBoth(rig, 60);
        Assert.True(rig.Controller.IsInState(MovementStateNames.OnLedge), rig.Controller.GetStateName().ToString());
        Assert.Equal(344f - 117f, rig.Pawn.Location.Z, 1);
    }

    [Theory]
    [InlineData(SBasePawn.SA_SHL_S_JUMP, "CoopCrAlHpEdP")]
    [InlineData(SBasePawn.SA_SHL_S_JUMPWALL, "CoopCrAlHpEdP")]
    [InlineData(SBasePawn.SA_HOIST_S_JUMP, "CoopDynHstBP")]
    [InlineData(SBasePawn.SA_HOIST_S_JUMP_NOWALL, "CoopDynHstBPf")]
    public void ThePartnersHalfRunsItsFullExecuteActionWithoutWritingItsAction(int action, string partnerClip)
    {
        (CollisionDataBuilder level, TraversalDoc ges) = Wall(512f);
        PawnRig rig = GeRig(PawnTeam.Spy, level, ges, V(0f, -150f, 0f));
        RigMate mate = rig.AddMate(PawnTeam.Spy, V(0f, 55f, 0f), -16384);
        StepBoth(rig, 10);
        rig.Pawn.m_InteractionTarget = mate.Pawn;
        int mateAction = mate.Pawn.sAction;
        // The partner's own prologue (coop.md rule 22): BlendChangeTime 0.25, fBlendTimeWithC0 0.
        mate.Pawn.BlendChangeTime = 9f;
        mate.Pawn.fBlendTimeWithC0 = 0.7f;
        rig.Pawn.ExecuteAction(action);
        Assert.Equal(new Name(partnerClip), mate.Pawn.PawnAnim[0].Sequence);
        Assert.Equal(0.25f, mate.Pawn.BlendChangeTime);
        Assert.Equal(0f, mate.Pawn.fBlendTimeWithC0);
        Assert.Equal(mateAction, mate.Pawn.sAction);
    }

    /// <summary>
    /// The chain of coop.md rules 14 to 21 after a boost (rules 4 to 12), all from the T key with the real clips: the
    /// boosted spy hangs and offers a hoist, the booster below climbs his legs and ends crouched on the top.
    /// </summary>
    private static void BoostThenHoist(PawnRig rig, RigMate helper, float ledgeZ)
    {
        var seen = new List<Name>();
        BoostToJump(rig, helper, seen);
        Assert.True(StepUntil(rig, NoInput, MovementStateNames.OnLedge, 400, seen), Seen(seen) + " at " + rig.Pawn.Location);
        var helperSeen = new List<Name>();
        Assert.True(StepUntilState(rig, helper.Controller, MovementStateNames.s_PlayerWalkingSt0H, 300, helperSeen), Seen(helperSeen));
        StepBoth(rig, 30);

        // The hanging spy offers a hoist (T).
        Assert.True(StepUntil(rig, CoopKey, MovementStateNames.s_Hoist_P_Waiting, 30, seen), Seen(seen));
        StepBoth(rig, 5);
        Assert.True(rig.Pawn.bReadyForHoistClimb);
        // The booster below is placed for it (rule 16): T climbs.
        Assert.Equal(SPlayerController.CA_HoistClimb, helper.Controller.CheckIfCanInteractWithMate(out Actor? found));
        Assert.Same(rig.Pawn, found);
        helper.Input = CoopKey;
        rig.Step(NoInput);
        helper.Input = NoInput;
        Assert.True(StepUntilState(rig, helper.Controller, MovementStateNames.s_Hoist_S_Jump, 300, helperSeen), Seen(helperSeen));
        Assert.True(rig.Controller.IsInState(MovementStateNames.s_Hoist_P_WaitForHoist), rig.Controller.GetStateName().ToString());
        Assert.True(StepUntilState(rig, helper.Controller, MovementStateNames.s_PlayerWalkingCr0H, 600, helperSeen), Seen(helperSeen) + " at " + helper.Pawn.Location);
        StepBoth(rig, 60);
        // On the top: his feet on the ledge's height, past the edge; the hanging spy back on the plain hang.
        Assert.InRange(helper.Pawn.Location.Z - helper.Pawn.CollisionHeight, ledgeZ - 3f, ledgeZ + 3f);
        Assert.True(rig.Controller.IsInState(MovementStateNames.OnLedge), rig.Controller.GetStateName().ToString());
    }

    [Fact]
    public void AfterTheBoostTheHangingSpyHoistsHisBooster()
    {
        Assert.SkipWhen(ContentOptions() is null, "the converted characters (root tracks) are not in .content");
        (CollisionDataBuilder level, TraversalDoc ges) = Wall(512f);
        PawnRig rig = ContentRig(level, ges, V(0f, -150f, 0f))!;
        RigMate helper = rig.AddMate(PawnTeam.Spy, V(0f, 55f, 0f), -16384);
        BoostThenHoist(rig, helper, 512f);
        Assert.True(helper.Pawn.Location.Y > 100f, $"the booster is on the top, at {helper.Pawn.Location}");
    }

    [Fact]
    public void OnSteeETheBoostAndTheHoistChainEndOnTheTop()
    {
        string? dir = OpenScct.Game.Tests.AI.AiTestKit.SteeEFolder;
        Assert.SkipWhen(dir is null || ContentOptions() is null, "SteeE or the characters are not in .content");
        PawnRig rig = PawnRig.ForMap(PawnTeam.Spy, dir!);
        rig.Place(V(-3340f, 2451f - 150f, -168f + 89.5f), new FRotator(0, 16384, 0), new FRotator(0, 16384, 0), false);
        rig.SetFastSpeed(true);
        RigMate helper = rig.AddMate(PawnTeam.Spy, V(-3340f, 2451f, -168f), -16384);
        rig.WaitStill(120);
        BoostThenHoist(rig, helper, 344f);
    }

    [Fact]
    public void AClimberLookingAwayFindsNoMate()
    {
        (CollisionDataBuilder level, TraversalDoc ges) = Wall(512f);
        PawnRig rig = GeRig(PawnTeam.Spy, level, ges, V(0f, -150f, 0f), yaw: -16384);
        RigMate helper = rig.AddMate(PawnTeam.Spy, V(0f, 55f, 0f), -16384);
        StepBoth(rig, 30);
        helper.Input = CoopKey;
        for (int i = 0; i < 300 && !helper.Pawn.bReadyForShortLadder; i++)
        {
            rig.Step(NoInput);
        }

        Assert.Equal(SPlayerController.CA_None, rig.Controller.CheckIfCanInteractWithMate(out _));
    }

    [Fact]
    public void TheSafeAreaNeedsAFloorBeforeTheMate()
    {
        CollisionDataBuilder b = Floor();
        PawnRig rig = PawnRig.Create(PawnTeam.Spy, b.Build());
        rig.Place(V(0f, -150f, 89.5f), new FRotator(0, 16384, 0), new FRotator(0, 16384, 0), false);
        RigMate mate = rig.AddMate(PawnTeam.Spy, V(0f, 55f, 0f), -16384);
        StepBoth(rig, 20);
        Assert.True(rig.Controller.AreaIsSafeToInteract(mate.Pawn));

        // The same mate over a hole: no floor 15 under the spot.
        CollisionDataBuilder holed = new();
        // The spot box (70 before the mate, 35 wide) lies over the gap; the mate stands at the edge of his floor.
        holed.AddBox(V(-1000f, -1000f, -20f), V(1000f, -110f, 0f));
        holed.AddBox(V(-1000f, 35f, -20f), V(1000f, 1000f, 0f));
        PawnRig rig2 = PawnRig.Create(PawnTeam.Spy, holed.Build());
        rig2.Place(V(0f, -150f, 89.5f), new FRotator(0, 16384, 0), new FRotator(0, 16384, 0), false);
        RigMate mate2 = rig2.AddMate(PawnTeam.Spy, V(0f, 55f, 0f), -16384);
        StepBoth(rig2, 20);
        Assert.False(rig2.Controller.AreaIsSafeToInteract(mate2.Pawn));
    }

    // ---- hoist ------------------------------------------------------------------------------------------------

    /// <summary>The helper dropped beside a wall whose top is 503 catches the ledge and hangs (centre about 386).</summary>
    private static (PawnRig Rig, RigMate Helper) HoistRig(float climberY = -100f, int climberYaw = 16384)
    {
        (CollisionDataBuilder level, TraversalDoc ges) = Wall(503f);
        PawnRig rig = GeRig(PawnTeam.Spy, level, ges, V(0f, climberY, 0f), climberYaw);
        RigMate helper = rig.AddMate(PawnTeam.Spy, V(0f, 100f - 39f, 310f), 16384);
        for (int i = 0; i < 300 && !helper.Controller.IsInState(MovementStateNames.OnLedge); i++)
        {
            rig.Step(NoInput);
        }

        Assert.True(helper.Controller.IsInState(MovementStateNames.OnLedge), helper.Controller.GetStateName().ToString());
        StepBoth(rig, 20);
        return (rig, helper);
    }

    [Fact]
    public void AHangingSpyOverAFloorOffersAStandardHoist()
    {
        (PawnRig rig, RigMate helper) = HoistRig();
        Assert.InRange(helper.Pawn.Location.Z - rig.Pawn.Location.Z, 237.36f, 356.04f);
        Assert.Equal((byte)SBasePawn.SHL_Standard, helper.Pawn.FindHoistClimbType(true));
        helper.Input = CoopKey;
        rig.Step(NoInput);
        Assert.True(helper.Controller.IsInState(MovementStateNames.s_Hoist_P_Waiting), helper.Controller.GetStateName().ToString());
        Assert.True(helper.Pawn.bReadyForHoistClimb);
        Assert.Equal(EPhysics.PHYS_Ledge, helper.Pawn.Physics);
    }

    [Fact]
    public void AClimberBehindTheHangingHelperIsHoisted()
    {
        (PawnRig rig, RigMate helper) = HoistRig();
        helper.Input = CoopKey;
        rig.Step(NoInput);
        Assert.True(helper.Pawn.bReadyForHoistClimb);
        Assert.Equal(SPlayerController.CA_HoistClimb, rig.Controller.CheckIfCanInteractWithMate(out Actor? mate));
        Assert.Same(helper.Pawn, mate);

        var seen = new List<Name>();
        Assert.True(StepUntil(rig, CoopKey, MovementStateNames.s_Hoist_S_Jump, 300, seen), Seen(seen));
        Assert.Contains(MovementStateNames.s_Hoist_S_GotoPos, seen);
        Assert.Equal(SBasePawn.SHL_Standard, rig.Controller.iHoistClimbType);
        Assert.True(helper.Controller.IsInState(MovementStateNames.s_Hoist_P_WaitForHoist));
        // The jump runs the helper's pull (X(m_InteractionTarget, 276)); the forced SA_HOIST_P_STOP of
        // s_Hoist_P_Waiting.EndState that follows keeps the pull clip playing.
        Assert.Equal(new Name("CoopDynHstBP"), helper.Pawn.PawnAnim[0].Sequence);
        Assert.False(helper.Pawn.bReadyForHoistClimb);
        // The jump clip, the finish clip, then crouched walking; the helper goes back to the plain hang.
        Assert.True(StepUntil(rig, NoInput, MovementStateNames.s_PlayerWalkingCr0H, 600, seen), Seen(seen));
        Assert.True(rig.Pawn.bIsCrouched);
        Assert.True(helper.Controller.IsInState(MovementStateNames.OnLedge), helper.Controller.GetStateName().ToString());
    }

    [Fact]
    public void AHoistMateOnTheOtherSideIsNotFound()
    {
        // The climber faces away from the wall: F . Fm is negative.
        (PawnRig rig, RigMate helper) = HoistRig(climberYaw: -16384);
        helper.Input = CoopKey;
        rig.Step(NoInput);
        Assert.True(helper.Pawn.bReadyForHoistClimb);
        Assert.Equal(SPlayerController.CA_None, rig.Controller.CheckIfCanInteractWithMate(out _));
    }
}
