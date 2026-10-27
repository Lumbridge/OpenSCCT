# Gas mask (SGadGasMask)

Units:
- def:SBase.SGadGasMask, uc:SBase.SGadGasMask:Use, uc:SBase.SGadGasMask:SetAmbientSoundOnServer, uc:SBase.SGadGasMask:Activate,
  uc:SBase.SGadGasMask:Load, uc:SBase.SGadGasMask:Unload, uc:SBase.SGadGasMask:Tick, uc:SBase.SGadGasMask:Destroyed,
  uc:SBase.SGadGasMask:PostBeginPlay, rep:SBase.SGadGasMask.SetAmbientSoundOnServer
- uc:SBase.SBasePawn:SetGasMaskUseOnServer, rep:SBase.SBasePawn.bGasMask, rep:SBase.SBasePawn.SetGasMaskUseOnServer
- The smoke protection lives in the emitters' native ticks: ASSmokeEmitter::Tick (game 0x10a9a130) and the sticky
  cam gas scan (0x10aa31f0), see `smoke-grenade.md` and `sticky-cam.md`.

## Purpose
The merc's gas mask gives 30 s of air in total. While it is on, smoke clouds and the sticky cam gas do not reach him.

## Data
| Field | Default | Notes |
|---|---|---|
| Charge / RefireRate | -10 / 0.1 s | not spent |
| MaskDuration | 30 s | the air left, never refilled |
| Sounds | activation SE_masqGaz_in, empty SE_masqGaz_vide, breathing RA_breath_masqGaz, choking ono_EtouffMaskGaz1 | |
| TextCanUseGasMask | "Gas mask selected, press \SECONDARYFIRE to activate it" | localized |
| DummyGasMask | an SGasMask attached to the head bone | visual |
| SBasePawn.bGasMask | false | replicated |

## Rules
1. **Use** (Alt-fire): with the mask on: the activation sound (slot Misc, radius 100) and `Activate(false)`. Off:
   refused within `RefireRate` of `LastUsed`; with air left the activation sound, `LastUsed = now`,
   `Activate(true)`; with none the empty sound. Then the base Use.
2. **Activate(on)**: `bGasMask` on the pawn here and on the server (`SetGasMaskUseOnServer`). On: under 7.5 s of air
   the controller plays the choking loop and the pawn's ambient sound is set to choking on the server
   (`SetAmbientSoundOnServer(pawn, 2)`), else the breathing loop (1). Off: the controller's breathing or choking loop
   stops and the server clears the pawn's (0).
3. **SetAmbientSoundOnServer(pawn, b)** (client to server): 1 breathing, 2 choking (the old sound stopped first), 0
   clears a breathing or choking ambient sound.
4. **Tick** (every machine with the gadget): while the mask is on, `MaskDuration -= dt` (the server adds the time to
   `Merc_GasMaskTimeUsed`); at 0 the air is clamped to 0 and the mask comes off; under 7.5 s while the pawn's ambient
   sound is not the choking one, off and on again (which switches to the choking loop).
5. **Load**: the selection hint (2 s). **Unload**: the mask comes off (selecting another gadget takes it off).
6. **PostBeginPlay**: based on the owner; on the server a visual mask is spawned and attached to the head bone.
   **Destroyed** removes it.
7. **Protection**: the smoke emitter skips a local player whose pawn wears the mask (`bGasMask`), so no cough, blur
   or faint; the sticky cam gas skips him too. Mercs knocked down by other means use the mask once more on entering
   the lying state (`knockout-and-terminate.md`): the mask comes off.

## States
None.

## Constants
| Name | Value | Unit | Provenance | Source |
|---|---|---|---|---|
| Air | 30 | s | decoded | MaskDuration |
| Choking below | 7.5 | s | decoded | Activate, Tick |
| Sound radius | 100 | uu | decoded | Use |

## Network
- The client decides; the server learns `bGasMask` (reliable) and sets the pawn's ambient sound, which replicates.
  Each machine runs its own countdown in `Tick`.
- Who hears what: the Use sounds (2D waves) and the controller's loop play on the owner's machine only. The pawn's
  replicated ambient sound is heard by everyone near the merc as a 3D loop of radius 60 (the Pawn's SoundRadius;
  audible to 100 x 60 uu, spec/native/audio.md rules 11 and 20), although the breathing random's wave is 2D. A bot
  (server world, no machine of its own) plays its Use sounds nowhere.

## Edge cases
- The air is not refilled by the ammo crate's full reload (it restores `Charge`, not `MaskDuration`).
- Death unloads the selected gadget, so a dying merc's mask comes off.

## Evidence
- local/re-out/uc/SBase/SGadGasMask.uc, SBasePawn.uc 1268, SPlayerController.uc 19302; game 0x10a9a130 and
  0x10aa31f0 read `bGasMask` (bit 0x2 at 0xae4, private Ghidra copy).
