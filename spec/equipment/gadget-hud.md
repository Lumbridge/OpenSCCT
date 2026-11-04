# Gadget HUD views (sticky cam, camera network, menus, gas mask, heart sensor)

Units:
- uc:SBase.SHud:IsInSticky, uc:SBase.SHud:IsInCamNetwork
- uc:SBase.SHud:s_Hud_UseObject_StickyCam.EndState, uc:SBase.SHud:s_Hud_UseObject_StickyCam.DrawHUD,
  uc:SBase.SHud:s_Hud_UseObject_StickyCam.PostRender
- uc:SBase.SHud:s_Hud_UseObject_CamNetwork.EndState, uc:SBase.SHud:s_Hud_UseObject_CamNetwork.DrawHUD,
  uc:SBase.SHud:s_Hud_UseObject_CamNetwork.PostRender
- The native page objects of P_StickyCam and P_CamNetWork, and the gadget parts of the P_HudATT and P_HudDEF page
  objects (no UFunction ids; drawing only).

## Purpose
What the player sees while a gadget view or gadget menu is up: the sticky cam view page, the camera network page,
the sticky cam and mine menus, the gas mask frame and air gauge, and the heart sensor cone in the spy radar. The
gadget rules themselves are in the gadget specs (`sticky-cam.md`, `camera-network-gadget.md`, `mine-launcher.md`,
`gas-mask.md`, `heartbeat-sensor.md`). This file is the client side: it reads the simulation and drives Magma areas of
`Menus/Hud.mgm`. Conventions (640 x 480 page units, 10 frames a second, area colour times widget colour, show =
visible + frame 0 + play) are in `docs/HUD.md`.

## Rules

### SHud states
1. `IsInSticky` is true while the SHud is in `s_Hud_UseObject_StickyCam`; `IsInCamNetwork` while it is in
   `s_Hud_UseObject_CamNetwork`. The controller's sticky cam states enter the first
   (`s_UseObject_StickyCam_SmoothToPosition.BeginState`), the camera network state the second
   (`s_UseObject_CamNetwork.BeginState`); leaving the view sends the HUD to no state.
2. Both states' `EndState` store the state name in `PreviousState`.
3. The sticky state's `DrawHUD` is empty; its `PostRender` is `DrawHUD` then the base HUD post render (console lines
   only): none of the SHud canvas extras of the spy page (markers, projection, screen effects) are drawn.
4. The camera network state's `PostRender` is `DrawHUD`, the base HUD post render, then the screen effects.
   `DrawHUD` draws the motion tracker lock boxes of the merc's goggles and the laser target name (estimated here:
   the client draws the motion tracker brackets as in the motion vision).

### Page switch
5. **Spy**: while `IsInSticky`, the page `P_StickyCam` replaces `P_HudATT` (the whole spy page is hidden: gauges,
   objectives, gadget box, message areas). **Merc**: while `IsInCamNetwork`, `P_CamNetWork` replaces `P_HudDEF`. Only
   the side's own page has its switch. The page object is made on entry and dropped on exit, so its areas start at
   frame 0 every time the view is entered.

### P_StickyCam (native page init only; no per-frame logic)
6. On entry: every area at frame 0; `StickyCam` shown and playing (the Magma data loops it every 6.8 s and its
   interference bars every 5.6 s); the console widgets `IconStickyCamUp`, `IconStickyCamA`, `IconStickyCamB`,
   `textStickyCam1..3` and the `cadre` box hidden; `GameInvite` hidden.
7. Key help: `PCtextStickyCam1Binding` = "zoom in key / zoom out key"; `PCtextStickyCam2Binding` = the key bound to
   exactly `Use` (gas); `PCtextStickyCam3Binding` = the key bound to `AltFire | GadgetQuickChoice 1 | HoldBreath`
   (exit). Empty when no key is bound. Rewritten when the bindings change.
8. The page shows no zoom value, gas count or energy: the field of view is the only zoom feedback.

### P_CamNetWork
9. On entry: as rule 6 for `Camnetwork` (loops every 5.5 s); hidden: `IconStickyCamUp`, `IconCamNetA`,
   `IconCamNetB`, `IconCamNetX`, `textCamNetwork1`, `textCamNetwork2`, `textCamNetwork3`, `textCamNetwork5`, and
   `GameInvite`.
10. Camera name (`textCamNetwork4`), every frame, only while the controller has a pawn whose `UsedObject` is set: the
    used object's `tCamAlreadyInUse` when its `aUsedByPawn` is not the player's pawn; else its `CamName` when that is
    not empty; else the text stays as it was.
11. ALERT (`Alert`): shown and restarted on the frame it becomes true, hidden otherwise: any actor of the SHud's
    `ActorToDraw` that is an `SObjective` with `MinTriggerTime != 0` (a spy is hacking).
12. Key help: `PCtextCamNetwork1Binding` = zoom keys as rule 7; `PCtextCamNetwork2Binding` = the `Use` key, or
    "Use key / BTW key" when a key is bound to the back-to-wall button (next / previous camera);
    `PCtextCamNetwork3Binding` = the exit key of rule 7.
13. No energy gauge, zoom value, vision mode label or ammunition on the page. The camera's shot energy ticks in the
    simulation but nothing in this build draws or spends it from the view (Fire is empty in the view state).

### Sticky cam menu (`StickyCamInteraction` on P_HudATT)
14. Shown while the controller is in `s_StickyCam_MenuInteraction` and the pawn's selected gadget is an
    `SGadStickyCam`; on showing: restarted, the console icon `A_button` hidden. Hidden otherwise. The spy page stays
    up behind it.
15. Every frame while shown, for lines i = 0..2: `textSticky{i+1}` = the gadget's `TextMenu[i]`. Line 1 is left
    with its text only when the pawn has no used cam. The arrow `PC_button` moves to the selected line's top. Line
    colour: `(128,128,128,128)` when `iCanUse[i] == 0` and i is not 2 (Exit); white when i is
    `iCurrentSelectedMenu`; else `(200,200,200,255)`. Colours multiply with the area colour.

### Mine menu (`MineInteraction` on P_HudDEF)
16. Shown and restarted while the controller is in `s_PutMineOnWall_InteractionMenu` and the selected gadget is an
    `SGadMultiMinesLauncher`; hidden otherwise.
17. Every frame while shown, for lines i below the launcher's `NbIT` (5): `textMine{i+1}` = `MineName[i]`; the
    selected line (`iCurrentMineType`) white with the arrow `PC_button` beside it; other lines `(200,200,200,255)`;
    an entry the launcher marks unavailable (`CanUse[i] == 0`, not the last entry) `(128,128,128,128)`. The arrow
    keeps its horizontal place and takes the selected line's top. The `A_button` console icon stays hidden.

### Gas mask (`MaskGas` on P_HudDEF)
18. Hidden (with its gauge) unless the selected gadget is an `SGadGasMask` and the pawn has no `UsedObject`. The mask
    frame comes up on selection, not on activation.
19. `t = trunc(MaskDuration)`. When the area was hidden: show and restart `MaskGas/Mask` and show `MaskGas/Jauge`,
    clear the low-air latch.
20. Worn (`bGasMask`): when `t < 15` and the latch is clear, set it and, when `RespirationMask2` is hidden, hide
    `RespirationMask` and show `RespirationMask2`; otherwise when `RespirationMask` is hidden, hide
    `RespirationMask2` and show `RespirationMask`. (The latch makes the fast breathing show for one update, then the
    normal loop comes back; that is what the game does.) Not worn: both breathing areas hidden.
21. Always: the gauge frame is `2 x (30 - t)` (0 full, 60 empty), paused.

### Heart sensor cone (`Radar/Heart`, `Radar/Heart1` on P_HudATT)
22. Hidden unless the selected gadget is an `SGadHeartBeatSensor` and the team has gadgets (`iNBGadgets > 0`).
23. Weapon holstered (`WeaponLoaded == 0`): `Heart` shown at frame 0 and stopped, `Heart1` hidden. This preserves
    `Heart1`'s clock and the last-frame wrap latch. Nonzero draw/holster states and `bWeaponTransit` still scan.
24. `StateOfDetect == 1`: `Heart` hidden; `Heart1` shown (from `Heart`'s integer frame when it was hidden) and playing; the
    detected sound (`Snd_DetectedPawn`) plays when more than 1.5 s passed since the last one.
25. `StateOfDetect == 2`: `Heart1` hidden; `Heart` shown and playing (from `Heart1`'s integer frame when it was hidden
    or stopped). `GetFrame` rounds to the nearest frame, with halves rounded up; fractional frame time is not
    carried across a switch. The same rounded frame is used for the scan-ping wrap comparison, while animation
    keyframe actions still use their exact elapsed times.
26. `StateOfDetect == 0`: `Heart` shown at frame 0, stopped; `Heart1` also rewound and stopped; wrap latch reset to 0.
    Selecting another gadget hides both cones and stops `Heart` only if it was visible, preserving their frames
    and the wrap latch. Switching away directly from detection leaves both hidden clocks running.
27. When the playing loop wraps (its frame went down) and `StateOfDetect != 0`, the check sound (`Snd_Check`) plays
    once. Switching between detected and scanning retains this comparison, including a switch on the wrap frame.
    Both sounds play through the HUD sound helper (0x10b26920): no actor, 2D, volume 1, radius 4096, pitch 1, on
    the local machine only.
28. The radar dish shows while either cone area is visible (the radar's own rule). Radar runs before the cone update:
    it uses the previous frame's cone visibility and detected latch. Sensor merc blips are gated by that latch, while
    teammates and spy-bullet tags use their own rules (see [heartbeat-sensor.md](heartbeat-sensor.md)).
29. The cone uses both authored arcs: `onde2` expands and fades linearly from full opacity to zero over 18 frames,
    and `onde3` over 15 frames. They run together, with no opacity boost. The separate `onde01` gradient sweep also
    remains visible. The scanning area loops at frame 20 (2.0 s); the detected area at frame 18 (1.8 s), at 10 fps.
    The detected-sound timestamp starts at 0, so the first heartbeat cannot play before level time exceeds 1.5 s.
30. Both cone clocks continue advancing and running their loop actions while hidden, provided they are playing.
    Holstering stops only `Heart`; hidden `Heart1` continues to loop. Reselecting or drawing again transfers the
    other area's current frame, not a frozen frame from when it was hidden. This is enabled specifically for the
    two scanner areas in the port; other Magma areas retain their existing clock behavior.
31. Spy radar images use the native rectangle geometry: interpolate each edge, truncate it to an integer, scale
    the authored bounds directly, and rotate about their midpoint. Do not expand the right/bottom edges by one
    page unit. This applies to the cone masks too; extra edge expansion moves the apex and enlarges the dish.
32. Image key colors truncate to byte channels before tinting. Tinted channels round to the nearest byte, halves
    up; the normal blend halves and truncates RGB before the texture stage doubles it. Alpha is not doubled.
    These rules also apply specifically to the spy radar, preserving the other HUD areas' existing rendering.

## Constants
| Name | Value | Unit | Provenance | Source |
|---|---|---|---|---|
| Fast breathing threshold | 15 | s of air | decoded | gas mask page method 0x10b27ec0 |
| Air gauge frames | 2 x (30 - t) | frames | decoded | 0x10b27ec0 |
| Detected sound spacing | 1.5 | s | decoded | heart cone 0x10b2d7a0 |
| Unavailable line colour | (128,128,128,128) | RGBA | decoded | sticky menu 0x10b2d1c0 |
| Unselected line colour | (200,200,200,255) | RGBA | decoded | 0x10b2d1c0 |

## Network
- Client only. Everything drawn comes from replicated or owner-side state: the HUD state, the controller state, the
  selected gadget and its menu fields (owner side), `bGasMask` (replicated), `MaskDuration` (owner tick),
  `StateOfDetect` (owner tick), the used object's name fields and `aUsedByPawn` (replicated).

## Edge cases
- The sticky cam fly-in (`s_UseObject_StickyCam_SmoothToPosition`) already shows the sticky page.
- The camera name keeps the last text when a camera has an empty name.
- A merc who selects the gas mask sees the frame at once even with the air flow off.

## Evidence
- local/re-out/uc/SBase/SHud.uc (IsInSticky, IsInCamNetwork, the two states).
- Native HUD page code of `System/SCCT Versus` read in the Bridge research (SCCT_Versus_UE5_Bridge
  docs/overlays/stickycam.md, camnetwork.md, gasmask.md, radar.md, reticle.md): page switch 0x10b2ccec (spy) and
  0x10b2743f (merc), page inits 0x10b34d70 and 0x10b33680, camera name 0x10b337a0, alert 0x10b335c0, bindings
  0x10b34be0 and 0x10b333f0, sticky menu 0x10b2cdef and 0x10b2d1c0, mine menu 0x10b27575 and 0x10b27770, gas mask
  0x10b27ec0, heart cone 0x10b2d7a0.
- `Menus/Hud.mgm` (converted to .content/shared/ui/magma/Hud.json): area and widget names.
- 2026-11-04 stock HUD captures at 640x480, six phases each of scanning and detection:
  `local/heartbeat-av-parity/original-stock-hud/capture.json` and `parsed-transforms.json`. Both arcs are visible.
  Production Godot renders are compared without image registration or color fitting in
  `local/heartbeat-av-parity/stock-visual-comparison.json`.
- The earlier 1280x720 reference used Reloaded's `widescreen_hud` patch. Its origin hook at Core 0x100a9107
  matches the shared widget name `onde2` and moves it to X=-300; the absent arc is not stock scanner behavior.
  Evidence: `local/heartbeat-av-parity/reloaded-hud-wave-override.asm.txt` and `original-draw-trace/parsed-submits.json`.
- Native image geometry/color paths: 0x10baaf10, 0x10a54600, 0x10a54330 and 0x10a525b0; retained in
  `local/heartbeat-av-parity/native-image-geometry.md` and `native-image-color-quantization.asm.txt`.
- Shared HUD init 0x10b318e0 hides both cone instances without stopping their clocks. GetFrame 0x10ba48a0 rounds
  half-up; keyframe actions use continuous elapsed time. Runtime clock and direct-deselection captures are in
  `local/heartbeat-av-parity/rewrite-final/rewrite-driver-timeline.json` and `rewrite-driver-direct-deselect.json`.

## Open questions
- The mine menu's greyed entries: the native reads the launcher's availability array; whether it greys every
  unavailable entry or only empty ones needs one capture.
