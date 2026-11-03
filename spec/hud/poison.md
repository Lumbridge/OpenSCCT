# Poison HUD: health frame and screen overlay

The health-gauge driver owns both the poison gauge animation and its screen overlay. The effect comes from
`Hud.mgm`'s `jauge_poison` area, instantiated as `A_Jauge/poison`; it is not a new shader or a plain green wash.
Damage and cure rules are in [health.md](../combat/health.md).

## Native activation

The health-gauge driver at game address `0x10b30090` tests the pawn's `bPoisonned` bit (`+0xae4`, mask `4`).
When poison becomes active and the area is hidden, it shows the area, seeks to one frame, and starts playback.
The seek argument is the area's frame interval in milliseconds (`area + 0x38`) multiplied by `0.001`; at the
authored 10 fps this is 0.1 seconds, or frame 1. When poison clears, the area is hidden.

Starting at frame 0 is incorrect: `jauge_poison` has a stop action there. Frame 10 has a goto-frame-1 action,
giving the active animation a 0.9-second loop. Keep this clock running while poison remains active; do not
restart it on every draw. A later infection starts again at frame 1.

## Authored appearance

`jauge_poison` contains the pulsing gauge frame, its side gradients, and two screen-sized image widgets using
material 59, `stickshock` (`Textures/stickshock.dds`, a 64-by-64 texture). Preserve their widget order, texture,
blend modes, keyframes and inherited area tint.

| Widget | Blend | Packed ABGR / RGBA | Authored rectangle, relative to the health area |
|---|---|---|---|
| `effet01` | Normal | `0x808AB996` / (150, 185, 138, 128) | frame 0: (-568, -249, 72, 231); frame 10: (-665, -320, 135, 280), linear |
| `effet02_dodge` | Dodge | `0xFF2E3E32` / (50, 62, 46, 255) | (-571, -249, 69, 231), constant |

The spy page's `Vie` instance is at (566, 230), tinted (150, 185, 138, 255). The two image rectangles therefore
cover the view, with the authored edge offsets and expanding first layer. Their normal and Dodge blends use the
existing Magma blending rules; a uniform color replacement would lose the pattern, expansion and brightening.

The package also contains `A_Effet_poison` (area 130), but no instance in the package references it. It is not the
native driver's target and has different animation keys; use the active `jauge_poison` source instead.

## Widescreen and editable HUD

The two screen-image widgets map with the full-screen stretch transform. Their bounds do not determine the
health gauge's side anchor. Health HUD position, scale, opacity and visibility edits apply to the gauge and its
poison frame, but not to these two screen images. Keep the original page-relative offsets and parent tint when
mapping the images. The animation is driven from the same replicated poison flag as the gauge.

These viewport and editor rules adapt the authored effect to the rewrite's resizable HUD. The authored colors,
texture, loop and activation frame remain native evidence.

## Evidence

- `local/re-out/c/hudpar/game/10b30090_FUN_10b30090.c`, lines 84-92, and `local/re-out/c/hudpar/gauge.asm`,
  addresses `0x10b30344` through `0x10b303a8`: poison flag, show/hide, one-frame seek, playback.
- `local/re-out/c/actions/game/10ba4a20_FUN_10ba4a20.c`: area time setter; `10ba49e0_FUN_10ba49e0.c`: playback
  setter. The native constructor resolves `jauge_poison` in `10b318e0_FUN_10b318e0.c`, lines 29-30.
- Converted original `shared/ui/magma/Hud.json`: `A_Jauge`, `jauge_poison` (area 43), `A_Effet_poison` (area 130),
  material 59, and `P_HudATT/Vie`. This is generated local content, not a new redistributed asset.
