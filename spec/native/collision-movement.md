# Moving actors with collision: MoveActor, FarMoveActor, encroachment and crush

Units:
- eng:ULevel::MoveActor
- eng:ULevel::FarMoveActor
- eng:ULevel::CheckEncroachment
- eng:FCollisionHash::ActorEncroachmentCheck
- eng:AActor::IsBlockedBy (vtable 0x70, used)
- eng:AActor::IsOverlapping (vtable 0xbc, used)

## Purpose

The one function that moves an actor through the world (sweeping its cylinder), the teleport used by root-motion
ends and stance changes, and the encroachment checks movers use to push or crush what they move into.

## Data

| Field | Owner | Use |
|---|---|---|
| bCollideActors, bCollideWorld | Actor | which part of the world the sweep sees |
| bBlockActors, bBlockPlayers | Actor | blocking for IsBlockedBy and touch rules |
| bIsAMover, StaticMesh | Actor | static-mesh movers are encroachers, not sweepers |
| bTryToMoveEncroachedActor | Actor | movers first try to shove what they hit |
| Attached, Base, RelativeLocation | Actor | attached actors move with the actor |
| Touching | Actor | kept in sync with overlaps |

## Rules

### MoveActor(Actor, Delta, NewRotation, Hit, bTest, bIgnorePawns, bIgnoreBases, bNoFail) -> bool

1. Zero Delta: nothing when the rotation is unchanged; with a changed rotation and no attached actors, rotate
   (when the primitive allows) and return true.
2. An actor with neither bCollideActors nor bCollideWorld (and no attached actor needing care): add Delta, set the
   rotation, return true.
3. Sweep: when (bCollideActors or bCollideWorld), the actor is not a static-mesh mover with bCollideActors, and
   |Delta| > 0 (nearly-zero deltas count as 0): MultiLineCheck from Location to Location + Delta with the
   cylinder extent (R, R, H), flags as in collision-traces.md, LevelInfo when bCollideWorld. With any blocking
   flag (bCollideWorld, bBlockActors or bBlockPlayers), Hit = the first result that is not a base relation
   (skipped when bIgnoreBases and the actor is based on it, or when it is based on the actor) and that blocks the
   actor (IsBlockedBy).
4. Hit.Time < 1 and not bNoFail: Delta *= Hit.Time.
5. A static-mesh mover with bCollideActors (not a test, not bNoFail): CheckEncroachment at the new location and
   rotation; encroached: return false without moving.
6. Move: out of the hash, Location += Delta, Rotation = NewRotation. Attached actors move along (based pawns are
   left to their own physics); a non-test, non-bNoFail move of an attached actor that is blocked and refused by
   EncroachingOn undoes the whole move and returns false. Update RelativeLocation and relative rotation for a
   based actor. Back into the hash.
7. Unless bTest: bump the blocking hit (Hit.Actor not bWorldGeometry and not a base relation: Hit.Actor.Bump(actor),
   actor.Bump(Hit.Actor)). Touches: when step 3 met a non-base hit, or the actor is not bBlockActors, or not
   bBlockPlayers, every swept hit closer than Hit.Time that the actor is not blocked by (and that is not a base
   relation under bIgnoreBases, nor the actor itself) gets BeginTouch. Then EndTouch every touching actor that no
   longer overlaps.
8. SetZone; return Hit.Time > 0.

The last argument some callers pass (an out vector) is filled with zero in the editor build.

### FarMoveActor(Actor, Dest, bTest, bNoCheck, bAttachedMove) -> bool

1. Out of the hash.
2. Unless bNoCheck, when bCollideWorld, or bCollideWhenPlacing and not a network client: FindSpot(cylinder, Dest)
   may adjust Dest; failure means no move.
3. Unless bTest or bNoCheck: CheckEncroachment(Dest, current rotation, touch notify); encroached means no move.
4. When the actor was not moved by those checks (or bTest), and the result is good: unless bTest set
   bJustTeleported, SetBase(None) unless bAttachedMove, and move attached actors along; Location = Dest (also
   with bTest).
5. Back into the hash; SetZone on success.

### CheckEncroachment(Actor, Location, Rotation, bTouchNotify) -> bool (true = refused)

For an actor with bCollideActors (and blocking): a static-mesh mover temporarily takes the test location and
rotation to build its collision; FCollisionHash::ActorEncroachmentCheck with mask 0x9f lists overlapped actors.
For each overlapped actor other than the level that blocks the encroacher:
1. Shove (bTryToMoveEncroachedActor, bCollideActors, static-mesh mover, the other not a projector): move the other
   by the encroacher's displacement (plus 1.5 times the rotation-induced offset of its position when the rotation
   changes) with moveSmooth; when it now overlaps no longer, the encroacher continues; when it still overlaps,
   the other is moved back from the new location.
2. Otherwise the encroacher's EncroachingOn(Other) decides: true refuses the move (return true).
After the loop: with bTouchNotify, end touches no longer overlapping; then for every overlapped actor that blocks
the encroacher: EncroachedBy(Actor) on it (probe-masked), and BeginTouch for those that do not block.

Crushing is script: movers react in EncroachingOn (by their encroach type) and pawns in EncroachedBy
(spec/devices/mover.md).

## States

None.

## Constants

| Name | Value | Provenance |
|---|---|---|
| encroachment mask | 0x9f | decoded |
| rotation shove factor | 1.5 | decoded |
| sweep extent | the cylinder (R, R, H) | decoded |

## Network

Movers run on every machine; encroachment events that damage are filtered by the scripts' role checks.

## Edge cases

- Pawns sweep as boxes of half extent (R, R, H), not as cylinders; the targets they hit answer with their own
  shapes (other pawns as cylinders).
- MoveActor returns true even for a partial move (Time > 0); callers use Hit.Time.

## Evidence

- Editor: MoveActor 0x110bc870, FarMoveActor 0x110bed60, CheckEncroachment 0x110bda90,
  FCollisionHash::ActorEncroachmentCheck 0x1115f210, AActor::IsBlockedBy 0x110a7af0, AActor::IsOverlapping
  0x110a77d0, FindSpot 0x110bea20.
- Game: MoveActor 0x10a1b780, CheckEncroachment 0x10a1c8d0.

## Open questions

1. FindSpot (editor 0x110bea20) and ActorEncroachmentCheck internals are taken as UE2-standard.
2. Whether the game build's MoveActor fills the out vector with a real remainder (the editor's is always zero).
