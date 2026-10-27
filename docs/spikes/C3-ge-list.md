# C3: GE list (traversal data) in the Level export

**Verdict: GO.** The GE list is fully decoded from the packages. All 83 shipped maps decode to the
last byte of their Level export, the SteeE truth matches exactly, and the 75 maps that also ship in
`Packages/MapsEd` give identical GE lists.

Date: 2026-10-23. Decoder: `re/formats/scctformats/ge.py` (`python -B -m scctformats.ge all --census
--compare MapsEd`). Output: `local/formats/ge/<Map>.json` and `local/formats/ge/_summary.json`.

## Format (package version 300)

The GE partition the engine keeps at `ULevel+0x39e8` is serialized at the end of the Level export.
Field order of the Level export, as read:

| Field | Encoding |
|---|---|
| tagged properties | `None` only |
| Actors | i32 Num, i32 Max, Num object refs (Actors[0] is the LevelInfo) |
| URL | 4 FStrings (Protocol, Host, Map, Portal), compact count + FStrings (Op), i32 Port, i32 Valid |
| Model A | i32 flag, object ref when the flag is set (`ULevel+0x144`) |
| Model B | i32 flag, object ref when the flag is set (`ULevel+0x14c`); the loaded Model is A when present, else B. Cooked maps store only B, the MapsEd copies store both |
| ApproxTime | f32 |
| FirstDeleted | object ref |
| TextBlocks | 16 object refs |
| TravelInfo | compact count + (FString, FString) pairs |
| GE partition | below (package version > 123) |

There are no ReachSpecs in this build's Level.

GE partition:

| Field | Encoding |
|---|---|
| partition version | i32, 4 in every map (older versions are loaded by another routine and the tree rebuilt; not supported, never seen) |
| bounds | 2 x FVector: the exact box of the first two points of every GE (a pipe's or ladder's side point is not included; verified on all 76 maps with GEs) |
| GEs | compact count, then one record per GE |
| index list | compact count, then i32 GE indices, referenced by the tree leaves |
| search tree | pre-order nodes, see below |

GE record:

| Field | Encoding | Notes |
|---|---|---|
| kind | i32 | POLE 1, FENCE 2, HOH 4, LG 8, LADDER 16, PIPE 32, ZL 64 |
| flags | i32 | the editor sets 0x2 on a GE it adds and a shared virtual clears it (most likely its selection bit); 26 GEs on 8 maps still carry it; no other bit is ever set |
| platform | i32 | version >= 170; 0 on every GE |
| ID | u8 | version >= 174; 0 = always present, else present while `ID & LevelInfo.GEActivationMask` |
| points | 2 or 3 FVectors | 3 for LADDER and PIPE (bottom, top, side), 2 for the rest |

Tree node: i32 type. 1 = empty child, nothing follows. 2, 4, 8 = split on X, Y, Z: f32 split value,
then the low child, then the high child. 16 = leaf: i32 first, i32 end, a half-open range of the
index list. The axis mapping and child order were checked on every map: every GE in a leaf has a
point on the correct side of every split above it (0 violations; swapping X and Y gives 2162
violations on SteeE, swapping the children 3914). Every GE is reachable from some leaf on every map.
The editor drops this tree when a GE is added and the game builds it again, so a port can rebuild it
from the GE list instead of reading it.

Evidence: the editor's GE record serializer (ChaosTheory_Editor.exe `0x113059b0`: kind, `+8` flags,
`+0xc` platform from version 0xaa, `+0x10` ID byte from version 0xae, then each kind's own point
serializer), the GE array (`0x11306f60`), the partition (`0x11308370`), the tree nodes (`0x113092e0`)
and `ULevel::Serialize` (`0x111293d0` to `0x11129704`, partition only when version > 0x7b). The GE
add routine (`0x11307d40`) sets `+8` to 2; a shared virtual (`0x11305970`) clears bit 2.

## SteeE against the known truth

| Check | Expected | Decoded |
|---|---|---|
| count | 228 = 220 LG + 6 PIPE + 2 ZL | 228 = 220 LG + 6 PIPE + 2 ZL |
| LevelInfo.GEActivationMask | 4 | 4 (stored on the map) |
| ID-4 GE | the crane wall-top ledge near (-3186, 56..1088, -808) | GE 153, LG (-3186.0, 1087.8, -808.0) to (-3186.0, 56.1, -808.0), the only GE with an ID |
| pipes X/Y | (-3312,-53), (-3312,1236), (-3312,2384), (-2279,2256), (-1918,1456), (-3417,-1648) | all six, bottom z -806 / -1128 / -1192, top z -168 / 152, side point 10 uu out |
| zips | (-1822,-319,-184) to (-2324,1472,131); (-2814,1468,140) to (-2844,-320,458) | GE 13 and GE 29, same points to 1 uu |
| SGETrigger | clears mask bit 4 | `SGETrigger271`, Tag `blast01`, GEMask 4, bToggle True |

SteeE's tree: 286 leaves, 56 empty children, 127 X, 119 Y and 95 Z splits, depth 19, 473 index entries.

## Other cross-checks

- RE+ notes say the masks are Factory 9, Station 4, Steel 4, Aquarius 2: decoded the same. Factory's
  42 ID-8, 25 ID-4, 4 ID-1 and 4 ID-2 ledges match the RE+ description of its explosion and falling
  container; Station's IDs 1, 2 and 4 (11, 4, 19 GEs) match its train and car park switches.
- RE+ notes the stock maximum is OrphD with 864 GEs (858 LG): decoded the same.
- MapsEd: 75 of the 83 maps have an editor copy; all 75 give the same kinds, IDs and points.
- 25 maps store a GEActivationMask; the rest use the LevelInfo class default 0 (P1 decoder).
- 247 GEs on all maps are absent at round start (an ID that shares no bit with the mask).
- POLE is never used by a shipped map.

## Census (counts per kind per map)

"IDs != 0" counts GEs that an SGETrigger can switch; "Mask" is LevelInfo.GEActivationMask, "(default)"
when the map does not store it.

| Map | GEs | POLE | FENCE | HOH | LG | LADDER | PIPE | ZL | IDs != 0 | Mask |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| AquaD | 407 | 0 | 0 | 2 | 402 | 1 | 2 | 0 | 8 | 2 |
| AquaE | 409 | 0 | 0 | 2 | 403 | 1 | 3 | 0 | 8 | 2 |
| AquaT | 413 | 0 | 0 | 2 | 407 | 1 | 3 | 0 | 8 | 2 |
| BankD | 752 | 0 | 0 | 6 | 721 | 2 | 23 | 0 | 0 | 0 (default) |
| BankP | 754 | 0 | 0 | 6 | 722 | 2 | 24 | 0 | 0 | 0 (default) |
| CineE | 814 | 0 | 0 | 7 | 794 | 1 | 12 | 0 | 0 | 0 (default) |
| CineS | 836 | 0 | 0 | 7 | 816 | 1 | 12 | 0 | 0 | 0 (default) |
| CineU | 840 | 0 | 0 | 7 | 818 | 3 | 12 | 0 | 0 | 0 (default) |
| ClarD | 555 | 0 | 4 | 42 | 441 | 10 | 52 | 6 | 0 | 0 (default) |
| ClubD | 559 | 0 | 0 | 14 | 506 | 1 | 38 | 0 | 0 | 5 |
| ClubE | 561 | 0 | 0 | 14 | 507 | 1 | 39 | 0 | 0 | 5 |
| ClubS | 559 | 0 | 0 | 14 | 506 | 1 | 38 | 0 | 0 | 5 |
| ClubT | 563 | 0 | 0 | 14 | 509 | 1 | 39 | 0 | 0 | 5 |
| CpusS | 505 | 0 | 2 | 2 | 483 | 11 | 5 | 2 | 0 | 0 (default) |
| DeftD | 572 | 0 | 10 | 10 | 521 | 11 | 15 | 5 | 0 | 0 (default) |
| DeftS | 587 | 0 | 10 | 10 | 536 | 11 | 15 | 5 | 0 | 0 (default) |
| Entry | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 (default) |
| FactD | 654 | 0 | 0 | 0 | 620 | 22 | 10 | 2 | 75 | 9 |
| FactE | 654 | 0 | 0 | 0 | 620 | 22 | 10 | 2 | 75 | 9 |
| FactP | 672 | 0 | 0 | 0 | 638 | 21 | 11 | 2 | 75 | 9 |
| FactT | 654 | 0 | 0 | 0 | 620 | 22 | 10 | 2 | 75 | 9 |
| GhosS | 325 | 0 | 0 | 0 | 322 | 0 | 2 | 1 | 0 | 0 (default) |
| KrauE | 506 | 0 | 3 | 4 | 466 | 11 | 20 | 2 | 0 | 0 (default) |
| KrauU | 575 | 0 | 3 | 4 | 533 | 11 | 21 | 3 | 0 | 0 (default) |
| Lobby | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 (default) |
| MissD | 538 | 0 | 0 | 8 | 500 | 15 | 14 | 1 | 13 | 194 |
| MissE | 538 | 0 | 0 | 8 | 500 | 15 | 14 | 1 | 13 | 194 |
| MissP | 538 | 0 | 0 | 8 | 500 | 15 | 14 | 1 | 13 | 194 |
| MounE | 565 | 0 | 11 | 13 | 492 | 14 | 32 | 3 | 0 | 0 (default) |
| MounS | 566 | 0 | 11 | 13 | 493 | 14 | 32 | 3 | 0 | 0 (default) |
| MounU | 579 | 0 | 11 | 15 | 497 | 14 | 39 | 3 | 0 | 0 (default) |
| MuseD | 382 | 0 | 0 | 19 | 341 | 1 | 17 | 4 | 0 | 0 (default) |
| MuseE | 382 | 0 | 0 | 19 | 341 | 1 | 17 | 4 | 0 | 0 (default) |
| MuseT | 383 | 0 | 0 | 19 | 342 | 1 | 17 | 4 | 0 | 0 (default) |
| OffsD | 709 | 0 | 4 | 3 | 680 | 2 | 19 | 1 | 3 | 0 (default) |
| OrphD | 864 | 0 | 0 | 0 | 858 | 0 | 4 | 2 | 12 | 0 (default) |
| OrphE | 862 | 0 | 0 | 0 | 856 | 0 | 4 | 2 | 13 | 0 (default) |
| OrphS | 830 | 0 | 0 | 0 | 822 | 0 | 6 | 2 | 1 | 0 (default) |
| OrphT | 882 | 0 | 0 | 0 | 876 | 0 | 4 | 2 | 12 | 0 (default) |
| PanaD | 419 | 0 | 0 | 0 | 416 | 2 | 1 | 0 | 0 | 0 (default) |
| PanaS | 426 | 0 | 0 | 0 | 420 | 2 | 2 | 2 | 0 | 0 (default) |
| PolaD | 349 | 0 | 4 | 1 | 333 | 5 | 4 | 2 | 0 | 1 |
| PolaE | 349 | 0 | 4 | 1 | 333 | 5 | 4 | 2 | 0 | 1 |
| PolaP | 359 | 0 | 4 | 1 | 343 | 5 | 4 | 2 | 0 | 1 |
| PolaT | 347 | 0 | 4 | 1 | 331 | 5 | 4 | 2 | 0 | 1 |
| PrisD | 569 | 0 | 0 | 5 | 555 | 2 | 5 | 2 | 0 | 0 (default) |
| PyroD | 1329 | 0 | 0 | 4 | 1262 | 5 | 58 | 0 | 0 | 0 (default) |
| RiveD | 446 | 0 | 0 | 7 | 423 | 4 | 12 | 0 | 0 | 0 (default) |
| RiveE | 446 | 0 | 0 | 7 | 423 | 4 | 12 | 0 | 0 | 0 (default) |
| RiveP | 438 | 0 | 0 | 7 | 415 | 4 | 12 | 0 | 0 | 0 (default) |
| SancD | 910 | 0 | 12 | 4 | 879 | 7 | 5 | 3 | 18 | 0 (default) |
| SancE | 587 | 0 | 2 | 0 | 584 | 1 | 0 | 0 | 0 | 0 (default) |
| ScheE | 293 | 0 | 0 | 5 | 242 | 6 | 34 | 6 | 0 | 0 (default) |
| ScheU | 345 | 0 | 0 | 5 | 291 | 6 | 37 | 6 | 0 | 0 (default) |
| ShadD | 242 | 0 | 0 | 0 | 235 | 1 | 6 | 0 | 0 | 0 (default) |
| ShadP | 318 | 0 | 0 | 0 | 311 | 1 | 6 | 0 | 0 | 0 (default) |
| SkysD | 633 | 0 | 0 | 2 | 627 | 0 | 2 | 2 | 0 | 0 (default) |
| SkysP | 642 | 0 | 0 | 2 | 637 | 0 | 2 | 1 | 0 | 0 (default) |
| SlauS | 571 | 0 | 0 | 0 | 571 | 0 | 0 | 0 | 0 | 0 (default) |
| StatD | 784 | 0 | 10 | 6 | 716 | 2 | 45 | 5 | 34 | 4 |
| StatE | 784 | 0 | 10 | 6 | 716 | 2 | 45 | 5 | 34 | 4 |
| StatS | 778 | 0 | 4 | 2 | 735 | 4 | 30 | 3 | 34 | 4 |
| SteeD | 224 | 0 | 0 | 0 | 216 | 0 | 6 | 2 | 1 | 4 |
| SteeE | 228 | 0 | 0 | 0 | 220 | 0 | 6 | 2 | 1 | 4 |
| SteeP | 224 | 0 | 0 | 0 | 216 | 0 | 6 | 2 | 1 | 4 |
| SteeT | 234 | 0 | 0 | 0 | 226 | 0 | 6 | 2 | 1 | 4 |
| StroD | 315 | 0 | 0 | 0 | 309 | 2 | 2 | 2 | 0 | 0 (default) |
| TermD | 700 | 0 | 0 | 3 | 675 | 13 | 8 | 1 | 0 | 0 (default) |
| TME30 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 (default) |
| TME32 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 (default) |
| TME33 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 (default) |
| TSP20 | 40 | 0 | 0 | 1 | 34 | 0 | 4 | 1 | 0 | 0 (default) |
| TSP21 | 8 | 0 | 0 | 0 | 8 | 0 | 0 | 0 | 0 | 0 (default) |
| TSP22 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 (default) |
| TSP23 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 (default) |
| TSP29 | 73 | 0 | 0 | 2 | 68 | 0 | 1 | 2 | 0 | 0 (default) |
| VertE | 994 | 0 | 0 | 43 | 845 | 10 | 87 | 9 | 0 | 0 (default) |
| VertU | 1039 | 0 | 0 | 43 | 882 | 10 | 95 | 9 | 0 | 0 (default) |
| VertX | 1025 | 0 | 0 | 43 | 871 | 10 | 92 | 9 | 0 | 0 (default) |
| WareD | 736 | 0 | 6 | 24 | 674 | 5 | 24 | 3 | 0 | 0 (default) |
| WareP | 711 | 0 | 6 | 24 | 648 | 5 | 25 | 3 | 0 | 0 (default) |
| ZiopD | 630 | 0 | 1 | 6 | 620 | 1 | 2 | 0 | 0 | 0 (default) |
| ZiopS | 628 | 0 | 1 | 6 | 618 | 1 | 2 | 0 | 0 | 0 (default) |
| **all** | 42517 | 0 | 137 | 553 | 39937 | 390 | 1345 | 155 | 528 | |

## Verified facts and assumptions

- Verified: the byte layout above (every Level export of the 83 maps consumed exactly; editor
  serializers read), the kind codes (the saver writes each kind's own kind function, vtable slot
  `+0x1c`, which returns 1, 2, 4, 8, 16, 32 or 64 for the seven vtables RE+ reads in a live editor),
  the axis mapping and child order of the tree, the bounds rule, SteeE truth.
- Verified (from RE+ and the editor code, not re-measured here): the activation rule (`ID == 0` or
  `ID & mask != 0`) and the SGETrigger rule (clear the bits when all are set, else set them).
- Assumption: the game build (`SCCT Versus`) loads the same layout; it is a sibling build of the
  editor from the same day and reads the same cooked maps. A live read of SteeE's GE list from a
  headless Play Level would close this (RE+ `ge.list` gives the same records from memory).
- The `+4` field of a GE in memory is a 1-based index assigned at load; it is not serialized.

## Next steps

- Feed `local/formats/ge/*.json` into the SVMap `sim/traversal.json` step (P4) and the GE queries in
  `OpenScct.Physics`; the search tree can be rebuilt there instead of copied.
- Spec the traversal tests that consume GEs (`CheckArmsZone`, ledge test +0x1A4420) against these
  records.
