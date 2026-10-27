# State switching, the action stream and traversal data on the network

Units:
- uc:SBase.SPlayerController:ChangeState
- uc:SBase.SPlayerController:PCChangeServerState
- uc:SBase.SPlayerController:MirrorState
- uc:SBase.SPlayerController:MirrorPredictedState
- uc:SBase.SPlayerController:PCChangeClientState
- uc:SBase.SPlayerController:MirrorStateForRepos
- uc:SBase.SPlayerController:PCChangeClientStateForRepos
- uc:SBase.SPlayerController:IAmOnClient
- uc:SBase.SPlayerController:ReplicateActionToServer
- uc:SBase.SPlayerController:ReplicateActionToServerWithVelocity
- uc:SBase.SPlayerController:ServerGetZipP
- uc:SBase.SPlayerController:ServerGetNewZipP
- uc:SBase.SPlayerController:ServerGetZipPAndNormal
- uc:SBase.SPlayerController:ServerSetWallJumpDir
- uc:SBase.SPlayerController:ServerSetInfoForVandamme
- uc:SBase.SPlayerController:ScriptBigServerMove (traversal payload part)
- uc:SBase.SPlayerController:SendPawnActions
- uc:SBase.SPlayerController:ClientGetPawnActions
- uc:SBase.SPlayerController:ClientGetPawnPosition
- uc:SBase.SBasePawn:ChangeEnumAction
- uc:SBase.SBasePawn:SendActions
- uc:SBase.SBasePawn:GetNewActions
- uc:SBase.SBasePawn:PostNetBeginPlay
- uc:SBase.SBasePawn:SendZipPToServer
- uc:SBase.SBasePawn:SendNewZipPToServer
- uc:SBase.SBasePawn:SendZipPAndNormalToServer
- uc:SBase.SBasePawn:SetInfoForVandamme
- uc:SBase.SBasePawn:SetWallJumpDir
- rep:SBase.SBasePawn.ZoneConfigForClient
- rep:SBase.SBasePawn.AllActions
- rep:SBase.SBasePawn.iNbActionForDistantClient
- rep:SBase.SBasePawn.fRepTimeOfLastActionChange
- rep:SBase.SBasePawn.BTW
- rep:SBase.SBasePawn.PipHFire
- rep:SBase.SBasePawn.WeaponLoaded
- rep:SBase.SBasePawn.IK_Type
- rep:SBase.SBasePawn.iYawPitchFromView
- rep:SBase.SBasePawn.iDesiredYawPitch
- rep:SBase.SBasePawn.m_InteractionTarget
- rep:SBase.SBasePawn.bCanBeGrabbed
- rep:Engine.LevelInfo.GEActivationMask
- nat:SBase.SBasePawn.CompressActions
- nat:SBase.SBasePawn.UnCompressActions
- nat:SBase.SBasePawn.NativeExecuteReceivedActionForServer

## Purpose

How a movement state change made by one machine reaches the others, how the animation "actions" that drive the
native moves are executed and sent, and how traversal data (GE points, wall normals, target positions) gets to the
server. This is the frame every movement spec assumes. Saved moves and position correction (MyServerMove,
BigServerMove, MyClientAdjustPosition family) belong to the netcode spec; only their traversal payload is listed
here.

## Data

| Field | Owner | Notes |
|---|---|---|
| MirrorStateAcknowledgementTime (MSA) | SPlayerController | the server time of the last server-imposed state the client accepted |
| MirrorStateLastTime | SPlayerController | latest server state-change time applied on the client |
| ActorPassedByMirrorState | SPlayerController | extra actor passed with a mirrored state |
| PreviousState | Engine.Controller | native; set by GotoState unless bDontSaveCurrentState |
| sAction, AllActions[6], iNbAction, iNbActionInTable, iNbActionsSent | SBasePawn | action queue |
| fTimeOfLastActionChange, fRepTimeOfLastActionChange, fOldTimeOfLastActionChange | SBasePawn | action batch times |
| bActionsSendWithVelocity, bActionsHasBeenSentByServerMove, bActionsSendByServerMove | SBasePawn | send flags |
| C_MAXACTIONS | SBasePawn const | 6 |

## Rules

### Who decides: the controlling side

`IAmOnClient()` is `RemoteRole != ROLE_AutonomousProxy`. It is true on the owning client of a remote player and for
the local player of a listen server or standalone game; it is false only on the server's copy of a remote player's
controller. Almost every movement decision (AnimEnd transitions, GE events, landings, physics events) is guarded by
this test, so movement is decided by the player's own machine and the server follows.

### Client-driven changes (ChangeState)

1. `ChangeState(State, Label, bDontSave)` (simulated event): GotoState locally; in standalone or on a listen server
   stop there; otherwise call `PCChangeServerState(MSA, State, Label, bDontSave)`, a reliable client-to-server call.
2. `PCChangeServerState` on the server accepts the change only if `|CodeTime - MSA| < 0.01`, i.e. the client had
   acknowledged the server's latest mirrored state. Before applying it, a change to s_FallDown label DIE_InPlace or
   to s_FallDropAttacked (when not already there) records the smoke instigator (if not done in the last 0.5 s and
   no recent sticky-shock grab) and counts the gas KO. Then GotoState with the same arguments.
3. A refused change leaves the server in its state; the client is corrected later by the server's own mirrored
   states or by the divergence reset (ReinitPawnBecauseOfDivergences, netcode spec).

### Server-driven changes (MirrorState)

1. `MirrorState(State, Label, bDontSave, AddActor)`: GotoState on the server; if this controller is a local
   viewport player stop there; otherwise, when more than 1 s passed since MSA, set MSA = now; then
   `PCChangeClientState(MSA, now, State, Label, bDontSave, AddActor)`, a reliable server-to-client call.
2. `PCChangeClientState` on the client applies only if `MirrorStateLastTime <= fNewTime` (in order): store
   MirrorStateLastTime, ActorPassedByMirrorState, MSA; skip a change to s_UseObject_StickyCam_SmoothToPosition while
   a predicted sticky cam is pending; GotoState.
3. `MirrorPredictedState(State, bDontSave)`: as MirrorState but without refreshing MSA.
4. `MirrorStateForRepos(State, Location, Yaw)`: as MirrorState, sending `PCChangeClientStateForRepos`; the client
   applies it in order, GotoState, and smooths the pawn horizontally toward Location with
   `ClientDistantMoveSmooth(delta with Z = 0, Yaw)`.

Movement uses MirrorState for the server-decided parts: deaths (s_Dead, PlayerFallingInerte), drop-attack knockdowns
(s_FallDropAttacked), spawn (EnterStartState), the coop pair start (AskServerToStartCoopInteraction).

### Actions (ChangeEnumAction and ExecuteAction)

An action is an SA_* id (SBasePawn consts, 0 to 328) naming a move step: it plays the clips and makes the native
changes (physics, collision, root motion) for that step.

1. `ChangeEnumAction(Action, bForce)`: return when the pawn has no controller or when the controller is the server
   copy of a remote player (`Controller.RemoteRole == ROLE_AutonomousProxy`). Otherwise, if the action differs from
   the current sAction or bForce: sAction = Action; `ExecuteAction(Action)` (native); batching: if more than 0.06 s
   passed since fTimeOfLastActionChange and every queued action was sent (`iNbActionsSent >= iNbActionInTable`),
   start a new batch (iNbActionInTable = 0, fTimeOfLastActionChange = now); if fewer than 6 are queued, append it
   (AllActions[iNbActionInTable], iNbAction++, iNbActionInTable++).
2. Repeating the current action without bForce does nothing (no replay). Movement code forces where it needs a
   restart (spawn, GotoWaitingState, coop).
3. `SendActions()` (event, raised natively by the player controller's tick on the owning client, pawn
   `ROLE_AutonomousProxy`; net-proxy.md rule 6): if actions are pending and
   they did not already go out inside a server move this tick (bActionsHasBeenSentByServerMove), compress them
   (CompressActions: `a1 | a2 << 9 | a3 << 18 | count << 27` and `a4 | a5 << 9 | a6 << 18`, 9-bit ids and a 3-bit
   count; net-moves.md rule 8) and send `ReplicateActionToServerWithVelocity(now, MSA, Val0,
   Val1, Velocity * 100)` when bActionsSendWithVelocity, else `ReplicateActionToServer(now, MSA, Val0, Val1)`
   (both reliable, client to server); iNbActionsSent = iNbActionInTable; iNbAction = 0. Then clear the three send
   flags.
4. Server side: `ReplicateActionToServer(BSTTime, MSATime, Val0, Val1)` applies only if
   `|MSATime - MSA| < 0.01`: `NativeExecuteReceivedActionForServer(Val0, Val1)`; then, if BSTTime is older than the
   last BigServerMove's time stamp, re-apply that move's actions (BSMS.Val0, Val1) and in root motion set
   SpecialYaw = int(BSMS.Vel.X / 100). The velocity variant also sets `Velocity = 0.01 * Vel` when not in root
   motion (or SpecialYaw = int(Vel.X / 100) in root motion), for both the new and the re-applied actions.
5. Other clients: the server sends each pawn's recent actions to every other client (native, through
   SendPawnActions, SendPawnPositionAndActions and the reliable variant, which call ClientGetPawnActions /
   ClientGetPawnPositionAndActions). The choice is made per viewer inside the pawn's native property list
   (net/proxy-pawns.md rule 1): actions alone are repeated unreliably for 0.15 s after each change; with a forced
   position sample (physics change, new actions, every 0.7 s at most, dead reckoning) they go with the position,
   reliably only for a batch new to that viewer. A client applies them only if
   `fRepTimeOfLastActionChange > WhichPawn.fOldTimeOfLastActionChange`: GetNewActions unpacks them into AllActions,
   iNbActionForDistantClient, fRepTimeOfLastActionChange, ZoneConfigForClient. It also copies m_AttackedBy,
   m_AttackTarget and SpecialYaw. The proxy tick then executes the new batch in order when
   `fRepTimeOfLastActionChange > fOldTimeOfLastActionChange`, through the engine's action code, and records
   `fOldTimeOfLastActionChange` (net-proxy.md rule 8).
6. Proxy position: ClientGetPawnPosition (unreliable) sets, for a pawn not in PHYS_FollowParent, ErrorDistance =
   0.1 * CompPosition.Location - Location (with its unit vector NormErrDist) and restarts fSmoothTime when the
   position changed, ReceivedRotation, and Velocity = ReceivedVelocity when it changed. Positions are sent x 10.
7. Pawns without a controller call InitializeBasicActions in PostNetBeginPlay.

### Traversal data to the server

| Sender (client) | RPC (reliable) | Server effect | Scaling |
|---|---|---|---|
| SendZipPToServer | ServerGetZipP(P1, P2) | ZipP1, ZipP2, NewZipP1, NewZipP2 = P1, P2 | none |
| SendNewZipPToServer | ServerGetNewZipP(P1, P2) | NewZipP1, NewZipP2 | none |
| SendZipPAndNormalToServer | ServerGetZipPAndNormal(P1, P2, N x 1000) | NewZipP*, ZipP*, m_geoNormal = N x 0.001 | normal x 1000 |
| SetWallJumpDir(dir) | ServerSetWallJumpDir(dir x 1000) | vWallJumpDir = dir x 0.001 | x 1000 |
| SetInfoForVandamme(dir) | ServerSetInfoForVandamme(vVandammePos, dir x 1000) | vVandammePos, vWallJumpDir, fLastRootMotionTimeCalled | x 1000 |
| SetVelocityAndOrientationForBlast(v) | ServerSetVelocityAndOrientationForBlast(v x 100) | blast velocity | x 100 |
| SetVelocityForDyingPawn(v) | ServerSetVelocityForDyingPawn(v x 100) | dying velocity | x 100 |

Each of these sets the value locally first; the server RPC is only sent when Role < Authority. The x 1000 and x
100 factors exist because vector RPC parameters are sent as rounded integers; the port must round the same way
(component-wise to the nearest integer) so client and server agree.

BigServerMove traversal payload (ScriptBigServerMove): the client's move carries `DiversInfos >> 4` = the pawn's
physics mode, which selects what is attached:

| Physics (DiversInfos >> 4) | ZipP1 | DifZipP2 | vect3 | vectUnit |
|---|---|---|---|---|
| 11 PHYS_Repositionnal | NewZipP1 | ZipP2 - NewZipP1 | vVandammePos | m_geoNormal x 1000 |
| 10 PHYS_Vandamme | 0 | 0 | vVandammePos | vWallJumpDir x 1000 |
| 6 OnPipe, 7 Linear, 9 Ledge, 13 OnLadder, 14 OnPipeH | NewZipP1 | NewZipP2 - NewZipP1 | 0 | 0 |
| any other | 0 | 0 | 0 | 0 |

The move is resent up to 4 times unreliably (NumberOfBigServerMoveToReSend = 4, C_MaxBSMReSend 4).

### Replicated pawn fields used by movement

- Reliable, server to all, `bNetInitial` only: BTW, PipHFire, WeaponLoaded, ZoneConfigForClient,
  fRepTimeOfLastActionChange, iNbActionForDistantClient, AllActions, IK_Type, bBeginSpecial12Anim,
  m_PawnNoMoreGrabbed. After the first bunch these change only through the action stream.
- Reliable, to simulated proxies only (`RemoteRole == ROLE_SimulatedProxy`): iDesiredYawPitch, iYawPitchFromView.
- Reliable, server to all: bCanBeGrabbed, m_InteractionTarget, m_AttackedBy, m_AttackTarget, UsedObject.
- LevelInfo.GEActivationMask is replicated (LevelInfo block), so GE availability is the same everywhere.

## States

No states of its own; every state change of the movement specs uses the mechanisms above.

## Constants

| Name | Value | Unit | Provenance | Source |
|---|---|---|---|---|
| State acknowledgement tolerance | 0.01 | s | decoded | PCChangeServerState, ReplicateActionToServer |
| MSA refresh | when older than 1.0 | s | decoded | MirrorState |
| Action batch window | 0.06 | s | decoded | ChangeEnumAction |
| Actions per batch | 6 | count | decoded | C_MAXACTIONS |
| Vector scaling | x 1000 for unit vectors, x 100 for velocities, x 0.1 for compressed positions | factor | decoded | RPC wrappers, ClientGetPawnPosition |
| BigServerMove resends | 4 | count | decoded | ScriptBigServerMove, C_MaxBSMReSend |
| Camera position report | 0.14 | s | decoded | PlayerTick |

## Network

This file is the network model for movement; see the per-move specs for which side runs what.

## Edge cases

- A client state change sent while a server MirrorState is in flight is refused (MSA mismatch), and so are the
  actions sent with the old MSA. Movement then depends on the server's later correction.
- ChangeEnumAction on the server copy of a remote player does nothing: BeginState code that plays actions on both
  sides only has effect on the controlling side; the server gets the actions from the client.
- Controller.GotoState is replaced by Reloaded.Core.dll in the shipped game (binding 0x58dc0320). Reloaded keeps the
  stock sequence (PreviousState = the state left on success, and also on a same-state call, unless
  bDontSaveCurrentState; then GotoLabel(Label or Begin)) and only adds, for the local player's controller, an
  anti-tamper check that redirects to s_MultiSpectating when the game code was modified (reloaded-hooks.md rule 3,
  natives2-events.md rule 3). The rewrite has no integrity system and ports the stock behaviour.

## Evidence

- Decompiled: local/re-out/uc/SBase/SPlayerController.uc (ChangeState, PCChangeServerState, MirrorState*,
  PCChangeClientState*, IAmOnClient, ReplicateActionToServer*, Server* RPCs, ScriptBigServerMove, SendPawn* and
  ClientGetPawn* functions, replication block), SBase/SBasePawn.uc (ChangeEnumAction, SendActions, GetNewActions,
  SendZipP*, SetInfoForVandamme, SetWallJumpDir, replication block), Engine/Controller.uc (GotoState, PreviousState),
  Engine/LevelInfo.uc (replication of GEActivationMask).
- Natives: ExecuteAction 0x10ad6800, NativeExecuteReceivedActionForServer 0x10ae4030, CompressActions 0x10ac20e0,
  UnCompressActions 0x10ac2290, ClientDistantMoveSmooth 0x10acc8c0, Controller.GotoState 0x58dc0320 (Reloaded.Core).
- uc-index.json replication entries for SPlayerController and SBasePawn (reliable flags and conditions).

## Open questions

None left from the native study.

### Resolved

1. Who calls SendActions and the SendPawn* events, and how often. Resolved: SendActions is raised by the player
   controller's tick on the owning client; the SendPawn* events are chosen per viewer inside the pawn's native
   list (samples at most every 0.09 s, at least every 0.7 s or on dead-reckoning failure, physics change or new
   actions; actions alone repeated for 0.15 s). Rules 3, 5. Evidence: net-proxy.md rules 1 to 6
   (ANSWERS-net proxy-pawns 1).
2. CompressActions bit layout. Resolved: `a1 | a2<<9 | a3<<18 | count<<27` and `a4 | a5<<9 | a6<<18`. Rule 3.
   Evidence: editor 0x111f8ad0 (ANSWERS-net move-protocol 10).
3. How the proxy plays AllActions. Resolved: the new batch in order when its time is newer than the last played,
   through the engine's action code. Rule 5. Evidence: net-proxy.md rule 8 (ANSWERS-net proxy-pawns 3).
4. The Reloaded GotoState replacement. Resolved: stock PreviousState and label handling plus an integrity check for
   the local player's controller. Edge cases. Evidence: reloaded-hooks.md rule 3.
