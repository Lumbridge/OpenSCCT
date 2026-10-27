# SDamageVolume (periodic damage zone)

Units:
- def:SBase.SDamageVolume
- uc:SBase.SDamageVolume:Touch
- uc:SBase.SDamageVolume:Trigger
- uc:SBase.SDamageVolume:UnTrigger
- uc:SBase.SGameInfo:FindDamageVolume

## Purpose

A brush volume that hurts pawns of a class inside it every `Period` seconds while active, and can be switched on and
off by events, crediting the player who caused it. SteeE has 7: six always-on volumes (10 hit points per second)
and the molotov fire (30 per second for 31 s after the bottle breaks).

## Data

| Field | Type | Default | Meaning |
|---|---|---|---|
| `bActive` | bool | false | damaging now |
| `bToggle` | bool | false | Trigger toggles |
| `HitPoints` | int | 5 | damage per application |
| `Period` | float | 1.0 | s between applications |
| `DamageType` | SDamageType | `SDTBase_NoAnim` (6) | |
| `DamagedClass` | class | `SBase.SBasePawn` | who is hurt |
| `TriggerController` | Controller | None | credited controller |
| `LastTouched` | Actor | None | last actor that entered |
| `LastDamageTime` | float, transient | 0 | |
| Defaults | | | Volume brush, `bStatic=False` (so it can be triggered), `RemoteRole=ROLE_None`, `bCollideActors`, hidden, `native` |

## Rules

1. `Touch(Other)`: if `Other` is not `LastTouched`, set `LastDamageTime = 0`; then `LastTouched = Other`.
2. `Trigger(Other, I)`: if `bToggle`, flip `bActive`; else if inactive, activate. If now active: the instigator is
   `I`, else `Other.Instigator`, else `SGameInfo.GPODamageInstigator`; if one was found,
   `TriggerController = instigator.Controller` (otherwise the previous value is kept). If now inactive:
   `TriggerController = None`.
3. `UnTrigger`: `bActive = false`, `TriggerController = None`.
4. Damage application (native tick, contract): while `bActive`, every `Period` seconds each touching actor whose
   class is `DamagedClass` or a subclass takes `HitPoints` damage of `DamageType`, with no direct instigator
   (`StandardReactionsToDamage` then looks the volume up, rule 6). `LastDamageTime = 0` on entry (rule 1) makes a
   newly entered pawn take its first hit at once. Measured in RE+: a merc in a 300 hit-point `SFell` volume
   ended at health -200 (the full amount is applied, health can go below zero); pawns that teleport in are not hurt
   until they move (no Touch).
5. `SGameInfo.FindDamageVolume(Victim, DamageType)`: among the volumes the victim touches, the first that is active,
   has the same `DamageType` and has a `TriggerController`; else None.
6. Credit: in `StandardReactionsToDamage`, a hit with no instigator (after the GPO fallback) takes
   `FindDamageVolume(...).TriggerController.Pawn` as instigator and that controller as killer. `SGameInfo.Killed`
   counts an environmental kill when the killer is that volume's `TriggerController`. Always-on volumes have no
   `TriggerController`, so their deaths have no killer (environmental deaths).
7. Only the volume is switched; the visuals (fire emitters) and sounds are separate actors on the same event.

### Native damage tick (decoded, natives2-damage.md rule 6)

Authority only and while bActive. The period uses the engine's real-time clock (appSeconds), not Level.TimeSeconds:
when `now - LastDamageTime > Period` (strictly), LastTouched is cleared (except on the first period) and
LastDamageTime = now; then every touching actor of DamagedClass (any class when None), in Touching order, is hurt:
a pawn only when its controller answers `bIsDamageVolumeSensitive()` true, through
`PawnTakeDamage(HitPoints, None, A.Location, A.Location - volume.Location, DamageType, 0, 0)`; other actors through
TakeDamage. The volume's own tick does not call the parent tick (no timers, state code or physics). A newly touching
actor is hurt on the volume's next period boundary (one clock for all); entering with LastDamageTime 0 hurts everyone
touching on the next tick.

## States

None (flag driven).

## Constants

| Name | Value | Unit | Provenance | Source |
|---|---|---|---|---|
| default HitPoints / Period | 5 / 1.0 | hp / s | decoded | def:SBase.SDamageVolume |
| default DamageType | SDTBase_NoAnim | | decoded | def |

SteeE volumes:

| Actor | Location | Active | HitPoints | Period | Type | Tag |
|---|---|---|---|---|---|---|
| SDamageVolume269, 2005, 1985, 2026, 2016, 2046 | various | always (`bActive`) | 10 | 1.0 | SDTBase_NoAnim | `SDamageVolume` (never fired) |
| SDamageVolume228 | (-1798, -876, -708) | off until triggered, `bToggle` | 30 | 1.0 | SDTBase_NoAnim | `moloaie` |

Molotov fire timeline: bottle (SGamePlayObject215) destroyed at t0 fires `molotov`; SMagicEvent232 fires `moloaie` at
t0 + 1.0 s (volume toggles on, credit to the bottle's destroyer if the instigator survives the chain) and at
t0 + 32.0 s (toggles off). A pawn standing in it takes 30 per second, up to 31 or 32 applications depending on
alignment (estimated).

## Network

Server only (`ROLE_None`). Damage reaches clients as pawn health and damage effects.

## Edge cases

- `LastTouched` holds one actor: two pawns entering alternately each reset `LastDamageTime`, which can make the
  volume hurt more often than once per `Period` (depends on the native tick, open).
- A toggle-type volume triggered by two separate sources turns off on the second.
- If the activation reaches the volume with no instigator and no fallback, `TriggerController` keeps its old value
  (None for the molotov).

## Evidence

- `local/re-out/uc/SBase/SDamageVolume.uc`, `SBase/SGameInfo.uc` (FindDamageVolume, Killed),
  `SBase/SPlayerController.uc` (StandardReactionsToDamage instigator fallbacks).
- SteeE.sdc: 7 `SDamageVolume` exports, `SMagicEvent232`, `SGamePlayObject215`.
- RE+ MapBuilderFacts.md (merc-only 300 hit-point volume, merc to -200, spy untouched) and MapBuilderPatterns.md
  (volume must be non-blocking; teleporting in skips the touch; `SDamage_ElbowShock` volume stuns).

## Open questions

2. Which instigator the molotov chain delivers: the native ProcessDamage fires stage events with instigator None
   (natives2-damage.md rule 1.6), so the credit depends on the script wrapper's GPODamageInstigator and on what
   SMagicEvent keeps for the 1 s queued entry. Scenario: a merc shoots the bottle, a spy dies in the fire;
   check the merc's kill and environmental counts.
3. Whether damage can come from volumes the pawn only touches with its collision edge (Touch rule, collision spec).

### Resolved

1. The native damage tick. Resolved: rule "Native damage tick" (real-time period strictly exceeded, every touching
   actor of DamagedClass, pawns only through bIsDamageVolumeSensitive, LastTouched cleared on each period after the
   first, no parent tick). Evidence: natives2-damage.md rule 6 (ANSWERS-natives2 23).
