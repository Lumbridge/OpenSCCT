# SVMap 1.1 conventions

Unchanged from 1.0. The only 1.1 addition is the `propPath` form used by event edges (README.md).

These rules apply to every file of an SVMap. The key words MUST, SHOULD and MAY are used in their usual sense.

## Text files

- UTF-8 without a byte order mark, LF line endings.
- JSON files are RFC 8259 JSON. Duplicate object keys are an error. `NaN`, `Infinity` and `-Infinity` are errors.
- Object keys in `sim/` JSON MUST be ASCII. Writers SHOULD pretty-print with sorted keys and a trailing newline; the
  hash rule below does not depend on layout.

## Simulation space (`sim/`)

The server reads only `sim/`, so everything there uses the original engine's conventions unchanged.

- **Units:** Unreal units (uu). One uu is treated as one centimetre for display only.
- **Axes:** left-handed, Z up. X forward, Y right, Z up, as in UE2.
- **Floats:** IEEE 754 binary32. A float-typed JSON value is written with a decimal point (`10.0`, not `10`) using
  the float32 text form below. Integers never have a decimal point.
- **Rotators:** `{"Pitch": int, "Yaw": int, "Roll": int}`, 65536 units per full turn, stored exactly as authored
  (not normalised; values outside 0..65535 occur in original maps and are kept).
- **Rotation matrix:** UE2 `FRotationMatrix`, row-vector convention (`P' = P * M`). With `SP, CP, SY, CY, SR, CR` the
  sine and cosine of pitch, yaw and roll (from the sim's UE2 sine table, never a libm call):

  | Row | X | Y | Z |
  |---|---|---|---|
  | X axis | `CP*CY` | `CP*SY` | `SP` |
  | Y axis | `SR*SP*CY - CR*SY` | `SR*SP*SY + CR*CY` | `-SR*CP` |
  | Z axis | `-(CR*SP*CY + SR*SY)` | `CY*SR - CR*SP*SY` | `CR*CP` |

  Yaw 16384 turns local +X into world +Y and local +Y into world -X.
- **Actor transform:** local to world as UE2 `AActor::LocalToWorld`:
  `world = ((local - PrePivot) * Scale) * M(Rotation) + Location`, with `Scale = DrawScale * DrawScale3D`
  (component-wise) and the multiplication by `M` in the row-vector form above.
- **Triangles:** the front-face normal of `(v0, v1, v2)` is `(v1 - v0) x (v2 - v0)` with the ordinary component formula
  (`x = ay*bz - az*by`, `y = az*bx - ax*bz`, `z = ax*by - ay*bx`), the UE2 `FPoly` rule. For a closed solid it
  points out of the solid.
- **Planes:** `(nx, ny, nz, w)` with a unit normal and `w = n . p` for any point `p` on the plane, as UE2 `FPlane`.
  The signed distance is `n . p - w`. Hull planes point out of the hull: `p` is inside when every distance is <= 0.

## Render space (`render/`)

- glTF 2.0, metres, right-handed, Y up.
- Positions: UE `(x, y, z)` becomes glTF `(x / 100, z / 100, y / 100)`. Directions and normals: `(x, z, y)`, not scaled.
- The swap of Y and Z is a reflection, so triangle winding flips: UE `(a, b, c)` is written as glTF `(a, c, b)`. glTF
  front faces are then counter-clockwise as the glTF spec requires and face the same way as in UE.
- The client converts in one place (`UeSpace`) and never mixes the two spaces.

## Names and ids

- **Actor ids** are the original export names (`SFlag28768`, `PlayerStart254`), unique within a map. Native maps use
  the same `ClassN` style (`SFlag0`). Pattern `^[A-Za-z_][A-Za-z0-9_]*$`, at most 63 characters (UE2 `NAME_SIZE`).
  UE2 names are case-insensitive, so ids MUST also be unique ignoring ASCII case.
- **Classes** are `Package.Class` with the original names (`Engine.Mover`, `SBase.SComputerObjectiveTrigger`).
- **Assets** are `Package.Group.Name` with every group level (`SteeE_tex.walls.brick01`), at least `Package.Name`.
  An asset whose package is the map id refers to the map's own `render/` library.
- **Properties** keep their UnrealScript names and casing (`DominationCaptureTime`, `bTriggerOnceOnly`).
- **Tags and events** match the way UE2 `FName` does: ignoring ASCII case. Converters write the effective `Tag` of
  every actor, so the tag index never needs class defaults.
- **GE ids** are `GE<index>` for converted maps, where `<index>` is the position in the original level's GE list.
  Native maps may use any id matching the actor id pattern.

## float32 text form

Every float-typed value is a binary32 number `f` written as:

1. the shortest decimal digit string that reads back as exactly `f` under round-to-nearest-even (when two strings of
   that length qualify, the one nearer to `f`), then
2. laid out positionally: no exponent, at least one digit on each side of the point, `-` for negative values
   including `-0.0`, no `+`.

| binary32 bits | text |
|---|---|
| `0x3dcccccd` | `0.1` |
| `0x3eaaaaab` | `0.33333334` |
| `0x42c80000` | `100.0` |
| `0x33d6bf95` | `0.0000001` |
| `0x4ceb79a3` | `123456790.0` |
| `0x80000000` | `-0.0` |
| `0x7f7fffff` | `340282350000000000000000000000000000000.0` |
| `0x00000001` | `0.000000000000000000000000000000000000000000001` |

In C#, `float.ToString("R", CultureInfo.InvariantCulture)` gives the shortest digits; re-lay them out positionally.
Reading uses a correctly rounded decimal to binary32 conversion (`float.Parse` with the invariant culture), never a
parse to double followed by a cast. `vectors/canonical.json` lists more cases for tests.

## Canonical sim hash

The `sim` hash identifies the simulation content of a map. Client and server compare it in the handshake, and the
Python pipeline and the C# reader MUST agree on it.

1. List every file under `sim/`, recursively. Its path is relative to the map root with `/` separators
   (`sim/gameplay.json`). Sort the paths by their UTF-8 bytes.
2. For each file compute its **file digest**, lowercase hex SHA-256 of its canonical bytes:
   - `.json` files: parse, then re-serialise canonically:
     - objects with keys sorted by their (ASCII) bytes, separators `,` and `:` and no whitespace anywhere;
     - strings as UTF-8, escaping only `"` as `\"`, `\` as `\\`, U+0008 `\b`, U+0009 `\t`, U+000A `\n`,
       U+000C `\f`, U+000D `\r`, and the other code points below U+0020 as `\u00xx` (lowercase hex); all other
       characters, including `/`, U+007F and non-ASCII, are written as themselves;
     - a number token with `.`, `e` or `E` is a float: round it to binary32 (round-to-nearest-even, directly from the
       decimal) and write it in the float32 text form; any other number token is an integer, written in decimal;
     - `true`, `false`, `null` as themselves;
     - array order unchanged; no trailing newline.
   - every other file (`collision.bin`): its bytes as they are.
3. Build the **group listing**: one line per file, in the order of step 1, `<file digest>  <path>\n` (two spaces,
   LF, the format of `sha256sum`).
4. The **sim hash** is lowercase hex SHA-256 of the UTF-8 group listing.

The same procedure gives the `render`, `audio` and `ui` hashes over their folders, except that only `sim/` JSON is
canonicalised: files in the other groups are always hashed as raw bytes. `manifest.json` lists every file digest and
every group hash; it is not itself part of any group.
