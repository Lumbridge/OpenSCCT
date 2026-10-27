# MoverRotating

Units:
- def:Engine.MoverRotating
- uc:Engine.MoverRotating:Timer
- uc:Engine.MoverRotating:InterpolateTo
- uc:Engine.MoverRotating:KeyFrameReached
- uc:Engine.MoverRotating:BeginPlay
- rep:Engine.MoverRotating.RealRotation
- rep:Engine.MoverRotating.SimOldRotRoll
- rep:Engine.MoverRotating.SimOldRotYaw
- rep:Engine.MoverRotating.SimOldRotPitch

## Purpose

A Mover that turns as well as moves: the same states and keys as `mover.md`, plus the rotation the leg starts from
packed for clients and a `RealRotation` for the client drift check. 26 on 5 maps: the BankD/BankP vault doors
(`coffre`, `coffre_2`: TriggerControl and TriggerToggle, chained by OpenedEvent), the BankD/BankP helicopter
(`start_helico_att`: three TriggerToggle movers with 3 keys whose OpenedEvent/ClosedEvent retrigger the group, so
the helicopter flies its route back and forth), and the StatD/StatE/StatS sniper window shutters
(`fenetre_spawn_1/2`: TriggerOpenOnceOnly).

## Data

| Field | Type | Meaning |
|---|---|---|
| `RealRotation` | rotator | server rotation at rest (replicated) |
| `SimOldRotPitch`, `SimOldRotYaw`, `SimOldRotRoll` | int | the leg's start rotation (replicated) |

All Mover fields apply; maps set `KeyPos`, `KeyRot` (up to 3 keys), `NumKeys`, `MoveTime` (5 to 8 s), `StayOpenTime`,
`InitialState`, `MoverEncroachType` (3 Ignore, 4 WaitAndRetry), `OpenedEvent`, `ClosedEvent`, the sounds and the
helicopter body's own `AmbientSound`.

## Rules

1. `InterpolateTo`: the Mover version (mover.md rule 1), and `SimOldRotYaw/Pitch/Roll = OldRot`.
2. `KeyFrameReached` (simulated): the Mover version (chaining through the keys) except that the stop branch does
   not call `StopSound`; it sets `AmbientSound = None` and, on non-clients with `ClientUpdate == 0`,
   `RealPosition = Location` and `RealRotation = Rotation`. (The helicopter body's map `AmbientSound` is therefore
   cleared at the end of its first leg; `DoOpen`/`DoClose` set it to `MoveAmbientSound`, None on that map.)
3. `BeginPlay` (simulated): copies the Original* values, collects AntiPortals, starts the 4 s (client) / 1 s
   (server) timer and returns on a client, as Mover; then calls `Mover.BeginPlay` (which does all of that once
   more, and places the mover at its key), places it at `BasePos + KeyPos[KeyNum]` / `BaseRot + KeyRot[KeyNum]`,
   on non-clients sets `RealPosition` and `RealRotation`, sets `ReturnGroup = Tag` when it is None, and clears
   `Leader` and `Follower`. Mover.PostBeginPlay then leaves the group leaderless when another mover has the same
   group (nobody sets `bIsLeader`), else makes it its own leader.
4. `Timer` (simulated): Mover's drift check; a client that corrects (second quiet tick) also sets
   `Rotation = RealRotation`; a server keeps both `RealPosition` and `RealRotation`.

## States

The Mover states (mover.md). None of its own.

## Constants

As Mover.

## Network

- Script block (reliable, authority): `RealRotation`, `SimOldRotRoll`, `SimOldRotYaw`, `SimOldRotPitch`, on top of
  the Mover list. The class has its own native list (`AMoverRotating`, editor 0x110ab320), not decoded in detail.
- Client start of a leg: the Mover native receive with the start rotation taken from `SimOldRot*` (estimated: the
  AMoverRotating receive is not decoded; those fields have no other reader).

## Edge cases

- Leaderless groups: a Stop or Return encroach type would call into a None leader in the script; the port's Mover
  falls back to the mover itself. No map uses Stop or Return on a MoverRotating.

## Open questions

1. Decode the AMoverRotating native receive and list (editor 0x110ab320) to confirm the start-rotation rule.
