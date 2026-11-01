using OpenScct.Content.Collision;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Devices;
using OpenScct.Game.Movement;
using OpenScct.Physics.Collision;
using OpenScct.Server;
using Xunit;

namespace OpenScct.Integration.Tests;

public sealed class MoverPredictionOrderTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ReplayPreservesRiderWhenMoverTicksBeforeOrAfterController(bool moverAfterController, bool walking)
    {
        var data = new CollisionDataBuilder();
        data.AddBox(new FVector(-4000, -4000, -100), new FVector(4000, 4000, 0));
        data.AddBoxHullCollider(ColliderKind.Mover, "Platform", CollisionDataBuilder.WorldFlags,
            new FVector(-300, -300, 0), new FVector(300, 300, 200));
        PawnRig rig = PawnRig.Create(PawnTeam.Spy, data.Build());
        var basis = rig.Level.SpawnActor(new Mover { bNoDelete = false }, default, default)!;
        basis.Physics = EPhysics.PHYS_None;
        basis.bCollideActors = basis.bBlockActors = basis.bBlockPlayers = true;
        rig.Collision!.BindLevel(rig.Level, name => name == "Platform" ? basis : null);
        rig.Place(new FVector(30, 0, 289.005f), default, default, false);
        rig.Pawn.SetBase(basis, bNotifyActor: false);
        var moves = new SBasePawnMoves(rig.Controller) { ModernCommands = true };
        var natives = Assert.IsType<DeviceNatives>(rig.Level.GetService<IActorNatives>());
        FVector acceleration = walking ? new FVector(1200, 0, 0) : default;
        FVector start = rig.Pawn.Location;
        moves.CapturePrediction(0);
        for (int i = 1; i <= 16; i++)
        {
            using var time = rig.Level.CommandTime(i, i * Sim.DeltaTime, Sim.DeltaTime);
            if (!moverAfterController) Carry();
            moves.MoveAutonomous(Sim.DeltaTime, acceleration, false);
            rig.Pawn.AdvancePredictionAnimation(Sim.DeltaTime);
            rig.Pawn.UpdateMovementAnimation(Sim.DeltaTime);
            // RealReplicateMove stores collision inputs during the controller's tick.
            moves.CapturePrediction(i);
            if (moverAfterController) Carry();
            // FinishPredictionTick refreshes the completed pawn state after all actors tick.
            moves.CapturePrediction(i);

            void Carry() => Assert.True(natives.MoveEncroacher(basis, new FVector(3, 0, 2), new FRotator(0, i * 256, 0)));
        }

        FVector expected = rig.Pawn.Location;
        FRotator expectedRotation = rig.Pawn.Rotation;
        FVector liveBasePosition = basis.Location;
        FRotator liveBaseRotation = basis.Rotation;
        for (int replay = 0; replay < 2; replay++)
        {
            moves.BeginPredictionReplay();
            try
            {
                moves.RestorePrediction(0, start, default, EPhysics.PHYS_Walking, basis, 0);
                for (int i = 1; i <= 16; i++)
                {
                    moves.ReplayPrediction(i, Sim.DeltaTime, acceleration, false);
                    // Corrections replace pending saved states for subsequent acknowledgements.
                    moves.CapturePrediction(i);
                }
            }
            finally { moves.EndPredictionReplay(expected); }
            Assert.InRange((expected - rig.Pawn.Location).Size(), 0, 0.02f);
            Assert.Equal(expectedRotation, rig.Pawn.Rotation);
            Assert.Same(basis, rig.Pawn.Base);
            Assert.Equal(liveBasePosition, basis.Location);
            Assert.Equal(liveBaseRotation, basis.Rotation);
        }
    }
}
