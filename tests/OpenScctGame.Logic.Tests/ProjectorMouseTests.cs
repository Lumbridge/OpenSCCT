using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using OpenScctGame.Play.Session;
using Xunit;

namespace OpenScctGame.Logic.Tests;

public sealed class ProjectorMouseTests
{
    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 1)]
    [InlineData(-1, 2)]
    public void SlowMouseDiagonalsTurnBothProjectorAxes(int x, int y)
    {
        var world = new World(1);
        var pawn = world.Level.Spawn<SPAWNDEFENSE>()!;
        var pc = world.Level.Spawn<SPlayerController>()!;
        var projector = world.Level.SpawnActor(new SProjoFixe { bNoDelete = false }, default, default)!;
        pc.Pawn = pc.SBPawn = pawn;
        pc.SPawnType = PawnType.SPAWNDEFENSE;
        pawn.UsedObject = projector;
        pc.GotoState("s_UseObject_Mirador");
        pc.DesiredFOV = 110;
        projector.PitchConstraint = projector.YawConstraint = new FVector(0, -14000, 14000);
        var mouse = new MouseLook { MercLook = 3f, MercCam = 2f };
        mouse.Apply(pc, x, y);
        Assert.Equal(MouseLook.CameraSensibilite, pc.fSensibiliteSouris);
        bool lastUse = false;
        ControlInput.Compute(pc, Sim.DeltaTime, ref lastUse);
        pc.eventUpdateRotation(Sim.DeltaTime, 0f);
        Assert.InRange(FRotator.NormalizeAxis(projector.iYaw), x * 6 - 1, x * 6 + 1);
        Assert.InRange(FRotator.NormalizeAxis(projector.iPitch), y * 6 - 1, y * 6 + 1);
    }

    [Fact]
    public void ControllerCameraInputRetainsItsDeadZone()
    {
        var world = new World(1);
        var pawn = world.Level.Spawn<SPAWNDEFENSE>()!;
        var pc = world.Level.Spawn<SPlayerController>()!;
        var projector = world.Level.SpawnActor(new SProjoFixe { bNoDelete = false }, default, default)!;
        pc.Pawn = pc.SBPawn = pawn;
        pawn.UsedObject = projector;
        pc.GotoState("s_UseObject_Mirador");
        var mouse = new MouseLook { Pad = true };
        mouse.Apply(pc, 1, 2);
        bool lastUse = false;
        ControlInput.Compute(pc, Sim.DeltaTime, ref lastUse);
        pc.eventUpdateRotation(Sim.DeltaTime, 0f);
        Assert.Equal(0, projector.iYaw);
        Assert.Equal(0, projector.iPitch);
    }
}
