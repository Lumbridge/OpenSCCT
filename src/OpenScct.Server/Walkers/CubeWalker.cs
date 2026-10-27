using System;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Provenance;

namespace OpenScct.Server;

/// <summary>
/// M0 test pawn: a box that walks on a flat plane under player input, with no collision. The server moves it
/// only when the owning client's moves arrive (UE2 style: a remotely controlled pawn has no physics of its own
/// on the server; ServerMove runs it), so server and client agree on which input produced which step. Its
/// physics mode is none, so a map's physics driver leaves it alone. Replicated with the generated Pawn layout
/// plus <see cref="CubeWalkerNet"/>.
/// </summary>
public sealed class CubeWalker : Pawn
{
    /// <summary>Ticks of missing moves the server fills in with the next move it gets (lost-move catch up).</summary>
    public const int MaxCatchUpTicks = 15;

    [Provenance(ProvenanceKind.Estimated, "Stock UE2 PhysicsVolume GroundFriction default")]
    public const float GroundFriction = 8f;

    public CubeWalker()
    {
        GroundSpeed = 440f;
        AccelRate = 2048f;
        Physics = EPhysics.PHYS_None;
        RemoteRole = ENetRole.ROLE_SimulatedProxy;
        bAlwaysRelevant = true;
        CollisionRadius = 32f;
        CollisionHeight = 32f;
    }

    /// <summary>A pawn is its own Instigator (Engine.Pawn.PreBeginPlay), which the autonomous downgrade relies on.</summary>
    protected override void PreBeginPlay()
    {
        base.PreBeginPlay();
        Instigator = this;
    }

    /// <summary>Newest move timestamp processed (UE2 PlayerController.CurrentTimeStamp). 0 before the first.</summary>
    public uint CurrentTimeStamp { get; private set; }

    /// <summary>Moves applied, counting filled-in ticks.</summary>
    public long MovesApplied { get; private set; }

    /// <summary>Moves that arrived again after they had been applied (the resend copies).</summary>
    public long MovesRepeated { get; private set; }

    /// <summary>Player id of the owner, for hosts and views.</summary>
    public int PlayerId { get; set; }

    /// <summary>
    /// Server: applies the client's moves, oldest first. Moves already applied are skipped; a gap (every copy of
    /// some moves lost) is filled by repeating the next move for the missing ticks, up to
    /// <see cref="MaxCatchUpTicks"/>, as UE2 lets one move cover the time since the previous one.
    /// </summary>
    public void ServerMove(ReadOnlySpan<CubeMove> moves)
    {
        for (int i = 0; i < moves.Length; i++)
        {
            CubeMove move = moves[i];
            if (CurrentTimeStamp != 0 && move.TimeStamp <= CurrentTimeStamp)
            {
                MovesRepeated++;
                continue;
            }

            uint gap = CurrentTimeStamp == 0 ? 1u : move.TimeStamp - CurrentTimeStamp;
            int steps = gap > MaxCatchUpTicks ? MaxCatchUpTicks : (int)gap;
            for (int s = 0; s < steps; s++)
            {
                MoveAutonomous(in move, Sim.DeltaTime);
            }

            CurrentTimeStamp = move.TimeStamp;
        }
    }

    /// <summary>One step of walking: face the view yaw, accelerate along the input, move.</summary>
    [Provenance(ProvenanceKind.Estimated, "M0 stand-in for walking physics: no collision, simplified velocity update")]
    public void MoveAutonomous(in CubeMove move, float DeltaTime)
    {
        Rotation = new FRotator(0, move.ViewYaw, 0);
        float cosYaw = SvmMath.Cos(move.ViewYaw);
        float sinYaw = SvmMath.Sin(move.ViewYaw);
        var forward = new FVector(cosYaw, sinYaw, 0f);
        var right = new FVector(-sinYaw, cosYaw, 0f);
        FVector input = forward * (move.Forward / 127f) + right * (move.Strafe / 127f);
        float inputSize = input.Size();
        if (inputSize > 1f)
        {
            input *= 1f / inputSize;
        }

        Acceleration = input * AccelRate;
        UpdateVelocity(DeltaTime);
        Location += Velocity * DeltaTime;
        MovesApplied++;
    }

    private void UpdateVelocity(float DeltaTime)
    {
        float friction = SvmMath.Min(1f, GroundFriction * DeltaTime);
        if (Acceleration.IsZero())
        {
            // Brake to a stop.
            Velocity *= SvmMath.Max(0f, 1f - 2f * friction);
            return;
        }

        // Turn the velocity towards the input direction, then accelerate, capped at GroundSpeed.
        float speed = Velocity.Size();
        FVector direction = Acceleration.SafeNormal();
        Velocity -= (Velocity - direction * speed) * friction;
        Velocity += Acceleration * DeltaTime;
        if (Velocity.SizeSquared() > GroundSpeed * GroundSpeed)
        {
            Velocity = Velocity.SafeNormal() * GroundSpeed;
        }
    }
}
