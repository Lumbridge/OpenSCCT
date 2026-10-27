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
/// The barrier vault, the waist and feet ledges, the wall jump, the split jump and the Vandamme brace on synthetic
/// levels (spec/movement/barrier-and-low-ledges.md, wall-jump-split-vandamme.md). The spy faces +Y.
/// </summary>
public sealed class WallMoveTests
{
    // ---- barrier ----------------------------------------------------------------------------------------------

    /// <summary>A rail 20 thick across y = 100, its top 110, with an LG along its near top edge.</summary>
    private static PawnRig RailRig(float top = 110f)
    {
        CollisionDataBuilder b = Floor();
        b.AddBox(V(-500f, 100f, 0f), V(500f, 120f, top));
        TraversalDoc ges = Doc(Ge(0, GEKind.LG, V(-500f, 100f, top), V(500f, 100f, top)));
        return GeRig(PawnTeam.Spy, b, ges, V(0f, 0f, 0f));
    }

    [Fact]
    public void JumpAtARailVaultsOverIt()
    {
        PawnRig rig = RailRig();
        rig.Hold(Up, 30);
        var seen = new List<Name>();
        Assert.True(StepUntil(rig, JumpPress with { BT_Up = 1 }, MovementStateNames.s_BarrierJump, 120, seen, Up), Seen(seen));
        Assert.Contains(MovementStateNames.s_BarrierJumpDetect, seen);
        Assert.True(StepUntil(rig, NoInput, MovementStateNames.s_PlayerWalkingSt0H, 300, seen), Seen(seen));
        Assert.Contains(MovementStateNames.s_PlayerJump, seen);
        // Over the rail and down on the far side.
        Assert.True(rig.Pawn.Location.Y > 120f + 35f, $"y {rig.Pawn.Location.Y}");
        Assert.InRange(rig.Pawn.Location.Z, 87f, 92f);
    }

    [Fact]
    public void ARailTooHighIsNotVaulted()
    {
        // 50 above the pawn's centre: outside the window (5 below to 40 above).
        PawnRig rig = RailRig(top: 89f + 50f);
        rig.Hold(Up, 30);
        var seen = new List<Name>();
        Assert.False(StepUntil(rig, JumpPress with { BT_Up = 1 }, MovementStateNames.s_BarrierJumpDetect, 60, seen, Up), Seen(seen));
    }

    // ---- waist and feet ledges --------------------------------------------------------------------------------

    /// <summary>A block from y = 100 with its top at <paramref name="top"/> and an LG along the front edge; the spy stands 37 out.</summary>
    private static PawnRig LedgeBlockRig(float top)
    {
        CollisionDataBuilder b = Floor();
        b.AddBox(V(-500f, 100f, 0f), V(500f, 700f, top));
        TraversalDoc ges = Doc(Ge(0, GEKind.LG, V(-500f, 100f, top), V(500f, 100f, top)));
        return GeRig(PawnTeam.Spy, b, ges, V(0f, 100f - 37f, 0f));
    }

    [Fact]
    public void JumpingAtAWaistHighLedgeClimbsOntoIt()
    {
        PawnRig rig = LedgeBlockRig(top: 160f);
        var seen = new List<Name>();
        Assert.True(StepUntil(rig, JumpPress, MovementStateNames.s_WaistLedgeDetect, 60, seen, NoInput), Seen(seen));
        Assert.Equal(LedgeType.LEDGE_WAIST, rig.Pawn.TypeLedge);
        Assert.True(StepUntil(rig, NoInput, MovementStateNames.s_PlayerWalkingSt0H, 300, seen), Seen(seen));
        Assert.Contains(MovementStateNames.s_WaistLedge, seen);
        rig.Hold(NoInput, 30);
        Assert.InRange(rig.Pawn.Location.Z, 160f + 87f, 160f + 92f);
        Assert.True(rig.Pawn.Location.Y > 100f);
    }

    [Fact]
    public void TheFeetLedgeStepsUpOntoTheLedge()
    {
        PawnRig rig = LedgeBlockRig(top: 40f);
        // The airborne feet test: the jump state takes the event, the detect snap (TypeLedge FEET) then the step up.
        rig.Step(JumpPress);
        rig.Pawn.ZipP1 = V(-500f, 100f, 40f);
        rig.Pawn.ZipP2 = V(500f, 100f, 40f);
        rig.Controller.eventFeetLedge();
        Assert.True(rig.Controller.IsInState(MovementStateNames.s_FeetLedgeDetect));
        Assert.Equal(LedgeType.LEDGE_FEET, rig.Pawn.TypeLedge);
        var seen = new List<Name>();
        Assert.True(StepUntil(rig, NoInput, MovementStateNames.s_PlayerWalkingSt0H, 300, seen), Seen(seen));
        Assert.Contains(MovementStateNames.s_FeetLedge, seen);
        rig.Hold(NoInput, 30);
        Assert.InRange(rig.Pawn.Location.Z, 40f + 87f, 40f + 92f);
    }

    // ---- wall jump ----------------------------------------------------------------------------------------------

    /// <summary>A wall across y = 400 from the floor to <paramref name="top"/>, with an LG along its top when given.</summary>
    private static PawnRig WallRig(float top, bool ledge)
    {
        CollisionDataBuilder b = Floor();
        b.AddBox(V(-500f, 400f, 0f), V(500f, 700f, top));
        TraversalDoc ges = ledge ? Doc(Ge(0, GEKind.LG, V(-500f, 400f, top), V(500f, 400f, top))) : Doc();
        return GeRig(PawnTeam.Spy, b, ges, V(0f, 0f, 0f));
    }

    [Fact]
    public void ARunningJumpAtAWallUnderALedgeKicksUpAndCatchesIt()
    {
        PawnRig rig = WallRig(top: 89f + 250f, ledge: true);
        rig.Hold(Up, 20);
        Assert.True(rig.Pawn.GroundSpeed > MovementK.WallJumpSpeedGate);
        var seen = new List<Name>();
        Assert.True(StepUntil(rig, JumpPress with { BT_Up = 1 }, MovementStateNames.s_FirstWallJump, 120, seen, Up), Seen(seen));
        Assert.Contains(MovementStateNames.s_FirstWallJumpPrepareFromJump, seen);
        Assert.Equal(89f + 250f, rig.Controller.fGEZLocation, 1);
        Assert.True(StepUntil(rig, NoInput, MovementStateNames.s_WallJumpSalto, 120, seen), Seen(seen));
        // 250 below the ledge: the high climb.
        Assert.Equal(SBasePawn.SA_WLJP_UP, rig.Pawn.sAction);
    }

    [Fact]
    public void AWallWithoutALedgeGivesTheBackFlip()
    {
        PawnRig rig = WallRig(top: 600f, ledge: false);
        rig.Hold(Up, 20);
        var seen = new List<Name>();
        Assert.True(StepUntil(rig, JumpPress with { BT_Up = 1 }, MovementStateNames.s_FirstWallJumpSalto, 120, seen, Up), Seen(seen));
        Assert.Contains(MovementStateNames.s_FirstWallJumpSaltoPrepareFromJump, seen);
        Assert.True(StepUntil(rig, NoInput, MovementStateNames.s_WallJumpSalto, 200, seen), Seen(seen));
        // The flip ends in the long fall with no air control (the stand-in clips have no root motion, so the spy lands
        // in the same frame: the fall's landing follows).
        for (int i = 0; i < 200 && rig.Controller.IsInState(MovementStateNames.s_WallJumpSalto); i++)
        {
            rig.Step(NoInput);
        }

        Assert.True(rig.Controller.bBlockControlForJump);
        Assert.False(rig.Controller.IsInState(MovementStateNames.s_WallJumpSalto));
        Assert.True(StepUntil(rig, NoInput, MovementStateNames.s_PlayerWalkingSt0H, 300, seen), Seen(seen));
        Assert.Contains(MovementStateNames.PlayerLanding, seen);
    }

    // ---- split jump and Vandamme ----------------------------------------------------------------------------

    /// <summary>A corridor along Y between walls at x = 0 and x = 190 (the left wall 37 from the spy's centre).</summary>
    private static PawnRig CorridorRig()
    {
        CollisionDataBuilder b = Floor();
        b.AddBox(V(-100f, -2000f, 0f), V(0f, 2000f, 800f));
        b.AddBox(V(190f, -2000f, 0f), V(300f, 2000f, 800f));
        return GeRig(PawnTeam.Spy, b, null, V(37f, 0f, 0f));
    }

    /// <summary>Jumps and presses jump again at the apex (|Velocity.Z| under 150).</summary>
    private static void SplitAtApex(PawnRig rig, List<Name> seen)
    {
        rig.Step(JumpPress);
        for (int i = 0; i < 60 && rig.Pawn.Velocity.Z > 100f; i++)
        {
            rig.Step(NoInput);
        }

        Assert.True(StepUntil(rig, JumpPress, MovementStateNames.s_PreSplitJump, 3, seen), Seen(seen));
    }

    [Fact]
    public void AJumpAtTheApexBesideAWallStartsTheSplitJump()
    {
        PawnRig rig = CorridorRig();
        var seen = new List<Name>();
        SplitAtApex(rig, seen);
        // The wall on the left (-X) of a spy facing +Y: the split to the right, held still, turned along the wall.
        Assert.Equal(EPhysics.PHYS_None, rig.Pawn.Physics);
        Assert.Equal(default, rig.Pawn.Velocity);
        Assert.Equal(V(1f, 0f, 0f), rig.Pawn.vWallJumpDir);
        Assert.Equal(V(1f, 0f, 0f), rig.Controller.vWallNormal);
    }

    [Fact]
    public void ThePushReachesTheFacingWallAndBracesBetweenThem()
    {
        PawnRig rig = CorridorRig();
        var seen = new List<Name>();
        SplitAtApex(rig, seen);
        // The split clip's push notify.
        rig.Pawn.ImpulsionSplit();
        Assert.True(rig.Controller.IsInState(MovementStateNames.s_SplitJump));
        Assert.Equal(2, rig.Pawn.nJumpNumber);
        Assert.Equal(250f, rig.Pawn.Velocity.X, 1);
        Assert.True(StepUntil(rig, NoInput, MovementStateNames.Vandamme, 120, seen), Seen(seen));
        Assert.Contains(MovementStateNames.VandammeBegin, seen);
        rig.Hold(NoInput, 120);
        // Braced at the corridor's middle, at 87 uu/s placement.
        Assert.Equal(EPhysics.PHYS_Vandamme, rig.Pawn.Physics);
        Assert.Equal(95f, rig.Pawn.Location.X, 0);
        Assert.True(StepUntil(rig, Crouch, MovementStateNames.s_PlayerJump, 5, seen, NoInput), Seen(seen));
        Assert.Equal(0, rig.Pawn.nJumpNumber);
    }

    [Fact]
    public void TheVandammeViewIsPackedForTheOtherPlayers()
    {
        Assert.Equal(0x12345678 & unchecked((int)0xFFFF0000) | 0x4321, SPlayerController.PackLW(0x12345678, 0x4321));
        Assert.Equal(0x5678 | (0x4321 << 16), SPlayerController.PackHW(0x12345678, 0x4321));
        Assert.Equal(0x0000FFFF, SPlayerController.PackLW(0, -1));
    }

    // ---- after the vault, and the movement clips' notifies ------------------------------------------------------

    [Fact]
    public void TheFallAfterTheBarrierLeaves200ForwardAnd162Down()
    {
        // Case 205: Falling, Velocity 200 X with Z -162, JumpStAlBf0 (golden moveset.spy-vault-barrier).
        PawnRig rig = RailRig();
        rig.Pawn.ChangeEnumAction(SBasePawn.SA_FALL_AFTERBARRIER, true);
        Assert.Equal(EPhysics.PHYS_Falling, rig.Pawn.Physics);
        Assert.Equal(0f, rig.Pawn.Velocity.X, 2);
        Assert.Equal(200f, rig.Pawn.Velocity.Y, 2);
        Assert.Equal(-162f, rig.Pawn.Velocity.Z, 2);
        Assert.Equal(new Name("JumpStAlBf0"), rig.Pawn.PawnAnim[0].Sequence);
    }

    [Fact]
    public void TheMovementClipNotifiesReachThePawn()
    {
        PawnRig rig = RailRig();
        OpenScct.Game.Animation.AnimNotifyHandlers handlers = OpenScct.Game.Animation.AnimNotifyHandlers.Of(rig.Level);
        foreach (string n in new[] { "ImpulsionSplit", "NotifyTakeArmControlForSplitJump", "ChangeAnimsForDrawUndrawSplitJump",
                     "NotifyTakeArmControlForPipH2", "NotifyTakeArmControlForPipH4" })
        {
            Assert.True(handlers.TryRun(rig.Pawn, new Name(n)), n);
        }
    }
}
