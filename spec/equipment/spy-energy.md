# Spy energy (Shocker ammunition and Camo Suit power)

Units:
- nat:SBase.SPawnAttaque.ComputeEnergyLevel, uc:SBase.SPawnAttaque:AuthorizeEnergy, uc:SBase.SBasePawn:PostBeginPlay (energy init),
  uc:SBase.SPlayerController:PlayerTick (energy call)
- uc:SBase.SGadCloak:CanTurnOn, uc:SBase.SGadCloak:CanTurnOff, uc:SBase.SGadCloak:QuickUse, uc:SBase.SGadCloak:ForceDesactivation,
  uc:SBase.SGadCloak:Load, uc:SBase.SGadCloak:Use, uc:SBase.SPlayerController:CanActivateCloak (energy-related parts)
- rep: none (EnergyLevel is not replicated)
- def:SBase.SPawnAttaque, def:SBase.SGadCloak

## Purpose
One energy bar per spy, spent by Shocker shots and drained by the Camo Suit, refilled in fixed steps once per
second. It is computed only on the spy's own machine.

## Data
| Field | Type | Default | Notes |
|---|---|---|---|
| SPawnAttaque.EnergyLevelMax | int | 100 | |
| SPawnAttaque.EnergyLevel | float | 0, set to EnergyLevelMax in SBasePawn.PostBeginPlay | |
| SPawnAttaque.EnergyRefillAmount | int | 7 | energy added per whole second |
| SPawnAttaque.EnergyRefill | float | 0 | accumulated seconds |
| SBasePawn.GlassEffectEnergyUsePerTick | float | 8.0 | despite the name, energy per second |
| SBasePawn.GlassEffect / DesiredGlassEffect | byte | 0 | cloak amount and target |
| SBasePawn.PerturbationStartTime / PerturbationDuration | float | 0 | cloak flicker window |
| SStickyShocker.EnergyNeeded | int | 15 | per shot |
| SGadCloak.Charge / RefireRate | int / float | -10 / 0.1 s | not consumed |

`SPawnAttaque.NbPointsRefillable`, `NbPointsRefilledPerSecond` (4), `HealthRefill`, `MinTimeHealthRefill` (3 s)
belong to spy health regeneration in the native `TickNative`, not to energy (combat spec).

## Rules
1. **Init.** `SBasePawn.PostBeginPlay` (every machine): for a spy, `NbPointsRefillable = 0` and
   `EnergyLevel = EnergyLevelMax` (a respawned spy starts full).
2. **Tick.** In the global `PlayerTick` (the local player's machine only), for a spy, unless the (absent in 4.0)
   jammer gadget is in use: `ComputeEnergyLevel(DeltaTime)`. Native behaviour (game 0x10af5fa0, read from the
   disassembly 2026-10-26): nothing without a `Controller` or when the controller's `RemoteRole` is
   AutonomousProxy (so it runs on the controlling machine), and nothing until the cloak flicker window is over
   (`now - PerturbationStartTime > PerturbationDuration`, strictly):
   - if `GlassEffect != 0` (cloak on or fading): `EnergyLevel -= dt * GlassEffectEnergyUsePerTick`; when it reaches
     0 or below, `EnergyLevel = 0` and `SetDesiredGlassValue(0, 1.0)` (forced decloak over 1 s);
   - else: `EnergyRefill += dt`; when `int(EnergyRefill)` went up this call and `EnergyLevel < EnergyLevelMax`,
     `EnergyLevel += EnergyRefillAmount` once, with no clamp: a bar at 95 becomes 102. `EnergyRefill` is never
     reset (it only grows while the cloak is off).
3. **Spending.** `AuthorizeEnergy(Amount)`: if `EnergyLevel < Amount` return false; else
   `EnergyLevel -= Amount` and return true. Called by `SStickyShocker.Fire` with 15 (`spy-shocker.md` Rule 8).
4. **Cloak gate** (`SGadCloak.CanTurnOn`): refused if any of: `bStickyShocking`; `now - LastUsed < RefireRate`;
   in a rain volume; weapon drawn (`WeaponLoaded != 0`); speed above 240 uu/s; `EnergyLevel < EnergyLevelMax`
   (a full bar is required); `!SPlayerController.CanActivateCloak()` (true globally, false in a few states).
   `CanTurnOff` = `SlopeGlassEffect > 0 && GlassEffect != 0`.
5. **Cloak toggle** (`QuickUse`, from the hold-breath / right button when the cloak is selected, Rule 6): ignore
   within 0.5 s of `LastUsed`; if `CanTurnOff`, `SetDesiredGlassValue(0)`; else if `CanTurnOn`, `LastUsed = now` and
   `SetDesiredGlassValue(255)`; else HUD message "Camo. Suit unavailable" for 2 s. `Load()` shows "Press
   \SECONDARYFIRE to activate the suit." for 3 s. `Use()` does nothing. `ForceDesactivation()` turns it off if on.
6. **Cloak button** (PlayerTick, spy, cloak selected, on the client): on press store `fLastPressedUseButton = now`.
   On release after more than 0.2 s: `QuickUse()`. On a shorter release: if it can turn off, `QuickUse()`; else
   select gadget slot 1 (`SelectGagdet(1)`) and `QuickUse()` if that is the cloak, otherwise `GadgetQuickChoice(1)`.
   While held, once more than 0.2 s has passed and `CanTurnOn`, `QuickUse()` fires without waiting for release.
7. **Cloak ramp** (`SBasePawn.SetDesiredGlassValue`, decoded from script). `SetDesiredGlassValue(D, T)` does
   nothing when `D == DesiredGlassEffect`; `T == 0` means `PerturbationDefaultValue` (1 s). It sets
   `DesiredGlassEffect = D`, `SlopeGlassEffect = (D - GlassEffect) / T` (signed, per second) and
   `BGlassEffect = GlassEffect - now x SlopeGlassEffect`, then `SetPerturbationGlass(T)` and, on the server,
   `SetDesiredGlassValueOnClients(D, T)` (the same ramp without the same-target test, the sound Cape_InOut at
   radius 300 and, on the controlling machine, `TestAndSetEMFVisible`). `SetPerturbationGlass(T, V)` does nothing
   while `BGlassEffect == 0`; else `PerturbationStartTime = now`, `PerturbationDuration = T` (0: default),
   `PerturbationValue = V` (0: 12.8), and the client half plays RA_Cape_damage (radius 300). So `CanTurnOff`
   (slope above 0) is true while cloaking or cloaked and false during an uncloak ramp.
7a. **Glass tick** (`SPawnAttaque.TickNative`, game 0x10af5cf0, Reloaded body 0x100caae0). On the controlling
   machine (the controller's RemoteRole is not AutonomousProxy) with a cloak target: in a rain volume, while
   `bStickyShocking`, or faster than 240 uu/s outside PHYS_RootMotion, `SetDesiredGlassValue(0, 1.0)` then
   `TestAndSetEMFVisible`. Then, while `GlassEffect != DesiredGlassEffect`, `g = (now + 0.1) x Slope + B`;
   `GlassEffect = DesiredGlassEffect` when falling and `g` is below the target or when `g` leaves [0, 255], else
   `int(g)`. During a perturbation window, while `cos(PerturbationValue x now) > 0`: range
   `n = int(510 (Start + Duration - now) / Duration)` kept in [10, 510], `r = rand() % n - n / 2`, `v = GlassEffect + r`
   (a negative result becomes 1, above 255 becomes 254; the stock game does this as well, checked 2026-10-26: only the
   lower bound is tested when `r <= 0`, only the upper one when `r > 0`), and a value equal to the target becomes
   125. The window test is strict (`now - Start < Duration`), the ramp value is truncated (`_ftol`), and while rising
   the value is not stopped at a target below 255 (only by leaving [0, 255]); targets are 0 or 255 in practice.
   Reloaded has a switch (DAT_1036abc0) that ignores the speed rule; the map setup 0x10017d70 clears it and no
   setter was found, so the port applies the speed rule. It runs on every machine (each draws the amount it ramps).
7b. **Drawing.** GlassEffect / 255 blends the scene behind the spy over its shading, in normal vision only; every
   goggle mode draws it uncloaked (docs/RENDERING.md#cloak).

## Constants
| Name | Value | Unit | Provenance | Source |
|---|---|---|---|---|
| Max energy | 100 | | decoded | def EnergyLevelMax |
| Refill | 7 per whole second | energy | decoded (native, prior) | def EnergyRefillAmount, Bridge docs/overlays/cloak.md |
| Cloak drain | 8 | energy/s | decoded (native, prior) | def GlassEffectEnergyUsePerTick |
| Shot cost | 15 | energy | decoded | def EnergyNeeded |
| Full bar cloak time | 12.5 | s | derived | 100 / 8 |
| Empty to full | 15 refill steps, about 15 s | s | derived | ceil(100 / 7) |
| Shots from a full bar | 6 (10 left) | | derived | 100 / 15 |
| Sustained fire | about 1 shot per 2.14 s | | derived | 15 / 7 |
| Cloak speed limit | 240 | uu/s | decoded | CanTurnOn |
| Button hold threshold | 0.2 | s | decoded | PlayerTick |

## Network
- Owning client only: EnergyLevel, EnergyRefill, ComputeEnergyLevel and AuthorizeEnergy all run where the spy is
  locally controlled (PlayerTick is a local-player event; AuthorizeEnergy is a plain function called from the
  client's Fire). The server neither tracks nor checks energy, so Shocker shots are client-authorised.
- The HUD gauge reads the local value and is shown only while `EnergyLevel < EnergyLevelMax`.
- The rewrite should keep the bar client-side for fidelity and may add a server shadow as an anti-cheat option.
- Cloak (rep-pawn.md blocks 6 and 7): `SetDesiredGlassValue` and `SetPerturbationGlass` are client to server;
  on the spy's own client they are sent and do not run there. The server runs them and broadcasts
  `SetDesiredGlassValueOnClients` and `SetPerturbationGlassOnClients` (broadcast flag, net-replication.md rule 20)
  to every client with the pawn's channel open, which runs the ramp and sound; a listen server runs them too. Port
  addition: a dedicated server also takes the ramp and the flicker window (no sound), since the original skips the
  call there and so kept the target at 255 after a damage decloak, refusing the next cloak.
- GlassEffect itself is not replicated: a client that opens the spy's channel while it is cloaked draws it opaque
  until the next toggle (as the original).

## Edge cases
- Firing at any time blocks cloaking until the bar is full again.
- Refill is quantised: a shot at 99.9 energy waits for the next whole second of accumulated idle time.
- `EnergyLevel` is not clamped after a +7 step: from 93.x or more the bar ends above 100 (up to 106.x). The HUD hides
  the gauge and the cloak gate passes at 100 or more, so the overshoot is invisible.
- Energy keeps refilling while the Shocker is drawn or fired; only an active or fading cloak stops it.
- During the cloak flicker window (1 s after each toggle or hit) neither drain nor refill runs.

## Evidence
- local/re-out/uc/SBase/SPawnAttaque.uc, SGadCloak.uc, SBasePawn.uc 1393-1400, SPlayerController.uc 3296-3343 and 3425-3428.
- local/reflect/game.json: SPawnAttaque.ComputeEnergyLevel 0x10af5fa0 (native 1415, stock game image);
  SPawnAttaque.TickNative 0x64773710 (Reloaded.Core.dll rva 0x23710).
- Bridge docs/overlays/cloak.md section 2 (native decode), docs/HUD.md (energy gauge).

## Open questions
- None open for the cloak network and drawing (resolved 2026-10-27, Network and rule 7b).

### Resolved
- ComputeEnergyLevel (2026-10-26, REA session; Ghidra defines no function at 0x10af5fa0, so read from the
  disassembly): no clamp at `EnergyLevelMax`, one step per call, `EnergyRefill` never reset, the flicker test is
  strict, and a controller on its controlling machine is required (rule 2). The port clamped and added a step per
  whole second crossed; fixed.
- SPawnAttaque.TickNative and energy or cloak timing. Resolved: TickNative runs only the screen glass effect (the cloak
  ramp): cleared on rain, sticky shock or speed above 240 (not in root motion), GlassEffect moving by the slope
  `int((TimeSeconds + 0.1) SlopeGlassEffect + BGlassEffect)`, jitter during a perturbation; Reloaded only adds the
  switch that ignores the speed condition and clamps the jitter. Energy is not touched (Enhanced camo changes:
  spec/enhanced/FEATURES.md enh:rules.camo). Evidence: natives2-damage.md rule 9, reloaded-hooks.md rule 11
  (ANSWERS-natives2 26).
