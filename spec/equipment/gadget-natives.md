# Gadget natives (SGadget charges, refire and selection)

Units:
- nat:SBase.SGadget.CanUse, nat:SBase.SGadget.SelectNext, nat:SBase.SGadget.DecCharge, nat:SBase.SGadget.ServerDecCharge,
  nat:SBase.SGadget.AddCharge, nat:SBase.SGadget.ServerAddCharge
- uc:SBase.SPlayerController:TellClientToAddGadgetCharge

## Purpose
Decodes of the SGadget natives that `inventory-selection.md` and `grenade-throw.md` left as contracts.

## Data
`Inventory.Charge` (replicated to the owner), `SGadget.LastUsed`, `RefireRate`, `bActivatable`, `IsAPadUpGadget`,
`MyGadget`.

## Rules
1. **CanUse** (decoded 0x10a9f9c0): false while `now - LastUsed < RefireRate`; then true when `Charge > 0` or the
   level is a tutorial (`CurLevelIsTutorial`). A negative charge (-10) is therefore not usable through CanUse
   outside the tutorial; the gadgets with -10 (heartbeat, gas mask, camera network, cloak) never call it.
2. **DecCharge(n = 1)** (decoded 0x10a9fa30): `Charge -= n` on this machine; on a machine without authority also
   `ServerDecCharge(n)`. No floor at 0 and no `LastUsed` change.
3. **ServerDecCharge(n = 1)** (0x10a9fab0): `Charge -= n` on the server.
4. **AddCharge(n = 1)** (0x10a9faf0): `Charge += n` here; without authority also `ServerAddCharge(n)`.
   **ServerAddCharge(n = 1)** (0x10a9fb70): `Charge += n`.
5. **SelectNext** (decoded 0x10a9f970): this gadget or the first one after it along `MyGadget` that is
   `bActivatable`, not `IsAPadUpGadget` and not an `SGadBomb` (class chain test); None when the list ends.
6. `LastUsed` is written by the gadgets themselves: the sticky cam when a cam sticks, the mine launcher when a mine is
   placed, the grenade launcher when it throws (Reloaded's ThrowGren), the cloak and the gas mask when switched on.
7. **TellClientToAddGadgetCharge(id, n)** (server to owner): the first gadget whose `GetUniqueIDByClass` is `id`
   shows "n name received" (3 s) and gets one charge (`AddCharge`). Used by the backpack share.

## States
None.

## Constants
None.

## Network
- The client lowers its own copy at once and the server lowers the replicated value; the next replication of
  `Charge` brings the client back to the server's count.

## Edge cases
- `DecCharge` below 0 is possible when a client sends it twice before the server's value arrives.

## Evidence
- Game natives 0x10a9f9c0, 0x10a9fa30, 0x10a9fab0, 0x10a9faf0, 0x10a9fb70, 0x10a9f970 (private Ghidra copy
  local/ghidra-work/gadgets); name table: CurLevelIsTutorial, ServerDecCharge.
- local/re-out/uc/SBase/SPlayerController.uc 6646 (TellClientToAddGadgetCharge).
