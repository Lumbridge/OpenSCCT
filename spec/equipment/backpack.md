# Backpack (SGadBackPack, SBackPack, SBackPack_Att)

Units:
- def:SBase.SGadBackPack, def:SBase.SBackPack, def:SBase.SBackPack_Att
- uc:SBase.SGadBackPack:GetBackPackCharge, uc:SBase.SGadBackPack:Tick, uc:SBase.SGadBackPack:Use,
  uc:SBase.SGadBackPack:Destroyed, uc:SBase.SGadBackPack:PostBeginPlay, uc:SBase.SBackPack:Tick
- The mate's side (the "Refill" entry, SBackPackInteraction and UpdateMyInventoryChargeBaseOnMateBackpack):
  `spec/objectives/team-interactions.md#refill`.

## Purpose
The merc's "Back Pack" (team option 0, not in the default kit). It holds a share of its carrier's gadget charges,
which he takes with Alt-fire, and a share for two team mates, which they take with the "Refill" Use entry on him.
The carrier wears a backpack mesh on his spine.

## Data
`SGadBackPack`: `Charge` -10 (unlimited), `RefireRate` 0.1, `bActivatable`, `aNbChargesGad_Self[4]` -1 (not filled),
`TextGadgetRefillDone` "Gadget refill done", `sUseSound` RA_BackPack_use (unused by the gadget itself),
`Controller_A`, `Controller_B`, `DummyBackPack`. On the carrier's pawn (replicated to everyone): `aBackPackOwner_A/B`
(the mates), `aNbChargesGad_Mate_A0..3/B0..3` (-1 until filled) and `aIDGad_Mate_A0..3/B0..3` (gadget ids).
`SBackPack`: mesh `SGameplayObjects.BackPacks.DEF_Backpack`, simulated proxy, `bReplicateInstigator`, no traces;
`SBackPack_Att`: `ATT_Backpack`.

## Rules
1. **GetBackPackCharge(gad)**: a team of 3 or more (`Team.Size >= 3`, the carrier's team) is "big". For a merc
   carrier, by gadget id: frag 4, mine launcher 10 and presence detector launcher 17 give 1 (big) or 2; flare 2 gives
   3 or 6; phosphorus 5 gives 2 or 3. For a spy carrier: smoke 7, chaff 1, flash-bang 3, alarm snare 11, sticky cam 13
   and spy bullet 14 give 3. Anything else: the class's charges x 0.67, truncated (the backpack itself, -10, gives -6).
2. **PostBeginPlay**: based on the owner. On the server: the side's mesh (SBackPack for a merc, SBackPack_Att
   otherwise) spawned owned by the carrier and `AttachToBone(mesh, 'B Spine2')` on him.
3. **Tick, mates** (server, every tick): the player controllers in list order whose pawn is of the carrier's side and
   not the carrier: the first fills `Controller_A` and `aBackPackOwner_A` when A is empty; a later one fills B when B
   is empty. Then A and B follow their controllers' current pawns.
4. **Tick, mate slots** (server): while `aNbChargesGad_Mate_A0` is -1 and mate A has gadgets, his gadgets in list
   order (torches and laser torches left out) up to the team's gadget count fill slots A0..A3 with rule 1's charge and
   the gadget id. The same for B.
5. **Tick, own share** (every machine with the gadget: the server and the owner): while `aNbChargesGad_Self[0]` is -1
   and the carrier has gadgets, the same walk fills the own share; when fewer slots than the team's gadget count were
   filled (the list has not fully arrived), `aNbChargesGad_Self[0]` goes back to -1 and the next tick tries again.
   The walk's counter is not reset after rule 4's B walk, so a first B fill on the server makes that tick's own fill
   fail and retry.
6. **Use** (Alt-fire, on the owner's machine): for each gadget in list order (torches left out, counter i), when its
   charge is below its class's and `aNbChargesGad_Self[i] > 0`: it gets `min(missing, share)` charges (`AddCharge`,
   which a client also sends to the server), the share drops by as much and "Gadget refill done" shows for 2 s.
   The own share lives on each machine on its own; the server's copy is not used by a client's Use.
7. **Destroyed**: the mesh is destroyed (the gadget's own Destroyed is not called).
8. **SBackPack.Tick** (every machine): `AttachToBone(self, 'B Spine2')` on its Instigator.
9. **Mates refill**: the mate's "Refill" entry shows on the carrier when one of the carrier's slots for that mate holds
   a charge for a gadget of his below its class's charges; using it adds the charges on the server and empties the
   slots (team-interactions.md).

## States
None.

## Constants
| Name | Value | Unit | Provenance | Source |
|---|---|---|---|---|
| Big team | 3 | players | decoded | GetBackPackCharge |
| Default share | 0.67 | x class charges | decoded | GetBackPackCharge |
| Refill text time | 2 | s | decoded | Use |
| Bone | B Spine2 | | decoded | PostBeginPlay, SBackPack.Tick |

## Network
- The server picks the mates and fills their slots on the carrier's pawn; the slot variables replicate to everyone,
  which the mates' "Refill" test reads on their machines.
- The own share and Use run on the owner's machine; `AddCharge` keeps the server's charges in step.
- The mesh is a simulated proxy with a replicated Instigator; each machine attaches it to the carrier's bone (the
  Base and bone are not replicated). The view draws it on the drawn skeleton's bone, hidden with the carrier's own
  view in first person.

## Evidence
- `local/re-out/uc/SBase/SGadBackPack.uc`, `SBackPack.uc`, `SBackPack_Att.uc`, `SBackPackInteraction.uc`,
  `STeamDefenseur.uc` (ClassInventory 0); gadget ids in SGadget's constants (inventory-selection.md).

## Open questions
- The port reads the team's gadget count with the inventory's helper (4 without a team), where the script would read
  0 without a team.
- The Instigator of the mesh: a UE2 pawn is its own Instigator, so the script's Spawn passes the carrier; the port
  sets it explicitly.
