using System;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Provenance;

namespace OpenScct.Net.Moves;

/// <summary>Which server move message is being processed.</summary>
public enum EServerMoveKind : byte
{
    Full,
    Short,
    PCRot,
    Big,
    BigResend,
}

/// <summary>
/// The server's half of the SCCT move protocol for one remote player: accepting moves (time stamps, the old-move
/// pair, the time-margin guard, the physics check, the speed cap, the aim), simulating them through the pawn, the
/// correction decision and the forced reinit, big moves with their actions, snap and traversal payload, and the
/// PCRot form with Reloaded's change. Written from spec/native/net-moves.md rules 11 to 15 and
/// spec/native/net-reloaded.md rule 1.
/// </summary>
public sealed class ServerMoves
{
    private readonly int[] _actions = new int[6];

    public ServerMoves(IMovePawn pawn, IMoveController controller, IMoveTransport transport)
    {
        Pawn = pawn ?? throw new ArgumentNullException(nameof(pawn));
        Controller = controller ?? throw new ArgumentNullException(nameof(controller));
        Transport = transport ?? throw new ArgumentNullException(nameof(transport));
    }

    public IMovePawn Pawn { get; }

    public IMoveController Controller { get; }

    public IMoveTransport Transport { get; }

    /// <summary>The client's net speed (the connection's CurrentNetSpeed), for the correction rate limit.</summary>
    public int ClientNetSpeed { get; set; } = K.ConfiguredInternetSpeed;

    /// <summary>Apply Reloaded's MyServerMoveWithPCRot wrapper (MaxTimeMargin infinite during the call). On by default.</summary>
    public bool ReloadedPCRot { get; set; } = true;

    public bool FixedTickSimulation { get; set; }
    public float? CommandDeltaTime { get; set; }

    // ---- PlayerController fields --------------------------------------------------------------------------------

    /// <summary>
    /// PlayerController.CurrentTimeStamp: the controller's own field when the adapter shares it
    /// (<see cref="IMoveControllerCurrentTimeStamp"/>), as the pawn's server tick advances it (rule 18).
    /// </summary>
    public float CurrentTimeStamp
    {
        get => Controller is IMoveControllerCurrentTimeStamp shared ? shared.CurrentTimeStamp : _currentTimeStamp;
        set
        {
            if (Controller is IMoveControllerCurrentTimeStamp shared)
            {
                shared.CurrentTimeStamp = value;
            }
            else
            {
                _currentTimeStamp = value;
            }
        }
    }

    private float _currentTimeStamp;

    /// <summary>
    /// PlayerController.ServerTimeStamp: the controller's own field when the controller adapter shares it
    /// (<see cref="IMoveControllerTimeStamp"/>), so the engine's stalled-move catch-up and the moves use one value.
    /// </summary>
    public float ServerTimeStamp
    {
        get => Controller is IMoveControllerTimeStamp shared ? shared.ServerTimeStamp : _serverTimeStamp;
        set
        {
            if (Controller is IMoveControllerTimeStamp shared)
            {
                shared.ServerTimeStamp = value;
            }
            else
            {
                _serverTimeStamp = value;
            }
        }
    }

    private float _serverTimeStamp;

    public float LastUpdateTime;
    public float ClientUpdateTime = MoveK.ClientUpdateTimeDefault;
    public float TimeMargin;
    public float MaxTimeMargin = MoveK.MaxTimeMarginLow;
    public float MaxResponseTime = MoveK.MaxResponseTime;

    // ---- SPlayerController fields -------------------------------------------------------------------------------

    public float fLastTimePhysicsChecked;
    public float BigServerMoveLastActionTime;
    public BigServerMoveData BSMS;

    /// <summary>Stamp of the last short move received (server side of MyShortServerMove).</summary>
    public float fLastMovementTime;

    public FVector vOldAccel;

    /// <summary>fLastTimeOnAMover: no writer in the original (dead in practice).</summary>
    public float fLastTimeOnAMover = MoveK.LastTimeOnAMoverDefault;

    /// <summary>Corrections and reinits sent, for tests and diagnostics.</summary>
    public MoveCounters Sent { get; } = new();

    /// <summary>Moves accepted and simulated.</summary>
    public long MovesApplied { get; private set; }

    /// <summary>Moves ignored (stale stamp, waiting for reinit, physics mismatch).</summary>
    public long MovesIgnored { get; private set; }

    // ---- messages -------------------------------------------------------------------------------------------

    /// <summary>MyServerMove (native 1390): a full move with acceleration (× 10 on the wire).</summary>
    [UcNative("ASPlayerController::execMyServerMove", Exe = 0x10aacec0, Editor = 0x111efe80)]
    [PortOf("nat:SBase.SPlayerController.MyServerMove", Spec = "spec/native/net-moves.md#rules")]
    public void MyServerMove(float TimeStamp, FVector InAccel, FVector ClientLoc, int View, byte DiversInfos, int OldTimeDelta, int OldAccel) =>
        ProcessServerMove(EServerMoveKind.Full, TimeStamp, InAccel, ClientLoc, View, DiversInfos, OldTimeDelta, OldAccel, snapped: false);

    /// <summary>MyShortServerMove (script on the server, move-protocol.md rule 11): a move without acceleration.</summary>
    [Uc("SBase.SPlayerController.MyShortServerMove", Sha1 = "2d80d1eba80feb18b3a88df12dbbffc063777e64")]
    [PortOf("uc:SBase.SPlayerController:MyShortServerMove", Spec = "spec/net/move-protocol.md#rules")]
    public void MyShortServerMove(float TimeStamp, FVector ClientLoc, int View, byte DiversInfos, int OldTimeDelta, int OldAccel)
    {
        fLastMovementTime = TimeStamp;
        vOldAccel = default;
        ProcessServerMove(EServerMoveKind.Short, TimeStamp, default, ClientLoc, View, DiversInfos, OldTimeDelta == 0 ? -1 : OldTimeDelta,
            OldAccel, snapped: false);
    }

    /// <summary>
    /// MyServerMoveWithPCRot (native 1392, rule 14) as the reference install runs it: Reloaded wraps the original
    /// with MaxTimeMargin set to infinity (rule 15).
    /// </summary>
    [UcNative("ASPlayerController::execMyServerMoveWithPCRot", Exe = 0x10aaec30, Editor = 0x111f0ba0)]
    [PortOf("nat:SBase.SPlayerController.MyServerMoveWithPCRot", Spec = "spec/native/net-moves.md#rules")]
    [PortOf("rl:SBase.SPlayerController.MyServerMoveWithPCRot", Spec = "spec/native/net-reloaded.md#rules")]
    public void MyServerMoveWithPCRot(float TimeStamp, FVector ClientLoc, int View, byte DiversInfos, int OldTimeDelta, int OldAccel)
    {
        float saved = MaxTimeMargin;
        if (ReloadedPCRot)
        {
            MaxTimeMargin = float.PositiveInfinity;
        }

        try
        {
            ProcessServerMove(EServerMoveKind.PCRot, TimeStamp, default, ClientLoc, View, DiversInfos, OldTimeDelta, OldAccel, snapped: false);
        }
        finally
        {
            if (ReloadedPCRot && float.IsPositiveInfinity(MaxTimeMargin))
            {
                MaxTimeMargin = saved;
            }
        }
    }

    /// <summary>BigServerMove (native 1393, reliable): a move carrying a pawn action batch (rule 13).</summary>
    [UcNative("ASPlayerController::execBigServerMove", Exe = 0x10aae660, Editor = 0x111f06e0)]
    [PortOf("nat:SBase.SPlayerController.BigServerMove", Spec = "spec/native/net-moves.md#rules")]
    public void BigServerMove(float MSATime, float TimeStamp, FVector InAccel, FVector ClientLoc, FVector Vel, int View, byte DiversInfos,
        int Val0, int Val1)
    {
        var move = new BigServerMoveData
        {
            MSATime = MSATime, TimeStamp = TimeStamp, InAccel = InAccel, ClientLoc = ClientLoc, Vel = Vel, View = View,
            iDiversInfos = DiversInfos, Val0 = Val0, Val1 = Val1,
        };
        ProcessBigMove(move, resend: false);
    }

    /// <summary>UnreliableReplicateBigServerMove (native 1686): one of the three resends of a big move, with its payload.</summary>
    [UcNative("ASPlayerController::execUnreliableReplicateBigServerMove", Exe = 0x10ab1670, Editor = 0x111f1930)]
    [PortOf("nat:SBase.SPlayerController.UnreliableReplicateBigServerMove", Spec = "spec/native/net-moves.md#rules")]
    public void UnreliableReplicateBigServerMove(in BigServerMoveData move) => ProcessBigMove(move, resend: true);

    private void ProcessBigMove(in BigServerMoveData move, bool resend)
    {
        // Accepted when the epochs differ by less than 1 s (trunc of the difference is 0) and the stamp is new.
        if (SvmMath.Trunc(move.MSATime - Controller.MirrorStateAcknowledgementTime) != 0 || (!FixedTickSimulation && move.TimeStamp <= BigServerMoveLastActionTime))
        {
            MovesIgnored++;
            return;
        }

        if (resend)
        {
            BSMS = move;
        }
        else
        {
            BSMS.TimeStamp = move.TimeStamp;
        }

        EPhysics previous = Pawn.Physics;
        float error = (Pawn.Location - move.ClientLoc).Size();
        if (error < MoveK.BigMoveSnapRadius || previous != EPhysics.PHYS_RootMotion || Controller.bUseRootMotionAdditionnalPhysics)
        {
            Pawn.ExecuteReceivedActions(move.Val0, move.Val1);
        }

        bool snapped = false;
        if (error >= MoveK.BigMoveSnapRadius || previous == EPhysics.PHYS_FollowParent)
        {
            // The reliable form leaves a pawn that came from following a parent in root motion (game 0x10aae660);
            // the resend restores the previous physics in that case too (0x10ab1670 has no such test).
            if (Pawn.Physics == EPhysics.PHYS_RootMotion && previous != EPhysics.PHYS_RootMotion && !Controller.bUseRootMotionAdditionnalPhysics &&
                (resend || previous != EPhysics.PHYS_FollowParent))
            {
                Pawn.SetPhysics(previous);
            }
        }
        else
        {
            Pawn.SnapTo(move.ClientLoc);
            snapped = true;
        }

        // The client's velocity is taken whenever the error is under 100 uu, a pawn that was following a parent
        // included (game 0x10aae660).
        if (error < MoveK.BigMoveSnapRadius)
        {
            Pawn.Velocity = move.Vel * (1f / MoveK.VelocityWireScale);
        }

        if (resend)
        {
            ApplyTraversalPayload(move, previous);
        }

        ProcessServerMove(resend ? EServerMoveKind.BigResend : EServerMoveKind.Big, move.TimeStamp, move.InAccel, move.ClientLoc, move.View,
            (byte)move.iDiversInfos, -1, 0, snapped);
        if (FixedTickSimulation && snapped && Pawn is IMoveCommandPawn animation)
        {
            // The action packet carries the completed client step. Consume the matching root cursor too;
            // otherwise the next command applies the same clip displacement a second time.
            animation.ConsumeMoveRootMotion();
            animation.AdvanceMoveAnimation(CommandDeltaTime ?? Sim.DeltaTime);
        }
        BigServerMoveLastActionTime = move.TimeStamp;
    }

    /// <summary>Rule 13: the resend's traversal vectors, by the pawn's new physics.</summary>
    private void ApplyTraversalPayload(in BigServerMoveData move, EPhysics previous)
    {
        switch (Pawn.Physics)
        {
            case EPhysics.PHYS_OnPipe:
            case EPhysics.PHYS_Linear:
            case EPhysics.PHYS_Ledge:
            case EPhysics.PHYS_OnLadder:
                SetZipPoints(move);
                break;
            case EPhysics.PHYS_OnPipeH:
                if (previous == EPhysics.PHYS_OnPipe)
                {
                    Pawn.NewZipP1 = move.ZipP1;
                    Pawn.NewZipP2 = move.ZipP1 + move.DifZipP2;
                }
                else
                {
                    SetZipPoints(move);
                }

                break;
            case EPhysics.PHYS_Vandamme:
                if (!Pawn.bKeepLocalVandammePos)
                {
                    Pawn.vVandammePos = move.vect3;
                }

                Pawn.vWallJumpDir = move.vectUnit * (1f / MoveK.UnitWireScale);
                break;
            case EPhysics.PHYS_Repositionnal:
                SetZipPoints(move);
                if (!Pawn.bKeepLocalVandammePos)
                {
                    // As for the split jump, a kept local position stays (game 0x10ab1670).
                    Pawn.vVandammePos = move.vect3;
                }

                Pawn.m_geoNormal = move.vectUnit * (1f / MoveK.UnitWireScale);
                break;
        }
    }

    private void SetZipPoints(in BigServerMoveData move)
    {
        FVector p1 = move.ZipP1;
        FVector p2 = move.ZipP1 + move.DifZipP2;
        Pawn.NewZipP1 = p1;
        Pawn.NewZipP2 = p2;
        Pawn.ZipP1 = p1;
        Pawn.ZipP2 = p2;
    }

    // ---- the move core ----------------------------------------------------------------------------------------

    /// <summary>
    /// ProcessServerMove (rule 11) and the correction decision (rule 12). <paramref name="OldTimeDelta"/> below 0
    /// means no old-move pair. A snapped big move is not simulated again.
    /// </summary>
    [UcNative("ASPlayerController::ProcessServerMove", Exe = 0x10aac4a0, Editor = 0x111ef4f0)]
    [PortOf("eng:ASPlayerController::ProcessServerMove", Spec = "spec/native/net-moves.md#rules")]
    public void ProcessServerMove(EServerMoveKind kind, float TimeStamp, FVector InAccel, FVector ClientLoc, int View, byte DiversInfos,
        int OldTimeDelta, int OldAccel, bool snapped)
    {
        if (Controller.bWaitingForReinit || (!FixedTickSimulation && TimeStamp <= CurrentTimeStamp))
        {
            MovesIgnored++;
            return;
        }

        bool pcRot = kind == EServerMoveKind.PCRot;
        float now = Controller.TimeSeconds;
        int clientZone = MovePacking.DiversZone(DiversInfos);
        bool speedFlag = MovePacking.DiversSpeed(DiversInfos);
        bool jump = MovePacking.DiversJump(DiversInfos);
        var clientPhysics = MovePacking.DiversPhysics(DiversInfos);

        // 11.1 the old move: only with a non-zero OldTimeDelta byte (game 0x10aac4a0 and 0x10aaec30 test the byte, so 0
        // means no old move whatever the caller passes).
        if (OldTimeDelta > 0 && clientPhysics == Pawn.Physics)
        {
            float oldStamp = TimeStamp - OldTimeDelta * MoveK.OldTimeDeltaUnit;
            if (oldStamp > CurrentTimeStamp + MoveK.OldStampMargin)
            {
                float dt = pcRot ? OldTimeDelta * MoveK.OldTimeDeltaUnit : Math.Min(MaxResponseTime, oldStamp - CurrentTimeStamp);
                // Both forms rebuild the old acceleration from OldAccel (game 0x10aac4a0 and 0x10aaec30).
                Apply(dt, MovePacking.UnpackOldAccel(OldAccel), jump: false);
                CurrentTimeStamp = oldStamp;
            }
        }

        // 11.2, 11.3 the delta and the time-margin guard.
        float deltaTime = CommandDeltaTime ?? Math.Min(MaxResponseTime, TimeStamp - CurrentTimeStamp);
        if (!FixedTickSimulation && ServerTimeStamp > 0f)
        {
            TimeMargin = Math.Max(0f, TimeMargin + deltaTime - MoveK.TimeMarginFactor * (now - ServerTimeStamp));
            if (TimeMargin > MaxTimeMargin)
            {
                TimeMargin -= deltaTime;
                MaxTimeMargin = TimeMargin >= MoveK.MaxTimeMarginHigh ? MoveK.MaxTimeMarginHigh : MoveK.MaxTimeMarginLow;
                deltaTime = 0f;
            }
        }

        // 11.4 the controller's aim.
        MovePacking.UnpackView(View, out int pitch, out int yaw);
        Controller.Rotation = new FRotator(pitch, yaw, 0);

        // The PCRot form writes both halves of iYawPitchFromView here, before the physics check, so the aim follows even
        // a move whose physics differs (game 0x10aaec30); the full form writes it in 11.8, after the check.
        if (pcRot)
        {
            Pawn.iYawPitchFromView = MovePacking.PackW(yaw, pitch);
        }

        // 11.5 the physics check.
        if (!FixedTickSimulation && clientPhysics != Pawn.Physics)
        {
            if (now - fLastTimePhysicsChecked > MoveK.PhysicsMismatchReinit && Pawn.IsAlive)
            {
                // fLastTimePhysicsChecked is left as it is: only a matching move stamps it (game 0x10aac4a0), so a
                // mismatch that outlives the reinit raises the next one at once.
                CurrentTimeStamp = TimeStamp;
                ServerTimeStamp = now;
                Sent.Reinits++;
                Controller.ReinitPawnBecauseOfDivergences();
            }

            MovesIgnored++;
            return;
        }

        fLastTimePhysicsChecked = now;
        CurrentTimeStamp = TimeStamp;
        ServerTimeStamp = now;

        // 11.6 the speed cap (not for PCRot).
        FVector accel = pcRot ? default : InAccel * (1f / MoveK.AccelWireScale);
        if (!pcRot)
        {
            CapGroundSpeed(accel, speedFlag);
        }

        // 11.7 following a parent: applied, no correction (the PCRot form has no such step, game 0x10aaec30).
        EPhysics physics = Pawn.Physics;
        if (!pcRot && physics == EPhysics.PHYS_FollowParent)
        {
            Apply(deltaTime, accel, jump);
            return;
        }

        // 11.8 the aim and the pawn's yaw (PCRot wrote its aim before the physics check and leaves the pawn's rotation
        // alone). A flying pawn only takes the view pitch into the high half of iYawPitchFromView; its yaw half and its
        // rotation stay (game 0x10aac4a0).
        if (!pcRot)
        {
            if (physics == EPhysics.PHYS_Flying)
            {
                Pawn.iYawPitchFromView = MovePacking.PackW(Pawn.iYawPitchFromView, pitch);
            }
            else
            {
                Pawn.iYawPitchFromView = MovePacking.PackW(yaw, pitch);
                FRotator r = Pawn.Rotation;
                Pawn.Rotation = new FRotator(0, yaw, r.Roll);
            }
        }

        // 11.9 apply. The PCRot form has no root motion step: it applies and goes on to the correction.
        bool rootOnly = !FixedTickSimulation && !pcRot && physics == EPhysics.PHYS_RootMotion && !Pawn.bUseRootMotionAdditionnalPhysics;
        if (!rootOnly && !snapped && !Controller.IsPaused && deltaTime > 0f &&
            (FixedTickSimulation || clientPhysics == Pawn.Physics || Pawn.Physics == EPhysics.PHYS_Repositionnal))
        {
            Apply(deltaTime, accel, jump);
        }

        if (rootOnly)
        {
            return;
        }

        // 11.10 the client's zone configuration.
        if (clientZone != Pawn.ZoneConfig)
        {
            ClientLoc.Z += Pawn.ZCDecal(Pawn.ZoneConfig, clientZone);
        }

        DecideCorrection(pcRot, TimeStamp, ClientLoc, clientPhysics, clientZone, now);
    }

    private void Apply(float deltaTime, FVector accel, bool jump)
    {
        if (deltaTime <= 0f)
        {
            return;
        }

        if (FixedTickSimulation)
        {
            while (deltaTime > 0f)
            {
                float step = deltaTime <= Sim.DeltaTime + MoveK.SplitEpsilon ? deltaTime : Sim.DeltaTime;
                Pawn.MoveAutonomous(step, accel, jump);
                deltaTime -= step;
                jump = false;
            }
        }
        else Pawn.MoveAutonomous(deltaTime, accel, jump);
        MovesApplied++;
    }

    /// <summary>Rule 11.6: ground speed from the acceleration's size, capped per side.</summary>
    [Provenance(ProvenanceKind.Decoded, "net-moves.md rule 11.6 and its edge case (spy adrenaline reads as zone bits)")]
    private void CapGroundSpeed(FVector accel, bool speedFlag)
    {
        EPhysics physics = Pawn.Physics;
        float size = accel.Size();
        if (physics is EPhysics.PHYS_Walking or EPhysics.PHYS_Falling)
        {
            float cap = Pawn.IsMerc
                ? (speedFlag ? MoveK.MercChargeCap : MoveK.MercCap)
                : (Pawn.bIsRolling ? MoveK.SpyRollCap : MoveK.SpyWalkCap) * (speedFlag ? Pawn.AdrenalineFactor : 1f);
            Pawn.GroundSpeed = Math.Min(size / MoveK.WalkSpeedDivisor * Pawn.RunningSpeed, cap);
        }
        else if (physics == EPhysics.PHYS_Linear)
        {
            Pawn.GroundSpeed = size / MoveK.LinearSpeedDivisor * Pawn.RunningSpeed;
        }
    }

    /// <summary>Rule 12 (rule 14's intervals for PCRot): when to correct, which form, and when to reinit.</summary>
    private void DecideCorrection(bool pcRot, float timeStamp, FVector clientLoc, EPhysics clientPhysics, int clientZone, float now)
    {
        // Sequenced acknowledgements reconcile this state with replay. The decoded shift/reinit policy would
        // discard valid traversal history and is reserved for the original transport.
        if (FixedTickSimulation) return;
        float errorSquared;
        float forced = pcRot ? MoveK.ForcedCorrectionIntervalPCRot : MoveK.ForcedCorrectionInterval;
        float gap = pcRot ? MoveK.MinCorrectionGapPCRot : MoveK.MinCorrectionGap;
        EPhysics physics = Pawn.Physics;
        if (now - LastUpdateTime > forced)
        {
            errorSquared = MoveK.ForcedCorrectionError;
        }
        else if (now - LastUpdateTime <= MoveK.CorrectionRateBytes / Math.Max(1, ClientNetSpeed) || now - ClientUpdateTime <= gap)
        {
            return;
        }
        else
        {
            FVector diff = Pawn.Location - clientLoc;
            bool ignoreZ = pcRot
                ? Pawn.BaseMoverMovingVertically || physics is EPhysics.PHYS_OnPipeH or EPhysics.PHYS_Linear
                : (physics == EPhysics.PHYS_Falling && now - fLastTimeOnAMover < MoveK.MoverWindow) ||
                  Pawn.BaseMoverMovedVertically(MoveK.MoverWindow) || physics is EPhysics.PHYS_OnPipeH or EPhysics.PHYS_Linear;
            if (ignoreZ)
            {
                diff.Z = 0f;
            }

            // The root motion weight is only in the full form; PCRot compares the plain squared error.
            errorSquared = diff.SizeSquared() * (!pcRot && physics == EPhysics.PHYS_RootMotion ? MoveK.RootMotionErrorScale : 1f);
            if (errorSquared <= MoveK.CorrectionErrorSquared)
            {
                return;
            }
        }

        ClientUpdateTime = now;
        LastUpdateTime = now;
        FVector location = Pawn.Location;
        if (pcRot)
        {
            location.Z -= Pawn.ZCDecal(Pawn.ZoneConfig, clientZone);
        }

        if (Pawn.Velocity.IsZero())
        {
            ScriptMyShortClientAdjustPosition(timeStamp, location.X, location.Y, location.Z, Pawn.Base);
        }
        else
        {
            FVector v = Pawn.Velocity;
            ScriptMyClientAdjustPosition(timeStamp, location.X, location.Y, location.Z, v.X, v.Y, v.Z, Pawn.Base);
        }

        bool bothWalk = physics == EPhysics.PHYS_Walking && clientPhysics == EPhysics.PHYS_Walking;
        if (errorSquared > MoveK.ReinitErrorSquared && Pawn.IsAlive && !bothWalk)
        {
            Sent.Reinits++;
            Controller.ReinitPawnBecauseOfDivergences();
        }
    }

    [Uc("SBase.SPlayerController.ScriptMyShortClientAdjustPosition", Sha1 = "4aac46b93cea2505f2c911e2d7fcb634553e66ef")]
    [PortOf("uc:SBase.SPlayerController:ScriptMyShortClientAdjustPosition", Spec = "spec/net/move-protocol.md#rules")]
    public void ScriptMyShortClientAdjustPosition(float TimeStamp, float ClientLocX, float ClientLocY, float ClientLocZ, Actor? ClientBase)
    {
        Transport.MyShortClientAdjustPosition(TimeStamp, ClientLocX, ClientLocY, ClientLocZ, ClientBase);
        Sent.ShortCorrections++;
    }

    [Uc("SBase.SPlayerController.ScriptMyClientAdjustPosition", Sha1 = "eb2bffbda23302d0c39706d0c6d11915c60f80d8")]
    [PortOf("uc:SBase.SPlayerController:ScriptMyClientAdjustPosition", Spec = "spec/net/move-protocol.md#rules")]
    public void ScriptMyClientAdjustPosition(float TimeStamp, float ClientLocX, float ClientLocY, float ClientLocZ, float ClientVelX,
        float ClientVelY, float ClientVelZ, Actor? ClientBase)
    {
        Transport.MyClientAdjustPosition(TimeStamp, ClientLocX, ClientLocY, ClientLocZ, ClientVelX, ClientVelY, ClientVelZ, ClientBase);
        Sent.Corrections++;
    }

    /// <summary>
    /// The network part of ReinitPawnBecauseOfDivergences, normal case (state-sync.md rule 12.3): the pawn's reset,
    /// a new epoch, the reliable ClientReinitPawnBecauseOfDivergences (location relative to a mover base), and the
    /// wait for the client. The controller port runs the rest of the event (cases, damage, states).
    /// </summary>
    [Uc("SBase.SPlayerController.ReinitPawnBecauseOfDivergences", Sha1 = "6cc7a48b4e0338b34c21daa26c53713fd02ce985")]
    [PortOf("uc:SBase.SPlayerController:ReinitPawnBecauseOfDivergences", Spec = "spec/net/state-sync.md#rules")]
    public void ReinitPawn()
    {
        Pawn.ReinitBecauseOfDivergences();
        float now = Controller.TimeSeconds;
        Controller.MirrorStateAcknowledgementTime = now;
        Actor? b = Pawn.Base;
        FVector location = b is { bIsAMover: true } ? Pawn.Location - b.Location : Pawn.Location;
        Transport.ClientReinitPawnBecauseOfDivergences((byte)ReinitType.RT_Normal, now, location, Pawn.Rotation, b);
        Controller.bWaitingForReinit = true;
    }

    /// <summary>TellServerIAMReadyToReinit arrived: the controller's waiting state answers it.</summary>
    public void TellServerIAMReadyToReinit()
    {
        if (Controller.bWaitingForReinit)
        {
            Controller.ServerReinitReady();
        }
    }
}
