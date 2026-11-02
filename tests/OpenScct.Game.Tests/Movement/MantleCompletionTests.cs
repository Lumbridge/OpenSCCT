using System.Collections.Generic;
using OpenScct.Content;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using Xunit;
using static OpenScct.Game.Tests.Movement.MovementTestKit;
using static OpenScct.Game.Tests.Movement.MovesetTestKit;

namespace OpenScct.Game.Tests.Movement;

public sealed class MantleCompletionTests
{
    [Theory]
    [InlineData(SBasePawn.SA_WAISTLEDGE_DETECT, "s_WaistLedgeDetect")]
    [InlineData(SBasePawn.SA_FEETLEDGE_DETECT, "s_FeetLedgeDetect")]
    [InlineData(SBasePawn.SA_BARRIER_JUMP_DETECT, "s_BarrierJumpDetect")]
    [InlineData(SBasePawn.SA_LEDGE_GRAB, "GrabLedge")]
    public void LostLedgeEndpointsReleaseThePlayerInsteadOfWaitingForever(int action, string state)
    {
        PawnRig rig = Rig(PawnTeam.Spy, options: ContentOptions());
        rig.Pawn.ZipP1 = rig.Pawn.ZipP2 = V(0, 100, 128);
        rig.Pawn.ChangeEnumAction(action);
        rig.Controller.GotoState(new Name(state));
        rig.Hold(NoInput, 120);
        Assert.Equal(EPhysics.PHYS_Walking, rig.Pawn.Physics);
        Assert.StartsWith("s_PlayerWalking", rig.Controller.GetStateName().ToString());
    }

    public static TheoryData<int, int, bool> Ledges()
    {
        var cases = new TheoryData<int, int, bool>();
        foreach (int height in new[] { 96, 100, 128, 175, 208, 264, 320, 408 })
            foreach (int hz in new[] { 30, 60, 120 })
                foreach (bool running in new[] { false, true }) cases.Add(height, hz, running);
        return cases;
    }

    [Theory]
    [MemberData(nameof(Ledges))]
    public void JumpAndClimbReturnsControlAcrossLedgeHeights(int height, int hz, bool running)
    {
        var collision = Floor();
        collision.AddBox(V(-500, 100, 0), V(500, 1200, height));
        var ledges = Doc(Ge(0, GEKind.LG, V(-500, 100, height), V(500, 100, height)));
        PawnRig rig = PawnRig.Create(PawnTeam.Spy, collision.Build(), ledges, ContentOptions());
        rig.Place(V(0, running ? -180 : 63, 89.5f), new FRotator(0, 16384, 0), new FRotator(0, 16384, 0), false);
        rig.SetFastSpeed(true);
        Assert.True(rig.WaitStill(120));
        var seen = new List<string>();
        for (int tick = 0; tick < hz * 6; tick++)
        {
            bool forward = running && tick < hz * 2 || rig.Controller.IsInState(MovementStateNames.OnLedge);
            rig.Step(new PlayerInputFrame { Jump = tick == (running ? hz / 3 : 0), BT_Up = forward ? (byte)1 : (byte)0 }, 1f / hz);
            string state = rig.Controller.GetStateName().ToString();
            if (seen.Count == 0 || seen[^1] != state) seen.Add(state);
        }
        Assert.True(rig.Pawn.Physics == EPhysics.PHYS_Walking,
            $"height={height}; running={running}; hz={hz}; {rig.Pawn.Location}; {rig.Pawn.Physics}; {string.Join(", ", seen)}");
        Assert.StartsWith("s_PlayerWalking", rig.Controller.GetStateName().ToString());
    }
}
