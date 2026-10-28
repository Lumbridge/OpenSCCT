# Player input, speed mode and view/pawn rotation

Units:
- uc:SBase.SPlayerController:PlayerTick (input and control part only; the gadget, smoke and HUD parts belong to other specs)
- uc:SBase.SPlayerController:ComputeCrouchOrRoll
- uc:SBase.SPlayerController:ChangeSpeed
- uc:SBase.SPlayerController:GetSpeedMode
- uc:SBase.SPlayerController:UpdateRotation
- uc:SBase.SPlayerController:UpdateRotationAttaquant_Mouse
- uc:SBase.SPlayerController:UpdateRotationDefenseur_Mouse
- uc:SBase.SPlayerController:UpdateRotationDefenseur
- uc:SBase.SPlayerController:UpdateCamRotation
- uc:SBase.SPlayerController:UpdateLimitedRotation
- uc:SBase.SPlayerController:ClampRotation
- uc:SBase.SPlayerController:UpdatePawnRotationWeapon
- uc:SBase.SPlayerController:UpdatePawnRotationMario
- uc:SBase.SPlayerController:UpdatePawnRotationMarioJump
- uc:SBase.SPlayerController:fGetForwardForPipe
- uc:SBase.SPlayerController:UpdateDesiredRotation
- uc:SBase.SPlayerController:ControleAccelerationPad
- uc:SBase.SPlayerController:ControleSmoothXYPad
- uc:SBase.SPlayerController:ControleSmoothNormePad
- uc:Engine.PlayerController:Jump
- uc:Engine.PlayerInput:PlayerInput
- nat:SBase.SPlayerController.NativeComputeControlVariables
- nat:SBase.SPlayerController.NativePlayerInput
- nat:SBase.SPlayerController.NativeRotationCameraDefenseur
- def:Engine.PlayerInput
- rep:SBase.SPlayerController.MyDesiredRotation

## Purpose

Turns the player's buttons and mouse into the values every movement state reads: `aForward`, `aStrafe`, the
movement bitmask `iMove`, the turn and look values `fTurnValue` / `fLookUpValue`, the jump, crouch and roll
requests, and the spy's slow/fast mode. It also owns the three rotation models the movement states use: the
controller (view) rotation, the pawn yaw that turns towards the stick direction for the spy ("Mario" movement),
and the pawn yaw that copies the view for weapon movement.

## Data

| Field | Owner | Type | Default | Notes |
|---|---|---|---|---|
| aForward, aStrafe, aTurn, aLookUp, aUp, aMouseX, aMouseY, aBaseY | Engine.PlayerController | input float | 0 | written by the native input code each frame |
| BT_Up, BT_Down, BT_Left, BT_Right, BT_NextItem, BT_PrevItem | Engine.PlayerController | input byte | 0 | W, S, A, D are bound to these buttons, not to axes (`Default_kbd_0.ini`) |
| bCrouch, bRoll, bUsing, bUseBackToWall, bFireMode, bHoldBreath, bUseGadget, bReload, bROF, bSpecialsChat, bZoomIn, bZoomOut | SPlayerController | input byte | 0 | `bUsing`, `bUseBackToWall`, `bFireMode` are bound with `InvButton` |
| bPressedJump | Engine.PlayerController | bool | false | set by the `Jump` exec function, cleared by the movement states |
| bFastSpeed | SPlayerController | bool | true (set in PostBeginPlay) | spy only; toggled by `ChangeSpeed` (middle mouse); WheelUp sets it and WheelDown clears it when the profile's mouse option is not 0 |
| bCrouchWithRoll | SPlayerController | bool | false (set true for a spy at spawn) | the "want to be crouched" latch |
| bCanComputeRollButton | SPlayerController | bool | false | edge detector for the crouch button |
| bRollAction | SPlayerController | bool | false | roll request for this frame |
| fNormePadMove, fNormePadCam, fNormePadCamSmoothee | Engine.PlayerController | float | 0 | stick magnitudes 0..1, native |
| fTurnValue, fLookUpValue | Engine.PlayerController | float | 0 | per-frame turn and look inputs, native |
| fPadXMaxLu, fPadYMaxLu | Engine.PlayerController | float | 500, 500 | def: SPlayerController |
| iMove, iOldMove, iRealMove | SPlayerController | int | 0 | bitmask of cMove* flags (native writes iMove) |
| BT_UseJusteAppuye | SPlayerController | bool | false | "Use pressed this frame", native |
| fSensibiliteSouris | Engine.PlayerController | float | 1.0 | `SPlayerProfile.MouseSensitivity / 10`; the profile clamps MouseSensitivity to 4..40, default 10 |
| bInvertHorizontalAxisCamSpy, bInvertVerticalAxis | SPlayerController | bool | false | from the profile (InvertX, InvertY) |
| bBinocularVision | SPlayerController | bool | false | halves all view rotation rates |
| MyDesiredRotation, LocalMyDesiredRotation | SPlayerController | Rotator | (0,0,0) | the view rotation the server is told about |
| LastDesiredRotationUpdate | SPlayerController | float | 0 | |
| fTimeWhenHit, AdrenalineTime, AdrenalineFactor | SPlayerController | float | -, 5.0, 1.2 | def: SPlayerController |
| m_LastRollingTime, fTimeBetweenTwoRolls | SPlayerController | float | -10.0, 0.5 | def: SPlayerController |
| bJammered, fTimeToStopJammer, fJammerDuration | SPlayerController | bool, int, int | false, 0, 4 | |
| iDontUpdatePadInput, iTimeToStopStickyShock | Engine.PlayerController / SPlayerController | int | 0 | sticky shock input lock |

Movement bitmask constants (decoded, SPlayerController consts): cMoveNone 0, cMoveLeft 1, cMoveRight 2,
cMoveForward 4, cMoveBack 8, cMoveUp 16, cMoveDown 32, cMinPadValue 3000, AXIS_MAX 900.

PlayerInput defaults (def:Engine.PlayerInput): MouseSmoothingMode 1, MouseSmoothingStrength 0.3,
MouseSensitivity 3.0, MouseSamplingTime 0.008333, MouseAccelThreshold 100.0, DoubleClickTime 0.25. The script
`PlayerInput` event is empty in this game; all sampling is native.

## Rules

### Frame order (global PlayerTick)

1. On a client, copy `bAvlb_CinematicCamera` into the GRI. If the free camera is flying, clear bFire, bAltFire,
   bUsingNow, TimerUSe, fThrowForce. If the console is open, clear the same plus BT_Up, BT_Down, BT_Left,
   BT_Right and hide the gadget choice.
2. If `fTimeWhenHit > 0` and `fTimeWhenHit + AdrenalineTime < TimeSeconds`, set fTimeWhenHit to -10 and stop the
   heartbeat sound.
3. Call the native `NativeComputeControlVariables(DeltaTime)`. It produces aForward, aStrafe, iMove,
   fTurnValue, fLookUpValue, fNormePadMove, fNormePadCam and BT_UseJusteAppuye for this frame (decoded in
   input.md 1 to 3; it runs every live PlayerTick unless iDontUpdatePadInput is set, which zeroes the inputs):
   - with k = GetSpeedMode(): `aForward = (BT_Up - BT_Down) x 6000 x k`, `aStrafe = (BT_Right - BT_Left) x 6000 x k`
     (one key gives exactly 6000 k, a diagonal 6000 k on both axes, no normalisation in the native; analog axis
     values the bindings wrote are overwritten);
   - `aTurn = viewport width x aMouseX / 640`, `aLookUp = viewport height x aMouseY / 480`;
     `fTurnValue = clamp(0.75 aTurn, -2000, 2000)`, `fLookUpValue = clamp(0.75 aLookUp, -2000, 2000)`, negated
     with bInvertVerticalAxis;
   - `fNormePadMove = (|BT_Right - BT_Left| + |BT_Up - BT_Down|) x k`, capped at 1; `fNormePadCam = |(aTurn,
     aLookUp)| / 6000`;
   - BT_UseJusteAppuye is the rising edge of bUsing;
   - iMove bits (pawn-relative): 1 left, 2 right (camera-relative move against the pawn's left axis, +-2100),
     4 forward (along the facing above 4500, 3000 when bit 4 was set last frame), 8 backward (below -4500),
     16 and 32 stick up and down (aForward against +-3000 when walking, falling or for the merc; a screen-space
     test against 0.45 in the spy's climbing modes). On a ledge or fence only bits 1, 2, 4, 8, from the raw stick
     against +-2100.
   The measured speeds 423.5, 169.4, 221.375, 346.5 and 116.91 all follow, the last through int(0.5 x 1823.4) = 911.
4. Return if there is no SBPawn.
5. Jammer: while `bJammered`, when `TimeSeconds > fTimeToStopJammer` play action SA_JAMMER_STOP and clear
   bJammered; otherwise scale `aForward` and `aStrafe` by
   `max(0.5, 1 - (fTimeToStopJammer - TimeSeconds) / fJammerDuration)` and clear bPressedJump.
6. Sticky shock: if `iDontUpdatePadInput != 0` and `TimeSeconds > iTimeToStopStickyShock`, clear the lock, play
   SA_DMG_ELECTRIC_STOP_ANIM, end the merc weapon's electric state, and if `bTazered` was set clear it and, when
   in one of s_PlayerWalkingSt2H, s_PlayerWalkingSt0H, s_PlayerWalkingCr0H, s_PlayerWalkingCr2H, change state to
   `s_FallDown` label `DIE_InPlace`. Otherwise (no lock to release) run `ComputeCrouchOrRoll`.
   So crouch and roll requests are not read while the sticky-shock input lock is held.
7. The rest of PlayerTick (gadgets, fire mode, smoke, HUD) then runs, and finally, in this order:
   `ClientUpdatePosition` if `bUpdatePosition`, then `PlayerMove(DeltaTime)` of the current state, then the
   specials chat, then every 0.14 s `UpdatePositionForServer(Location, Rotation)` (unreliable), then the idle
   timer for the objective reminder (fLastTimeMoved).
8. Spy fire-mode button (`bFireMode`) while unarmed: holding it 0.5 s sets bForceFireMode with
   bForceLaserFireMode false; releasing it before 0.5 s sets bForceFireMode with bForceLaserFireMode true. The
   walking states read bForceFireMode to draw the weapon (see walking.md). While armed, releasing it sets
   bTestBoutonLache, which the armed states read to holster.
9. Spy `bUnCrouchAndUnDraw`: if the state's `CanUncrouchAndUndraw()` is false it is cleared. Otherwise, when set:
   crouched clears bCrouchWithRoll (stand up); standing and armed plays SA_WALK_ST_2Hto0H and changes to
   `s_PlayerWalkingMario_T_2HToOH`; standing and unarmed clears it and launches a pending quick gadget.

### Crouch and roll latch (ComputeCrouchOrRoll)

1. When `bCrouch > 0` and `bCanComputeRollButton` is false: set bCanComputeRollButton and toggle
   `bCrouchWithRoll`. When `bCrouch == 0`: clear bCanComputeRollButton. The crouch key is therefore a toggle that
   flips once per press.
2. When `bRoll > 0`, set `bRollAction` unless any of these holds: pawn is a merc; pawn physics is not
   PHYS_Walking; WeaponLoaded > 0; `fNormePadMove < 0.3`; `|Pawn.Velocity| < 65`;
   `m_LastRollingTime >= TimeSeconds - fTimeBetweenTwoRolls`. bRollAction is consumed and cleared by the walking
   state the same frame (walking.md).

### Jump request

`Jump` (bound to Space through the alias `Function Jump | Axis aUp Speed=+280.0`) sets `bPressedJump` when the
player is not in inventory mode. Each movement state reads and clears it inside PlayerMove.

### Speed mode (GetSpeedMode, ChangeSpeed)

`SPlayerController.PostBeginPlay` sets `bFastSpeed` to true on every machine the controller exists on (the server and
the owning client), so a spy enters the level running. Nothing resets it on death or respawn: the controller outlives
its pawns, so a respawned spy keeps the speed he had. `ChangeSpeed` toggles `bFastSpeed` for a spy only. `GetSpeedMode` is an event read by native code (no script
caller) and returns, in this order of tests:

| Condition | Result |
|---|---|
| bBinocularVision | 0.8 |
| no SBPawn | 1.0 |
| pawn is a merc | 1.0 |
| bFastSpeed | 1.0 |
| in state s_PlayerWalkingWeapon or a child of it | 0.6 |
| otherwise | 0.4 |

The measured spy speeds fit this factor being applied to the stick values before the walking states clamp them
(run 423, slow 169 = 0.4 x 423; crouched 221, slow 89 = 0.4 x 221). Which native applies it is an open question.

### View rotation (UpdateRotation)

1. Copy LocalMyDesiredRotation into MyDesiredRotation.
2. If `bLocalShootedPawn != bShootedPawn`, copy it and set fDateLastShootedPawn = TimeSeconds.
3. Merc: `UpdateRotationDefenseur_Mouse(dt)`, which is the same as `UpdateRotationAttaquant_Mouse(dt)`. Spy:
   `UpdateCamRotation(dt)`.
4. aTurnPrevious = aTurn; LocalMyDesiredRotation = MyDesiredRotation.

`UpdateRotationAttaquant_Mouse(dt)` (used by the merc), with r = 0.5 under binoculars else 1, on
MyDesiredRotation (the aim the rifle fires along, merc-rifle.md rule 6):

    MyDesiredRotation.Yaw   += int(fTurnValue   * r * fSensibiliteSouris * dt * 50)
    MyDesiredRotation.Pitch += int(fLookUpValue * r * fSensibiliteSouris * dt * 50)

then Normalize it, clamp its Pitch to [-12000, 13000], `SetRotation(MyDesiredRotation)` (the view is the aim), and
`UpdateDesiredRotation(MyDesiredRotation)`.
Each `int()` truncates toward zero, separately for yaw and pitch.

`UpdateCamRotation(dt)` (spy), with r as above and V = Normalize(Rotation):
- Armed spy (`SPawnAttaque.WeaponLoaded == 2`): a pitch repulsion coefficient
  `c = 1 - (|V.Pitch| - 6000) / 9000` when `|V.Pitch| > 6000`, else 1; c is forced to 1 when the look input
  moves back toward level (Pitch > 0 with fLookUpValue < 0, or Pitch < 0 with fLookUpValue > 0).
  `V.Pitch += int(r * c * fLookUpValue * dt * fSensibiliteSouris * 50)`, Normalize, clamp Pitch to
  [-12000, 15000], add the target snap `V.Yaw += int(fSnapAngleX * 50 * dt)`,
  `V.Pitch += int(fSnapAngleY * 50 * dt)` when bSnappedToTarget, then
  `V.Yaw += int(r * fTurnValue * dt * fSensibiliteSouris * 50)`. There is no final pitch clamp after the snap.
- Unarmed spy: `V.Pitch += int(r * sens * 50 * dt * fLookUpValue)`; `V.Yaw += int(r * sens * 50 * dt * s *
  fTurnValue)` with s = -1 when bInvertHorizontalAxisCamSpy else 1; Normalize; clamp Pitch to [-12000, 13000].
- `SetRotation(V)`. The spy path never calls UpdateDesiredRotation.

`UpdateRotationDefenseur(dt, maxPitch)`: when falling, zero fTurnValue;
`SetRotation(NativeRotationCameraDefenseur(dt))`; send `UpdateDesiredRotation` when MyDesiredRotation changed or
at least 0.2 s passed since LastDesiredRotationUpdate. Nothing reaches it in the PC build: the global
UpdateRotation always takes `UpdateRotationDefenseur_Mouse` for a merc, the one script call
(`s_Binoculars_Def.UpdateRotationDefenseur_Mouse`) resolves to the scope state's own override, and neither the game
nor Reloaded.Core calls the event natively (no code in either uses its name index 0x13c0dc, nor that of
NativeRotationCameraDefenseur, 0x13c824). So NativeRotationCameraDefenseur (the merc pad view: pad acceleration,
the snap, ForceRotateTo's turn and the double-tap quick turn) never runs on PC, and the rewrite does not port it.

`UpdateDesiredRotation(Rot)` is an unreliable client-to-server function that sets MyDesiredRotation on the
server.

### Limited view rotation (UpdateLimitedRotation)

Used on ledges, pipes, ladders, zip lines, fences, back to wall and split jumps to keep the camera inside a yaw
and pitch window around a reference. Inputs: minYaw, maxYaw, minPitch, maxPitch (min values are negative), YawRef,
PitchRef.

1. f = dt * 50 * fSensibiliteSouris. V = Rotation.
2. `V.Yaw -= int(fTurnValue * f)` when bInvertHorizontalAxisCamSpy and WeaponLoaded == 0, else
   `V.Yaw += int(fTurnValue * f)`. `V.Pitch += int(fLookUpValue * f * 0.667)`.
3. Yaw: if the current Rotation.Yaw already lies inside the window
   (`ClampRotation(YawRef, Rotation.Yaw, minYaw, maxYaw) & 65535 == Rotation.Yaw & 65535`), then
   `V.Yaw = ClampRotation(YawRef, V.Yaw, minYaw, maxYaw)`. Otherwise compute
   `t = (V.Yaw - YawRef) & 65535`; clamp t to [0, maxYaw & 65535] when t < 32768, else to
   [minYaw & 65535, 65535]; if the clamp changed t, move V.Yaw toward `t + YawRef` by at most
   `int(Level.m_dT * 50000)` per frame in either direction (ClampRotation with that step as both limits).
4. Pitch: the same with PitchRef, minPitch, maxPitch.
5. SetRotation(V).

`ClampRotation(Value, Target, Min, Max)` returns Value moved toward Target along the shorter way round, by at most
`Max` in the positive direction and `-Min` in the negative direction, masked to 0..65535. With both values masked
to 0..65535 first:
- Target > Value and Target - Value > 32768: `(Value - |clamp(Value + 65536 - Target, 0, -Min)|) & 65535`
- Target > Value otherwise: `(Value + |clamp(Target - Value, 0, Max)|) & 65535`
- Value > Target and Value - Target > 32768: `(Value + |clamp(Target + 65536 - Value, 0, Max)|) & 65535`
- Value > Target otherwise: `(Value - |clamp(Value - Target, 0, -Min)|) & 65535`
- Value == Target: the function has no return statement and yields 0 (see Edge cases).

### Pawn rotation for weapon movement (UpdatePawnRotationWeapon)

Given integer Forward and Strafe: X, Y = axes of Pawn.Rotation flattened (Z = 0);
`a = Forward * X / |X| + Strafe * Y / |Y|`, a.Z = 0. The pawn's yaw is set to the controller's yaw (pitch and
roll of the pawn kept). Returns a. The pawn always faces the view; strafing is sideways movement.

### Pawn rotation for spy movement (UpdatePawnRotationMario)

The pawn turns toward the stick direction with a rate that depends on its horizontal speed v = |Velocity.xy|.

1. Turn rate: `w = (v * (0.0125 * v - 2) + 2) * 80000` when v < 80, else `w = 80000` (rotator units per second).
   `MaxDeltaTime = 32000 / w`.
2. Desired direction: ForcedAcceleration when given (non-zero), else
   `a = Forward * normal(X) + Strafe * normal(Y)` with X, Y the flattened axes of the controller rotation. a.Z = 0.
3. dt is clamped to at most MaxDeltaTime. `step = dt * w`.
4. If a is non-zero: `d = int(float(yaw(a) - Pawn.Yaw)) & 65535`.
   - If `d < 32768` and `d > step`: d = step, and if `|Pawn.Velocity| < 10` the returned acceleration is zeroed.
   - If `d >= 32768` and `d < 65536 - step`: d = 65536 - step, with the same zeroing rule.
   - Pawn.Yaw += int(d); SetRotation.
5. With X the pawn's new forward axis, `p = a . X`. Return `p * normal(a)` when p > 0, else zero. So the pawn
   only accelerates along directions in front of it and slows to turn on the spot when nearly still.

Consequence of the formula in step 1 (decoded, reproduce exactly): w is positive (160000 at v = 0) only for
v < 1.0076; between v = 1.0076 and v = 80 it is negative, which makes MaxDeltaTime negative, so dt is always
clamped and `step = (32000 / w) * w = 32000`: the turn is effectively instant (up to 32000 units, about 176
degrees) in one frame. From v = 80 up, the rate is 80000 units/s (about 440 degrees/s).

### Pawn rotation for spy jumps (UpdatePawnRotationMarioJump)

Air control in the spy jump states. The pawn rotation is not changed here.

1. Desired direction a: normal(ForcedAcceleration) when given, else `Forward * X/|X| + Strafe * Y/|Y|` from the
   flattened controller axes. a.Z = 0. n = |a|.
2. If n > 0, with h = |Velocity.xy| and X the pawn forward axis:
   - h > 10: `t = (Velocity . X) / h`; if `(a . X) * t < 0` then t = -t; `a = X * n * t`; then
     `a *= max(0, 1 - |h| / RunningSpeed)`.
   - otherwise `a = X * (a . X)`.
3. Return a. Air acceleration is only ever along the pawn's facing and fades to zero as horizontal speed reaches
   RunningSpeed (385).

### Pipe stick projection (fGetForwardForPipe)

`fGetForwardForPipe(Forward, Strafe) = (Forward * X/|X| + Strafe * Y/|Y|) . vector(Pawn.Rotation)` with X, Y the
flattened controller axes: the stick's component along the way the pawn faces.

### Pad helpers (ControleAccelerationPad, ControleSmoothXYPad, ControleSmoothNormePad)

No script code calls these three functions; they are kept for completeness and may be dead.
- ControleAccelerationPad: when fNormePadCam > 0.75, start a timer (fTempsPadMax) and ramp
  `fPadAcceleration = min(1 + (TimeSeconds - fTempsPadMax) * k, 1 + k)` with `k = (fNormePadCam - 0.75) * 4`;
  when fNormePadCam < 0.65, reset the timer and set fPadAcceleration = fNormePadCam / 0.75.
- ControleSmoothXYPad: with `f = dt * 50`, each of fTurnValue and fLookUpValue becomes
  `(prev * 10 + value * f) / (f + 10)`; prev resets to 0 when fNormePadCam == 0 or when value * prev < -250000.
- ControleSmoothNormePad: rescale (fTurnValue, fLookUpValue) to length `fNormePadCamSmoothee * 1000`.

## States

No states of its own. PlayerTick dispatches to the current state's PlayerMove.

## Constants

| Name | Value | Unit | Provenance | Source |
|---|---|---|---|---|
| Rotation rate scale | 50 | per second | decoded | UpdateRotationAttaquant_Mouse, UpdateCamRotation |
| Binocular rotation ratio | 0.5 | factor | decoded | same |
| Merc and unarmed spy pitch clamp | -12000 .. 13000 | rotator units | decoded | same |
| Armed spy pitch clamp | -12000 .. 15000 | rotator units | decoded | UpdateCamRotation |
| Armed spy pitch repulsion | starts 6000, span 9000 | rotator units | decoded | UpdateCamRotation |
| Limited rotation pitch factor | 0.667 | factor | decoded | UpdateLimitedRotation |
| Limited rotation recentre speed | 50000 x m_dT | rotator units per frame | decoded | UpdateLimitedRotation |
| Mario turn rate | 80000 (SpeedRot) | rotator units/s | decoded | UpdatePawnRotationMario, const SpeedRot |
| Mario slow-turn threshold | 80 | uu/s | decoded | UpdatePawnRotationMario |
| Mario stop-turn threshold | 10 | uu/s | decoded | UpdatePawnRotationMario |
| Mario max step | 32000 | rotator units | decoded | UpdatePawnRotationMario |
| Air control fade speed | RunningSpeed = 385 | uu/s | decoded | def:SBase.SBasePawn |
| Desired rotation resend | 0.2 | s | decoded | UpdateRotationDefenseur |
| Camera position report | 0.14 | s | decoded | PlayerTick |
| Roll cooldown | fTimeBetweenTwoRolls = 0.5 | s | decoded | def:SBase.SPlayerController |
| Roll minimum stick | 0.3 | fNormePadMove | decoded | ComputeCrouchOrRoll |
| Roll minimum speed | 65 | uu/s | decoded | ComputeCrouchOrRoll |
| Adrenaline | factor 1.2 for 5.0 s after a hit | factor, s | decoded | def:SBase.SPlayerController |
| Jammer minimum input scale | 0.5 | factor | decoded | PlayerTick |
| Fire mode hold time | 0.5 | s | decoded | PlayerTick |
| Speed modes | 0.8 binoculars, 1.0 merc or fast, 0.6 weapon slow, 0.4 slow | factor | decoded | GetSpeedMode |
| Mouse sensitivity | profile value / 10, profile 4..40, default 10 | factor | decoded | SPlayerProfile.SetMouseSensitivity |
| Mouse axis speed | 6.00 | binding speed | decoded | Default_kbd_0.ini IK_MouseX / IK_MouseY |
| Stick full scale | 6000 per key, times GetSpeedMode | input units | measured and decoded | input.md 1.3; walking clamps; matches measured speeds |
| Turn and look scale | 0.75, clamp +-2000; mouse scaled by viewport width / 640 and height / 480 | factor, input units | decoded | input.md 1.1, 1.6 |
| iMove thresholds | 2100 side (and ledge or fence), 4500 forward and back (3000 hysteresis), 3000 stick up and down, 0.45 screen-space | input units | decoded | input.md 3 |

## Network

- All of this runs on the owning client (Role AutonomousProxy) and in standalone or on a listen host for the
  local player. A dedicated server never samples input; it receives moves (`MyServerMove` family) and state
  changes (`PCChangeServerState`).
- `UpdateDesiredRotation` is unreliable, client to server (replication block `Role < ROLE_Authority`). Only the
  merc path and UpdateRotationDefenseur call it.
- `MyDesiredRotation` is replicated reliably server to client only with bNetInitial.
- `UpdatePositionForServer(Location, Rotation)` is unreliable client to server, sent every 0.14 s.

## Edge cases

- `ClampRotation` with Value == Target (after masking) returns 0. In UpdateLimitedRotation this can snap the view
  yaw or pitch to 0 for one frame when the new value lands exactly on the reference. The port must keep this.
- UpdatePawnRotationMario divides by zero when v is exactly a root of `0.0125 v^2 - 2v + 2` (v = 1.00756...): UE2
  float division by zero gives an infinite MaxDeltaTime, so dt is not clamped and step = dt * w with w = 0, no
  turn that frame. Use IEEE semantics, do not guard.
- All `int()` conversions truncate toward zero; yaw changes smaller than one unit are lost each frame, so very
  slow mouse motion at low frame times does not turn the view. Keep the per-frame truncation.
- The console being open clears the BT_* movement buttons but not aForward/aStrafe produced by the native; the
  native reads the buttons, so movement stops on the next frame.
- The crouch toggle in step 1 of ComputeCrouchOrRoll is skipped entirely while the sticky-shock input lock is
  held, so presses during the lock are lost, not queued.

## Evidence

- Decompiled: local/re-out/uc/SBase/SPlayerController.uc (PlayerTick, ComputeCrouchOrRoll, ChangeSpeed,
  GetSpeedMode, UpdateRotation*, UpdateCamRotation, UpdateLimitedRotation, ClampRotation, UpdatePawnRotation*,
  fGetForwardForPipe, Controle*), local/re-out/uc/Engine/PlayerInput.uc, Engine/PlayerController.uc (Jump),
  SBase/SPlayerProfile.uc (SetMouseSensitivity, InvertX/InvertY).
- Raw tokens checked for the Mario turn-rate expression: `python -B -m ucdecomp dis ... SPlayerController.UpdatePawnRotationMario`
  (offset 0x0040: multiply, subtract, multiply, add, multiply as written above).
- Bindings: game System/Default_kbd_0.ini (W/A/S/D = BT_*, Space = Jump, C = Crouch, Q = Roll, Shift = Use,
  Z = BTW, E = FireMode, middle mouse = ChangeSpeed, mouse axes Speed 6.00).
- Natives (game exe): NativeComputeControlVariables 0x10aaf400 (editor exec 0x111f37d0), NativePlayerInput
  0x10aaa8d0 (editor exec 0x111ede90), NativeRotationCameraDefenseur 0x10aaab30 (editor exec 0x111edf60),
  NativeComputeSquinting 0x10aafc80. Engine.Actor.SetRotation resolves to 0x58dc0420, inside Reloaded.Core.dll
  (the community patch hooks it).
- Measured speeds: RE+ docs/MapBuilderPatterns.md "Limits measured in game" (run 423, slow 169, crouched 221,
  slow 89; merc 346, crouched 117).

## Open questions

5. Two details left open by the native study (input.md open questions 1 and 2): that the two Player fields scaling
   the mouse are the viewport size, and the exact screen-space construction of iMove bits 16 and 32 in the spy's
   climbing modes. How mouse smoothing (MouseSmoothingMode 1, strength 0.3, sampling 0.008333) and the binding
   Speed 6.00 shape aMouseX before the native is engine input code, not decoded here. Trace: move the mouse a known
   number of counts at two resolutions and read fTurnValue.

### Resolved

1. NativeComputeControlVariables mapping, where GetSpeedMode applies, exactly 6000 per key. Resolved: the mapping in
   Frame order step 3 (6000 x GetSpeedMode per key, analog axes overwritten, 0.75 turn and look scale clamped to
   +-2000, fNormePadMove and fNormePadCam as given); the measured speeds 423.5, 169.4, 221.375, 346.5 and 116.91 all
   follow. Evidence: input.md 1, 2; game 0x10aaf400, editor 0x111e6460 (ANSWERS-actions 4).
2. Is a slow diagonal faster than slow straight movement. Resolved: the native gives 2400 on both axes for a slow
   diagonal and does not normalise; any normalisation is the script's (walking clamps). Evidence: input.md rule 1.3
   (ANSWERS-actions 5).
3. Which iMove bits from which inputs, and the thresholds. Resolved: bits 1, 2, 4, 8 pawn-relative against 2100 and
   4500 (hysteresis 3000), 16 and 32 against 3000 when walking or falling (screen-space elsewhere); ledge and fence
   use the raw stick against 2100. Evidence: input.md 3 (ANSWERS-actions 4).
4. Does NativePlayerInput run for live players. Resolved: its body (the button and mouse mapping) does:
   NativeComputeControlVariables runs the same function every live PlayerTick unless iDontUpdatePadInput is set.
   Evidence: input.md rule 2.2 (ANSWERS-actions 6).
