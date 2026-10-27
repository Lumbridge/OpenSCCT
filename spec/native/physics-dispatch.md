# Physics dispatch: performPhysics, startNewPhysics, setPhysics

Units:
- eng:ASBasePawn::performPhysics
- eng:ASBasePawn::startNewPhysics
- eng:ASBasePawn::setPhysics
- eng:AActor::setPhysics
- eng:AActor::performPhysics
- nat:Engine.Actor.SetPhysics (script entry, ends in setPhysics)

## Purpose

How a pawn's physics runs each tick: the per-tick wrapper (performPhysics), the mode switch on the Physics byte
(startNewPhysics, which every mode also calls again when it changes mode in the middle of a tick), and what
happens when the mode changes (setPhysics). Every SCCT pawn is an SBasePawn, so the SBasePawn versions are the ones
that run.

## Data

| Field | Owner | Use here |
|---|---|---|
| Physics | Actor (byte at +0x2d8) | selects the mode function |
| OldZ | Pawn | Location.Z at the start of the tick |
| OldAcceleration | Pawn | Acceleration at the end of the tick |
| AvgPhysicsTime | Pawn | running average of deltaTime |
| bSimulateGravity | Pawn | rewritten every tick: true while Physics is Walking or Falling |
| fZMaxFalling | SPlayerController | set to Location.Z when the pawn enters Falling from any other mode |
| bActionsSendByServerMove | SBasePawn | set to true by every setPhysics call (also by a same-mode call to RootMotion) |
| bKeepLocalVandammePos | SBasePawn | cleared by every real mode change |
| bNoReposAtEnd | SBasePawn | read by EndPhysRepositionning |

EPhysics values: 0 None, 1 Walking, 2 Falling, 3 Rotating, 4 Projectile, 5 RootMotion, 6 OnPipe, 7 Linear,
8 OnVerticalPlane, 9 Ledge, 10 Vandamme, 11 Repositionnal, 12 FollowParent, 13 OnLadder, 14 OnPipeH,
15 MovingBrush, 16 Flying.

## Rules

### performPhysics(deltaTime), once per pawn tick

1. Save the start velocity V0 = Velocity; OldZ = Location.Z.
2. startNewPhysics(deltaTime, 0).
3. bSimulateGravity = (Physics == Walking or Physics == Falling), using the mode after the move.
4. If the pawn has a Controller: Controller.MoveTimer -= deltaTime. Then, unless bInterpolating, run
   physicsRotation(deltaTime, V0) when any of these holds: bCrawler (Pawn bit 0x100), Rotation != DesiredRotation
   (any component), RotationRate.Roll > 0, or the pawn is human controlled (Controller with
   bIsAPlayerController).
5. OldAcceleration = Acceleration; AvgPhysicsTime = 0.2 * deltaTime + 0.8 * AvgPhysicsTime.
6. PendingTouch: if set, call its PostTouch(this) when it probes that event, then pop it from the pending list.

### startNewPhysics(deltaTime, Iterations)

Return at once when `deltaTime < 0.0003` or `Iterations > 7`. Otherwise dispatch on Physics:

| Physics | Call | Arguments passed |
|---|---|---|
| 1 Walking | physWalking (virtual) | deltaTime, Iterations |
| 2 Falling | physFalling (virtual) | deltaTime, Iterations |
| 5 RootMotion | physRootMotion | deltaTime |
| 6 OnPipe | physOnPipe | deltaTime |
| 7 Linear | physLinear | deltaTime, 0 |
| 8 OnVerticalPlane | physOnVerticalPlane | deltaTime, 0 |
| 9 Ledge | physLedge | deltaTime, 0 |
| 10 Vandamme | physVandamme | deltaTime, 0 |
| 11 Repositionnal | physRepositionning | deltaTime |
| 12 FollowParent | physFollow | deltaTime, Iterations, 0 |
| 13 OnLadder | physOnLadder | deltaTime |
| 14 OnPipeH | physOnPipeH | deltaTime |
| 16 Flying | physFlying | deltaTime, Iterations |
| 0, 3, 4, 15 | nothing | |

So only Walking, Falling, FollowParent and Flying see the iteration count; the SCCT traversal modes always start at
zero and never sub-step (they do one move per call).

### setPhysics(NewPhysics, NewFloor, NewFloorV)

1. Same mode: no change, except that a call with RootMotion while already in RootMotion still sets
   bActionsSendByServerMove. Return.
2. Leaving Repositionnal (11) on the authority (Role == ROLE_Authority): EndPhysRepositionning() first
   (physics-rootmotion-repositioning.md).
3. Entering Falling from another mode with a Controller: Controller.fZMaxFalling = Location.Z.
4. The actor part (AActor::setPhysics):
   - leaving RootMotion with a skeletal mesh instance: unlock the root motion (lock mode 0);
   - Physics = NewPhysics;
   - Walking, None or Rotating: with no NewFloor find a base under the actor (findBase), else SetBase(NewFloor,
     NewFloorV) when it differs; any other mode clears the base (SetBase(None)).
   - None or Rotating: Velocity = 0 and Acceleration = 0.
5. bActionsSendByServerMove = true; bKeepLocalVandammePos = false.

## States

None. The physics mode is the state; the script changes it through actions (ExecuteAction), the natives through
setPhysics and through the mode functions' own events.

## Constants

| Name | Value | Provenance |
|---|---|---|
| minimum step time | 0.0003 s | decoded (editor 0x112157c0, game 0x10acaaf0, .rdata 0x1149017c / 0x10c11198) |
| maximum iterations | 7 (an eighth call returns) | decoded |
| AvgPhysicsTime filter | 0.2 new, 0.8 old | decoded |

## Network

performPhysics runs wherever the pawn's physics runs: on the owning client for its own pawn, on the server while
replaying a client's moves, and on every machine for simulated proxies (whose Controller is None, which changes the
walking path, see physics-walking.md). setPhysics step 2 runs only on the authority.

## Edge cases

- Mode changes inside a mode function re-enter startNewPhysics with the remaining time and the current Iterations
  (Walking and Falling only); the traversal modes are always entered with zero.
- RootMotion with additional physics calls the other mode functions itself (physics-rootmotion-repositioning.md);
  those calls do not go through startNewPhysics.

## Evidence

- Editor (ChaosTheory_Editor.exe): ASBasePawn::performPhysics 0x11207b70, startNewPhysics 0x112157c0 (vtable
  slot 0x150, jump table at 0x112158a0), ASBasePawn::setPhysics 0x111fd020, AActor::setPhysics 0x1117bd30,
  AActor::performPhysics 0x111803f0.
- Game (SCCT Versus): startNewPhysics 0x10acaaf0 (jump table 0x10acabf4; the game vtable puts physWalking at slot
  0x138 and physFalling at 0x110, five editor-only slots shorter), performPhysics 0x10ace9f0 (vtable slot 0x100),
  setPhysics 0x10ad29a0 (slot 0xfc).
- The game build is link-time optimised: several mode functions take `this` in EAX, ESI or EDI instead of ECX.

## Open questions

1. physicsRotation (editor APawn::physicsRotation 0x1117a6b0, game 0x10a2b4b0) is not decoded here; the SCCT
   script sets Rotation directly in most states, so it matters mainly for the yaw interpolation of the
   traversal modes, which do their own turning.
2. physFollow (FollowParent, grabbed pawns) and physFlying are not decoded.
