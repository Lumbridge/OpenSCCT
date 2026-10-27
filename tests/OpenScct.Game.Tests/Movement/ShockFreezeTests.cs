using OpenScct.Core;
using OpenScct.Game.AI;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using Xunit;
using static OpenScct.Game.Tests.Movement.MovementTestKit;

namespace OpenScct.Game.Tests.Movement;

/// <summary>
/// The sticky-shock freeze on the victim (spec/equipment/sticky-shock-effect.md rules 3 and 4, spec/native/input.md
/// rule 2.2): while iDontUpdatePadInput is set NativeComputeControlVariables zeroes the stick, the mouse and the
/// action bytes, so the shocked pawn neither walks nor turns until the freeze ends.
/// </summary>
public sealed class ShockFreezeTests
{
    [Theory]
    [InlineData(PawnTeam.Merc)]
    [InlineData(PawnTeam.Spy)]
    public void ShockedPawnCannotWalkOrTurnUntilTheFreezeEnds(PawnTeam team)
    {
        PawnRig rig = Rig(team);
        rig.Hold(Forward, 40);
        rig.Controller.ClientStickyShock(2, true);
        Assert.Equal(1, rig.Controller.iDontUpdatePadInput);

        var held = new PlayerInputFrame { BT_Up = 1, BT_Left = 1, MouseX = 2000f, MouseY = 1000f };
        rig.Hold(held, 30);
        float y0 = rig.Pawn.Location.Y;
        float x0 = rig.Pawn.Location.X;
        int yaw0 = rig.Controller.Rotation.Yaw;
        int pitch0 = rig.Controller.Rotation.Pitch;
        rig.Hold(held, 60);
        Assert.Equal(0f, rig.Controller.aForward);
        Assert.Equal(0f, rig.Controller.aStrafe);
        Assert.Equal(0f, rig.Controller.fTurnValue);
        Assert.InRange(rig.Pawn.Location.Y - y0, -2f, 2f);
        Assert.InRange(rig.Pawn.Location.X - x0, -2f, 2f);
        Assert.Equal(yaw0, rig.Controller.Rotation.Yaw);
        Assert.Equal(pitch0, rig.Controller.Rotation.Pitch);

        // The freeze ends on the first tick past int(t0 + F): the keys move the pawn again.
        int guard = 0;
        while (rig.Controller.iDontUpdatePadInput != 0 && guard++ < 600)
        {
            rig.Step(held);
        }

        Assert.Equal(0, rig.Controller.iDontUpdatePadInput);
        float y1 = rig.Pawn.Location.Y;
        rig.Hold(Forward, 60);
        Assert.True(rig.Pawn.Location.Y - y1 > 50f);
    }

    [Fact]
    public void ShockedBotKeepsItsViewAndAim()
    {
        // A bot's view stands for its mouse, which the freeze zeroes for a player.
        PawnRig rig = Rig(PawnTeam.Merc);
        rig.Controller.ClientStickyShock(2, true);
        FRotator view = rig.Controller.Rotation;
        FRotator aim = rig.Controller.MyDesiredRotation;
        var turn = new FRotator(0, view.Yaw + 8000, 0);
        new AiInput { BT_Up = 1, View = turn, Aim = turn }.ApplyTo(rig.Controller);
        Assert.Equal(view, rig.Controller.Rotation);
        Assert.Equal(aim, rig.Controller.MyDesiredRotation);
    }
}
