# Team interactions: mate entries, revive, co-op moves, HUD prompts

Units:
- nat:SBase.SMateInteraction.CanUse (the native of the four mate entries; each class has its own C++ virtual behind it)
- uc:SBase.SHealMateInteraction:InitInteract, uc:SBase.SRemoveSpyBulletInteraction:InitInteract, uc:SBase.SBackPackInteraction:InitInteract
- uc:SBase.SPlayerController:UpdateMyInventoryChargeBaseOnMateBackpack
- nat:SBase.SBasePawn.DetectedByMarqueurSpy
- nat:SBase.SPlayerController.CheckIfCanInteractWithMate (the HUD's quick form)
- the spy HUD page's coop icon driver (native, unnamed)

This page gathers what one player can do for a team mate, and decodes the natives the other specs left as expected
contracts. The Use menu itself is `interaction-system.md`; knock-out, the revive request and its states are
`../combat/knockout-and-terminate.md` (rules 32 to 35); the boost and hoist moves are `../movement/coop.md`.

## Purpose

Team mates help each other in four ways:

| Help | How (keys of the stock layout) | Who |
|---|---|---|
| Heal Mate | Use menu (hold Shift, release on "Heal Mate") in front of a hurt mate who faces you | both sides |
| Unspy Mate | Use menu, "Unspy Mate", on a mate carrying a spy mark | both sides |
| Refill | Use menu, "Refill", on a mate carrying a backpack for you | in practice mercs (the backpack gadget) |
| Revive | Use (Shift) near a knocked-out mate lying on the ground | both sides |
| Boost (short ladder), hoist | the co-op key (T): one becomes the helper, the other climbs | spies |

There is no move in this build that removes a sticky shock from a team mate: a sticky shock is a timed effect on
the shocked pawn (`../equipment/sticky-shock-effect.md`), and no interaction, menu entry or native touches another
pawn's shock. The only tracker a mate can remove is the spy mark (`bSpyBulletOn`: a merc's laser mark on a spy, or a
spy bullet on a merc), through "Unspy Mate".

## Mate search

The four mate entries share one search, run by their native `CanUse` each time the menu asks it (on the input
machine: `HasUsableInteractions` when Use is pressed in a walking state, the menu's selection natives, the HUD's
list). Each class adds its own test (below). In order:

1. No controller or no pawn: not usable.
2. The class's own precondition (heal: `TheController.iHealShot > 0`; backpack: the player's gadget test).
3. For each pawn of the level's pawn list, in order: skip it unless `Health > 0` and not `bLyingOnGround`; then
   unless it is not the player's own pawn, passes the class's mate test and has the player's `SPawnType`; then
   unless the distance from the player's pawn is under 200 uu (3D), the normalised direction from the player to it
   dotted with the player pawn's facing (`vector(Pawn.Rotation)`) is over 0.75 (about 41 degrees: the mate is in
   front), and the mate's facing dotted with the player's facing is under -0.75 (the mate faces the player); then
   unless a line from the player's pawn to the mate (flags 0x86: movers, BSP, world geometry; zero extent; the
   entry as the source) hits nothing.
4. The first pawn that passes becomes `Mate`. When it differs from the entry's current `Mate`, the entry is set and
   the `ReplicateMate(Mate)` event runs: a reliable client-to-server function (`interaction-system.md` network), so
   the server's copy of the entry, owned by the player's controller, learns the mate the client chose.
5. The entry's own `bUsable`, `UsableBy`, reuse time and facing fields are not read.

Class tests:

| Entry | Precondition | Mate test |
|---|---|---|
| SMateInteraction (base, never spawned) | none | none |
| SHealMateInteraction | `PC.iHealShot > 0` | `Mate.Health != Mate.default.Health` (hurt, or over) |
| SRemoveSpyBulletInteraction | none | `Mate.bSpyBulletOn` |
| SBackPackInteraction | see Refill | the mate's backpack slot for this player holds a charge for a gadget not full |
| SLeaveInteraction (Exit) | always usable | - |

`iHealShot` defaults to 5 and nothing in script or native code changes it, so heals are unlimited.

### Heal Mate

`InitInteract` (server, from `InteractionInit`): if `Mate.Health > 0`: the mate's controller counts its poison
drain (`CountPoisonDrain`), `Mate.Health = Mate.default.Health`, `bPoisonned = false`; then the default
`InitInteract` mirrors the healer back to his previous state. No animation, no sound, no points: the menu closes
and the mate's health bar fills.

### Unspy Mate

`InitInteract`: `Mate.DetectedByMarqueurSpy(0, false)` (with no mate this does nothing), then the default.

`DetectedByMarqueurSpy(Team, bActivate)` (native 1818, a reliable client-to-server function of SBasePawn):

- `bActivate`: `bSpyBulletOn = true`, `bForceSpied = true` (the mark does not expire while held), every controller
  of the other side gets `bCanListenSpyBulletedGuy = true`, `iTeamSpying = Team`, `fTimeSpy = TimeSeconds`, then the
  pawn's `SetEMF` event.
- not `bActivate`, only when `bSpyBulletOn`: `bForceSpied = false`, `bSpyBulletOn = false`; when no pawn of this
  side is still marked, every controller of the other side gets `bCanListenSpyBulletedGuy = false`; the
  controller's `OtherTeamCantHearMe` event; and `fTimeSpy = TimeSeconds - TimeSpyBullet`. The pawn's own tick then
  runs its mark expiry at once (the "no longer spied" team message and its clean-up), since `fTimeSpy != 0`,
  `bForceSpied` is false and more than `TimeSpyBullet` has passed.

### Refill

`CanUse`: for the player's gadget list (`MyGadget` chain), every gadget that is not an SGadTorche or SGadTorcheLaser
(or a subclass) counts, in order; for the first four, `full[i] = default.Charge <= Charge`. When the list has such
gadgets and all four flags are set (four full gadgets), not usable. Otherwise the mate search with the test: (the
mate's `aBackPackOwner_A` is the player's pawn and one of `aNbChargesGad_Mate_A0..A3` is above 0 where `full[i]` is
false) or the same with `aBackPackOwner_B` and `B0..B3`.

`InitInteract`: with a mate: `UpdateMyInventoryChargeBaseOnMateBackpack(Mate)`; the mate plays `sUseSound`
(`RA_BackPack_use`) at volume 1, radius 100; the player's stats count `Merc_BackpacksTaken`, the mate's
`Merc_BackpacksGiven`; then the default.

`UpdateMyInventoryChargeBaseOnMateBackpack(Mate)`: for each counted gadget i (same order and skip rule) below its
default charges: the slot is A when `Mate.aBackPackOwner_A == Pawn`, else B; when the slot's charge i is above 0,
`n = byte(Clamp(default.Charge - Charge, 0, slot[i]))`, the gadget gets `AddChargeOnClientFromServer(n)` and the
slot loses n.

### Revive

`../combat/knockout-and-terminate.md` rules 32 to 35. In short: Use pressed in a walking state; spies try grab
first, mercs try the Use menu first; `FindPawnToReanimate` looks for a sleeping team mate within 250 uu with the
direction to it dotted with the facing above -0.5 and asks the server (`AskServerToReanimatePawn`, reliable), which
sends the mate to `s_Reanimating` (SA_STANDUP: the wake-up clip on channel 0) and the reviver to
`s_ReanimateFriend_0H` or `_2H` (SA_REANIMATE: ReanStAlFd0 / ReanCrAlFd0, or Fd2 with the weapon, on channel 16).
The mate search above skips a lying mate, so the menu never offers Heal Mate on a downed mate: Use revives him.

## HUD prompts

The spy page's list of Use menu entries is `interaction-system.md`'s menu in slot order: the touched objects by
priority, then Refill, Unspy Mate, Heal Mate, Give, Exit (all five fixed entries have priority 999999 and keep their
slot order; the menu's first selection is the first usable slot, `SelectNextItem` walks the slots and wraps). The
merc page has no list page: a merc uses the menu without seeing it.

Coop icons (`A_Coop`, areas `Move_coop` to `Move_coop4`; the decoded driver):

1. All hidden while the controller is in `s_Hack_Use` or `s_Computer_Use`, in a tutorial level
   (`CurLevelIsTutorial`), or while the own pawn is a ready helper (`bReadyForShortLadder` or `bReadyForHoistClimb`).
2. Otherwise `CheckIfCanInteractWithMate(Mate, 1)`: the mate search of the co-op key with its second argument set,
   which answers 1 as soon as a ready boost helper is in reach, without the boost spot test and without writing the
   ledge ends (`ZipP1`, `ZipP2`) that the key's call (argument 0) writes.
3. 1: `Move_coop2` ("climb on him"). 3: `Move_coop4` ("be boosted up").
4. 0: a walking pawn (Physics 1) with a boost spot (`CheckIfCanStartSHL`'s body) gets `Move_coop` ("offer a
   ladder"); a pawn on a ledge (Physics 9) with a hoist type under it (`FindHoistClimbType`'s body) gets
   `Move_coop3` ("offer a hoist"). Anything else: none.
5. A shown icon starts its animation from frame 0 when it appears and stays until another state hides it.

This replaces the approximation of `../hud/counters-and-prompts.md` rule 6.

## Network

- The mate search runs on the input machine. `ReplicateMate` is reliable client to server on the entry, which the
  player's controller owns. `InteractionInit` (reliable) carries the client's entry; the server's `InitInteract`
  uses the `Mate` its copy holds (set by `ReplicateMate`, or by the server's own search when the server runs it).
- `bSpyBulletOn`, `Health` and `bPoisonned` replicate from the server; the client sees the heal or the mark go.
- `DetectedByMarqueurSpy` is also a reliable client-to-server function; the menu calls it on the server.

## Constants

| Name | Value | Unit | Provenance | Source |
|---|---|---|---|---|
| Mate search range | 200 (3D, strict) | uu | decoded | mate CanUse (game 0x10c10af0) |
| Mate facing threshold | 0.75, both ways (strict) | dot | decoded | mate CanUse (game 0x10c10e70) |
| Mate line flags | 0x86, zero extent | | decoded | mate CanUse |
| iHealShot | 5, never spent | heals | decoded | SPlayerController default; no writer in script or the exe |
| Backpack sound radius | 100 | uu | decoded | SBackPackInteraction.InitInteract |
| Refillable gadgets | the first 4 that are not torches | | decoded | backpack CanUse, UpdateMyInventoryChargeBaseOnMateBackpack |

## Bots

The original has no bots. The rewrite's bots use these interactions only through a player's keys (docs/AI.md,
"Helping team mates"): their choice of when to help is the bots' own.

## Evidence

- Game: CanUse virtuals (vtable slot +0x138; editor +0x14c) heal 0x10ab97f0, unspy 0x10ae8050, backpack 0x10abc920;
  ReplicateMate call 0x10ab9a70 (FName 1057691); exec thunk 0x10ab96f0; DetectedByMarqueurSpy 0x10ae70c0 (events
  SetEMF 876965, OtherTeamCantHearMe 875074); CheckIfCanInteractWithMate 0x10aadc30 (second argument skips
  0x10ae6d40 and the ZipP writes); spy HUD coop driver 0x10b2db20 (spy page object vtable 0x10be0e00 per Bridge
  docs/OVERLAYS.md; state names 9003 s_Hack_Use, 9004 s_Computer_Use; event CurLevelIsTutorial 1039341); menu
  natives HasUsableInteractions 0x10abed10 (two or more usable), SelectFirstViewAbleItem 0x10abee10, SelectNextItem
  0x10abeed0, AddInteractionObj 0x10abf4c0 (inserts before the first slot of higher priority, scanning ten slots).
- Editor twins: ASMateInteraction CanUse 0x111ca490, heal 0x111ca6e0, backpack 0x111ca960, unspy 0x111cadf0.
- Field offsets from local/reflect: iHealShot 0xc10, Mate 0x364, bLyingOnGround 0xaf0 bit 0x4000, bSpyBulletOn
  0xaf4 bit 0, bForceSpied 0xaf0 bit 3, bCanListenSpyBulletedGuy 0xc38 bit 2, aNbChargesGad_Mate_A0 0xb18,
  aBackPackOwner_A 0xb38, aBackPackOwner_B 0xb5c, Inventory.Charge 0x330.
- Decompiled script: local/re-out/uc/SBase/SHealMateInteraction.uc, SRemoveSpyBulletInteraction.uc,
  SBackPackInteraction.uc, SMateInteraction.uc, SPlayerController.uc (UpdateMyInventoryChargeBaseOnMateBackpack),
  SBasePawn.uc (spy mark expiry in Tick).

## Open questions

1. The level's pawn list order (ULevel + 0x5c) is taken as the actor order; with two mates both in reach the first
   one wins.
2. Not yet compared with a two-player capture of the real game (heal, unspy, revive, boost in the sandbox).
