# Merc reticle, sound arrows, reticle message, posture icon and alert box

Units:
- the merc page drivers of P_HudDEF for Reticule, ReticuleMessage, Pos_Merc and AlertMessage (native, unnamed)
- nat:SBase.SHud.HudDef (its aim point helper, game 0x10af7cf0)
- nat:SBase.SHud.DrawTargetName (rule 3e; also called by s_Hud_UseObject_CamNetwork.DrawHUD)
- uc:SBase.SHud:NotifyHotSound
- uc:SBase.SPlayerController:ClientNotifyNoise

## Purpose

What the merc page draws at the aim point and next to the ammunition. spec/hud's sibling files cover the rest of
the page; docs/HUD.md the page player.

## Data

| Field | Owner | Notes |
|---|---|---|
| WeaponLoaded, MyWeapon | SBasePawn | weapon out, the weapon's class picks the reticle |
| TorcheLaser.bInUse | SPAWNDEFENSE | the laser sight is on |
| bNoReticule | SPlayerController | set by about 39 script states (reload, ladders, grabs, KO, charge) |
| fDateLastShootedPawn | PlayerController | the local merc's shot hit a pawn |
| SpotReticule | SHud | aim point in 640 x 480 canvas units (z written 0) |
| P | SHud | the viewed pawn (PostRender: `SBasePawn(PlayerOwner.ViewTarget)`) |
| MyDesiredRotation | SPlayerController | the aim the rifle traces and the frag is thrown along |
| vCamLocation, rCamRotation, FovAngle | SPlayerController | the view of the frame (PlayerCalcView) |
| bReticuleFlash, ReticuleFlashOn/Off/Time, ReticuleFlashAlpha | SHud, HUD | the dormant flash alpha |
| Left/Right/Top/Bottom/Forward/BackwardArrowTime, ...ArrowInt | HUD | sound arrow ends and loudness |
| ReticuleMessage | SHud | StackedMessage |
| bIsCrouched | Pawn | posture |

## Rules

Aim point (SHud.PostRender for a merc, then HudDef: its helper at game 0x10af7cf0, before the Enhanced Reality markers
and the motion tracking):

3a. PostRender sets `P = SBasePawn(PlayerOwner.ViewTarget)` when there is a view target; a spectator, a hidden HUD
    (`bHideHUD`) or a spy's pawn (HudAtt) never reaches HudDef. The helper does nothing while `P.MyWeapon` is None.
    Otherwise `ReticuleFlashAlpha` is 1 without `bReticuleFlash`; with it, `e = now - ReticuleFlashTime` gives
    `1 - e / ReticuleFlashOn` while `e <= ReticuleFlashOn`, else `(e - ReticuleFlashOn) / ReticuleFlashOff`, clamped
    to 0..1, and once `e` passes `ReticuleFlashOn + ReticuleFlashOff` the time moves on by that period (no page reads
    the alpha).
3b. Unless `bReticuleOff`, `P.WeaponLoaded == 0` or there is no PlayerOwner, the aim point is
    `P.Location + P.WalkBob + (0, 0, P.EyeHeight) + 15000 * vector(MyDesiredRotation)`: the eye as the native reads it
    (the same point SPAWNDEFENSE.EyePosition gives) along the aim. The laser's target name pass and the HUD tracker's
    expiry run here too, before the aim point (rule 3e).
3c. The point goes through the view of the frame (the controller's PlayerCalcView, with FovAngle, in an
    FCameraSceneNode) and the canvas' screen-to-canvas matrix: `SpotReticule = (x, y, 0)` in 640 x 480 units. A point
    on the view's axis lands on (320.5, 240.5) (measured). Nothing smooths it: when the aim is the view (the merc's
    mouse sets both) it stays there, and an aim kick that the view has not taken shows as an offset.
3d. The port projects through the last tick's calc view (vCamLocation, rCamRotation, FovAngle; FovAngle across the
    4:3 frame, wider screens adding width, as the renderer draws), the original's same-frame pairing of aim and view;
    the picture drawn between ticks turns with the camera, so the reticle keeps its place in the view.

3e. **The laser's target name** (`DrawTargetName`, native 1809, game 0x10af7800, body 0x10af7920). The merc HUD's
    aim point helper runs the body past the gates of rule 3b (and P.MyWeapon), and `s_Hud_UseObject_CamNetwork.DrawHUD`
    calls the native after the goggles' tracker. Only while the viewed merc's laser sight is on
    (`P.TorcheLaser.bInUse`):
    - The HUD's own tracker `MyTrackingVision` (an SMotionTrackingVision, never activated) is spawned the first time,
      with `PlayerOwner` the HUD's.
    - When the laser has a target (`TorcheLaser.TargetPawn`, the laser native's pick): its lock in that tracker gets
      `EndTime = now + TimeToStayLock` (1.5 s); with no lock yet, a new lock goes first in the list with `StartTime =
      now`, the same `EndTime` and both flags clear, `nbCarName = 0`, and `Snd_TeamateInSight`
      (Armes_impacts.Gadgets.Hud_viseProgress, volume 1, radius 100) plays when the target is a merc (any team). Then
      `MyTargetName` = the target's player name.
    - The tracker's HUD is drawn (`RenderMotionTracking`, spec/equipment/motion-tracking.md DrawHUD). A lock never
      fades: its `bRemove` stays clear, so it is held until it expires.
    - For the target's lock, once `now > StartTime + TimeToScanName` (1.5 s): a teammate (same `SPawnType` as P) sets
      the lock's `ItsMyFriend` (so the tracker draws the friend marks and the blinking "Hold Fire") and writes the name
      in `iColorFriend_R/G/B`; anyone else in `Hud_R/G/B` (the merc's 128, 93, 34), alpha 255, in the canvas font.
      Place: X = `(SP1.X + SP2.X) / 2 - XL / 2` (XL, YL the size of the whole name), Y = `SP1.Y - YL`, truncated, at
      ResolutionRatioX/Y. Typing: while `nbCarName < len - 1` it grows by one each frame and the first `nbCarName`
      characters are drawn with a `_` after them; then the whole name.
    Every call (laser on or off) then removes the first lock whose `EndTime` has passed, one a frame. The lock thus
    stays 1.5 s after the laser leaves the target. The goggles' tracker never marks friends; only this one does.

Reticle (`Reticule` -> A_Reticule; nothing happens without a pawn, the areas keep their visibility):

1. Hidden while the weapon is holstered (`WeaponLoaded == 0`), the laser sight is on, or `bNoReticule`.
2. Otherwise the reticle area of the weapon class is shown (`MERC_reticule_uzi` for SWeaponDefense_Uzi,
   `MERC_reticule_shotgun` for SWeaponDefense_Shotgun, `MERC_reticule` for other SWeaponDefense; the others are
   hidden only by the page init, never again) and A_Reticule is shown.
3. Placement: the page instance `Reticule` moves to `ftol(SpotReticule - half)`, with
   `half = (max - min) / 2` (integer division) of the box of A_Reticule's elements accumulated from (0, 0)
   (game 0x10b286a2, FUN_10ba4ab0: every element, hidden ones included). Each element's rect getter is not decoded;
   the original draws with a half of (64, 62): at rest its centre dot (rond02) is drawn from pixel (637, 357) at
   1280 x 720, one unit up and left of SpotReticule (320.5, 240.5), and about half a unit nearer with the reticle
   opened by a strafe. Drawing only the parts shown now (half about (52, 42)) put the dot 11 units right and 20 down
   of the aim: 16 px right and 30 px down at 1280 x 720, where the shots do not go.
4. Hit marker: the inner `Reticule` instance (A_AllReticule) is red while `fDateLastShootedPawn + 0.4 > now`, white
   otherwise.
5. Spread: the selected area's frame is `trunc(10 x GetAccuracy() / (MaxDispersion - MinDispersion))`, held (frames
   past 10 hold the frame-10 keys).
6. Sound arrows, with `t = trunc(now)`: `arrowSoundLeft` while `t < LeftArrowTime`, `arrowSoundRight` (Right),
   `arrowSoundForward` (Forward), `arrowSoundBehind` (Backward) are instances shown from frame 0 and played when they
   appear; the `arrowSoundUp` and `arrowSoundDown` widgets show while `t < TopArrowTime` / `BottomArrowTime`.
7. The whole reticle driver is frozen during a HUD shutdown.

Sound arrow times (`SHud.NotifyHotSound(NoisyActor, NoiseLoudness)`, from `SPlayerController.ClientNotifyNoise`,
which skips every merc weapon dummy (`SWeaponDefenseDummy`, the script's spy check there is empty), so rifle shots
give no arrow):

8. `Loudness = NoiseLoudness x (1600 - d) / 1600`, clamped to 0..1, with d the distance from the pawn; 0 within
   50 uu. Nothing at 0.01 or below.
9. More than 240 uu above the pawn sets Top, more than 240 below sets Bottom. Then the yaw from the pawn to the
   noise, relative to the pawn's yaw and normalised: below 8192 in magnitude Forward, beyond 24576 Backward, 8192 to
   24576 Right, -24576 to -8192 Left.
10. An arrow fires when `now >= its time` or the loudness beats its stored loudness: its time becomes now + 1 and
    its loudness the new one; the `echo_indic1` sound plays when `now >= its time + 1`.

Reticle message (`ReticuleMessage` -> A_MessageReticule):

11. While `SHud.ReticuleMessage.Message` is not empty its widget shows the text and the instance moves to
    `(SpotReticule.X - 320, SpotReticule.Y - 240)`; once `StartedTime + StayInStackTime <= now` the message is
    cleared. With no message the widget hides.

Posture icon (`Pos_Merc` -> A_TamponPosMerc -> Move_Merc, never hidden while the page shows):

12. Crouched: when Move_Merc is stopped at frame 10 or below, it plays from frame 10 (to 14, crouched). Standing:
    when it is stopped at frame 14 or later, or at 0, it plays from 0 (to 4, standing). A change during the 0.4 s
    cross-fade waits until it stops.

Alert box (`AlertMessage` -> A_MessageAlert):

13. Shown from frame 0 and played when an objective countdown starts (the ChronoObjectif driver returns true:
    spec/hud/counters-and-prompts.md rule 3), hidden when no countdown runs. Spies have no alert box.

## Constants

| Name | Value | Unit | Provenance | Source |
|---|---|---|---|---|
| Aim point distance | 15000 | uu | decoded | game 0x10af7f03 |
| Aim on the view axis | (320.5, 240.5) | canvas units | measured | sandbox capture, eight aims, standing, crouched, strafing |
| Reticle half box | (64, 62) | page units | measured | sandbox capture (decoded formula, rect getter not decoded) |
| Hit marker time | 0.4 | s | decoded | reticle handler 0x10b28550 |
| Noise range | 1600 | uu | decoded | SHud.NotifyHotSound |
| Noise dead zone | 50 | uu | decoded | SHud.NotifyHotSound |
| Up / down | 240 | uu | decoded | SHud.NotifyHotSound |
| Front / back yaw | 8192 / 24576 | rotator units | decoded | SHud.NotifyHotSound |
| Arrow time | 1 | s | decoded | SHud.NotifyHotSound |
| Posture frames | 0 to 4 standing, 10 to 14 crouched | frame | decoded | posture handler 0x10b283f0 |
| Target lock hold | TimeToStayLock 1.5 | s | decoded | game 0x10af7aa8; SHud defaults |
| Target name delay | TimeToScanName 1.5 | s | decoded | game 0x10af7b12; SHud defaults |
| Target name typing | one character a frame, `_` cursor | | decoded | game 0x10af7c16 |
| Sight sound radius | 100 | uu | decoded | game 0x10af7a1b |

## Network

`ClientNotifyNoise` is a reliable server-to-client function of the controller. Everything else is read on the
merc's own machine.

## Edge cases

- The weapon reticle areas are never hidden again once shown; a weapon change mid-life could show two.
- `bDrawReticule` and `SHud.bReticuleOff` are not read by the page (binoculars replace the page anyway).

## Evidence

- SCCT Versus UE5 Bridge HUD research, docs/overlays/reticle.md sections 1 to 3 (reticle handler 0x10b28550,
  posture handler 0x10b283f0, merc update 0x10b27190, A_Reticule and Move_Merc keyframes).
- `local/re-out/uc/SBase/SHud.uc` NotifyHotSound, PostRender; `SBase/SPlayerController.uc` ClientNotifyNoise.
- Game 0x10af7cf0 (the aim point helper, called from 0x10af8060 in ASHud::execHudDef), its projection
  (FCameraSceneNode 0x10a165a0, project 0x10a12d60, screen to canvas 0x1094ff40 through Canvas.pCanvasUtil + 0x14);
  the reticle handler 0x10b28550 (placement 0x10b286a2..0x10b28708, box FUN_10ba4ab0, ftol 0x10b83aa0). Decompiles
  and disassembly under local/re-out/c/reticle (private Ghidra copy local/ghidra-work/reticle).
- Sandbox capture (local/reticle/capture.py, SteeE combat rig, headless, 1280 x 720): SpotReticule, the calc view,
  the eye and the drawn frame at eight aims; the camera is the eye (Location + WalkBob + EyeHeight) standing (55.5)
  and crouched (20.0); the tracer impacts of single shots fall on the dot within the spread.

## Open questions

1. Which actors replicate to the merc's machine: a noise from an actor the client does not have (not relevant to
   it) arrives without its actor and gives no arrow.
2. The rect getter of a Magma element (vtable + 0x10 of the object at element + 0x2c) is not decoded, so the half box
   of rule 3 is the measured (64, 62) rather than computed from the elements' keys (no combination of first, last or
   current keys with or without hidden elements gives it); the original's dot moves about half a unit as the reticle
   opens.
3. Whether Reloaded's wide-screen page maps a SpotReticule off the centre to the same pixel as the projection (the
   port puts the reticle on the projected pixel); only an aim kick the view has not taken moves it off the centre.
