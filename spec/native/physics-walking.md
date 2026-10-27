# Walking physics: physWalking, MycalcVelocity, stepUp, CheckForLedges

Units:
- eng:ASBasePawn::physWalking
- eng:ASBasePawn::physWalkingSimulated
- eng:ASBasePawn::PhysWalkingWithChild
- eng:ASBasePawn::MycalcVelocity
- eng:APawn::stepUp
- eng:APawn::processHitWall
- eng:ASBasePawn::CheckForLedges
- eng:AActor::TwoWallAdjust (used, standard)

## Purpose

PHYS_Walking for SBasePawn: turn the scripted Acceleration into a ground velocity, probe for walls just ahead and
slide along them, move in sub-steps with step-up, keep the pawn on the floor, and hand over to the script when the
floor is lost. The same function also serves root motion with additional walking physics and repositioning with
collision, which feed it a ready-made velocity (vDeltaMove) instead of Acceleration.

## Data

| Field | Owner | Use |
|---|---|---|
| Acceleration | Actor | set by script each move; its length is the acceleration in uu/s^2 (up to 3000 from input) |
| GroundSpeed | Pawn | speed cap; the script sets it per frame; this native sets it to 0 when Acceleration is zero |
| vDeltaMove | SBasePawn | velocity to walk with in RootMotion-with-walking (5) and Repositionnal (11) |
| Floor | Pawn | normal of the floor found by the floor probe |
| bCanWalkOffLedges | Pawn | default true for SBasePawn; see CheckForLedges |
| m_AttackTarget | SBasePawn | non-None enables the ledge stop while bCanWalkOffLedges is false |
| bCanJump, bIsWalking, bIsCrouched | Pawn | ledge decision when the floor is lost |
| bNoVelocityUpdate, bJustTeleported | Pawn / Actor | suppress the final velocity rewrite |
| fLastTimeOnAMover | SPlayerController | Level.TimeSeconds while the base is a mover |

Constants used below: G = (0, 0, -1) (the down direction; zone gravity is not read), Down = 36 G.

## Rules

### 0. Early outs and base effects

1. No Controller: return (proxies use physWalkingSimulated through their own callers).
2. Base is a mover (bIsAMover): Controller.fLastTimeOnAMover = Level.TimeSeconds.
3. On the authority, when the base is an SBasePawn that has a Controller and that controller's
   CanBeDropAttacked() returns true: if the base pawn is bStickySkinned, call this Controller's
   StickyBlast(Base) and return; else if this pawn is not crouched, call DropAttack(Base) and return; if crouched,
   UnCrouch(true) first and only drop-attack when it succeeds (otherwise walk on).

### 1. Desired move

- RootMotion with bUseRootMotionAdditionnalPhysics, or Repositionnal: M = vDeltaMove; AccelDir = SafeNormal(M);
  if M is exactly zero, AccelDir = 0 and GroundSpeed = 0. MycalcVelocity is not called.
- Otherwise: Velocity.Z = 0, Acceleration.Z = 0; AccelDir = SafeNormal(Acceleration), or zero with GroundSpeed = 0
  when Acceleration is exactly zero; MycalcVelocity(AccelDir, deltaTime, GroundSpeed, friction 8, bFluid 0,
  bBrake 1); M = (Velocity.X, Velocity.Y, 0).

### 2. MycalcVelocity(AccelDir, dt, maxSpeed, friction, bFluid, bBrake)

1. If bBrake and Acceleration is exactly zero (braking): sub-step the decay `V = V - 2 V f h` with h = 0.03 while
   more than 0.03 s remain, then once with the rest. The result is the time average of the sub-step velocities:
   each sub-step whose velocity still has a positive dot product with the start velocity adds `V h / dt`. Zero it
   when it points against the start velocity or its square is below 100. (With GroundSpeed 0 the cap in step 4 then zeroes the
   velocity anyway, so walking stops on the frame the input stops.)
2. Else: `V = V - (V - AccelDir |V|) dt f` (turns the velocity toward the input direction keeping its speed).
3. `V = V (1 - bFluid dt f) + Acceleration dt`. Acceleration is not clamped to AccelRate.
4. Not human controlled: maxSpeed = maxSpeed * DesiredSpeed. If `|V|^2 > maxSpeed^2`: V = SafeNormal(V) maxSpeed.

From rest with input of size a the speed grows by a dt per tick until GroundSpeed. Speed modes (sneak, walk,
run, fast) exist only as the GroundSpeed and Acceleration the script passes in (input-and-rotation.md); the native
has no mode logic of its own.

### 3. Wall pre-probe (before moving)

Let D = M dt with D.Z = 0. If |D| > 0:

1. Start S = Location - Down / 2 (18 above), extent E = (R - 0.2, R - 0.2, H + 18), flags 0xd6 (no pawns).
   Dir = D / |D|, P = (-Dir.Y, Dir.X, 0), L = D + 0.2 Dir.
2. A hit h "counts" when h.Actor is set and `(h.Normal . L) h.Time < 0.2`.
3. Probe A to S + L + 0.2 P. If it hit (Time != 1) with `|h.Normal . Dir| <= 0.0001`, skip the pre-probe.
4. If A does not count, probe B to S + L - 0.2 P; if B does not count either, skip the pre-probe.
5. With the counting hit h: Bump (below), processHitWall(h.Normal, h.Actor), then slide both the pawn's velocity
   and the move: `Velocity -= (h.Normal . Velocity) h.Normal`, `M -= (h.Normal . M) h.Normal`.
6. D = M dt (Z = 0); stop here if |D| = 0. Recompute Dir, P, L and probe C (+0.2 P) then, if C does not count,
   probe D (-0.2 P). On a counting hit: Bump, processHitWall, and M = 0.

Bump(h): when h.Actor is not bWorldGeometry and this pawn is not based on it: h.Actor.Bump(this), then this pawn's
Bump(h.Actor) (which first offers it to Controller.NotifyBump).

### 4. Sub-step loop

Remaining time T = deltaTime, OldLocation, OldBase, bCheckedFall = false, bJustTeleported = false, carry C = 0.
While T > 0 and Iterations < 8 and the Controller still exists:

1. Iterations += 1; `tick = T` when T <= 0.05, else `tick = min(0.05, T / 2)`; T -= tick; SubLoc = Location.
2. Delta = tick M. If Delta is nearly zero (each component below 0.0001): T = 0 and go to the floor probe.
3. Ledge stop: when bCanWalkOffLedges is false: if m_AttackTarget is None set bCanWalkOffLedges = true; else
   Delta = CheckForLedges(AccelDir, Delta, G, bCheckedFall, bMustJump) and T = 0 when the result is zero.
4. C = Delta; if `|Delta|^2 < 0.009` then Delta = 0; C -= Delta (the part too small to move).
   MoveActor(Delta, Rotation).
5. On a blocking hit (Time < 1): C = 0; DesiredDir = Delta / |Delta|. Only when bCanWalkOffLedges:
   - stepUp(G, DesiredDir, Delta (1 - Time), fresh hit);
   - floor check: box (R, R, H) from Location to Location - (0, 0, 1.52), flags 0xdf; when it finds nothing or a
     floor with Normal.Z < 0.7, undo with FarMoveActor(OldLocation) and slide instead: N = SafeNormal of the
     first hit's normal with Z = 0, `S = (Delta - (Delta . N) N)(1 - Time)`; if |S| > 0.009 stepUp(G, S/|S|, S)
     and repeat the 1.52 floor check, undoing to OldLocation again on failure.
6. If the pawn is now Falling (a stepUp event or script changed it): T += (1 - min(1, |XY moved| / |Delta|)) tick;
   call Pawn.Falling() then Controller.Falling(); a switch to Flying sets Velocity = (0, 0, AirSpeed) and
   Acceleration = (0, 0, AccelRate); startNewPhysics(T, Iterations); return.
7. Floor probe: box (R, R, H) from Location to Location + Down, flags 0xdf. FloorDist = 36 Time. Floor =
   Hit.Normal.
   - When the floor is walkable (Normal.Z >= 0.7), or Delta is nearly zero, or `Hit.Normal . Delta >= 0`:
     - hit with (Hit.Actor != Base or FloorDist > 1.42): MoveActor(Down) (drop to it), then SetBase(Hit.Actor)
       when it changed;
     - else when FloorDist < 1.39: MoveActor((0, 0, 1.405 - FloorDist)) and keep the floor normal (Time 0).
   - Otherwise (a steep surface we are moving into): move by the down vector projected on that surface,
     `F = N (N . (0, 0, 34)) - (0, 0, 34)`, zeroed when `|F|^2 < 0.009`; then SetBase on change.
8. Walkable floor hit (Time < 1, Normal.Z >= 0.7): next iteration. (The UE2 slope slide that follows is dead code
   here: it needs `Normal.Z * 8 < 3.3`.)
9. No walkable floor:
   - bCanJump, not bCheckedFall and the Controller probes MayFall: bCheckedFall = true; Controller.MayFall().
   - Stay on the ledge when not bJustTeleported and (not bCanJump, or (not bCanWalkOffLedges and (bIsWalking or
     bIsCrouched))): Velocity = Acceleration = 0, FarMoveActor(OldLocation), restore OldBase when it was
     bStatic, bWorldGeometry or not bMovable, Controller.MoveTimer = -1, return.
   - FloorDist <= 32: a narrow probe, box (R/2, R/2, H) down 36 with flags 0x2df; any hit keeps walking (next
     iteration).
   - Else (or the narrow probe found nothing): start falling: Tf = remaining time computed as
     `(1 - min(1, |XY moved this step| / |Delta|)) tick + T` (0 when Delta was zero); unless bJustTeleported or
     bNoVelocityUpdate, and when the pawn moved, `Velocity = (Location - OldLocation) / (deltaTime - Tf)`;
     clear bNoVelocityUpdate; Velocity.Z = 0; then on the controlling side (Controller.RemoteRole is not
     AutonomousProxy) call Controller.MyFalling(0.0). Return. The physics mode is not changed here; the script's
     MyFalling plays the fall action, which sets PHYS_Falling.
10. After the loop: unless bJustTeleported or bNoVelocityUpdate, `Velocity = (Location + C - OldLocation) /
    deltaTime`; clear bNoVelocityUpdate; Velocity.Z = 0.

### 5. stepUp(G, DesiredDir, Delta, Hit)

Up = 34 G (so `-Up` lifts 34).

1. The wall is near vertical (`|Hit.Normal.Z| < 0.08`) or walkable (`Hit.Normal.Z >= 0.7`): MoveActor(-Up), then
   MoveActor(Delta) (Delta zeroed when `|Delta|^2 < 0.009`).
   Else, when not walking: MoveActor(Delta + (0, 0, |Delta| Hit.Normal.Z)) (zeroed below 0.009 squared). When
   walking, nothing.
2. Hit after that (Time < 1):
   - Push: human controlled, the hit actor is a pushable Decoration and `Hit.Normal . DesiredDir < -0.9`:
     bNoVelocityUpdate = true, Velocity *= Mass / (Deco.Mass + Mass), processHitWall; return if Falling, else
     continue at the slide below.
   - Another step: `|Hit.Normal.Z| < 0.08` and `Time |Delta|^2 > 144`: MoveActor(Up), then
     stepUp(G (1 - t), DesiredDir, Delta (1 - t), Hit) where t is the Time of that downward move, and return.
     The recursive call gets a shortened step (34 (1 - t)), as in the original.
   - Slide: processHitWall; return if Falling; N = horizontal SafeNormal of the hit normal;
     `S = (Delta - (Delta . N) N)(1 - Time)`; only when `S . Delta >= 0`: MoveActor(S) and on a second hit
     processHitWall, return if Falling, TwoWallAdjust(DesiredDir, S, Normal, N, Time) and MoveActor(S).
3. Always end with MoveActor(Up) (back down by 34).

### 6. CheckForLedges(AccelDir, Delta, G) (only while bCanWalkOffLedges is false and m_AttackTarget is set)

Point probes (zero extent) and small boxes B = (R/4, R/4, H), flags 0x2df unless noted:

1. Front = Delta + R AccelDir (0.75 R when bAvoidLedges). Zero-extent trace Location to Location + Front; any
   hit returns Delta unchanged.
2. Down distance d = max(34, |Delta| + R) + H + 4. Zero-extent trace down d from the front point (flags 0xdf).
   Without bAvoidLedges the front point becomes Location + Delta for the next steps.
3. A floor that is missing or steeper than 0.7, or one deeper than allowed for its slope
   (`min(34, (|Delta| + R) / N.Z sqrt(1 - N.Z^2)) + H + 4 < d Time`): box trace from Location to the point,
   then down 38 with B; a walkable floor (Time < 1, Normal.Z >= 0.7) returns Delta.
4. Otherwise try to sidestep: with Dir = SafeNormal(point - Location) and its perpendicular, box-trace sideways by
   |Delta| each way, then down 38; the first side with a walkable floor gives `SafeNormal(side - Location) |Delta|`;
   if neither side works return zero (the pawn stops at the edge).

### 7. physWalkingSimulated(deltaTime, Iterations) (proxies in root motion with walking)

Same sub-step rule with M = vDeltaMove, all three axes (Z is not cleared). Per sub-step:

1. Delta = tick M. Nearly zero (each component below 0.0001): T = 0 and no move. Else MoveActor(Delta) and, on a
   hit, stepUp(G, Delta / |Delta|, Delta (1 - Time), hit).
2. Floor probe: box (R, R, H) from Location to Location + Down (36), flags 0xdf; FloorDist = 36 Time; Floor =
   Hit.Normal (zero with no hit).
3. Walkable floor (Normal.Z >= 0.7), or Delta nearly zero, or `Normal . Delta >= 0`:
   - no hit, or (Hit.Actor == Base and FloorDist <= 1.42): when FloorDist < 1.39 lift by `(0, 0, 1.405 - FloorDist)`;
     with no hit nothing moves;
   - otherwise (another actor, or farther than 1.42): MoveActor(Down). There is no SetBase here.
4. Else (a steep surface moved into): MoveActor(`N (N . (0, 0, 34)) - (0, 0, 34)`), with no tiny-move cut.

No pre-probe, no ledge logic, no events. Velocity = (Location - start) / deltaTime.

PhysWalkingWithChild just calls physWalking.

### 8. processHitWall(HitNormal, HitActor) (APawn, virtual; called by every mode above)

1. No HitActor, or HitActor is a Pawn: nothing (walls only).
2. bDirectHitWall or no Controller: HitWall(HitNormal, HitActor) (the actor event), done.
3. In Repositionnal: Controller.NotifyHitWall(HitNormal, HitActor); true ends it.
4. Only while Acceleration is non-zero (or in Repositionnal): Dir = SafeNormal(Velocity); when walking, both
   HitNormal and Dir lose their Z and are renormalised. HitNormal is the caller's variable, so walking callers
   continue (slide, TwoWallAdjust) with the horizontal normal.
5. When `Dir . HitNormal <= Controller.MinHitWall` (-0.98 for SPlayerController: nearly head-on) and
   Controller.NotifyHitWall returns false: unless Falling, MoveActor((0, 0, -34)) (with the UE2 crouch-walk retry
   for non-human pawns that can crouch), then HitWall(HitNormal, HitActor).

## States

None.

## Constants

| Name | Value | Provenance |
|---|---|---|
| ground friction (hard-coded, zone value not read) | 8.0 | decoded |
| braking sub-step | 0.03 s | decoded |
| braking stop threshold | speed^2 < 100 | decoded |
| sub-step | min(0.05, T/2) when T > 0.05 | decoded |
| floor probe depth | 36 | decoded (game .rdata 0x10c10f58) |
| step height (stepUp) | 34 | decoded (game .rdata 0x10c10f34); measured 32 climbs, 36 does not |
| pre-probe side offset and radius shrink | 0.2 (a .data value, never written) | decoded (editor 0x11626868, game 0x10c437dc) |
| pre-probe lift and extent | +18 (Down/2), Z extent H + 18 | decoded |
| parallel-wall tolerance | 0.0001 | decoded |
| walkable floor | Normal.Z >= 0.7 | decoded |
| floor distance band | lift below 1.39 to 1.405; re-drop above 1.42 | decoded |
| after-step floor check depth | 1.52 (1.42 + 0.1) | decoded |
| narrow ledge probe | FloorDist <= 32, box (R/2, R/2, H) | decoded |
| tiny-move cut | squared length < 0.009 | decoded |
| stepUp near-vertical wall | `|N.Z| < 0.08`; second step when Time |Delta|^2 > 144 | decoded |
| push threshold | Normal . Dir < -0.9 | decoded |
| CheckForLedges | front R (0.75 R with bAvoidLedges), down max(34, |Delta|+R)+H+4, then 38 | decoded |
| trace flags | pre-probe 0xd6, floor 0xdf, clearance 0x2df | decoded |

## Network

The owning client runs this with its own input; the server runs it while replaying the client's moves
(MyServerMove calls the state's MyProcessMove, then physics). MyFalling is called only where the Controller's
RemoteRole is not AutonomousProxy, so on the server a remote player's pawn never raises it; the server's copy
falls when the client's fall action arrives.

## Edge cases

- Walking stops dead when input stops: GroundSpeed is set to 0 with zero Acceleration and the speed cap zeroes the
  velocity.
- The pre-probe ignores pawns (flags 0xd6); pawns are only met by MoveActor itself.
- With zero acceleration the floor probe still runs, so a standing pawn follows a moving floor.
- The original MoveActor writes its last argument (an out vector added into C here) as `Delta - Delta`, which is
  always zero in the editor build; treat C as only the sub-0.009 remainder.

## Evidence

- Editor: physWalking 0x112025e0, physWalkingSimulated 0x11202080, PhysWalkingWithChild 0x111f8be0,
  MycalcVelocity 0x111fc2d0, APawn::stepUp 0x1117d2a0 (vtable 0x12c), APawn::processHitWall 0x1117d920
  (vtable 0x118), ASBasePawn::CheckForLedges 0x111face0, AActor::TwoWallAdjust 0x110dc0c0.
- Game: physWalking 0x10ac6b40 (vtable 0x138), physWalkingSimulated 0x10ac5db0 (re-read 2026-10-26), MycalcVelocity
  0x10ace4d0 (braking average re-read 2026-10-26), AActor::findBase 0x10a2ae30 (8 down, box (R, R, H), flags 0xdf,
  SetBase on change, Floor not written),
  stepUp 0x10a2c460 (vtable 0x11c), processHitWall 0x10a2ced0 (vtable 0x108), TwoWallAdjust 0x109b0630;
  CheckForLedges is inlined into physWalking.
- Matched by constant fingerprints (re/ghidra/scripts/physics/match3.py) and by vtable slot shift.
- Re-read with REA (Ghidra 12.1.4, 2026-10-26): MycalcVelocity (the braking time average, the stop test `V . V0 <
  0` or `|V|^2 < 100`, the DesiredSpeed cap for a controller without bIsAPlayerController, Actor 0x2f4 bit 16) and
  physWalkingSimulated (at most 8 sub-steps, the 1.39 / 1.405 / 1.42 band against Base, the steep slide) match
  sections 2 and 7.

## Open questions

1. Trace the ramp: from rest with full input, |Velocity| per frame should be `3000 n / 60` capped at
   GroundSpeed (input 6000 * 0.5, see walking.md). A measured curve would confirm that Acceleration reaches the
   native unscaled.
2. Answered: AActor::TwoWallAdjust (game 0x109b0630) is the UE2 one. With `HitNormal . OldHitNormal > 0` the rest
   slides along HitNormal times (1 - HitTime) and is zeroed unless it keeps a positive dot with DesiredDir; else it
   runs along `SafeNormal(HitNormal x OldHitNormal)` times (1 - HitTime), negated when it points against DesiredDir.
3. APawn::walkMove (editor 0x110dcd50) is the UE2 navigation step used by AI MoveTo; player pawns never reach it
   in Versus, so it is not decoded.
