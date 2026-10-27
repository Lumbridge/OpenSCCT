# Merc eye adaptation (asked for; not in the original)

Units: none. This page records why the rewrite has no merc eye adaptation.

## Purpose
Ryan asked for the merc's normal view (RendMap 5, no torch, no vision mode) to get brighter over time while he
stands still, "as in the original". Before porting it, the rule was looked for in the code and measured in the
original game. Neither shows such a rule.

## Rules
1. There is no eye adaptation. The merc's normal view is drawn the same way however long he stands still; its
   brightness depends only on the lights, the torch and the player's Brightness, Contrast and Gamma settings.

## Evidence
- Script: no SBase, Engine or GUI class reads time standing still, writes a brightness, gamma, contrast or
  exposure value, or changes `RendMap` 5 drawing (searched local/re-out/uc for adapt, lumin, bright, gamma, contrast,
  exposure and the French terms the code uses).
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

## Open questions
- If Ryan remembers a specific map, mode or setting (for example a Reloaded option, a fullscreen run, or his own eyes
  getting used to a dark screen), capture that case the same way before porting anything.
