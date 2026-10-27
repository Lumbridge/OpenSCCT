# Native physics and collision specs (index)

Behaviour of the engine's movement and collision code, decoded from the editor twin (ChaosTheory_Editor.exe,
base 0x10E00000) and located in the game (SCCT Versus, base 0x10900000). These replace the estimated algorithms in
OpenScct.Physics. Script-side movement is in spec/movement; this folder is the native half of native-contracts.md.

| File | Covers |
|---|---|
| [physics-dispatch.md](physics-dispatch.md) | performPhysics, startNewPhysics (the Physics switch), setPhysics |
| [physics-walking.md](physics-walking.md) | physWalking, MycalcVelocity, stepUp, CheckForLedges, physWalkingSimulated |
| [physics-falling.md](physics-falling.md) | physFalling, air control, gravity, caps, drop-attack homing, bCanLand, processLanded, fZMaxFalling |
| [physics-rootmotion-repositioning.md](physics-rootmotion-repositioning.md) | physRootMotion (additional physics), physFallingInRootMotion, EndRootMotion, bServerTestRootMotion, physRepositionning, EndPhysRepositionning, physVandamme |
| [physics-traversal.md](physics-traversal.md) | physOnPipe, TestTopPipe, physOnPipeH, physOnLadder, physLinear, SetZipSpeed, UnZipSpeed, physOnVerticalPlane, physLedge |
| [physics-ge-queries.md](physics-ge-queries.md) | GE elements and partition query, FindArmsGE, CheckArmsZone, CheckFeetZone, CheckBackFeetZone, the per-kind arm and feet tests, TestTopLedge, bTestTopLadder, CheckFence, CheckBarrier, CheckWallJump, CheckGEForWallJump |
| [collision-traces.md](collision-traces.md) | trace flags, FCheckResult, SingleLineCheck, MultiLineCheck, SinglePointCheck, MultiPointCheck, actor hash queries, ShouldTrace |
| [collision-primitives.md](collision-primitives.md) | cylinder, BSP and static mesh LineCheck and PointCheck; hull versus per-poly selection |
| [collision-movement.md](collision-movement.md) | MoveActor, FarMoveActor, CheckEncroachment, crush |
| [ANSWERS-physics.md](ANSWERS-physics.md) | answers to open questions of spec/movement and the C2 spike |

Unit ids: native UFunctions keep their `nat:` ids; engine functions without a script binding use
`eng:<CppClass>::<Function>` (not yet in re/inventory/inventory.json).

Evidence tooling (tracked, our code): re/ghidra/scripts/physics/ (query server, type builder from the reflection
dumps, signature and naming helpers, editor-to-game matchers). Decompiled output stays in local/re-out/c/physics/.
Names and addresses: re/ghidra/annotations/physics.json.

Common notation: R and H are the pawn's CollisionRadius and CollisionHeight; dt the physics delta time; ToWorld the
actor's local-to-world transform; boxes are given as half extents; "clear" means the trace hit nothing (Time 1).
