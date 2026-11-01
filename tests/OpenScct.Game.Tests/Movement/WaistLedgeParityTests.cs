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

/// <summary>The waist-to-hang height boundary, initial stance and approach angle, with the converted animation tracks.</summary>
public sealed class WaistLedgeParityTests
{
    [Theory]
    [InlineData(false, 60)]
    [InlineData(false, 120)]
    [InlineData(true, 60)]
    [InlineData(true, 120)]
    public void MoonDReportedWaistLedgeCanBeClimbed(bool moving, int hz)
    {
        string folder = System.IO.Path.Combine(RepoRoot, ".content", "svmap", "MoonD");
        Assert.SkipWhen(!System.IO.Directory.Exists(folder), "MoonD has not been converted into .content/svmap.");
        PawnRig rig = PawnRig.ForMap(PawnTeam.Spy, folder);
        rig.Place(V(-4217.1587f, -3249.0166f, 889.005f), new FRotator(0, 4783633, 0), new FRotator(-3634, -495, 0), false);
        rig.SetFastSpeed(true);
        Assert.True(rig.WaitStill(120));
        var seen = new List<Name>();
        int grabFrames = 0;
        float largestGrabDrop = 0f;
        float largestRootStep = 0f;
        float previousRoot = 0f;
        float previousWorldRoot = 0f;
        float grabFromRoot = 0f;
        for (int i = 0; i < hz * 2; i++)
        {
            bool wasGrabbing = rig.Controller.IsInState(MovementStateNames.s_WaistLedgeDetect);
            float previousZ = rig.Pawn.Location.Z;
            rig.Step(new PlayerInputFrame { Jump = i == 0, BT_Up = moving ? (byte)1 : (byte)0 }, 1f / hz);
            Name state = rig.Controller.GetStateName();
            bool grabbing = state == MovementStateNames.s_WaistLedgeDetect;
            PawnAnimChannel channel = rig.Pawn.PawnAnim[0];
            float root = channel.Clip?.RootAt(channel.ClipFrame).Z ?? 0f;
            if (grabbing && !wasGrabbing)
            {
                grabFromRoot = previousRoot;
            }

            // Use the animation system's pose-cache weight and converted root track to include the grab blend,
            // rather than treating actor motion alone as the visible motion. A new tween's first draw keeps
            // the preceding jump pose; the completed grab and climb have matching root positions.
            if (grabbing && channel.Frame < 0f)
            {
                root = grabFromRoot + (root - grabFromRoot) * channel.TweenWeight;
            }

            float worldRoot = rig.Pawn.Location.Z + rig.Pawn.PrePivot.Z + root;
            if (grabbing)
            {
                grabFrames++;
            }

            if (grabbing || wasGrabbing)
            {
                largestGrabDrop = System.Math.Max(largestGrabDrop, previousZ - rig.Pawn.Location.Z);
                largestRootStep = System.Math.Max(largestRootStep, System.Math.Abs(worldRoot - previousWorldRoot));
            }

            previousRoot = root;
            previousWorldRoot = worldRoot;

            if (seen.Count == 0 || seen[^1] != state)
            {
                seen.Add(state);
            }

            if (rig.Pawn.Location.X > -4176f && rig.Pawn.Location.Z > 980f && rig.Pawn.Physics == EPhysics.PHYS_Walking)
            {
                break;
            }
        }

        Assert.Contains(MovementStateNames.s_WaistLedgeDetect, seen);
        Assert.Contains(MovementStateNames.s_WaistLedge, seen);
        Assert.True(grabFrames / (float)hz >= 0.08f - 1f / hz, $"Grab lasted only {grabFrames} frames at {hz} Hz");
        Assert.True(largestGrabDrop <= 150f / hz + 0.05f, $"Grab dropped {largestGrabDrop} uu in one {hz} Hz step");
        Assert.True(largestRootStep <= 6f * 60f / hz, $"Blended grab root jumped {largestRootStep} uu in one {hz} Hz step");
        Assert.True(rig.WaitStill(120));
        Assert.Equal(EPhysics.PHYS_Walking, rig.Pawn.Physics);
        Assert.InRange(rig.Pawn.Location.X, -4176f, -4083f - rig.Pawn.CollisionRadius);
        Assert.InRange(rig.Pawn.Location.Z, 900f + rig.Pawn.CollisionHeight, 904f + rig.Pawn.CollisionHeight);
    }

    [Fact]
    public void RepeatedPositionNotificationsDoNotInterruptTheGrabAnimation()
    {
        CollisionDataBuilder level = Floor();
        level.AddBox(V(-500f, 100f, 0f), V(500f, 700f, 100f));
        TraversalDoc ges = Doc(Ge(0, GEKind.LG, V(-500f, 100f, 100f), V(500f, 100f, 100f)));
        PawnRig rig = PawnRig.Create(PawnTeam.Spy, level.Build(), ges, ContentOptions());
        rig.Place(V(0f, 63f, 89.5f), new FRotator(0, 16384, 0), new FRotator(0, 16384, 0), false);
        rig.SetFastSpeed(true);
        Assert.True(rig.WaitStill(120));
        Assert.True(StepUntil(rig, JumpPress, MovementStateNames.s_WaistLedgeDetect, 30, hold: NoInput));
        Assert.True(rig.Pawn.PawnAnim[0].bPlaying);
        rig.Controller.eventTopSynchro();
        rig.Controller.eventTopSynchro();
        Assert.True(rig.Controller.IsInState(MovementStateNames.s_WaistLedgeDetect));

        Assert.True(StepUntil(rig, NoInput, MovementStateNames.s_WaistLedge, 30));
    }

    [Theory]
    [InlineData(60)]
    [InlineData(120)]
    public void LowWaistLedgeClimbsWhenItsAuthoredHoldWouldEnterTheFloor(int hz)
    {
        CollisionDataBuilder level = Floor();
        level.AddBox(V(-500f, 100f, 0f), V(500f, 700f, 100f));
        TraversalDoc ges = Doc(Ge(0, GEKind.LG, V(-500f, 100f, 100f), V(500f, 100f, 100f)));
        PawnRig rig = PawnRig.Create(PawnTeam.Spy, level.Build(), ges, ContentOptions());
        rig.Place(V(0f, 63f, 89.5f), new FRotator(0, 16384, 0), new FRotator(0, 16384, 0), false);
        rig.SetFastSpeed(true);
        Assert.True(rig.WaitStill(120));
        var seen = new List<Name>();
        for (int i = 0; i < hz * 2; i++)
        {
            rig.Step(i == 0 ? JumpPress : NoInput, 1f / hz);
            Name state = rig.Controller.GetStateName();
            if (seen.Count == 0 || seen[^1] != state)
            {
                seen.Add(state);
            }

            if (rig.Pawn.Location.Y > 100f && rig.Pawn.Physics == EPhysics.PHYS_Walking)
            {
                break;
            }
        }

        Assert.Contains(MovementStateNames.s_WaistLedge, seen);
        Assert.True(rig.WaitStill(120));
        Assert.Equal(EPhysics.PHYS_Walking, rig.Pawn.Physics);
        Assert.True(rig.Pawn.Location.Y > 100f, Seen(seen));
        Assert.InRange(rig.Pawn.Location.Z, 187.5f, 191.5f);
    }

    [Theory]
    [InlineData(60)]
    [InlineData(120)]
    public void LowWaistLedgeStillRejectsAnObstructedLanding(int hz)
    {
        CollisionDataBuilder level = Floor();
        level.AddBox(V(-500f, 100f, 0f), V(500f, 700f, 100f));
        // Less than crouching room above the platform; the entry clearance must still reject it.
        level.AddBox(V(-500f, 100f, 205f), V(500f, 700f, 300f));
        TraversalDoc ges = Doc(Ge(0, GEKind.LG, V(-500f, 100f, 100f), V(500f, 100f, 100f)));
        PawnRig rig = PawnRig.Create(PawnTeam.Spy, level.Build(), ges, ContentOptions());
        rig.Place(V(0f, 63f, 89.5f), new FRotator(0, 16384, 0), new FRotator(0, 16384, 0), false);
        rig.SetFastSpeed(true);
        Assert.True(rig.WaitStill(120));
        var seen = new List<Name>();
        for (int i = 0; i < hz * 2; i++)
        {
            rig.Step(i == 0 ? JumpPress with { BT_Up = 1 } : Up, 1f / hz);
            seen.Add(rig.Controller.GetStateName());
        }

        Assert.DoesNotContain(MovementStateNames.s_WaistLedge, seen);
        Assert.DoesNotContain(MovementStateNames.s_WaistLedge_Cr, seen);
        Assert.True(rig.Pawn.Location.Y < 100f, $"{rig.Pawn.Location}: {Seen(seen)}");
    }

    [Fact]
    public void FloorLimitedWaistLedgeRechecksStandingClearanceAboveTheRaisedHold()
    {
        CollisionDataBuilder level = Floor();
        level.AddBox(V(-500f, 100f, 0f), V(500f, 700f, 100f));
        // The original Z=80 hold clears this ceiling, but the floor-limited standing hold does not.
        level.AddBox(V(-500f, 100f, 285f), V(500f, 700f, 320f));
        TraversalDoc ges = Doc(Ge(0, GEKind.LG, V(-500f, 100f, 100f), V(500f, 100f, 100f)));
        PawnRig rig = PawnRig.Create(PawnTeam.Spy, level.Build(), ges, ContentOptions());
        rig.Place(V(0f, 63f, 89.5f), new FRotator(0, 16384, 0), new FRotator(0, 16384, 0), false);
        rig.SetFastSpeed(true);
        Assert.True(rig.WaitStill(120));
        var seen = new List<Name>();
        float highestHead = 0f;
        for (int i = 0; i < 120; i++)
        {
            rig.Step(i == 0 ? JumpPress : NoInput);
            seen.Add(rig.Controller.GetStateName());
            if (rig.Pawn.Location.Y + rig.Pawn.CollisionRadius > 100f)
            {
                highestHead = System.Math.Max(highestHead, rig.Pawn.Location.Z + rig.Pawn.CollisionHeight);
            }
        }

        Assert.Contains(MovementStateNames.s_WaistLedge_Cr, seen);
        Assert.DoesNotContain(MovementStateNames.s_WaistLedge, seen);
        Assert.True(highestHead < 285f, $"Head reached {highestHead}");
        Assert.Equal(EPhysics.PHYS_Walking, rig.Pawn.Physics);
        Assert.True(rig.Pawn.bIsCrouched);
        Assert.InRange(rig.Pawn.Location.Z, 100f + rig.Pawn.CrouchHeight, 104f + rig.Pawn.CrouchHeight);
    }

    [Theory]
    [InlineData("GE0", 1f, false)]
    [InlineData("GE71", -1f, true)]
    public void PolarBaseMidHeightLedgesClimbOntoTheirAuthoredSurface(string id, float side, bool crouched)
    {
        string folder = System.IO.Path.Combine(RepoRoot, ".content", "svmap", "PolaD");
        Assert.SkipWhen(!System.IO.Directory.Exists(folder), "PolaD has not been converted into .content/svmap.");
        PawnRig rig = PawnRig.ForMap(PawnTeam.Spy, folder);
        GERecord ge = TraversalDoc.Load(System.IO.Path.Combine(folder, "sim", "traversal.json")).Find(id)!;
        FVector middle = (ge.P0 + ge.P1) * 0.5f;
        FVector along = (ge.P1 - ge.P0).SafeNormal();
        FVector normal = new FVector(along.Y, -along.X, 0f) * side;
        FVector start = middle + normal * 37f - new FVector(0f, 0f, 1f);
        Assert.True(rig.Collision!.LineCheck(out FCheckResult hit, rig.Pawn, start - new FVector(0f, 0f, 250f),
            start, default, ETraceFlags.TRACE_AllBlocking));
        FVector at = hit.Location + new FVector(0f, 0f, 89.5f);
        int yaw = (-normal).Rotation().Yaw;
        rig.Place(at, new FRotator(0, yaw, 0), new FRotator(0, yaw, 0), false);
        rig.SetFastSpeed(true);
        Assert.True(rig.WaitStill(120));
        var seen = new List<Name>();
        rig.Step(JumpPress);
        for (int i = 0; i < 300; i++)
        {
            rig.Step(rig.Controller.IsInState(MovementStateNames.OnLedge) ? Up : NoInput);
            Name state = rig.Controller.GetStateName();
            if (seen.Count == 0 || seen[^1] != state)
            {
                seen.Add(state);
            }

            if (rig.Pawn.Physics == EPhysics.PHYS_Walking && rig.Pawn.Location.Z > middle.Z + rig.Pawn.CollisionHeight - 1f)
            {
                break;
            }
        }

        Assert.Contains(crouched ? MovementStateNames.s_WaistLedge_Cr : MovementStateNames.s_WaistLedge, seen);
        Assert.Equal(crouched, rig.Pawn.bIsCrouched);
        Assert.Equal(EPhysics.PHYS_Walking, rig.Pawn.Physics);
        Assert.InRange(rig.Pawn.Location.Z, middle.Z + rig.Pawn.CollisionHeight - 1f, middle.Z + rig.Pawn.CollisionHeight + 4f);
    }

    [Theory]
    [InlineData(false, false, 16384)]
    [InlineData(true, true, 16384)]
    [InlineData(false, true, 8192)]
    [InlineData(true, false, 24576)]
    public void MidHeightLedgeCanBeClimbedFromEitherSpeedAndStance(bool fast, bool crouched, int yaw)
    {
        CollisionDataBuilder level = Floor();
        level.AddBox(V(-500f, 100f, 0f), V(500f, 700f, 160f));
        TraversalDoc ges = Doc(Ge(0, GEKind.LG, V(-500f, 100f, 160f), V(500f, 100f, 160f)));
        PawnRig rig = PawnRig.Create(PawnTeam.Spy, level.Build(), ges, ContentOptions());
        float height = crouched ? rig.Pawn.CrouchHeight : rig.Pawn.CollisionHeight;
        rig.Place(V(0f, 63f, height + 2f), new FRotator(0, yaw, 0), new FRotator(0, yaw, 0), crouched);
        rig.SetFastSpeed(fast);
        rig.WaitStill(120);
        var seen = new List<Name>();
        rig.Step(JumpPress);
        for (int i = 0; i < 300; i++)
        {
            rig.Step(NoInput);
            Name state = rig.Controller.GetStateName();
            if (seen.Count == 0 || seen[^1] != state)
            {
                seen.Add(state);
            }

            if (rig.Pawn.Physics == EPhysics.PHYS_Walking && rig.Pawn.Location.Z > 240f)
            {
                break;
            }
        }

        Assert.True(rig.Pawn.Location.Y > 100f, $"{rig.Pawn.Location}: {Seen(seen)}");
        Assert.InRange(rig.Pawn.Location.Z, 247f, 252f);
    }

    [Theory]
    [InlineData(160f)]
    public void MidHeightLedgeUnderACeilingMantlesCrouched(float top)
    {
        CollisionDataBuilder level = Floor();
        level.AddBox(V(-500f, 100f, 0f), V(500f, 700f, top));
        level.AddBox(V(-500f, 130f, top + 140f), V(500f, 700f, top + 200f));
        TraversalDoc ges = Doc(Ge(0, GEKind.LG, V(-500f, 100f, top), V(500f, 100f, top)));
        PawnRig rig = PawnRig.Create(PawnTeam.Spy, level.Build(), ges, ContentOptions());
        rig.Place(V(0f, 63f, 89.5f), new FRotator(0, 16384, 0), new FRotator(0, 16384, 0), false);
        rig.SetFastSpeed(true);
        rig.WaitStill(120);
        var seen = new List<Name>();
        Assert.True(StepUntil(rig, JumpPress, MovementStateNames.s_WaistLedgeDetect, 120, seen, NoInput), Seen(seen));
        Assert.True(StepUntil(rig, NoInput, MovementStateNames.s_PlayerWalkingCr0H, 300, seen), Seen(seen));
        Assert.True(rig.Pawn.Location.Y > 100f, $"{rig.Pawn.Location}: {Seen(seen)}");
        Assert.InRange(rig.Pawn.Location.Z, top + rig.Pawn.CrouchHeight, top + rig.Pawn.CrouchHeight + 3f);
    }

    [Theory]
    [InlineData(128f, false)]
    [InlineData(160f, false)]
    [InlineData(200f, false)]
    [InlineData(208f, false)]
    [InlineData(128f, true)]
    [InlineData(160f, true)]
    [InlineData(200f, true)]
    [InlineData(208f, true)]
    public void JumpingAgainstAMidHeightLedgeMantlesOntoIt(float top, bool moving)
    {
        CollisionDataBuilder level = Floor();
        level.AddBox(V(-500f, 100f, 0f), V(500f, 700f, top));
        TraversalDoc ges = Doc(Ge(0, GEKind.LG, V(-500f, 100f, top), V(500f, 100f, top)));
        PawnRig rig = PawnRig.Create(PawnTeam.Spy, level.Build(), ges, ContentOptions());
        rig.Place(V(0f, 63f, 89.5f), new FRotator(0, 16384, 0), new FRotator(0, 16384, 0), false);
        rig.SetFastSpeed(true);
        rig.WaitStill(120);
        if (moving)
        {
            rig.Hold(Up, 30);
        }

        var seen = new List<Name>();
        rig.Step(JumpPress with { BT_Up = moving ? (byte)1 : (byte)0 });
        for (int i = 0; i < 300; i++)
        {
            rig.Step(moving || rig.Controller.IsInState(MovementStateNames.OnLedge) ? Up : NoInput);
            Name state = rig.Controller.GetStateName();
            if (seen.Count == 0 || seen[^1] != state)
            {
                seen.Add(state);
            }

            if (rig.Pawn.Physics == EPhysics.PHYS_Walking && rig.Pawn.Location.Z > top + 80f)
            {
                break;
            }
        }

        Assert.True(rig.Pawn.Location.Z >= top + 87f, $"{rig.Pawn.Location}: {Seen(seen)}");
        Assert.InRange(rig.Pawn.Location.Z, top + 87f, top + 92f);
        Assert.True(rig.Pawn.Location.Y > 100f, $"{rig.Pawn.Location}: {Seen(seen)}");
        Assert.Equal(EPhysics.PHYS_Walking, rig.Pawn.Physics);
    }
}
