# SGETrigger (traversal element activation mask)

Units:
- def:SBase.SGETrigger
- uc:SBase.SGETrigger:Trigger
- uc:SBase.SGETrigger:UnTrigger
- rep:Engine.LevelInfo.GEActivationMask

## Purpose

Switches groups of traversal elements (GEs: ledges, pipes, zip lines, ladders) on or off by editing the level's
`GEActivationMask`. A GE with ID 0 is always present; a GE with a non-zero ID is present while
`(ID & GEActivationMask) != 0`. SteeE has one SGETrigger, Tag `blast01`, which removes the ledge on top of the wall
the crane destroys.

## Data

| Field | Owner | Type | Default | Meaning |
|---|---|---|---|---|
| `GEMask` | SGETrigger | byte | 0 | the bits this trigger controls |
| `bToggle` | SGETrigger | bool | false | Trigger toggles instead of setting |
| `GEActivationMask` | LevelInfo | byte | 0 (class), 4 stored on SteeE | live mask |
| Defaults | SGETrigger | | | `RemoteRole=ROLE_None`, hidden |

## Rules

1. `Trigger`: if not `bToggle`, or the mask does not already contain all of `GEMask`
   (`(GEActivationMask & GEMask) != GEMask`): `GEActivationMask = GEActivationMask | GEMask` (switch on).
   Otherwise (toggle and all bits already set): `GEActivationMask = GEActivationMask - (GEActivationMask & GEMask)`
   (switch off), which equals clearing the bits.
2. `UnTrigger`: clear the bits: `GEActivationMask = GEActivationMask - (GEActivationMask & GEMask)`.
3. A toggling trigger with a multi-bit mask where only some bits are set switches all of them on (not off).
4. The traversal code reads the mask when it tests GEs (see the GE format spike and the traversal specs); the
   change is immediate for every later test. A player already hanging on a GE that disappears is handled by the
   traversal code (not specified here).

## States

None.

## Constants

| Name | Value | Unit | Provenance | Source |
|---|---|---|---|---|
| SteeE initial mask | 4 | bitmask | decoded | SteeE LevelInfo0 `GEActivationMask` |
| SteeE SGETrigger271 | GEMask 4, bToggle true, Tag `blast01` | | decoded | SteeE.sdc |
| SteeE ID-4 GE | GE 153, LG from (-3186.0, 1087.8, -808.0) to (-3186.0, 56.1, -808.0) | uu | decoded | `docs/spikes/C3-ge-list.md` |

SteeE result: at the crane `blast01` notify, mask 4 has all bits of 4, so the toggle clears it: mask becomes 0 and the
wall-top ledge GE 153 is gone for the rest of the round. A second `blast01` (never sent) would restore it.

## Network

The server changes the mask. `GEActivationMask` is replicated reliably from the authority to every client
(`LevelInfo` replication, no dirty condition), so clients' traversal prediction sees the change after it arrives;
until then a client can still predict a grab on the removed ledge, which the server rejects.

## Edge cases

- SGETrigger has no sender filter: anything firing `blast01` would toggle the ledge.
- The mask is a byte; bits above 7 do not exist.

## Evidence

- `local/re-out/uc/SBase/SGETrigger.uc`, `Engine/LevelInfo.uc` (replication block: `GEActivationMask` reliable if
  `Role == ROLE_Authority`).
- `docs/spikes/C3-ge-list.md` (ID gate rule and SteeE values), `local/formats/ge/SteeE.json` (`ge_triggers`).
- Reflection: `Engine.LevelInfo.GEActivationMask` ByteProperty, flags Edit and Net.

## Open questions

1. What happens to a pawn hanging on GE 153 when the mask clears (drop, or keep hanging until it moves). Scenario:
   hang on the wall-top ledge during the crane sequence and record the controller state at 10.96 s.
2. Client prediction window: measure the delay between the server clearing the mask and the client's traversal
   rejecting the ledge.
