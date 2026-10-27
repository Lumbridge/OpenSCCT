# P4: every shipped map converted to SVMap 1.1

**Verdict: GO.** All 83 maps of `Packages/Maps` convert to SVMap 1.1 folders, pass the validator, and convert
byte-identically a second time. The C# reader (`OpenScct.Content`) loads all 83 with sim hashes equal to the pipeline's.
SteeE's known facts all hold, including the crane chain timings.

Date: 2026-10-23. Pipeline: `content/svmbuild` (`maps` P4, `validate` P8, `classes` P1, `chars` P5). Run:

```
cd content
python -B -m svmbuild classes chars maps validate --maps all --jobs 6 --check-stable --report ../.content/reports/all-maps.json
```

Output (gitignored): `.content/svmap/<Map>/` (1.9 GB for all maps), `.content/shared/classes.json`,
`.content/shared/chars/`. Format: `spec/svmap/1.1`.

## Timings and budgets

| Measure | Budget | Result |
|---|---|---|
| SteeE, maps step | < 30 s | 4.4 s (5.3 s wall with interpreter start); open 0.20, gameplay 0.02, collision 0.71, traversal 0.00, render 2.19, write 1.12 |
| all 83 maps, maps + validate with `--check-stable` (each map built twice), 6 jobs | < 20 min with 8 jobs | 149 s wall (353 s of maps step time) |
| no-change rebuild, all maps | < 10 s | 11.2 s wall (168 cache hits; most of it is checking each map's discovered packages and outputs) |
| largest map (PrisD) | | 17.7 s |

## SteeE

Sizes: sim 3.36 MB (`gameplay.json` 0.35 MB, `collision.bin` 2.88 MB, `traversal.json`
0.06 MB, `classes.json` 0.07 MB), render 20.0 MB (`world.glb` 12.3 MB, 180 PNG textures
7.1 MB, `materials.json`, `lighting.json`, `fx.json` 0.6 MB), ui 4 KB.

Actors: 1,536 in `Level.Actors`: 1 LevelInfo, 441 gameplay
actors written to `gameplay.json`, 1084 render-only (static mesh actors, lights,
ambient sounds, anti-portal), 10 editor cameras.

| Class (gameplay.json) | Count |
|---|---:|
| `SBase.SGamePlayObject` | 67 |
| `Engine.BlockingVolume` | 48 |
| `Engine.Volume` | 42 |
| `Engine.ZoneInfo` | 36 |
| `Engine.Emitter` | 31 |
| `SBase.SDisk` | 30 |
| `Engine.Mover` | 24 |
| `SBase.STriggerLight` | 20 |
| `Engine.PlayerStart` | 18 |
| `SBase.SAmbientSoundTrigger` | 15 |
| `SBase.SVitre` | 14 |
| `SBase.SHelpPath` | 13 |
| `SBase.SZoneTips` | 12 |
| `Engine.SoundTrigger` | 9 |
| `Engine.Trigger` | 7 |
| `SBase.SDamageVolume` | 7 |
| `SBase.SExtincteur` | 7 |
| `SBase.SObjective` | 6 |
| `SBase.SComputerObjectiveTrigger` | 5 |
| `SBase.SInterrupteur` | 4 |
| `SBase.SPresenceTrigger` | 4 |
| `SBase.SAmmoStock` | 3 |
| `SBase.SCamNetwork` | 3 |
| `SBase.SInfirmary` | 3 |
| `SBase.SMission` | 3 |
| `SBase.SAnimatedMesh` | 2 |
| `Engine.LevelInfo` | 1 |
| `Engine.MusicTrigger` | 1 |
| `Engine.SkyZoneInfo` | 1 |
| `SBase.SFlag` | 1 |
| `SBase.SFlagDropZone` | 1 |
| `SBase.SGETrigger` | 1 |
| `SBase.SMagicEvent` | 1 |
| `SBase.SShaker` | 1 |
| `SBase.STriggerPlayerStart` | 1 |

Collision: 1,949 colliders, 123,401 triangles, 38,178 vertices,
2,205 BSP nodes, 97 hulls, 54 cylinders, 39 zones,
157 collision materials.

| Collider kind | Count | Triangles |
|---|---:|---:|
| worldBsp | 1 | 4,130 (1,624 nodes; 119 non-solid portal and anti-portal polygons left out, 410 zero-area fan triangles skipped) |
| brush (48 BlockingVolume, 42 Volume, 7 SDamageVolume) | 97 | 1,160 (all convex: 97 hulls) |
| meshPoly / meshHull (static mesh per-polygon / simple sets) | 842 / 790 | 104,668 / 13,443 over every mesh kind |
| gpo (SGamePlayObject and subclasses, every mesh state) | 177 | |
| glass (SVitre) | 14 | |
| mover | 28 | |
| cylinders: collision / interaction | 43 / 11 | |

141 static mesh actors have `bCollideActors` false and get no collider.

### Known facts

| Fact | Expected | Converted |
|---|---|---|
| terminals | 5 | 5 `SComputerObjectiveTrigger` |
| disk | 1 flag, 1 drop zone | 1 `SFlag` (`SFlag28768`), 1 `SFlagDropZone` |
| Story settings | 10 min, 4 / 3 lives, 10 s respawn | `mapSettings.Story`: TimeLimit 10, SpiesLives 4, SentriesLives 3, RespawnTime 10 |
| Story mission | 4 of 6 objectives | `SMission28821`, minimumObjectives 4, 6 objectives (5 terminals, the disk) |
| GEs | 228 (220 LG, 6 PIPE, 2 ZL), mask 4 | 228 (220 LG, 6 PIPE, 2 ZL); `GEActivationMask` 4; GE153 the only one with an ID (4) |
| crane switch | `SInterrupteur28808` fires `blast` | edge `Event` `blast` -> SAnimatedMesh277, SoundTrigger396, SoundTrigger359 |
| crane notifies | `tueletube` ~10.27 s, `blast01` ~10.96 s in an 11.25 s clip | notify edges at 10.2703 s -> Mover2064 and 10.962 s -> the 14 receivers of `steee-chains.md` in order (SGamePlayObject261 ... SGETrigger271); `sqt_grue_seq` 451 frames at 40 fps, (451 - 1) / 40 = 11.25 s |
| wall | `sqt_murkc_avant` swaps to `sqt_murkc_apres` | SGamePlayObject261: colliders for both meshes, `apres` with startsDisabled (variants: TriggeredMesh and DamagedMeshes[0]); accept edge `DestroyOnEventFrom[0]` -> SAnimatedMesh277 |
| crane collision | `sqt_grue_coll01` to `coll02` | SGamePlayObject3357: both meshes, per-polygon (1 triangle) and simple sets (212 / 192 triangles) |
| shaker | senders = the crane | accept edges `ShakeEvents[0..1].EventSender` -> SAnimatedMesh277 |

## All maps

Columns: written actors (LevelInfo plus gameplay actors), actors in `Level.Actors`, colliders, collision
triangles, cylinders, GEs, event edges, sim and render size in MB, maps step seconds (6 jobs in parallel, so
per-map times include contention). Every row is ok: converted, validated, and byte-identical on a second build.

| Map | Status | Actors | Level actors | Colliders | Triangles | Cylinders | GEs | Edges | Sim MB | Render MB | Time s |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| AquaD | ok | 576 | 1,412 | 1,493 | 105,368 | 62 | 407 | 177 | 3.3 | 18.5 | 6.0 |
| AquaE | ok | 576 | 1,413 | 1,492 | 105,429 | 60 | 409 | 176 | 3.3 | 18.6 | 6.1 |
| AquaT | ok | 598 | 1,475 | 1,544 | 109,146 | 60 | 413 | 177 | 3.4 | 19.0 | 6.0 |
| BankD | ok | 241 | 838 | 1,082 | 82,212 | 40 | 752 | 59 | 2.6 | 15.5 | 5.1 |
| BankP | ok | 242 | 838 | 1,088 | 82,521 | 41 | 754 | 59 | 2.6 | 15.5 | 5.1 |
| CineE | ok | 199 | 1,235 | 1,033 | 67,244 | 37 | 814 | 41 | 2.0 | 12.5 | 4.2 |
| CineS | ok | 192 | 1,241 | 1,059 | 67,094 | 49 | 836 | 46 | 2.0 | 12.5 | 2.8 |
| CineU | ok | 192 | 1,260 | 1,095 | 70,442 | 36 | 840 | 52 | 2.1 | 12.1 | 2.7 |
| ClarD | ok | 627 | 5,178 | 5,001 | 307,470 | 71 | 555 | 220 | 8.1 | 56.0 | 12.2 |
| ClubD | ok | 499 | 1,450 | 1,635 | 99,844 | 45 | 559 | 223 | 3.1 | 17.9 | 4.9 |
| ClubE | ok | 500 | 1,452 | 1,638 | 99,964 | 45 | 561 | 223 | 3.1 | 17.9 | 4.9 |
| ClubS | ok | 504 | 1,460 | 1,643 | 100,260 | 46 | 559 | 226 | 3.2 | 17.4 | 4.8 |
| ClubT | ok | 500 | 1,454 | 1,639 | 100,348 | 43 | 563 | 229 | 3.2 | 18.0 | 4.8 |
| CpusS | ok | 254 | 1,385 | 1,468 | 147,293 | 49 | 505 | 58 | 4.2 | 36.5 | 6.9 |
| DeftD | ok | 344 | 1,083 | 1,182 | 94,750 | 99 | 572 | 134 | 2.9 | 14.7 | 3.3 |
| DeftS | ok | 320 | 1,067 | 1,158 | 94,442 | 97 | 587 | 112 | 2.9 | 14.7 | 3.3 |
| Entry | ok | 3 | 14 | 2 | 660 | 0 | 0 | 0 | 0.0 | 2.6 | 0.6 |
| FactD | ok | 502 | 1,139 | 1,152 | 66,023 | 50 | 654 | 215 | 2.5 | 11.8 | 3.4 |
| FactE | ok | 503 | 1,140 | 1,154 | 65,982 | 53 | 654 | 216 | 2.5 | 11.8 | 3.2 |
| FactP | ok | 477 | 1,115 | 1,126 | 64,952 | 48 | 672 | 170 | 2.4 | 11.6 | 3.1 |
| FactT | ok | 497 | 1,134 | 1,140 | 65,112 | 56 | 654 | 209 | 2.4 | 11.8 | 3.1 |
| GhosS | ok | 259 | 956 | 1,162 | 72,934 | 15 | 325 | 14 | 2.1 | 17.4 | 3.9 |
| KrauE | ok | 298 | 881 | 937 | 75,798 | 62 | 506 | 85 | 2.4 | 13.7 | 3.2 |
| KrauU | ok | 340 | 937 | 1,012 | 79,748 | 61 | 575 | 99 | 2.5 | 14.2 | 3.4 |
| Lobby | ok | 16 | 27 | 2 | 660 | 0 | 0 | 0 | 0.0 | 2.6 | 0.5 |
| MissD | ok | 579 | 1,873 | 2,030 | 107,347 | 102 | 538 | 198 | 3.5 | 19.2 | 4.5 |
| MissE | ok | 579 | 1,873 | 2,030 | 107,427 | 102 | 538 | 198 | 3.5 | 19.1 | 4.5 |
| MissP | ok | 562 | 1,855 | 2,040 | 107,889 | 105 | 538 | 198 | 3.5 | 19.1 | 4.4 |
| MounE | ok | 251 | 864 | 723 | 43,555 | 24 | 565 | 67 | 1.6 | 14.2 | 3.1 |
| MounS | ok | 249 | 855 | 730 | 42,742 | 46 | 566 | 72 | 1.6 | 14.0 | 3.0 |
| MounU | ok | 227 | 842 | 734 | 46,009 | 24 | 579 | 66 | 1.7 | 14.1 | 3.0 |
| MuseD | ok | 280 | 908 | 841 | 40,412 | 54 | 382 | 48 | 1.6 | 10.2 | 2.9 |
| MuseE | ok | 294 | 922 | 849 | 40,788 | 62 | 382 | 60 | 1.6 | 10.3 | 2.9 |
| MuseT | ok | 281 | 954 | 874 | 47,888 | 57 | 383 | 45 | 1.8 | 12.8 | 3.3 |
| OffsD | ok | 360 | 1,116 | 1,170 | 121,863 | 48 | 709 | 144 | 3.8 | 76.9 | 5.9 |
| OrphD | ok | 645 | 1,930 | 2,304 | 132,257 | 74 | 864 | 211 | 4.0 | 17.2 | 4.1 |
| OrphE | ok | 634 | 1,918 | 2,288 | 131,298 | 74 | 862 | 209 | 4.0 | 17.2 | 4.1 |
| OrphS | ok | 327 | 972 | 1,197 | 70,894 | 41 | 830 | 96 | 2.2 | 11.2 | 2.8 |
| OrphT | ok | 634 | 1,931 | 2,294 | 132,080 | 74 | 882 | 205 | 4.0 | 17.3 | 4.0 |
| PanaD | ok | 232 | 1,029 | 1,316 | 112,323 | 20 | 419 | 46 | 3.2 | 29.1 | 5.8 |
| PanaS | ok | 221 | 1,029 | 1,342 | 116,313 | 21 | 426 | 51 | 3.3 | 29.5 | 6.0 |
| PolaD | ok | 349 | 804 | 814 | 66,034 | 49 | 349 | 57 | 2.3 | 17.0 | 3.5 |
| PolaE | ok | 348 | 805 | 810 | 65,547 | 45 | 349 | 57 | 2.2 | 17.0 | 3.4 |
| PolaP | ok | 344 | 799 | 810 | 65,455 | 45 | 359 | 57 | 2.2 | 16.9 | 3.4 |
| PolaT | ok | 329 | 784 | 783 | 65,504 | 42 | 347 | 38 | 2.2 | 17.2 | 3.3 |
| PrisD | ok | 512 | 3,322 | 2,965 | 433,363 | 60 | 569 | 160 | 10.1 | 122.0 | 17.7 |
| PyroD | ok | 448 | 1,228 | 1,118 | 101,556 | 32 | 1329 | 101 | 3.5 | 37.5 | 6.5 |
| RiveD | ok | 377 | 1,393 | 1,515 | 89,263 | 50 | 446 | 107 | 2.9 | 17.9 | 4.0 |
| RiveE | ok | 380 | 1,396 | 1,516 | 89,117 | 50 | 446 | 113 | 2.9 | 17.9 | 4.0 |
| RiveP | ok | 355 | 1,358 | 1,498 | 88,499 | 46 | 438 | 106 | 2.8 | 17.9 | 4.2 |
| SancD | ok | 361 | 1,574 | 1,697 | 149,300 | 30 | 910 | 58 | 4.2 | 30.0 | 6.0 |
| SancE | ok | 320 | 1,338 | 1,498 | 143,318 | 15 | 587 | 31 | 3.6 | 25.6 | 4.7 |
| ScheE | ok | 171 | 774 | 700 | 42,481 | 24 | 293 | 23 | 1.6 | 23.1 | 3.7 |
| ScheU | ok | 186 | 808 | 728 | 44,258 | 31 | 345 | 31 | 1.6 | 23.2 | 3.8 |
| ShadD | ok | 479 | 1,499 | 1,351 | 201,642 | 27 | 242 | 122 | 4.7 | 29.9 | 5.6 |
| ShadP | ok | 420 | 855 | 800 | 59,293 | 30 | 318 | 46 | 2.3 | 32.8 | 5.1 |
| SkysD | ok | 417 | 1,300 | 1,204 | 80,611 | 84 | 633 | 141 | 2.7 | 35.8 | 7.1 |
| SkysP | ok | 421 | 1,305 | 1,211 | 80,928 | 85 | 642 | 141 | 2.8 | 35.9 | 7.0 |
| SlauS | ok | 178 | 687 | 658 | 52,795 | 25 | 571 | 46 | 1.7 | 14.4 | 3.9 |
| StatD | ok | 392 | 1,003 | 952 | 69,843 | 65 | 784 | 202 | 2.6 | 16.5 | 4.3 |
| StatE | ok | 389 | 1,000 | 948 | 69,712 | 60 | 784 | 193 | 2.6 | 16.5 | 4.2 |
| StatS | ok | 355 | 935 | 904 | 65,880 | 56 | 778 | 191 | 2.4 | 16.1 | 4.1 |
| SteeD | ok | 445 | 1,542 | 1,956 | 124,408 | 57 | 224 | 60 | 3.4 | 20.1 | 4.6 |
| SteeE | ok | 442 | 1,536 | 1,949 | 123,401 | 54 | 228 | 55 | 3.4 | 20.0 | 4.4 |
| SteeP | ok | 445 | 1,542 | 1,956 | 124,398 | 57 | 224 | 60 | 3.4 | 20.1 | 4.4 |
| SteeT | ok | 448 | 1,564 | 1,989 | 128,284 | 54 | 234 | 55 | 3.5 | 20.4 | 4.4 |
| StroD | ok | 313 | 1,415 | 2,168 | 142,894 | 25 | 315 | 74 | 3.9 | 45.5 | 6.2 |
| TermD | ok | 263 | 1,612 | 1,093 | 307,199 | 65 | 700 | 91 | 8.3 | 95.8 | 8.2 |
| TME30 | ok | 80 | 499 | 768 | 80,157 | 32 | 0 | 31 | 1.7 | 14.9 | 1.7 |
| TME32 | ok | 58 | 345 | 518 | 61,275 | 24 | 0 | 11 | 1.3 | 13.4 | 1.4 |
| TME33 | ok | 69 | 622 | 971 | 96,889 | 33 | 0 | 16 | 2.5 | 18.1 | 2.4 |
| TSP20 | ok | 105 | 430 | 478 | 30,878 | 33 | 40 | 64 | 0.9 | 7.5 | 1.5 |
| TSP21 | ok | 40 | 116 | 103 | 12,044 | 14 | 8 | 12 | 0.4 | 3.9 | 1.1 |
| TSP22 | ok | 101 | 227 | 191 | 20,555 | 44 | 0 | 27 | 0.6 | 4.7 | 1.2 |
| TSP23 | ok | 97 | 323 | 388 | 29,151 | 35 | 0 | 46 | 0.9 | 6.6 | 1.3 |
| TSP29 | ok | 83 | 850 | 1,337 | 92,280 | 12 | 73 | 24 | 2.1 | 12.0 | 1.8 |
| VertE | ok | 307 | 1,161 | 1,260 | 73,101 | 75 | 994 | 111 | 2.4 | 17.6 | 3.5 |
| VertU | ok | 275 | 1,136 | 1,339 | 76,032 | 59 | 1039 | 136 | 2.4 | 17.2 | 3.5 |
| VertX | ok | 295 | 1,153 | 1,296 | 72,517 | 76 | 1025 | 117 | 2.4 | 17.7 | 3.6 |
| WareD | ok | 259 | 870 | 1,054 | 91,959 | 50 | 736 | 60 | 2.9 | 15.1 | 3.6 |
| WareP | ok | 241 | 830 | 1,025 | 87,137 | 50 | 711 | 50 | 2.8 | 14.4 | 3.6 |
| ZiopD | ok | 297 | 1,304 | 1,262 | 74,763 | 30 | 630 | 59 | 2.5 | 22.5 | 5.0 |
| ZiopS | ok | 306 | 1,311 | 1,288 | 74,683 | 42 | 628 | 65 | 2.5 | 22.4 | 4.6 |
| **all 83** | 83 ok | 28,114 | 97,810 | 104,568 | 7,675,185 | 4,025 | 42,517 | 8,379 | 232 | 1757 | 353 |

Totals: colliders by kind brush 3,259, glass 544, gpo 9,634, meshHull 37,811, meshPoly 50,331, mover 2,906, worldBsp 83. GEs by kind
FENCE 137, HOH 553, LADDER 390, LG 39,937, PIPE 1,345, ZL 155, the same totals as the C3 census
(`docs/spikes/C3-ge-list.md`, 42,517). Actors: 28,031 gameplay, 68,854 render-only,
842 editor, 83 LevelInfo. Render: 60,339 mesh instances, 12,224
PNG files, 13,301 lights, 2,105 emitters.

## Failures and how they were fixed

The final run has none. Earlier runs of this spike failed on:

1. BankD, AquaD, AquaE (first parallel run): two workers wrote the same content-addressed cache object at once and
   the second rename was refused on Windows. The object store now treats that as already written.
2. CpusS: the material `PCF09_TXT.Extérieur.Facade01` and the texture `beton_abimé3` have non-ASCII names; a
   material key in `materials.json` and a PNG file name broke the ASCII-only canonical writer. Client JSON now
   allows non-ASCII object keys (sorted by UTF-8 bytes) and file names escape other characters as `_uXXXX`.

Conversion notes that remain (counted per map in the report, none fatal):

- 688 references to actors that are exports of the map but not in `Level.Actors` (deleted actors the editor left
  behind; mostly `IgnoredEchelonLights`, `ObjectLights`, `ManualExcludes` entries): written as `null`.
- Entry and Lobby have no `<Map>-i.utc`: empty `mapSettings`, no `ui/mapinfo.json`.

## Cross-checks

- C# reader: a scratch console program (not tracked) ran `SvMap.Load` and `MapLoader.Load` on all 83 folders: every
  map loads with strict collision reading (the three 1.1 chunks skipped as unknown), its computed sim hash equals the
  manifest's, `CheckReferences` finds nothing, and `MapLoader` instantiates every actor (ported classes or markers).
  `CheckDerived` reports `eventEdges` on 80 maps because it implements the 1.0 rule (only `Event`); see open issues.
- `tests/OpenScct.Content.Tests`: 116 passed (TestBox fixture and Python cross-check unchanged).
- Stability: every map was converted twice in one run (`--check-stable`) and SteeE again in a new process; all
  files byte-identical.

## Verified facts and assumptions

- Verified: every actor of every map decodes with the exact class layouts (no unknown property, no size mismatch);
  GE lists equal the C3 decoder's; UModel zone records are 12 bytes (Polys export follows); MeshAnimation notify
  records are `f32, name, objref` (all exports consumed to the end).
- Assumption: render-only classes (`StaticMeshActor`, `Light`, `AmbientSound`, `Projector`, `AntiPortalActor`,
  `ERainVolume`, soft-body cloth) never matter to the simulation beyond their collision, which is kept.
- Assumption: PS2-only actors (`Platform = PLF_PSX2_Only`) do not exist in the PC game (scctpkg's finding on live
  maps); they are left out of sim/ and render/.
- Assumption: a brush's collision transform is the actor transform (`MainScale` / `PostScale` are identity on every
  shipped brush; the converter notes any that is not).
- Not decided here: which static mesh collision set each trace kind uses in this build (both stored, `CSRC.traceUse`
  per the UE2 rule) and the BSP leaf solidity (`LeafHulls` not decoded; `BSPN` provisional).

## Open issues

1. `OpenScct.Content.GameplayDoc.CheckDerived` needs the 1.1 event-edge rule (spec/svmap/1.1/README.md "Event edges") for
   converted maps; the derivation is in `content/svmbuild/svmap.py` (`_derive_event_edges_11`).
2. `MapLoader` reports 79 to 281 unapplied props per map (ports not written yet); `sim/classes.json` now carries
   the class defaults a loader can apply before the instance props.
3. Interaction cylinders use the generic `SInteractObject` size rule; pickups, drop zones and terminals resize at
   run time (`interaction-system.md`).
4. `LeafHulls` and the BSP leaf table are not decoded; `BSPN.collisionBound` is -1.
5. Textures are PNG (mip 0) until the P2 textures step writes DDS mip chains.

## Next steps

- Port `CheckDerived` 1.1 in `OpenScct.Content`; let `MapLoader` apply `sim/classes.json` defaults.
- Collision oracle on SteeE (RE+ live `trace_box`) against `collision.bin`, starting with the crane and the wall.
- P2 textures and P3 meshes as shared packs, so maps reference them instead of embedding PNGs and decoding meshes.
