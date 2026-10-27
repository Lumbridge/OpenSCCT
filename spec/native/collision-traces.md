# Collision traces: trace flags, SingleLineCheck, MultiLineCheck, point checks, the actor hash

Units:
- eng:ULevel::SingleLineCheck
- eng:ULevel::MultiLineCheck
- eng:ULevel::SinglePointCheck
- eng:ULevel::MultiPointCheck
- eng:FCollisionHash::ActorLineCheck
- eng:FCollisionHash::ActorPointCheck
- eng:AActor::ShouldTrace
- eng:APawn::ShouldTrace
- nat:Engine.Actor.Trace (script entry over SingleLineCheck)

## Purpose

The query layer every physics function uses: a box (or zero-extent) sweep from Start to End that returns the first
blocking hit, or all hits, against the BSP and the actors in the collision hash, filtered by trace flags.

## Data

FCheckResult (0x38 bytes): Next, Actor, Location, Normal, Primitive, Time (1 = no hit), Item (BSP node or mesh
triangle, -1 none), Material, a second index (+0x30, mesh material slot), and a "passes through" word (+0x34)
that SingleLineCheck uses for bullet-penetrable hits.

Trace flags of this build (bit names follow UE2.5; meanings decoded from the checks that read them):

| Bit | Name | Effect |
|---|---|---|
| 0x1 | Pawns | pawns are candidates (APawn::ShouldTrace returns this bit) |
| 0x2 | Movers | with 0x1, 0x8, 0x10 and 0x80 makes up the hash mask 0x9b |
| 0x4 | Level | test the BSP of LevelInfo's level |
| 0x8 | Volumes | hash mask |
| 0x10 | Others | non-pawn, non-world actors |
| 0x20 | OnlyProjActor | with Others: only bProjTarget actors, or bBlockActors and bBlockPlayers |
| 0x40 | Blocking | with Others (and for pawns): only actors the source IsBlockedBy |
| 0x80 | LevelGeometry | bWorldGeometry actors (static meshes); for a pawn source with a static mesh actor and 0x80, the actor counts only when bBlockPlayers |
| 0x100 | ShadowCast | only bShadowCast actors; static meshes use their per-poly triangles; BSP surfaces with poly flag 0x1 are skipped by SingleLineCheck |
| 0x200 | StopAtFirstHit | stop after the BSP hit or the first actor hit (clearance tests) |
| 0x400 | SingleResult | added by SingleLineCheck unless 0x20000 is set |
| 0x1000 | Material | the BSP returns the surface material |
| 0x2000 | visible mesh | actors drawn as a static mesh (DrawType 8) or mesh (2) count |
| 0x8000 | AcceptProjectors | only bAcceptsProjectors actors |
| 0x10000 | camera | the hit actor needs bBlockCamera |
| 0x20000 | sight | SingleLineCheck does not add SingleResult |
| 0x80000 | backdrop skip | with 0x100: SingleLineCheck skips BSP hits on surfaces with poly flag 0x80 (also with 0x100 alone) |

Masks used by the physics natives: MoveActor (bCollideActors ? (bIgnorePawns ? 0x18 : 0x19) : 0) |
(bCollideWorld ? 0x86 : 0); walking and falling wall pre-probes 0xd6; floor probes 0xdf; clearance tests 0x2df;
pawn-and-mover-only clearance 0x243 (no BSP, no static meshes); landing probe 0x297; falling side probes 0x97;
drop-attack line of sight 0x29f; HOH legs 0x201; CheckFence 0x1086; CheckGEForWallJump 0x10df; barrier 0x287
(0x286 with a second GE); encroachment 0x9f.

## Rules

### SingleLineCheck(Hit, Source, End, Start, Flags, Extent) -> bool (true = nothing hit)

1. Flags |= 0x400 unless 0x20000 is set. LevelInfo = the level's LevelInfo when Flags has 0x4.
2. Hits = MultiLineCheck(End, Start, Extent, LevelInfo, Flags, Source), sorted by Time.
3. Walk the list in order. With Flags & 0x80100, a hit on the level (Actor == Actors[0]) whose BSP surface has a
   poly flag in `((Flags & 0x100) | 0x8000) >> 8` is skipped. A hit with its pass-through word set is reported
   to Source as BulletWentTru(hit location, normal, ...) and skipped. The first other hit is copied to Hit and the
   function returns false.
4. No hit left: Hit.Time = 1, Hit.Actor = None, return true.

### MultiLineCheck(Mem, End, Start, Extent, LevelInfo, Flags, Source)

1. Up to 64 hits are gathered.
2. Level first (Flags & 0x4 and a LevelInfo): the BSP model's LineCheck(Hit, None, End, Start, Extent, node flags
   0). On a hit: it is hit 0 with Actor = LevelInfo; t = min(Time, 1); End is pulled to Start + (End - Start) t; with
   Flags & 0x200 the actor phase is skipped.
3. Actors (Flags & 0x9b and a hash): FCollisionHash::ActorLineCheck(End, Start, Extent, Flags, Source); each
   result's Time is multiplied by t so that times stay relative to the original segment.
4. Sort by Time (quick sort, compare on Time), clamp Time to at most 1, link and return.

### FCollisionHash::ActorLineCheck(End, Start, Extent, Flags, ExtraNodeFlags, Source)

- Non-zero extent: every hash cell overlapped by the swept box is visited once (a per-query tag on the actor
  prevents repeats). Candidates need bBlockNonZeroExtentTraces, must not be Source or any Owner of Source, and must
  pass ShouldTrace(Source, Flags); their primitive's LineCheck (actor, End, Start, Extent, Flags) decides the hit.
- Zero extent: the cells along the segment are walked in order from Start (a 3D grid walk); candidates need
  bBlockZeroExtentTraces; with Flags & 0x200 the walk stops at the first hit; with 0x400 it stops when the
  nearest hit so far lies in the current cell.

### ShouldTrace(Source, Flags)

AActor:
1. Not traced when Flags has 0x100 and the actor is not bShadowCast; when Source is bOnlyAffectPawns (only pawns
   are hit then, see the pawn rule); when the actor is bOnlyAffectPawns and Source is not a Pawn; when Flags has
   0x10000 and the actor is not bBlockCamera.
2. Flags & 0x8000: return bAcceptsProjectors. Flags & 0x2000: static mesh or mesh drawn actors return true.
   Flags & 0x100: return bShadowCast.
3. bWorldGeometry actors: return Flags & 0x80, except a static mesh actor tested for a pawn Source with 0x80, which
   returns bBlockPlayers.
4. Flags & 0x10: without 0x20 and 0x40 true; with 0x40, true when Source IsBlockedBy this actor; with 0x20, true
   for bProjTarget or (bBlockActors and bBlockPlayers).
5. Otherwise false.

AVolume (game 0x10991de0, the Volume and BlockingVolume vtable slot +0xac; AActor's is 0x10991c40): the same first
gates (shadow, bOnlyAffectPawns both ways); a bWorldGeometry volume returns true with 0x80; otherwise false without
0x8; with 0x20 true only for bProjTarget or (bBlockActors and bBlockPlayers); else with 0x40 true when Source
IsBlockedBy the volume; else true. So OnlyProjActor traces (script Trace with actors, the sticky cam line) pass
through zone, sound, physics, damage and sensor volumes, and a BlockingVolume is seen by 0x80 as world geometry.

APawn: the same first gates (0x100 and bShadowCast, bOnlyAffectPawns); with 0x40 the Source must be blocked by the
pawn; 0x8000 gives bAcceptsProjectors; 0x2000 with a static mesh drawn pawn gives true; else Flags & 0x1.

### SinglePointCheck(Hit, Location, Extent, ExtraNodeFlags, LevelInfo, bActors) -> bool (true = free)

MultiPointCheck with the level's BSP (point check of the model, Actor = LevelInfo) and, with bActors, the hash
(ActorPointCheck with mask 0x9f; MultiPointCheck's world-only option, not used by SinglePointCheck, gives 0x86).
Free when no result; otherwise Hit = the result whose Location is nearest to Location and the function returns
false.

## States

None.

## Constants

| Name | Value | Provenance |
|---|---|---|
| hit buffer | 64 | decoded |
| hash actor mask | 0x9b | decoded |
| point-check masks | 0x9f (0x86 world only) | decoded |
| trace flag table | above | decoded from the readers |

## Network

None: the same on every machine.

## Edge cases

- The pawn wall pre-probes (0xd6) do not see pawns; MoveActor does (0x19).
- Clearance tests with 0x243 see only pawns and movers: a wall or a static mesh does not block them.
- A zero-extent StopAtFirstHit query can return an actor that is not the nearest when two lie in the same cell
  (cell order, then hash bucket order).

## Evidence

- Editor: SingleLineCheck 0x110be450, MultiLineCheck 0x110bc2b0, SinglePointCheck 0x110be1d0, MultiPointCheck
  0x110bbff0, FCollisionHash::ActorLineCheck 0x1115f5a0, ActorPointCheck 0x1115e950, AActor::ShouldTrace
  0x110ac870 (vtable 0xb4), APawn::ShouldTrace 0x110dc370.
- Game: SingleLineCheck 0x10a1d200 (called with 0x2df by the ledge test 0x10aa4420; the backdrop skip reads the hit
  node's surface: Nodes[Item].iSurf, Surfs[iSurf].PolyFlags, and the mask is built as in rule 3), MultiLineCheck 0x10a1d400;
  AVolume::ShouldTrace 0x10991de0;
  ActorLineCheck is 0x109b5f60 or 0x109b5e50 (fingerprint tie).

## Open questions

1. Which bits SCCT's own code sets for 0x1000, 0x2000, 0x10000 and 0x80000 (fire, camera, sound and vision
   traces) belongs to the combat and camera specs; only the readers are decoded here.
2. The hash cell size and bucket function (editor 0x1115e600) are not decoded; they only change the order of equal
   results for StopAtFirstHit queries.
