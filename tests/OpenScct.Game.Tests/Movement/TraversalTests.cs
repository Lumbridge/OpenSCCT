using System;
using System.Collections.Generic;
using OpenScct.Content;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using OpenScct.Physics.Collision;
using Xunit;
using static OpenScct.Game.Tests.Movement.MovementTestKit;

namespace OpenScct.Game.Tests.Movement;

/// <summary>
/// Ledge, vertical pipe, zip line and back to wall on synthetic levels with GEs (spec/movement/ledge.md,
/// pipe-vertical.md, zip-line.md, back-to-wall.md). The spy starts standing, fast, facing +Y.
/// </summary>
public sealed class TraversalTests
{
    private static readonly PlayerInputFrame JumpPress = new() { Jump = true };

    private static readonly PlayerInputFrame NoInput = new();

    private static GERecord Ge(int index, GEKind kind, params FVector[] points) =>
        new(index, $"GE{index}", kind, points, 0, GEPlatform.Common, GESource.Synthetic("test"));

    private static TraversalDoc Doc(params GERecord[] ges) => new("1.0", "test", ges);

    private static PawnRig TraversalRig(CollisionDataBuilder level, TraversalDoc ges, FVector at, int yaw = 16384, bool crouched = false)
    {
        PawnRig rig = PawnRig.Create(PawnTeam.Spy, level.Build(), ges);
        float h = crouched ? rig.Pawn.CrouchHeight : 87.5f;
        rig.Place(V(at.X, at.Y, at.Z + h + 2f), new FRotator(0, yaw, 0), new FRotator(0, yaw, 0), crouched);
        rig.SetFastSpeed(true);
        rig.WaitStill(120);
        return rig;
    }

    /// <summary>
    /// Steps until the controller is in <paramref name="state"/> (or the frames run out); true when it got there.
    /// The first frame takes <paramref name="first"/> (a jump press), the others <paramref name="hold"/>.
    /// </summary>
    private static bool StepUntil(PawnRig rig, PlayerInputFrame first, Name state, int frames, List<Name>? seen = null,
        PlayerInputFrame? hold = null)
    {
        for (int i = 0; i < frames; i++)
        {
            rig.Step(i == 0 ? first : hold ?? (first.Jump ? first with { Jump = false } : first));
            Name s = rig.Controller.GetStateName();
            if (seen is not null && (seen.Count == 0 || seen[^1] != s))
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

    // ---- ledge ------------------------------------------------------------------------------------------------

    /// <summary>A floor at z = 0 and a block from y = 100 to 600 whose top is <paramref name="top"/>, with an LG along its front edge.</summary>
    private static (CollisionDataBuilder Level, TraversalDoc Ges) LedgeWall(float top, float halfWidth = 300f)
    {
        CollisionDataBuilder b = Floor();
        b.AddBox(V(-halfWidth, 100f, 0f), V(halfWidth, 600f, top));
        return (b, Doc(Ge(0, GEKind.LG, V(-halfWidth, 100f, top), V(halfWidth, 100f, top))));
    }

    [Fact]
    public void JumpingUnderALedgeCatchesItAndHangs()
    {
        (CollisionDataBuilder level, TraversalDoc ges) = LedgeWall(250f);
        PawnRig rig = TraversalRig(level, ges, V(0f, 60f, 0f));
        var seen = new List<Name>();
        Assert.True(StepUntil(rig, JumpPress, MovementStateNames.OnLedge, 120, seen), string.Join(", ", seen));
        Assert.Contains(MovementStateNames.GrabLedge, seen);
        Assert.Equal(EPhysics.PHYS_Ledge, rig.Pawn.Physics);
        rig.Hold(default, 30);
        Assert.True(rig.Controller.IsInState(MovementStateNames.OnLedge));
        // Hanging: the hands at the ledge (the hang point 117 below it and 39 out from the wall).
        Assert.InRange(rig.Pawn.Location.Z, 250f - 117f - 2f, 250f - 117f + 2f);
        Assert.InRange(rig.Pawn.Location.Y, 100f - 39f - 2f, 100f - 39f + 2f);
        Assert.Equal(16384, rig.Pawn.Rotation.Yaw & 65535);
    }

    [Fact]
    public void HangingSpyShimmiesAlongTheLedge()
    {
        (CollisionDataBuilder level, TraversalDoc ges) = LedgeWall(250f);
        PawnRig rig = TraversalRig(level, ges, V(0f, 60f, 0f));
        Assert.True(StepUntil(rig, JumpPress, MovementStateNames.OnLedge, 120));
        rig.Hold(default, 10);
        float x0 = rig.Pawn.Location.X;
        rig.Hold(new PlayerInputFrame { BT_Right = 1 }, 60);
        Assert.True(rig.Controller.IsInState(MovementStateNames.OnLedge));
        // Facing +Y the right axis is -X.
        Assert.True(x0 - rig.Pawn.Location.X > 40f, $"moved {x0 - rig.Pawn.Location.X}");
    }

    [Fact]
    public void ForwardOnTheLedgePullsUpOntoTheTop()
    {
        (CollisionDataBuilder level, TraversalDoc ges) = LedgeWall(250f);
        PawnRig rig = TraversalRig(level, ges, V(0f, 60f, 0f));
        Assert.True(StepUntil(rig, JumpPress, MovementStateNames.OnLedge, 120));
        rig.Hold(default, 10);
        var seen = new List<Name>();
        Assert.True(StepUntil(rig, Forward, MovementStateNames.s_PlayerWalkingSt0H, 200, seen), string.Join(", ", seen));
        Assert.Contains(MovementStateNames.TopLedge, seen);
        // Standing on the block top (z 250) a little way in from the edge.
        Assert.InRange(rig.Pawn.Location.Z, 250f + 87.5f - 1f, 250f + 87.5f + 4f);
        Assert.True(rig.Pawn.Location.Y > 100f, $"y {rig.Pawn.Location.Y}");
        Assert.Equal(EPhysics.PHYS_Walking, rig.Pawn.Physics);
    }

    [Fact]
    public void BackOnTheLedgeDropsAndTheSameLedgeIsNotCaughtAgain()
    {
        (CollisionDataBuilder level, TraversalDoc ges) = LedgeWall(250f);
        PawnRig rig = TraversalRig(level, ges, V(0f, 60f, 0f));
        Assert.True(StepUntil(rig, JumpPress, MovementStateNames.OnLedge, 120));
        rig.Hold(default, 10);
        var seen = new List<Name>();
        Assert.True(StepUntil(rig, new PlayerInputFrame { BT_Down = 1 }, MovementStateNames.s_PlayerJump, 10, seen));
        Assert.Equal(250f, rig.Pawn.LastZipP.Z);
        Assert.True(StepUntil(rig, default, MovementStateNames.s_PlayerWalkingSt0H, 200, seen), string.Join(", ", seen));
        Assert.DoesNotContain(MovementStateNames.GrabLedge, seen);

        // Walking again (AnimateWalking) forgets the ledge, as the golden traversal.spy-ledge-drop shows.
        Assert.Equal(MovementK.FarZipPoint, rig.Pawn.LastZipP);
    }

    [Fact]
    public void ALowCeilingGivesTheCrouchedPullUp()
    {
        // A slab 400 above the floor over the block: 150 of room on the top, enough to crouch, not to stand.
        (CollisionDataBuilder level, TraversalDoc ges) = LedgeWall(250f);
        level.AddBox(V(-300f, 130f, 400f), V(300f, 600f, 450f));
        PawnRig rig = TraversalRig(level, ges, V(0f, 60f, 0f));
        Assert.True(StepUntil(rig, JumpPress, MovementStateNames.OnLedge, 120));
        rig.Hold(default, 10);
        var seen = new List<Name>();
        Assert.True(StepUntil(rig, Forward, MovementStateNames.s_PlayerWalkingCr0H, 200, seen), string.Join(", ", seen));
        Assert.Contains(MovementStateNames.TopLedgeCrouch, seen);
        Assert.True(rig.Pawn.bIsCrouched);
        Assert.True(rig.Pawn.Location.Y > 100f);
    }

    // ---- vertical pipe ----------------------------------------------------------------------------------------

    /// <summary>A pipe at (0, 200) from the floor to z 600, its side point toward -Y, flush with a platform behind it.</summary>
    private static PawnRig PipeRig()
    {
        CollisionDataBuilder b = Floor();
        b.AddBox(V(-300f, 230f, 0f), V(300f, 900f, 600f));
        TraversalDoc ges = Doc(Ge(0, GEKind.PIPE, V(0f, 200f, 0f), V(0f, 200f, 600f), V(0f, 190f, 0f)));
        PawnRig rig = TraversalRig(b, ges, V(0f, 0f, 0f));
        // The pipe hold compares NewZipP with ZipP (physOnPipe rule 2); nothing in the movement specs sets NewZipP for
        // a plain grab, so the test starts with it on this pipe (see the report).
        rig.Pawn.NewZipP1 = V(0f, 200f, 0f);
        rig.Pawn.NewZipP2 = V(0f, 200f, 600f);
        return rig;
    }

    [Fact]
    public void WalkingIntoAPipeGrabsItAndClimbsAt145()
    {
        PawnRig rig = PipeRig();
        var seen = new List<Name>();
        Assert.True(StepUntil(rig, Forward, MovementStateNames.s_OnPipe, 150, seen), string.Join(", ", seen));
        Assert.Contains(MovementStateNames.s_GrabPipe, seen);
        Assert.Equal(EPhysics.PHYS_OnPipe, rig.Pawn.Physics);
        rig.Hold(Forward, 20);
        // Held 37 out from the pipe (the pipe arms zone), facing it, climbing at 145.
        Assert.Equal(163f, rig.Pawn.Location.Y, 1);
        Assert.Equal(16384, rig.Pawn.Rotation.Yaw & 65535);
        Assert.Equal(MovementK.PipeClimbSpeed, rig.Pawn.Velocity.Z, 1);
    }

    [Fact]
    public void ClimbingToThePipeTopStepsOntoTheFloor()
    {
        PawnRig rig = PipeRig();
        Assert.True(StepUntil(rig, Forward, MovementStateNames.s_OnPipe, 150));
        var seen = new List<Name>();
        Assert.True(StepUntil(rig, Forward, MovementStateNames.s_PlayerWalkingSt0H, 600, seen), string.Join(", ", seen));
        Assert.Contains(MovementStateNames.s_TopPipe, seen);
        Assert.InRange(rig.Pawn.Location.Z, 600f + 87.5f - 1f, 600f + 87.5f + 4f);
        Assert.True(rig.Pawn.Location.Y > 230f);
    }

    [Fact]
    public void CrouchDropsFromThePipe()
    {
        PawnRig rig = PipeRig();
        Assert.True(StepUntil(rig, Forward, MovementStateNames.s_OnPipe, 150));
        rig.Hold(Forward, 30);
        var seen = new List<Name>();
        Assert.True(StepUntil(rig, new PlayerInputFrame { bCrouch = 1 }, MovementStateNames.s_PlayerWalkingSt0H, 200, seen, NoInput),
            string.Join(", ", seen));
        Assert.Contains(MovementStateNames.s_PlayerJump, seen);
        Assert.InRange(rig.Pawn.Location.Z, 87f, 92f);
    }

    // ---- zip line ---------------------------------------------------------------------------------------------

    /// <summary>
    /// A zip line over the floor, 330 high at y = 0 down to 250 at y = 3000 (high end first unless
    /// <paramref name="lowEndFirst"/>), and a wall across it at y = 2600. The spy stands under it and waits out the
    /// 2 s special move cooldown of his first stance change.
    /// </summary>
    private static PawnRig ZipRig(bool lowEndFirst = false)
    {
        CollisionDataBuilder b = Floor();
        b.AddBox(V(-300f, 2600f, 0f), V(300f, 2700f, 600f));
        FVector high = V(0f, 0f, 330f);
        FVector low = V(0f, 3000f, 250f);
        TraversalDoc ges = Doc(lowEndFirst ? Ge(0, GEKind.ZL, low, high) : Ge(0, GEKind.ZL, high, low));
        PawnRig rig = TraversalRig(b, ges, V(0f, 100f, 0f));
        rig.Hold(default, 130);
        return rig;
    }

    [Fact]
    public void JumpingUnderAZipLineGrabsItAndSlidesAt423()
    {
        PawnRig rig = ZipRig();
        var seen = new List<Name>();
        Assert.True(StepUntil(rig, JumpPress, MovementStateNames.s_ZipLine, 150, seen), string.Join(", ", seen));
        Assert.Contains(MovementStateNames.s_GrabZip, seen);
        Assert.Equal(EPhysics.PHYS_Linear, rig.Pawn.Physics);
        rig.Hold(default, 90);
        Assert.True(rig.Controller.IsInState(MovementStateNames.s_ZipLine));
        Assert.Equal(423.3f, rig.Pawn.GroundSpeed, 1);
        Assert.InRange(rig.Pawn.Velocity.Size(), 421f, 424f);
    }

    [Fact]
    public void StickForwardTucksIntoTheSpeedPoseAndBack()
    {
        PawnRig rig = ZipRig();
        Assert.True(StepUntil(rig, JumpPress, MovementStateNames.s_ZipLine, 150));
        var seen = new List<Name>();
        Assert.True(StepUntil(rig, Forward, MovementStateNames.s_ZipLineSpeed, 120, seen), string.Join(", ", seen));
        Assert.True(rig.Pawn.bIsZipSpeed);
        Assert.Equal(604.7f, rig.Pawn.GroundSpeed, 0);
        Assert.True(StepUntil(rig, default, MovementStateNames.s_ZipLine, 120, seen), string.Join(", ", seen));
        Assert.Contains(MovementStateNames.s_ZipLineTrToNormal, seen);
        Assert.False(rig.Pawn.bIsZipSpeed);
    }

    [Fact]
    public void CrouchDropsFromTheZipLine()
    {
        PawnRig rig = ZipRig();
        Assert.True(StepUntil(rig, JumpPress, MovementStateNames.s_ZipLine, 150));
        rig.Hold(default, 30);
        rig.Step(new PlayerInputFrame { bCrouch = 1 });
        rig.Step(default);
        Assert.True(rig.Controller.IsInState(MovementStateNames.s_PlayerJump));
        Assert.Equal(EPhysics.PHYS_Falling, rig.Pawn.Physics);
    }

    [Fact]
    public void AWallAcrossTheLinePlaysTheWallClipThenFalls()
    {
        // TestZipWall looks along ZipP1 - ZipP2: with the low end first that is down the line.
        PawnRig rig = ZipRig(lowEndFirst: true);
        Assert.True(StepUntil(rig, JumpPress, MovementStateNames.s_ZipLine, 150));
        var seen = new List<Name>();
        Assert.True(StepUntil(rig, default, MovementStateNames.s_PlayerJump, 600, seen), string.Join(", ", seen));
        Assert.Contains(MovementStateNames.s_ZipWall, seen);
        Assert.InRange(rig.Pawn.Location.Y, 2400f, 2600f);
    }

    // ---- back to wall -----------------------------------------------------------------------------------------

    private static readonly PlayerInputFrame BtwKey = new() { bUseBackToWall = 1 };

    /// <summary>A 400 wide wall whose face is at y = -40 (behind a spy at the origin facing +Y).</summary>
    private static PawnRig BtwRig(int yaw = 16384)
    {
        CollisionDataBuilder b = Floor();
        b.AddBox(V(-200f, -100f, 0f), V(200f, -40f, 300f));
        return TraversalRig(b, Doc(), V(0f, 0f, 0f), yaw);
    }

    [Fact]
    public void BackToWallKeyNextToAWallPutsTheBackToIt()
    {
        PawnRig rig = BtwRig();
        var seen = new List<Name>();
        Assert.True(StepUntil(rig, BtwKey, MovementStateNames.s_BTW_up, 120, seen, NoInput), string.Join(", ", seen));
        Assert.Contains(MovementStateNames.s_EnterBTW, seen);
        Assert.Equal(BTW_.BTWStand, rig.Pawn.BTW);
        Assert.Equal(V(0f, 1f, 0f), rig.Controller.vWallNormal);
        Assert.Equal(16384, rig.Pawn.Rotation.Yaw & 65535);
    }

    [Fact]
    public void AWallInFrontTurnsTheSpyRoundOntoIt()
    {
        PawnRig rig = BtwRig(yaw: 49152);
        Assert.True(StepUntil(rig, BtwKey, MovementStateNames.s_BTW_up, 120, null, NoInput));
        Assert.Equal(16384, rig.Pawn.Rotation.Yaw & 65535);
    }

    [Fact]
    public void SidestepRunsAlongTheWallAndPeeksAtItsEnd()
    {
        PawnRig rig = BtwRig();
        Assert.True(StepUntil(rig, BtwKey, MovementStateNames.s_BTW_up, 120, null, NoInput));
        var left = new PlayerInputFrame { BT_Left = 1 };
        rig.Hold(left, 10);
        Assert.Equal(130.9f, rig.Pawn.GroundSpeed, 1);
        Assert.Equal(130.9f, rig.Pawn.Velocity.Size2D(), 0);
        Assert.Equal(0f, rig.Pawn.Velocity.Y, 2);
        var seen = new List<Name>();
        Assert.True(StepUntil(rig, left, MovementStateNames.s_BTW_StandRight, 200, seen), string.Join(", ", seen));
        // Stopped before the wall's end (x = 200).
        Assert.InRange(rig.Pawn.Location.X, 120f, 200f);
        Assert.Equal(BTW_.BTWStandRight, rig.Pawn.BTW);
        rig.Hold(left, 30);
        Assert.Equal(0f, rig.Pawn.Velocity.Size2D(), 2);
        Assert.True(StepUntil(rig, default, MovementStateNames.s_BTW_up, 5));
    }

    [Fact]
    public void CrouchedOnTheWallTheSidestepIsSlower()
    {
        PawnRig rig = BtwRig();
        Assert.True(StepUntil(rig, BtwKey, MovementStateNames.s_BTW_up, 120, null, NoInput));
        rig.Hold(default, 40);
        Assert.True(StepUntil(rig, new PlayerInputFrame { bCrouch = 1 }, MovementStateNames.s_BTW_down, 5, null, NoInput));
        Assert.True(rig.Pawn.bIsCrouched);
        rig.Hold(new PlayerInputFrame { BT_Left = 1 }, 10);
        Assert.Equal(81.158f, rig.Pawn.GroundSpeed, 1);
    }

    [Fact]
    public void BackToWallKeyAgainLeavesTheWall()
    {
        PawnRig rig = BtwRig();
        Assert.True(StepUntil(rig, BtwKey, MovementStateNames.s_BTW_up, 120, null, NoInput));
        rig.Hold(default, 10);
        var seen = new List<Name>();
        Assert.True(StepUntil(rig, BtwKey, MovementStateNames.s_PlayerWalkingSt0H, 120, seen, NoInput), string.Join(", ", seen));
        Assert.Contains(MovementStateNames.s_SortieBTW, seen);
        Assert.Equal(BTW_.NOBTW, rig.Pawn.BTW);
    }

    [Fact]
    public void WalkingSlowlyOffAnEdgeTurnsRoundAndHangsFromIt()
    {
        CollisionDataBuilder b = Floor();
        b.AddBox(V(-3000f, -3000f, 0f), V(3000f, 0f, 800f));
        TraversalDoc ges = Doc(Ge(0, GEKind.LG, V(-3000f, 0f, 800f), V(3000f, 0f, 800f)));
        PawnRig rig = TraversalRig(b, ges, V(0f, -200f, 800f));
        rig.SetFastSpeed(false);
        var seen = new List<Name>();
        Assert.True(StepUntil(rig, Forward, MovementStateNames.OnLedge, 300, seen), string.Join(", ", seen));
        Assert.Contains(MovementStateNames.PlayerFallToBackGrab, seen);
        Assert.Contains(MovementStateNames.GrabLedge, seen);
        // Turned round to face the wall (-Y) and hanging below the edge.
        Assert.Equal(49152, rig.Pawn.Rotation.Yaw & 65535);
        rig.Hold(NoInput, 20);
        Assert.InRange(rig.Pawn.Location.Z, 800f - 117f - 2f, 800f - 117f + 2f);
        Assert.InRange(rig.Pawn.Location.Y, 39f - 2f, 39f + 2f);
    }

    [Fact]
    public void ShimmyingRoundAnOutsideCornerHangsOnTheNextLedge()
    {
        // The block's front edge and its left side (x = -300) share the corner (-300, 100). The spy faces +Y, so his
        // right is -X: shimmying right takes him round the corner onto the side, facing +X.
        CollisionDataBuilder b = Floor();
        b.AddBox(V(-300f, 100f, 0f), V(0f, 600f, 250f));
        TraversalDoc ges = Doc(
            Ge(0, GEKind.LG, V(-300f, 100f, 250f), V(0f, 100f, 250f)),
            Ge(1, GEKind.LG, V(-300f, 100f, 250f), V(-300f, 600f, 250f)));
        PawnRig rig = TraversalRig(b, ges, V(-150f, 60f, 0f));
        Assert.True(StepUntil(rig, JumpPress, MovementStateNames.OnLedge, 120));
        rig.Hold(NoInput, 10);
        var seen = new List<Name>();
        Assert.True(StepUntil(rig, new PlayerInputFrame { BT_Right = 1 }, MovementStateNames.s_ChangeLedgeWait, 400, seen), string.Join(", ", seen));
        Assert.True(StepUntil(rig, NoInput, MovementStateNames.OnLedge, 200, seen), string.Join(", ", seen));
        Assert.Equal(V(-300f, 100f, 250f), rig.Pawn.ZipP1);
        Assert.Equal(V(-300f, 600f, 250f), rig.Pawn.ZipP2);
        Assert.Equal(0, rig.Pawn.Rotation.Yaw & 65535);
        rig.Hold(NoInput, 30);
        Assert.True(rig.Controller.IsInState(MovementStateNames.OnLedge));
        Assert.InRange(rig.Pawn.Location.X, -339f - 2f, -339f + 2f);
        Assert.InRange(rig.Pawn.Location.Z, 133f - 2f, 133f + 2f);
    }
}
