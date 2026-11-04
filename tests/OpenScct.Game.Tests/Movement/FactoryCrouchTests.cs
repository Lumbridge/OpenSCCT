using System.IO;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Movement;
using Xunit;
using static OpenScct.Game.Tests.Movement.MovementTestKit;

namespace OpenScct.Game.Tests.Movement;

public sealed class FactoryCrouchTests
{
    [Theory]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(120)]
    public void CrouchAtReportedLocationSettlesWithoutCameraHeightOscillation(int hz)
    {
        string folder = Path.Combine(RepoRoot, ".content", "svmap", "FactE");
        Assert.SkipWhen(!Directory.Exists(folder), "FactE has not been converted into .content/svmap.");
        PawnRig rig = PawnRig.ForMap(PawnTeam.Spy, folder);
        rig.Place(V(-329.99603f, -2194.4006f, 217.005f), new FRotator(0, 6023937, 0),
            new FRotator(-2988, -9307, 0), crouched: false);
        rig.SetFastSpeed(true);
        rig.Controller.bCrouchWithRoll = true;
        for (int tick = 0; tick < hz * 3; tick++)
        {
            rig.Step(default, 1f / hz);
            if (tick < hz) continue;
            Assert.True(rig.Pawn.bIsCrouched, $"tick {tick}: {rig.Controller.GetStateName()} at {rig.Pawn.Location}");
            Assert.Equal(rig.Pawn.CrouchHeight, rig.Pawn.CollisionHeight);
            Assert.Equal(EPhysics.PHYS_Walking, rig.Pawn.Physics);
            Assert.True(rig.Controller.IsInState(MovementStateNames.s_PlayerWalkingCr0H));
            Assert.InRange(rig.Pawn.Location.Z, 178f, 181f);
        }
    }
}
