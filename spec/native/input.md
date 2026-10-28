# Player input natives: NativePlayerInput and NativeComputeControlVariables

Units:
- nat:SBase.SPlayerController.NativePlayerInput
- nat:SBase.SPlayerController.NativeComputeControlVariables
- uc:SBase.SPlayerController:GetSpeedMode (called back by the native)

## Purpose

The two natives that turn the raw input fields of the controller (the movement buttons BT_Up, BT_Down, BT_Left,
BT_Right, the mouse axes aMouseX and aMouseY, the action bytes) into the stick values, look values and the move
mask iMove that the movement script reads (spec/movement/input-and-rotation.md, walking.md).

## Data

| Field | Owner | Written | Meaning |
|---|---|---|---|
| aForward, aStrafe | PlayerController | yes | stick values from the buttons, up to 6000 x speed mode |
| aTurn, aLookUp | PlayerController | yes | mouse look, scaled to a 640 x 480 reference |
| fTurnValue, fLookUpValue | PlayerController | yes | 0.75 x aTurn / aLookUp, clamped to +-2000 |
| fNormePadMove | PlayerController | yes | button movement norm, 0 to 1 |
| fNormePadCam | PlayerController | yes | look norm |aTurn, aLookUp| / 6000 |
| iMove | SPlayerController | yes | direction bits (rule 3) |
| BT_UseJusteAppuye, bUse_OldTrame | SPlayerController bools | yes | Use pressed this frame, Use state of the previous frame |
| iDontUpdatePadInput | PlayerController | read | non-zero: all inputs forced to zero |
| bInvertVerticalAxis | SPlayerController | read | flips fLookUpValue |

## Rules

### 1. Input mapping (the shared body; editor 0x111e6460, game 0x10aaa910)

Run by NativePlayerInput (spectating and dead states) and by NativeComputeControlVariables (every live
PlayerTick) unless iDontUpdatePadInput is set.

1. aTurn = SizeX x aMouseX / 640 and aLookUp = SizeY x aMouseY / 480, where SizeX and SizeY are two integer fields
   of the controller's Player (the viewport size in pixels, by reading).
2. k = the script event GetSpeedMode(): 0.8 with binocular vision, 1.0 without a pawn, for the merc or in fast
   mode, else 0.6 for a spy in s_PlayerWalkingWeapon and 0.4 for an unarmed spy (slow mode).
3. aForward = (BT_Up - BT_Down) x 6000 x k; aStrafe = (BT_Right - BT_Left) x 6000 x k (BT bytes are 0 or 1, so
   one key gives exactly 6000 k; opposite keys cancel; a diagonal gives 6000 k on both axes).
4. fNormePadMove = (|BT_Right - BT_Left| + |BT_Up - BT_Down|) x k, capped at 1.0.
5. fNormePadCam = sqrt(aTurn^2 + aLookUp^2) / 6000.
6. fTurnValue = clamp(0.75 aTurn, -2000, 2000); fLookUpValue = clamp(0.75 aLookUp, -2000, 2000), negated when
   bInvertVerticalAxis.
7. Any analog axis value the input bindings wrote into aForward or aStrafe is overwritten: movement comes only
   from the buttons.
8. **Reloaded** (Reloaded.Core 0x1001ab50, entered from game 0x10aaa973 right after GetSpeedMode, replacing steps
   3 and 4): each axis is also multiplied by a factor f, which is 1 unless the local player's pawn is a standing
   spy (SPawnAttaque, not bIsCrouched) holding a forward or back key and a side key, and Reloaded's own pad code set
   no analog scale this frame: then f = 0.70710677 on both axes (rl:fix.slow_strafe: a diagonal is no faster than a
   straight line). `aForward = (BT_Up - BT_Down) x 6000 x k x f`, the same for aStrafe, and `fNormePadMove =
   (|forward| f + |strafe| f) k`, capped at 1. Then the ledge assist (rl:input.ledgeassist): with `bUsing` held,
   `bFastSpeed` clear and `fNormePadMove > 0.4`, `fNormePadMove = 0.4` (spec/movement/ledge.md, Reloaded ledge
   assist). A second hook at 0x10aaaa32 (Reloaded 0x10019d30) replaces step 6's turn and look values; not decoded
   here.

### 2. NativeComputeControlVariables(dt) (editor exec 0x111f37d0, game 0x10aaf400)

1. Use edge: BT_UseJusteAppuye = bUsing and not bUse_OldTrame; then bUse_OldTrame = bUsing. Without bUsing,
   BT_UseJusteAppuye is cleared.
2. The input mapping of rule 1, or, with iDontUpdatePadInput, zero for aForward, aStrafe, aLookUp, aTurn,
   fLookUpValue, fTurnValue, fNormePadCam, fNormePadCamSmoothee, bPressedJump, bHoldBreath, bFireMode, bUsing,
   bUseGadget, bUnCrouchAndUnDraw and bUseBackToWall.
3. iMove: the previous value is kept for the hysteresis below, then iMove = 0. Without SBPawn or Pawn it stays 0.
4. Then the mask (rule 3).

### 3. iMove bits

| Bit | Name used here | Set when |
|---|---|---|
| 1 | left | ledge or fence: aStrafe < -2100; else the camera-relative move points to the pawn's left by more than 2100 |
| 2 | right | ledge or fence: aStrafe > 2100; else to the pawn's right by more than 2100 |
| 4 | forward | ledge or fence: aForward > 2100; else the move along the pawn's facing is above 4500 (3000 when bit 4 was set on the previous frame) |
| 8 | backward | ledge or fence: aForward < -2100; else the move along the facing is below -4500 |
| 16 | stick up | walking, falling or merc: aForward > 3000; other spy modes: the screen-space test below > 0.45 |
| 32 | stick down | walking, falling or merc: aForward < -3000; other spy modes: < -0.45 |

1. Ledge (Physics 9) and fence (Physics 8): bits 1, 2, 4, 8 from the raw stick against +-2100, nothing else.
2. Otherwise bits 16 and 32 first: with the pawn in Walking or Falling, or for the merc, from aForward against
   +-3000. For a spy in other modes (pipes, ladders, zip lines, Vandamme) the stick (aStrafe, -aForward) is
   compared in screen space with a direction built from the pawn's axes projected to the screen (relative to the
   viewport centre): the normalised dot product above 0.45 sets 16, below -0.45 sets 32. The stick vector is
   `(aStrafe, -aForward, 0.01)` and is normalised, so the speed mode does not change the bits (a slow spy's key, 2400,
   climbs). The projection details are not fully decoded (open question 2); the port takes the screen's up as the
   climb direction.
3. Then, for every mode but ledge and fence, the world move M = X' aForward + Y' aStrafe with X' and Y' the
   controller's (camera) yaw axes flattened and normalised; with F the pawn's facing and F x Up its left axis:
   M . (F x Up) > 2100 sets 1, < -2100 sets 2; M . F_xy / |F_xy| above 4500 (3000 with last frame's bit 4) sets 4,
   below -4500 sets 8.

### 4. Ground speed check (script, for reference)

The movement script turns the stick into GroundSpeed = RunningSpeed (385) x |newAccel| / 3000 with newAccel =
(int(0.5 aForward'), int(0.5 aStrafe')) after its own factors (walking.md). With rule 1 the measured speeds follow
exactly, including the integer truncation:

| Case | aForward after the script factors | int(0.5 x) | Speed (uu/s) |
|---|---|---|---|
| Spy fast run | 6000 x 1.0 x 1.1 = 6600 | 3300 | 423.5 |
| Spy slow (k 0.4) | 6000 x 0.4 x 1.1 = 2640 | 1320 | 169.4 |
| Spy crouched | 6000 x 1.0 x 0.575 = 3450 | 1725 | 221.375 |
| Merc standing | 6000 x 1.0 x 1.0 x 0.9 = 5400 | 2700 | 346.5 |
| Merc crouched | 6000 x 0.33767 x 0.9 = 1823.4 | 911 | 116.91 |

The merc crouched value (116.91, not 117.0) is the truncation of 911.7 to 911.

## States

None.

## Constants

| Name | Value | Unit | Provenance | Source |
|---|---|---|---|---|
| Button stick value | 6000 | stick units | decoded | input body |
| Mouse reference | 640 x 480 | px | decoded | input body |
| Look scale and clamp | 0.75, +-2000 | - | decoded | input body |
| Look norm divisor | 6000 | - | decoded | input body |
| Ledge and fence threshold | 2100 | stick units | decoded | NativeComputeControlVariables |
| Up and down threshold | 3000 | stick units | decoded | NativeComputeControlVariables |
| Forward threshold | 4500, 3000 with hysteresis | stick units | decoded | NativeComputeControlVariables |
| Side threshold | 2100 | stick units | decoded | NativeComputeControlVariables |
| Screen-space up/down threshold | 0.45 | cosine | decoded | NativeComputeControlVariables |
| Speed modes | 0.4, 0.6, 0.8, 1.0 | - | decoded | GetSpeedMode |

## Network

Local only: the natives run on the owning client (and on a listen host for its own player). The results reach
the server through the move (net-moves.md: acceleration, iMove-derived action flags), never as raw inputs.

## Edge cases

- With both keys of an axis held the axis is 0 (no last-key priority).
- A diagonal in slow mode gives 2400 on both axes; whether that is faster than straight slow movement depends on
  the script's normalisation (walking.md), not on these natives.
- iMove's forward hysteresis uses the previous frame's iMove only when it was computed (a frame without a pawn
  resets it).
- The look values are not scaled by dt here; the rotation code applies the frame time.

## Evidence

- Editor: execNativePlayerInput 0x111ede90, input body 0x111e6460, execNativeComputeControlVariables 0x111f37d0.
- Game: execNativePlayerInput 0x10aaa8d0, input body 0x10aaa910, execNativeComputeControlVariables 0x10aaf400
  (same constants).
- Script: GetSpeedMode (SPlayerController), PlayerTick of s_Dead and s_BaseSpectating (NativePlayerInput), the
  main PlayerTick (NativeComputeControlVariables).

## Open questions

1. Confirm that the two Player fields of rule 1.1 are the viewport size (a resolution change would scale mouse
   look); the 640 x 480 divisors suggest it.
2. The exact screen-space construction of rule 3.2 for climbing modes (which pawn axis is projected and how the
   magnitude enters). Trace: on a ladder and a pipe, hold W and S with the camera at several yaws and read iMove.
