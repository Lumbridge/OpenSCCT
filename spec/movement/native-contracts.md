# Native movement contracts for OpenScct.Physics

Units:
- nat:SBase.SPlayerController.CheckArmsZone
- nat:SBase.SPlayerController.CheckFeetZone
- nat:SBase.SPlayerController.CheckBackFeetZone
- nat:SBase.SPlayerController.CheckWallJump
- nat:SBase.SPlayerController.CheckGEForWallJump
- nat:SBase.SPlayerController.CheckBarrier
- nat:SBase.SPlayerController.CheckBTW
- nat:SBase.SPlayerController.CheckBTWCorner
- nat:SBase.SPlayerController.CheckBTWEyes
- nat:SBase.SPlayerController.bTestCanFireInBTW
- nat:SBase.SPlayerController.CheckForOtherPipeV
- nat:SBase.SPlayerController.CheckRoomToChangePipeV
- nat:SBase.SPlayerController.CheckRoomToChangeSideLadder
- nat:SBase.SPlayerController.CheckCeilingForLadder
- nat:SBase.SPlayerController.bCanGoInPipe4
- nat:SBase.SPlayerController.bCanTakeLedgeFromPipe
- nat:SBase.SPlayerController.bTestTopLadder
- nat:SBase.SPlayerController.bTestGrabLedgeGeo
- nat:SBase.SPlayerController.TestTopLedge
- nat:SBase.SPlayerController.CheckIfCanInteractWithMate
- nat:SBase.SPlayerController.AreaIsSafeToInteract
- nat:SBase.SPlayerController.MyReplicateMove
- nat:SBase.SPlayerController.MyReplicateMoveWithoutMoving
- nat:SBase.SBasePawn.CheckFence
- nat:SBase.SBasePawn.CanUnCrouch
- nat:SBase.SBasePawn.MovePawn
- nat:SBase.SBasePawn.NativeTick
- nat:SBase.SBasePawn.MyDoJump
- nat:SBase.SBasePawn.CanUnZipSpeed
- nat:SBase.SBasePawn.CanSetColAfterPipe4
- nat:SBase.SBasePawn.ExecuteAction
- nat:SBase.SBasePawn.InitializeBasicActions
- nat:SBase.SBasePawn.ClientDistantMoveSmooth
- nat:SBase.SBasePawn.ReinitBecauseOfDivergences
- nat:SBase.SBasePawn.CheckIfCanStartSHL
- nat:SBase.SBasePawn.FindHoistClimbType
- nat:SBase.SPAWNDEFENSE.MyCheckBob
- nat:SBase.SPawnAttaque.TickNative
- nat:Engine.Actor.Trace
- nat:Engine.Actor.SetPhysics
- nat:Engine.Actor.SetRotation
- nat:Engine.Controller.GotoState
- uc:SBase.SPlayerController:Ledge
- uc:SBase.SPlayerController:OrthoPipeH
- uc:SBase.SPlayerController:FenceTop
- uc:SBase.SPlayerController:ZipLine
- uc:SBase.SPlayerController:Pipe
- uc:SBase.SPlayerController:NarrowLadder
- uc:SBase.SPlayerController:TopPipe
- uc:SBase.SPlayerController:TopPipeCrouch
- uc:SBase.SPlayerController:TopLadder
- uc:SBase.SPlayerController:FeetLedge
- uc:SBase.SPlayerController:WaistLedge
- uc:SBase.SPlayerController:NoGE
- uc:SBase.SPlayerController:HandOverHand
- uc:SBase.SPlayerController:ChangeLedge
- uc:SBase.SPlayerController:AutoFeetLedge
- uc:SBase.SPlayerController:TopSynchro
- uc:SBase.SPlayerController:MyFalling
- uc:SBase.SPlayerController:MyNotifyLanded
- uc:SBase.SPlayerController:FellOutOfWorld
- uc:SBase.SPlayerController:DropAttack
- uc:SBase.SPlayerController:GetSpeedMode
- uc:SBase.SPlayerController:ScriptMyProcessMove
- uc:SBase.SBasePawn:RMColOn
- uc:SBase.SBasePawn:AnimNotifyNoise
- uc:SBase.SBasePawn:Tick
- uc:SBase.SPawnAttaque:Tick
- uc:SBase.SPAWNDEFENSE:CheckBob
- uc:SBase.SPAWNDEFENSE:Tick
- def:SBase.SBasePawn (zones, ZCDecal, fPhysReposLinearSpeed)
- uc:SBase.SGETrigger:Trigger
- uc:SBase.SGETrigger:UnTrigger
- def:SBase.SGETrigger
- rep:Engine.LevelInfo.GEActivationMask

## Purpose

The movement script decides states; the engine moves the pawn. This file lists everything the script expects from
native code, so OpenScct.Physics can implement it. Each entry gives the observable contract, the inputs and outputs the
script reads, the evidence, and how sure we are. The native bodies are now decoded in `spec/native`
(physics-*.md, collision-*.md, execute-action.md, anim.md, input.md, net-*.md); this file keeps the script-side
contract and points there for the full rules. "inferred" marks entries no native study has confirmed yet.

## Data

### Physics modes used by movement

| EPhysics | Value | Native (editor twin) | Set by | Script input | Measured |
|---|---|---|---|---|---|
| PHYS_None | 0 | - | grabbed-from-ledge code | - | - |
| PHYS_Walking | 1 | ASBasePawn::physWalking 0x112025e0, physWalkingSimulated 0x11202080, PhysWalkingWithChild 0x111f8be0, MycalcVelocity 0x111fc2d0, APawn::stepUp 0x1117d2a0 | walking actions, landing | Acceleration (direction and length), GroundSpeed | steady speed = GroundSpeed; step 32 ok, 36 not; 45 degree ramps (decoded: step 34, floor Normal.Z >= 0.7; physics-walking.md 4, 5) |
| PHYS_Falling | 2 | ASBasePawn::physFalling 0x11213090, processLanded 0x11210660, bCanLand 0x11210450 | MyDoJump, floor lost | Acceleration (air control along facing) | gravity 1500 (a fixed -1500 in the native); no terminal speed, but 3D speed is capped at 2500 and horizontal air speed at 417 per sub-step (physics-falling.md 2, 5) |
| PHYS_RootMotion | 5 | ASBasePawn::physRootMotion 0x112128c0, physFallingInRootMotion 0x1120c440, EndRootMotion 0x111fd160, bServerTestRootMotion 0x11215580 | traversal actions (climbs, vault, wall jump, fence cross, coop) | none (zero moves) | mesh locked, Location moved on the native's schedule |
| PHYS_OnPipe | 6 | APawn::physOnPipe 0x1120a470 | SA_ON_PIPE and kin | Acceleration.Z = +-145 climb; X = +-524 slide term | 145 uu/s |
| PHYS_Linear | 7 | APawn::physLinear 0x11208af0 | SA_ON_ZIP and kin | Acceleration = v * m_geoNormal, GroundSpeed = 385 v / 191 | 407 to 424 uu/s |
| PHYS_OnVerticalPlane | 8 | ASBasePawn::physOnVerticalPlane 0x11208670 | SA_ONFENCE | Acceleration: Z +-120, side 120 Y, length 120 | - |
| PHYS_Ledge | 9 | APawn::physLedge 0x11204870 | SA_ONLEDGE, SA_LEDGE_GRAB (TypeLedge 0); the waist, barrier and feet detect actions (TypeLedge 1, 2, 3: a snap to the attach point, then TopSynchro) | Acceleration = 80 * straf * Y | - |
| PHYS_Vandamme | 10 | ASBasePawn::physVandamme 0x111fb380 | SA_VANDAMME_* | none | - |
| PHYS_Repositionnal | 11 | ASBasePawn::physRepositionning 0x111fc7a0, EndPhysRepositionning 0x111fcbc0 | SA_REPOS, SA_FAST_REPOS, SA_GRAB_REPOS, SA_SHL_S_REPOS, SA_SERVER_REPOS, SA_FAST_REPOS_SANS_RECAL | target vVandammePos, facing vWallJumpDir | crouched walk-off hang showed Physics 11 |
| PHYS_FollowParent | 12 | - | grab (carried pawn) | - | - |
| PHYS_OnLadder | 13 | APawn::physOnLadder 0x1120b5d0 | SA_ON_LADDER and kin | Acceleration.Z = +-126 or the rung snap value; X = +-524 slide; (0, 20, 0) / (0, 15, 0) approach | 126 uu/s |
| PHYS_OnPipeH | 14 | APawn::physOnPipeH 0x112092c0 | SA_ON_PIPEH, SA_ON_PIPEH_4 | Acceleration.X = 126 (hands) or +-113 (legs over) | 105 uu/s |

Dispatch: ASBasePawn::performPhysics 0x11207b70, setPhysics 0x111fd020 (AActor::setPhysics 0x1117bd30).
Physics modes are never set by the movement script directly; they follow the actions (ExecuteAction) the script
plays, plus the native's own transitions (floor lost, landed, end of root motion, end of repositioning). The
dispatch is decoded in physics-dispatch.md: the mode table, the 0.0003 s and 8-iteration sub-step limits, which
modes receive the iteration count (only Walking, Falling, FollowParent and Flying), and the setPhysics side effects
(EndPhysRepositionning, fZMaxFalling, base, zeroing, bActionsSendByServerMove). The physics mode of every action is
in execute-action.md section 4.

### Collision per stance (ZoneConfig)

| ZoneConfig | Value | Radius | Half height | When |
|---|---|---|---|---|
| ZC_Stand | 0 | 35 | 87.5 | default |
| ZC_Crouch | 1 | 35 | spy 50, merc 68 (CrouchHeight) | crouched |
| ZC_ZipSpeed | 2 | 35 | 45 (ZipHeight, inferred) | speed pose on a zip line (SetZipSpeed) |
| ZC_PipeH4 | 3 | 35 | 27.5 (Pipe4Height) | legs over a horizontal pipe (SetColForPipe4) |

ZCDecal[16] (def, per class) holds the Location.Z shift for a change of zone configuration. Decoded values:
SPawnAttaque (1) 37.5, (2) -42.5, (3) -60, (4) -37.5, (8) 42.5, (10) 42.5, (12) 60, (14) 60; SPAWNDEFENSE the same
with (1) 19.5 and (4) -19.5. Read as `ZCDecal[from * 4 + to]`, entries 1, 2, 3, 4, 8 and 12 match the cylinder
changes (stand to crouch keeps the feet: 87.5 - 50 = 37.5; stand to zip speed or pipe 4 keeps the top: 42.5 and
60); entries 10 and 14 do not fit that reading (open question 6). The GE tests read `ZCDecal[ZoneConfig * 4]` (from
the current configuration back to standing) to get the standing-equivalent Location.Z in the ledge, waist, feet,
back-feet and fence tests (physics-ge-queries.md Data). ZoneConfigForClient replicates the current configuration
to other clients with the actions.

### GE search zones (pawn space: offsets from Location, rotated by the full rotation for arms zones and by the yaw for feet zones)

| Zone | Offset | Radius | Set by |
|---|---|---|---|
| Normal arms | (0, 0, 44.45) | 139.7 | ResetZones, SetNormalZones |
| Ledge grab arms | (39, 0, 117) | 40 | SetLedgeGrabZones (GrabLedge) |
| Pipe arms | (37, 0, 90.3) | 25.4 | SetPipeZones |
| Narrow ladder arms | (37.5, 0, 100.5) | 19.05 | (no script setter) |
| Hand over hand arms | (0, 0, 128) | 50 | (no script setter) |
| Zip line arms | (0, 0, 116) | 60 | SetZipLineZones |
| Fence arms | (45.72, 0, 90) | 25 | SetFenceZones (GrabFence) |
| Normal feet | (39, 2, -85) | 60 | ResetZones standing, SetNormalZones |
| Crouched feet | (0, 0, -63.5) | 76.2 | ResetZones crouched |

The script keeps the active ones in m_CurrentArmsZone / m_CurrentArmsRadius and m_CurrentFeetZone /
m_CurrentFeetRadius. The natives search with the current zones and radii, corrected by PrePivot -
Default.PrePivot; only the ledge test uses m_LedgeGrabArmsZone whatever the current arms zone
(physics-ge-queries.md 2, 3, 6).

## Rules

### Frame order (controlling side)

1. PlayerTick (script): input natives, then the state's PlayerMove, which calls the zone checks and other natives
   and ends with `MyReplicateMove(dt, newAccel)` (client) or `MyProcessMove(dt, newAccel)` (server / standalone).
2. MyProcessMove sets Pawn.Acceleration; the state may override it. On a client, MyReplicateMove (native,
   0x10aad5c0, editor RealReplicateMove 0x111f2a80) saves the move, moves the pawn locally
   (autonomous physics with this acceleration and dt) and sends it to the server (netcode spec).
3. Pawn physics for the frame (performPhysics on the current mode). During it, the native calls the script events
   below. Events raised from inside physics run immediately (UE2 semantics), so a state change made in an event
   takes effect for the rest of the frame.
4. Animation update: AnimEnd(channel) events and clip notifies (ImpulsionSplit, NotifyTakeArmControl*, RMColOn,
   AnimNotifyNoise...) are raised from the animation tick. Many state transitions depend on AnimEnd timing, so the
   port's animation channels must reproduce UE2 one-shot end times (see "Clip timing").

### Events the natives raise (script entry points)

| Event | Raised by (inferred where not decoded) | Meaning, arguments |
|---|---|---|
| MyFalling(LeftTime) | physWalking when the floor is lost, only where the Controller's RemoteRole is not AutonomousProxy; it rewrites Velocity from the displacement first and does not set PHYS_Falling itself (physics-walking.md 4.9) | LeftTime: always 0.0 |
| MyNotifyLanded(HitNormal, fTimeLeft, LandedOn) -> bool | physFalling on a floor hit with Normal.Z >= 0.7 once bCanLand passes (a box hit 0.2 H + 8 below the pawn, 0.9 cylinder), only on the controlling side, then physics restarts with the remaining time; processLanded (wall-slide landings) with no role test; root-motion falling above Normal.Z 0.707 (NotifyHitWall below). Engine Landed is never called (physics-falling.md 7) | return true when handled |
| NotifyHitWall(HitNormal, Wall) -> bool | walking, pipe, ladder, zip and BTW physics on a blocking hit (also the floor for pipe and ladder modes) | return true to stop the native's default handling |
| NotifyBump(Other) -> bool | any mode when touching another actor | movers, pawns |
| FellOutOfWorld | below the world / KillZ | - |
| DropAttack(Pawn) | physWalking (standing on a pawn that can be drop-attacked), physFalling and physFallingInRootMotion (the server-side trigger and homing), physLinear (the zip hold move hitting a pawn); a roll runs in walking or root-motion physics and triggers it only through these (physics-walking.md 0, physics-falling.md 4) | target |
| TopSynchro | end of a repositioning (PHYS_Repositionnal within 1 of vVandammePos with the yaw matching vWallJumpDir), also used by slides to say "reached the GE end" (ladder slide: one per rung) | - |
| NoGE | the GE being used is gone: physLedge when no LG (or connected LG) still contains the hands, physLinear and physOnPipeH when the hold move is blocked by a non-pawn, physLedge also after SA_LEDGE_GRAB with no LG (physics-traversal.md) | - |
| Ledge | CheckArmsZone through the LG arms test (game 0x10aa4420, rule 6 of the GE queries below); eventLedge game 0x10ab2dd0 | ZipP1/ZipP2 = the ledge |
| WaistLedge(bCanGoNoCrouch) | CheckArmsZone through the LG arms test, only when not walking | bCanGoNoCrouch true when the standing clearance (114.5 up, 56 over) passes, false when only the crouched one (109.2 up, 77 over) does (physics-ge-queries.md 4) |
| FeetLedge | CheckFeetZone through CheckLedgeGrabFeet | - |
| AutoFeetLedge | CheckArmsZone through the LG arms test, only while walking (physics-ge-queries.md 4, 6) | - |
| Pipe(locationEnd, orientationEnd) | CheckArmsZone or a pipe-end test: a PIPE GE | ZipP1/ZipP2/m_geoNormal filled |
| TopPipe(bLeft), TopPipeCrouch(bLeft) | CheckFeetZone at a pipe top (from walking), or physOnPipe at the top (TestTopPipe 0x111fe5e0: TopPipe when standing fits, else TopPipeCrouch when crouched fits, else a nearby LG gives Ledge; physics-traversal.md TestTopPipe) | side; crouch-only exit |
| NarrowLadder | CheckArmsZone: a LADDER GE | - |
| TopLadder | CheckFeetZone at a ladder top, or physOnLadder when the hands pass the ladder top | - |
| HandOverHand | CheckArmsZone (CheckHOHArms 0x111ed7a0) or physOnPipe at a HOH junction | NewZipP1/NewZipP2 for the HOH |
| OrthoPipeH(bToLeft) | physOnPipeH at a perpendicular HOH within 31 | NewZipP1/NewZipP2 |
| ZipLine(locationEnd, orientationEnd) | CheckArmsZone: a ZL GE; physOnPipeH at an end within 3 of a ZL (input at least 120) | end point; m_geoNormal = slide direction, always downhill (CheckZipArms) |
| ChangeLedge(iType, P1, P2) | physLedge at a corner joining another LG: a shared end within 2 and perpendicular segments (0.1); outside corners need 75 + 75 clearance; collinear ledges merge instead (physics-traversal.md physLedge 6 to 8) | 0 inside right, 1 inside left, 2 outside right, 3 outside left; P1/P2 the next ledge |
| FenceTop(locationEnd, orientationEnd) | physOnVerticalPlane: a FENCE GE in the arms zone above the hands with clear boxes (35, 35, 20) up 127.5, over 75 and down 198.25; the climb stops (physics-ge-queries.md 5) | - |
| GetSpeedMode() -> float | NativeComputeControlVariables, which multiplies the key input by it each frame (input.md) | 1.0, 0.8, 0.6 or 0.4 (input-and-rotation.md) |
| ScriptMyProcessMove(dt, accel) | MyServerMove processing on the server, per client move | calls the state's MyProcessMove |
| RMColOn (SBasePawn) | clip notify during root motion | sets bUseRootMotionAdditionnalPhysics = true and RootMotionAdditionnalPhysics = PHYS_None (collision back on during the clip) |
| AnimNotifyNoise (SBasePawn) | clip notify (every machine) | the local player controller's ClientNotifyNoise(this pawn, 1.0): the merc HUD's sound arrow; nothing without a local player |

Events are virtual and state-overridable, and several natives are too: CheckArmsZone and CheckFeetZone are
non-final natives, so a state can replace them with script (s_WaistLedgeDetect replaces CheckFeetZone with
nothing; PlayerFallingInerte and PlayerFallingKO ignore CheckArmsZone). The port must dispatch them through the
state like any script function.

### GE queries (all zone and test natives)

1. Data: the map's GE list (spec/formats/ge, `local/formats/ge/<Map>.json`): kind (POLE 1, FENCE 2, HOH 4, LG 8,
   LADDER 16, PIPE 32, ZL 64), ID byte, 2 points (3 for LADDER and PIPE: bottom, top, side point 10 uu toward the
   climber at the bottom's height). The engine keeps a k-d tree over the GEs; any spatial index giving the same
   result set works.
2. Activation: a GE takes part in every query only while `ID == 0` or `(ID & LevelInfo.GEActivationMask) != 0`.
   GEActivationMask is replicated reliably from the authority (LevelInfo replication block, no bNetDirty
   condition). SGETrigger (RemoteRole None, so server only) changes it. Trigger: without bToggle, set the GEMask
   bits (`mask |= GEMask`); with bToggle, set them when they are not all set, else clear them
   (`mask -= mask & GEMask`). UnTrigger: clear them. SteeE: mask 4, one ID-4 ledge (GE 153, the crane wall top)
   disappears when `blast01` triggers SGETrigger271 (bToggle, GEMask 4).
3. Team filter: only spies take LG, PIPE, HOH, ZL and FENCE; both teams take LADDER (measured). The script handlers
   check the team for most events but not all (Mario walking's Pipe handler has no team test), so the native must
   not raise spy-only events for a merc, or the merc's states must not contain those handlers (they do not: the
   merc walks in weapon states, which only handle TopLadder and NarrowLadder).
4. Outputs: before raising an event the native fills SBasePawn.ZipP1/ZipP2 (the GE's points in the order the move
   needs, for example the slide direction for ZL, bottom and top for PIPE), m_geoNormal (unit GE normal or
   direction), and for chained moves NewZipP1/NewZipP2. The script then sends them to the server (SendZipPToServer,
   SendZipPAndNormalToServer, SendNewZipPToServer) and the server's natives use those values, not their own search.
5. Re-grab guard: LastZipP is set to ZipP1 when the spy drops off a GE; several handlers refuse a GE whose ZipP1
   equals LastZipP (ledges: unless the height differs by more than 20).
6. Ledge test (the LG arms test, game 0x10aa4420 = +0x1A4420, decoded in physics-ge-queries.md 4): for a ledge
   point C (the closest point to the ledge-grab zone) with horizontal normal n toward the pawn, the hang spot is
   C + 39 n at Z = C.Z - 117. With Ref = Location.Z + H - 87.5, two box traces with extent (35, 35, 87.5) and flags
   0x2DF run from (Location.X, Location.Y, Ref) to the hang spot, then from the hang spot up 50; both must be clear.
   Also required: C at least 24.5 from either end of the ledge, Velocity.Z > -1270, Hang.Z - 5 < Ref < Hang.Z + 35,
   and a horizontal distance squared from the pawn to the hang spot below 830. The same function raises WaistLedge
   (when not walking) and AutoFeetLedge (only while walking), tested before Ledge. Ledges shorter than about 80
   fail in practice (RE+ spike).
7. Search (physics-ge-queries.md 2, 3, 6): candidates are GEs whose segment lies within the current zone's radius
   of the zone centre; the nearest wins, except that the pipe and hand-over-hand modes prefer their own kind; at
   most one event per call. CheckArmsZone looks at HOH, LG, LADDER, PIPE and ZL (never FENCE or POLE);
   CheckFeetZone at LG, LADDER and PIPE.

### Per-native contracts

| Native | Game exe | Editor | Signature | Contract (script view) | Certainty |
|---|---|---|---|---|---|
| CheckArmsZone | 0x10aa54d0 | exec 0x111f2330 | () | GE search in the arms zone over HOH, LG, LADDER, PIPE and ZL; the nearest GE wins (pipe and HOH modes prefer their own kind) and raises at most one event per call (Ledge, WaistLedge, AutoFeetLedge, Pipe, NarrowLadder, HandOverHand, ZipLine), filling ZipP*; state handlers set bFindGE | decoded (physics-ge-queries.md 2, 4) |
| CheckFeetZone | 0x10aa7a80 | exec 0x111f26c0; CheckPipeFeet 0x111ec970, CheckLadderFeet 0x111ecf30, CheckLedgeGrabFeet 0x111ed2f0 | () | GE search in the feet zone over LG, LADDER and PIPE: TopPipe, TopPipeCrouch, TopLadder, FeetLedge | decoded (physics-ge-queries.md 3, 6) |
| CheckBackFeetZone | 0x10aa7e00 | exec 0x111ec6d0; CheckBackLedgeGrabFeet 0x111e4ef0 | () -> bool | the back-feet point mirrors the feet zone (X negated, yaw only, ZCDecal correction); the nearest LG within the feet radius fills ZipP and vBackFeetNormal = the ledge's horizontal normal facing the pawn's forward side; returns true | decoded (physics-ge-queries.md 7) |
| CheckWallJump | 0x10aa5a10 | exec 0x111ea780 | (X, Y, Z, out HitNormal) -> bool | a wall beside the airborne pawn usable for a split jump; returns its normal | inferred |
| CheckGEForWallJump | 0x10aa6250 | exec 0x111eaf20 | (X, Y, Z, out GEZPosition) -> bool | walls are traces, not GEs; only LG GEs are searched above the far point (box (60, 60, 300), near-perpendicular, more than 87.5 up); GEZPosition = that ledge's Z, or 200 above the probe point when none | decoded (physics-ge-queries.md 12) |
| CheckBarrier | 0x10aa5390 | exec 0x111ea600; CheckIfBarrier 0x112108f0 | () -> bool | an LG GE in the arms zone plus geometry: facing within 0.7, a height window, then a (35, 35, 22.5) box over the top 140 beyond and 20 aside, and down 85.2; ZipP1/ZipP2 = the LG points | decoded (physics-ge-queries.md 11) |
| CheckBTW | 0x10aa97e0 | exec 0x111e5640 | (optional Reverse) -> bool | Reverse probes forward (a wall in front), else backward, 1.5 R; then two waist probes (+-0.5 R at -0.5 H) and a head probe (0.8 H) along the wall direction must hit PHYS_None actors; vWallNormal = SafeNormal(sum of those three normals) | decoded (natives2-traversal.md 1) |
| CheckBTWCorner | 0x10aa91c0 | exec 0x111e5050 | (vNormal) -> bool | no angle window: a full flat wall in the plane of vNormal (cross at most 0.1) passing CheckBTW's three probes; vWallNormal untouched | decoded (natives2-traversal.md 3) |
| CheckBTWEyes | 0x10aaa000 | exec 0x111e5dc0 | (ToRight) -> int | 1 wall and floor go on; 0 the wall ends at head height and 100 beside is clear; -1 blocked beside or a drop | decoded (natives2-traversal.md 2) |
| bTestCanFireInBTW | 0x10ab22a0 | exec 0x111e8690 | (bIsLeft) -> bool | room to lean out and fire on that side | inferred |
| CheckForOtherPipeV | 0x10aa4110 | exec 0x111e92a0 | (out P1, out P2, out Normal) -> bool | the nearest GE within 10 of m_PipeArmsZone.X + 16 in front, when it is a PIPE; Normal = SafeNormal(P3 - P1) | decoded (natives2-traversal.md 4) |
| CheckRoomToChangePipeV | 0x10aa3e10 | exec 0x111e4960 | (bToLeft) -> bool | three clear (35, 35, 87.5) boxes round the pipe: aside zone.X + 8, forward 2 zone.X + 16, back to the centre line | decoded (natives2-traversal.md 5) |
| CheckRoomToChangeSideLadder | 0x10aa3b00 | - | (bToLeft) -> bool | clearance for the side swap clip | inferred |
| CheckCeilingForLadder | 0x10aa3a40 | exec 0x111e44c0 | () -> bool | true when the pawn may climb higher (no ceiling) | decoded from use |
| bCanGoInPipe4 | 0x10aab310 | exec 0x111e6680 | () -> bool | room to swing the legs over a HOH | inferred |
| bCanTakeLedgeFromPipe | 0x10ab1de0 | exec 0x111e81e0 | (bOnRight) -> bool | an LG from FindArmsGE with zone (40, +-40, 90.3) radius 60, both ends more than sqrt(35) from the closest point, two clear boxes (down 27 and 45 aside, then 14 forward); writes NewZipP1/NewZipP2 (search and clearance on opposite sides in both builds) | decoded (natives2-traversal.md 6) |
| bTestTopLadder | 0x10ab1140 | 0x111e7c60, exec 0x111e7f10 | (out iCanUncrouch) -> bool | room to climb off the top: spy up 192 then forward 73.9, merc up 196 then forward 68.8, true when both are clear; iCanUncrouch 1 when a (35, 35, 8) box from 20 below to 79.5 above that spot is clear | decoded (physics-ge-queries.md 9) |
| bTestGrabLedgeGeo | 0x10aac090 | exec 0x111e6980 | () -> bool | an LG 90 to 102 from the current ledge height (arms zone (37, 0, 210)), aligned within 10, with the controller's Location +-20 Right inside it; writes nothing | decoded (natives2-traversal.md 7) |
| TestTopLedge | 0x10aac030 | exec 0x111e6920; ASBasePawn::TestTopLedge 0x1120f610 | (bCrouchOnly) -> int | 1 room to stand (up 210.8, forward 57.8); 2 room to crouch (crouched up 206.1, forward 75.7); 3 room to vault over the top (box Z 44, 95 forward at +162, down to +78), tested after 1 or 2; 4 a barrier above the ledge, crouched (an LG 90 to 102 higher, more traces to +173), only when 1 and 2 failed; 0 none. bCrouchOnly returns test 2's result at once (no 1, 3 or 4) | decoded (physics-ge-queries.md 8, natives2-traversal.md 9) |
| CheckFence | 0x10acf300 | exec 0x112085a0 | (out Normal, out Wall, Pos) -> int | pure geometry and material, no GE: 0 nothing, 1 climbable (material bClimbable), 2 a flat face that is not climbable; two opposite probes and four corner probes (R + 10, H - 15) must hit the same plane; Wall = the hit actor; writes the normal | decoded (physics-ge-queries.md 10) |
| CanUnCrouch | 0x10acc930 | exec 0x11206900 | () -> bool | a standing cylinder fits at the stood-up location; read as UnCrouch(false)'s decoded stand-up test (a box sweep with the current cylinder from Location up twice the height change, flags 0x2DF, must be clear; execute-action.md rule 5.6) | inferred (the CanUnCrouch body itself is not decoded) |
| MovePawn | 0x10ae2da0 | exec 0x111fe4a0 | (vDelta) | swept move by vDelta; when blocked more than 50 uu short of the target, teleport to the target; the base is not changed explicitly (client correction) | decoded (editor 0x111fe4a0, net-moves.md) |
| NativeTick | 0x10ae3c20 | exec 0x1120ff60 | (dt) | per-tick pawn work: proxy smoothing (ErrorDistance, fSmoothTime, ReceivedVelocity), action playback for remote pawns (AllActions, iNbActionForDistantClient), IK | inferred |
| MyDoJump | 0x10ae00b0 | 0x111f9cf0, exec 0x111f9f00 | () | Physics = Falling, Velocity.Z = JumpZ, nJumpNumber = 1, jump clip | inferred, height measured |
| CanUnZipSpeed | 0x10acce10 | exec 0x11206b70 | () -> bool | room to leave the zip speed pose: the FarMoveActor test of UnZipSpeed (default cylinder restored by moving down) | decoded (physics-traversal.md) |
| CanSetColAfterPipe4 | 0x10acd360 | exec 0x111fc240 | () -> bool | room to restore the full cylinder after pipe 4 | inferred |
| ExecuteAction | 0x10ad6800 | SBasePawn::ExecuteAction 0x11215980, exec 0x1121eb10 | (Action) | plays the action's clips and sets physics, collision, crouch, IK and root motion (329 values: 314 cases, 15 no-ops) | decoded (execute-action.md section 4; Reloaded's wrapper passes every action through, see Edge cases) |
| InitializeBasicActions | 0x10ae4720 | - | () | prepares the action table on pawns without a controller (remote proxies) | inferred |
| ClientDistantMoveSmooth | 0x10acc8c0 | 0x11206650, exec 0x11206850 | (vDelta, iNewYaw) | smooth a remote pawn by vDelta (Z ignored by the caller) to yaw iNewYaw: a swept move with one slide along the hit surface | decoded (editor 0x11206650) |
| ReinitBecauseOfDivergences | 0x10ae5130 | exec 0x111ff170 | () | reset the basic action state (BTW, PipHFire, IK type, rolling, special anim, weapon transit, lying flags, blend times, attack partners), physics walking, idle animation, collision on | decoded (net-moves.md rule 17) |
| CheckIfCanStartSHL | 0x10ae6ff0 | exec 0x112125a0 | (out Loc, out Rot) -> bool | a short-ladder spot under a ledge | inferred |
| FindHoistClimbType | 0x10ae5150 | 0x11211130, exec 0x11215680 | (optional bIgnorePawn, optional bQuickCheckBarier) -> byte | SHL_* type of the hoist climb (0 none) | inferred |
| CheckIfCanInteractWithMate | 0x10aae290 | 0x111effc0 | (out MateFound) -> byte | 0 none, 1 mate ready to boost, 3 mate hanging ready to hoist | decoded from use |
| AreaIsSafeToInteract | 0x10aad9f0 | - | (Target) -> bool | room for the coop move | inferred |
| MyReplicateMove | 0x10aad5c0 | RealReplicateMove 0x111f2a80, exec 0x111f3000 | (dt, newAccel) | client: save, apply and send the move | netcode spec |
| MyReplicateMoveWithoutMoving | 0x10aae310 | exec 0x111f3450 | (LeftTime, newAccel) | client: flush a move at a physics event without moving again | netcode spec |
| MyCheckBob (SPAWNDEFENSE) | 0x10abdfc0 | - | (dt) | merc view bob (WalkingBob 0.01, CrouchingBob 0.08) | inferred |
| TickNative (SPawnAttaque) | 0x58dbfb90 | exec 0x1121f350 | (defaultHealth) | spy per-tick native, no movement: the screen glass effect (decoded, reloaded-hooks.md rule 11) and the health regeneration (combat/health.md rule 3); the game's binding points into Reloaded.Core.dll | Reloaded hook |
| Engine.Actor.SetRotation | 0x58dc0420 | - | (NewRotation) | binding points into Reloaded.Core.dll | Reloaded hook |
| Engine.Controller.GotoState | 0x58dc0320 | - | (NewState, Label, bDontSaveCurrentState) | sets PreviousState unless bDontSaveCurrentState; binding points into Reloaded.Core.dll | Reloaded hook |
| Engine.Actor.Trace | 0x10a27450 | AActor::Trace 0x110acac0 | (out HitLocation, out HitNormal, End, Start, bTraceActors, Extent, ...) -> Actor | standard UE2 box trace; script relies on extents (35, 35, 50), (35, 35, 87.5), (35, 35, 27.5), (35, 35, 20) and zero extent | standard |

### Native-only movement code (no script binding, must be ported)

ASBasePawn::MycalcVelocity 0x111fc2d0 (ground velocity from Acceleration and GroundSpeed), Crouch 0x11206990 /
UnCrouch 0x111fb750 / 0x111fb920, SetZipSpeed 0x111fbb80 / UnZipSpeed 0x111fbcf0, SetColForPipe4 0x111fc0e0 /
SetColAfterPipe4 0x111fbed0, CheckForLedges 0x111face0 (and APawn::CheckForLedges 0x1117c370), TestTopPipe
0x111fe5e0, UpdateMovementAnimation 0x112008a0, AnimateWalking 0x111fd360, PlayRoll 0x111fe260, PlayRollUp
0x111fe360, PlayJumpStart 0x111f97a0, PlayLanding 0x111f99d0, IsJumpingForward 0x111f9350, PlayAnimWithBlend
0x111f9fd0, AnimateStandUp 0x111f8ce0, IsNetRelevantFor 0x111fe9b0; APawn::calcVelocity 0x1117cbe0,
physicsRotation 0x1117a6b0, processHitWall 0x1117d920, CanCrouchWalk 0x1117b110, jumpLanding 0x110dc6d0.
Decoded since (natives2-movement.md rules 1 to 7): physicsRotation (players turn only with bRotateToDesired, off in
Versus; the walking roll lean always runs with RotationRate.Roll 3072, leaning from the side acceleration above
200 uu/s and decaying otherwise; FollowParent zeroes pitch and roll), FindBase (stock: a cylinder box 8 down,
flags 0xdf, SetBase when the hit actor changes), FindSpot (stock UE2 with actors never checked), moveSmooth and
TwoWallAdjust (stock UE2). The walking TopPipe exit (CheckPipeFeet) is decoded in natives2-traversal.md rule 8.

### Repositioning (PHYS_Repositionnal)

Script pattern (interactions, coop, top of pipe and ladder): set SBasePawn.vVandammePos to the target location (Z
usually the pawn's own), call SetInfoForVandamme(dir) which sets vWallJumpDir = dir (the facing to reach) and
fLastRootMotionTimeCalled, and play a repositioning action (SA_REPOS 148, SA_FAST_REPOS 161, SA_GRAB_REPOS 19,
SA_SHL_S_REPOS 253, SA_SERVER_REPOS 321, SA_FAST_REPOS_SANS_RECAL 328). The waist, barrier and feet detect actions
are not repositioning: they set PHYS_Ledge with a TypeLedge and physLedge snaps to the attach point
(barrier-and-low-ledges.md). Contract (decoded, physics-rootmotion-repositioning.md): the pawn moves in a straight
line toward vVandammePos (Z ignored when within 5, or with bUseAdditionnalPhysicsForRepos) at fPhysReposLinearSpeed
per second and turns toward vWallJumpDir at 3000 per call; TopSynchro is raised once it is within 1 of the target
and the yaw matches; touching another actor calls NotifyBump (the script aborts with SA_EXIT_REPOS); a GE that is
no longer valid calls NoGE. Collision with the world is off, unless bUseAdditionnalPhysicsForRepos is set (then the
pawn walks). EndPhysRepositionning snaps to the target on the authority when the mode ends.
fPhysReposLinearSpeed defaults to 87 and the repositioning actions set it: SA_REPOS and SA_GRAB_REPOS 180,
SA_FAST_REPOS and SA_FAST_REPOS_SANS_RECAL 300, SA_SERVER_REPOS 220 (server-controlled), SA_SHL_S_REPOS 380, all with
bUseAdditionnalPhysicsForRepos (execute-action.md constants). physVandamme uses its own literal 87.
bServerControlsRepos and bUseAdditionnalPhysicsForRepos (SBasePawn) are native flags.

### Root motion (PHYS_RootMotion)

1. Mesh lock: at the move start the mesh's world transform is locked and the clip's root bone carries the body
   (lock modes 1 or 3; SkeletalMeshInstance +0x184; root-motion lock note). The pawn's Location and Rotation are
   moved by the native for collision on a schedule that does not follow the root bone; the renderer must draw the
   locked transform plus the clip, never Location plus the clip.
2. Script-visible fields: iLockRootMotionType, fLastRootMotionTimeCalled, vSpeedAfterRM, vMoveAfterRM, rRotAfterRM,
   bReverseRM, RootMotionAdditionnalPhysics, bUseRootMotionAdditionnalPhysics (RMColOn), SpecialYaw (set from the
   action velocity on the server while in root motion), vLocationBeforeRM, rRotBeforeRM (Pawn).
3. Decoded end offsets (SBasePawn consts, forward X, right Y, up Z from the start; used by native code): barrier
   C_BARRIER_LEDGE (140, 20, 12.8); ledge to barrier C_LEDGE_BARRIER (95, 0, 121); pull-up standing C_LEDGE_UPST
   (57.8, 0, 210.8); pull-up crouched C_LEDGE_UPCR (75.7, 0, 206.1); waist ledge C_WAIST_LEDGE (56, 0, 114.5),
   crouched (77, 0, 109.2); feet ledge C_FEET_LEDGE (112.4, -, 39); fence top C_TOPFENCE (75, 0, -3.25); ledge corner
   C_LEDGE_CORNER (75, 75); ladder top C_TOP_LADDER (73.9, -, 192), armed C_TOP_LADDERW (68.8, -, 196), crouched
   C_TOP_LADDER_CR (75, -, 156); down from a ladder top C_DOWNTOP_LADDER (72, -, -192) (W and CR the same);
   down from a pipe top C_DOWNTOPPIPE (96.3, 38.67, -181.7), C_DOWNTOPPIPE2 (97.3, -39.17, -186.15), crouched
   (82.7, +-35, -179.6); pipe to ledge C_TR_PIPE_LEDGE (14, -45, -27); ledge to pipe C_TR_LEDGE_PIPE (-14, -45, 8);
   ortho HOH right (30, 30, 0), left (30, -30, 0); barrier above ledge crouched C_BARRIER_ABOVE_LEDGE_CR (95, 0, 173);
   hoist climber spot HoistOffset (130.7, -, 296.7) (script use).
4. Measured schedules (Bridge): LedgCrNwUp0 steps Location up 306 then down 100; LedgStAlUp0 root rises about 204;
   JumpStAlBr0 about 147 forward with a 30 degree yaw swing; wall jump spins the actor 180 degrees.

### Clip timing for AnimEnd-driven transitions

AnimEnd fires when a one-shot clip on that channel reaches its last frame (decoded, anim.md rule 2): frames are
normalised over the clip's frame count N and a non-looping clip ends when its frame reaches AnimLast = 1 - 1/N,
after (N - 1) / (N x normalised rate) seconds, that is (N - 1) / R for a clip of R frames per second at rate 1.
AnimEnd fires in that tick, one tick later in root motion. A notify at exactly frame 0 does not fire at the start
(the first advance only nudges the frame to 1e-4 and the notify window excludes the start frame), nor after a loop
wrap. Examples from the Bridge animation catalog: LedgStAlBg0 8 @15 (0.47 s), LedgStAlUp0 19 @15 (1.2 s),
LedgStAlToL 26 @15, FencStAlCo0 51 @15 (3.33 s), PipVStAlEuL 36 @15 (2.33 s), LaddStAlEu0 24 @15, RollStAlFdL 16
@15 (1.0 s), WljpStAlBkL 31 @30 (1.0 s), WljpStAlUpL 17 @30, ZippStAlBg0 16 @15. Channels used by movement:
0 base, 14 draw (DRAWCHANNEL), 15 double jump and roll (DBLJUMPCHANNEL), 17 damage (DAMAGECHANNEL).

### Network-side natives

NativeExecuteReceivedActionForServer (0x10ae4030, Val0/Val1 compressed actions), CompressActions (0x10ac20e0) /
UnCompressActions (0x10ac2290) pack up to 6 action ids into two ints; MyServerMove (0x10aacec0), BigServerMove
(0x10aae660), UnreliableReplicateBigServerMove (0x10ab1670), MyServerMoveWithPCRot (Reloaded 0x58dbfbc0) carry the
moves. These belong to the netcode spec; the movement rules above only need: actions executed on the server must
set the same physics mode and collision as on the client, and the server's natives must use the GE points the
client sent.

## States

No states of its own (see each traversal spec).

## Constants

| Name | Value | Unit | Provenance | Source |
|---|---|---|---|---|
| Gravity | -1500 | uu/s^2 | decoded and measured | def:Engine.LevelInfo DefaultGravity |
| Repositioning speed | fPhysReposLinearSpeed 87 | uu/s | decoded (use inferred) | def:SBase.SBasePawn |
| Ledge clearance box | (35, 35, 87.5), flags 0x2DF, +50 second trace | uu | decoded | game 0x10aa4420 (+0x1A4420), physics-ge-queries.md 4 |
| Hang spot | C + 39 n, Z = C.Z - 117; end margin 24.5; Velocity.Z > -1270; height window -5 to +35; distance squared < 830 | uu | decoded | physics-ge-queries.md 4 |
| Step height | 34 (stepUp lifts 34; a second step needs the near-vertical wall test); measured 32 climbs, 36 does not | uu | decoded and measured | physics-walking.md 5; RE+ MoveLabD |
| Floor slope | walkable floor Normal.Z >= 0.7 (45.6 degrees); measured 45 degrees walked up and down at full speed | - | decoded and measured | physics-walking.md 4; RE+ ClimbLabD |
| Floor probe, floor gap | probe 36 below; ideal gap 1.39 to 1.42 | uu | decoded | physics-walking.md 4 |
| Ground friction | 8 (hard-coded); AccelRate is not used on the ground | - | decoded | physics-walking.md 2 |
| Air control | Acceleration.XY clamped to AirControl x AccelRate = 4096; horizontal air speed capped at 417 per sub-step; 3D speed capped at 2500; AirSpeed and MaxFallSpeed are not read | uu/s^2, uu/s | decoded | physics-falling.md 2, 5 |
| Physics sub-step | 0.0003 s minimum, 8 iterations at most | s | decoded | physics-dispatch.md |
| Repositioning speeds | 87 default (fPhysReposLinearSpeed), 180, 220, 300, 380 per action; Vandamme 87 literal | uu/s | decoded | execute-action.md constants, physics-rootmotion-repositioning.md |
| Trace flags 0x2DF | Pawns, Movers, Level, Volumes, Others, Blocking, LevelGeometry, StopAtFirstHit | bits | decoded | collision-traces.md |
| Zone sizes | see the zone table | uu | decoded | def:SBase.SBasePawn |
| ZCDecal | see the collision section | uu | decoded | def:SBase.SPawnAttaque / SPAWNDEFENSE |
| ZipHeight, Pipe4Height | 45, 27.5 | uu | decoded | def:SBase.SBasePawn |
| AccelRate, AirControl, AirSpeed, MaxFallSpeed | 2048, 2.0, 600, 1200 (only AccelRate and AirControl are read, in the air clamp) | uu/s^2, factor, uu/s, uu/s | decoded | def:SBase.SBasePawn; physics-falling.md 2 |

## Network

- Natives that raise movement events run on whichever machine runs the pawn's physics: the owning client
  (autonomous), the server (authority, replaying the client's moves) and, for proxies, the simulated copies. The
  script reacts only on the controlling side (RemoteRole != AutonomousProxy); the server copy follows ChangeState.
- On the server the natives must not re-search GEs for a client's move: they use ZipP1/ZipP2/NewZipP*/m_geoNormal/
  vVandammePos/vWallJumpDir as sent by the client (ServerGetZipP*, ServerSetInfoForVandamme, BigServerMove
  traversal payload; state-and-network.md).

## Edge cases

- The Reloaded.Core community patch replaces three natives the movement depends on (Actor.SetRotation,
  Controller.GotoState, SPawnAttaque.TickNative) and MyServerMoveWithPCRot, and wraps ExecuteAction. Their
  behaviour is decoded from the DLL in spec/native/reloaded-hooks.md (rules 2, 3 and 11) and net-reloaded.md: the
  ExecuteAction wrapper passes every action through, the only change being the fGEZLocation shift of 46 around
  action 234 for the local player's own pawn; Reloaded sets MaxTimeMargin to infinity during MyServerMoveWithPCRot.
  The editor twin has the unpatched versions.
- Several natives write controller or pawn fields that script then reads without a visible assignment (vWallNormal,
  vBackFeetNormal, fZMaxFalling, nJumpNumber, bIsRolling, bCanStop, bReadyFor*, bIsZipSpeed, ZipP*). The port must
  write the same fields at the same points.

## Evidence

- Natives and addresses: the Bridge natives table natives_all.txt (1,066 natives with game exe thunk addresses);
  re/ghidra/annotations/editor.json (editor guard-string names and addresses).
- Script use: local/re-out/uc/SBase/SPlayerController.uc, SBasePawn.uc, SPawnAttaque.uc, SPAWNDEFENSE.uc,
  Engine/Actor.uc, Engine/Pawn.uc, Engine/LevelInfo.uc, SBase/SGETrigger.uc.
- Defaults: local/formats/classdefaults_effective.json.
- GE format and SteeE truth: docs/spikes/C3-ge-list.md, local/formats/ge/SteeE.json.
- Measurements: RE+ docs/MapBuilderPatterns.md, MapBuilderFacts.md; Bridge docs/handoff/traversal_2026-09-28.md;
  memory notes scct-root-motion-lock, scct-anim-channel-timing, ledge-grab-clearance.
- Clip lengths: SCCT_Versus_UE5_Bridge/docs/animations_catalog.json.

## Open questions

6. ZCDecal entries 10 and 14 do not fit the "from * 4 + to" reading, and no decoded GE test reads them (they read
   only `ZCDecal[ZoneConfig * 4]`). Find any other reader before porting them as data only.

Traces: the M1 scenarios (tests/scenarios/movement and traversal) cover the slice moves recorded so far; every
remaining slice move needs one, with Physics, Location, Velocity, the controller state and the action ids per frame.
The sim-versus-game findings are in docs/spikes/R4-sim-vs-game.md.

### Resolved

1. performPhysics / setPhysics dispatch, physWalking + MycalcVelocity, physFalling. Resolved: the dispatch, the
   0.0003 s and 8-iteration limits and the setPhysics side effects are decoded; MycalcVelocity is custom (friction
   8, velocity turned toward the input and capped at GroundSpeed, AccelRate unused on the ground, no input stops the
   pawn on that frame); step 34, floor Normal.Z >= 0.7; air control clamp 4096, horizontal air cap 417, gravity a
   fixed -1500; fZMaxFalling set on entering Falling and raised at the end of every falling call. Rules above and
   in walking.md and jump-fall-land.md. Evidence: physics-dispatch.md, physics-walking.md 2 to 5, physics-falling.md
   2, 5, 8; editor 0x111fc2d0, game 0x10ace4d0 (ANSWERS-physics 1 to 7).
2. ExecuteAction per SA_* for the slice moves. Resolved: all 329 values decoded (314 cases, 15 no-ops). Slice
   summary: walk and crouch actions (75 to 89) set Walking through AnimateWalking; jumps (90, 91) set Falling and
   play the jump start, the velocity comes from MyDoJump; landings (92 to 102) set Walking (101 root motion);
   falls (103 to 106) set Falling with c0 AFall t0.2 (t0.35 for the long fall); roll (130, 131) Walking plus the
   c15 roll clip; ledge (111 to 118) Ledge, root motion L3 with fixed end offsets for the climb-outs (TOPLEDGE 57.8
   forward + 210.8 up through bServerTestRootMotion); pipe (143 to 152) OnPipe holds and root-motion exits
   (TOPPIPE 79.4 + 45 right + 191.5 up, L1); zip (133 to 142) Linear; feet, waist and barrier (171 to 175, 203 to
   205) root motion L3 with end offsets, or Ledge for the detect cases with TypeLedge 3, 1, 2. Evidence:
   execute-action.md section 4 (ANSWERS-actions 1).
3. CheckArmsZone / CheckFeetZone kinds, zones, heights and choice. Resolved: current zones with their radii,
   rotated by the full rotation (arms) or the yaw (feet), PrePivot-corrected; nearest GE by segment distance wins,
   pipe and HOH modes prefer their own kind; one event per call; arms HOH, LG, LADDER, PIPE, ZL; feet LG, LADDER,
   PIPE; the ledge test uses m_LedgeGrabArmsZone. GE queries rules 6 and 7 above. Evidence: physics-ge-queries.md
   2, 3, 4, 6; game 0x10aa4420, eventLedge 0x10ab2dd0 (ANSWERS-physics 8, 11).
4. physLedge, physOnPipe, physLinear, physRepositionning. Resolved: hold-and-snap at 500 uu/s, per-mode turn rates,
   the Acceleration conventions and every TopSynchro, NoGE, ChangeLedge, TopPipe, TopPipeCrouch, TopLadder,
   HandOverHand, OrthoPipeH, ZipLine, Pipe, Ledge and NotifyHitWall site are decoded; the events table and the
   repositioning section above summarise them, the traversal specs carry the per-mode rules. Evidence:
   physics-traversal.md, physics-rootmotion-repositioning.md (ANSWERS-physics 9, 13 to 22, 24, 27, 28).
5. The Reloaded.Core hooks (SetRotation, GotoState, TickNative, MyServerMoveWithPCRot). Resolved by reading the DLL
   (never patched): see Edge cases. Evidence: reloaded-hooks.md rules 2, 3, 11; net-reloaded.md; ANSWERS-actions 12,
   ANSWERS-net move-protocol 9.
