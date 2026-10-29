# Camera natives: calc view dispatch, the following camera solver and the 35 camera modes

Units:
- nat:SBase.SPlayerController.NativeGlobalPlayerCalcView
- nat:Engine.PlayerController.NativeGlobalPlayerCalcView
- eng:ASPlayerController::NativePlayerCalcView (and the _Default, _MultiSpectating, _StickyCam, _Mirador,
  _StickyCamSmoothToPosition variants)
- nat:SBase.SPlayerCam.InitCameraSettings
- nat:SBase.SPlayerCam.SetMode
- nat:SBase.SPlayerCam.UpdateView
- nat:SBase.SPlayerCam.FollowingRotation
- nat:SBase.SPlayerCam.ResettingRotation
- def:SBase.SPlayerCam (m_camParam)

## Purpose

Where the view comes from each frame: the controller's calc view picks a view source by state, the SPlayerCam actor
solves the third-person (following) camera from the mode table m_camParam[35], or the first-person view from the
pawn's eyes. The camera is local to each client (it never affects the simulation), so this spec is for the Godot
client's camera.

## Data

ECamParam (one per ECamMode, 0x98 bytes, m_camParam at SPlayerCam +0x2f8): offset[4] (vectors), Distance[4],
minYaw, maxYaw, minPitch, maxPitch, Damping, interSpeed, collInterSpeed, collCBInterSpeed, targetXYSpeed,
targetZSpeed, twigX, twigZ, biasCut, biasSlope, offsetSpeed, useAngles, alignPawn, usePitchCurve, useCollTarget,
useCylColl, useColl, useTwig. The four entries of offset and Distance are indexed by m_volSize (EVolumeSize), but
the solver resets m_volSize to 0 (EVS_Normal) at the start of every solve, so only entry 0 is used in this build.

Read by the solver: offset[0], Distance[0], interSpeed, collInterSpeed, collCBInterSpeed, targetZSpeed, twigX,
twigZ, offsetSpeed, usePitchCurve, useCollTarget, useCylColl, useColl, useTwig. Read by script (GetMinYaw and
kin): minYaw, maxYaw, minPitch, maxPitch. Not read by any decoded native: Damping, targetXYSpeed, biasCut, biasSlope,
useAngles, alignPawn.

Mode table (the native InitCamParam, editor 0x111f71d0, fills each mode, copying its parent first and then
overriding; the values below are the resolved table, index 0 entries only; flags: C useColl, Y useCylColl,
T useCollTarget, P usePitchCurve, W useTwig, A alignPawn, G useAngles):

| # | Mode | Parent | Dist0 | offset0 (X, Y, Z) | inter / coll / collCB speed | offsetSpeed | targetZSpeed | Flags | twig X, Z | Yaw limits | Pitch limits |
|---|---|---|---|---|---|---|---|---|---|---|---|
| 0 | Walking | - | 250 | 0, 0, 0 | 400 / 400 / 400 | 75 | 300 | CYPWG | 20, 10 | -65536 to 65536 | -13000 to 12000 |
| 1 | WalkingCr | Walking | 250 | 0, 0, 0 | 400 / 400 / 400 | 75 | 300 | CYPWG | 20, 10 | -65536 to 65536 | -13000 to 12000 |
| 2 | Running | Walking | 250 | 0, 0, 0 | 500 / 500 / 0 | 75 | 300 | CYPWG | 20, 10 | -65536 to 65536 | -13000 to 12000 |
| 3 | RunningCr | WalkingCr | 250 | 0, 0, 0 | 500 / 500 / 0 | 75 | 300 | CYPWG | 20, 10 | -65536 to 65536 | -13000 to 12000 |
| 4 | SecondPerson | Walking | 30 | 0, 27, 0 | 400 / 400 / 0 | 400 | 100 | CTWAG | 30, 30 | -65536 to 65536 | -15000 to 15000 |
| 5 | SecondPersonCr | SecondPerson | 10 | 0, 27, 20 | 400 / 400 / 0 | 400 | 100 | CTWAG | 30, 30 | -65536 to 65536 | -15000 to 15000 |
| 6 | Grab | Walking | 250 | 0, 0, 0 | 400 / 400 / 400 | 75 | 300 | CYPWG | 20, 10 | -65536 to 65536 | -13000 to 12000 |
| 7 | GrabFP | SecondPerson | 90 | 0, 15, -5 | 400 / 400 / 0 | 400 | 100 | CTWAG | 30, 30 | -65536 to 65536 | -5000 to 7500 |
| 8 | Carry | Walking | 250 | 0, 0, 0 | 400 / 400 / 400 | 75 | 300 | CYPWG | 20, 10 | -65536 to 65536 | -13000 to 12000 |
| 9 | CarryCr | WalkingCr | 250 | 0, 0, 0 | 400 / 400 / 400 | 75 | 300 | CYPWG | 20, 10 | -65536 to 65536 | -13000 to 12000 |
| 10 | Throw | - | 70 | 0, 30, -10 | 400 / 400 / 400 | 400 | 300 | CYTWAG | 20, 20 | -65536 to 65536 | -14000 to 14000 |
| 11 | ThrowCr | Throw | 70 | 0, 30, -10 | 400 / 400 / 400 | 400 | 300 | CYTWAG | 20, 20 | -65536 to 65536 | -14000 to 14000 |
| 12 | Sniping | SecondPerson | 0 | 0, 25, 0 | 800 / 400 / 0 | 400 | 200 | CTAG | 30, 30 | -65536 to 65536 | -15000 to 15000 |
| 13 | HSphere | FSphere | 250 | 0, 0, 0 | 400 / 400 / 400 | 400 | 300 | CYTPWG | 10, 30 | -20000 to 20000 | -13000 to 12000 |
| 14 | FSphere | - | 250 | 0, 0, 0 | 400 / 400 / 400 | 400 | 300 | CYTG | 0, 0 | -65536 to 65536 | -13000 to 10000 |
| 15 | HOH | FSphere | 250 | 0, 0, 0 | 400 / 400 / 400 | 20 | 300 | CYG | 40, 30 | -65536 to 65536 | -13000 to 10000 |
| 16 | HOHFU | FSphere | 250 | -40, 0, 0 | 400 / 400 / 400 | 20 | 300 | CYPG | 0, 0 | -65536 to 65536 | -13000 to 12000 |
| 17 | HOHFP | - | 27 | 5, 0, 40 | 200 / 0 / 0 | 40 | 30 | WG | 30, 30 | -10000 to 10000 | -12000 to 5000 |
| 18 | BTW | - | 150 | 15, 0, 0 | 200 / 400 / 400 | 125 | 250 | CYPG | 0, 0 | -20000 to 20000 | -13000 to 13000 |
| 19 | BTWPeakLeft | BTW | 100 | 15, -60, -10 | 200 / 400 / 400 | 125 | 250 | CYPG | 0, 0 | -20000 to 20000 | -13000 to 13000 |
| 20 | BTWPeakRight | BTWPeakLeft | 100 | 15, 60, -10 | 200 / 400 / 400 | 125 | 250 | CYPG | 0, 0 | -20000 to 20000 | -13000 to 13000 |
| 21 | BTWLeftFP | BTWPeakLeft | 50 | 15, -60, -10 | 200 / 400 / 400 | 125 | 250 | CYPG | 0, 0 | -12000 to 2000 | -12000 to 12000 |
| 22 | BTWRightFP | BTWPeakRight | 50 | 15, 60, -10 | 200 / 400 / 400 | 125 | 250 | CYPG | 0, 0 | -2000 to 12000 | -12000 to 12000 |
| 23 | Rapel | HSphere | 250 | -30, 0, -20 | 400 / 400 / 400 | 400 | 300 | CYTPWG | 10, 30 | -20000 to 20000 | -13000 to 12000 |
| 24 | RapelFP | - | 45 | -60, 25, -35 | 800 / 0 / 0 | 400 | 300 | WG | 30, 30 | -12000 to 12000 | -8000 to 13000 |
| 25 | DoorPeekRight | - | 50 | 45, 25, 5 | 400 / 400 / 400 | 400 | 300 | WG | 20, 40 | 0 to 16000 | -12000 to 12000 |
| 26 | DoorPeekLeft | - | 50 | 45, -25, 5 | 400 / 400 / 400 | 400 | 300 | WG | 20, 40 | -16000 to 0 | -12000 to 12000 |
| 27 | SplitJump | FSphere | 250 | 0, 0, -40 | 50 / 400 / 400 | 40 | 300 | CYG | 0, 0 | -65536 to 65536 | -13000 to 10000 |
| 28 | SplitJumpFP | SplitJump | 27 | 5, 12, 0 | 50 / 400 / 0 | 40 | 100 | CAG | 30, 30 | -65536 to 65536 | -15000 to 15000 |
| 29 | FirstPerson | Walking | 0 | 0, 0, 0 | 800 / 400 / 0 | 75 | 200 | CWAG | 30, 30 | -65536 to 65536 | -15000 to 15000 |
| 30 | OnLedge | HSphere | 250 | -40, 0, 10 | 400 / 400 / 400 | 20 | 300 | CYPWG | 10, 30 | -65535 to 65535 | -15000 to 15000 |
| 31 | HOHFPHanged | BTWLeftFP | 125 | -30, -15, -120 | 200 / 400 / 400 | 40 | 250 | CYPG | 0, 0 | -12000 to 2000 | -12000 to 12000 |
| 32 | OnLadder | Walking | 250 | -20, 0, 0 | 400 / 400 / 400 | 75 | 300 | CYPWG | 20, 10 | -32767 to 32768 | -15000 to 14000 |
| 33 | NoTargetCol | Walking | 250 | 0, 0, 0 | 200 / 400 / 400 | 75 | 300 | CYPWG | 20, 10 | -32767 to 32768 | -15000 to 14000 |
| 34 | SmoothTransitionCentered | Walking | 250 | 0, 0, 0 | 50 / 400 / 400 | 75 | 50 | CYPWG | 20, 10 | -65536 to 65536 | -13000 to 12000 |

Other SPlayerCam fields used: m_collisionRadius and m_collisionHeight (the camera box), m_absoluteMinDist,
m_closeupDist, m_closeupHeight, m_closeupDamping, m_minCleanDist, m_targetZMaxDist, m_pitchCurveBias, m_maxPitch,
m_SmoothDisplacement, m_SmoothIsAlwaysActive, m_Smoothvalue, m_fixedTargetPos, m_fixedTargetSpeed, the state
m_prevStartPos, m_prevOffset, m_prevTargetZ, m_prevIdealDistance, m_prevDistance, m_prevCloseupAdjust, and the
shake, tilt and hit roll fields.

## Rules

### 1. Calc view dispatch

1. NativeGlobalPlayerCalcView(out ViewActor, out CameraLocation, out CameraRotation) (SBase 0x111e74c0, Engine
   0x11113e90) calls the controller's NativePlayerCalcView with bUpdate = 1.
2. NativePlayerCalcView (editor 0x111f3e50) picks the source by the controller's current state name:
   - s_UseObject_Mirador: the Mirador view (rule 4.3);
   - s_UseObject_StickyCam_SmoothToPosition: the smooth-to-sticky-camera view (rule 4.4);
   - s_UseObject_StickyCam and s_UseObject_CamNetwork: the sticky camera view (rule 4.2);
   - s_MultiSpectating, s_Spectating_Visitor, s_MultiSpectating_WhenKilled: the spectating view (rule 4.1);
   - s_PlayerFalling_OutOfWorld for a spy: the view stays at vCamLocWhenFallingDead and looks at the pawn;
   - anything else: the default view (rule 2).
3. The result is also stored in vCamLocation and rCamRotation. With bUpdate, once per Level.TimeSeconds, every live
   SBasePawn whose native weapon slot holds an SWeaponAttaque gets a view-dependent weapon update (editor
   0x111d8f90; not decoded here).
4. Calls per frame (measured 2026-10-29, Frida on the running game, Play Level SteeE as the spy): the engine runs
   NativePlayerCalcView (game 0x10aafe70) with bUpdate three times per rendered frame, from 0x109ebbb9 (the scene
   draw 0x109ebb20), 0x109e3f35 and 0x10af8e07; the following solver (game 0x10a94e00) runs on each, so it ran 3600
   times in 1200 frames. Each solve steps by the frame's full Level.m_dT, so every ease of rule 3 (offset, target
   height, ideal and final distance) and of the other view sources moves three steps per frame: the rates of the
   mode table are a third of what the player sees. The rewrite solves the view three times per tick
   (PlayerCalcView.FrameView).

### 2. Default view (editor 0x111e3830, game 0x10ab00e0)

1. When the controller has no Pawn, or ViewTarget is None or being destroyed, the view target is reset to the pawn
   (or the controller when there is no pawn).
2. ViewActor = ViewTarget; the start location is the ViewTarget's Location, the rotation the one passed in.
3. With a camera: bBehindView without bFirstPersonWithPawn gives the behind view, only in the camera's s_Following
   state (rule 2.5; another state leaves the start location and rotation). Else the own view (game 0x10a98840): a
   merc with DebugCamMode 1 gets rule 2.5 without solving (the controller's spot); otherwise, outside
   s_IntroCinematic, the first-person view of rule 5.
4. The camera's m_PrevPosition, vCamLocation and rCamRotation take the result.
5. The s_Following view (game 0x10a96590): the pawn is m_EPC.SBPawn, else SpecTarget. A spy with DebugCamMode 1
   keeps the view given. With bUpdate, a mesh and no pause, m_sam = the pawn, the solver of rule 3 runs and
   m_rotatedSam is cleared. With a pawn, the view is the controller's Location and Rotation (where the solver put
   it); outside DebugCamMode 1, vFixedPos takes the controller's Location.

### 3. Following camera (SPlayerCam s_Following, editor 0x111f6af0 and the solver 0x111f56a0)

The pawn is m_EPC.SBPawn, or SpecTarget when spectating. With bUpdate, a mesh and no pause, m_sam = that pawn and
the solver runs; the output is the controller's own Location and Rotation (the solver moves the controller actor
to the camera spot). P = m_camParam[m_camMode]; dt = Level.m_dT; "approach x toward y at s" = move by at most s dt
(per component for scalars, along the line for vectors).

1. m_volSize = 0. Camera box = (m_collisionRadius, m_collisionRadius, m_collisionHeight) when useCylColl, else
   (2, 2, 2). Pitch p = the controller's pitch in [-32768, 32767].
2. Start point S: the pawn's Location with Z = Location.Z + CollisionHeight - m_collisionHeight. In mode 34
   (SmoothTransitionCentered) S is a pawn bone position plus 30 in Z and smoothing is forced on (m_Smoothvalue 20).
   With m_SmoothDisplacement and m_Smoothvalue > 0, S approaches that point from m_prevStartPos at
   |pawn Velocity| + m_Smoothvalue; unless m_SmoothIsAlwaysActive, smoothing switches off once the remaining gap is
   within one step. With the fixed target bit of m_rotatedSam, S approaches m_fixedTargetPos at m_fixedTargetSpeed
   instead. m_prevStartPos = S.
3. Offset: offset[0] rotated by the pawn's pitch and yaw; m_prevOffset approaches it at offsetSpeed.
4. Twig (useTwig): (-p / 16384 x twigX, 0, p / 16384 x twigZ) rotated by the controller's yaw.
5. Close-up (m_closeupDist > 0): m_minCleanDist = m_absoluteMinDist + m_closeupDist; when useCylColl and the last
   distance is below it, adjust = (m_minCleanDist - m_prevDistance) / m_closeupDist x m_closeupHeight; the lift
   added to Z is damping x adjust + (1 - damping) x m_prevCloseupAdjust, and m_prevCloseupAdjust keeps the raw
   adjust. Otherwise there is no lift and m_prevCloseupAdjust = 0.
6. Target T = S + offset + twig (+ close-up Z). With useCollTarget (and no fixed target) T is swept from S with the
   camera box and stops at the first hit. The solver passes 0xdf and the sweep helper (game 0x10a961c0) clears bit 0,
   so the sweep runs with 0xde: other pawns do not stop the target.
7. Target Z: m_prevTargetZ approaches T.z at |pawn Velocity.Z (+ a mover base's Velocity.Z)| + targetZSpeed (modes 4,
   5 and 28 use + 190 while the pawn moves vertically), and is kept within m_targetZMaxDist of T.z.
8. Ideal distance: when Distance[0] is within interSpeed dt of m_prevIdealDistance it is taken as is. Otherwise a
   shrinking ideal moves by interSpeed dt, and a growing one by the pawn's movement along the controller's forward
   axis since SBasePawn.m_prevPos (at least 0, at most interSpeed dt): the camera only backs out as the pawn walks
   away from it.
9. Pitch curve (usePitchCurve, p > 0): D = ideal x Bias(1 - clamp(p / m_maxPitch, 0, 1), m_pitchCurveBias), with
   Bias(x, b) = x ^ (ln b / ln 0.5).
10. D changes by at most interSpeed per second from m_prevDistance.
11. Camera C = T - D x the controller's forward axis.
12. Collision (useColl): box sweep from T to C, both less the close-up lift (flags 0xde, plus the camera-blocking
    bit 0x10000 when collCBInterSpeed > 0), the pawn as the source. With a fixed target a hit whose Time is 0 (the
    sweep starting inside a blocker) is ignored. On a hit, D = the distance from the sweep start to
    the hit, reached from the last frame's distance m_prevDistance (not from step 10's D) at collCBInterSpeed per
    second when the hit actor has bBlockCamera and neither bBlockActors nor bBlockPlayers (the level's geometry,
    whose actor is the LevelInfo), else at collInterSpeed. With or without useColl, D is then at least
    m_absoluteMinDist and C = T - D x the controller's forward axis.
13. Final clearance, with the flags of step 12: modes 4 and 5 sweep a (2, 2, 2) box then a (12, 12, 12) box from T to
    C and pull C in to each hit; other modes sweep (12, 12, 12), on a hit set D = max(hit distance,
    m_absoluteMinDist), rebuild C along the controller's rotation and sweep once more, a second hit again setting
    D = max(hit distance, m_absoluteMinDist) and rebuilding C.
14. m_prevDistance = D; the view rotation gets the hit roll, shake roll and tilt pitch (UpdateView, rule 6); the
    controller is moved to C (FarMoveActor, no collision test).
15. Mode 33 (NoTargetCol): when the pitch is below 2000 and a sweep 20 up from the camera, with the box and flags
    of step 12, is blocked, fPitchCorrectionSpeed grows by 3200 per second up to 6400, else shrinks by 3200 per
    second to 0; while above 0 the controller's pitch becomes int(pitch + fPitchCorrectionSpeed dt) (truncated),
    through a rotation-only MoveActor.

### 4. Object and spectating views

1. Spectating (0x111e3990, game 0x10ab08d0): with a ViewTarget, ViewActor = ViewTarget, the start location is its
   Location, bBehindView is set and, in the camera's s_Following state, the view of rule 2.5 runs; m_PrevPosition
   takes the result. Without a ViewTarget nothing changes.
2. Sticky camera and camera network (0x111e7600, game 0x10ab04f0): with the pawn's UsedObject, the view sits 3 uu
   along the forward axis of the object's mount rotation (int(PitchConstraint.X), int(YawConstraint.X),
   int(RollConstraint.X), set from its placed rotation) from its location, 5 more uu along its current forward axis
   while the controller is in s_UseObject_CamNetwork, with the object's rotation; without one, the default view.
3. Mirador (0x111e7850, game 0x10ab06f0): the used object's location plus SProjoFixe.CameraOffset (+0x3a8) along the
   object's axes, with the object's rotation; without a used object, the default view.
4. Smooth to sticky camera (0x111f1400, game 0x10ab01e0): the view looks from the used object to the pawn; the
   target is 30 uu along the controller's forward axis from the object; the camera moves from m_PrevPosition toward
   it at 2000 uu/s and, within 80 uu of the object, the controller changes state: `ChangeState('s_UseObject_StickyCam',
   None, true)` (the hardcoded name 0x192 of the name table); without a used object, the default view.

### 5. First-person view (editor 0x111f5230, game 0x10a98390)

1. Without m_bAttachToBone: rotation = the controller's (roll 0); location = start + the controller's pawn's
   WalkBob + (0, 0, EyeHeight); then UpdateView on that rotation. UpdateView turns the controller only: this view
   keeps roll 0, so in first person the hit and shake rolls never show, and the tilt reaches the view only through
   the controller's pitch (the port keeps the tilt on the view pitch and the roll at 0, open question 2).
2. With m_bAttachToBone: the parent bone (m_parentBone) of the pawn's mesh gives the location; the view rotation is
   OrthoRotation (game 0x10950770) of the frame whose X, Y and Z axes are the bone's -Y, -Z and X. The spy's view is
   raised 15 uu in world Z; the merc's controller takes the view rotation. m_vRelativeLocation is added along the
   view rotation's axes (the head views use 'B Head' with (13, 0, 9): 13 ahead, 9 up). No UpdateView on this path.
3. ShakeOffset is added. With m_SmoothDisplacement and m_Smoothvalue > 0 (game 0x10a98720): unless
   m_SmoothIsAlwaysActive, smoothing switches off when m_PrevRotation.Yaw already equals the yaw; the yaw then turns
   from m_PrevRotation.Yaw the short way toward the target by min(gap, m_Smoothvalue dt) truncated, as a 16 bit
   angle.
4. m_PrevRotation takes the view rotation; outside DebugCamMode 1, vFixedPos takes the location.

### 6. Shake, tilt and hit roll (UpdateView, editor 0x111f4e50, game 0x10a96280)

Every step is MoveTowardScalar(value, target, speed, Level.m_dT) (the +0x54c read is the LevelInfo's m_dT) truncated
toward zero:
1. A nonzero m_hitRoll becomes the roll, then fades toward 0 at m_hitFadeOut.
2. When m_shakeRoll or m_shakeTargetRoll is nonzero, m_shakeRoll becomes the roll (replacing the hit roll); when the
   shake has reached its target, the target becomes -MoveTowardScalar(target, 0, m_shakeFadeOut, dt): it shrinks
   and flips sign, so the shake swings from side to side as it dies down; then m_shakeRoll moves toward the
   (new) target at m_shakeSpeed.
3. When m_tiltPitch or m_tiltTargetPitch is nonzero, m_tiltPitch is added to the pitch, then the target and the tilt
   step the same way (shrink and flip) with m_tiltFadeOut and m_tiltSpeed, so the tilt nods up and down.
4. The values applied are those from before the step. The resulting rotation is applied to the controller with a
   rotation-only MoveActor (the port returns it as the view rotation instead, see open question 2).

### 7. SetMode and the camera state natives

- SetMode(NewMode) (0x111f5170): nothing when NewMode is the current mode. Leaving mode 34 turns smoothing on
  (m_SmoothDisplacement). Walking to Running and WalkingCr to RunningCr just switch. Otherwise, when the old mode has
  useColl and the new one does not, m_prevIdealDistance = m_prevDistance (the camera keeps its pulled-in distance);
  else m_prevIdealDistance = the new mode's Distance[m_volSize]. Then m_camMode = NewMode.
- FollowingRotation (game 0x10a964f0): outside a pause, when the pawn (SBPawn, else SpecTarget) is the ViewActor
  argument and has a mesh, m_sam = the pawn and m_rotatedSam is set; else m_rotatedSam is cleared. It turns nothing.
- ResettingRotation (game 0x10a96750): false while paused; true when |Yaw - m_resettingRotation.Yaw| < 5 and
  |Pitch - m_resettingRotation.Pitch| < 5 on the raw controller values. It only tests; no decoded native turns the
  controller toward the target, and no script calls ResetRotation, so s_Reseting is never entered in play.
- InitCameraSettings fills all 35 modes once (InitCamParam).

### 8. Script side (SPlayerCam and SPlayerController)

The camera actor's script around the natives (decompiled SPlayerCam and SPlayerController, described here):

1. Each machine that runs SPlayerController.PostBeginPlay (simulated, so the server and every client) spawns one
   SPlayerCam owned by the controller (collision ignored) and sets its m_EPC to the controller. SpawnCamCtrl does
   the same later when a controller lost it. The camera is bHidden and does not replicate its movement.
2. SPlayerCam.PostBeginPlay: InitCameraSettings, then m_prevDistance = m_prevIdealDistance = 200.
3. ActivateSmooth(value, always): m_SmoothDisplacement = true; a value of 0 becomes 150 for a spy
   (SPAWNOFFENSE) and 15000 otherwise; m_Smoothvalue = value; m_SmoothIsAlwaysActive = always.
   DesactivateSmooth(now): with now, m_SmoothDisplacement = true (the script sets it, it does not clear it);
   without, m_SmoothIsAlwaysActive = false, so the smoothing ends by itself once caught up (rule 3.2).
4. SetFixeTarget(pos, speed): m_fixedTargetSpeed, m_fixedTargetPos, m_fixedTarget = true; StopFixeTarget clears
   m_fixedTarget.
5. AttachCamToBone(on, bone, offset): m_bAttachToBone, m_parentBone, m_vRelativeLocation; attaching also clears
   the effects (ResetEffects).
6. GetMinYaw, GetMaxYaw, GetMinPitch, GetMaxPitch read the current mode's limits; GetCurrentDistance reads
   Distance[m_volSize] of the current mode; GetCurrentParam returns the current mode's whole entry.
7. States: s_Following is the auto state. Its ResetRotation(rot, rate), when m_resetEnable and the dot product of
   the controller's view direction with vector(rot) is below 0.92, stores rot in m_resettingRotation, scales
   m_resetPitchSpeed and m_resetYawSpeed (from their defaults) by rate and enters s_Reseting, whose UpdateRotation
   returns to s_Following once ResettingRotation() is true. s_Fixed and s_IntroCinematic have empty
   UpdateRotation; the global ones are empty too.
8. SetDebugCamMode(mode) accepts 0 or 1 only, stores it in DebugCamMode and clears the controller's roll.

## States

The camera actor's script states (s_Following, s_Fixed, s_Reseting, s_IntroCinematic) choose behind-view solving
(s_Following only) and the script rotation update.

## Constants

| Name | Value | Unit | Provenance | Source |
|---|---|---|---|---|
| Mode table | above | uu, uu/s, rotation units | decoded | InitCamParam 0x111f71d0 (matches the Bridge camparams dump) |
| Twig scale | 1 / 16384 | per rotation unit | decoded | solver |
| Final clearance boxes | 2 and 12 | uu | decoded | solver |
| Vertical boost speed | 190 | uu/s | decoded | solver (modes 4, 5, 28) |
| Mode 34 smoothing | 20, start + 30 Z | uu/s, uu | decoded | solver |
| Pitch correction | 3200 per s, max 6400 | rotation units | decoded | solver (mode 33) |
| Sticky camera offsets | 3, +5 network | uu | decoded | 0x111e7600 |
| Smooth to sticky camera | 30 ahead, 2000 uu/s, arrive within 80 | uu | decoded | 0x111f1400 |
| Spy first-person bone offset | 15, world Z | uu | decoded | game 0x10a98390 |
| ResettingRotation tolerance | below 5 | rotation units | decoded | game 0x10a96750 |

## Network

Local. The calc view runs on the client that renders; the server never solves cameras (the server's view of a
remote player is its pawn's rotation, net-moves.md).

## Edge cases

- Because m_volSize is forced to 0, the crouched modes (WalkingCr, RunningCr, CarryCr) differ from their standing
  parents only in index-3 offsets that are never used: crouching does not move the camera by mode data.
- The solver moves the controller actor; script reading the controller's Location after the calc view sees the
  camera position.

## Evidence

- Editor: NativeGlobalPlayerCalcView 0x111e74c0 / 0x11113e90, NativePlayerCalcView 0x111f3e50, _Default
  0x111e3830, _MultiSpectating 0x111e3990, _StickyCam 0x111e7600, _Mirador 0x111e7850,
  _StickyCamSmoothToPosition 0x111f1400, SPlayerCam behind view 0x111f70d0, own view 0x111f70f0, s_Following view
  0x111f6af0, solver 0x111f56a0, UpdateView 0x111f4e50, FirstPersonCalcView 0x111f5230, InitCamParam 0x111f71d0,
  SetMode 0x111f5170, ResettingRotation 0x111f5130 (helper 0x111f4df0), bias 0x10fea400.
- Game exe (SCCT_Versus, base 0x10900000): NativePlayerCalcView_Default 0x10ab00e0, own view 0x10a98840, s_Following
  view 0x10a96590, solver 0x10a94e00, UpdateView 0x10a96220 / body 0x10a96280, FirstPersonCalcView 0x10a98390, yaw
  smoothing 0x10a98720, OrthoRotation 0x10950770, MultiSpectating 0x10ab08d0, StickyCam 0x10ab04f0, Mirador
  0x10ab06f0, StickyCamSmoothToPosition 0x10ab01e0, FollowingRotation 0x10a964f0, ResettingRotation 0x10a96750,
  SetMode 0x10a967c0, InitCameraSettings 0x10a96920 (per mode 0x10a96960), MoveTowardScalar 0x109532c0.
- Bridge: out/_spyview/camparams.txt (the 35 resolved modes).

## Open questions

1. The view-dependent weapon update of rule 1.3 (editor 0x111d8f90).
2. UpdateView applies the effects to the controller's own rotation (rotation-only MoveActor), so the tilt pitch
   feeds back into the aim each frame; the port keeps the controller's aim and puts the effects on the view only.
   Whether the original's controller rotation is rebuilt each frame from elsewhere (which would cancel the
   feedback) is not decoded. (The +0x54c factor of the earlier question is the LevelInfo's m_dT.) REA (2026-10-26)
   confirms both callers pass the controller's own rotation (solver 0x10a96003, first person 0x10a98390) and that
   the first-person view rotation is not the one UpdateView changes. If nothing rebuilds the controller's pitch,
   every shake device nods the aim of the players it reaches by the sum of the tilt values over its swing; this is
   a gameplay change (aim drift) and wants a check in the original before the port follows it.
3. Whether Damping, targetXYSpeed, biasCut, biasSlope, useAngles and alignPawn are read anywhere (script or
   NativeRotationCameraDefenseur); none of the decoded natives reads them.
4. Who writes SBasePawn.m_prevPos (+0x4d4), the previous position rule 3.8 measures the pawn's movement from; the
   port uses the pawn's location at the camera's last solve. REA shows the moved vector is the solver's start point
   before smoothing (pawn X and Y, Z lifted by CollisionHeight - m_collisionHeight; the bone point in mode 34) minus
   m_prevPos, so if m_prevPos holds the unlifted location the dot product carries a constant lift x forward.Z term.
5. The bone axes of the first-person bone view (rule 5.2) assume the client's posed bones keep the original mesh's
   bone frames.
