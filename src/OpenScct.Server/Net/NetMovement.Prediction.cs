using System;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Animation;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using OpenScct.Net.Moves;

namespace OpenScct.Server;

public sealed partial class SBasePawnMoves
{
    private PredictionFrame[]? _prediction;
    private bool _wasReplaying;
    private EPhysics _replayExpectedPhysics;
    private FVector _replayReference;
    private int _replayAction;
    private readonly PredictionCollisionFrame _liveCollision = new();
    private Actor? _replayBase;
    private FVector _replayBasePosition;
    private FRotator _replayBaseRotation;
    private uint _authoritativeOwnerAction;
    public void NoteAuthoritativeOwnerAction(uint sequence) => _authoritativeOwnerAction = sequence;

    private sealed class PredictionFrame
    {
        public long Tick;
        public float Time;
        public float AuthoritativeStateTime;
        public uint AuthoritativeOwnerAction;
        public EPhysics Physics;
        public FVector Position, Velocity;
        public Actor? Base;
        public FVector BasePosition;
        public FRotator BaseRotation;
        public int Action => _pawn_sAction;
        public LatentFrame ControllerState, PawnState;
        public PawnRootMotion.State Root;
        public readonly NetworkAnimChannel[] Channels = new NetworkAnimChannel[PawnAnimation.ChannelCount];
        public readonly PredictionCollisionFrame Collision = new();
        private long _collisionTick = long.MinValue;
        private FVector _pawn_ZipP1;
        private FVector _pawn_ZipP2;
        private FVector _pawn_NewZipP1;
        private FVector _pawn_NewZipP2;
        private FVector _pawn_m_geoNormal;
        private FVector _pawn_vBackFeetNormal;
        private FVector _pawn_m_CurrentArmsZone;
        private float _pawn_m_CurrentArmsRadius;
        private FVector _pawn_m_CurrentFeetZone;
        private float _pawn_m_CurrentFeetRadius;
        private FVector _pawn_vDeltaMove;
        private FVector _pawn_vVandammePos;
        private FVector _pawn_vWallJumpDir;
        private FVector _pawn_vSpeedAfterRM;
        private FVector _pawn_vMoveAfterRM;
        private FVector _pawn_vLocationBeforeRM;
        private int _pawn_rRotBeforeRM;
        private int _pawn_rRotAfterRM;
        private int _pawn_iLockRootMotionType;
        private float _pawn_fLastRootMotionTimeCalled;
        private EPhysics _pawn_RootMotionAdditionnalPhysics;
        private float _pawn_fPhysReposLinearSpeed;
        private bool _pawn_bUseAdditionnalPhysicsForRepos;
        private bool _pawn_bNoReposAtEnd;
        private bool _pawn_bKeepLocalVandammePos;
        private bool _pawn_bReverseRM;
        private bool _pawn_bIsZipSpeed;
        private bool _pawn_bIsPipe4Decal;
        private bool _pawn_bIsLeftPart;
        private bool _pawn_bIsRolling;
        private bool _pawn_bCanStop;
        private bool _pawn_bIsStopped;
        private bool _pawn_ReadyBoost, _pawn_ReadyHoist, _pawn_Grabbable, _pawn_Terminable;
        private Actor? _pawn_UsedObject;
        private SBasePawn? _pawn_Interaction, _pawn_Target, _pawn_Attacker, _pawn_Released, _pawn_Terminate;
        private bool _pawn_bBeginSpecial12Anim;
        private bool _pawn_bUseRootMotionAdditionnalPhysics;
        private byte _pawn_ZoneConfig;
        private byte _pawn_ZoneConfigForClient;
        private LedgeType _pawn_TypeLedge;
        private int _pawn_nJumpNumber;
        private int _pawn_sAction;
        private float _pawn_GroundSpeed;
        private float _pawn_CollisionRadius;
        private float _pawn_CollisionHeight;
        private FVector _pawn_PrePivot;
        private bool _pawn_bIsCrouched;
        private FRotator _pawn_Rotation;
        private float _pawn_OldRotYaw;
        private EPhysics _pawn_OldPhysics;
        private float _pawn_fBlendTimeWithC0;
        private int _pawn_ChannelForBlendWithC0;
        private Name _pawn_ForwardAnim;
        private Name _pawn_LeftAnim;
        private Name _pawn_RightAnim;
        private Name _pawn_RunAnim;
        private FRotator _pc_Rotation;
        private FRotator _pc_MyDesiredRotation;
        private float _pc_fZMaxFalling;
        private float _pc_fLastTimeOnAMover;
        private FVector _pc_vWallNormal;
        private FVector _pc_vNormalFence;
        private bool _pc_bPressedJump;
        private bool _pc_bCanStop;
        private bool _pc_bTestBackFeetGrab;
        private bool _pc_bFindGE;
        private bool _pc_bIsCharging;
        private bool _pc_bBlockControlForJump;
        private float _pc_aForward;
        private float _pc_aStrafe;
        private float _pc_aUp;
        private byte _pc_BT_Up;
        private byte _pc_BT_Down;
        private byte _pc_BT_Left;
        private byte _pc_BT_Right;
        private byte _pc_bFireMode;
        private byte _pc_bCrouch;
        private byte _pc_bRoll;
        private float _pc_MoveTimer;
        private float _pc_TimerRate, _pc_TimerCounter, _pc_LatentFloat;
        private float _pawn_TimerRate, _pawn_TimerCounter, _pawn_LatentFloat;
        private bool _pc_bTimerLoop, _pawn_bTimerLoop;
        private float _pawn_OldZ, _pawn_AvgPhysicsTime;
        private FVector _pawn_OldAcceleration, _pawn_Floor;
        private bool _pawn_bSimulateGravity, _pawn_bBelowKillZ;

        public void Capture(SBasePawn pawn, SPlayerController pc)
        {
            Tick = pawn.XLevel.TickCount; Time = pawn.XLevel.TimeSeconds; Physics = pawn.Physics;
            AuthoritativeStateTime = pc.MirrorStateLastTime;
            Position = pawn.Location; Velocity = pawn.Velocity;
            Base = pawn.Base; BasePosition = Base?.Location ?? default; BaseRotation = Base?.Rotation ?? default;
            // FinishPredictionTick refreshes the completed owner state after all actors have ticked. Keep the
            // collision inputs from the original movement call; a proxy may have moved later in that world tick.
            if (!pawn.XLevel.World.IsReplaying && _collisionTick != Tick)
            {
                Collision.Capture(pawn);
                _collisionTick = Tick;
            }
            ControllerState = pc.StateFrame; PawnState = pawn.StateFrame; Root = pawn.NetworkRootMotion.Capture();
            for (int i = 0; i < Channels.Length; i++) Channels[i] = NetworkAnimChannel.Capture(pawn.PawnAnim[i]);
            _pawn_ZipP1 = pawn.ZipP1;
            _pawn_ZipP2 = pawn.ZipP2;
            _pawn_NewZipP1 = pawn.NewZipP1;
            _pawn_NewZipP2 = pawn.NewZipP2;
            _pawn_m_geoNormal = pawn.m_geoNormal;
            _pawn_vBackFeetNormal = pawn.vBackFeetNormal;
            _pawn_m_CurrentArmsZone = pawn.m_CurrentArmsZone;
            _pawn_m_CurrentArmsRadius = pawn.m_CurrentArmsRadius;
            _pawn_m_CurrentFeetZone = pawn.m_CurrentFeetZone;
            _pawn_m_CurrentFeetRadius = pawn.m_CurrentFeetRadius;
            _pawn_vDeltaMove = pawn.vDeltaMove;
            _pawn_vVandammePos = pawn.vVandammePos;
            _pawn_vWallJumpDir = pawn.vWallJumpDir;
            _pawn_vSpeedAfterRM = pawn.vSpeedAfterRM;
            _pawn_vMoveAfterRM = pawn.vMoveAfterRM;
            _pawn_vLocationBeforeRM = pawn.vLocationBeforeRM;
            _pawn_rRotBeforeRM = pawn.rRotBeforeRM;
            _pawn_rRotAfterRM = pawn.rRotAfterRM;
            _pawn_iLockRootMotionType = pawn.iLockRootMotionType;
            _pawn_fLastRootMotionTimeCalled = pawn.fLastRootMotionTimeCalled;
            _pawn_RootMotionAdditionnalPhysics = pawn.RootMotionAdditionnalPhysics;
            _pawn_fPhysReposLinearSpeed = pawn.fPhysReposLinearSpeed;
            _pawn_bUseAdditionnalPhysicsForRepos = pawn.bUseAdditionnalPhysicsForRepos;
            _pawn_bNoReposAtEnd = pawn.bNoReposAtEnd;
            _pawn_bKeepLocalVandammePos = pawn.bKeepLocalVandammePos;
            _pawn_bReverseRM = pawn.bReverseRM;
            _pawn_bIsZipSpeed = pawn.bIsZipSpeed;
            _pawn_bIsPipe4Decal = pawn.bIsPipe4Decal;
            _pawn_bIsLeftPart = pawn.bIsLeftPart;
            _pawn_bIsRolling = pawn.bIsRolling;
            _pawn_bCanStop = pawn.bCanStop;
            _pawn_bIsStopped = pawn.bIsStopped;
            _pawn_ReadyBoost = pawn.bReadyForShortLadder; _pawn_ReadyHoist = pawn.bReadyForHoistClimb;
            _pawn_Grabbable = pawn.bCanBeGrabbed; _pawn_Terminable = pawn.bCanBeTerminated;
            _pawn_UsedObject = pawn.UsedObject; _pawn_Interaction = pawn.m_InteractionTarget;
            _pawn_Target = pawn.m_AttackTarget; _pawn_Attacker = pawn.m_AttackedBy;
            _pawn_Released = pawn.m_PawnNoMoreGrabbed; _pawn_Terminate = pawn.m_PawnToTerminate;
            _pawn_bBeginSpecial12Anim = pawn.bBeginSpecial12Anim;
            _pawn_bUseRootMotionAdditionnalPhysics = pawn.bUseRootMotionAdditionnalPhysics;
            _pawn_ZoneConfig = pawn.ZoneConfig;
            _pawn_ZoneConfigForClient = pawn.ZoneConfigForClient;
            _pawn_TypeLedge = pawn.TypeLedge;
            _pawn_nJumpNumber = pawn.nJumpNumber;
            _pawn_sAction = pawn.sAction;
            _pawn_GroundSpeed = pawn.GroundSpeed;
            _pawn_CollisionRadius = pawn.CollisionRadius;
            _pawn_CollisionHeight = pawn.CollisionHeight;
            _pawn_PrePivot = pawn.PrePivot;
            _pawn_bIsCrouched = pawn.bIsCrouched;
            _pawn_Rotation = pawn.Rotation;
            _pawn_OldRotYaw = pawn.OldRotYaw;
            _pawn_OldPhysics = pawn.OldPhysics;
            _pawn_fBlendTimeWithC0 = pawn.fBlendTimeWithC0;
            _pawn_ChannelForBlendWithC0 = pawn.ChannelForBlendWithC0;
            _pawn_ForwardAnim = pawn.ForwardAnim;
            _pawn_LeftAnim = pawn.LeftAnim;
            _pawn_RightAnim = pawn.RightAnim;
            _pawn_RunAnim = pawn.RunAnim;
            _pc_Rotation = pc.Rotation;
            _pc_MyDesiredRotation = pc.MyDesiredRotation;
            _pc_fZMaxFalling = pc.fZMaxFalling;
            _pc_fLastTimeOnAMover = pc.fLastTimeOnAMover;
            _pc_vWallNormal = pc.vWallNormal;
            _pc_vNormalFence = pc.vNormalFence;
            _pc_bPressedJump = pc.bPressedJump;
            _pc_bCanStop = pc.bCanStop;
            _pc_bTestBackFeetGrab = pc.bTestBackFeetGrab;
            _pc_bFindGE = pc.bFindGE;
            _pc_bIsCharging = pc.bIsCharging;
            _pc_bBlockControlForJump = pc.bBlockControlForJump;
            _pc_aForward = pc.aForward;
            _pc_aStrafe = pc.aStrafe;
            _pc_aUp = pc.aUp;
            _pc_BT_Up = pc.BT_Up;
            _pc_BT_Down = pc.BT_Down;
            _pc_BT_Left = pc.BT_Left;
            _pc_BT_Right = pc.BT_Right;
            _pc_bFireMode = pc.bFireMode;
            _pc_bCrouch = pc.bCrouch;
            _pc_bRoll = pc.bRoll;
            _pc_MoveTimer = pc.MoveTimer;
            _pc_TimerRate = pc.TimerRate; _pc_TimerCounter = pc.TimerCounter; _pc_bTimerLoop = pc.bTimerLoop;
            _pc_LatentFloat = pc.LatentFloat;
            _pawn_TimerRate = pawn.TimerRate; _pawn_TimerCounter = pawn.TimerCounter; _pawn_bTimerLoop = pawn.bTimerLoop;
            _pawn_LatentFloat = pawn.LatentFloat;
            _pawn_OldZ = pawn.OldZ; _pawn_AvgPhysicsTime = pawn.AvgPhysicsTime;
            _pawn_OldAcceleration = pawn.OldAcceleration; _pawn_Floor = pawn.Floor;
            _pawn_bSimulateGravity = pawn.bSimulateGravity;
            _pawn_bBelowKillZ = MovementNatives.Driver(pawn)?.GetState(pawn).bBelowKillZ ?? false;
        }

        public void Controls(SPlayerController pc)
        {
            pc.RestoreStateFrame(ControllerState);
            pc.Rotation = _pc_Rotation;
            pc.MyDesiredRotation = _pc_MyDesiredRotation;
            pc.fZMaxFalling = _pc_fZMaxFalling;
            pc.fLastTimeOnAMover = _pc_fLastTimeOnAMover;
            pc.vWallNormal = _pc_vWallNormal;
            pc.vNormalFence = _pc_vNormalFence;
            pc.bPressedJump = _pc_bPressedJump;
            pc.bCanStop = _pc_bCanStop;
            pc.bTestBackFeetGrab = _pc_bTestBackFeetGrab;
            pc.bFindGE = _pc_bFindGE;
            pc.bIsCharging = _pc_bIsCharging;
            pc.bBlockControlForJump = _pc_bBlockControlForJump;
            pc.aForward = _pc_aForward;
            pc.aStrafe = _pc_aStrafe;
            pc.aUp = _pc_aUp;
            pc.BT_Up = _pc_BT_Up;
            pc.BT_Down = _pc_BT_Down;
            pc.BT_Left = _pc_BT_Left;
            pc.BT_Right = _pc_BT_Right;
            pc.bFireMode = _pc_bFireMode;
            pc.bCrouch = _pc_bCrouch;
            pc.bRoll = _pc_bRoll;
            // The world already advanced these timers at each recorded input boundary. Restore their cursor;
            // invoking timer callbacks again during movement replay would repeat gameplay or recovery effects.
            pc.TimerRate = _pc_TimerRate; pc.TimerCounter = _pc_TimerCounter; pc.bTimerLoop = _pc_bTimerLoop;
            pc.LatentFloat = _pc_LatentFloat;
            if (pc.Pawn is SBasePawn pawn)
            {
                pawn.TimerRate = _pawn_TimerRate; pawn.TimerCounter = _pawn_TimerCounter; pawn.bTimerLoop = _pawn_bTimerLoop;
                pawn.LatentFloat = _pawn_LatentFloat;
            }
        }

        public void Context(SBasePawn pawn)
        {
            pawn.RestoreStateFrame(PawnState);
            pawn.SetBase(Base, bNotifyActor: false);
            pawn.ZipP1 = _pawn_ZipP1;
            pawn.ZipP2 = _pawn_ZipP2;
            pawn.NewZipP1 = _pawn_NewZipP1;
            pawn.NewZipP2 = _pawn_NewZipP2;
            pawn.m_geoNormal = _pawn_m_geoNormal;
            pawn.vBackFeetNormal = _pawn_vBackFeetNormal;
            pawn.m_CurrentArmsZone = _pawn_m_CurrentArmsZone;
            pawn.m_CurrentArmsRadius = _pawn_m_CurrentArmsRadius;
            pawn.m_CurrentFeetZone = _pawn_m_CurrentFeetZone;
            pawn.m_CurrentFeetRadius = _pawn_m_CurrentFeetRadius;
            pawn.vDeltaMove = _pawn_vDeltaMove;
            pawn.vVandammePos = _pawn_vVandammePos;
            pawn.vWallJumpDir = _pawn_vWallJumpDir;
            pawn.vSpeedAfterRM = _pawn_vSpeedAfterRM;
            pawn.vMoveAfterRM = _pawn_vMoveAfterRM;
            pawn.vLocationBeforeRM = _pawn_vLocationBeforeRM;
            pawn.rRotBeforeRM = _pawn_rRotBeforeRM;
            pawn.rRotAfterRM = _pawn_rRotAfterRM;
            pawn.iLockRootMotionType = _pawn_iLockRootMotionType;
            pawn.fLastRootMotionTimeCalled = _pawn_fLastRootMotionTimeCalled;
            pawn.RootMotionAdditionnalPhysics = _pawn_RootMotionAdditionnalPhysics;
            pawn.fPhysReposLinearSpeed = _pawn_fPhysReposLinearSpeed;
            pawn.bUseAdditionnalPhysicsForRepos = _pawn_bUseAdditionnalPhysicsForRepos;
            pawn.bNoReposAtEnd = _pawn_bNoReposAtEnd;
            pawn.bKeepLocalVandammePos = _pawn_bKeepLocalVandammePos;
            pawn.bReverseRM = _pawn_bReverseRM;
            pawn.bIsZipSpeed = _pawn_bIsZipSpeed;
            pawn.bIsPipe4Decal = _pawn_bIsPipe4Decal;
            pawn.bIsLeftPart = _pawn_bIsLeftPart;
            pawn.bIsRolling = _pawn_bIsRolling;
            pawn.bCanStop = _pawn_bCanStop;
            pawn.bIsStopped = _pawn_bIsStopped;
            pawn.bReadyForShortLadder = _pawn_ReadyBoost; pawn.bReadyForHoistClimb = _pawn_ReadyHoist;
            pawn.bCanBeGrabbed = _pawn_Grabbable; pawn.bCanBeTerminated = _pawn_Terminable;
            pawn.UsedObject = _pawn_UsedObject is { bDeleteMe: false } ? _pawn_UsedObject : null;
            pawn.m_InteractionTarget = _pawn_Interaction is { bDeleteMe: false } ? _pawn_Interaction : null;
            pawn.m_AttackTarget = _pawn_Target is { bDeleteMe: false } ? _pawn_Target : null;
            pawn.m_AttackedBy = _pawn_Attacker is { bDeleteMe: false } ? _pawn_Attacker : null;
            pawn.m_PawnNoMoreGrabbed = _pawn_Released is { bDeleteMe: false } ? _pawn_Released : null;
            pawn.m_PawnToTerminate = _pawn_Terminate is { bDeleteMe: false } ? _pawn_Terminate : null;
            pawn.bBeginSpecial12Anim = _pawn_bBeginSpecial12Anim;
            pawn.bUseRootMotionAdditionnalPhysics = _pawn_bUseRootMotionAdditionnalPhysics;
            pawn.ZoneConfig = _pawn_ZoneConfig;
            pawn.ZoneConfigForClient = _pawn_ZoneConfigForClient;
            pawn.TypeLedge = _pawn_TypeLedge;
            pawn.nJumpNumber = _pawn_nJumpNumber;
            pawn.sAction = _pawn_sAction;
            pawn.GroundSpeed = _pawn_GroundSpeed;
            pawn.CollisionRadius = _pawn_CollisionRadius;
            pawn.CollisionHeight = _pawn_CollisionHeight;
            pawn.PrePivot = _pawn_PrePivot;
            pawn.bIsCrouched = _pawn_bIsCrouched;
            pawn.Rotation = _pawn_Rotation;
            pawn.OldRotYaw = _pawn_OldRotYaw;
            pawn.OldPhysics = _pawn_OldPhysics;
            pawn.fBlendTimeWithC0 = _pawn_fBlendTimeWithC0;
            pawn.ChannelForBlendWithC0 = _pawn_ChannelForBlendWithC0;
            pawn.ForwardAnim = _pawn_ForwardAnim;
            pawn.LeftAnim = _pawn_LeftAnim;
            pawn.RightAnim = _pawn_RightAnim;
            pawn.RunAnim = _pawn_RunAnim;
            for (int i = 0; i < Channels.Length; i++) Channels[i].Apply(pawn, i, presentationOnly: false);
            pawn.NetworkRootMotion.Restore(Root);
            pawn.Physics = Physics;
            pawn.OldZ = _pawn_OldZ; pawn.AvgPhysicsTime = _pawn_AvgPhysicsTime;
            pawn.OldAcceleration = _pawn_OldAcceleration; pawn.Floor = _pawn_Floor;
            pawn.bSimulateGravity = _pawn_bSimulateGravity;
            if (pawn.Controller is SPlayerController pc) pc.MoveTimer = _pc_MoveTimer;
            if (MovementNatives.Driver(pawn) is { } driver) driver.GetState(pawn).bBelowKillZ = _pawn_bBelowKillZ;
        }

        public void MovementOrientation(SBasePawn pawn)
        {
            // PlayerMove turns the body before MoveAutonomous. That orientation is part of the input, and
            // replaying acceleration alone leaves an idle spy facing an older path segment after corrections.
            // Root-driven turns remain driven by their restored lock and clip, rather than applied twice.
            if (Physics is EPhysics.PHYS_Walking or EPhysics.PHYS_Falling)
            {
                pawn.Rotation = _pawn_Rotation;
                pawn.GroundSpeed = _pawn_GroundSpeed;
            }
        }
    }

    public void CapturePrediction(int slot)
    {
        if (P is not { } pawn) return;
        if (_prediction is null)
        {
            _prediction = new PredictionFrame[SequencedMoveTransport.Capacity];
            for (int i = 0; i < _prediction.Length; i++) _prediction[i] = new PredictionFrame();
        }
        PredictionFrame frame = _prediction[slot];
        long tick = frame.Tick;
        float time = frame.Time;
        frame.Capture(pawn, _pc);
        frame.AuthoritativeOwnerAction = _authoritativeOwnerAction;
        if (pawn.XLevel.World.IsReplaying)
        {
            frame.Tick = tick;
            frame.Time = time;
        }
    }

    public void BeginPredictionReplay()
    {
        _wasReplaying = _pc.XLevel.World.IsReplaying;
        _pc.XLevel.World.IsReplaying = true;
    }

    public bool PredictionHistoryComplete(int slot) => _prediction is not null && _prediction[slot].Collision.Complete;

    public void RebaseAuthoritativeState(int slot, Actor? @base, FVector basePosition, FRotator baseRotation,
        ref FVector position, ref FVector velocity)
    {
        if (_prediction is null || @base is null || !ReferenceEquals(_prediction[slot].Base, @base)) return;
        PredictionFrame frame = _prediction[slot];
        position = TransformVector(position - basePosition, baseRotation, frame.BaseRotation) + frame.BasePosition;
        velocity = TransformVector(velocity, baseRotation, frame.BaseRotation);
    }

    private static FVector TransformVector(FVector vector, FRotator from, FRotator to)
    {
        if (from == to) return vector;
        FCoords oldAxes = FCoords.FromRotator(from), newAxes = FCoords.FromRotator(to);
        return newAxes.XAxis * (vector | oldAxes.XAxis) + newAxes.YAxis * (vector | oldAxes.YAxis) +
            newAxes.ZAxis * (vector | oldAxes.ZAxis);
    }

    private void StageCollision(SBasePawn pawn, PredictionFrame frame)
    {
        _liveCollision.Apply();
        _liveCollision.Capture(pawn);
        frame.Collision.Apply();
        MovementNatives.Driver(pawn)?.Collision.NotifyCollisionChanged();
    }

    // A reliable authoritative action can arrive after the acknowledged input was simulated. Its state entry
    // and animation must survive older corrections; the next acknowledgement has history under the new state.
    public bool CanReplayPrediction(int slot) => _prediction is not null &&
        _prediction[slot].AuthoritativeStateTime >= _pc.MirrorStateLastTime &&
        _prediction[slot].AuthoritativeOwnerAction >= _authoritativeOwnerAction;

    public void RestorePrediction(int slot, FVector location, FVector velocity, EPhysics physics, Actor? @base, byte zone)
    {
        if (P is not { } pawn || _prediction is null) return;
        PredictionFrame frame = _prediction[slot];
        StageCollision(pawn, frame);
        frame.Controls(_pc); frame.Context(pawn);
        _replayExpectedPhysics = frame.Physics;
        _replayReference = frame.Position; _replayAction = frame.Action;
        _replayBase = frame.Base; _replayBasePosition = frame.BasePosition; _replayBaseRotation = frame.BaseRotation;
        pawn.ZoneConfigForClient = zone;
        if (pawn.ZoneConfig != zone)
        {
            ProxyAnimation.ApplyZoneConfig(pawn);
            if (zone <= 1) pawn.bIsCrouched = zone == 1;
        }
        if (physics != pawn.Physics) MovementNatives.SetPhysics(pawn, physics);
        MovementNatives.FarMoveActor(pawn, location);
        pawn.SetBase(@base, bNotifyActor: false);
        pawn.ZoneConfig = pawn.ZoneConfigForClient = zone;
        pawn.Velocity = velocity;
        pawn.vLocationBeforeRM += location - frame.Position;
    }

    public void ReplayPrediction(int slot, float deltaTime, FVector acceleration, bool jump)
    {
        if (P is not { } pawn || _prediction is null) return;
        PredictionFrame frame = _prediction[slot];
        using var time = pawn.XLevel.ReplayTime(frame.Tick, frame.Time, deltaTime);
        StageCollision(pawn, frame);
        frame.Controls(_pc);
        if (frame.Physics != _replayExpectedPhysics || frame.Action != _replayAction)
        {
            // A state entry already ran its action and physics in this recorded tick. Restore that completed
            // transition once; advancing its root again here would skip the first frame of a climb or mount.
            FVector prior = pawn.Location;
            frame.Context(pawn);
            MovePawn(frame.Position - _replayReference);
            pawn.Velocity = frame.Velocity;
            pawn.vLocationBeforeRM += prior - _replayReference;
            _replayExpectedPhysics = frame.Physics; _replayReference = frame.Position; _replayAction = frame.Action;
            _replayBase = frame.Base; _replayBasePosition = frame.BasePosition; _replayBaseRotation = frame.BaseRotation;
            return;
        }
        // DeviceNatives moves a mover's riders outside their MoveAutonomous step. Reproduce that displacement
        // between input ticks, including rotation about the mover, without advancing the mover or its other riders.
        if (_replayBase is { bIsAMover: true, bDeleteMe: false } movingBase &&
            ReferenceEquals(movingBase, frame.Base) && ReferenceEquals(movingBase, pawn.Base))
            CarryReplayPawn(pawn, movingBase, frame.BasePosition, frame.BaseRotation);
        _replayBase = frame.Base; _replayBasePosition = frame.BasePosition; _replayBaseRotation = frame.BaseRotation;
        _replayExpectedPhysics = frame.Physics;
        _replayReference = frame.Position; _replayAction = frame.Action;
        frame.MovementOrientation(pawn);
        MoveAutonomous(deltaTime, acceleration, jump);
        pawn.AdvancePredictionAnimation(deltaTime);
        pawn.UpdateMovementAnimation(deltaTime);
    }

    private void CarryReplayPawn(SBasePawn pawn, Actor basis, FVector position, FRotator rotation)
    {
        FVector target = position + TransformVector(pawn.Location - _replayBasePosition, _replayBaseRotation, rotation);
        FRotator turned = pawn.Rotation;
        turned.Yaw += rotation.Yaw - _replayBaseRotation.Yaw;
        if (MovementNatives.Driver(pawn) is { } driver)
            driver.MoveActor(pawn, target - pawn.Location, turned, out _, Ignore: basis);
        else { pawn.Location = target; pawn.Rotation = turned; }
    }

    public void EndPredictionReplay(FVector previousLocation)
    {
        _liveCollision.Apply();
        _liveCollision.Clear();
        SBasePawn? pawn = P;
        try
        {
            if (pawn is not null)
            {
                MovementNatives.Driver(pawn)?.Collision.NotifyCollisionChanged();
                if (_replayBase is { bIsAMover: true, bDeleteMe: false } basis && ReferenceEquals(pawn.Base, basis))
                    CarryReplayPawn(pawn, basis, basis.Location, basis.Rotation);
            }
        }
        finally { _pc.XLevel.World.IsReplaying = _wasReplaying; }
        if (pawn is null) return;
        // A correction can land the collision body before our predicted falling physics reaches the floor.
        // Restoring PHYS_Walking bypasses processLanded, while replay restores the old airborne controller.
        // Complete that transition once at the resulting present state, outside replay, so the normal landing
        // animation and reliable owner-to-server state change run (including when no pending input remains).
        // No part of the current physics step has run here. Leave its full delta remaining: the landing
        // callback splits movement at this boundary and must not charge another full step to the input tick.
        if (!_wasReplaying && pawn.Physics == EPhysics.PHYS_Walking && _pc.StateNode is SPlayerController.s_PlayerJump)
            _pc.eventMyNotifyLanded(pawn.Floor.Z > 0.7f ? pawn.Floor : new FVector(0, 0, 1), _pc.XLevel.DeltaTime, pawn.Base);
        // Replay preserves the live touch set. Reconcile it once at the resulting present position, so crossing
        // a trigger in historical inputs cannot duplicate an interaction or remove a current menu entry.
        if (!_wasReplaying) MovementNatives.Driver(pawn)?.EncroachTouches(pawn);
        FVector correction = previousLocation - pawn.Location;
        if (correction.SizeSquared() > 40000f)
        {
            pawn.NetworkCorrectionOffset = default; pawn.NetworkCorrectionSeconds = 0f;
        }
        else
        {
            pawn.NetworkCorrectionOffset += correction; pawn.NetworkCorrectionSeconds = 0.1f;
        }
    }
}
