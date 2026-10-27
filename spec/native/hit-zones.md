# Hit zones: TraceBone, the bone pills and the approximate pill

Units:
- nat:Engine.Actor.TraceBone
- uc:SBase.SBasePawn:GetApproxPillFromHit
- eng:USkeletalMesh::LineCheck
- eng:USkeletalMesh::Serialize (collision points, capsules, spheres)

## Purpose

How a bullet trace turns into a pill (hit zone) number 1 to 15, the index (minus one) into the 15-entry
DamageLookupTable and DamageSnipeLookupTable of combat/hit-zones-and-weapons.md. The native TraceBone does a line
trace whose pawn hits are tested against per-bone capsules and spheres stored in the skeletal mesh; the script
GetApproxPillFromHit is the fallback used when no pill is known.

## Data

Skeletal mesh collision data (USkeletalMesh, serialized after the two name arrays and the 0x30-byte table, only for
package versions above 124):

| Table | Element | Meaning |
|---|---|---|
| Collision points | bone index (int), offset (3 floats) | a point in the bone's space; the mesh instance keeps their world positions for the current pose (instance +0x394, 12 bytes each) |
| Capsules | point A, point B (ints), radius (float), pill tag (int), priority (int, from licensee version 9; 1 before) | a capsule between two collision points |
| Spheres | point (int), radius (float), pill tag (int), priority (as above) | a sphere around one collision point |

The four character meshes of SPerso.ukx (spy ATT_01, merc DEF_01 and their PS2 copies; package version 175,
licensee 0, so every priority is 1) carry the same table: 26 collision points, 13 capsules and 2 spheres:

| Pill | Shape | Between | Radius (uu) | DamageLookupTable (%) |
|---|---|---|---|---|
| 1 | capsule | two points on B Head | 9.0 | 300 |
| 2 | capsule | B Spine2 to B Pelvis, left side (bone-space Z offsets +6.21 and +4.76) | 12.5 | 100 |
| 3 | capsule | B L UpperArm to B L ForeArm | 7.0 | 40 |
| 4 | capsule | B L ForeArm, elbow to wrist | 6.5 | 40 |
| 5 | sphere | B L Hand | 7.0 | 20 |
| 6 | capsule | B L Thigh to B L Calf | 9.5 | 100 |
| 7 | capsule | B L Calf to B L Foot | 7.5 | 100 |
| 8 | capsule | B L Foot to B L Toe0 | 5.5 | 20 |
| 9 | capsule | B Spine2 to B Pelvis, right side (Z offsets -6.21 and -4.76) | 12.5 | 100 |
| 10 | capsule | B R UpperArm to B R ForeArm | 7.0 | 40 |
| 11 | capsule | B R ForeArm, elbow to wrist | 6.5 | 40 |
| 12 | sphere | B R Hand | 7.0 | 20 |
| 13 | capsule | B R Thigh to B R Calf | 9.5 | 100 |
| 14 | capsule | B R Calf to B R Foot | 7.5 | 100 |
| 15 | capsule | B R Foot to B R Toe0 | 5.5 | 20 |

So pills 1 to 5 and 6 to 8 are the head, left torso, left arm and LEFT leg, 9 to 12 the right torso and right arm,
13 to 15 the RIGHT leg (the B L / B R bones of the skeleton). The extraction tool is
re/ghidra/scripts/actions/pills.py; its output (with the point offsets) stays in local/.

## Rules

### 1. TraceBone (native 1122)

Script signature: `TraceBone(out bIsAFakeBackdrop, out PillTag, out HitLocation, out HitNormal, TraceEnd, optional
TraceStart, optional out Material, optional bRealFire)`, returns the hit Actor.

1. TraceStart defaults to the caller's Location.
2. Trace flags: 0x40097 (Pawns, Movers, Level, Others, LevelGeometry and the pill bit 0x40000); bRealFire adds
   0x20000 (no SingleResult, so pass-through hits are reported through BulletWentTru, collision-traces.md); asking
   for Material adds 0x1000.
3. First pass: SingleLineCheck(Start to End, zero extent) with those flags and the caller as source.
4. Second pass, over every pawn in the level's pawn list except the caller and the actors the caller is based on
   (the Base chain): when the distance from the pawn's Location to the trace segment is below 3 x the pawn's
   CollisionHeight, its primitive (the skeletal mesh) runs the pill line check of rule 2 with the same flags; a
   hit replaces the current result when its hit location is closer to the trace start.
5. Outputs: the returned Actor, HitLocation and HitNormal are the result's; PillTag is the result's pill field
   (+0x30 of the check result: 0 when no pill was hit, for world geometry, movers and cylinder hits); Material is
   the result's material; bIsAFakeBackdrop is 1 only when the hit primitive is the BSP model and the hit surface
   has poly flag 0x80 (fake backdrop), else 0.
6. A BSP hit returns the LevelInfo (the level's hit actor); a trace that hits nothing at all returns None (the
   native has no fallback), so script callers that dereference the result assume the 15000 uu trace always ends in
   the world.

### 2. Pill line check (USkeletalMesh::LineCheck, editor 0x111090a0)

1. Without the pill bit (0x40000) in the trace flags, or with no capsules and no spheres, the generic primitive
   check runs (the bounding cylinder or box).
2. Otherwise the mesh instance is updated for the actor (its current pose) and the collision point world positions
   refreshed.
3. For each capsule: if the segment-to-segment distance between the trace and the capsule axis is below its
   radius, the exact entry time t of the trace into the capsule is computed; for each sphere: if the point's
   distance to the trace segment is below its radius, the entry time is computed and normalised by the trace
   length.
4. The best hit is the one with the highest priority, then the smallest t. With all priorities 1 (the shipped
   characters) this is simply the nearest pill along the trace.
5. On a hit (t below 1.001): Time = t, Location = Start + t (End - Start), Normal = the unit vector from the
   closest point of the capsule axis (or the sphere centre) to the hit location, Actor = the owner, Primitive = 0,
   and the pill tag in the result's pill field. The function then reports a hit; otherwise no hit.

### 3. Approximate pill (script GetApproxPillFromHit)

Restated here for completeness; it does not use the mesh. Y = the pawn's right axis; d = horizontal unit vector
from the pawn to HitLocation; dotY = d . Y; p = (HitLocation.Z - Location.Z) / CollisionHeight. p > 0.68 gives
P_Head (0); 0 <= p <= 0.68 gives P_ChestL (3) when dotY < 0, else P_ChestR (4); p < 0 gives P_LegL (7) when dotY < 0,
else P_LegR (8). The damage pipeline adds 1 and uses the 15-pill numbering, so the approximate pills land on pills
1, 4, 5, 8 and 9 (head, left forearm, left hand, left foot, right torso), which do not match their EDamageLocation
names; combat/hit-zones-and-weapons.md rule 2 says to reproduce this.

## States

None.

## Constants

| Name | Value | Unit | Provenance | Source |
|---|---|---|---|---|
| TraceBone flags | 0x40097, +0x20000 real fire, +0x1000 material | bits | decoded | editor 0x110ac5d0 |
| Pawn pass radius | 3 x CollisionHeight | uu | decoded | editor 0x110ac5d0 |
| Pill acceptance | t < 1.001 | - | decoded | editor 0x111090a0 |
| Pill table | above | uu | decoded | SPerso.ukx through pills.py |
| Approximate head threshold | 0.68 of CollisionHeight | - | decoded | script |

## Network

TraceBone runs where the shot is resolved: on the server for the merc rifle and Uzi (TraceFire) and for the spy
shocker's server fire. The pose used is the server's copy of the target pawn's animation, so the pill depends on the
server-side animation state of the target (its actions arrive with its moves, execute-action.md).

## Edge cases

- A pill is only found when the target pawn's pose puts a capsule on the trace; a shot between the legs that is
  inside the collision cylinder hits nothing in the pill pass and may hit the world behind.
- The second pass checks neither bCollideActors nor bHidden: every pawn in the level's pawn list is tested,
  including a dead pawn whose body is still in the list.
- Pills extend beyond the collision cylinder (arms, feet); the 3 x CollisionHeight gate keeps them reachable.

## Evidence

- Editor: execTraceBone 0x11189830, TraceBone core 0x110ac5d0, USkeletalMesh::LineCheck 0x111090a0 (vtable
  0x11490280 slot +0x64), USkeletalMesh::Serialize 0x111137a0 (collision points 0x11103ff0, capsules 0x11104150
  with elements 0x110faa40, spheres 0x11104250), actor GetPrimitive slot +0xb8 (0x110a7a80), FCollisionHash
  ActorLineCheck 0x1115f5a0.
- Game: execTraceBone 0x10a25270.
- Data: SPerso.ukx skeletal meshes ATT_01, DEF_01 (and _PS2), read with re/ghidra/scripts/actions/pills.py.

## Open questions

1. Whether the hash pass (rule 1.3) forwards the pill bit to the pawn's mesh check (the call passes the flags as
   its last argument, by reading of FCollisionHash::ActorLineCheck); if not, only the second pass finds pills.
2. The order of the pawn list of rule 1.4 (it decides nothing when hits differ in distance, only for exact ties).
