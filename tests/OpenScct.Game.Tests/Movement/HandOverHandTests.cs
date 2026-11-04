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
/// Hand over hand on a synthetic bar (spec/movement/pipe-horizontal.md): an HOH bar 300 above the floor along X from
/// -600 to 600 at y = 0; the spy jumps into it from under it.
/// </summary>
public sealed class HandOverHandTests
{
    private const float BarZ = 300f;

    private static PawnRig BarRig(float x = 0f, PawnRigOptions? options = null)
    {
        CollisionDataBuilder b = Floor();
        TraversalDoc ges = Doc(Ge(0, GEKind.HOH, V(-600f, 0f, BarZ), V(600f, 0f, BarZ)));
        PawnRig rig = PawnRig.Create(PawnTeam.Spy, b.Build(), ges, options);
        rig.Place(V(x, 0f, 89.5f), default, default, crouched: false);
        rig.SetFastSpeed(true);
        rig.WaitStill(120);
        return rig;
    }

    private static PawnRig Hanging(float x = 0f, PawnRigOptions? options = null)
    {
        PawnRig rig = BarRig(x, options);
        var seen = new List<Name>();
        Assert.True(StepUntil(rig, JumpPress, MovementStateNames.s_OnPipeH, 200, seen, NoInput), Seen(seen));
        Assert.Contains(MovementStateNames.s_GrabPipH, seen);
        rig.Hold(NoInput, 20);
        return rig;
    }

    /// <summary>Authored script notify order without requiring converted character assets.</summary>
    private sealed class PipeFireAnimations : IPawnAnimationSource
    {
        private static PawnClipNotify Notify(float time, string name) =>
            new(time, new UAnimNotify_Script { NotifyName = new Name(name) });

        private static readonly Dictionary<Name, PawnClip> Clips = new()
        {
            [new Name("PipHStAlBg1")] = new(new Name("PipHStAlBg1"), 30, 30f)
            {
                Notifies = [Notify(0.36842105f, "NotifyDrawUndrawWeapon"), Notify(0.59999996f, "NotifyTakeArmControlForPipH2")],
            },
            [new Name("PipHStAlEd1")] = new(new Name("PipHStAlEd1"), 30, 30f)
            {
                Notifies = [Notify(0.24f, "NotifyDrawUndrawWeapon")],
            },
            [new Name("PipHStSpBg2")] = new(new Name("PipHStSpBg2"), 30, 30f)
            {
                Notifies = [Notify(0.26923078f, "NotifyDrawUndrawWeapon"), Notify(0.46153846f, "NotifyTakeArmControlForPipH4")],
            },
            [new Name("PipHStSpEd2")] = new(new Name("PipHStSpEd2"), 30, 30f)
            {
                Notifies = [Notify(0.1724138f, "NotifyDrawUndrawWeapon")],
            },
        };

        public bool TryGetClip(SBasePawn pawn, Name sequence, out PawnClip clip) =>
            Clips.TryGetValue(sequence, out clip!) || PawnAnimationSource.Default.TryGetClip(pawn, sequence, out clip);
    }

    [Fact]
    public void JumpingIntoABarGrabsItAndHangsUnderIt()
    {
        PawnRig rig = Hanging();
        Assert.Equal(EPhysics.PHYS_OnPipeH, rig.Pawn.Physics);
        Assert.Equal(PipHFire_.NoPipHFire, rig.Pawn.PipHFire);
        // The hands on the bar: the pawn hangs m_HandOverHandArmsZone.Z (128) below it.
        Assert.InRange(rig.Pawn.Location.Z, BarZ - 128f - 1f, BarZ - 128f + 1f);
        Assert.InRange(rig.Pawn.Location.Y, -1f, 1f);
    }

    [Fact]
    public void ForwardMovesAlongTheBarAt126()
    {
        PawnRig rig = Hanging();
        float x0 = rig.Pawn.Location.X;
        rig.Hold(Up, 60);
        Assert.True(rig.Controller.IsInState(MovementStateNames.s_OnPipeH));
        Assert.Equal(MovementK.HohMoveRequest, rig.Pawn.Velocity.Size2D(), 1);
        Assert.InRange(SvmMath.Abs(rig.Pawn.Location.X - x0), 120f, 130f);
    }

    [Fact]
    public void JumpSwingsTheLegsOverAndBack()
    {
        PawnRig rig = Hanging();
        var seen = new List<Name>();
        Assert.True(StepUntil(rig, JumpPress, MovementStateNames.s_OnPipeH_4, 120, seen, NoInput), Seen(seen));
        Assert.Contains(MovementStateNames.s_PipH_TR2_4, seen);
        // The legs-over cylinder: half height Pipe4Height, the top kept, zone configuration 3.
        Assert.Equal(rig.Pawn.Pipe4Height, rig.Pawn.CollisionHeight);
        Assert.True(rig.Pawn.bIsPipe4Decal);
        Assert.Equal(3, rig.Pawn.ZoneConfig);
        rig.Hold(Up, 30);
        Assert.Equal(MovementK.HohLegsOverMoveRequest, rig.Pawn.Velocity.Size2D(), 1);
        Assert.True(StepUntil(rig, JumpPress, MovementStateNames.s_OnPipeH, 120, seen, NoInput), Seen(seen));
        Assert.Contains(MovementStateNames.s_PipH_TR2_2, seen);
        Assert.Equal(87.5f, rig.Pawn.CollisionHeight);
        Assert.False(rig.Pawn.bIsPipe4Decal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DrawingAndHolsteringOnTheBarMovesThePistolBetweenHandAndHolster(bool inverted)
    {
        PawnRig rig = Hanging(options: new PawnRigOptions { Animations = new PipeFireAnimations() });
        SStickyShocker pistol = rig.Level.SpawnActor(new SStickyShocker { Instigator = rig.Pawn },
            rig.Pawn.Location, rig.Pawn.Rotation, rig.Pawn)!;
        Assert.NotNull(pistol);
        pistol.GiveTo(rig.Pawn);
        var seen = new List<Name>();
        Name hang = inverted ? MovementStateNames.s_OnPipeH_4 : MovementStateNames.s_OnPipeH;
        if (inverted)
        {
            Assert.True(StepUntil(rig, JumpPress, hang, 120, seen, NoInput), Seen(seen));
        }

        rig.Hold(NoInput, 40);
        Assert.Equal(0, rig.Pawn.WeaponLoaded);
        Name drawing = inverted ? MovementStateNames.s_PipH_TR2Fire4 : MovementStateNames.s_PipH_TR2Fire2;
        Name firing = inverted ? MovementStateNames.s_PipH_Fire4 : MovementStateNames.s_PipH_Fire2;
        Name holstering = inverted ? MovementStateNames.s_PipH_Fire2TR4 : MovementStateNames.s_PipH_Fire2TR2;
        Assert.True(StepUntil(rig, new PlayerInputFrame { bFireMode = 1 }, drawing, 10, seen, NoInput), Seen(seen));
        Assert.Equal(new Name(inverted ? "PipHStSpBg2" : "PipHStAlBg1"), rig.Pawn.PawnAnim[0].Sequence);
        Assert.Equal(0, rig.Pawn.WeaponLoaded);
        Assert.True(StepUntil(rig, NoInput, firing, 120, seen), Seen(seen));

        // Keep stepping beyond the draw's AnimEnd: a completed pose alone cannot make a holstered gun appear.
        rig.Hold(NoInput, 30);
        Assert.Equal(1, rig.Pawn.WeaponLoaded);
        Assert.Same(pistol, rig.Pawn.MyWeapon);
        Assert.Equal(new Name("B R Hand"), pistol.AttachmentBone);
        Assert.Equal(inverted ? PipHFire_.PipH4 : PipHFire_.PipH2, rig.Pawn.PipHFire);
        Assert.Equal(inverted ? ECamMode.ECM_HOHFPHanged : ECamMode.ECM_HOHFP, rig.Controller.MovementCamMode);

        Assert.True(StepUntil(rig, new PlayerInputFrame { bFireMode = 1 }, holstering, 10, seen, NoInput), Seen(seen));
        Assert.Equal(new Name(inverted ? "PipHStSpEd2" : "PipHStAlEd1"), rig.Pawn.PawnAnim[0].Sequence);
        Assert.True(StepUntil(rig, NoInput, hang, 120, seen), Seen(seen));
        rig.Step(NoInput);
        Assert.Equal(0, rig.Pawn.WeaponLoaded);
        Assert.Equal(new Name("HOLSTER"), pistol.AttachmentBone);
        Assert.Equal(EPhysics.PHYS_OnPipeH, rig.Pawn.Physics);
    }

    [Fact]
    public void TheLegsOverNeedsBarAhead()
    {
        // Facing +X 60 from the bar's end: the hands' point 95 ahead is past it.
        PawnRig rig = Hanging(x: 540f);
        rig.Step(JumpPress);
        rig.Hold(NoInput, 30);
        Assert.True(rig.Controller.IsInState(MovementStateNames.s_OnPipeH));
        Assert.False(rig.Pawn.bIsPipe4Decal);
    }

    [Fact]
    public void BackTurnsRound()
    {
        PawnRig rig = Hanging();
        int yaw = rig.Pawn.Rotation.Yaw & 65535;
        var seen = new List<Name>();
        Assert.True(StepUntil(rig, Down, MovementStateNames.s_PipH_Return, 10, seen), Seen(seen));
        Assert.Equal((yaw + 32768) & 65535, rig.Pawn.Rotation.Yaw & 65535);
        Assert.True(StepUntil(rig, NoInput, MovementStateNames.s_OnPipeH, 120, seen), Seen(seen));
    }

    [Fact]
    public void HoldingCrouchDropsFromTheBar()
    {
        PawnRig rig = Hanging();
        var seen = new List<Name>();
        Assert.True(StepUntil(rig, Crouch, MovementStateNames.s_PlayerWalkingSt0H, 300, seen, NoInput), Seen(seen));
        Assert.Contains(MovementStateNames.s_PlayerJump, seen);
        Assert.InRange(rig.Pawn.Location.Z, 87f, 92f);
    }
}
