# Ammo stock (SAmmoStock, SAmmoStockInteraction)

Units:
- uc:SBase.SAmmoStock:AcceptStickableGPO, uc:SBase.SAmmoStock:PostNetBeginPlay, uc:SBase.SAmmoStock:Use, def:SBase.SAmmoStock
- uc:SBase.SAmmoStockInteraction:InitInteract, def:SBase.SAmmoStockInteraction
- uc:SBase.SPlayerController:ReloadAmmoStock
- uc:SBase.SWeaponDefense:FullReload, uc:SBase.SWeaponDefense:s_FullReloading.BeginState, uc:SBase.SWeaponDefense:s_FullReloading.AnimEnd
- uc:SBase.SInteractObject:InitInteract, uc:SBase.SInteractObject:PostBeginPlay (radius and usability parts)

## Purpose
A placed ammunition crate that mercs use to refill the rifle to its maximum. In Sabotage it also refills a spy's
bomb, but the interaction is merc-only by default.

## Data
| Field | Default | Notes |
|---|---|---|
| SAmmoStock.InteractionClass | SAmmoStockInteraction | |
| SAmmoStock.StaticMesh | SGameplayObjects.Objects.dr_ammoclip | |
| SAmmoStock.HeatIntensity / EMFIntensity | 0.6 / 0.6 | |
| SAmmoStock flags | bNoDelete, bShouldBaseAtStartup, bBlockActors, bBlockPlayers, bSideFadeEffect, bNetInitialRotation | |
| SAmmoStockInteraction.UsableBy | U_SPAWNDEFENSE | mercs only |
| SAmmoStockInteraction.iPriority | 20 | |
| SAmmoStockInteraction.Description | "Pick up" | |
| SAmmoStockInteraction.vRelativePositionForInteraction | (55, 0, 0) | interaction spot 55 uu along the crate's X axis (estimated meaning) |
| SBasicGamePlayObject.fInteractionRadius / fInteractionHeight | per instance | used as the interaction cylinder when >= 0 |

SteeE instances (map data, `SteeE.sdc`):

| Actor | Location (uu) | Yaw | Interaction radius / height |
|---|---|---|---|
| SAmmoStock8843 | (-1879.4, -1017.7, -1128.0) | 17160 | 120 / 60 |
| SAmmoStock12649 | (-1260.5, 1914.9, -1128.0) | -23904 | 80 / 60 |
| SAmmoStock8832 | (-3578.4, 1573.3, -1128.0) | 17160 | 120 / 60 |

All three are tagged `SAmmoStock`, in group `stmtotal,__ALL-Icones`, placed with EMFIntensity 0.2 (overridden at runtime).

## Rules
1. **Setup.** `SAmmoStock.PostNetBeginPlay` (every machine): `bEMFIsOn = true`, `EMFIntensity = 0.6`,
   `EMFOpacity = 1`, `bSideFadeEffect = true`, `HeatIntensity = 0.6`, `HeatOpacity = 1` (visible in EMF and thermal).
   `AcceptStickableGPO` is false (sticky cams and similar cannot attach).
2. **Interaction volume** (SInteractObject base): a collision cylinder with the instance's `fInteractionRadius` and
   `fInteractionHeight` when they are >= 0, else the crate's collision size plus 150 uu; based on the crate. Usable
   by mercs only. Approach, prompt and priority handling belong to the interaction spec.
3. **Use** (server, `SAmmoStockInteraction.InitInteract`): `SAmmoStock.Use(Instigator)` calls
   `Instigator.ReloadAmmoStock()`, then the base `InitInteract` returns the controller to its previous state
   (`MirrorState(PreviousState)` if alive and a previous state exists).
4. **ReloadAmmoStock** (reliable server -> owning client RPC, runs on the client):
   - a merc: clear the HUD reticle flash; mark refilled;
   - if the pawn has an SGadBomb: `FullReload()` on it and `Charge = default.Charge`; mark refilled;
   - if refilled: `MyWeapon.FullReload()` and play Munitions_caisses1 (radius 100) to the owner.
5. **Rifle full reload** (`merc-rifle.md` Rule 18): refused during a HUD perturbation; only if `Ammo < MaxAmmo`;
   2.067 s reload clip, then `Ammo = 125`, `ClipAmmo = 25`.
6. **Not refilled**: grenades, mines, cameras and any other gadget charges. Uses are unlimited and there is no
   cooldown in script (`iReusableTime` 0).

## States
None of its own; the weapon goes `s_Selected -> s_FullReloading -> s_Selected / s_Firing`.

## Constants
| Name | Value | Unit | Provenance | Source |
|---|---|---|---|---|
| Refill target | Ammo 125, ClipAmmo 25 | rounds | decoded | SWeaponDefense defaults |
| Refill time | 2.067 | s | decoded (clip data) | FirstReloStFd2 |
| EMF and heat intensity at runtime | 0.6 | | decoded | PostNetBeginPlay |
| Default interaction size | collision + 150 | uu | decoded | SInteractObject.PostBeginPlay |
| Sound radius | 100 | uu | decoded | ReloadAmmoStock |

## Network
- Server: interaction acceptance and `InitInteract`. Client: `ReloadAmmoStock` and the weapon full reload (ammo is
  client-side, `merc-rifle.md` Network). The crate itself is a static map actor (bNoDelete).

## Edge cases
- A merc with full ammo who uses the crate hears the crate sound but the rifle does not reload.
- Using the crate during a HUD perturbation (after a shock or chaff) plays the sound but refuses the reload.
- In Story mode no spy carries a bomb, so the bomb branch is unused on SteeE.
- The middle crate (80 uu) uses a smaller radius than the other two (120 uu); Enhanced enlarged the radius of the
  crate near the Shop shutters (spec/enhanced/FEATURES.md, SteeE section).

## Evidence
- local/re-out/uc/SBase/SAmmoStock.uc, SAmmoStockInteraction.uc, SInteractObject.uc, SPlayerController.uc 8024-8048,
  SWeaponDefense.uc FullReload and s_FullReloading.
- SteeE.sdc exports SAmmoStock8843, SAmmoStock12649, SAmmoStock8832 (read with scctpkg).

## Open questions
- How the interaction position (55, 0, 0) and `rRotationForInteraction` are used (walk-to spot or facing test); see
  the interaction spec. Scenario: use each SteeE crate from several sides and record the merc's position.
