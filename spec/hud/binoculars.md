# Binocular pages: the spy's binoculars and the merc's rifle scope

Units:
- the binocular page objects of the HUD page manager (native, unnamed): P_BinocularsATT (init 0x10b35190, update
  0x10b350c0, key help 0x10b34f30, speak box 0x10b35370) and P_BinocularsDEF (init 0x10b33d70, update 0x10b342c0,
  key help 0x10b339f0, reticle message 0x10b347b0)
- uc:SBase.SHud:s_H_Binoculars_Att.PostRender, uc:SBase.SHud:s_H_Binoculars_Def.PostRender
- nat:SBase.SHud.DrawObjectivesBinocularsAtt (the drawing; the target choice is spec/equipment/spy-binoculars.md)

## Purpose

What the HUD shows while a spy looks through his binoculars or a merc through his rifle scope. The views themselves
(entering, zoom steps, field of view, breathing, locking an objective) are spec/equipment/spy-binoculars.md and
spec/equipment/merc-scope.md; this spec is the HUD side: which page is drawn and how its areas follow the controller.

## Data

| Field | Owner | Type | Notes |
|---|---|---|---|
| state s_Binoculars_Att / s_Binoculars_Def | SPlayerController | state | picks the page; s_Binoculars_Def_Reloading extends s_Binoculars_Def |
| fBinocularVisionFOV | SPlayerController | float | zoom step 30, 20, 10 |
| BinocularHoldBreathTime | SPlayerController | float | breath left, 0 to 5 s |
| bHoldBreath | SPlayerController | byte (input) | the merc holds his breath |
| fDateLastShootedPawn | PlayerController | float | the local player's shot hit a pawn |
| VoiceChatTarget | SPlayerController | Actor | a comms hack target |
| SpotReticule | SHud | vector | aim point in 640 x 480 canvas units |
| ReticuleMessage | SHud | StackedMessage | "Reloading" and the like |
| EnhancedRealityLevel, LockedObjective | SHud | int, SObjectiveTrigger | the spy's markers and his lock |
| BinocularsAttLock, BinocularAttLocked, DropZone | SHud | localized strings | "Press \USE to lock.", "Obj. locked, press \USE to unlock.", "Drop zone" |
| fDistanceUnit, StrDistanceUnit | SHud | float, string | 0.01 "m" (HudUnit) |

## Rules

Page choice:

1. The page manager draws P_BinocularsATT instead of P_HudATT while the spy's controller is in `s_Binoculars_Att`,
   and P_BinocularsDEF instead of P_HudDEF while the merc's is in `s_Binoculars_Def` (the reloading child state
   included). The page object is made anew each time, so every visit starts as rule 2 says. The game stretches both
   pages over the whole screen, texts included (the inner frame of the spy page is 804 pixels wide at 1280 x 720).

Both pages:

2. Creation: every top-level area at frame 0; the frame, sound meter (spy) and zoom areas shown and played; the
   console-only texts hidden (spy: IconZoomUp, IconZoomDown, textZoomAtt1, textZoomAtt2 and the comms hack help
   PCtextZoomAtt3 with its binding; merc: IconZoomUp, IconZoomDown, textZoomDef1 to 3 and PCtextZoomDef4 with its
   binding); the zoom label "x2" while the recorded step is 1; the speak box's Element4 hidden; the invitation icon
   hidden; the key help written (rule 6). The merc's range text and reticle message start empty.
3. Each update, the step `trunc(fBinocularVisionFOV x 0.1)` is compared with the recorded one; on a change the label
   becomes "x6" for step 1, "x4" for step 2 and "x2" otherwise, and the step is recorded. Because the record starts
   at 1, the first update on entering (FOV 30, step 3) already counts as a change.
4. The page follows the speak box (rule 7) and rewrites the key help when the console reports changed keys.

Spy page:

5. On a step change the zoom marker `zoomInside` moves to its home x + 35 for x6, its home x for x4 and its home x
   - 35 for x2 (home 317, 387). The frame, the sights and the self-looping sound meter (2.8 s) are page keyframes.

Key help and speak box:

6. "Zoom In / Zoom Out:" takes `"%s / %s"` of the console's two wheel key names (Mouse Wheel Up / Mouse Wheel Down
   in the stock layouts), "Exit:" the key bound to `SwitchBinocularVision`, the merc's "Hold Breath:" the key bound to
   `AltFire | GadgetQuickChoice 1 | HoldBreath`. An unbound command gives an empty text. Key names are the
   Engine.int `LocalizedKeyName` entries.
7. While `VoiceChatTarget` is set, the speak box (A_SpeakATTBin or A_SpeakDEFBin) shows MagmaDynamic `TextChat2`
   ("Hack"); when it was hidden it is shown, rewound to frame 0 and played. Without a target a shown box hides.

Merc page:

8. On a step change the frame area jumps to frame 10 (zooming in, the step went down) or 20 (zooming out) and plays:
   the scope rings dip, the inner ring closes to a circle or opens wide, the side bars slide, then the frame's own
   keys go back to its static loop. Entering the scope therefore plays the zoom-out keys once (rule 3).
9. Range finder: while the pawn has a weapon with a mesh, a line from a bone of the weapon along the bone's X axis for
   15000 uu; `d` is the distance to the hit, 0 without one. The text is `"%02i.%02i"` of `a = trunc(d x fDistanceUnit)`
   and `b = trunc(d - a / fDistanceUnit)` (whole metres and the uu left over, "12.37").
10. The sights' four corners are (253, 178, 68, 255), or (255, 0, 0, 255) while `fDateLastShootedPawn + 0.4` is later
    than now. Without held breath the sights' top-left goes to `(trunc(SpotReticule.X - 31), trunc(SpotReticule.Y -
    17))`, the vertical axis to `(trunc(SpotReticule.X), 0)` and the horizontal to `(0, trunc(SpotReticule.Y))`;
    holding the breath puts them back at (289, 223), (320, 0) and (0, 240). The breath gauge is set to frame
    `trunc((5 - BinocularHoldBreathTime) x 4)` and stopped. The reticle message shows while `SHud.ReticuleMessage`
    is not empty, its widget moved by `(trunc(SpotReticule.X - 320), trunc(SpotReticule.Y - 240))`; once its time is
    over it is blanked to " " (stay 0, started now, priority 1).
11. The merc's aim point (`s_H_Binoculars_Def.PostRender`): `SpotReticule` is the screen point of
    `Pawn.Location + EyePosition() + X x 1000` with X from `MyDesiredRotation`, clamped to 0.25 to 0.75 of the width
    and 0.2 to 0.8 of the height. The same PostRender ends an expired HUD perturbation, counts the foam
    (`DrawProjection`, not while dead) and runs the screen effects.

Spy markers (`DrawObjectivesBinocularsAtt`, called by `s_H_Binoculars_Att.PostRender` only while
`EnhancedRealityLevel != 0`, with the target point (0.46 ClipX, 0.45 ClipY)):

12. Canvas space is 640 x 480 stretched over the screen; the normal Enhanced Reality markers are not drawn in the
    binoculars. Margins: X0 = 68, Y0 = 67 (half the 32 x 32 arrow texture plus 52 and 51).
13. Each entry of `ObjectivesAndTriggersList`: objectives are skipped; an objective trigger is kept when `bActive` or
    `bLocked`, or when it is a drop zone in GM_Multi; other actors are kept.
14. Colours before the canvas doubling (spec/hud/screen-effects.md rule 0): a triggering trigger (96, 0, 0), else a
    locked one (96, 96, 0), else (100, 123, 92); other actors (128, 128, 128). A trigger's percent is its capture
    percent clamped to 0..99; other actors have none.
15. On screen means `X0 < sx < 640 - X0`, `Y0 < sy < 480 - Y0` and in front. The first trigger per frame with
    `sx - 40 < TargetX < sx` and `sy - 40 < TargetY < sy` is the lock candidate: instead of its icon, the panel
    (BinocularA_all tile 0, 64, 128 x 64) at (sx - 73, sy - 43) sized 300 x 60, its name (DropZone for a drop zone,
    else TriggerName) at (sx - 70, sy - 40), `"%.0f %s / %i %%"` (distance, unit, percent; `"%.0f %s"` for a drop zone,
    a disk or an entry without percent) at (sx - 69, sy - 20) and the lock hint at (sx - 69, sy - 5):
    BinocularAttLocked when the trigger is `SHud.LockedObjective`, else BinocularsAttLock, with `\USE` expanded to
    the Use key's name.
16. Other on-screen entries (triggers, or actors with bSecondaryObjective): the entry's MN_boutons icon 30 x 30 at
    (sx - 15, sy - 15) in its colour, then in 128 grey a top-right bracket (tile 121, 96) at (sx + a, sy - a - 7) and
    a bottom-left one (tile 96, 121) at (sx - a - 7, sy + a), 7 x 7, with a = 6 while `frac(TimeSeconds) > 0.5`, else
    13 (a 1 Hz pulse).
17. Off-screen entries: the point is clamped to [X0, 640 - X0 - 16] x [Y0, 480 - Y0 - 16] (an entry behind the view is
    first pushed to the opposite edge) and the Att_Arrow texture drawn 32 x 32 centred there, turned by
    `Yaw = 16384 - atan2(sy - 240, sx - 320) x 10430.2` (the texture's arrow points down, so it points at the entry).

## Constants

| Name | Value | Unit | Provenance | Source |
|---|---|---|---|---|
| Zoom labels | x6, x4, x2 for steps 1, 2, other | | decoded | strings at 0x10c37a34.., 0x10c3761c.. |
| Spy zoom marker step | 35 | page units | decoded | 0x10b35190 (`+ 0x23`, `- 0x23`) |
| Merc zoom-in / zoom-out frames | 10 / 20 | frame | decoded | 0x10b342c0 |
| Hit flash | 0.4 | s | decoded | 0x10b342c0 |
| Sights colour / hit colour | (253, 178, 68, 255) / (255, 0, 0, 255) | RGBA | decoded | 0x10b342c0 (0xff44b2fd, 0xff0000ff) |
| Sights half size | 31, 17 | page units | decoded | 0x10b33d70 (from the widget's rect 289, 223, 352, 258) |
| Breath gauge frame | trunc((5 - hold) x 4) | frame | decoded | 0x10b342c0 (constants 5.0, 4.0) |
| Range line | 15000 | uu | decoded | 0x10b342c0 |
| Range text | %02i.%02i | | decoded | string at 0x10c37814 |
| Aim distance | 1000 | uu | decoded | s_H_Binoculars_Def.PostRender |
| Aim clamp | 0.25..0.75 W, 0.2..0.8 H | | decoded | s_H_Binoculars_Def.PostRender |
| Marker target | 0.46 W, 0.45 H | | decoded | s_H_Binoculars_Att.PostRender |
| Marker margins | 68, 67 | canvas units | decoded | 0x10a90f70 |
| Lock box | 40 | canvas units | decoded | 0x10a90f70 |
| Lock panel | tile (0, 64, 128, 64), 300 x 60 at (-73, -43) | | decoded | 0x10a90f70 |
| Marker text height | 13 | canvas units | estimated | C.TinyFont not decoded |

## Network

Nothing is sent: the pages read the local controller and SHud. The zoom step, the breath and the hit time are the
owning client's own state (spec/equipment/merc-scope.md, spy-binoculars.md).

## Edge cases

- Entering the scope always plays the zoom-out ring keys once, since the fresh page records step 1.
- The spy's zoom label and marker change on the step, not the eased field of view.
- With no free point to aim at (no projector), the merc's sights and axes stay at the page centre.
- A trigger that is neither active nor locked has no marker, even while its objective counts.

## Evidence

- Own Ghidra copy local/ghidra-work/hud: 0x10b35190, 0x10b350c0, 0x10b34f30, 0x10b35370, 0x10b33d70, 0x10b342c0,
  0x10b339f0, 0x10b347b0 (decompiles and instruction listings in local/re-out/c/actions/game/), helpers 0x10ba4980
  (SetVisible), 0x10ba49e0 (Play/Stop), 0x10ba4a20 (SetTime); strings read from the image.
- `local/re-out/uc/SBase/SHud.uc`: states s_H_Binoculars_Att and s_H_Binoculars_Def, defaults (BinocularsAttLock,
  BinocularAttLocked, DropZone).
- SCCT Versus UE5 Bridge research docs/overlays/binoculars.md: the page data, DrawObjectivesBinocularsAtt 0x10a90f70,
  and the live capture that settled the stretch and the key help texts.
- `Menus/Hud.mgm` (converted `shared/ui/magma/Hud.json`): P_BinocularsATT and P_BinocularsDEF areas and keyframes.

## Open questions

1. The weapon bone of the range finder is only an FName index (393); the port uses the rifle's FX bone.
2. The page manager's extra check before the merc page (name 0x445) is not identified.
3. The alert box on the merc page (A_MessageAlert) runs from a shared helper whose trigger is not decoded; the port
   shows it as on P_HudDEF (spec/hud/merc-reticle.md rule 13).
4. The lock panel inherits the canvas colour of the previous draw (usually 128 grey); the port draws it grey.
