using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Provenance;

namespace OpenScct.Game.Movement;

/// <summary>
/// The movement calls a client sends to the server (state-and-network.md, "Traversal data to the server" and the
/// action stream). They are only made where the pawn's Role is below Authority, so a standalone game or a listen
/// host never makes them; the network layer implements this as level service and delivers each call to the
/// server's copy of the controller, which then runs the matching Server* function.
/// </summary>
public interface IMovementNet
{
    /// <summary>ServerGetZipP(P1, P2), reliable.</summary>
    void ServerGetZipP(SPlayerController PC, FVector P1, FVector P2);

    /// <summary>ServerGetNewZipP(P1, P2), reliable.</summary>
    void ServerGetNewZipP(SPlayerController PC, FVector P1, FVector P2);

    /// <summary>ServerGetZipPAndNormal(P1, P2, N x 1000), reliable.</summary>
    void ServerGetZipPAndNormal(SPlayerController PC, FVector P1, FVector P2, FVector NormalX1000);

    /// <summary>ServerSetInfoForVandamme(vVandammePos, dir x 1000), reliable.</summary>
    void ServerSetInfoForVandamme(SPlayerController PC, FVector Position, FVector DirX1000);

    /// <summary>ServerSetWallJumpDir(dir x 1000), reliable.</summary>
    void ServerSetWallJumpDir(SPlayerController PC, FVector DirX1000);

    /// <summary>UpdateDesiredRotation(Rot), unreliable.</summary>
    void UpdateDesiredRotation(SPlayerController PC, FRotator Rot);

    /// <summary>ReplicateActionToServer / ReplicateActionToServerWithVelocity with the queued actions, reliable.</summary>
    void ReplicateActionToServer(SPlayerController PC, int[] Actions, int Count, bool bWithVelocity);

    /// <summary>ServerPlayLandingSound, unreliable.</summary>
    void ServerPlayLandingSound(SPlayerController PC);

    /// <summary>
    /// MyReplicateMove (native): the client saves the move, moves its own pawn with it (autonomous physics) and
    /// sends it to the server.
    /// </summary>
    void MyReplicateMove(SPlayerController PC, float DeltaTime, FVector NewAccel);

    /// <summary>MyReplicateMoveWithoutMoving (native): the client flushes the move so far at a physics event.</summary>
    void MyReplicateMoveWithoutMoving(SPlayerController PC, float LeftTime, FVector NewAccel);

    /// <summary>
    /// SetMateTarget(tgt), reliable: the server runs <see cref="SPlayerController.SetMateTarget"/> on its copy of the
    /// controller. Without a network layer the call is dropped.
    /// </summary>
    void SetMateTarget(SPlayerController PC, SBasePawn? Target)
    {
    }

    /// <summary>
    /// AskServerToStartCoopInteraction(), reliable: the server runs
    /// <see cref="SPlayerController.AskServerToStartCoopInteraction"/> on its copy. Without a network layer it is dropped.
    /// </summary>
    void AskServerToStartCoopInteraction(SPlayerController PC)
    {
    }

    /// <summary>
    /// UpdatePositionForServer(NewLocation, NewRotation), unreliable: the server runs
    /// <see cref="SPlayerController.UpdatePositionForServer"/> on its copy. Without a network layer it is dropped.
    /// </summary>
    void UpdatePositionForServer(SPlayerController PC, FVector NewLocation, FRotator NewRotation)
    {
    }
}

/// <summary>The level's <see cref="IMovementNet"/>, and the RPC vector rounding.</summary>
public static class MovementNet
{
    /// <summary>The level's network calls, or <see cref="MovementNetLocal"/>.</summary>
    public static IMovementNet Of(Actor actor) => actor.XLevel?.GetService<IMovementNet>() ?? MovementNetLocal.Instance;

    /// <summary>A vector RPC parameter: scaled, then each component rounded to the nearest integer.</summary>
    [Provenance(ProvenanceKind.Decoded, "state-and-network.md: vector RPC parameters are sent as rounded integers (x 1000 unit vectors, x 100 velocities)")]
    public static FVector Scaled(FVector v, float scale) =>
        new(SvmMath.Round(v.X * scale), SvmMath.Round(v.Y * scale), SvmMath.Round(v.Z * scale));
}

/// <summary>No network: the calls are dropped (they are never made on the server's own machine).</summary>
public sealed class MovementNetLocal : IMovementNet
{
    public static readonly MovementNetLocal Instance = new();

    private MovementNetLocal()
    {
    }

    public void ServerGetZipP(SPlayerController PC, FVector P1, FVector P2)
    {
    }

    public void ServerGetNewZipP(SPlayerController PC, FVector P1, FVector P2)
    {
    }

    public void ServerGetZipPAndNormal(SPlayerController PC, FVector P1, FVector P2, FVector NormalX1000)
    {
    }

    public void ServerSetInfoForVandamme(SPlayerController PC, FVector Position, FVector DirX1000)
    {
    }

    public void ServerSetWallJumpDir(SPlayerController PC, FVector DirX1000)
    {
    }

    public void UpdateDesiredRotation(SPlayerController PC, FRotator Rot)
    {
    }

    public void ReplicateActionToServer(SPlayerController PC, int[] Actions, int Count, bool bWithVelocity)
    {
    }

    public void ServerPlayLandingSound(SPlayerController PC)
    {
    }

    /// <summary>Without a network layer the move is applied here, as the server's own player's would be.</summary>
    public void MyReplicateMove(SPlayerController PC, float DeltaTime, FVector NewAccel) => PC.eventMyProcessMove(DeltaTime, NewAccel);

    public void MyReplicateMoveWithoutMoving(SPlayerController PC, float LeftTime, FVector NewAccel)
    {
    }
}
