# Falling physics: physFalling, landing, drop-attack homing

Units:
- eng:ASBasePawn::physFalling
- eng:ASBasePawn::bCanLand
- eng:ASBasePawn::processLanded
- eng:AActor::TwoWallAdjust (used)

## Purpose

PHYS_Falling for SBasePawn: air control, a wall pre-probe like walking's, gravity with a half-step integrator,
a horizontal air-speed cap, drop-attack homing and triggering on the server, wall slides, the landing test and the
apex tracking (fZMaxFalling) that fall damage reads.

## Data

| Field | Owner | Default (SBasePawn) | Use |
|---|---|---|---|
| AirControl | Pawn | 2.0 | air acceleration factor |
| AccelRate | Pawn | 2048 | with AirControl gives the air acceleration cap |
| Acceleration | Actor | script | air input; restored to the script's value at the end |
| fZMaxFalling | SPlayerController | 0 | highest Location.Z of the airborne phase |
| fDeathPawnHeight | LevelInfo | map | below it the controller gets FellOutOfWorld |
| m_AttackTarget | SBasePawn | None | drop-attack homing target, recomputed every call |
| Fell_MinHeightForDmg, Fell_MaxHeightForDmg | SBasePawn | spy 600 / 1500, merc 450 / 1000 | a fall that would kill disables the drop attack |
| SPawnType | SBasePawn | | homing only between different types |

## Rules

### 1. Out of the world

If Level.fDeathPawnHeight is non-zero and Location.Z is below it, and the Controller's RemoteRole is not
AutonomousProxy (the controlling side), call Controller.FellOutOfWorld() and carry on.

### 2. Air control (once per call)

1. Save the script acceleration A0 = Acceleration. Acceleration.Z = 0.
2. maxAccel = AirControl * AccelRate (4096 for SBasePawn). With s = |Velocity.XY|: if `s < 10` and
   AirControl > 0, maxAccel += (10 - s) / deltaTime; else if AirControl < 0.05, maxAccel = 0.
3. If `|Acceleration|^2 > maxAccel^2`: Acceleration = SafeNormal(Acceleration) maxAccel.

### 3. Wall pre-probe

As walking (physics-walking.md section 3) with these differences: D = (Velocity + 0.5 Acceleration dt) dt with
Z = 0, start S = Location (no lift), extent (R - 0.2, R - 0.2, H - 10), flags 0xd6, no parallel-wall skip.
On the first counting hit: Bump, processHitWall, remember the wall normal (bHitWall), and remove the normal part
from both Acceleration and Velocity. Then re-probe once more on both sides; a counting hit there: Bump,
processHitWall, Velocity.XY = 0 and Acceleration.XY = 0, and remember it (bSecondWall).

### 4. Drop-attack homing and trigger (authority only)

m_AttackTarget = None; best = 0.92. For every actor in Level.Actors, last to first, that is an SBasePawn with a
Controller whose CanBeDropAttacked() returns true and whose SPawnType differs from this pawn's:

1. Homing: when `Other.Z + 180 < Z`, Velocity.Z < 0 and `|Velocity.X| > 40` or `|Velocity.Y| > 40`:
   T = normalize(Other.Location - Location); V = normalize(normalize(Velocity) + Fwd) with Fwd the pawn's
   rotation vector, after `V.Z -= 3` before the final normalisation. If `T . V >= best` and a zero-extent trace
   from Location to Other.Location + (0, 0, 100) with flags 0x29f hits nothing: best = T . V, m_AttackTarget =
   Other.
2. Trigger: only when the fall would not kill (`fZMaxFalling - Z < Fell_MinHeightForDmg`, or the fall damage
   `(fZMaxFalling - Z - Min) / (Max - Min) * Default.Health` is below Health). Then when
   `|Location - Other.Location| < 2.1 H`, `Fwd . (Other.Location - Location) > 0.7`, `Z - Other.Z < 0.5 H`,
   `(Z - H) - Other.Z + Other.H > -100` and Velocity.Z < -800: a zero-extent trace from Other.Location to
   Location (0x29f) that is clear or hits Other triggers: if Other is bStickySkinned,
   Controller.StickyBlast(Base) and keep scanning; else Controller.DropAttack(Other).

### 5. Sub-step loop

T = deltaTime; while T > 0 and Iterations < 8:

1. Iterations += 1; tick as walking (0.05 s, halving); T -= tick; OldLocation = Location; bJustTeleported =
   false; Vz0 = Velocity.Z.
2. Half-step velocity: `V = V + 0.5 (Acceleration + (0, 0, -1500)) tick` (gravity is fixed; zone gravity is not
   read).
3. Homing pull: with a target, P = Target.Location - Location, X and Y scaled to length 200 when |P| > 200;
   k = min(20, 0.04 (|Target.Velocity| + 100)); `V.X = 0.5 V.X + 0.5 k P.X`, same for Y.
4. Horizontal cap: if `|V.XY| > 417`, V.XY = 417 normalize(V.XY) (Z kept).
5. Move: Delta = V tick; the XY part is zeroed when `Delta.X^2 + Delta.Y^2 < 0.009` (carry C keeps the cut part);
   MoveActor(Delta). Return if bDeleteMe.
6. Hit (Time < 1):
   - The hit actor is an SBasePawn and Velocity.Z changed during the move (an event handled it, for example a
     drop attack): return.
   - Wall (Normal.Z < 0.7): processHitWall; slide `S = (Delta - (Delta . N) N)(1 - Time)`; with a remembered
     wall normal W from earlier this call: when `N . W < 0` or a second wall was found, S = Delta and
     TwoWallAdjust(Delta, S, N, W, Time); remember N. When `S . Delta >= 0`: move S (XY zeroed below 0.009
     squared); a hit with Normal.Z >= 0.7 lands (processLanded(N, Actor, 0, Iterations), return); a wall hit:
     processHitWall, and only for the first such slide TwoWallAdjust(SafeNormal(Delta), S, N, oldN, Time) and
     move again; land when that last move ends on a floor (Normal.Z >= 0.7) or when the two wall normals both
     point up (positive Z), face apart (`N1 . N2 < 0`) and S.Z is 0 (a V-shaped trough). Every landing from a
     slide calls processLanded with remaining time 0.
   - Floor (Normal.Z >= 0.7): remaining = (1 - Time) tick + T. Unless bJustTeleported, when Time > 0.1 and
     tick Time > 0.003: Velocity = (Location - OldLocation) / (tick Time). GroundSpeed = |Velocity.XY|.
     If bCanLand(N, Actor, remaining, Iterations): return.
     Otherwise (no ground under the feet, a narrow top): probe sideways for a steep surface to slide off,
     see 6 below.
7. After a non-landing step, unless bJustTeleported: `Velocity = (Location + C - OldLocation) / tick`; then when
   Velocity.Z < Vz0 or Vz0 >= 0: `Velocity.Z = 2 Velocity.Z - Vz0` (with a free fall this gives
   Vz0 + a tick exactly). Cap: `|Velocity|^2 > 2500^2` gives Velocity = 2500 SafeNormal(Velocity).
8. After the loop: `fZMaxFalling = max(fZMaxFalling, Location.Z)`; Acceleration = A0.

### 6. Narrow-top side probes (in 5.6, when bCanLand fails)

Probe start S = Location - (0, 0, 0.2 H + 8), box 0.9 (R, R, H), flags 0x97. Direction U = the wall normal from
this call (negated) when there was one, else -normalize(Velocity.XY) or (1, 0, 0) when still. Probe to
S + 0.2 R U, then S - 0.2 R U, then along the two perpendiculars. The first probe that hits a steep surface
(Time < 1 and Normal.Z < 0.3) pushes the pawn by 0.1 R along that surface's normal (MoveActor), and when that
push is blocked almost at once (Time < 0.1) the pawn lands (processLanded with the floor hit). When no probe finds
such a surface: processLanded(N, Actor, remaining, Iterations).

### 7. bCanLand(HitNormal, HitActor, remaining, Iterations) and processLanded

Landing probe for both: box 0.9 (R, R, H) from Location to Location - (0, 0, 0.2 H + 8), flags 0x297.

- bCanLand: probe misses: return false. Otherwise, on the controlling side (Controller.RemoteRole is not
  AutonomousProxy): Floor = HitNormal; Controller.MyNotifyLanded(HitNormal, remaining, HitActor); if the script
  put the pawn in Walking, Acceleration = SafeNormal(Acceleration) (unit length); unless now RootMotion,
  startNewPhysics(remaining, Iterations). Return true in both cases; on the server for a remote client nothing is
  called and the pawn stays in Falling on the floor until the client's landing action arrives.
- processLanded (virtual, used by the wall slides): when the probe misses, FindSpot with extent 1.1 (R, R, H) at
  Location; if that gives another spot, FarMoveActor there and return without landing when the pawn actually
  moved. Then Floor = HitNormal; Controller.MyNotifyLanded(HitNormal, remaining, HitActor) (no role test); unit
  Acceleration when Walking; startNewPhysics(remaining, Iterations) unless RootMotion.

### 8. Apex tracking

fZMaxFalling is written in exactly two places: setPhysics sets it to Location.Z when the pawn enters Falling from
another mode, and every physFalling call (and physFallingInRootMotion) raises it to the current Location.Z.
Walking never touches it; the script resets it on mover bumps and after some landings.

## States

None.

## Constants

| Name | Value | Provenance |
|---|---|---|
| gravity | -1500 uu/s^2 (hard-coded) | decoded (game .rdata 0x10c10b78) |
| air speed cap (horizontal) | 417 uu/s | decoded (game also holds 417^2 = 173889) |
| terminal speed (3D) | 2500 uu/s | decoded (6.25e6 compare) |
| air control floor | s < 10, AirControl < 0.05 | decoded |
| wall pre-probe extent | (R - 0.2, R - 0.2, H - 10) | decoded |
| homing | cos 0.92, height 180, speed 40, LOS +100, pull 200, k = 0.04 (v + 100) capped 20 | decoded |
| drop trigger | 2.1 H, 0.7, 0.5 H, -100, Vz < -800 | decoded |
| landing probe | 0.9 (R, R, H), down 0.2 H + 8, flags 0x297 | decoded |
| side probes | 0.2 R, slope 0.3, push 0.1 R, Time 0.1, flags 0x97 | decoded |
| velocity rewrite | Time > 0.1 and tick Time > 0.003 | decoded |
| FindSpot extent | 1.1 (R, R, H) | decoded |

## Network

The homing and drop-attack trigger run only on the authority. bCanLand's landing events run only on the
controlling side; the server copy of a remote player keeps falling physics until the replicated landing action.
The FellOutOfWorld call has the same role test.

## Edge cases

- The air acceleration cap is 4096, so with the script's input (about 3000) air control is never clamped; the 417
  cap is what limits horizontal air speed. A running jump at GroundSpeed above 417 is cut to 417 on the first
  falling tick.
- MaxFallSpeed (1200) is not read; the measured 2110 uu/s after a 1500 drop fits the 2500 cap.
- Acceleration is restored to the script value after the call, so the clamping in section 2 never leaks.

## Evidence

- Editor: physFalling 0x11213090 (vtable 0x120), bCanLand 0x11210450, processLanded 0x11210660 (vtable 0x11c),
  events CanBeDropAttacked 0x111fa300, DropAttack 0x111fa340, StickyBlast 0x111fa4a0, MyNotifyLanded 0x111ff280,
  FellOutOfWorld (name 0x22dbb, through FindFunctionChecked).
- Game: physFalling 0x10ad3dc0 (vtable 0x110), bCanLand 0x10ae4390, processLanded 0x10ae4500 (vtable 0x10c).

## Open questions

1. The forward vector used by the homing and the trigger comes from the pawn rotation (a rotator-to-vector helper,
   editor 0x10e524d0); confirm in a trace that a spy falling backward cannot drop-attack.
2. The trigger's `Fwd . (Other - Location) > 0.7` uses the unnormalised offset; it is effectively "in front".
