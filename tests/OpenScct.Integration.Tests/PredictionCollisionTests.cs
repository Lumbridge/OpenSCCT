using OpenScct.Content.Collision;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Devices;
using OpenScct.Game.Movement;
using OpenScct.Physics.Collision;
using OpenScct.Server;
using Xunit;

namespace OpenScct.Integration.Tests;

public sealed class PredictionCollisionTests
{
    private sealed class BlockingPawn : Pawn
    {
        public int Bumps;
        protected override void Bump(Actor other) => Bumps++;
    }

    private static CollisionDataBuilder Floor()
    {
        var data = new CollisionDataBuilder();
        data.AddBox(new FVector(-4000, -4000, -100), new FVector(4000, 4000, 0));
        return data;
    }

    [Fact]
    public void ReplayUsesTheObservedPawnCylindersAndRestoresTheirLivePoseWithoutBumpEffects()
    {
        PawnRig rig = PawnRig.Create(PawnTeam.Spy, Floor().Build());
        rig.Place(new FVector(0, 0, 89.005f), default, default, false);
        var other = rig.Level.SpawnActor(new BlockingPawn
        {
            Physics = EPhysics.PHYS_None, CollisionRadius = 35, CollisionHeight = 87.5f,
            bCollideActors = true, bBlockActors = true, bBlockPlayers = true,
        }, new FVector(85, 0, 89), default)!;
        rig.Collision!.NotifyCollisionChanged();
        var moves = new SBasePawnMoves(rig.Controller) { ModernCommands = true };
        FVector start = rig.Pawn.Location;
        rig.Pawn.GroundSpeed = 423.5f;
        rig.Controller.SetTimer(4f, true); rig.Pawn.SetTimer(2f, false);
        moves.CapturePrediction(0);
        var acceleration = new FVector(2048, 0, 0);
        for (int i = 1; i <= 16; i++)
        {
            using var time = rig.Level.CommandTime(i, i * Sim.DeltaTime, Sim.DeltaTime);
            if (i == 9) other.Location = new FVector(100, 600, 89);
            if (i == 12) rig.Pawn.GroundSpeed = 169.4f;
            rig.Controller.TimerCounter = i * Sim.DeltaTime;
            rig.Pawn.TimerCounter = i * Sim.DeltaTime * 0.5f;
            rig.Controller.LatentFloat = i;
            rig.Pawn.LatentFloat = i * 2;
            moves.MoveAutonomous(Sim.DeltaTime, acceleration, false);
            rig.Pawn.AdvancePredictionAnimation(Sim.DeltaTime);
            rig.Pawn.UpdateMovementAnimation(Sim.DeltaTime);
            moves.CapturePrediction(i);
        }
        FVector expected = rig.Pawn.Location;
        float expectedMoveTimer = rig.Controller.MoveTimer;
        Assert.True(other.Bumps > 0);
        int bumps = other.Bumps;
        // This newly received pose would block the old inputs if replay queried the live collision world.
        FVector live = other.Location = new FVector(80, 0, 89);
        other.CollisionRadius = 50; other.CollisionHeight = 50;
        rig.Pawn.Location += new FVector(0, 12, 0);
        FVector before = rig.Pawn.Location;
        moves.BeginPredictionReplay();
        try
        {
            moves.RestorePrediction(0, start, default, EPhysics.PHYS_Walking, rig.Level.Info, 0);
            for (int i = 1; i <= 16; i++) moves.ReplayPrediction(i, Sim.DeltaTime, acceleration, false);
            Assert.InRange((expected - rig.Pawn.Location).Size(), 0, 0.02f);
            Assert.Equal(expectedMoveTimer, rig.Controller.MoveTimer, 4);
            Assert.Equal(16 * Sim.DeltaTime, rig.Controller.TimerCounter);
            Assert.Equal(8 * Sim.DeltaTime, rig.Pawn.TimerCounter);
            Assert.Equal(4f, rig.Controller.TimerRate); Assert.True(rig.Controller.bTimerLoop);
            Assert.Equal(2f, rig.Pawn.TimerRate); Assert.False(rig.Pawn.bTimerLoop);
            Assert.Equal(16, rig.Controller.LatentFloat); Assert.Equal(32, rig.Pawn.LatentFloat);
        }
        finally { moves.EndPredictionReplay(before); }
        Assert.Equal(live, other.Location);
        Assert.Equal(50, other.CollisionRadius);
        Assert.Equal(50, other.CollisionHeight);
        Assert.Equal(bumps, other.Bumps);
        Assert.False(rig.World.IsReplaying);
        void Replay()
        {
            moves.BeginPredictionReplay();
            try
            {
                moves.RestorePrediction(0, start, default, EPhysics.PHYS_Walking, rig.Level.Info, 0);
                for (int i = 1; i <= 16; i++) moves.ReplayPrediction(i, Sim.DeltaTime, acceleration, false);
            }
            finally { moves.EndPredictionReplay(expected); }
        }
        for (int i = 0; i < 60; i++) Replay();
        long allocated = System.GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 60; i++) Replay();
        Assert.Equal(0, System.GC.GetAllocatedBytesForCurrentThread() - allocated);
    }

    [Fact]
    public void ReplayCarriesTheOwnerThroughTranslatedAndRotatedMovingBaseHistory()
    {
        CollisionDataBuilder data = Floor();
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
        FVector start = rig.Pawn.Location;
        moves.CapturePrediction(0);
        for (int i = 1; i <= 16; i++)
        {
            using var time = rig.Level.CommandTime(i, i * Sim.DeltaTime, Sim.DeltaTime);
            Assert.True(natives.MoveEncroacher(basis, new FVector(3, 0, 1), new FRotator(0, i * 256, 0)));
            moves.MoveAutonomous(Sim.DeltaTime, default, false);
            rig.Pawn.AdvancePredictionAnimation(Sim.DeltaTime);
            rig.Pawn.UpdateMovementAnimation(Sim.DeltaTime);
            moves.CapturePrediction(i);
        }
        FVector expected = rig.Pawn.Location;
        FVector liveBase = basis.Location;
        FRotator liveRotation = basis.Rotation;
        moves.BeginPredictionReplay();
        try
        {
            moves.RestorePrediction(0, start, default, EPhysics.PHYS_Walking, basis, 0);
            for (int i = 1; i <= 16; i++) moves.ReplayPrediction(i, Sim.DeltaTime, default, false);
            Assert.InRange((expected - rig.Pawn.Location).Size(), 0, 0.02f);
        }
        finally { moves.EndPredictionReplay(expected); }
        Assert.Equal(liveBase, basis.Location);
        Assert.Equal(liveRotation, basis.Rotation);
        Assert.Same(basis, rig.Pawn.Base);
        // A correction is expressed in the server base's pose, then reconstructed in the recorded input pose.
        FVector corrected = liveBase + new FVector(0, 30, start.Z);
        FVector velocity = new(0, 10, 0);
        moves.RebaseAuthoritativeState(0, basis, liveBase, new FRotator(0, 16384, 0), ref corrected, ref velocity);
        Assert.InRange((corrected - start).Size(), 0, 0.02f);
        Assert.InRange((velocity - new FVector(10, 0, 0)).Size(), 0, 0.02f);
    }

    [Fact]
    public void CollisionHistoryExhaustionIsExplicitAndLiveActorsSurviveAnInterruptedReplay()
    {
        PawnRig rig = PawnRig.Create(PawnTeam.Spy, Floor().Build());
        rig.Place(new FVector(0, 0, 89.005f), default, default, false);
        var other = rig.Level.SpawnActor(new BlockingPawn(), new FVector(1000, 1000, 89), default)!;
        var moves = new SBasePawnMoves(rig.Controller) { ModernCommands = true };
        moves.CapturePrediction(0);
        other.Location = new FVector(2000, 2000, 89);
        moves.BeginPredictionReplay();
        try
        {
            moves.RestorePrediction(0, rig.Pawn.Location, default, EPhysics.PHYS_Walking, rig.Level.Info, 0);
            Assert.Equal(new FVector(1000, 1000, 89), other.Location);
            // A failed replay still runs this same finally path in ClientMoves.
        }
        finally { moves.EndPredictionReplay(rig.Pawn.Location); }
        Assert.Equal(new FVector(2000, 2000, 89), other.Location);
        for (int i = 0; i < 128; i++)
            rig.Level.SpawnActor(new BlockingPawn(), new FVector(3000, i * 100, 89), default);
        moves.CapturePrediction(1);
        Assert.False(moves.PredictionHistoryComplete(1));
        Assert.True(moves.PredictionHistoryComplete(0));
    }
}
