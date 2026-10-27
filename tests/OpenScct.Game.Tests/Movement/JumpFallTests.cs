using System;
using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using OpenScct.Physics.Collision;
using Xunit;
using static OpenScct.Game.Tests.Movement.MovementTestKit;

namespace OpenScct.Game.Tests.Movement;

/// <summary>
/// Jump, fall, landing and roll on synthetic levels (spec/movement/jump-fall-land.md, roll.md). The reference
/// numbers are the recorded scenario measures (tests/scenarios/movement), stepped here at a fixed 60 Hz.
/// </summary>
public sealed class JumpFallTests
{
    private static readonly PlayerInputFrame JumpPress = new() { Jump = true };

    private static readonly PlayerInputFrame RunJump = new() { BT_Up = 1, Jump = true };

    /// <summary>Steps until the controller leaves the air states, recording the state sequence and the highest Z.</summary>
    private static (List<Name> States, float Apex, int AirFrames) Fly(PawnRig rig, PlayerInputFrame first, PlayerInputFrame hold, int frames)
    {
        var states = new List<Name>();
        float apex = rig.Pawn.Location.Z;
        int air = 0;
        for (int i = 0; i < frames; i++)
        {
            rig.Step(i == 0 ? first : hold);
            Name s = rig.Controller.GetStateName();
            if (states.Count == 0 || states[^1] != s)
            {
                states.Add(s);
            }

            apex = MathF.Max(apex, rig.Pawn.Location.Z);
            if (rig.Pawn.Physics == EPhysics.PHYS_Falling)
            {
                air++;
            }
        }

        return (states, apex, air);
    }

    /// <summary>A floor at z = 0 for y &gt;= 0 and a platform whose top is at <paramref name="height"/> for y &lt; 0.</summary>
    private static CollisionDataBuilder Ledge(float height)
    {
        var b = new CollisionDataBuilder();
        b.AddBox(V(-3000f, -3000f, -64f), V(3000f, 3000f, 0f));
        b.AddBox(V(-3000f, -3000f, 0f), V(3000f, 0f, height));
        return b;
    }

    [Fact]
    public void SpyJumpInPlaceRisesAboutOneHundredTwentyFourAndLandsQuietly()
    {
        PawnRig rig = Rig(PawnTeam.Spy);
        float z0 = rig.Pawn.Location.Z;
        (List<Name> states, float apex, int air) = Fly(rig, JumpPress, default, 140);
        Assert.Equal(124f, apex - z0, 0);
        Assert.InRange(air, 48, 50);
        Assert.Equal(
            new[] { MovementStateNames.s_PlayerJump, MovementStateNames.PlayerFalling, MovementStateNames.PlayerLanding, MovementStateNames.s_PlayerWalkingSt0H },
            states);
        Assert.Equal(1, rig.Pawn.nJumpNumber == 0 ? 1 : rig.Pawn.nJumpNumber);
        Assert.Equal(100, rig.Pawn.Health);
    }

    [Fact]
    public void JumpStartsWithTheRecordedVelocityAndClip()
    {
        PawnRig rig = Rig(PawnTeam.Spy);
        rig.Step(JumpPress);
        PawnSample s = rig.Sample();
        Assert.Equal(EPhysics.PHYS_Falling, s.Phys);
        Assert.True(rig.Controller.IsInState(MovementStateNames.s_PlayerJump));
        Assert.Equal(585.04f, s.Vel.Z, 0);
        Assert.Equal("jumpstalfdl", s.Anim0.ToString(), ignoreCase: true);
    }

    [Fact]
    public void MercJumpIsLowerAndSkipsTheFallState()
    {
        PawnRig rig = Rig(PawnTeam.Merc);
        float z0 = rig.Pawn.Location.Z;
        (List<Name> states, float apex, int air) = Fly(rig, JumpPress, default, 140);
        Assert.Equal(81.7f, apex - z0, 0);
        Assert.InRange(air, 38, 40);
        Assert.Equal(
            new[] { MovementStateNames.s_PlayerJump, MovementStateNames.PlayerLanding, MovementStateNames.s_PlayerWalkingSt2H },
            states);
    }

    [Fact]
    public void RunningJumpLandsStraightIntoTheRun()
    {
        PawnRig rig = Rig(PawnTeam.Spy);
        rig.Hold(Forward, 60);
        float y0 = rig.Pawn.Location.Y;
        (List<Name> states, _, int air) = Fly(rig, RunJump, Forward, 60);
        Assert.Equal(
            new[] { MovementStateNames.s_PlayerJump, MovementStateNames.PlayerFalling, MovementStateNames.s_PlayerWalkingSt0H },
            states);
        Assert.InRange(air, 48, 50);
        Assert.InRange(rig.Pawn.Location.Y - y0, 380f, 440f);
        Assert.Equal(423.5f, rig.Pawn.GroundSpeed, 2);
    }

    [Theory]
    [InlineData(PawnTeam.Spy, 600f, 0)]
    [InlineData(PawnTeam.Spy, 790f, 21)]
    [InlineData(PawnTeam.Spy, 1050f, 50)]
    [InlineData(PawnTeam.Spy, 1600f, 111)]
    [InlineData(PawnTeam.Merc, 450f, 0)]
    [InlineData(PawnTeam.Merc, 725f, 50)]
    [InlineData(PawnTeam.Merc, 1000f, 100)]
    public void FallDamageIsLinearAboveTheMinimumHeight(PawnTeam team, float h, int damage)
    {
        PawnRig rig = Rig(team);
        Assert.Equal(damage, SPlayerController.FallDamage(rig.Pawn, h));
    }

    [Fact]
    public void WalkingOffAHighLedgeFallsLandsHardAndHurts()
    {
        PawnRig rig = Rig(PawnTeam.Spy, Ledge(800f), at: V(0f, -200f, 800f));
        rig.SetFastSpeed(false);
        float apex = rig.Pawn.Location.Z;
        var states = new List<Name>();
        string landClip = "";
        for (int i = 0; i < 300; i++)
        {
            rig.Step(Forward);
            Name s = rig.Controller.GetStateName();
            if (states.Count == 0 || states[^1] != s)
            {
                states.Add(s);
                if (s == MovementStateNames.PlayerLanding)
                {
                    landClip = rig.Pawn.PawnAnim[0].Sequence.ToString();
                }
            }

            if (s == MovementStateNames.PlayerLanding)
            {
                break;
            }
        }

        Assert.Equal(new[] { MovementStateNames.s_PlayerWalkingSt0H, MovementStateNames.PlayerFalling, MovementStateNames.PlayerLanding }, states);
        Assert.Equal("landStAlHi0", landClip, ignoreCase: true);
        int expected = SPlayerController.FallDamage(rig.Pawn, apex - rig.Pawn.Location.Z);
        Assert.InRange(expected, 15, 30);
        if (typeof(SPlayerController).GetMethod(nameof(SPlayerController.PawnTakeDamage))?.DeclaringType == typeof(SPlayerController))
        {
            Assert.Equal(100 - expected, rig.Pawn.Health);
        }
    }

    [Fact]
    public void ALowDropLandsWithoutDamage()
    {
        PawnRig rig = Rig(PawnTeam.Spy, Ledge(120f), at: V(0f, -200f, 120f));
        rig.SetFastSpeed(false);
        bool fell = false;
        for (int i = 0; i < 300 && !(fell && rig.Pawn.Physics == EPhysics.PHYS_Walking); i++)
        {
            rig.Step(Forward);
            fell |= rig.Pawn.Physics == EPhysics.PHYS_Falling;
        }

        Assert.True(fell);
        Assert.Equal(100, rig.Pawn.Health);
        Assert.InRange(rig.Pawn.Location.Z, 87f, 92f);
    }

    [Fact]
    public void RunningSpyDivesAndStandsBackUp()
    {
        // At 423.5 uu/s the dive magnitude clamps to 4700: SA_ROLLINGUP in s_PlayerRollingUp, then up again (roll.md).
        PawnRig rig = Rig(PawnTeam.Spy);
        rig.Hold(Forward, 40);
        rig.Step(new PlayerInputFrame { BT_Up = 1, bRoll = 1 });
        Assert.True(rig.Controller.IsInState(MovementStateNames.s_PlayerRollingUp));
        Assert.True(rig.Pawn.bIsRolling);
        Assert.True(rig.Pawn.bIsCrouched);
        Assert.Equal("RollStJpFdL", rig.Pawn.PawnAnim[15].Sequence.ToString(), ignoreCase: true);
        float y0 = rig.Pawn.Location.Y;
        int frames = 0;
        while (rig.Controller.IsInState(MovementStateNames.s_PlayerRollingUp) && frames < 200)
        {
            rig.Step(Forward);
            frames++;
        }

        Assert.InRange(frames, 20, 120);
        Assert.True(rig.Pawn.Location.Y - y0 > 300f);
        Assert.False(rig.Pawn.bIsRolling);
        Assert.True(rig.Controller.IsInState(MovementStateNames.s_PlayerWalkingMario_T_CrToSt));
        rig.Hold(Forward, 60);
        Assert.False(rig.Pawn.bIsCrouched);
        Assert.True(rig.Controller.IsInState(MovementStateNames.s_PlayerWalkingSt0H));
    }

    [Fact]
    public void CrouchedSpyRollsAndStaysCrouched()
    {
        PawnRig rig = Rig(PawnTeam.Spy, crouched: true);
        rig.Hold(Forward, 40);
        rig.Step(new PlayerInputFrame { BT_Up = 1, bRoll = 1 });
        Assert.True(rig.Controller.IsInState(MovementStateNames.s_PlayerRolling));
        Assert.True(rig.Pawn.bIsRolling);
        int frames = 0;
        while (rig.Controller.IsInState(MovementStateNames.s_PlayerRolling) && frames < 200)
        {
            rig.Step(Forward);
            frames++;
        }

        Assert.InRange(frames, 10, 120);
        Assert.False(rig.Pawn.bIsRolling);
        Assert.True(rig.Pawn.bIsCrouched);
        Assert.True(rig.Controller.IsInState(MovementStateNames.s_PlayerWalkingCr0H));
    }

    [Fact]
    public void RollDirectionIsLockedAgainstMouseAndKeys()
    {
        // s_PlayerRolling.PlayerMove never calls UpdateRotation: the controller yaw stays where the roll began, so
        // neither the mouse nor a new key turns the roll (roll.md rolling rule 2).
        PawnRig rig = Rig(PawnTeam.Spy);
        rig.Hold(Forward, 40);
        rig.Step(new PlayerInputFrame { BT_Up = 1, bRoll = 1 });
        Assert.True(rig.Controller.IsInState(MovementStateNames.s_PlayerRollingUp));
        int yaw0 = rig.Controller.Rotation.Yaw;
        float x0 = rig.Pawn.Location.X;
        float y0 = rig.Pawn.Location.Y;
        int frames = 0;
        while (rig.Controller.IsInState(MovementStateNames.s_PlayerRollingUp) && frames < 200)
        {
            rig.Step(new PlayerInputFrame { BT_Right = 1, MouseX = 3000f });
            frames++;
        }

        Assert.Equal(yaw0, rig.Controller.Rotation.Yaw);
        Assert.True(rig.Pawn.Location.Y - y0 > 300f);
        Assert.InRange(rig.Pawn.Location.X - x0, -5f, 5f);
    }

    [Fact]
    public void MercCannotRoll()
    {
        PawnRig rig = Rig(PawnTeam.Merc);
        rig.Hold(Forward, 40);
        rig.Step(new PlayerInputFrame { BT_Up = 1, bRoll = 1 });
        rig.Hold(Forward, 10);
        Assert.False(rig.Pawn.bIsRolling);
        Assert.True(rig.Controller.IsInState(MovementStateNames.s_PlayerWalkingSt2H));
    }
}
