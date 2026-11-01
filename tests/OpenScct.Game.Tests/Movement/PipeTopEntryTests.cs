using OpenScct.Content;
using OpenScct.Core;
using OpenScct.Game.Movement;
using Xunit;
using static OpenScct.Game.Tests.Movement.MovementTestKit;

namespace OpenScct.Game.Tests.Movement;

public sealed class PipeTopEntryTests
{
    [Theory]
    [InlineData(2640f, 1320f, true)]
    [InlineData(6600f, 3300f, false)]
    [InlineData(2640f, 5000f, true)]
    public void PipeTopRequiresSlowInputRatherThanLowWorldAcceleration(float stick, float acceleration, bool enters)
    {
        var level = Floor();
        level.AddBox(V(-500f, 0f, 0f), V(500f, 400f, 300f));
        var pipe = new GERecord(0, "GE0", GEKind.PIPE,
            [V(0f, -10f, 0f), V(0f, -10f, 300f), V(0f, -20f, 0f)], 0, GEPlatform.Common, GESource.Synthetic());
        PawnRig rig = PawnRig.Create(PawnTeam.Spy, level.Build(), new TraversalDoc("1.0", "test", [pipe]));
        var facing = new FRotator(0, -16384, 0);
        rig.Place(V(0f, 40f, 389.5f), facing, facing, crouched: false);
        rig.WaitStill(120);
        rig.Controller.aForward = stick;
        rig.Pawn.Acceleration = V(0f, -acceleration, 0f);
        rig.Driver.CheckFeetZone(rig.Pawn);
        Assert.Equal(enters, rig.Controller.IsInState(MovementStateNames.s_WaitAtTopPipe));
    }
}
