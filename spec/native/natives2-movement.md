# physicsRotation, FindBase, FindSpot, moveSmooth, TwoWallAdjust

Units:
- eng:APawn::physicsRotation
- eng:AActor::physicsRotation
- eng:AActor::findBase
- eng:ULevel::FindSpot (with eng:ULevel::CheckSlice)
- eng:AActor::moveSmooth (nat:Engine.Actor.MoveSmooth)
- eng:AActor::TwoWallAdjust

## Purpose

The engine movement helpers that physics-dispatch.md, physics-walking.md and collision-movement.md used on the
UE2 lineage without decoding. They are decoded here from the editor build; the game build has the same code at
the addresses in Evidence. Apart from the points marked "SCCT", they match stock UE2.5.

## Rules

### 1. APawn::physicsRotation(deltaTime, OldVelocity)

Called by performPhysics (physics-dispatch.md rule 4) with the velocity from before the move. Working rotation
R = Rotation; bWalking = Physics is PHYS_Walking or PHYS_RootMotion (5); bFixedRotationDir is cleared on the pawn.

Turning (yaw and pitch):
1. Human controlled pawn: the pawn's bRotateToDesired is copied from its Controller's. When it is set, the yaw
   rate is `round(2 EnemyTurnSpeed dt)` (floor of x + 0.5; EnemyTurnSpeed of the PlayerController, 45000 by default) and
   DesiredRotation.Pitch / Yaw are copied from the Controller's DesiredRotation, then the shared turn below runs.
   When it is clear (the Versus default: no controller sets it), no yaw or pitch turning happens.
2. Other pawns: bRotateToDesired = (Controller exists). Without an enemy in focus the yaw rate is
   `round(dt * clamp(2 |Yaw - DesiredYaw|, RotationRate.Yaw, 2 RotationRate.Yaw))` (16-bit difference folded above
   32768); with Controller.Enemy == Controller.Focus it is the controller's AcquisitionYawRate (or the larger of
   the pawn's and the controller's RotationRate.Yaw once the enemy has been acquired and is visible), and the
   acquired flag is set once the yaw is within 2048 of the target.
3. Shared turn: with bRotateToDesired, Yaw turns toward DesiredRotation.Yaw by at most the rate (fixed-rate turn);
   when not bRollToDesired and walking or falling, DesiredRotation.Pitch = 0; unless (bCrawler and walking) the
   Pitch turns toward DesiredRotation.Pitch by the same rate; a controller with bRotateToDesired gets the pawn's
   new yaw and its own pitch turned toward its DesiredRotation.Pitch.

Roll (checked in this order):
4. bRollToDesired: Roll turns toward DesiredRotation.Roll at the same rate.
5. bCrawler: walking: R = FindSlopeRotation(Floor, R); otherwise pitch and roll turn toward 0 at
   `ftol(RotationRate.Yaw dt)`.
6. Otherwise, the walking lean (SCCT pawns take this path: RotationRate.Roll is 3072 in every Versus pawn class):
   - RotationRate.Roll <= 0 or not PHYS_Walking: Roll = 0.
   - Else r = Roll & 0xffff clamped into the lean window: below 32768 at most RotationRate.Roll, otherwise at least
     65536 - RotationRate.Roll.
   - Speed squared below 40000, or the acceleration `(Velocity - OldVelocity) / dt` squared at most 10000: decay
     with s = min(1, 8 dt): Roll = ftol((1 - s) r) when r < 32768, else ftol(r + s (65536 - r)).
   - Otherwise lean: a = the acceleration in the frame of (Pitch, Yaw, 0); t = a.Y / AccelRate * 2048. Target =
     min(RotationRate.Roll, ftol(t)) when a.Y > 0, else max(65536 - RotationRate.Roll, ftol(t + 65536)).
     Rotation.Roll is reduced to 16 bits and unwrapped toward the target (plus 65536 when the target is above
     32768 and the roll below it, minus 65536 when the target is at most 32768 and the roll above it). Then with
     s = min(1, 5 dt): Roll = ftol((1 - s) Rotation.Roll + s Target).
7. PHYS_FollowParent (12): Pitch = Roll = 0.
8. If R differs from Rotation: MoveActor with a zero delta to R (it can be refused by encroachment).

ftol is the C conversion (truncation toward zero).

### 2. AActor::physicsRotation(deltaTime)

Stock UE2: runs when bRotateToDesired or bFixedRotationDir, and with bRotateToDesired only while Rotation differs
from DesiredRotation. Per axis the step is `int(RotationRate.axis dt)`: with bFixedRotationDir the axis spins by
the step; with bRotateToDesired it turns toward DesiredRotation.axis by at most the step (an axis already equal
is left). A changed rotation is applied by MoveActor with a zero delta; with bRotateToDesired, reaching
DesiredRotation exactly raises EndedRotation.

### 3. AActor::findBase()

Box (CollisionRadius, CollisionRadius, CollisionHeight), flags `0xdf`, from Location to Location - (0, 0, 8).
When the hit actor (possibly none) differs from Base: SetBase(hit actor, hit normal, notify).

### 4. ULevel::FindSpot(Extent, Location) -> bool (actors never checked)

1. If the full Extent does not encroach world geometry at Location: true, unchanged.
2. Zero Extent: false.
3. CheckSlice (rule 5): true when it placed the actor. When it reports that the horizontal slice itself is
   blocked, try the corners: SliceExtent = (0.5 E.X, 0.5 E.Y, 1); for i, j in (-1, 1) (i over X outer, j over Y
   inner, while fewer than two fit), offset O = (0.55 E.X i, 0.55 E.Y j, 0); when the slice fits at
   Start + O, count it and move Location by 1.1 O.
4. None fit: false. Exactly one: Location = 2 Location - Start.
5. If the full Extent fits at Location: line check with Extent, flags `0x86`, from Location toward Start for 0.2
   of the way; a hit moves Location to the hit location. True. Otherwise false.

### 5. ULevel::CheckSlice(Location, Extent, out bTryCorners) -> bool

SliceExtent = (E.X, E.Y, 1); bTryCorners = false.
1. The slice encroaches world geometry at Location: bTryCorners = true, return false.
2. Line check with SliceExtent, flags `0x86`, from Location down 2 E.Z; N = its normal.
3. No hit: Location.Z -= E.Z. Hit with Time <= 0.5 (floor close): Location.Z += 2 (0.5 - Time) E.Z + 1; if the
   full Extent then fits, return true; else move by E.X (N.X, N.Y, 0) and return whether it fits. Hit with
   Time > 0.5: Location.Z = Location.Z - (2 Time - 1) E.Z + 1.
4. (No hit or far floor.) If the full Extent encroaches: move by E.X (N.X, N.Y, 0) and return whether it fits.
   Otherwise line check with the full Extent, flags `0x86`, up E.Z: no hit raises Location by E.Z, a hit moves it
   to the hit location; true.

### 6. AActor::moveSmooth(Delta) -> bool (script MoveSmooth clears bJustTeleported first)

1. MoveActor(Delta). If it was not blocked (Time 1), return its result.
2. GravDir = (0, 0, -1) (a constant: zone gravity is not read); Dir = SafeNormal(Delta); u = GravDir . Dir.
3. When `|Hit.Normal.Z| < 0.2` and `-0.2 < u < 0.5` (a wall, moving roughly level): stepUp(GravDir, Dir,
   (1 - Time) Delta, Hit).
4. Otherwise A = (Delta - Hit.Normal (Delta . Hit.Normal)) (1 - Time). If `Delta . A >= 0`: keep the first normal,
   Dir = SafeNormal(Delta), MoveActor(A); if that is blocked too: SmoothHitWall(normal, actor), TwoWallAdjust(Dir,
   A, new normal, first normal, Time) and MoveActor(A) once more.
5. Return the result of the first MoveActor.

### 7. AActor::TwoWallAdjust(DesiredDir, Delta, HitNormal, OldHitNormal, HitTime)

- `OldHitNormal . HitNormal <= 0` (a corner of 90 degrees or less): NewDir = SafeNormal(HitNormal x OldHitNormal);
  Delta = (Delta . NewDir) (1 - HitTime) NewDir; negated when `DesiredDir . Delta < 0`.
- Otherwise: Delta = (Delta - HitNormal (Delta . HitNormal)) (1 - HitTime); zero when `Delta . DesiredDir <= 0`.

This is the stock rule; physics-walking.md open question 2 is answered: the walking slide uses it unchanged.

## Constants

| Name | Value | Provenance |
|---|---|---|
| walking lean | speed squared 40000, acceleration squared 10000, gain 2048 / AccelRate, blend min(1, 5 dt), decay min(1, 8 dt) | decoded |
| pawn RotationRate | (4096, 20000, 3072) in SBasePawn and both Versus pawn classes | decoded (class defaults) |
| player turn rate | 2 EnemyTurnSpeed (45000) per second, only with bRotateToDesired | decoded |
| findBase | 8 down, cylinder box, flags 0xdf | decoded |
| FindSpot | corners 0.55, move 1.1, slice (0.5, 0.5, 1), pull back 0.2, flags 0x86 | decoded |
| CheckSlice | down 2 E.Z, Time 0.5, +1 lift, push E.X along the floor normal | decoded |
| moveSmooth | gravity (0, 0, -1), wall 0.2, step window -0.2..0.5 | decoded |

## Edge cases

- The lean uses AccelRate, which the SBase script does not change at run time; the roll stays inside +-3072.
- A zero-delta MoveActor for rotation can be refused (the rotation is then not applied).
- FindSpot never tests actors in this build.

## Evidence

- Editor: APawn::physicsRotation 0x1117a6b0, AActor::physicsRotation 0x1117c140, AActor::findBase 0x1117a470,
  ULevel::FindSpot 0x110bea20, CheckSlice 0x110be6a0, AActor::moveSmooth 0x1117b8f0, execMoveSmooth 0x11180190,
  TwoWallAdjust 0x110dc0c0; the gravity constant (0, 0, -1) is the vector table at 0x11693330 + 0x5c.
- Game: APawn::physicsRotation 0x10a2b4b0, AActor::physicsRotation 0x10a2bb20, findBase 0x10a2ae30, FindSpot
  0x10a1b1f0, moveSmooth 0x10a2aa90, execMoveSmooth 0x10a2aa30, TwoWallAdjust 0x109b0630.
- Class defaults from the local class defaults pack (RotationRate, EnemyTurnSpeed, bRotateToDesired).

## Open questions

1. Whether the script overwrites the pawn roll each frame after the lean (SetRotation in PlayerMove): a golden of
   a fast turn while running would show the lean in the recorded pawn rotation.
