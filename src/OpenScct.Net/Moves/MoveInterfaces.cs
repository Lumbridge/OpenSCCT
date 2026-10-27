using System;
using OpenScct.Core;
using OpenScct.Core.Engine;

namespace OpenScct.Net.Moves;

/// <summary>
/// What the move protocol reads and drives on the player's pawn (SBasePawn). The pawn movement port implements it;
/// the protocol never moves the pawn itself, it asks through <see cref="MoveAutonomous"/>, <see cref="MovePawn"/>
/// and <see cref="SnapTo"/>.
/// </summary>
public interface IMovePawn
{
    FVector Location { get; }

    FVector Velocity { get; set; }

    /// <summary>The pawn's rotation (the server sets it from View, net-moves.md rule 11.8).</summary>
    FRotator Rotation { get; set; }

    EPhysics Physics { get; }

    /// <summary>Restores a physics mode (big moves undo an action's switch to root motion, rule 13).</summary>
    void SetPhysics(EPhysics physics);

    Actor? Base { get; }

    /// <summary>Health above zero.</summary>
    bool IsAlive { get; }

    /// <summary>The collision configuration (0 standing, 1 crouched, 2 zip, 3 pipe).</summary>
    byte ZoneConfig { get; }

    /// <summary>The height offset between two zone configurations (<c>ZCDecal[from × 4 + to]</c>).</summary>
    float ZCDecal(int from, int to);

    /// <summary>SPawnType == 1.</summary>
    bool IsMerc { get; }

    float RunningSpeed { get; }

    /// <summary>The server's per-move speed cap goes here (rule 11.6).</summary>
    float GroundSpeed { get; set; }

    bool bIsRolling { get; }

    bool bIsCharging { get; }

    /// <summary>A spy's adrenaline is active (<c>AdrenalineTime + fTimeWhenHit &gt;= Level.TimeSeconds</c>).</summary>
    bool AdrenalineActive { get; }

    float AdrenalineFactor { get; }

    /// <summary>Vandamme, one of the four BTW fire values, or PipH4/PipH2 fire: the PCRot message (rule 3.5).</summary>
    bool UsesPCRot { get; }

    bool bTestBackFeetGrab { get; }

    /// <summary>Actions wait for a big move: <c>bActionsSendByServerMove &amp;&amp; iNbAction &gt; 0</c>.</summary>
    bool ActionsWaitForBigMove { get; }

    /// <summary>
    /// Takes the queued actions for a big move (<c>AllActions</c>, <c>iNbActionInTable</c>), then marks them sent
    /// (<c>bActionsHasBeenSentByServerMove = true</c>, <c>iNbAction = 0</c>, <c>iNbActionsSent</c>).
    /// </summary>
    int TakeActionsForBigMove(Span<int> actions);

    /// <summary>NativeExecuteReceivedActionForServer: runs a received action pair on the server.</summary>
    void ExecuteReceivedActions(int val0, int val1);

    /// <summary>The pawn's iYawPitchFromView, set by every server move (PackW(Yaw, Pitch)).</summary>
    int iYawPitchFromView { get; set; }

    /// <summary>
    /// The pawn's bUseRootMotionAdditionnalPhysics: a root motion move with it set is simulated by the server move
    /// (ProcessServerMove reads the pawn's flag, Pawn + 0x2f4 bit 0x1000 in game 0x10aac4a0).
    /// </summary>
    bool bUseRootMotionAdditionnalPhysics => false;

    FVector ZipP1 { get; set; }

    FVector ZipP2 { get; set; }

    FVector NewZipP1 { get; set; }

    FVector NewZipP2 { get; set; }

    FVector vVandammePos { get; set; }

    FVector vWallJumpDir { get; set; }

    FVector m_geoNormal { get; set; }

    bool bKeepLocalVandammePos { get; }

    /// <summary>
    /// MoveAutonomous: ScriptMyProcessMove (the pawn's acceleration), the acceleration truncated toward zero to
    /// whole units, then the pawn's physics for <paramref name="deltaTime"/>.
    /// </summary>
    void MoveAutonomous(float deltaTime, FVector acceleration, bool jump);

    /// <summary>MovePawn (native 1402): a swept move by <paramref name="delta"/>; teleport when blocked more than 50 uu short.</summary>
    void MovePawn(FVector delta);

    /// <summary>
    /// The big move snap (rule 13): a sweep to <paramref name="location"/>, retried horizontally when blocked, then
    /// once more with the full offset unless the pawn is falling after a mover or stands on a moving mover.
    /// </summary>
    void SnapTo(FVector location);

    /// <summary>ReinitBecauseOfDivergences (native 1670): resets the pawn's action and movement state (rule 17).</summary>
    void ReinitBecauseOfDivergences();

    /// <summary>The pawn's base is a mover that moved vertically within <paramref name="seconds"/> (fLastTimeMoverMoves).</summary>
    bool BaseMoverMovedVertically(float seconds);

    /// <summary>The pawn's base is a mover with a non-zero vertical velocity.</summary>
    bool BaseMoverMovingVertically { get; }

    /// <summary>Sets location, base and rotation (ClientReinitPawnBecauseOfDivergences).</summary>
    void Place(FVector location, Actor? newBase, FRotator rotation);
}

/// <summary>What the move protocol reads and drives on the player controller (SPlayerController).</summary>
public interface IMoveController
{
    /// <summary>The controller's rotation (aim; View carries its pitch and, for PCRot, its yaw).</summary>
    FRotator Rotation { get; set; }

    bool bPressedJump { get; }

    /// <summary><c>Level.TimeSeconds</c>.</summary>
    float TimeSeconds { get; }

    /// <summary>The level is paused (no move is simulated).</summary>
    bool IsPaused { get; }

    /// <summary>The epoch of the mirrored state (MirrorStateAcknowledgementTime).</summary>
    float MirrorStateAcknowledgementTime { get; set; }

    bool bWaitingForReinit { get; set; }

    /// <summary>The controller's bUseRootMotionAdditionnalPhysics (read by big moves on the server).</summary>
    bool bUseRootMotionAdditionnalPhysics { get; }

    /// <summary>
    /// Server: the event ReinitPawnBecauseOfDivergences (state-sync.md rule 12). The controller port decides the
    /// case (dead, grabbed...) and, for the normal case, calls <see cref="ServerMoves.ReinitPawn"/>.
    /// </summary>
    void ReinitPawnBecauseOfDivergences();

    /// <summary>
    /// Client: after ClientReinitPawnBecauseOfDivergences placed the pawn, the controller's own reset (FOV,
    /// camera, weapon) and its <c>s_ClientIsReinit</c> state, which sends TellServerIAMReadyToReinit every second.
    /// </summary>
    void ClientReinitDone(byte reinitType);

    /// <summary>Server: TellServerIAMReadyToReinit arrived (the controller's <c>s_WaitForClientToReinit</c> answers it).</summary>
    void ServerReinitReady();
}

/// <summary>
/// A controller adapter that shares PlayerController.ServerTimeStamp with the server moves (the engine tick's
/// remote autonomous catch-up writes it too, spec/native/net-replication.md rule 26).
/// </summary>
public interface IMoveControllerTimeStamp
{
    float ServerTimeStamp { get; set; }
}

/// <summary>
/// A controller adapter that shares PlayerController.CurrentTimeStamp with the server moves: the pawn's own server
/// tick (ASBasePawn::Tick) advances it when it catches a falling pawn up, so the next move does not simulate that
/// time again (spec/native/net-moves.md rule 18).
/// </summary>
public interface IMoveControllerCurrentTimeStamp
{
    float CurrentTimeStamp { get; set; }
}

/// <summary>The move protocol's messages to the other side (replicated functions of SPlayerController).</summary>
public interface IMoveTransport
{
    int OutstandingReliable { get; }

    void MyServerMove(float TimeStamp, FVector InAccel, FVector ClientLoc, int View, byte DiversInfos, int OldTimeDelta, int OldAccel);

    void MyShortServerMove(float TimeStamp, FVector ClientLoc, int View, byte DiversInfos, int OldTimeDelta, int OldAccel);

    void MyServerMoveWithPCRot(float TimeStamp, FVector ClientLoc, int View, byte DiversInfos, int OldTimeDelta, int OldAccel);

    void BigServerMove(float MSATime, float TimeStamp, FVector InAccel, FVector ClientLoc, FVector Vel, int View, byte DiversInfos,
        int Val0, int Val1);

    void UnreliableReplicateBigServerMove(in BigServerMoveData move);

    void MyShortClientAdjustPosition(float TimeStamp, float NewLocX, float NewLocY, float NewLocZ, Actor? NewBase);

    void MyClientAdjustPosition(float TimeStamp, float NewLocX, float NewLocY, float NewLocZ, float NewVelX, float NewVelY, float NewVelZ,
        Actor? NewBase);

    void ClientReinitPawnBecauseOfDivergences(byte RT, float MSATime, FVector vPos, FRotator rRot, Actor? aBase);

    void TellServerIAMReadyToReinit();
}

/// <summary>SBase.SPlayerController.BigServerMoveStruct (BSMS): the last big move, kept for its resends.</summary>
public struct BigServerMoveData
{
    public float MSATime;
    public float TimeStamp;
    public int View;
    public int Val0;
    public int Val1;
    public int iDiversInfos;
    public FVector InAccel;
    public FVector ClientLoc;
    public FVector Vel;
    public FVector ZipP1;
    public FVector DifZipP2;
    public FVector vect3;
    public FVector vectUnit;
}

/// <summary>ReinitType_ of SPlayerController.</summary>
public enum ReinitType : byte
{
    RT_Normal = 0,
    RT_KO = 1,
    RT_Dead = 2,
    RT_Grabbed = 3,
    RT_Grabbing = 4,
}
