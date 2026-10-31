using System;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Provenance;

namespace OpenScct.Net.Moves;

/// <summary>
/// The owning client's half of the SCCT move protocol for one player controller: frame batching and message choice
/// (MyReplicateMove, RealReplicateMove), the saved moves, the big move and its resends, and the corrections from the
/// server (MyShortClientAdjustPosition, MyClientAdjustPosition, MyLongClientAdjustPosition, ClientUpdatePosition),
/// which shift the present position instead of replaying moves. Written from spec/native/net-moves.md rules 1 to 10
/// and 16, spec/net/move-protocol.md rules 2 to 9 and 19 to 22, and spec/net/state-sync.md rule 13.
/// <para>The controller port calls <see cref="MyReplicateMove"/> from PlayerMove when <c>Role &lt; ROLE_Authority</c>,
/// <see cref="MyReplicateMoveWithoutMoving"/> from MyFalling and MyNotifyLanded, and <see cref="ClientUpdatePosition"/>
/// from PlayerTick when <see cref="bUpdatePosition"/> is set.</para>
/// </summary>
public sealed class ClientMoves
{
    private readonly int[] _actions = new int[6];

    public ClientMoves(IMovePawn pawn, IMoveController controller, IMoveTransport transport)
    {
        Pawn = pawn ?? throw new ArgumentNullException(nameof(pawn));
        Controller = controller ?? throw new ArgumentNullException(nameof(controller));
        Transport = transport ?? throw new ArgumentNullException(nameof(transport));
        SavedMoves = new SavedMoveList(transport is SequencedMoveTransport ? SequencedMoveTransport.Capacity : MoveK.SavedMoveRing,
            preallocate: transport is SequencedMoveTransport);
    }

    public IMovePawn Pawn { get; }

    public IMoveController Controller { get; }

    public IMoveTransport Transport { get; }

    // ---- pending move (native state) ---------------------------------------------------------------------------

    public float fPendingDeltaTime;
    public FVector vPendingAccel;
    public EPhysics ePendingPhysics;
    public byte PendingDiversInfo;
    public bool bPhysicsChangeWithinPhysics;
    public float fLeftTimeForSecondPhysics;

    /// <summary>Stamp of the last full or PCRot move sent (ScriptMyServerMove), for the old-move pair.</summary>
    public float fLastMovementTime;

    /// <summary>Acceleration of the last full move sent, as sent (× 10).</summary>
    public FVector vOldAccel;

    public int NumberOfBigServerMoveToReSend;

    public BigServerMoveData BSMS;

    /// <summary>The short move toggle (one process-wide flag in the original, per controller here).</summary>
    public bool ShortMoveToggle;

    public SavedMoveList SavedMoves { get; }

    public long MovesReplayed { get; private set; }
    public long AuthoritativeCorrectionsReceived { get; private set; }
    public long CorrectionsObsoletedByState { get; private set; }
    public float LastCorrectionDistance { get; private set; }
    private FVector _authoritativeVelocity;
    private EPhysics _authoritativePhysics;
    private byte _authoritativeZone;
    private bool Modern => Transport is SequencedMoveTransport;
    private uint _correctionEpoch, _acknowledgedSequence;

    public void ResetPredictionHistory()
    {
        SavedMoves.Clear(); bUpdatePosition = false; UpdatingSMove = null;
        _acknowledgedSequence = 0; fPendingDeltaTime = 0f; NumberOfBigServerMoveToReSend = 0;
    }

    public void AcknowledgePrediction(uint epoch, uint sequence, float stamp)
    {
        if (_correctionEpoch == epoch && sequence <= _acknowledgedSequence) return;
        _correctionEpoch = epoch; _acknowledgedSequence = sequence; CurrentTimeStamp = stamp;
        bUpdatePosition = false; UpdatingSMove = null;
    }

    public void ReceiveAuthoritativeState(uint epoch, uint sequence, float stamp, FVector location, FVector velocity, EPhysics physics, byte zone, Actor? @base,
        FVector? basePosition = null, FRotator baseRotation = default)
    {
        if (Pawn is not IMovePrediction prediction || (_correctionEpoch == epoch && sequence <= _acknowledgedSequence)) return;
        _correctionEpoch = epoch; _acknowledgedSequence = sequence;
        AuthoritativeCorrectionsReceived++;
        CurrentTimeStamp = stamp;
        if (Pawn is IMoveCommandPawn { CommandSimulationEnabled: false }) { bUpdatePosition = false; return; }
        for (SavedMove? move = SavedMoves.FirstSMove; move is not null; move = move.NextMove)
        {
            if (move.Sequence == sequence && move.Epoch == epoch)
            {
                if (!prediction.PredictionHistoryComplete(move.Slot))
                { ((SequencedMoveTransport)Transport).RequestResynchronization(); return; }
                if (!prediction.CanReplayPrediction(move.Slot)) { CorrectionsObsoletedByState++; return; }
                if (basePosition is { } baseAt)
                    prediction.RebaseAuthoritativeState(move.Slot, @base, baseAt, baseRotation, ref location, ref velocity);
                if ((move.SavedLocation - location).SizeSquared() < MoveK.CorrectionErrorSquared &&
                    (move.SavedVelocity - velocity).SizeSquared() < 1f && move.SavedPhysics == physics &&
                    move.ZoneConfig == zone && ReferenceEquals(move.SavedBase, @base)) return;
                UpdatingSMove = move; vUpdateLocation = location; _authoritativeVelocity = velocity;
                _authoritativePhysics = physics; _authoritativeZone = zone; aUpdateBase = @base; bUpdatePosition = true;
                return;
            }
            if (ReferenceEquals(move, SavedMoves.LastSMove)) break;
        }
        ((SequencedMoveTransport)Transport).RequestResynchronization();
    }

    public void EnsurePredictionStep()
    {
        if (!Modern) return;
        if (SavedMoves.LastSMove is not { } last || last.TimeStamp < Controller.TimeSeconds - MoveK.SplitEpsilon)
        {
            if (Pawn is IMoveCommandPawn { CommandSimulationEnabled: true })
                Pawn.MoveAutonomous(Sim.DeltaTime, default, false);
            fPendingDeltaTime = Sim.DeltaTime;
            var transport = (SequencedMoveTransport)Transport;
            transport.Heartbeat = true;
            try { RealReplicateMove(Controller.TimeSeconds, default, CurrentDiversInfos(), allowBig: true); }
            finally { transport.Heartbeat = false; }
            fPendingDeltaTime = 0f;
        }
    }

    public void FinishPredictionTick()
    {
        if (!Modern) return;
        EnsurePredictionStep();
        if (SavedMoves.LastSMove is { } move && Pawn is IMovePrediction prediction)
        {
            move.SavedLocation = Pawn.Location; move.SavedPhysics = Pawn.Physics;
            move.SavedVelocity = Pawn.Velocity; move.SavedBase = Pawn.Base; move.ZoneConfig = Pawn.ZoneConfig;
            prediction.CapturePrediction(move.Slot);
        }
        (Transport as SequencedMoveTransport)?.SealPredictionTick();
    }

    // ---- corrections ------------------------------------------------------------------------------------------

    /// <summary>Stamp of the newest correction received (PlayerController.CurrentTimeStamp on the client).</summary>
    public float CurrentTimeStamp;

    public SavedMove? UpdatingSMove;
    public FVector vUpdateLocation;
    public FVector vUpdateVelocity;
    public Actor? aUpdateBase;
    public bool bUpdatePosition;
    public bool bUpdating;

    /// <summary>fLastTimeOnAMover: no writer in the original (rule 20.4b never fires).</summary>
    public float fLastTimeOnAMover = MoveK.LastTimeOnAMoverDefault;

    /// <summary>Messages sent, by kind, for tests and diagnostics.</summary>
    public MoveCounters Sent { get; } = new();

    /// <summary>Corrections applied (ClientUpdatePosition ran without cancelling).</summary>
    public int CorrectionsApplied { get; private set; }

    // ---- per frame -------------------------------------------------------------------------------------------

    /// <summary>
    /// MyReplicateMove (native 1391, rule 1): moves the pawn locally for <paramref name="DeltaTime"/>, then batches
    /// the frame and sends when due (rule 2).
    /// </summary>
    [UcNative("ASPlayerController::execMyReplicateMove", Exe = 0x10aad5c0, Editor = 0x111f3000)]
    [PortOf("nat:SBase.SPlayerController.MyReplicateMove", Spec = "spec/native/net-moves.md#rules")]
    public void MyReplicateMove(float DeltaTime, FVector NewAccel)
    {
        bPhysicsChangeWithinPhysics = false;
        fLeftTimeForSecondPhysics = 0f;
        Pawn.MoveAutonomous(DeltaTime, NewAccel, Controller.bPressedJump);
        float frame = DeltaTime;
        if (bPhysicsChangeWithinPhysics)
        {
            if (fLeftTimeForSecondPhysics < MoveK.SplitEpsilon && !Pawn.ActionsWaitForBigMove)
            {
                return;
            }

            // The part before the split went out already; the rest of the frame is batched as usual.
            frame = fLeftTimeForSecondPhysics;
        }

        Batch(frame, NewAccel, Controller.TimeSeconds, CurrentDiversInfos());
    }

    /// <summary>
    /// MyReplicateMoveWithoutMoving (native 1361, rule 6), from MyFalling and MyNotifyLanded while the frame is
    /// simulated: the part already simulated (<c>frameDelta − LeftTime</c>) is batched, then the pending move is
    /// sent at once at <c>Level.TimeSeconds − LeftTime</c>.
    /// </summary>
    [UcNative("ASPlayerController::execMyReplicateMoveWithoutMoving", Editor = 0x111f3450)]
    [PortOf("nat:SBase.SPlayerController.MyReplicateMoveWithoutMoving", Spec = "spec/native/net-moves.md#rules")]
    public void MyReplicateMoveWithoutMoving(float LeftTime, FVector NewAccel, float frameDelta)
    {
        bPhysicsChangeWithinPhysics = true;
        float done = Math.Max(0f, frameDelta - LeftTime);
        float stamp = Controller.TimeSeconds - LeftTime;
        byte divers = CurrentDiversInfos();
        if (fPendingDeltaTime > 0f && (ePendingPhysics != Pawn.Physics || PendingDiversInfo != divers))
        {
            RealReplicateMove(stamp - done, vPendingAccel, PendingDiversInfo, allowBig: false);
            fPendingDeltaTime = 0f;
        }

        Merge(done, NewAccel, divers);
        RealReplicateMove(stamp, vPendingAccel, PendingDiversInfo, allowBig: true);
        fPendingDeltaTime = 0f;
        fLeftTimeForSecondPhysics = LeftTime;
    }

    private byte CurrentDiversInfos() =>
        MovePacking.DiversInfos(Pawn.Physics, Controller.bPressedJump, Pawn.IsMerc, Pawn.bIsCharging, Pawn.AdrenalineActive, Pawn.ZoneConfig);

    /// <summary>
    /// Rule 2: frames merge while reliable data is unacknowledged (window <c>0.09 × min(1, OutstandingReliable / 40)</c>),
    /// as long as no actions wait and physics and DiversInfos are unchanged.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "net-moves.md rule 2, game 0x10aad5c0")]
    private void Batch(float deltaTime, FVector accel, float now, byte divers)
    {
        if (Modern)
        {
            fPendingDeltaTime = deltaTime;
            RealReplicateMove(now, accel, divers, allowBig: true);
            fPendingDeltaTime = 0f;
            return;
        }
        float maxPending = MoveK.MaxPendingWindow * Math.Min(1f, Transport.OutstandingReliable / (float)MoveK.MaxAcceptableUnAckBunch);
        bool actions = Pawn.ActionsWaitForBigMove;
        if (!actions && (fPendingDeltaTime <= 0f || (ePendingPhysics == Pawn.Physics && PendingDiversInfo == divers)))
        {
            Merge(deltaTime, accel, divers);
            if (fPendingDeltaTime >= maxPending)
            {
                RealReplicateMove(now, vPendingAccel, PendingDiversInfo, allowBig: true);
                fPendingDeltaTime = 0f;
            }

            return;
        }

        if (fPendingDeltaTime > 0f)
        {
            RealReplicateMove(now - deltaTime, vPendingAccel, PendingDiversInfo, allowBig: false);
        }

        fPendingDeltaTime = deltaTime;
        vPendingAccel = accel;
        ePendingPhysics = Pawn.Physics;
        PendingDiversInfo = divers;
        if (deltaTime >= maxPending || actions)
        {
            RealReplicateMove(now, vPendingAccel, PendingDiversInfo, allowBig: true);
            fPendingDeltaTime = 0f;
        }
    }

    private void Merge(float deltaTime, FVector accel, byte divers)
    {
        float total = fPendingDeltaTime + deltaTime;
        vPendingAccel = total > 0f ? (vPendingAccel * fPendingDeltaTime + accel * deltaTime) * (1f / total) : accel;
        fPendingDeltaTime = total;
        ePendingPhysics = Pawn.Physics;
        PendingDiversInfo = divers;
    }

    /// <summary>
    /// RealReplicateMove (rule 3): the big-move resends, a saved move, then one of BigServerMove (actions wait),
    /// nothing (root motion, following a parent), MyServerMoveWithPCRot, MyServerMove (non-zero acceleration) or,
    /// every other call, MyShortServerMove.
    /// </summary>
    [UcNative("ASPlayerController::RealReplicateMove", Exe = 0x10aad000, Editor = 0x111f2a80)]
    [PortOf("eng:ASPlayerController::RealReplicateMove", Spec = "spec/native/net-moves.md#rules")]
    public void RealReplicateMove(float TimeStamp, FVector Accel, byte DiversInfos, bool allowBig)
    {
        if (NumberOfBigServerMoveToReSend != 0)
        {
            if (NumberOfBigServerMoveToReSend < MoveK.C_MaxBSMReSend)
            {
                ScriptUnreliableReplicateBigServerMove(BSMS);
            }

            NumberOfBigServerMoveToReSend--;
        }

        EPhysics physics = Pawn.Physics;
        SavedMove saved = SavedMoves.Add(TimeStamp, Pawn.Location, physics, (byte)(DiversInfos & 3));
        saved.DeltaTime = Modern ? Math.Max(0f, fPendingDeltaTime)
            : fPendingDeltaTime > 0f ? fPendingDeltaTime : Sim.DeltaTime;
        saved.SavedVelocity = Pawn.Velocity; saved.SavedBase = Pawn.Base;
        saved.Acceleration = Accel; saved.Jump = Controller.bPressedJump;
        if (Transport is SequencedMoveTransport sequenced)
        {
            sequenced.DeltaTime = saved.DeltaTime;
            saved.Sequence = sequenced.NextSequence; saved.Epoch = sequenced.CommandEpoch;
        }
        if (Modern && Pawn is IMovePrediction prediction) prediction.CapturePrediction(saved.Slot);
        FVector velocity = Pawn.bTestBackFeetGrab ? default
            : physics == EPhysics.PHYS_RootMotion ? new FVector(Pawn.Rotation.Yaw, 0f, 0f)
            : Pawn.Velocity;
        int oldTimeDelta = MovePacking.OldTimeDelta(TimeStamp, fLastMovementTime);
        int oldAccel = oldTimeDelta >= 0 ? MovePacking.PackOldAccel(vOldAccel * (1f / MoveK.AccelWireScale)) : 0;
        int pitch = Controller.Rotation.Pitch;

        if (allowBig && Pawn.ActionsWaitForBigMove)
        {
            int count = Pawn.TakeActionsForBigMove(_actions);
            ScriptBigServerMove(TimeStamp, Accel * MoveK.AccelWireScale, Pawn.Location, velocity * MoveK.VelocityWireScale,
                MovePacking.PackView(pitch, Pawn.Rotation.Yaw), DiversInfos, _actions, count);
            ShortMoveToggle = true;
            return;
        }

        if (!Modern && physics is EPhysics.PHYS_RootMotion or EPhysics.PHYS_FollowParent)
        {
            return;
        }

        if (Pawn.UsesPCRot)
        {
            ScriptMyServerMoveWithPCRot(TimeStamp, Pawn.Location, MovePacking.PackView(pitch, Controller.Rotation.Yaw), DiversInfos,
                oldTimeDelta, oldAccel);
            return;
        }

        if (!Accel.IsZero())
        {
            ScriptMyServerMove(TimeStamp, Accel * MoveK.AccelWireScale, Pawn.Location, MovePacking.PackView(pitch, Pawn.Rotation.Yaw),
                DiversInfos, oldTimeDelta, oldAccel);
            ShortMoveToggle = true;
            return;
        }

        if (Modern || ShortMoveToggle)
        {
            ScriptMyShortServerMove(TimeStamp, Pawn.Location, MovePacking.PackView(pitch, Pawn.Rotation.Yaw), DiversInfos, oldTimeDelta,
                oldAccel);
        }

        ShortMoveToggle = !ShortMoveToggle;
    }

    // ---- the script send events --------------------------------------------------------------------------------

    [Uc("SBase.SPlayerController.ScriptMyServerMove", Sha1 = "d9e33c82c6a93d81e42198df5088ee514f560d29")]
    [PortOf("uc:SBase.SPlayerController:ScriptMyServerMove", Spec = "spec/net/move-protocol.md#rules")]
    public void ScriptMyServerMove(float TimeStamp, FVector InAccel, FVector ClientLoc, int View, byte DiversInfos, int OldTimeDelta,
        int OldAccel)
    {
        fLastMovementTime = TimeStamp;
        vOldAccel = InAccel;
        Transport.MyServerMove(TimeStamp, InAccel, ClientLoc, View, DiversInfos, OldTimeDelta, OldAccel);
        Sent.Full++;
    }

    [Uc("SBase.SPlayerController.ScriptMyShortServerMove", Sha1 = "d119e3c6198c06bc0278b1a2a476b6d5a9744013")]
    [PortOf("uc:SBase.SPlayerController:ScriptMyShortServerMove", Spec = "spec/net/move-protocol.md#rules")]
    public void ScriptMyShortServerMove(float TimeStamp, FVector ClientLoc, int View, byte DiversInfos, int OldTimeDelta, int OldAccel)
    {
        Transport.MyShortServerMove(TimeStamp, ClientLoc, View, DiversInfos, OldTimeDelta, OldAccel);
        Sent.Short++;
    }

    [Uc("SBase.SPlayerController.ScriptMyServerMoveWithPCRot", Sha1 = "0d0071159faa184244ec2d08753f6e742f44ca81")]
    [PortOf("uc:SBase.SPlayerController:ScriptMyServerMoveWithPCRot", Spec = "spec/net/move-protocol.md#rules")]
    public void ScriptMyServerMoveWithPCRot(float TimeStamp, FVector ClientLoc, int View, byte DiversInfos, int OldTimeDelta, int OldAccel)
    {
        fLastMovementTime = TimeStamp;
        vOldAccel = default;
        Transport.MyServerMoveWithPCRot(TimeStamp, ClientLoc, View, DiversInfos, OldTimeDelta, OldAccel);
        Sent.PCRot++;
    }

    /// <summary>
    /// ScriptBigServerMove (move-protocol.md rule 6): packs the actions, sends the reliable big move, keeps it in
    /// BSMS with the traversal payload of the pawn's physics, and arms three unreliable resends.
    /// </summary>
    [Uc("SBase.SPlayerController.ScriptBigServerMove", Sha1 = "687fff59448eea2c9da63460bc0f87df13ec8441")]
    [PortOf("uc:SBase.SPlayerController:ScriptBigServerMove", Spec = "spec/net/move-protocol.md#rules")]
    public void ScriptBigServerMove(float TimeStamp, FVector InAccel, FVector ClientLoc, FVector Vel, int View, byte DiversInfos,
        ReadOnlySpan<int> actions, int NbAction)
    {
        MovePacking.CompressActions(actions, NbAction, out int val0, out int val1);
        float msa = Controller.MirrorStateAcknowledgementTime;
        Transport.BigServerMove(msa, TimeStamp, InAccel, ClientLoc, Vel, View, DiversInfos, val0, val1);
        BSMS = new BigServerMoveData
        {
            MSATime = msa, TimeStamp = TimeStamp, View = View, Val0 = val0, Val1 = val1, iDiversInfos = DiversInfos,
            InAccel = InAccel, ClientLoc = ClientLoc, Vel = Vel,
        };
        switch ((EPhysics)(DiversInfos >> 4))
        {
            case EPhysics.PHYS_Repositionnal:
                BSMS.ZipP1 = Pawn.NewZipP1;
                BSMS.DifZipP2 = Pawn.ZipP2 - Pawn.NewZipP1;
                BSMS.vect3 = Pawn.vVandammePos;
                BSMS.vectUnit = Pawn.m_geoNormal * MoveK.UnitWireScale;
                break;
            case EPhysics.PHYS_Vandamme:
                BSMS.vect3 = Pawn.vVandammePos;
                BSMS.vectUnit = Pawn.vWallJumpDir * MoveK.UnitWireScale;
                break;
            case EPhysics.PHYS_OnPipe:
            case EPhysics.PHYS_Linear:
            case EPhysics.PHYS_Ledge:
            case EPhysics.PHYS_OnLadder:
            case EPhysics.PHYS_OnPipeH:
                BSMS.ZipP1 = Pawn.NewZipP1;
                BSMS.DifZipP2 = Pawn.NewZipP2 - Pawn.NewZipP1;
                break;
        }

        NumberOfBigServerMoveToReSend = MoveK.C_MaxBSMReSend;
        Sent.Big++;
    }

    [Uc("SBase.SPlayerController.ScriptUnreliableReplicateBigServerMove", Sha1 = "8d9ab46197eadfa9e90869f1ccfbbda2b7f7ec1f")]
    [PortOf("uc:SBase.SPlayerController:ScriptUnreliableReplicateBigServerMove", Spec = "spec/net/move-protocol.md#rules")]
    public void ScriptUnreliableReplicateBigServerMove(in BigServerMoveData move)
    {
        Transport.UnreliableReplicateBigServerMove(move);
        Sent.BigResend++;
    }

    // ---- corrections -----------------------------------------------------------------------------------------

    [Uc("SBase.SPlayerController.MyShortClientAdjustPosition", Sha1 = "069f644f5d3665d0470a05a98379d9a8edc2c8bb")]
    [PortOf("uc:SBase.SPlayerController:MyShortClientAdjustPosition", Spec = "spec/net/move-protocol.md#rules")]
    public void MyShortClientAdjustPosition(float TimeStamp, float NewLocX, float NewLocY, float NewLocZ, Actor? NewBase) =>
        MyLongClientAdjustPosition(TimeStamp, NewLocX, NewLocY, NewLocZ, 0f, 0f, 0f, NewBase, Pawn.ZoneConfig);

    [Uc("SBase.SPlayerController.MyClientAdjustPosition", Sha1 = "b3a97dc8a3ab95ea03d2739bce8fc164c9b99190")]
    [PortOf("uc:SBase.SPlayerController:MyClientAdjustPosition", Spec = "spec/net/move-protocol.md#rules")]
    public void MyClientAdjustPosition(float TimeStamp, float NewLocX, float NewLocY, float NewLocZ, float NewVelX, float NewVelY,
        float NewVelZ, Actor? NewBase) =>
        MyLongClientAdjustPosition(TimeStamp, NewLocX, NewLocY, NewLocZ, NewVelX, NewVelY, NewVelZ, NewBase, Pawn.ZoneConfig);

    /// <summary>
    /// Rule 20: finds the saved move with the correction's exact stamp; if the error there is at least
    /// <see cref="MoveK.fErrorMax"/> it arms the correction for <see cref="ClientUpdatePosition"/>.
    /// </summary>
    [Uc("SBase.SPlayerController.MyLongClientAdjustPosition", Sha1 = "2c868f2f7e012686730e19ae8eccbaa7c5a5675c")]
    [PortOf("uc:SBase.SPlayerController:MyLongClientAdjustPosition", Spec = "spec/net/move-protocol.md#rules")]
    public void MyLongClientAdjustPosition(float TimeStamp, float NewLocX, float NewLocY, float NewLocZ, float NewVelX, float NewVelY,
        float NewVelZ, Actor? NewBase, byte NewZoneConfig)
    {
        if (CurrentTimeStamp > TimeStamp)
        {
            return;
        }

        CurrentTimeStamp = TimeStamp;
        var newLocation = new FVector(NewLocX, NewLocY, NewLocZ);
        var newVelocity = new FVector(NewVelX, NewVelY, NewVelZ);
        if (Pawn.Physics == EPhysics.PHYS_RootMotion)
        {
            return;
        }

        UpdatingSMove = null;
        SavedMove? beforeMove = null;
        int i = 0;
        float now = Controller.TimeSeconds;
        for (SavedMove? move = SavedMoves.FirstSMove; move is not null;)
        {
            if (move.TimeStamp == CurrentTimeStamp)
            {
                if ((NewBase is Mover { } nb && now - nb.fLastTimeMoverMoves < MoveK.MoverWindow) ||
                    Pawn.BaseMoverMovedVertically(MoveK.MoverWindow))
                {
                    newLocation.Z = move.SavedLocation.Z;
                }

                if (Pawn.Physics == EPhysics.PHYS_Falling && now - fLastTimeOnAMover < MoveK.MoverWindow)
                {
                    newLocation.Z = move.SavedLocation.Z;
                }

                if ((move.SavedLocation - newLocation).Size() < MoveK.fErrorMax)
                {
                    return;
                }

                UpdatingSMove = move;
                break;
            }

            beforeMove = move;
            i++;
            move = move.NextMove;
            if (i > MoveK.SavedMoveRing - 1)
            {
                break;
            }
        }

        if (i > MoveK.SavedMoveRing - 1)
        {
            return;
        }

        vUpdateLocation = newLocation;
        vUpdateVelocity = default;
        if (Pawn.Physics == EPhysics.PHYS_Falling && beforeMove is not null)
        {
            float dt = TimeStamp - beforeMove.TimeStamp;
            if (dt > 0f)
            {
                FVector travelled = UpdatingSMove is null ? default : UpdatingSMove.SavedLocation - beforeMove.SavedLocation;
                vUpdateVelocity = newVelocity - travelled * (1f / dt);
            }

            vUpdateVelocity.Z = 0f;
        }

        aUpdateBase = NewBase;
        bUpdatePosition = true;
    }

    /// <summary>
    /// Rule 21: applies an armed correction as an offset of the present position (no replay), unless physics changed
    /// since the matched move; later saved moves shift by the offset actually applied.
    /// </summary>
    [Uc("SBase.SPlayerController.ClientUpdatePosition", Sha1 = "e0079c8401082a4b4eff61fad6e788b13a9c7410")]
    [PortOf("uc:SBase.SPlayerController:ClientUpdatePosition", Spec = "spec/net/move-protocol.md#rules")]
    public void ClientUpdatePosition()
    {
        if (Modern && Pawn is IMoveCommandPawn { CommandSimulationEnabled: false }) { bUpdatePosition = false; return; }
        if (Modern && Pawn is IMovePrediction prediction)
        {
            bUpdatePosition = false;
            if (UpdatingSMove is not { } acknowledged) return;
            if (!prediction.CanReplayPrediction(acknowledged.Slot)) return;
            FVector previous = Pawn.Location;
            prediction.BeginPredictionReplay();
            try
            {
                prediction.RestorePrediction(acknowledged.Slot, vUpdateLocation, _authoritativeVelocity, _authoritativePhysics, aUpdateBase, _authoritativeZone);
                for (SavedMove? move = acknowledged.NextMove; move is not null && move.Epoch == acknowledged.Epoch && move.Sequence > acknowledged.Sequence; move = move.NextMove)
                {
                    if (!prediction.PredictionHistoryComplete(move.Slot))
                    { ((SequencedMoveTransport)Transport).RequestResynchronization(); break; }
                    prediction.ReplayPrediction(move.Slot, move.DeltaTime, move.Acceleration, move.Jump);
                    move.SavedLocation = Pawn.Location; move.SavedPhysics = Pawn.Physics;
                    move.SavedVelocity = Pawn.Velocity; move.SavedBase = Pawn.Base; move.ZoneConfig = Pawn.ZoneConfig;
                    prediction.CapturePrediction(move.Slot); MovesReplayed++;
                    if (ReferenceEquals(move, SavedMoves.LastSMove)) break;
                }
                LastCorrectionDistance = (Pawn.Location - previous).Size();
                CorrectionsApplied++;
            }
            finally { prediction.EndPredictionReplay(previous); }
            return;
        }
        if (Pawn.Physics is EPhysics.PHYS_RootMotion or EPhysics.PHYS_FollowParent)
        {
            return;
        }

        bUpdatePosition = false;
        SavedMove? updating = UpdatingSMove;
        if (updating is null)
        {
            return;
        }

        bUpdating = true;
        EPhysics before = updating.SavedPhysics;
        bool cancelled = SavedMoves.LastSMove is { } last && last.SavedPhysics != before;
        if (!cancelled)
        {
            for (SavedMove? m = updating.NextMove; m is not null && m.TimeStamp > CurrentTimeStamp; m = m.NextMove)
            {
                if (m.SavedPhysics != before)
                {
                    cancelled = true;
                    break;
                }

                if (ReferenceEquals(m, SavedMoves.LastSMove))
                {
                    break;
                }
            }
        }

        if (!cancelled)
        {
            if ((aUpdateBase is Mover { } ub && ub.Velocity.Z != 0f) || Pawn.BaseMoverMovingVertically)
            {
                vUpdateLocation.Z = updating.SavedLocation.Z;
            }

            FVector diff = vUpdateLocation - updating.SavedLocation;
            FVector old = Pawn.Location;
            Pawn.MovePawn(diff);
            diff = Pawn.Location - old;
            if (Pawn.Physics == EPhysics.PHYS_Falling)
            {
                Pawn.Velocity += vUpdateVelocity;
            }

            for (SavedMove? m = updating.NextMove; m is not null && m.TimeStamp > CurrentTimeStamp; m = m.NextMove)
            {
                m.SavedLocation += diff + vUpdateVelocity * (m.TimeStamp - CurrentTimeStamp);
                if (ReferenceEquals(m, SavedMoves.LastSMove))
                {
                    break;
                }
            }

            CorrectionsApplied++;
        }

        bUpdating = false;
    }

    /// <summary>
    /// The network part of ClientReinitPawnBecauseOfDivergences (state-sync.md rule 13): the new epoch, the pawn's
    /// reset and placement (relative to a mover base), then the controller's own reset.
    /// </summary>
    [Uc("SBase.SPlayerController.ClientReinitPawnBecauseOfDivergences", Sha1 = "5a3d50ba79d53e10de76d95b5469cc45ffab1271")]
    [PortOf("uc:SBase.SPlayerController:ClientReinitPawnBecauseOfDivergences", Spec = "spec/net/state-sync.md#rules")]
    public void ClientReinitPawnBecauseOfDivergences(byte RT, float MSATime, FVector vPos, FRotator rRot, Actor? aBase)
    {
        if (Modern) { SavedMoves.Clear(); bUpdatePosition = false; UpdatingSMove = null; _acknowledgedSequence = 0; }
        Controller.MirrorStateAcknowledgementTime = MSATime;
        Pawn.ReinitBecauseOfDivergences();
        FVector location = aBase is { bIsAMover: true } ? aBase.Location + vPos : vPos;
        Pawn.Place(location, aBase, rRot);
        Controller.ClientReinitDone(RT);
    }
}

/// <summary>How many of each move message went out.</summary>
public sealed class MoveCounters
{
    public long Full;
    public long Short;
    public long PCRot;
    public long Big;
    public long BigResend;
    public long ShortCorrections;
    public long Corrections;
    public long Reinits;
}
