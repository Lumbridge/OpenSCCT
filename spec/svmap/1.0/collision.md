# `sim/collision.bin` (SVMap 1.0)

Everything the server needs to collide against a map: world geometry, brushes, static meshes, movers, glass and
gameplay objects, actor cylinders and zones. Coordinates and planes follow [conventions.md](conventions.md)
(uu, left-handed, Z up, UE2 triangle and plane rules).

## Encoding

- Little-endian. `u8/u16/u32/i32` are unsigned and signed integers, `f32` is IEEE 754 binary32 (finite, never NaN).
- `none` in an index field is `0xFFFFFFFF` (`u32`) or `-1` (`i32`).
- The file is a 16-byte file header followed by chunks. Every chunk starts at a 16-byte aligned offset and is padded
  with zero bytes to the next multiple of 16, so the file size is a multiple of 16.
- All records are fixed size with no implicit padding, so a reader can map each chunk payload to an array of structs.

### File header (16 bytes)

| Offset | Type | Field | Value |
|---|---|---|---|
| 0 | `char[4]` | magic | `SVCL` |
| 4 | `u16` | major | `1` |
| 6 | `u16` | minor | `0` |
| 8 | `u32` | chunkCount | number of chunks that follow |
| 12 | `u32` | flags | `0` (reserved) |

### Chunk header (16 bytes)

| Offset | Type | Field | Meaning |
|---|---|---|---|
| 0 | `char[4]` | fourcc | chunk type, ASCII |
| 4 | `u32` | version | layout version of this chunk type (all `1` in SVMap 1.0) |
| 8 | `u32` | size | payload bytes, excluding this header and the padding |
| 12 | `u32` | count | number of records (for `STRS`: number of strings) |

The payload starts right after the header. For fixed-size records `size == count * stride`. The next chunk starts at
`align16(payloadStart + size)`.

### Chunk order and rules

A 1.0 writer emits exactly these ten chunks, once each, in this order, with `count = 0` where there is nothing to
store: `STRS VERT TRIS TFLG PLNS HULL BSPN COLL CYLS ZONE`. A reader finds chunks by fourcc, MUST reject a duplicate
of a known chunk or a known chunk with an unknown version, and MUST skip unknown chunk types (later minor versions
may add chunks). All indices are absolute positions in the target chunk, not relative to a collider.

## Chunks

### `STRS` string table

`count` strings. Payload: `u32 offsets[count]`, then the string bytes. `offsets[i]` is the byte offset of string `i`
from the first byte after the offset array. Each string is UTF-8 and ends with a zero byte. Writers add strings in
order of first use. Other chunks refer to strings by index (`u32`, `none` for no string).

### `VERT` vertices (stride 12)

| Offset | Type | Field |
|---|---|---|
| 0 | `f32[3]` | x, y, z |

Positions are in the owning collider's local space (see `COLL`). Colliders may share a vertex range (instancing).

### `TRIS` triangles (stride 16)

| Offset | Type | Field | Meaning |
|---|---|---|---|
| 0 | `u32` | i0 | `VERT` index |
| 4 | `u32` | i1 | `VERT` index |
| 8 | `u32` | i2 | `VERT` index |
| 12 | `u16` | collider | `COLL` index of the owning collider |
| 14 | `u16` | material | `TFLG` index |

Front face as in the conventions: `(v1 - v0) x (v2 - v0)`.

### `TFLG` collision materials (stride 8)

One record per distinct combination of surface type, flags and source material; triangles and hulls point here.

| Offset | Type | Field | Meaning |
|---|---|---|---|
| 0 | `u8` | surfaceType | original `ESurfaceType` value (footsteps, impacts) |
| 1 | `u8` | reserved | `0` |
| 2 | `u16` | flags | bits below |
| 4 | `u32` | name | `STRS` index of the source material asset path, or `none` |

| Bit | Flag | Meaning |
|---|---|---|
| 0 | climbable | spy climb surfaces |
| 1 | noSplitJump | split jump not allowed against it |
| 2 | noStickyCam | sticky cameras do not attach |
| 3 | semisolid | UE2 `PF_Semisolid` |
| 4 | invisible | UE2 `PF_Invisible`: collides but is not drawn |
| 5-15 | reserved | `0` |

### `PLNS` planes (stride 16)

| Offset | Type | Field |
|---|---|---|
| 0 | `f32[3]` | nx, ny, nz (unit) |
| 12 | `f32` | w |

### `HULL` convex hulls (stride 48)

| Offset | Type | Field | Meaning |
|---|---|---|---|
| 0 | `u32` | planeStart | first `PLNS` record |
| 4 | `u32` | planeCount | planes, all pointing out of the hull |
| 8 | `u32` | vertStart | first `VERT` record of the hull's corners |
| 12 | `u32` | vertCount | corners (may be 0; used for support points and debug drawing) |
| 16 | `u16` | material | `TFLG` index |
| 18 | `u16` | reserved | `0` |
| 20 | `u32` | reserved | `0` |
| 24 | `f32[3]` | min | local-space bounds |
| 36 | `f32[3]` | max | |

### `BSPN` BSP nodes (stride 48, provisional)

This layout follows UE2 `FBspNode` and becomes final when the map converter decodes the original `LeafHulls`; until
then readers MAY ignore `BSPN` and collide by triangles.

| Offset | Type | Field | Meaning |
|---|---|---|---|
| 0 | `f32[3]` | nx, ny, nz | node plane |
| 12 | `f32` | w | |
| 16 | `i32` | front | child node, `-1`: empty space in front |
| 20 | `i32` | back | child node, `-1`: solid behind |
| 24 | `i32` | coplanar | next coplanar node, `-1` |
| 28 | `i32` | collisionBound | `HULL` index of the node's collision bound, `-1` |
| 32 | `u32` | triStart | `TRIS` records of this node's surface polygon |
| 36 | `u32` | triCount | |
| 40 | `u16` | nodeFlags | original `NodeFlags` |
| 42 | `u8` | zoneFront | zone index in front |
| 43 | `u8` | zoneBack | zone index behind |
| 44 | `u32` | reserved | `0` |

### `COLL` colliders (stride 128)

| Offset | Type | Field | Meaning |
|---|---|---|---|
| 0 | `u8` | kind | table below |
| 1 | `u8` | reserved | `0` |
| 2 | `u16` | flags | table below |
| 4 | `u32` | actor | `STRS` index of the owning actor id, `none` for the level itself |
| 8 | `u32` | parent | `STRS` index of the actor this one is attached to (`attached`, based movers), `none` |
| 12 | `u32` | reserved | `0` |
| 16 | `u32` | vertStart | `VERT` range |
| 20 | `u32` | vertCount | |
| 24 | `u32` | triStart | `TRIS` range |
| 28 | `u32` | triCount | |
| 32 | `u32` | bspStart | `BSPN` range (root is `bspStart`) |
| 36 | `u32` | bspCount | |
| 40 | `u32` | hullStart | `HULL` range |
| 44 | `u32` | hullCount | |
| 48 | `f32[3]` | location | initial transform, as the conventions' actor transform |
| 60 | `i32[3]` | rotation | pitch, yaw, roll (65536 per turn) |
| 72 | `f32[3]` | scale | `DrawScale * DrawScale3D` |
| 84 | `f32[3]` | prePivot | |
| 96 | `f32[3]` | aabbMin | world-space bounds at the initial transform |
| 108 | `f32[3]` | aabbMax | |
| 120 | `u32` | reserved | `0` |
| 124 | `u32` | reserved | `0` |

Collider kinds:

| Value | Kind | Source |
|---|---|---|
| 0 | worldBsp | the level's world BSP model |
| 1 | brush | brush actors and volumes (`BlockingVolume`) |
| 2 | meshPoly | static mesh per-polygon collision |
| 3 | meshHull | static mesh simplified collision (convex hulls or its collision BSP) |
| 4 | mover | `Mover` and subclasses |
| 5 | glass | breakable glass (`SVitre`) |
| 6 | gpo | gameplay objects (`SGamePlayObject` and similar swappable or breakable meshes) |
| 7 | attached | collision attached to another actor (`parent`) |

Collider flags, from the owning actor's properties (`bCollideActors`, `bBlockActors`, ...):

| Bit | Flag |
|---|---|
| 0 | collideActors |
| 1 | blockActors |
| 2 | blockPlayers |
| 3 | blockZeroExtent |
| 4 | blockNonZeroExtent |
| 5 | blockCamera |
| 6 | bulletGoesThrough |
| 7 | startsDisabled |
| 8-15 | reserved, `0` |

Geometry rule: a collider collides by its hulls when `hullCount > 0`, else by its BSP nodes when `bspCount > 0`, else
by its triangles. A `worldBsp` collider with no nodes (synthetic and native maps) is collided by its triangles.
Vertices, planes and hull bounds are in the collider's local space and move with its transform; converters MAY bake
static geometry to world space and store an identity transform (location 0, rotation 0, scale 1, prePivot 0).

### `CYLS` actor cylinders (stride 32)

Initial collision cylinders of placed actors that collide as cylinders (triggers, pickups, interaction volumes).
Actors spawned at run time take their cylinder from their properties instead.

| Offset | Type | Field | Meaning |
|---|---|---|---|
| 0 | `u32` | actor | `STRS` index of the actor id |
| 4 | `u16` | flags | collider flag bits |
| 6 | `u16` | reserved | `0` |
| 8 | `f32[3]` | location | cylinder centre |
| 20 | `f32` | radius | `CollisionRadius` |
| 24 | `f32` | height | `CollisionHeight` (half height, as UE2) |
| 28 | `u32` | reserved | `0` |

### `ZONE` zones (stride 32)

Record `i` describes zone index `i`; zone 0 is the default zone (the `LevelInfo`).

| Offset | Type | Field | Meaning |
|---|---|---|---|
| 0 | `u32` | actor | `STRS` index of the `ZoneInfo` (or `LevelInfo` for zone 0), `none` |
| 4 | `u32` | flags | bit 0 skyZone, others reserved `0` |
| 8 | `f32[3]` | aabbMin | zone bounds |
| 20 | `f32[3]` | aabbMax | |

Maps with BSP nodes find a point's zone through the BSP. Maps without, find it as the first zone from index 1 whose
bounds contain the point, else zone 0.

## Validation

`svmbuild` rejects a file when: the magic, major version or a known chunk version is wrong; a known chunk is
missing, duplicated or out of order (unknown chunks are skipped); offsets are not 16-byte aligned or padding is not
zero; `size` does not match `count`; any index or range is out of bounds; a triangle's `collider` does not own it
(outside that collider's `triStart` range); a float is not finite; a string is not valid UTF-8; an actor name is not
an actor id in `sim/gameplay.json`.
