# SInfirmary and SAmmoStock (heal and resupply stations)

Units:
- def:SBase.SInfirmary
- uc:SBase.SInfirmary:AcceptStickableGPO
- uc:SBase.SInfirmary:Use
- def:SBase.SInfirmaryInteraction
- uc:SBase.SInfirmaryInteraction:InitInteract
- def:SBase.SAmmoStock
- uc:SBase.SAmmoStock:AcceptStickableGPO
- uc:SBase.SAmmoStock:PostNetBeginPlay
- uc:SBase.SAmmoStock:Use
- def:SBase.SAmmoStockInteraction
- uc:SBase.SAmmoStockInteraction:InitInteract
- uc:SBase.SPlayerController:ReloadAmmoStock
- rep:SBase.SPlayerController.ReloadAmmoStock

## Purpose

Wall medkits that refill a player's health and remove poison, usable by both teams, and ammo crates that refill a
merc's weapon (and a bomb gadget's charges), usable by mercs only. SteeE has 3 of each. Both are
SBasicGamePlayObjects that spawn an interaction object the player uses with the Use key (the interaction framework
is specified with the interaction system).

## Data

| Class | Field | Value |
|---|---|---|
| SInfirmary | `HealSound` | `Armes_impacts.Manip.RA_Medical_open` |
| SInfirmary | `ReUseTime` | int, no default (0) |
| SInfirmary | `LastUseTime` | -9999.0 |
| SInfirmary | mesh, flags | `SGameplayObjects.Objects.first_aid`, `DT_StaticMesh`, `bNoDelete`, `bCollideActors` |
| SInfirmary | `InteractionClass` | `SBase.SInfirmaryInteraction` |
| SInfirmaryInteraction | `iPriority` 20, `Description` "Heal", `vRelativePositionForInteraction` (55, 0, 0), `UsableBy` inherited `U_BOTH` |
| SAmmoStock | mesh, flags | `SGameplayObjects.Objects.dr_ammoclip`, `bBlockActors`, `bBlockPlayers`, `bShouldBaseAtStartup`, `bNetInitialRotation`, `bSideFadeEffect`, `HeatIntensity=0.6`, `EMFIntensity=0.6` |
| SAmmoStock | `InteractionClass` | `SBase.SAmmoStockInteraction` |
| SAmmoStockInteraction | `iPriority` 20, `Description` "Pick up", position (55, 0, 0), `UsableBy=U_SPAWNDEFENSE` (mercs) |

## Rules

1. Neither accepts sticky gadgets (`AcceptStickableGPO` false).
2. `SInfirmaryInteraction.InitInteract(Instigator, Sub)`: if the user's pawn is alive (`Health > 0`), call
   `SInfirmary.Use(Instigator)`; then the parent interaction logic.
3. `SInfirmary.Use(PC)`: only if `LastUseTime + ReUseTime < TimeSeconds` (with `ReUseTime` 0: any later moment,
   never twice in the same instant). Then `LastUseTime = TimeSeconds`; if the pawn's health is below its class
   default, the player's stats `MedkitsUsed += 1`; `PC.CountPoisonDrain()` (credits drained health to the poisoner);
   clear `bPoisonned`; `Health = default.Health`; a spy's `NbPointsRefillable = 0`; `PlayOwnedSound(HealSound,
   SLOT_Misc, 1.0, , 100)`.
4. Heal is full and instant, unlimited uses, both teams (RE+: health 40 to 100 for both).
5. `SAmmoStockInteraction.InitInteract`: `SAmmoStock.Use(Instigator)` then the parent logic (no health test).
6. `SAmmoStock.Use(PC)` (simulated): `PC.ReloadAmmoStock()`.
7. `ReloadAmmoStock` (declared reliable from the authority, so a server call runs on the owning client):
   if the pawn is a merc, clear the HUD reticule flash and mark reloaded; if it carries an `SGadBomb`, `FullReload` it,
   reset its `Charge` to the default and mark reloaded; if reloaded, `MyWeapon.FullReload()` and play
   `Munitions_caisses1` (SLOT_Misc, volume 1, radius 100).
8. `SAmmoStock.PostNetBeginPlay` (simulated): `bEMFIsOn`, `EMFIntensity 0.6`, `EMFOpacity 1`, `bSideFadeEffect`,
   `HeatIntensity 0.6`, `HeatOpacity 1` (visible in thermal and EMF vision).

## States

None of their own.

## Constants

| Name | Value | Unit | Provenance | Source |
|---|---|---|---|---|
| infirmary reuse delay | 0 (strictly later time) | s | decoded | def, Use |
| heal sound radius | 100 | | decoded | Use |
| interaction offset | (55, 0, 0) | uu, local | decoded | interaction defaults |
| interaction priority | 20 | | decoded | interaction defaults |

SteeE instances: SInfirmary245, 249, 4327 (radius 60, height 120, in front); SAmmoStock8832, 8843 (radius 120,
height 60) and 12649 (radius 80, height 60).

## Network

- Use arrives through the interaction system on the server (`InitInteract` runs where the interaction is handled).
- Healing changes server-side pawn state (health replicates).
- `ReloadAmmoStock` executes on the merc's own client when called on a server; on a listen host for the host's own
  merc it runs locally. Weapon ammo authority belongs to the weapon spec.

## Edge cases

- Using the medkit at full health still clears poison and plays the sound, but does not count a medkit use.
- A spy using the ammo crate is blocked by `UsableBy`; if reached anyway, nothing reloads unless the spy carries a
  bomb gadget (then the bomb and the weapon reload).

## Evidence

- `local/re-out/uc/SBase/SInfirmary.uc`, `SInfirmaryInteraction.uc`, `SAmmoStock.uc`, `SAmmoStockInteraction.uc`,
  `SInteractObject.uc` (defaults: `UsableBy=U_BOTH`), `SPlayerController.uc` (ReloadAmmoStock, CountPoisonDrain,
  replication block).
- RE+ MapBuilderFacts.md (infirmary usable by both, health 40 to 100; ammo stock merc-only prompt).

## Open questions

1. Where the weapon's `FullReload` takes effect when called on a remote client (does the server's ammo count change?).
   Weapon spec; scenario: empty a merc rifle on a remote client, use the crate, fire and compare server ammo.
2. Whether the interaction system enforces a use duration or animation before `InitInteract` for these stations.
