# Spy energy (Shocker ammunition and Camo Suit power)

Units:
- nat:SBase.SPawnAttaque.ComputeEnergyLevel, uc:SBase.SPawnAttaque:AuthorizeEnergy, uc:SBase.SBasePawn:PostBeginPlay (energy init),
  uc:SBase.SPlayerController:PlayerTick (energy call)
- uc:SBase.SGadCloak:CanTurnOn, uc:SBase.SGadCloak:CanTurnOff, uc:SBase.SGadCloak:QuickUse, uc:SBase.SGadCloak:ForceDesactivation,
  uc:SBase.SGadCloak:Load, uc:SBase.SGadCloak:Use, uc:SBase.SPlayerController:CanActivateCloak (energy-related parts)
- rep: none (EnergyLevel is not replicated)
- def:SBase.SPawnAttaque, def:SBase.SGadCloak

## Purpose
One energy bar per spy, spent by Shocker shots and drained by the Camo Suit. OpenSCCT refills it continuously;
the decoded original used fixed steps once per second. It is computed only on the spy's own machine.

## Data
| Field | Type | Default | Notes |
|---|---|---|---|
| SPawnAttaque.EnergyLevelMax | int | 100 | |
| SPawnAttaque.EnergyLevel | float | 0, set to EnergyLevelMax in SBasePawn.PostBeginPlay | |
| SPawnAttaque.EnergyRefillAmount | int | 7 | energy per second; whole-second steps in the original |
| SPawnAttaque.EnergyRefill | float | 0 | original accumulated idle seconds; not used by OpenSCCT refill |
| SBasePawn.GlassEffectEnergyUsePerTick | float | 8.0 | despite the name, energy per second |
| SBasePawn.GlassEffect / DesiredGlassEffect | byte | 0 | cloak amount and target |
| SBasePawn.PerturbationStartTime / PerturbationDuration | float | 0 | cloak flicker window |
| SStickyShocker.EnergyNeeded | int | 15 | per shot |
| SGadCloak.Charge / RefireRate | int / float | -10 / 0.1 s | not consumed |

`SPawnAttaque.NbPointsRefillable`, `NbPointsRefilledPerSecond` (4), `HealthRefill`, `MinTimeHealthRefill` (3 s)
belong to spy health regeneration in the native `TickNative`, not to energy (combat spec).

## Current OpenSCCT behavior

- **Activation:** every spy owns cloak independently of inventory. Any `EnergyLevel > 0` is sufficient; there is
  no percentage threshold, activation cost or replacement cooldown. Health, shock, rain, weapon/transit, speed
  and controller-state restrictions still apply.
- **Drain:** while `DesiredGlassEffect != 0`, spend 8 energy/s continuously, including fade-in and active cloak
  flicker. A zero glass sample during active flicker never pauses spending. Switching cloak off stops drain
  immediately; the battery holds steady through the remaining fade-out and flicker (`SpyCloak.IsEngaged`),
  with neither drain nor recharge. Clamp depletion to zero and request the existing forced decloak; energy
  cannot become negative.
- **Recovery:** refill continuously at 7 energy/s, clamped to `EnergyLevelMax`, only when the cloak target and
  visible glass are both zero and the perturbation window has finished. No refill occurs during a fade or flicker.
- **Shared pool:** Shocker shots still spend 15 energy. A shot no longer locks cloak until the bar is full again;
  any remaining positive energy can power it after the normal weapon/state restrictions clear.
- **Controls and presentation:** Hold/Toggle still acts immediately on press, with local network prediction and
  the existing visual fades, movement limits and damage reveal. A forced decloak while Hold remains pressed does
  not automatically reactivate; release and press again.
- **HUD:** the existing energy gauge appears below full charge and while cloak is engaged, even at full charge.
  At 20% energy or less it is amber; it adds no status text. HUD-editor visibility and opacity still apply.

These are intentional changes to the decoded original. The numbered rules, constants and original edge cases
below preserve the original evidence; their full-bar gate, quantised refill and flicker pause are not current
OpenSCCT behavior.

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
This table records the decoded original. OpenSCCT keeps the maximum, drain, shot cost and speed limit, uses
7 energy/s continuous recovery (about 14.29 s from empty once fully decloaked), and has no button hold threshold.

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
- The original HUD gauge reads the local value and is shown only while `EnergyLevel < EnergyLevelMax`.
  OpenSCCT also shows it while cloak is engaged and applies the low-energy tint described above.
- The rewrite should keep the bar client-side for fidelity and may add a server shadow as an anti-cheat option.
- Cloak (rep-pawn.md blocks 6 and 7): `SetDesiredGlassValue` and `SetPerturbationGlass` are client to server;
  in the decoded original, on the spy's own client they are sent and do not run there. The server runs them and broadcasts
  `SetDesiredGlassValueOnClients` and `SetPerturbationGlassOnClients` (broadcast flag, net-replication.md rule 20)
  to every client with the pawn's channel open, which runs the ramp and sound; a listen server runs them too. Port
  addition: a dedicated server also takes the ramp and the flicker window (no sound), since the original skips the
  call there and so kept the target at 255 after a damage decloak, refusing the next cloak.
- **Port addition: immediate local transition.** The owning client starts the same ramp, flicker window and
  sounds when it requests a new cloak target, including reversing a fade when Hold is released or Toggle is
  pressed again. It does not wait for the network round trip. Each request uses `CombatCall.Int2` as a correlation
  ID, echoed on both broadcasts caused by that request. The owner acknowledges its predicted transition without
  restarting the fade, flicker or sounds; other clients run those effects normally. Uncorrelated damage flicker
  and forced decloaks still apply. An already-reached target is acknowledged to its requesting owner without
  restarting the transition for observers. This is a responsiveness change to the decoded behavior, not a new
  body animation: the original cloak scripts animate the glass effect and do not request a skeletal activation clip.
- **Port addition: observer initialization and ticks.** The original did not replicate `GlassEffect`, leaving a
  newly opened pawn channel opaque until the next toggle. The port sends a silent `InitialCloakState` through the
  existing reliable combat stream once the observer acknowledges the pawn channel. It carries the current amount,
  target, and remaining fade/flicker durations; the receiver rebases those durations to its own clock. Network pose
  playback also advances the spy's local glass tick instead of skipping it with the normal script tick. Neither
  observer path controls cloak input or spends energy.
- **Port addition: detection during fade-out.** A cloak target, nonzero glass amount, or unfinished fade-out
  perturbation keeps the spy fully EMF-visible. The owner updates its local EMF flags immediately and sends the
  normal EMF state to the server; `bEMFIsOn` and `EMFOpacity` then replicate to observers. Once fade-out completes,
  the owner rechecks other detection sources before clearing EMF. Cloak does not change heat intensity or opacity,
  so the spy stays visible in thermal vision throughout.

## Edge cases
The following describe the decoded original; the current OpenSCCT overrides are listed above.

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
  strict, and a controller on its controlling machine is required (rule 2). The port originally restored these
  rules after an incorrect clamp and multi-step refill; continuous recovery and clamping are now intentional
  OpenSCCT changes documented above.
- SPawnAttaque.TickNative and energy or cloak timing. Resolved: TickNative runs only the screen glass effect (the cloak
  ramp): cleared on rain, sticky shock or speed above 240 (not in root motion), GlassEffect moving by the slope
  `int((TimeSeconds + 0.1) SlopeGlassEffect + BGlassEffect)`, jitter during a perturbation; Reloaded only adds the
  switch that ignores the speed condition and clamps the jitter. Energy is not touched (Enhanced camo changes:
  spec/enhanced/FEATURES.md enh:rules.camo). Evidence: natives2-damage.md rule 9, reloaded-hooks.md rule 11
  (ANSWERS-natives2 26).
