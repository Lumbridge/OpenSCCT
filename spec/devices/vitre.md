# SVitre (breakable glass)

Units:
- def:SBase.SVitre
- def:SBase.SVitreEmitter

## Purpose

A window pane: a one-hit-point SGamePlayObject that blocks players and thrown objects until any damage breaks it,
then disappears in a burst of glass and lets everything through. SteeE has 14 panes; 13 of them also switch on an
outside ambience loop when they break. All behaviour is inherited from `gameplay-object.md`; this file fixes the
class's own defaults and what the player sees.

## Data

`def:SBase.SVitre` (own defaults over SGamePlayObject):

| Field | Value |
|---|---|
| `StaticMesh` | `SGameplayObjects.Objects.F_vitreSolo01`, `DrawType=DT_StaticMesh` |
| `HitPoints` | 1, `bDamageable=True` |
| `TakeDamageTypes` | empty (every damage type breaks it) |
| `DamagedMeshes(0)` | `StaticMesh=None`, `Percent=100`, `bDisableCollision=False`, `Skin=None`, `Event=None`, `EventType=EVT_Trigger` |
| `SpawnableObjects(0)` | `SpawnClass=SBase.SVitreEmitter`, `MinPercent=100`, `MaxPercent=100`, `Scaled=True`, no particle template |
| `DestroySound` | `Armes_impacts.Impacts.RA_Break_Window` |
| flags | `bWorldGeometry`, `bBlockActors`, `bBlockPlayers`, `bSeeThroughInMotionVision`, `bCollideActors` |
| inherited | `RemoteRole=ROLE_SimulatedProxy`, `bNoDelete`, `Mass=8`, `ShakeRange=2000`, `nativereplication`, native class |

`def:SBase.SVitreEmitter`: one sprite emitter, 150 shards (`glassbreaking3`, 4 x 4 frames), all spawned at once
(`InitialParticlesPerSecond=50000`), start box X +-62, Z +-53 in the actor's frame, start speed X +-50, gravity
`Z -700`, damping Z 0.1 to 0.2, 2 collisions each, sizes 2 to 7, fading, `AutoDestroy`, `LifeSpan=10` s,
`RemoteRole=ROLE_SimulatedProxy`.

## Rules

1. Any accepted damage (bullet, explosion, charge, crush) drives `HitPoints` from 1 to 0, i.e. damage percent 100:
   the pane plays `RA_Break_Window`, its mesh becomes None, its stage Event fires (instance-set), and an
   `SVitreEmitter` is spawned at the pane (`gameplay-object.md` rules 8 and 9).
2. After breaking, the pane no longer draws and no longer blocks: players walk and grenades fly through the frame
   (the SteeE zone tip near the courtyard window says so). This follows from the mesh being removed; whether the
   native also turns collision off is open.
3. A broken pane stays broken for the round (`ResetDamageTime` is 0). Triggering it does nothing (dead
   damageable objects ignore Trigger).
4. While intact the pane is world geometry for traces and movement, and is drawn see-through in motion-tracking vision.

## States

None of its own (SGamePlayObject states apply; a pane never flies or resets).

## Constants

| Name | Value | Unit | Provenance | Source |
|---|---|---|---|---|
| HitPoints | 1 | hp | decoded | def:SBase.SVitre |
| shard count / life | 150 / 10 | count / s | decoded | def:SBase.SVitreEmitter |
| shard start box | X +-62, Z +-53 | uu | decoded | def:SBase.SVitreEmitter |

SteeE panes (all on the class mesh; DrawScale3D (2.6, 1, 1.3) for bay windows, (1.07, 1, 1.5) for others):

| Panes | Stage Event | Receiver |
|---|---|---|
| SVitre16149, 16150, 16143 + 16151, 16146 | `baie_brisee1`, `2`, `3`, `4` | SAmbientSoundTrigger484, 737, 493 + 474, 453 (`Squ_Fenetre_ouverte1`) |
| SVitre11159, 3996, 16140, 1551, 7090, 1548, 11162 | `vitre_brisee`, `2` to `7` | SAmbientSoundTrigger736, 21258, 325, 494, 21259, 418, 297 |
| SVitre1935 | `vitre_bas_brisee` | SoundTrigger267 (`Squ_Fenetre_ouverte2`, one shot) |
| SVitre1557 | none (class default stage) | |

## Network

Server breaks the pane. Clients see it through the replicated mesh change (native replication, see
`gameplay-object.md`), the replicated `SVitreEmitter` actor, the server-played break sound and the replicated
ambient loop of the triggered SAmbientSoundTrigger.

## Edge cases

- The emitter's start box is not scaled for the larger bay panes unless the native spawn path applies `Scaled`
  (script only scales particle templates, and SVitre has none).
- A pane broken by an explosion that also kills a player gives no kill credit of its own (it deals no damage).

## Evidence

- `local/re-out/uc/SBase/SVitre.uc`, `SBase/SVitreEmitter.uc`, effective defaults in
  `local/formats/classdefaults_effective.json`.
- SteeE.sdc: 14 `SVitre` exports and their receivers (event graph).
- SZoneTips28835 text (SteeE): break the glass to exit to the courtyard; grenades go through a window without glass.

## Open questions

1. Does the native drop collision for a pane whose stage mesh is None while `bDisableCollision` is false (else a
   default 22 x 22 cylinder might remain)? Scenario: break a pane and walk, then throw a grenade, through its centre.
2. Does a bullet that breaks a pane continue through it (penetration or a second trace)? Weapons spec; scenario:
   shoot a target directly behind a pane.
3. What the `native` SVitre class adds in C++ (rendering order with glass, `bPlanarDepthSortingWithGlass`, decals).
