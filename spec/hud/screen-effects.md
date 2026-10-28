# HUD screen effects: colour washes, damage feedback and the merc page effects

Units:
- nat:SBase.SHud.AddVisualSfx, nat:SBase.SHud.SfxForceSlot, nat:SBase.SHud.ProcessSFX (hooked by Reloaded, same types
  and timing), nat:SBase.SHud.SetHudPerturbationLength, nat:SBase.SHud.SetHudProjection, nat:SBase.SHud.DrawProjection
- uc:SBase.SHud:HudShutDown, uc:SBase.SHud:StopHudShutDown, uc:SBase.SHud:StopHudPerturbation
- nat:SBase.SPlayerController.ClientDamageHudEffect (HUD part)
- nat:SBase.SHud.DrawBloodSplash (rule 17: decoded, never shown)
- the merc page drivers of P_HudDEF for Perturb_Global, the shutdown, A_StickyShock(2), gouttes, Mousse and Flash

## Purpose

What the HUD draws over the view when a player is hit, flashed, knocked out, shocked, soaked or foamed, and when it
draws it. The simulation tells the HUD through the SHud natives (or, in the rewrite, the CombatFx notifications that
stand for them); this spec is the HUD side.

## Data

SFX slots: eight of each, on SHud: `Sfx_Type` (SFXType), `Sfx_Evolution` (SFXA_Constant 0, SFXA_Linear 1),
`Sfx_CurrentValue`, `Sfx_StartTime`, `Sfx_StartValue`, `Sfx_EndTime`, `Sfx_EndValue`, `Sfx_Duration`,
`Sfx_KillAfterTimeIsOver` (int) and one `Sfx_ForcedSlot` (-1 when unset).

| SFXType | Value | What ProcessSFX does with the current value v |
|---|---|---|
| SFXT_TextureColor | 1 | full-screen black at alpha v (0..255) |
| SFXT_CrossEye | 2 | `PlayerController.CrossEyedFactor = clamp(v, 0, 1)` (render) |
| SFXT_Smoke | 3 | the same factor |
| SFXT_TextureColorRed | 4 | full-screen (90, 0, 0) at alpha v |
| SFXT_TextureColorWhite | 5 | full-screen (128, 128, 128) at alpha v |
| SFXT_FlashEffect | 6 | `PlayerController.RemanenceBlendFactor = v` (render, flash-bang after-image) |
| SFXT_TextureColorSleep | 7 | full-screen black at alpha v; the knock-out screen (rule 8) |

## Rules

The canvas:

0. Everything the SHud canvas and the console draw (washes, markers, canvas texts, console lines) is modulated
   twice: each colour channel doubles, capped at 255, and the alpha is kept (inferred: `SFXT_TextureColorWhite`
   draws 0x808080, and the merc's canvas colour (128, 93, 34) doubles to the merc page colour). So the red wash
   (90, 0, 0) shows as (180, 0, 0) and the grey wash as white.

Adding an effect (`AddVisualSfx(Type, StartValue, EndValue, Duration, Evolution, StartTime, EndTime, bDontKill)`):

1. The slot: the forced slot when `SfxForceSlot(n)` set one (then the forced slot is cleared; an effect replaced
   there stops its render factor: CrossEye always, Smoke unless the new effect is Smoke, Flash); else the first slot
   whose type is None, or a running FlashEffect slot when the new effect is a FlashEffect (so a second flash
   restarts the first). With no free slot nothing happens.
2. `KillAfterTimeIsOver = !bDontKill`. `StartTime` is the given one, or now when 0. The duration is the given one;
   when 0 it is `EndTime - StartTime`, or 5 s when EndTime is 0 too. `EndTime = StartTime + Duration`.
3. Replacing a knock-out slot (old type 7) gives the HUD back: bReticuleOff, bHideHUD, bShutDown, bTimeOff,
   bAMMOOff, bROFOff, bLifeOff and bGadgetOff are cleared.
4. `StartValue = -10000` keeps the slot's current value as the start; otherwise start and end are the given ones.

Each HUD draw (`ProcessSFX`, after the page and the console lines):

5. For every slot with a type: Constant gives `v = StartValue`; Linear gives
   `v = StartValue + (EndValue - StartValue) * (now - StartTime) / (EndTime - StartTime)`; any other evolution clears
   the slot. `v` is clamped between StartValue and EndValue, then applied as in the table (alpha bytes truncate).
6. A slot whose `EndTime <= now` and whose `KillAfterTimeIsOver` is 1 ends: its render factor goes back to 0, its
   type to None and its value to EndValue. Slots with bDontKill hold their end value until replaced.

Damage feedback (`ClientDamageHudEffect(DamageType)` on the hit player's machine):

7. Nothing at all without a HUD (`myHUD`). For every damage type except 7 (extinguisher), 8 (flash-bang) and 12
   (sticky shocker), when the controller has a pawn: `SetHudPerturbationLength(1.5)` (merc only, rule 10; this is the
   real SHud perturbation, so a hit merc cannot reload, switch goggles or scope for 1.5 s) and, for a spy pawn
   (`IsA SPawnAttaque`), a red wash `AddVisualSfx(SFXT_TextureColorRed, 120, 0, 0.5, Linear)`: a flat full-screen
   colour (ProcessSFX draws the canvas's plain tile, no damage texture). Type 8: nothing in thermal, motion or EMF
   goggles (the goggle's `CurrentMode` 36, 37, 38, not `RendMap`: they differ while the goggle clip plays);
   otherwise the after-image capture (controller flag 0x800 at +0x5f0), the flash whistle `Snd_FlashBangSiffle`
   played by the audio device at the controller (volume 1, radius 100, pitch 1; this machine only) and
   `AddVisualSfx(SFXT_FlashEffect, 1, 0, 8, Linear)`; on the merc page the white-out (rule 14). Type 7: motion and
   EMF goggles switch off (the goggle's mode event with 1 in mode 37, 2 in 38: the active mode's key again,
   0x10ab0a9a) and `SetHudProjection(1, 5, 10)` (rule 15). The goggle switch, the whistle and the 1.5 s perturbation
   are game state and run in the simulation's ClientDamageHudEffect; the washes and page effects are the HUD's.

Knock-out screen (type 7 slot):

8. While the pawn is dead, or the controller has `bEndGame`, the slot ends at once (the lying-on-ground timer is
   expired, bReticuleOff and bHideHUD are cleared). Otherwise, while the pawn has a weapon slot, bShutDown is set,
   and bHideHUD too while `v < 255` or now is within `fLyingOnGroundTime + fLyingOnGroundDuration`. At full black
   the text "Sleeping" (`SHud.TextSleeping`) is drawn centred with a number under it: `%i` of
   `trunc(fLyingOnGroundTime + fLyingOnGroundDuration - now)`, not clamped (0x10a9239f).

Merc page effects (P_HudDEF only; spies have none of these areas, and the natives below do nothing for spies):

9. Order of the merc page update: shutdown, then (when no shutdown runs) the foam, and the perturbation only when
   the foam is not active; then droplets, sticky shock, posture icon and flash.
10. `SetHudPerturbationLength(L)` (0x10a92880): `P` becomes the owner's pawn; only when `P.SPawnType` is
    SPAWNDEFENSE: when not perturbed, bPerturbation with start now and length L; when already perturbed, the start
    is kept and the length becomes `now - start + L` (the end moves to now + L, shorter or longer). For a spy it does
    nothing, so a spy's reload, goggles and camera network are never refused by a perturbation.
    `StopHudPerturbation` clears it.
11. Perturbation driver: on its rising edge every top-level instance of the page jumps to frame 60 and plays (frames
    60 and up hold the perturbed keys: `Perturb_Global` loops 60 to 90 every 3 s, each area dissolves through its
    `perturb01` noise mask); `Snd_HudPerturbation` plays. When the length has passed, `StopHudPerturbation`. On the
    falling edge every top-level instance goes back to frame 0, stopped, and the sound stops.
12. Shutdown (`HudShutDown(tToClose, Length)` on a merc: sets bShutDown, adds "System malfunction" to the stack for
    3 s and plays `Snd_HudShutdown`): on its rising edge every top-level instance goes to frame 10 and plays; 12 s after
    the start (truncated to whole seconds; the script lengths are not used) bShutDown clears and `StopHudShutDown`
    adds "Rebooting ..." for 3 s; then for 2 s every instance that is stopped between frames 10 and 40 goes to frame 40
    and plays (the reboot keys).
13. Sticky shock: while the pawn `bStickyShocking`: a head shot (`bStickyShockedInTheHead`) shows A_StickyShock, a
    body shot A_StickyShock2, each from frame 60 when it was hidden (loop 60 to 73, 1.3 s); both hide when the shock
    ends.
14. Flash white-out: on a flash-bang hit (rule 7) `Flash` is shown from frame 0 and played: white, alpha 255 to 0 over
    8 s (80 frames), then stopped; it hides once it has stopped.
15. Foam: `SetHudProjection(splash, stay, clean)` (mercs) sets bProjection (or, during the stay, restarts the stay).
    `DrawProjection` counts 0.1 s steps: after `splash x 10` steps bProjection gives way to bNeigeDisp and
    TimeStopProj = trunc(now); the stay lasts until `TimeStopProj + stay < now`, then `clean x 10` steps end it. The
    counters do not run in motion or EMF vision. `Mousse` is shown from frame 0 while bProjection, held at frame 12
    during the stay, played from 13 (to 100) during the clean, hidden when both flags clear.
16. Droplets: while the pawn's `CurrentRainVolume` is set, `gouttes` (A_gouttelettes, 5 fps) is shown and played
    from 0 when hidden or past frame 50 (it loops itself every 9 s); when it clears, a playing loop caught at frame 45
    or later jumps to 50 (the outro, stops at 59), and a stopped one hides.
17. Blood splash (`DrawBloodSplash(Canvas)`, native 1437, game 0x10af7600, body 0x10af7380), called by the global
    `SHud.PostRender` every frame. It does nothing unless `bSplashBloodStart` is set and `fTimeBloodSplash[0] + 0.16
    <= now`. Then, while `iNbBloodSplash < 5`, splash `i` (from the current count) is added once `i == 0` or
    `fTimeBloodSplash[0] + 0.1 i < now`: its time is now, its place `x = 280 - 60 i + r`, `y = 220 - 40 i - r`
    (`r = rand() % 50 + 10`, 0 for odd `i`), its size `50 + 20 i` (stored, not read by the draw). Each splash
    is a tile from its place with alpha `255 - 150 (now - its time)` (gone below 1), 128 x 128 texels for even
    splashes and 32 x 32 for odd ones, colour grey 0x808080 (the tile's far corner adds a canvas field at +0x2C that
    was not identified); when all five have faded the count, the time and the flag are cleared. Nothing in the game sets `bSplashBloodStart`: no script line writes it, and the only
    writes to that bit field in the game and in Reloaded.Core touch other bits (bProjection in DrawProjection and its
    setter, bShowConnectedPlayers in Reloaded). The overlay never appears, and the rewrite does not draw it.

## Constants

| Name | Value | Unit | Provenance | Source |
|---|---|---|---|---|
| SFX slots | 8 | | decoded | SHud.SFX_NbSlots, ProcessSFX loop |
| Default SFX duration | 5.0 | s | decoded | AddVisualSfx helper game 0x10a92690 |
| Keep-current start value | -10000 | | decoded | game 0x10a92690 |
| Red wash | (90, 0, 0), 120 to 0 over 0.5 s, linear | | decoded | ProcessSFX type 4; ClientDamageHudEffect 0x10ab0990 |
| White wash | (128, 128, 128) | | decoded | ProcessSFX type 5 |
| Damage perturbation | 1.5 | s | decoded | ClientDamageHudEffect |
| Flash after-image | 1 to 0 over 8 s | | decoded | ClientDamageHudEffect type 8 |
| Perturbed frame | 60 | frame | decoded | merc perturbation handler 0x10b2c5f0 |
| Shutdown frames | 10, reboot 40 | frame | decoded | merc shutdown handler 0x10b2c430 |
| Shutdown length | 12 + 2 | s | decoded | 0x10b2c430 (fixed; script lengths ignored) |
| Sticky shock frame | 60 | frame | decoded | merc sticky handler 0x10b281c0 |
| Flash white-out | 80 frames at 10 fps | | decoded | Hud.mgm A_Flash |
| Foam projection | (1, 5, 10) | s | decoded | ClientDamageHudEffect type 7 |
| Foam hold / clean frames | 12 / 13 | frame | decoded | merc foam handler 0x10b282b0 |
| Droplet outro | frames 45, 50 | frame | decoded | merc droplet handler 0x10b28100 |

## Network

Everything here runs on the player's own machine from replicated or client-run calls (`ClientDamage`,
`ClientStickyShock`, `PlayerTick` state); nothing is sent.

## Edge cases

- Starting a perturbation of length 0 shows frame 60 for one update and ends on the next (spawns do this).
- While the foam is up, or during a shutdown, the perturbation does not update: it starts and stops at once later.
- A head shot during a body shock shows both sticky shock areas.
- With no free SFX slot, a new wash is lost (eight concurrent effects).

## Evidence

- Native decode (own Ghidra copy local/ghidra-work/hud): AddVisualSfx exec game 0x10a92940 and helper 0x10a92690,
  SfxForceSlot 0x10a92640, ProcessSFX 0x10a91e90 (stock body; Reloaded replaces it at 0x10023660, same types and
  timing, spec/native/reloaded-hooks.md rule 13). Outputs: local/re-out/c/actions/game/, local/re-out/c/transfer/.
- SCCT Versus UE5 Bridge HUD research (docs/overlays/hudfx.md, flash.md): the merc page handlers 0x10b27190,
  0x10b2c5f0, 0x10b2c430, 0x10b281c0, 0x10b282b0, 0x10b28100, 0x10b28480; ClientDamageHudEffect 0x10ab0990.
- REA recheck 2026-10-26 (Ghidra 12.1.4 through REA, shared session; capstone over the exe for the page handlers
  Ghidra leaves undefined): ClientDamageHudEffect 0x10ab0990 (myHUD gate, whistle, IsA SPawnAttaque, SetHudProjection
  splash/stay/clean in arg/EDX/arg), SetHudPerturbationLength 0x10a92880 (merc gate on P.SPawnType), SetHudProjection
  0x10a92aa0 (merc gate), ProcessSFX 0x10a91e90 (type 4 plain tile, sleep number), perturbation handler 0x10b2c5f0
  (frame 60, Snd_HudPerturbation, end test), health gauge 0x10b30090.
- `local/re-out/uc/SBase/SHud.uc`: HudShutDown, StopHudShutDown, StopHudPerturbation, defaults (TextSleeping).
- `Menus/Hud.mgm` (converted `shared/ui/magma/Hud.json`): the areas' keyframes.

## Open questions

1. Resolved (REA 2026-10-26): the number under "Sleeping" is the lying-on-ground seconds left, truncated (rule 8).
2. The SETMASK dissolve of the perturbed areas cuts per pixel in the original; the page player cuts convex quads.
3. Whether Reloaded's ProcessSFX differs in any type (compare one capture of each).
