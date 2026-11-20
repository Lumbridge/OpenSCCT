using System.Collections.Generic;
using OpenScct.Content;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Equipment;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using OpenScct.Game.Svm;
using OpenScct.Physics.Collision;
using Xunit;
using static OpenScct.Game.Tests.Movement.MovementTestKit;
using static OpenScct.Game.Tests.Movement.MovesetTestKit;

namespace OpenScct.Game.Tests.Movement;

/// <summary>
/// The ladder for both teams on a synthetic level (spec/movement/ladder.md): a ladder at y = 100 from the floor to
/// z = 400 against a platform whose top is z = 400; the climber side is -Y. Rungs sit 28 + 64 k above the bottom.
/// </summary>
public sealed class LadderTests
{
    private const float Top = 400f;

    private static (CollisionDataBuilder Level, TraversalDoc Ges) LadderLevel(float ceiling = 0f)
    {
        CollisionDataBuilder b = Floor();
        b.AddBox(V(-300f, 105f, 0f), V(300f, 900f, Top));
        if (ceiling > 0f)
        {
            b.AddBox(V(-300f, -200f, ceiling), V(300f, 104f, ceiling + 50f));
        }

        return (b, Doc(Ge(0, GEKind.LADDER, V(0f, 100f, 0f), V(0f, 100f, Top), V(0f, 90f, 0f))));
    }

    private static PawnRig LadderRig(PawnTeam team, float ceiling = 0f)
    {
        (CollisionDataBuilder level, TraversalDoc ges) = LadderLevel(ceiling);
        return GeRig(team, level, ges, V(0f, 20f, 0f));
    }

    private static bool OnRung(SBasePawn pawn)
    {
        float above = pawn.Location.Z - MovementK.LadderRungOffset;
        float off = above - MovementK.LadderRungSpacing * SvmMath.Round(above / MovementK.LadderRungSpacing);
        return SvmMath.Abs(off) < 0.6f;
    }

    [Theory]
    [InlineData(PawnTeam.Spy, 16384)]
    [InlineData(PawnTeam.Spy, -16384)]
    [InlineData(PawnTeam.Merc, 16384)]
    [InlineData(PawnTeam.Merc, -16384)]
    public void CrouchingNearALadderStaysCrouchedWithoutRepeatedTransitions(PawnTeam team, int yaw)
    {
        (CollisionDataBuilder level, TraversalDoc ges) = LadderLevel();
        PawnRig rig = GeRig(team, level, ges, V(0f, 20f, 0f), yaw: yaw);
        rig.Step(Crouch);
        float height = rig.Pawn.CrouchHeight;
        for (int tick = 0; tick < 120; tick++)
        {
            rig.Step(NoInput);
            Assert.True(rig.Pawn.bIsCrouched, $"tick {tick}: {rig.Controller.GetStateName()}");
            Assert.Equal(height, rig.Pawn.CollisionHeight);
            Assert.InRange(rig.Pawn.Location.Z, height, height + 3f);
        }

        Assert.True(rig.Controller.IsInState(team == PawnTeam.Spy
            ? MovementStateNames.s_PlayerWalkingCr0H : MovementStateNames.s_PlayerWalkingCr2H));
    }

    [Theory]
    [InlineData(PawnTeam.Spy)]
    [InlineData(PawnTeam.Merc)]
    public void WalkingIntoALadderFromCrouchStillGrabsIt(PawnTeam team)
    {
        (CollisionDataBuilder level, TraversalDoc ges) = LadderLevel();
        PawnRig rig = GeRig(team, level, ges, V(0f, 20f, 0f), crouched: true);
        Assert.True(StepUntil(rig, Up, MovementStateNames.s_OnLadder, 200));
        Assert.False(rig.Pawn.bIsCrouched);
        Assert.Equal(EPhysics.PHYS_OnLadder, rig.Pawn.Physics);
    }

    [Fact]
    public void CrouchedLadderGrabStillRequiresRoomToStand()
    {
        (CollisionDataBuilder level, TraversalDoc ges) = LadderLevel(ceiling: 150f);
        PawnRig rig = GeRig(PawnTeam.Spy, level, ges, V(0f, 60f, 0f), crouched: true);
        rig.Pawn.Acceleration = V(0, 1000, 0);
        rig.Driver.CheckArmsZone(rig.Pawn);
        Assert.True(rig.Pawn.bIsCrouched);
        Assert.True(rig.Controller.IsInState(MovementStateNames.s_PlayerWalkingCr0H));
        Assert.Equal(EPhysics.PHYS_Walking, rig.Pawn.Physics);
    }

    [Theory]
    [InlineData(PawnTeam.Spy)]
    [InlineData(PawnTeam.Merc)]
    public void WalkingIntoALadderGrabsItAndClimbsAt126(PawnTeam team)
    {
        PawnRig rig = LadderRig(team);
        var seen = new List<Name>();
        Assert.True(StepUntil(rig, Up, MovementStateNames.s_OnLadder, 200, seen), Seen(seen));
        Assert.Contains(MovementStateNames.s_GrabLadder, seen);
        Assert.Equal(EPhysics.PHYS_OnLadder, rig.Pawn.Physics);
        rig.Hold(Up, 30);
        Assert.True(rig.Controller.IsInState(MovementStateNames.s_OnLadder));
        // Held m_NarrowLadderArmsZone.X (37.5) out from the ladder, facing it, climbing at SpeedForLadder.
        Assert.Equal(100f - 37.5f, rig.Pawn.Location.Y, 1);
        Assert.Equal(16384, rig.Pawn.Rotation.Yaw & 65535);
        Assert.Equal(MovementK.SpeedForLadder, rig.Pawn.Velocity.Z, 1);
    }

    [Theory]
    [InlineData(PawnTeam.Spy)]
    [InlineData(PawnTeam.Merc)]
    public void ReleasingUpHoldsTheCompletedClimbPoseAndResumesFromIt(PawnTeam team)
    {
        PawnRig rig = LadderRig(team);
        Assert.True(StepUntil(rig, Up, MovementStateNames.s_OnLadder, 200));
        rig.Hold(Up, 20);
        rig.Hold(NoInput, 40);
        Assert.True(OnRung(rig.Pawn));
        Assert.Equal(0f, rig.Pawn.Velocity.Z);
        var animation = rig.Pawn.PawnAnim;
        float frame = animation[5].Frame;
        Assert.Equal(1f, animation[5].BlendAlpha);
        Assert.Equal(0f, animation[5].Rate);
        bool mirror = animation[0].bMirror;
        rig.Hold(NoInput, 30);
        Assert.Equal(frame, animation[5].Frame);
        Assert.Equal(1f, animation[5].BlendAlpha);
        Assert.Equal(mirror, animation[0].bMirror);
        rig.Step(Up);
        // The animation advances before movement, so the first resume tick still has the held phase.
        Assert.Equal(frame, animation[5].Frame);
        Assert.True(animation[5].Rate > 0f);
        rig.Step(Up);
        float advance = animation[5].Frame - frame;
        if (advance < 0f) { advance += 1f; }
        Assert.InRange(advance, 0.00001f, 0.2f);
    }

    [Fact]
    public void ReleasingUpStopsExactlyOnTheNextRung()
    {
        PawnRig rig = LadderRig(PawnTeam.Spy);
        Assert.True(StepUntil(rig, Up, MovementStateNames.s_OnLadder, 200));
        rig.Hold(Up, 20);
        float z = rig.Pawn.Location.Z;
        rig.Hold(NoInput, 40);
        Assert.True(OnRung(rig.Pawn), $"z {rig.Pawn.Location.Z}");
        Assert.True(rig.Pawn.Location.Z > z, "moved on to the next rung");
        Assert.True(rig.Pawn.Location.Z - z < MovementK.LadderRungSpacing + 1f);
        Assert.Equal(0f, rig.Pawn.Velocity.Z, 2);
    }

    [Fact]
    public void ReleasingDownStopsBackOnTheCurrentRung()
    {
        PawnRig rig = LadderRig(PawnTeam.Spy);
        Assert.True(StepUntil(rig, Up, MovementStateNames.s_OnLadder, 200));
        rig.Hold(Up, 90);
        rig.Hold(NoInput, 40);
        Assert.True(OnRung(rig.Pawn));
        rig.Hold(Down, 10);
        Assert.True(rig.Pawn.Velocity.Z < -100f);
        rig.Hold(NoInput, 40);
        Assert.True(OnRung(rig.Pawn), $"z {rig.Pawn.Location.Z}");
    }

    [Fact]
    public void ACeilingAboveStopsTheClimbOnARung()
    {
        PawnRig rig = LadderRig(PawnTeam.Spy, ceiling: 330f);
        Assert.True(StepUntil(rig, Up, MovementStateNames.s_OnLadder, 200));
        rig.Hold(Up, 200);
        Assert.True(rig.Controller.IsInState(MovementStateNames.s_OnLadder));
        Assert.True(OnRung(rig.Pawn), $"z {rig.Pawn.Location.Z}");
        Assert.Equal(0f, rig.Pawn.Velocity.Z, 2);
        // The (35, 35, 87.5) box 64 above must be clear: the head stays under the ceiling.
        Assert.True(rig.Pawn.Location.Z + 87.5f + 64f > 330f);
    }

    [Theory]
    [InlineData(PawnTeam.Spy)]
    [InlineData(PawnTeam.Merc)]
    public void ClimbingToTheTopStepsOntoThePlatform(PawnTeam team)
    {
        PawnRig rig = LadderRig(team);
        Assert.True(StepUntil(rig, Up, MovementStateNames.s_OnLadder, 200));
        var seen = new List<Name>();
        Name walking = team == PawnTeam.Spy ? MovementStateNames.s_PlayerWalkingSt0H : MovementStateNames.s_PlayerWalkingSt2H;
        Assert.True(StepUntil(rig, Up, walking, 900, seen), Seen(seen));
        Assert.Contains(MovementStateNames.s_LadderToTopTr, seen);
        Assert.Contains(MovementStateNames.s_LadderToTop, seen);
        Assert.Contains(MovementStateNames.s_OnLadderTop, seen);
        Assert.Contains(MovementStateNames.s_LadderTop, seen);
        rig.Hold(NoInput, 30);
        Assert.Equal(EPhysics.PHYS_Walking, rig.Pawn.Physics);
        Assert.InRange(rig.Pawn.Location.Z, Top + 87.5f - 1f, Top + 87.5f + 4f);
        Assert.True(rig.Pawn.Location.Y > 105f, $"y {rig.Pawn.Location.Y}");
        if (team == PawnTeam.Merc)
        {
            // ResetMercCam: the head camera and the behind view are off again.
            Assert.False(rig.Controller.bBehindView);
            Assert.False(rig.Controller.bFirstPersonWithPawn);
        }
    }

    [Fact]
    public void TheMercClimbsWithTheHeadCameraAndAnExaggeratedAim()
    {
        PawnRig rig = LadderRig(PawnTeam.Merc);
        Assert.True(StepUntil(rig, Up, MovementStateNames.s_OnLadder, 200));
        Assert.True(rig.Controller.bBehindView);
        Assert.True(rig.Controller.bNoReticule);
        Assert.True(rig.Controller.bFirstPersonWithPawn);
        // A view 1000 to the right of the ladder is aimed 1400 to the right.
        rig.Step(new PlayerInputFrame { View = new FRotator(0, 16384 + 1000, 0) });
        Assert.Equal(16384 + 1400, rig.Controller.MyDesiredRotation.Yaw & 65535);
    }

    [Theory]
    [InlineData("top")]
    [InlineData("bottom")]
    [InlineData("drop")]
    public void MercGetsTheEquippedRifleBackWhenLeavingALadder(string exit)
    {
        PawnRig rig = LadderRig(PawnTeam.Merc);
        var rifle = rig.Pawn.XLevel.Spawn<SWeaponDefense>(rig.Pawn.Location)!;
        rifle.GiveTo(rig.Pawn);
        rig.Pawn.WeaponLoaded = 2;
        rifle.bDrawWeapon = true;
        rifle.GotoState(new Name("s_Selected"));
        Assert.True(StepUntil(rig, Up, MovementStateNames.s_OnLadder, 200));
        Assert.False(rifle.bDrawWeapon);
        rig.Hold(Up, 45);
        // The mount lowered the gun, but the converted dismount clip has no draw notify.
        rig.Pawn.WeaponLoaded = 0;
        rifle.AnimateUnDraw();
        var seen = new List<Name>();
        var input = exit switch { "top" => Up, "bottom" => Down, _ => Crouch };
        Assert.True(StepUntil(rig, input, MovementStateNames.s_PlayerWalkingSt2H, 900, seen,
            exit == "drop" ? NoInput : input), Seen(seen));
        rig.Hold(NoInput, 15);
        Assert.False(rig.Controller.bBehindView);
        Assert.False(rig.Controller.bFirstPersonWithPawn);
        Assert.Equal(2, rig.Pawn.WeaponLoaded);
        Assert.True(rifle.bDrawWeapon);
        Assert.True(rifle.IsInState(new Name("s_Selected")), rifle.GetStateName().ToString());
        int clip = rifle.ClipAmmo;
        rig.Controller.eventFire(1f);
        Assert.Equal(clip - 1, rifle.ClipAmmo);
    }

    [Fact]
    public void MirroredLadderExitRestoresTheRifleOnTheServerWithoutOwnerAnimation()
    {
        PawnRig rig = LadderRig(PawnTeam.Merc);
        var rifle = rig.Pawn.XLevel.Spawn<SWeaponDefense>(rig.Pawn.Location)!;
        rifle.GiveTo(rig.Pawn);
        rig.Controller.RemoteRole = ENetRole.ROLE_AutonomousProxy;
        rig.Controller.GotoState(MovementStateNames.s_EndLadderDrawWeapon);
        rig.Pawn.WeaponLoaded = 0;
        rifle.bDrawWeapon = false;
        rifle.AnimateUnDraw();
        rig.Controller.PCChangeServerState(rig.Controller.MirrorStateAcknowledgementTime,
            MovementStateNames.s_PlayerWalkingSt2H);
        Assert.True(rig.Controller.IsInState(MovementStateNames.s_PlayerWalkingSt2H));
        Assert.Equal(2, rig.Pawn.WeaponLoaded);
        Assert.True(rifle.bDrawWeapon);
        Assert.True(rifle.IsInState(new Name("s_Selected")));
    }

    [Theory]
    [InlineData("top", false)]
    [InlineData("bottom", false)]
    [InlineData("top", true)]
    [InlineData("bottom", true)]
    public void M4RedrawOnDismountReturnsToAnActiveLocomotionPose(string exit, bool useCharacterClips)
    {
        var options = useCharacterClips ? ContentOptions() : null;
        if (useCharacterClips && options is null) Assert.Skip("no converted characters under .content/shared/chars");
        var (level, ges) = LadderLevel();
        PawnRig rig = PawnRig.Create(PawnTeam.Merc, level.Build(), ges, options);
        rig.Place(V(0f, 20f, 89.5f), new FRotator(0, 16384, 0), new FRotator(0, 16384, 0), false);
        rig.WaitStill(120);
        var rifle = rig.Pawn.XLevel.Spawn<M4A1S>(rig.Pawn.Location)!;
        rifle.GiveTo(rig.Pawn);
        rig.Pawn.WeaponLoaded = 2;
        rifle.bDrawWeapon = true;
        rifle.GotoState(new Name("s_Selected"));
        Assert.True(StepUntil(rig, Up, MovementStateNames.s_GrabLadder, 120));
        if (!useCharacterClips) PawnWeapons.MercNotifyDrawUndrawWeapon((SPAWNDEFENSE)rig.Pawn);
        Assert.True(StepUntil(rig, Up, MovementStateNames.s_OnLadder, 200));
        rig.Hold(Up, 45);
        var input = exit == "top" ? Up : Down;
        var dismount = exit == "top" ? MovementStateNames.s_LadderTop : MovementStateNames.s_EndLadderDrawWeapon;
        Assert.True(StepUntil(rig, input, dismount, 900));
        Assert.Equal(0, rig.Pawn.WeaponLoaded);
        // The synthetic character has no animation events. Supply the two authored hand notifies in that case.
        if (!useCharacterClips)
            PawnWeapons.MercNotifyDrawUndrawWeapon((SPAWNDEFENSE)rig.Pawn);
        bool drewFromHolster = false;
        for (int tick = 0; tick < 180; tick++)
        {
            drewFromHolster |= rifle.ViewAnim.IsAnimating(SWeaponDefense.MVTCHANNEL) &&
                rifle.ViewAnim.IsBackward(SWeaponDefense.MVTCHANNEL) &&
                rifle.ViewAnim.Sequence(SWeaponDefense.MVTCHANNEL) == ViewModelClips.FirstDrawStEd2;
            if (rig.Controller.IsInState(MovementStateNames.s_PlayerWalkingSt2H)) break;
            rig.Step(input);
        }
        Assert.True(rig.Controller.IsInState(MovementStateNames.s_PlayerWalkingSt2H));
        Assert.True(drewFromHolster);
        rig.Hold(NoInput, 90);
        Assert.False(rig.Controller.bBehindView);
        Assert.Equal(2, rig.Pawn.WeaponLoaded);
        Assert.True(rifle.bDrawWeapon);
        Assert.True(rifle.IsInState(new Name("s_Selected")));
        Assert.Equal(ViewModelClips.FirstWaitStFd2, rifle.ViewAnim.Sequence(SWeaponDefense.MVTCHANNEL));
        Assert.True(rifle.ViewAnim.IsAnimating(SWeaponDefense.MVTCHANNEL));
    }

    [Fact]
    public void CrouchLetsGoOfTheLadder()
    {
        PawnRig rig = LadderRig(PawnTeam.Spy);
        Assert.True(StepUntil(rig, Up, MovementStateNames.s_OnLadder, 200));
        rig.Hold(Up, 60);
        var seen = new List<Name>();
        Assert.True(StepUntil(rig, Crouch, MovementStateNames.s_PlayerWalkingSt0H, 300, seen, NoInput), Seen(seen));
        Assert.Contains(MovementStateNames.s_PlayerJump, seen);
        Assert.InRange(rig.Pawn.Location.Z, 87f, 92f);
    }

    [Fact]
    public void UseAndDownSlidesToTheFloor()
    {
        PawnRig rig = LadderRig(PawnTeam.Spy);
        Assert.True(StepUntil(rig, Up, MovementStateNames.s_OnLadder, 200));
        rig.Hold(Up, 90);
        var seen = new List<Name>();
        var slide = new PlayerInputFrame { BT_Down = 1, bUsing = 1 };
        Assert.True(StepUntil(rig, slide, MovementStateNames.s_SpeedLadder, 10, seen), Seen(seen));
        // Down held: the slide term -524 adds 524 uu/s every second.
        rig.Hold(slide, 30);
        Assert.True(rig.Pawn.Velocity.Z < -200f, $"vz {rig.Pawn.Velocity.Z}");
        Assert.True(StepUntil(rig, slide, MovementStateNames.PlayerLanding, 400, seen), Seen(seen));
        Assert.InRange(rig.Pawn.Location.Z, 87f, 92f);
    }

    [Fact]
    public void ReleasingDownDuringTheSlideCatchesTheLadderOnARung()
    {
        PawnRig rig = LadderRig(PawnTeam.Spy);
        Assert.True(StepUntil(rig, Up, MovementStateNames.s_OnLadder, 200));
        rig.Hold(Up, 90);
        var slide = new PlayerInputFrame { BT_Down = 1, bUsing = 1 };
        var seen = new List<Name>();
        Assert.True(StepUntil(rig, slide, MovementStateNames.s_SpeedLadder, 10, seen), Seen(seen));
        // Well below the top (more than 130 under it): the catch stops on a rung.
        rig.Hold(slide, 30);
        Assert.True(StepUntil(rig, NoInput, MovementStateNames.s_OnStopLadder, 200, seen), Seen(seen));
        Assert.Contains(MovementStateNames.s_LadderGrabFall, seen);
        Assert.True(OnRung(rig.Pawn), $"z {rig.Pawn.Location.Z}");
    }

    [Fact]
    public void WalkingToTheTopEdgeRepositionsAndTurnsOntoTheLadder()
    {
        (CollisionDataBuilder level, TraversalDoc ges) = LadderLevel();
        // On the platform facing the ladder's edge (-Y), close to the ladder top.
        PawnRig rig = GeRig(PawnTeam.Spy, level, ges, V(0f, 180f, Top), yaw: -16384);
        var seen = new List<Name>();
        Assert.True(StepUntil(rig, Up, MovementStateNames.s_downTopLadder, 400, seen), Seen(seen));
        Assert.Contains(MovementStateNames.s_WaitAtTopLadder, seen);
        // The turn down onto the ladder is a root-motion clip (lock 3, a half turn).
        Assert.Equal(EPhysics.PHYS_RootMotion, rig.Pawn.Physics);
        Assert.Equal(32768, rig.Pawn.rRotAfterRM);
    }

    [Fact]
    public void WalkingOffTheTopGetsOnTheLadderFromAboveWithTheRealClips()
    {
        PawnRigOptions? options = ContentOptions();
        if (options is null)
        {
            Assert.Skip("no converted characters under .content/shared/chars");
        }

        (CollisionDataBuilder level, TraversalDoc ges) = LadderLevel();
        PawnRig rig = PawnRig.Create(PawnTeam.Spy, level.Build(), ges, options);
        PawnClip top = PawnActions.Clip(rig.Pawn, new Name("LaddStAlBu0"));
        if (top.Root is null && top.RootTrack is null)
        {
            Assert.Skip("LaddStAlBu0 has no root track in the animation source");
        }

        rig.Place(V(0f, 180f, Top + 89.5f), new FRotator(0, -16384, 0), new FRotator(0, -16384, 0), false);
        rig.SetFastSpeed(true);
        rig.WaitStill(120);
        var seen = new List<Name>();
        Assert.True(StepUntil(rig, Up, MovementStateNames.s_OnLadderTop, 600, seen, NoInput), Seen(seen));
        Assert.Contains(MovementStateNames.s_downTopLadder, seen);
        Assert.Equal(EPhysics.PHYS_OnLadder, rig.Pawn.Physics);
        Assert.True(StepUntil(rig, Down, MovementStateNames.s_OnLadder, 200, seen), Seen(seen));
    }
}
