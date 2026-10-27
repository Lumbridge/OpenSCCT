# Network relevancy (IsNetRelevantFor) per class

Units: eng:AActor::IsNetRelevantFor, eng:APawn::IsNetRelevantFor, eng:ASBasePawn::IsNetRelevantFor, eng:ASPlayerController::IsNetRelevantFor, eng:ASGamePlayObject::IsNetRelevantFor, eng:ASFlag::IsNetRelevantFor, eng:ASDisk::IsNetRelevantFor, eng:ASInterrupteur::IsNetRelevantFor, eng:ASTriggerLight::IsNetRelevantFor, eng:ASInteractionManager::IsNetRelevantFor, eng:ASLeaveInteraction::IsNetRelevantFor, eng:ASShareObjectInteraction::IsNetRelevantFor, eng:ASGadTorche::IsNetRelevantFor, eng:ASCamera::IsNetRelevantFor, eng:ASVolumetricSensor::IsNetRelevantFor

## Purpose

The exact test the server pass (`net-replication.md` rules 4 and 6) uses to decide whether an actor
is relevant to a connection, for the base class and every override reachable in the SteeE slice.
The class-to-test mapping is taken from the game's own vtables, which is the only reliable source:
some overrides exist in the game without a guard-named twin in the editor and the other way round.

## Data

Inputs: `RealViewer` (the connection's player controller), `Viewer` (its view target), `Src` (the
camera location from `PlayerCalcView`). Actor fields: `bAlwaysRelevant`, `bHidden`,
`bOnlyOwnerSee`, `bBlockPlayers`, `bNoDelete`, `Owner`, `Base`, `AttachmentBone`, `Instigator`,
`AmbientSound`, `SoundRadius`, `Location`, `CollisionHeight`, the primitive bounding sphere radius
`r`. Pawn cache: `NetRelevancyTime`, `LastRealViewer`, `LastViewer`, `bCachedRelevant`.

"Owned by X" means X appears in the actor's `Owner` chain (the actor itself included).

## Rules

1. **AActor (default).** Relevant when any of these holds, checked in order:
   1. `bAlwaysRelevant`; the actor is `Viewer`; `Viewer` is the actor's `Instigator`; owned by
      `Viewer`; owned by `RealViewer`;
   2. it has an `AmbientSound` and `|Location − Viewer.Location| < 100 × SoundRadius`;
   3. it has an `Owner`, is based on that owner and is not `bOnlyOwnerSee`: the owner's result is
      returned;
   4. if `(bHidden or bOnlyOwnerSee)` and not `bBlockPlayers` and no `AmbientSound`: not relevant;
   5. `|Location − Src|² − r² < 2500²`: relevant;
   6. otherwise relevant only when a BSP-only line check from `Location` to `Src` is clear.
   Only world geometry blocks the check; actors, movers and static meshes do not.
2. **APawn.** First a per-tick cache: when the relevancy was already computed this tick for the same
   `RealViewer` and `Viewer`, the cached answer is returned. Otherwise relevant (and cached as
   relevant) when `bAlwaysRelevant`, owned by `Viewer` or `RealViewer`, the pawn is `Viewer`, or
   `Viewer` is its `Instigator`. Then: an `AmbientSound` within `100 × SoundRadius` of the viewer →
   relevant; hidden as in 1.4 → not relevant; `|Location − Src| − r < 2500` (linear distance) →
   relevant; else two BSP line checks to `Src`, from `Location + (0, 0, CollisionHeight)` and from
   `Location`: relevant when either is clear. Every outcome is written to the cache with the time and
   both viewers.
3. **SBasePawn** (also `SPawnAttaque`, whose vtable has its own copy): the APawn test; when it
   says relevant, the three linked pawns (`m_PawnNoMoreGrabbed`, `m_AttackTarget`, `m_AttackedBy`)
   are also marked relevant in their caches for this viewer and tick. When it says not relevant, the
   pawn is still relevant if one of those three has a cached "relevant" from this tick for the same
   viewers, or if its `UsedObject` is an `SCamNetwork` (an IsA test, subclasses included) and
   `bUsingCamNetwork` is set; in both cases the pawn's own cache is rewritten as relevant for this tick
   and viewers (the cache write 0x10a304a0). Confirmed in game 0x10ae41c0: partners at pawn + 0x634,
   0x62c and 0x630 in that order; a cached "relevant" is flag 0x400 of + 0x4ac with the stamp at + 0x390
   equal to `TimeSeconds` and `RealViewer`/`Viewer` at + 0x394/+ 0x398; `UsedObject` + 0x604,
   `bUsingCamNetwork` bit 0x100 of + 0xaf0. So a grab, a
   terminate or an attack pair is always relevant as a pair, in either direction, provided the
   partner was evaluated earlier in the same pass.
4. **SPlayerController.** Relevant only to its own connection (`RealViewer` is the controller).
5. **SGamePlayObject** (also `SVitre`, `SComputerObjectiveTrigger`, `SFlagDropZone`,
   `SBombTargetObjectiveTrigger`): the AActor test with one extra step after 1.3: a `bPickable` object
   attached to a bone (`AttachmentBone` set) of a skeletal-mesh `Base` returns the base's result.
   `SBasicGamePlayObject` itself, `SObjectiveTrigger` and `SInteractObject` use the AActor test.
6. **SFlag.** `bAlwaysRelevant` → relevant; `bHidden` → not relevant; carried (`Base` is a pawn) →
   the carrier's result; otherwise relevant.
7. **SDisk.** As SFlag, except that while carried it returns the carrier's cached pawn relevancy flag
   (`bCachedRelevant`) without re-evaluating it.
8. **SInterrupteur, SAmmoStock, SInfirmary.** `bAlwaysRelevant` → relevant; `bHidden` → not
   relevant; relevant when `|Location − Src| < 2500`, else not (no line check).
9. **STriggerLight.** `bAlwaysRelevant` or `bNoDelete` → relevant; `bHidden` without ambient sound →
   not; `|Location − Src| < DistAlwaysVisibleIn` → relevant; `< DistNeverVisibleOut` → BSP line check;
   else not relevant. Map trigger lights are `bNoDelete`, so in SteeE they are always relevant.
10. **SInteractionManager, SPlayerCam.** Relevant only when owned by `RealViewer`.
11. **SLeaveInteraction, SShareObjectInteraction.** Owned by `RealViewer` → relevant; no `Owner`, or
    `Base` differs from `Owner` → not relevant; else the base's result.
12. **SGadTorche and its subclass** (editor overrides): the `Instigator`'s result.
13. **SCamera, SVolumetricSensor** (outside the slice; editor): distance and line tests on their own
    radii; listed for completeness only.
14. Every other slice class (`SCamNetwork`, `SStickyCam`, `SObjective`, `SMission`, `STeamInfo`,
    weapons, gadgets, `SAmbientSoundTrigger`, `SGoggle`, movers, infos) uses the AActor test, which
    for `bAlwaysRelevant` classes (pawns, infos, movers, objective actors) is always true and for
    owner-only inventory is decided by the owner rule of the pass before relevancy is even asked.

## States

None. The pawn cache is valid for one tick and one pair of viewers.

## Constants

| Name | Value | Unit | Provenance | Source |
|---|---|---|---|---|
| Always-visible distance | 2500 | uu | decoded | game 0x10a8f760, 0x10ae41c0, 0x10aea530 |
| Ambient sound relevancy factor | 100 | × SoundRadius | decoded | game constant 0x10c71afc used by 0x10a8f760 |

## Network

Server only, during the replication pass.

## Edge cases

- Distances are measured from the camera location `Src` (kept current by
  `UpdatePositionForServer`, `spec/net/move-protocol.md` rule 17), except the ambient sound test,
  which uses the view target's location.
- The pawn cache makes the result depend on evaluation order within a pass (rule 3 and rule 7).
- Relevancy is only re-evaluated about once per second per channel and a channel survives 5 s of
  irrelevance (`net-replication.md` rule 6), so a non-relevant result rarely closes a channel quickly.

## Evidence

- Game vtable slot 57 of each class's `vftable` (labelled in the game program): AActor 0x10a8f760,
  SPlayerController 0x10a30490, SGamePlayObject family 0x10a8f9e0, STriggerLight 0x10ab9540,
  SInteractionManager and SPlayerCam 0x10abfab0, SDisk 0x10abfd60, SPawnAttaque 0x10ae41c0, SFlag
  0x10ae9a00, SAmmoStock / SInterrupteur / SInfirmary 0x10aea530, SLeave / SShareObjectInteraction
  0x10af7320.
- Editor twins: `AActor::IsNetRelevantFor` 0x11182c90, `APawn` 0x11182f40 (cache write 0x11182300),
  `ASBasePawn` 0x111fe9b0, `ASGamePlayObject` 0x111c1f10, `ASBasicGamePlayObject` 0x111c1b10 (same
  test as AActor), `ASFlag` 0x111cf570, `ASTriggerLight` 0x111d3c30 (guard-named "AActor"),
  `ASLeaveInteraction` 0x111be0c0, `ASShareObjectInteraction` 0x111c9f00, `ASGadTorche` 0x111de220
  and 0x111df8f0, `ASCamera` 0x111d2150, `ASVolumetricSensor` 0x111c1580.
- Field offsets (reflection): `bOnlyOwnerSee` 0x2e8 bit 19, `bBlockPlayers` 0x2f0 bit 15,
  `NetRelevancyTime` 0x390, `LastRealViewer` 0x394, `LastViewer` 0x398, `bCachedRelevant` 0x4ac bit
  10, `STriggerLight.DistAlwaysVisibleIn` 0x31c, `DistNeverVisibleOut` 0x320.

## Open questions

1. `SPAWNDEFENSE` has no labelled vtable in the game; confirm it uses the SBasePawn test.
2. Which `SoundRadius` units SCCT uses (the factor 100 suggests a script value in metres or a
   designer scale); check a map ambient sound's radius against its audible range.
