# Root motion, repositioning and Vandamme physics

Units:
- eng:ASBasePawn::physRootMotion
- eng:ASBasePawn::physFallingInRootMotion
- eng:ASBasePawn::EndRootMotion
- eng:ASBasePawn::bServerTestRootMotion
- eng:ASBasePawn::physRepositionning
- eng:ASBasePawn::EndPhysRepositionning
- eng:ASBasePawn::physVandamme

## Purpose

PHYS_RootMotion (5) moves the pawn by the animation's root bone, with or without collision and optionally through
another mode ("additional physics"); EndRootMotion snaps the pawn to the pre-computed end of the move.
PHYS_Repositionnal (11) slides the pawn in a straight line to a target spot and facing (the "repos" actions before
traversal clips) and calls TopSynchro on arrival. PHYS_Vandamme (10) holds the brace between two walls.

## Data

| Field | Owner | Use |
|---|---|---|
| iLockRootMotionType | SBasePawn | lock mode given to the mesh instance; bit 2 also applies the root rotation |
| fLastRootMotionTimeCalled | SBasePawn | last Level.TimeSeconds processed (root motion) / next allowed time (repos) |
| vDeltaMove | SBasePawn | this frame's root translation; for walking-with-root-motion and repos it becomes a velocity |
| bUseRootMotionAdditionnalPhysics | Actor (0x2f4 bit 0x1000) | collision on during root motion (RMColOn sets it) |
| RootMotionAdditionnalPhysics | SBasePawn (byte 0xab0) | mode used for the additional physics |
| vSpeedAfterRM, vMoveAfterRM, rRotAfterRM | SBasePawn | velocity, offset and yaw applied by EndRootMotion |
| vLocationBeforeRM, rRotBeforeRM | Pawn | start location and yaw of the move |
| bReverseRM | SBasePawn | EndRootMotion returns to the start instead |
| vVandammePos, vWallJumpDir | SBasePawn | repos target location and facing direction (also the Vandamme target) |
| fPhysReposLinearSpeed | SBasePawn | repos speed, 87 by default |
| bUseAdditionnalPhysicsForRepos | SBasePawn | repos walks with collision instead of sliding through |
| bNoReposAtEnd | SBasePawn | skip the final snap of EndPhysRepositionning once |

## Rules

### physRootMotion(dt)

1. Needs a skeletal mesh with a skeletal mesh instance; otherwise Velocity = Acceleration = 0 and return.
2. If the instance has no root lock yet, lock it with iLockRootMotionType.
3. Now = (float) Level.TimeSeconds. If `Now < fLastRootMotionTimeCalled + 0.0001`, return (one update per game
   time step, even if physics runs twice).
4. OldLocation = Location; save bCollideWorld and bCollideActors. Advance the mesh (the instance's root update),
   then vDeltaMove = the root location delta and R = the root rotation delta.
5. Without bUseRootMotionAdditionnalPhysics: collision fully off (SetCollision(false, ...) and bCollideWorld =
   false); MoveActor(vDeltaMove, Rotation, plus R when iLockRootMotionType has bit 2); restore both flags.
6. With it, by RootMotionAdditionnalPhysics:
   - 0 None: MoveActor((0, 0, vDeltaMove.Z), Rotation) then MoveActor((vDeltaMove.X, vDeltaMove.Y, 0),
     Rotation, plus R with bit 2). Collision stays on.
   - 1 Walking: with bit 2 first MoveActor(0, Rotation + R) (rotate only); Rotation.Roll = 0; vDeltaMove.Z = 0;
     vDeltaMove /= dt; then physWalking(dt, 0), or physWalkingSimulated(dt, 0) without a Controller.
   - 2 Falling: physFallingInRootMotion(dt, 0, R or zero).
   - 6 OnPipe: Acceleration = 0, physOnPipe(dt), then with collision off MoveActor(vDeltaMove, Rotation + R),
     collision restored.
   - 7 physLinear, 8 physOnVerticalPlane, 9 physLedge, 10 physVandamme, 13 physOnLadder, 14 physOnPipeH,
     16 physFlying, each with (dt, 0). Other values: nothing.
7. Unless bJustTeleported, and when the pawn is not a simulated proxy or has additional physics:
   `Velocity = (Location - OldLocation) / (Now - fLastRootMotionTimeCalled)`.
8. fLastRootMotionTimeCalled = Now.

### physFallingInRootMotion(dt, Iterations, R)

1. FellOutOfWorld test as physFalling.
2. On the authority: the drop-attack trigger of physFalling (section 4.2) with two different bounds:
   `-100 <= Z - Other.Z` and `(Z - H) - Other.Z + Other.H > 50`; the first triggering target ends the call
   (StickyBlast(Base) or DropAttack(Other)).
3. MoveActor((0, 0, vDeltaMove.Z), Rotation), then MoveActor((vDeltaMove.X, vDeltaMove.Y, 0), Rotation + R).
4. On a hit: Normal.Z <= 0.707 calls Controller.NotifyHitWall(Normal, Actor), else
   Controller.MyNotifyLanded(Normal, (1 - Time) dt, Actor).
5. fZMaxFalling = max(fZMaxFalling, Location.Z).

### EndRootMotion (virtual, called when the root-motion clip ends)

Runs only for Role >= AutonomousProxy.
1. vSpeedAfterRM non-zero: Velocity = vSpeedAfterRM; vSpeedAfterRM = 0.
2. vMoveAfterRM and rRotAfterRM both zero: return.
3. Normal end: MoveActor(0, (0, rRotBeforeRM + rRotAfterRM, 0)) to set the final yaw; if vMoveAfterRM is zero go
   to 5; else Destination = vLocationBeforeRM + vMoveAfterRM.
4. bReverseRM: MoveActor(0, (0, rRotBeforeRM, 0)); Destination = vLocationBeforeRM.
5. FarMoveActor(Destination, test 0, noCheck 1, attachedMove 1) (no collision test); bReverseRM = false.

The end offsets in vMoveAfterRM are prepared by script from the SBasePawn constants (native-contracts.md, root
motion section), already turned into world space.

### bServerTestRootMotion(Action, vMove, Yaw) (server)

For action 113 (0x71) with the mesh check passing, the server re-runs TestTopLedge(false) and refuses the move
when it does not return 1: it sends ClientStopRM(113) and returns false. Otherwise vMoveAfterRM = vMove,
rRotAfterRM = the rounded yaw, setPhysics(PHYS_RootMotion), return true.

### physRepositionning(dt)

1. Now = Level.TimeSeconds; if `Now < fLastRootMotionTimeCalled` return; fLastRootMotionTimeCalled = Now + dt.
2. D = vVandammePos - Location; D.Z = 0 when `|D.Z| < 5` or bUseAdditionnalPhysicsForRepos.
3. step = fPhysReposLinearSpeed dt; Move = D when `|D| <= step` or `|D| <= 0.1`, else step SafeNormal(D).
4. Yaw: TargetYaw = vWallJumpDir.Rotation().Yaw; NewYaw = FixedTurn(current, TargetYaw, 3000) (3000 per call,
   not scaled by dt); pitch and roll 0.
5. Without bUseAdditionnalPhysicsForRepos: bCollideWorld = false, MoveActor(Move, NewRotation), then bCollideWorld
   = true (always true afterwards). With it: MoveActor(0, NewRotation); vDeltaMove = (Move.X, Move.Y, 0) / dt;
   Rotation.Roll = 0; physWalking(dt, 0).
6. Velocity = (Location - start) / dt.
7. Arrival: D recomputed as in 2; when `|D| < 1` and NewYaw equals TargetYaw (16-bit compare): Controller.TopSynchro().
8. Acceleration = 0.

### EndPhysRepositionning (setPhysics leaving mode 11, authority)

1. bNoReposAtEnd: clear it and return.
2. D = vVandammePos - Location (Z = 0 with bUseAdditionnalPhysicsForRepos); with bCollideWorld off,
   MoveActor(D, (0, vWallJumpDir.Rotation().Yaw, 0)); bCollideWorld = true; Velocity = Acceleration = 0.

### physVandamme(dt, Iterations)

1. D = vVandammePos - Location (3D); Move = D when `|D| <= 87 dt` or `|D| <= 0.1`, else 87 dt SafeNormal(D). The
   speed 87 is a literal, not fPhysReposLinearSpeed.
2. Facing: Fwd = the pawn's forward axis, W = vWallJumpDir; U = SafeNormal(W x Fwd); F = U x W (Fwd with its W
   component removed); NewYaw = FixedTurn(Yaw, F.Rotation().Yaw, 3000).
3. MoveActor(Move, (Pitch, NewYaw, Roll)).
4. Velocity = (Location - start) * dt (multiplied, not divided: the original stores a tiny velocity here);
   Acceleration = 0.

FixedTurn(current, target, rate): the 16-bit yaw moves toward the target by at most `rate` units along the shorter
way and snaps when closer (editor 0x10fdeef0).

## States

None.

## Constants

| Name | Value | Provenance |
|---|---|---|
| root motion time guard | 0.0001 s | decoded |
| repos speed | fPhysReposLinearSpeed (87 default) | decoded field, default from SBasePawn defaults |
| repos vertical dead band | 5 | decoded |
| repos / Vandamme / ledge / zip turn rate | 3000 yaw units per call | decoded |
| Vandamme speed | 87 (literal) | decoded |
| arrival | distance < 1 and yaw equal | decoded |
| server-tested action | 113 | decoded |
| landing split in root-motion falling | Normal.Z > 0.707 | decoded |

## Network

The client and the server both run root motion and repositioning. The server's EndPhysRepositionning snap runs
only on the authority. bServerTestRootMotion is the server's check of a client-requested root motion (ledge
pull-up). TopSynchro is raised wherever the pawn physics runs with a Controller; the script reacts on the
controlling side.

## Edge cases

- The 3000-per-call turn rates do not scale with frame time: at 60 Hz a 180 degree turn (32768) takes 11 frames.
- Repositioning without additional physics turns bCollideWorld on at the end of every call, even if it was off.
- The root-motion velocity is the real displacement over the elapsed game time, so the server sees sensible
  velocities for remote pawns in root motion.

## Evidence

- Editor: physRootMotion 0x112128c0 (additional-physics switch table at 0x11212ee0), physFallingInRootMotion
  0x1120c440, EndRootMotion 0x111fd160 (vtable 0x144), bServerTestRootMotion 0x11215580, physRepositionning
  0x111fc7a0, EndPhysRepositionning 0x111fcbc0, physVandamme 0x111fb380.
- Game: physRootMotion 0x10acac40, physFallingInRootMotion 0x10ad38d0, EndRootMotion 0x10ad65e0 (vtable 0x130),
  physRepositionning 0x10ad2220, physVandamme 0x10ac8d20.

## Open questions

1. The root update called before reading the root delta (editor 0x111015f0) is not decoded; it advances the
   animation channels. Root delta semantics (per frame, relative to the lock) need a trace.
2. physVandamme's velocity scaling by dt looks like an original bug; confirm with a trace (Velocity during a
   Vandamme brace should be near zero).
