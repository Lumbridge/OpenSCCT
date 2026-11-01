using System;
using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Net.Moves;
using OpenScct.Physics.Collision;
using OpenScct.Physics.Movement;
using OpenScct.Provenance;

namespace OpenScct.Server;

/// <summary>
/// The move protocol's view of an SBasePawn and its SPlayerController: the generated fields the protocol reads, the
/// controller's ScriptMyProcessMove and the level's physics for MoveAutonomous, and the physics driver's sweeps for
/// the correction and the big move snap.
/// </summary>
public sealed partial class SBasePawnMoves : IMovePawn, IMovePrediction, IMoveCommandPawn
{
    public bool CommandDriven { get; set; }
    public bool ModernCommands { get; set; }
    public bool CommandSimulationEnabled => P is { m_AttackedBy: null } pawn && !IsCorpse(_pc, pawn) &&
        !_pc.bGameEnded && _pc.StateNode is not SPlayerController.s_Inactive;

    // The reliable death state can arrive before the independently replicated Health property.
    // PlayerFallingKO inherits the inert-fall implementation but remains a living, predicted pawn.
    internal static bool IsCorpse(SPlayerController pc, SBasePawn pawn) => pawn.Health <= 0 ||
        pc.StateNode is SPlayerController.s_Dead or SPlayerController.s_LandingInerte ||
        (pc.StateNode is SPlayerController.PlayerFallingInerte and not SPlayerController.PlayerFallingKO);
    private float _commandTime;
    private long _commandTick;
    public void SetCommandTime(float stamp) { _commandTime = stamp; _commandTick = (long)Math.Round(stamp * Sim.Hz); }
    public void SetCommandTime(float stamp, long inputTick) { _commandTime = stamp; _commandTick = inputTick; }
    private OpenScct.Game.Animation.INetworkPawnActions? _commandActions;
    private SBasePawn? _previousInputOwner;
    public void BeginMoveCommand()
    {
        _commandActions = _pc.XLevel.GetService<OpenScct.Game.Animation.INetworkPawnActions>();
        if (_commandActions is null) return;
        _previousInputOwner = _commandActions.InputOwner;
        _commandActions.InputOwner = P;
    }
    public void EndMoveCommand()
    {
        if (_commandActions is not null) _commandActions.InputOwner = _previousInputOwner;
        _commandActions = null; _previousInputOwner = null;
    }
    private readonly SPlayerController _pc;

    public SBasePawnMoves(SPlayerController pc)
    {
        _pc = pc ?? throw new ArgumentNullException(nameof(pc));
    }

    private SBasePawn? P => _pc.Pawn as SBasePawn;

    /// <summary>MovePawn teleports when its sweep stops more than 50 uu short (the squared distance).</summary>
    [Provenance(ProvenanceKind.Decoded, "editor 0x111fe4a0: 2500.0 < |Dest - Location|^2")]
    private const float MovePawnTeleportSq = 2500f;

    public FVector Location => P?.Location ?? default;

    public FVector Velocity
    {
        get => P?.Velocity ?? default;
        set
        {
            if (P is { } p)
            {
                p.Velocity = value;
            }
        }
    }

    public FRotator Rotation
    {
        get => P?.Rotation ?? default;
        set
        {
            if (P is { } p)
            {
                p.Rotation = value;
            }
        }
    }

    public EPhysics Physics => P?.Physics ?? EPhysics.PHYS_None;

    public void SetPhysics(EPhysics physics)
    {
        if (P is { } p)
        {
            p.Physics = physics;
        }
    }

    public Actor? Base => P?.Base;

    public bool IsAlive => P is { Health: > 0 };

    public byte ZoneConfig => P?.ZoneConfig ?? 0;

    public float ZCDecal(int from, int to)
    {
        int i = from * 4 + to;
        return P is { } p && (uint)i < (uint)p.ZCDecal.Length ? p.ZCDecal[i] : 0f;
    }

    public bool IsMerc => (int)_pc.SPawnType == 1;

    public float RunningSpeed => P?.RunningSpeed ?? 0f;

    public float GroundSpeed
    {
        get => P?.GroundSpeed ?? 0f;
        set
        {
            if (P is { } p)
            {
                p.GroundSpeed = value;
            }
        }
    }

    public bool bIsRolling => P?.bIsRolling ?? false;

    public bool bIsCharging => _pc.bIsCharging;

    public bool AdrenalineActive => _pc.AdrenalineTime + _pc.fTimeWhenHit >= _pc.Level.TimeSeconds;

    public float AdrenalineFactor => _pc.AdrenalineFactor;

    /// <summary>Vandamme, the four BTW fire values, or PipH4/PipH2 fire (net-moves.md rule 3.5).</summary>
    public bool UsesPCRot => P is { } p && (p.Physics == EPhysics.PHYS_Vandamme ||
        p.BTW is BTW_.BTWFireStandLeft or BTW_.BTWFireStandRight or BTW_.BTWFireCrouchLeft or BTW_.BTWFireCrouchRight ||
        p.PipHFire is PipHFire_.PipH4 or PipHFire_.PipH2);

    public bool bTestBackFeetGrab => _pc.bTestBackFeetGrab;

    // A dead/results pawn can still author a root-motion death action. Modern commands no longer
    // simulate that pawn, so leave its action for the reliable SendActions RPC instead of consuming
    // it into a movement command that the authority will discard.
    public bool ActionsWaitForBigMove => (!ModernCommands || CommandSimulationEnabled) &&
        P is { bActionsSendByServerMove: true, iNbAction: > 0 };

    public int TakeActionsForBigMove(Span<int> actions)
    {
        if (P is not { } p)
        {
            return 0;
        }

        int end = Math.Clamp(p.iNbActionInTable, 0, Math.Min(6, p.AllActions.Length));
        int count = ModernCommands ? Math.Min(end, p.iNbAction) : end;
        int first = end - count;
        for (int i = 0; i < count && i < actions.Length; i++)
        {
            actions[i] = p.AllActions[first + i];
        }

        p.iNbActionsSent = count;
        p.bActionsHasBeenSentByServerMove = true;
        p.iNbAction = 0;
        return count;
    }

    /// <summary>NativeExecuteReceivedActionForServer: the pair's actions run through the pawn's action code.</summary>
    public void ExecuteReceivedActions(int val0, int val1)
    {
        if (P is not { } p)
        {
            return;
        }

        Span<int> actions = stackalloc int[6];
        int count = MovePacking.UnCompressActions(val0, val1, actions);
        OpenScct.Game.Animation.ProxyAnimation.ExecuteReceivedActionsForServer(p, actions, count);
    }

    public int iYawPitchFromView
    {
        get => P?.iYawPitchFromView ?? 0;
        set
        {
            if (P is { } p)
            {
                p.iYawPitchFromView = value;
            }
        }
    }

    // The big move resend's traversal points (the only writer of these setters) arrive rounded: the server takes the
    // GE point each was rounded from (GEPointCheck).
    public FVector ZipP1 { get => P?.ZipP1 ?? default; set => Set(p => p.ZipP1 = GEPointCheck.Exact(p, value)); }

    public FVector ZipP2 { get => P?.ZipP2 ?? default; set => Set(p => p.ZipP2 = GEPointCheck.Exact(p, value)); }

    public FVector NewZipP1 { get => P?.NewZipP1 ?? default; set => Set(p => p.NewZipP1 = GEPointCheck.Exact(p, value)); }

    public FVector NewZipP2 { get => P?.NewZipP2 ?? default; set => Set(p => p.NewZipP2 = GEPointCheck.Exact(p, value)); }

    public FVector vVandammePos { get => P?.vVandammePos ?? default; set => Set(p => p.vVandammePos = value); }

    public FVector vWallJumpDir { get => P?.vWallJumpDir ?? default; set => Set(p => p.vWallJumpDir = value); }

    public FVector m_geoNormal { get => P?.m_geoNormal ?? default; set => Set(p => p.m_geoNormal = value); }

    public bool bKeepLocalVandammePos => P?.bKeepLocalVandammePos ?? false;

    private void Set(Action<SBasePawn> set)
    {
        if (P is { } p)
        {
            set(p);
        }
    }

    /// <summary>
    /// MoveAutonomous (editor 0x111ef3c0): ScriptMyProcessMove sets the pawn's acceleration, which is truncated toward
    /// zero to whole units, then the pawn's physics runs for <paramref name="deltaTime"/>.
    /// </summary>
    [UcNative("ASPlayerController::MoveAutonomous", Editor = 0x111ef3c0)]
    public void MoveAutonomous(float deltaTime, FVector acceleration, bool jump)
    {
        if (P is not { } p)
        {
            return;
        }

        using var clock = CommandDriven ? p.XLevel.CommandTime(_commandTick, _commandTime, deltaTime) : default;
        // Traversal RPCs were received on the host clock. Their next-physics guard must not hold older queued
        // input ticks until that wall-clock receive time; each sequenced command is already executed once.
        if (CommandDriven && p.fLastRootMotionTimeCalled > _commandTime) p.fLastRootMotionTimeCalled = _commandTime;
        _pc.bPressedJump = jump || _pc.bPressedJump;
        _pc.ScriptMyProcessMove(deltaTime, acceleration);
        FVector a = p.Acceleration;
        p.Acceleration = new FVector(SvmMath.Trunc(a.X), SvmMath.Trunc(a.Y), SvmMath.Trunc(a.Z));
        p.XLevel.PhysicsDriver?.performPhysics(p, deltaTime);
        if (CommandDriven) AdvanceMoveAnimation(deltaTime);
    }

    public void ConsumeMoveRootMotion()
    {
        if (P is not { Physics: EPhysics.PHYS_RootMotion } pawn) return;
        PawnRootMotion root = pawn.NetworkRootMotion;
        if (root.RootLockType == 0) root.LockRootMotion(pawn.iLockRootMotionType);
        root.AdvanceRootMotion(out _, out _);
        pawn.fLastRootMotionTimeCalled = _commandTime;
    }

    public void AdvanceMoveAnimation(float deltaTime)
    {
        if (P is not { } pawn) return;
        using var clock = CommandDriven ? pawn.XLevel.CommandTime(_commandTick, _commandTime, deltaTime) : default;
        pawn.AdvancePredictionAnimation(deltaTime);
        pawn.UpdateMovementAnimation(deltaTime);
        pawn.OldPhysics = pawn.Physics;
    }

    /// <summary>
    /// MovePawn (native 1402): a sweep by <paramref name="delta"/>; when it is blocked more than 50 uu short, a
    /// checked teleport to the target (FarMoveActor with FindSpot and the encroachment test, which refuses a spot inside
    /// a wall or another pawn and leaves the pawn where the sweep stopped).
    /// </summary>
    [UcNative("ASBasePawn::execMovePawn", Exe = 0x10ae2da0, Editor = 0x111fe4a0)]
    [Provenance(ProvenanceKind.Decoded, "net-moves.md rule 16; editor 0x111fe4a0: MoveActor, then when Hit.Time < 1 and the miss is over 50 uu (squared 2500) FarMoveActor(Dest, bTest 0, bNoCheck 0, bAttachedMove 0)")]
    public void MovePawn(FVector delta)
    {
        if (P is not { } p)
        {
            return;
        }

        FVector target = p.Location + delta;
        if (p.XLevel.PhysicsDriver is PhysicsDriver driver)
        {
            driver.MoveActor(p, delta, p.Rotation, out FCheckResult hit);
            if (hit.Time < 1f && (target - p.Location).SizeSquared() > MovePawnTeleportSq)
            {
                driver.FarMoveActor(p, target);
            }
        }
        else
        {
            p.Location = target;
        }
    }

    /// <summary>The big move snap: sweep, then horizontally, then once more with the full offset (net-moves.md rule 13).</summary>
    public void SnapTo(FVector location)
    {
        if (P is not { } p)
        {
            return;
        }

        if (p.XLevel.PhysicsDriver is not PhysicsDriver driver)
        {
            p.Location = location;
            return;
        }

        FVector delta = location - p.Location;
        if (driver.MoveActor(p, delta, p.Rotation, out FCheckResult _) && (location - p.Location).SizeSquared() < 1f)
        {
            return;
        }

        FVector rest = location - p.Location;
        driver.MoveActor(p, new FVector(rest.X, rest.Y, 0f), p.Rotation, out FCheckResult _);
        // The third, full sweep is skipped when the base is a mover that moved in the last 3 s (fLastTimeMoverMoves,
        // game 0x10aae660 and 0x10ab1670); the original's other skip, falling within 3 s of fLastTimeOnAMover, is dead
        // (no writer).
        if (!BaseMoverMovedVertically(MoveK.MoverWindow))
        {
            driver.MoveActor(p, location - p.Location, p.Rotation, out FCheckResult _);
        }
    }

    /// <summary>
    /// ReinitBecauseOfDivergences (native 1670): the movement state back to walking through setPhysics, so a pawn
    /// cut out of a root-motion move has its root unlocked (AActor::setPhysics) and the next move locks afresh (the
    /// action reset belongs to the pawn port).
    /// </summary>
    [Provenance(ProvenanceKind.Estimated, "net-moves.md rule 17: only physics walking here until the pawn port has the reset")]
    [Provenance(ProvenanceKind.Decoded, "game 0x10ae5000 (body of exec 0x10ae5130): the pawn's setPhysics (vtable 0xfc) with PHYS_Walking and no floor")]
    public void ReinitBecauseOfDivergences()
    {
        if (P is { } p)
        {
            MovementNatives.SetPhysics(p, EPhysics.PHYS_Walking);
            p.bIsRolling = false;
            p.NetworkCorrectionOffset = default; p.NetworkCorrectionSeconds = 0f;
            p.ResetNetworkHistory();
        }
    }

    public bool BaseMoverMovedVertically(float seconds) =>
        P?.Base is Mover m && _pc.Level.TimeSeconds - m.fLastTimeMoverMoves < seconds;

    public bool BaseMoverMovingVertically => P?.Base is Mover m && m.Velocity.Z != 0f;

    public bool bUseRootMotionAdditionnalPhysics => P?.bUseRootMotionAdditionnalPhysics ?? false;

    public void Place(FVector location, Actor? newBase, FRotator rotation)
    {
        if (P is not { } p)
        {
            return;
        }

        if (p.XLevel.PhysicsDriver is PhysicsDriver driver)
        {
            driver.FarMoveActor(p, location, bNoCheck: true);
        }
        else
        {
            p.Location = location;
        }

        p.SetBase(newBase);
        p.Rotation = rotation;
    }
}

/// <summary>The move protocol's view of an SPlayerController.</summary>
public sealed class SPlayerControllerMoves : IMoveController, IMoveControllerTimeStamp, IMoveControllerCurrentTimeStamp
{
    private readonly SPlayerController _pc;

    public SPlayerControllerMoves(SPlayerController pc)
    {
        _pc = pc ?? throw new ArgumentNullException(nameof(pc));
    }

    public ServerMoves? Server { get; set; }

    public ClientMoves? Client { get; set; }

    public FRotator Rotation
    {
        get => _pc.Rotation;
        set => _pc.Rotation = value;
    }

    /// <summary>The controller's own ServerTimeStamp (the engine tick's catch-up reads and writes it).</summary>
    public float ServerTimeStamp
    {
        get => _pc.ServerTimeStamp;
        set => _pc.ServerTimeStamp = value;
    }

    /// <summary>The controller's own CurrentTimeStamp on the server (the pawn's server tick advances it).</summary>
    public float CurrentTimeStamp
    {
        get => _pc.CurrentTimeStamp;
        set => _pc.CurrentTimeStamp = value;
    }

    public bool bPressedJump => _pc.bPressedJump;

    public float TimeSeconds => _pc.Level.TimeSeconds;

    public bool IsPaused => _pc.Level.Pauser is not null;

    public float MirrorStateAcknowledgementTime
    {
        get => _pc.MirrorStateAcknowledgementTime;
        set => _pc.MirrorStateAcknowledgementTime = value;
    }

    public bool bWaitingForReinit
    {
        get => _pc.bWaitingForReinit;
        set => _pc.bWaitingForReinit = value;
    }

    public bool bUseRootMotionAdditionnalPhysics => _pc.bUseRootMotionAdditionnalPhysics;

    /// <summary>
    /// Until the controller port has ReinitPawnBecauseOfDivergences, the normal case runs for a living pawn (dead,
    /// grabbed and lying pawns are left alone as state-sync.md rule 12 says).
    /// </summary>
    [Provenance(ProvenanceKind.Estimated, "state-sync.md rule 12 case 1 (MirrorState s_Dead) is left out: a dead pawn is not reinitialised")]
    [Provenance(ProvenanceKind.Decoded, "state-sync.md rule 12 case 2: a grab or a knock-down is left alone")]
    public void ReinitPawnBecauseOfDivergences()
    {
        if (_pc.Pawn is not SBasePawn { Health: > 0 } pawn)
        {
            return;
        }

        // Case 2: a merc held by a spy, a spy holding or attacking someone, a pawn on the ground.
        bool merc = _pc.SPawnType == PawnType.SPAWNDEFENSE;
        if ((merc && pawn.m_AttackedBy is not null) || (!merc && pawn.m_AttackTarget is not null) || pawn.bLyingOnGround)
        {
            return;
        }

        Server?.ReinitPawn();
        pawn.DistantClientReinit(0);
    }

    /// <summary>The controller's s_ClientIsReinit stand-in: answers at once (the original repeats it every second).</summary>
    public void ClientReinitDone(byte reinitType) => Client?.Transport.TellServerIAMReadyToReinit();

    /// <summary>
    /// s_WaitForClientToReinit.TellServerIAMReadyToReinit (state-sync.md rule 15): the wait ends and the server mirrors
    /// the side's crouched walking state under the new epoch, so the two copies' states agree again (the client's
    /// state changes sent before the reinit carry the old epoch and are refused). The client leaves s_ClientIsReinit
    /// on that state change and its EndState runs GotoWaitingState (rule 14), which picks the idle state for the
    /// pawn's stance and announces it back; until the client port has s_ClientIsReinit, the server asks for that
    /// GotoWaitingState itself (rule 6), right after the mirrored state.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "state-sync.md rule 15: MirrorState s_PlayerWalkingCr0H for a spy, s_PlayerWalkingCr2H for a merc")]
    [Provenance(ProvenanceKind.Estimated, "state-sync.md rule 14: s_ClientIsReinit.EndState's GotoWaitingState asked by the server (the client state is not ported)")]
    public void ServerReinitReady()
    {
        if (!_pc.bWaitingForReinit)
        {
            return;
        }

        _pc.bWaitingForReinit = false;
        _pc.eventMirrorState(_pc.SPawnType == PawnType.SPAWNDEFENSE ? MovementStateNames.s_PlayerWalkingCr2H : MovementStateNames.s_PlayerWalkingCr0H);
        _pc.GotoWaitingState();
    }
}

/// <summary>
/// The movement port's network calls (<see cref="IMovementNet"/>) on a networked level: MyReplicateMove runs the
/// client half of the move protocol for an autonomous controller (and the controller's own process move
/// elsewhere), the traversal helpers and action packets go as the controller's replicated functions, and received
/// move functions reach the controller's protocol objects. Install one per level with <see cref="Install"/>.
/// </summary>
public sealed partial class NetMovementBridge : IMovementNet, IMoveEndpointSource, OpenScct.Game.Animation.INetworkPawnCommands
{
    private readonly NetDriver _driver;
    private readonly NetCallArgs _args = new();
    private readonly Dictionary<Actor, MoveEndpoint> _endpoints = new();
    private readonly List<Actor> _deadEndpoints = new();
    private readonly int[] _actions = new int[6];

    public NetMovementBridge(NetDriver driver)
    {
        _driver = driver ?? throw new ArgumentNullException(nameof(driver));
    }

    /// <summary>Installs the bridge on <paramref name="level"/> and routes the move functions <paramref name="driver"/> receives.</summary>
    public static NetMovementBridge Install(Level level, NetDriver driver)
    {
        var bridge = new NetMovementBridge(driver);
        level.SetService<IMovementNet>(bridge);
        level.SetService<IMoveEndpointSource>(bridge);
        level.SetService<OpenScct.Game.Animation.INetworkPawnCommands>(bridge);
        level.SetService(bridge);
        if (driver.IsServer) level.SetService<OpenScct.Game.Animation.INetworkPawnActions>(bridge);
        NetMoveTransport.Register(driver, bridge.EndpointOf);
        if (driver.IsServer)
        {
            MovementServerFunctions.Register(driver, bridge.EndpointOf);
            driver.DeferReceivedFunction = bridge.DeferMovementCall;
        }

        return bridge;
    }

    /// <summary>The protocol objects of <paramref name="controller"/>, created on first use (null for other actors).</summary>
    public MoveEndpoint? EndpointOf(Actor controller)
    {
        if (controller is not SPlayerController pc)
        {
            return null;
        }

        if (_endpoints.TryGetValue(pc, out MoveEndpoint? endpoint))
        {
            NoteNewPawn(pc, endpoint);
            return endpoint;
        }

        var pawn = new SBasePawnMoves(pc);
        var adapter = new SPlayerControllerMoves(pc);
        IMoveTransport transport = new NetMoveTransport(_driver, pc);
        bool modern = _driver.IsServer ? _driver.Settings.PawnProxies == PawnProxyMode.Snapshots
            : pc.XLevel.GetService<ClientSession>()?.PawnProxies == PawnProxyMode.Snapshots;
        SequencedMoveTransport? sequenced = modern ? new SequencedMoveTransport(_driver, pc, pawn, adapter, transport) : null;
        transport = sequenced ?? transport;
        pawn.CommandDriven = modern && _driver.IsServer;
        pawn.ModernCommands = modern;
        if (sequenced is not null && _driver.IsServer)
        {
            sequenced.ApplyControls = sequence => ApplyMovementCalls(pc, sequence);
            sequenced.PredictionReset = () => ResetMovementCalls(pc);
        }
        if (_driver.IsServer)
        {
            var server = new ServerMoves(pawn, adapter, transport)
            {
                ClientNetSpeed = _driver.OwningConnection(pc)?.CurrentNetSpeed ?? K.ConfiguredInternetSpeed,
                FixedTickSimulation = modern,
            };
            adapter.Server = server;
            endpoint = new MoveEndpoint(server, null, sequenced);
        }
        else
        {
            var client = new ClientMoves(pawn, adapter, transport);
            adapter.Client = client;
            if (sequenced is not null) sequenced.PredictionReset = client.ResetPredictionHistory;
            endpoint = new MoveEndpoint(null, client, sequenced);
        }

        _endpoints[pc] = endpoint;
        NoteNewPawn(pc, endpoint);
        return endpoint;
    }

    /// <summary>The protocol objects of <paramref name="controller"/> if they exist already (no side effects), for readouts.</summary>
    public MoveEndpoint? PeekEndpoint(Actor controller) =>
        _endpoints.TryGetValue(controller, out MoveEndpoint? endpoint) ? endpoint : null;

    public bool DrivesPawn(SBasePawn pawn) => pawn.Role == ENetRole.ROLE_Authority && pawn.m_AttackedBy is null &&
        pawn.Controller is SPlayerController pc && !SBasePawnMoves.IsCorpse(pc, pawn) && !pc.bGameEnded &&
        pc.StateNode is not SPlayerController.s_Inactive && PeekEndpoint(pc)?.Sequenced is not null;

    public void BeforeOwnerAnimation(SBasePawn pawn)
    {
        if (pawn.Controller is not SPlayerController { bWaitingForReinit: false } pc) return;
        if (!SBasePawnMoves.IsCorpse(pc, pawn)) EndpointOf(pc)?.Client?.EnsurePredictionStep();
        else
        {
            // Autonomous pawns do not run Actor.NativeTick physics. Once input prediction stops at death,
            // the body must still consume root motion and fall/land; those callbacks finish the death states.
            // Do not record an unsent prediction heartbeat during the death/respawn delay.
            pawn.Acceleration = default;
            pawn.XLevel.PhysicsDriver?.performPhysics(pawn, Sim.DeltaTime);
        }
    }

    public void BeforeClientTick()
    {
        ApplyOwnerActions();
        foreach (var kv in _endpoints)
        {
            if (kv.Key is SPlayerController pc) NoteNewPawn(pc, kv.Value);
            if (kv.Value.Client is { bUpdatePosition: true } client) client.ClientUpdatePosition();
        }
    }

    public void BeforeServerTick()
    {
        var connections = _driver.ClientConnections;
        for (int i = 0; i < connections.Count; i++)
            if (connections[i].Actor is SPlayerController { Pawn: not null } pc) EndpointOf(pc);
        foreach (var kv in _endpoints)
            if (!kv.Key.bDeleteMe && kv.Value.Server is { } server) kv.Value.Sequenced?.PumpServer(server);
    }

    public void AfterTick()
    {
        foreach (var kv in _endpoints) if (kv.Key.bDeleteMe) _deadEndpoints.Add(kv.Key);
        foreach (Actor actor in _deadEndpoints)
        {
            _endpoints.Remove(actor); _pawns.Remove(actor);
            if (actor is SPlayerController departed) { _controls.Remove(departed); _ownerActionSequences.Remove(departed); }
        }
        _deadEndpoints.Clear();
        foreach (var kv in _endpoints)
        {
            if (kv.Key.bDeleteMe) continue;
            if (_driver.IsServer && kv.Value.Server is { } server) kv.Value.Sequenced?.SendAcknowledgement(server);
            else if (kv.Key is SPlayerController { Pawn: SBasePawn pawn } pc)
            {
                if (pawn.NetworkCorrectionSeconds > 0f)
                {
                    float left = System.Math.Max(0f, pawn.NetworkCorrectionSeconds - Sim.DeltaTime);
                    pawn.NetworkCorrectionOffset *= left / pawn.NetworkCorrectionSeconds;
                    pawn.NetworkCorrectionSeconds = left;
                }
                if (!pc.bWaitingForReinit)
                {
                    // AnimEnd can author another action after the controller's SendActions pass. Flush it in
                    // this command's completion fence so its state, collision and pose share the same boundary.
                    if (kv.Value.Sequenced is not null) pawn.SendActions();
                    if (!SBasePawnMoves.IsCorpse(pc, pawn)) kv.Value.Client?.FinishPredictionTick();
                }
            }
        }
        if (_driver.IsServer) FlushOwnerActions();
    }

    private readonly Dictionary<Actor, Pawn?> _pawns = new();

    // The physics check's 3 s (net-moves.md rule 11.5) count from the pawn's first move: a player who was dead or
    // spectating for longer sent no moves, and the stale fLastTimePhysicsChecked would raise a reinit at the
    // respawned pawn's first move, before its server copy has left PHYS_None.
    [Provenance(ProvenanceKind.Estimated, "net-moves.md rule 11.5 read as continuous mismatch of the current pawn")]
    private void NoteNewPawn(SPlayerController pc, MoveEndpoint endpoint)
    {
        if (_pawns.TryGetValue(pc, out Pawn? last) && ReferenceEquals(last, pc.Pawn))
        {
            return;
        }

        _pawns[pc] = pc.Pawn;
        // Possession and the new command epoch can arrive in different packets. Never replay a previous
        // body's saved controller/animation state onto its replacement while waiting for that epoch.
        endpoint.Client?.ResetPredictionHistory();
        if (endpoint.Client is not null && pc.Pawn is { bDeleteMe: false } fresh)
        {
            // GivePawn/ClientRestart can precede the pawn's actor channel. If the controller's Pawn
            // reference resolves later, finish possession here even if a mirrored walking state has
            // already replaced WaitingForPawn. Otherwise the camera stays on the old spectator target.
            fresh.Controller = pc;
            if (fresh.Health > 0 && !ReferenceEquals(pc.ViewTarget, fresh) && !(pc.PlayerReplicationInfo?.bOnlySpectator ?? false))
                pc.ClientRestart();
        }
        if (endpoint.Server is not { } server) return;
        endpoint.Sequenced?.ResetLifetime();
        server.fLastTimePhysicsChecked = pc.Level?.TimeSeconds ?? 0f;
    }

    public void MyReplicateMove(SPlayerController PC, float DeltaTime, FVector NewAccel)
    {
        if (_driver.IsServer || PC.Role != ENetRole.ROLE_AutonomousProxy || EndpointOf(PC)?.Client is not { } client)
        {
            PC.eventMyProcessMove(DeltaTime, NewAccel);
            return;
        }

        if (client.bUpdatePosition)
        {
            client.ClientUpdatePosition();
        }

        client.MyReplicateMove(DeltaTime, NewAccel);
    }

    public void MyReplicateMoveWithoutMoving(SPlayerController PC, float LeftTime, FVector NewAccel)
    {
        if (PC.XLevel.World.IsReplaying) return;
        if (!_driver.IsServer && EndpointOf(PC)?.Client is { } client)
        {
            client.MyReplicateMoveWithoutMoving(LeftTime, NewAccel, PC.Level.XLevel.DeltaTime);
        }
    }

    private void Send(SPlayerController pc, string function) => _driver.ProcessRemoteFunction(pc, function, _args);

    public void ServerGetZipP(SPlayerController PC, FVector P1, FVector P2)
    {
        _args.Clear().Vector(0, P1).Vector(1, P2);
        Send(PC, nameof(ServerGetZipP));
    }

    public void ServerGetNewZipP(SPlayerController PC, FVector P1, FVector P2)
    {
        _args.Clear().Vector(0, P1).Vector(1, P2);
        Send(PC, nameof(ServerGetNewZipP));
    }

    public void ServerGetZipPAndNormal(SPlayerController PC, FVector P1, FVector P2, FVector NormalX1000)
    {
        _args.Clear().Vector(0, P1).Vector(1, P2).Vector(2, NormalX1000);
        Send(PC, nameof(ServerGetZipPAndNormal));
    }

    public void ServerSetInfoForVandamme(SPlayerController PC, FVector Position, FVector DirX1000)
    {
        _args.Clear().Vector(0, Position).Vector(1, DirX1000);
        Send(PC, nameof(ServerSetInfoForVandamme));
    }

    public void ServerSetWallJumpDir(SPlayerController PC, FVector DirX1000)
    {
        _args.Clear().Vector(0, DirX1000);
        Send(PC, nameof(ServerSetWallJumpDir));
    }

    public void UpdateDesiredRotation(SPlayerController PC, FRotator Rot)
    {
        _args.Clear().Rotator(0, Rot);
        Send(PC, nameof(UpdateDesiredRotation));
    }

    /// <summary>ReplicateActionToServer(BSTTime, MSATime, Val0, Val1) or the form with the velocity × 100 (move-protocol.md rule 9).</summary>
    public void ReplicateActionToServer(SPlayerController PC, int[] Actions, int Count, bool bWithVelocity)
    {
        ArgumentNullException.ThrowIfNull(Actions);
        Array.Clear(_actions);
        if (PeekEndpoint(PC)?.Sequenced is not null && PC.Pawn is SBasePawn pawn)
        {
            int end = Math.Clamp(Count, 0, Math.Min(Actions.Length, 6));
            Count = Math.Clamp(pawn.iNbAction, 0, end);
            Array.Copy(Actions, end - Count, _actions, 0, Count);
        }
        else Array.Copy(Actions, _actions, Math.Min(Actions.Length, 6));
        MovePacking.CompressActions(_actions, Count, out int val0, out int val1);
        _args.Clear().Float(0, PC.Level.TimeSeconds).Float(1, PC.MirrorStateAcknowledgementTime).Int(2, val0).Int(3, val1);
        if (bWithVelocity)
        {
            _args.Vector(4, MovementNet.Scaled(PC.Pawn?.Velocity ?? default, MoveK.VelocityWireScale));
            Send(PC, "ReplicateActionToServerWithVelocity");
        }
        else
        {
            Send(PC, "ReplicateActionToServer");
        }
    }

    /// <summary>SetMateTarget(tgt): the co-op climber's mate to the server (reliable); the server runs the method of that name.</summary>
    public void SetMateTarget(SPlayerController PC, SBasePawn? Target)
    {
        _args.Clear().Object(0, Target);
        Send(PC, nameof(SetMateTarget));
    }

    /// <summary>AskServerToStartCoopInteraction(): the climber reached the helper (reliable); the server starts both halves.</summary>
    public void AskServerToStartCoopInteraction(SPlayerController PC)
    {
        _args.Clear();
        Send(PC, nameof(AskServerToStartCoopInteraction));
    }

    /// <summary>UpdatePositionForServer(NewLocation, NewRotation): the client's camera spot (unreliable); the server runs the method of that name.</summary>
    public void UpdatePositionForServer(SPlayerController PC, FVector NewLocation, FRotator NewRotation)
    {
        _args.Clear().Vector(0, NewLocation).Rotator(1, NewRotation);
        Send(PC, nameof(UpdatePositionForServer));
    }

    /// <summary>ServerPlayLandingSound is the pawn's function: it goes on the pawn's channel.</summary>
    public void ServerPlayLandingSound(SPlayerController PC)
    {
        if (PC.XLevel.World.IsReplaying) return;
        if (PC.Pawn is { } pawn)
        {
            _driver.ProcessRemoteFunction(pawn, nameof(ServerPlayLandingSound), _args.Clear());
        }
    }
}
