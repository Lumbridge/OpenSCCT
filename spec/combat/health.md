# Health: values, spy regeneration, poison, healing

Units:
- def:Engine.Pawn (Health)
- rep:Engine.Pawn.Health
- nat:SBase.SPawnAttaque.TickNative
- uc:SBase.SPawnAttaque:Tick
- def:SBase.SPawnAttaque (MinTimeHealthRefill, NbPointsRefilledPerSecond, EnergyLevelMax, EnergyRefillAmount, blood drops)
- rep:SBase.SPawnAttaque.NbPointsRefillable
- rep:SBase.SPawnAttaque.BloodDropStartTime
- uc:SBase.SBasePawn:PostBeginPlay (refill pool reset)
- nat:SBase.SBasePawn.NativeTick (poison drain, expected)
- uc:SBase.SBasePawn:Tick (calls NativeTick)
- rep:SBase.SBasePawn.bPoisonned
- rep:SBase.SBasePawn.PoisonnedBy
- def:SBase.SBasePawn (fPoisonned_HealthLostPerSecond)
- uc:SBase.SPlayerController:CountPoisonDrain
- uc:SBase.SInfirmary:Use
- uc:SBase.SHealMateInteraction:InitInteract
- uc:SBase.SHud:PostRender (health percentage part, reference only)

## Purpose
Both teams have 100 hit points. Spies regain part of the damage they take after a short pause (merc bullets are 70 %
regenerable, drops and grab damage are not). Poison drains health over time. Infirmaries and the team heal restore
full health. Mercs never regenerate.

## Data
| Field | Owner | Type | Default | Provenance |
|---|---|---|---|---|
| Health | Pawn | int | 100 | decoded (def, effective; config flag, no ini override) |
| NbPointsRefillable | SPawnAttaque | int | 0 | decoded; replicated reliable from server |
| HealthRefill | SPawnAttaque | float | 0 | decoded; reset to 0 on every damage |
| MinTimeHealthRefill | SPawnAttaque | float | 3.0 | decoded |
| NbPointsRefilledPerSecond | SPawnAttaque | int | 4 | decoded |
| EnergyLevel / EnergyLevelMax / EnergyRefillAmount | SPawnAttaque | float / int / int | 100 at spawn / 100 / 7 | decoded (shocker energy, gadget spec) |
| BloodDropStartTime, BloodDropPerSecond, BloodDropDuration, MinBloodDropPerSecond, BloodDropDecreaseDuration | SPawnAttaque | float | -, 2.0, 10.0, 0.2, 8.0 | decoded (cosmetic blood trail after a merc bullet hit) |
| bPoisonned | SBasePawn | bool | false | decoded; replicated reliable |
| PoisonnedBy | SBasePawn | SBasePawn | None | decoded; replicated; not written by script |
| fPoisonned_HealthLostPerSecond | SBasePawn | float | 6.0 | decoded (config) |
| PercentDamageRefillable | per damage call | int | weapons 70, frag 30, mines 30, grenades 50, charge 100, berserk 0, drops 0, taser 0 | decoded |

## Rules
1. Spawn: `SBasePawn.PostBeginPlay` sets a spy's `NbPointsRefillable = 0` and `EnergyLevel = EnergyLevelMax`.
2. Every damage on a spy (`damage-pipeline.md` rule 28): `HealthRefill = 0` and
   `NbPointsRefillable += int(float(ActualDamage * PercentDamageRefillable) * 0.01)`.
3. Regeneration is native, in `SBasePawn.NativeTick`, not in TickNative (decoded, natives2-damage.md rules 8, 9;
   `TickNative` ignores its argument in stock and in Reloaded and only runs the glass effect). Authority only: for an
   SPawnAttaque with `0 < Health < 100` that is not poisoned: moving (`|Velocity|^2 >= 0.01`) or bLyingOnGround resets
   HealthRefill to 0; otherwise HealthRefill += dt, and once it exceeds MinTimeHealthRefill (3 s), each time
   `int(HealthRefill)` grows (once per whole second) `n = min(NbPointsRefilledPerSecond (4), NbPointsRefillable)`
   moves from the pool to Health, capped at the literal 100, and BloodDropStartTime = 0.
4. Poison: SRTD sets `bPoisonned = true` on any SDamage_Poison hit. The drain is native, in `SBasePawn.NativeTick`
   (decoded, natives2-damage.md rule 8): `L = dt x fPoisonned_HealthLostPerSecond` (6 hp/s); while
   `Health >= int(bHealthDecrease - 2 L)`, bHealthDecrease -= L and Health = int(bHealthDecrease); at Health <= 0
   bPoisonned is cleared, Health = 1 and the controller gets `PawnTakeDamage(1500, PoisonnedBy or Instigator,
   Location, (0, 0, -1), SDTBase, 3, 0)`, so the poison kills with that credit. Other damage lowering Health resets
   bHealthDecrease to it.
5. `CountPoisonDrain()` (stats only, called by SRTD before applying damage and by the heals): Drained =
   PoisonLastHealth - max(Health, 0); if > 0, victim DamageTaken and PoisonInstigator DamageGiven grow by it;
   PoisonLastHealth = Health; clear PoisonInstigator when not poisoned or dead.
6. Infirmary (`SInfirmary.Use(PC)`, ReUseTime 0 so always usable): if Health < 100, `Stats.MedkitsUsed += 1`;
   `CountPoisonDrain()`; cure poison; `Health = 100`; a spy's NbPointsRefillable = 0; heal sound (radius 100).
7. Team heal (`SHealMateInteraction.InitInteract`): if the mate is alive: `CountPoisonDrain()` on his controller,
   `Health = 100`, cure poison (the refill pool is not cleared).
8. Health display (HUD spec): PercentHealth = `100 * Health / default.Health` clamped at 0; the "previous health" bar
   falls toward it at 40 % per second.

## States
Not applicable.

## Constants
| Name | Value | Unit | Provenance | Source |
|---|---|---|---|---|
| max health | 100 | hp | decoded | def:Engine.Pawn |
| regen delay | 3.0 | s | decoded | MinTimeHealthRefill |
| regen rate | 4 | hp/s | decoded | NbPointsRefilledPerSecond |
| bullet regenerable share | 70 | % | decoded | SWeapon.PercentDamageRefillable |
| poison drain | 6 | hp/s | decoded | fPoisonned_HealthLostPerSecond |
| HUD health bar decay | 40 | % per s | decoded | SHud |

## Network
- Health replicates from the server (Pawn replication). NbPointsRefillable and bPoisonned replicate reliably from the
  server; HealthRefill does not.
- `TickNative` and `NativeTick` run on all machines; the server's result is authoritative through Health
  replication.

## Edge cases
- Grab damage (the 51 on a merc) and direct `Health` writes do not go through SRTD and never add to a refill pool
  (mercs have none anyway).
- A spy hit by a 70 % weapon for 25 regains at most 17 (int(25 * 70 * 0.01)).

## Evidence
- local/re-out/uc/SBase/{SPawnAttaque,SBasePawn,SInfirmary,SHealMateInteraction,SHud}.uc, SPlayerController.uc
  (CountPoisonDrain sha1 8a770b7f4a3e, SRTD).
- Natives: SPawnAttaque.TickNative (native 1414; live pointer Reloaded.Core.dll+0x23710, local/reflect/game.json),
  SBasePawn.NativeTick 0x10ae3c20 (native 1403; editor 0x1120ff60, the health part 0x1121006f to 0x112102e9, int()
  being the _ftol helper at 0x1123fb80), SPawnAttaque.ComputeEnergyLevel 0x10af5fa0; stock TickNative 0x10af5cf0 has
  no health rule.
- Script order: SPawnAttaque.Tick calls TickNative(default.Health) then super.Tick; SBasePawn.Tick calls NativeTick
  first (sha1 8604928c46b3).

## Open questions
- Who writes PoisonnedBy (no script writer; the poison death credits it, or Instigator when it is None).

### Resolved
- `TickNative` behaviour and regeneration. Resolved: regeneration is in `SBasePawn.NativeTick` (server, SPawnAttaque,
  0 < Health < 100, still and not lying; after 3 s, min(4, pool) on each whole second; cap 100); TickNative handles
  the glass effect only and ignores its argument, stock and Reloaded. Rule 3. Evidence: natives2-damage.md rules 8, 9
  (ANSWERS-natives2 25).
- Poison drain location, rate, death attribution. Resolved: rule 4 (6 hp/s accumulated in bHealthDecrease, a 1500
  SDTBase kill with PoisonnedBy or Instigator). Evidence: natives2-damage.md rule 8 (ANSWERS-natives2 25).
