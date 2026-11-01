# Merc eye adaptation and the squint

Units:
- nat:SBase.SPlayerController.NativeComputeSquinting

This page records the merc's squint value and its quantized contribution to scene lighting.

## Purpose
Ryan asked for the merc's normal view (RendMap 5, no torch, no vision mode) to get brighter over time while he
stands still, "as in the original". Before porting it, the rule was looked for in the code and measured in the
original game. Neither shows such a rule.

## Rules
1. There is no eye adaptation. The merc's normal view is drawn the same way however long he stands still; its
   brightness depends only on the lights, the torch and the player's Brightness, Contrast and Gamma settings.
2. **The squint** (`NativeComputeSquinting(dt)`, native 1386, decoded game 0x10aafc80). The merc branch of the
   local player's `PlayerTick` calls it every tick, before the smoke cough. It keeps two timers on the controller,
   `DurationStatic` and `DurationForSquinting`, and writes `PlayerController.SquintingFactor`:
   - In the scope (`bBinocularVision`) or without a torch actor (`SPAWNDEFENSE.Torche`): `SquintingFactor = 0`,
     nothing else changes.
   - Torch on (`Torche.bInUse`): `DurationStatic = 0`, `DurationForSquinting -= dt`.
   - Otherwise, with `v` the pawn's speed: when `v < 5` and both `|int(aLookUp)|` and `|int(aTurn)|` are at most 10
     (standing still, not looking around), `DurationStatic += dt`, and once `DurationStatic > 0.5`,
     `DurationForSquinting += dt`. Else `DurationStatic = 0` and: `v >= 80` gives `DurationForSquinting -= dt`;
     a slower move gives `DurationForSquinting -= 0.3 dt`, then raised to 1.2 when below 1.2 (so a slow walk also
     lifts a small value up to 1.2).
   - `DurationForSquinting` is clamped to 0..2; `SquintingFactor = 0.15 D` while `D < 1`, else
     `0.15 + 0.25 (D - 1)` (at most 0.4).
   Its only reader is the scene pre-render (game 0x10a0af70): in `RendMap` 5, when `SquintingFactor` is not 0, the
   render interface's light byte (+0xC) becomes `int(SquintingFactor x 28)`. Night vision sets the same byte to 30
   together with the interface's night flag (+4). `RenderingModeNormal::SetAmbientLight` (game 0x10979380)
   adds the halved byte even without that flag; the diffuse Modulate2X stage restores twice that value.
   The renderer therefore adds `2 * (int(SquintingFactor * 28) >> 1) / 255` in normal vision, reaching `10/255`.
   The earlier whole-frame measurements below did not isolate this small actor-light contribution and are not
   evidence that the byte has no effect. A 2026-10-31 AquaE weapon-draw probe observed zero actor ambient,
   render-interface byte 11 and resulting D3D ambient `0xff050505` (local/viewmodel-light/runtime-evidence.json).


## Evidence
- Script: no SBase, Engine or GUI class reads time standing still, writes a brightness, gamma, contrast or
  exposure value, or changes `RendMap` 5 drawing (searched local/re-out/uc for adapt, lumin, bright, gamma, contrast,
  exposure and the French terms the code uses).
- Native: NativeComputeSquinting game 0x10aafc80 (editor exec 0x111e7280); the pre-render step game 0x10a0af70
  (RendMap 34: +4 = 1 and +0xC = 30; RendMap 5: +0xC = int(SquintingFactor x 28.0, 0x10c10b0c) when not 0).
- Native: `UD3DRenderDevice::UpdateGamma` (game 0x1095dab0) builds the gamma ramp from the client's Brightness and
  Contrast (each clamped to 0..0.8) and Gamma only; it is reached through the client's `UpdateGamma` (vtable), which
  the options menu calls. No string or property of the game or of Reloaded.Core names an exposure, adaptation or
  dark-vision setting for mercs (local/reloaded_strings).
- Measured (sandbox Play Level as the merc on SteeE, headless, frames from the d3d9 frame share, HUD hidden, pawn
  still): mean luminance of the whole frame every second for 45 s and every 2 s for 120 s.

| Place | Start | Range over the run | Shape |
|---|---|---|---|
| dark-room (dark) | 1.33 | 1.33 to 1.42 (of 255) | slow wave of about 70 s, back to 1.33, no trend |
| storage (dark) | 3.13 | 3.07 to 3.16 | same wave, no trend |
| merc-spawn (lit) | 20.56 | 20.52 to 20.56 | flat |

  The small wave rises and falls back while the merc stands still (its source, the idle view model or a light, was
  not isolated); there is no steady brightening. A change made by the fullscreen gamma ramp would not be in these
  frames, but the ramp only follows the settings above.
- Tools: local/merc-kit/eye_capture.py (sandbox capture), results in local/merc-kit/eye1 and local/merc-kit/eye.

## Validation limits
The runtime probe establishes the actor ambient path. The older whole-frame measurements above do not establish
pixel-identical adaptation across every surface/material path.
