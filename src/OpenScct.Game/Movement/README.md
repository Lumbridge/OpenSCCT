# Movement (OpenScct.Game.Movement)

Helpers for the pawn and controller movement port: the input sampling native, the pawn action player
(ExecuteAction), the pawn animation channels and their root motion, the camera view windows, plus `PawnRig`, the
factory the differential tests use to play scenarios in the rewrite. The physics modes, GE searches and traversal
natives are OpenScct.Physics' `PhysicsDriver` (docs/PHYSICS.md, "Pawn movement API"); this layer calls it.

The script ports live next to their classes, written from `spec/movement/*.md` and `spec/native/execute-action.md`:
`SBase/SPlayerController.Movement*.cs` (input, rotation, walking helpers, permissions, GE events, zone and
traversal natives), one `SBase/SPlayerController.<state>.cs` per state, `SBase/SBasePawn.Movement*.cs`,
`SBase/SPawnAttaque.Movement*.cs` and `SBase/SPAWNDEFENSE.Movement*.cs`.

## PawnRig (stable API)

`PawnRig` spawns one spy or merc pawn with its `SPlayerController` in a level with physics attached, and steps it
one frame at a time from per-frame input bytes. It is what `IScenarioSim` runners build on.

```csharp
using OpenScct.Game.Movement;

// A converted map (.content/svmap/<Map>), or synthetic collision (and GEs) for tests.
PawnRig rig = PawnRig.ForMap(PawnTeam.Spy, @"C:\...\.content\svmap\SteeE");
PawnRig rig2 = PawnRig.Create(PawnTeam.Merc, collisionData, traversalDoc);   // traversal may be null

// Start state, either the scenario's setup steps...
rig.Teleport(floor: new FVector(-492f, -2032f, -1143.5f), yaw: 16384);   // Z = floor + CollisionHeight + 2
rig.SetCrouched(false);
rig.SetFastSpeed(true);
rig.SetView(pitch: 0, yaw: 16384);
rig.SetHealth(100);
rig.WaitStill(maxFrames: 180);

// ...or the settled frame -1 of a reference run in one call.
rig.Place(location, pawnRotation, controllerRotation, crouched: false);

// Frames: inputs apply before the tick, Sample() reads the state after it (svm.trace/1 field names).
var input = new PlayerInputFrame { BT_Up = 1 };
rig.Step(input);                 // the fixed 60 Hz tick
rig.Step(input, 0.016639f);      // a variable step (DtReplay); time dilation carries the step
PawnSample s = rig.Sample();     // loc, vel, acc, rot, crot, phys, ps, cs, crouch, roll, hp, gs, ch, zmax, jn,
                                 // fwd, str, npm, imove, fast, cwr, pj, anim (channel 0)
```

### Types

| Type | What it is |
|---|---|
| `PawnTeam` | `Spy` (SPawnAttaque_1Mesh) or `Merc` (SPawnDefense_1Mesh) |
| `PawnRigOptions` | `Seed`, `NetMode` (default `NM_Standalone`), `Animations` (an `IPawnAnimationSource`), `CharacterContentDir` (the converted `.content/shared/chars`: clip lengths and root tracks; `ForMap` finds it next to the map), `Hooks` (an `IMovementHooks` for camera, weapon, combat and gadget calls; default: local stand-ins) |
| `PlayerInputFrame` | one frame of input: the controller input bytes `BT_Up`, `BT_Down`, `BT_Left`, `BT_Right`, `bCrouch`, `bRoll`, `bUsing`, `bFireMode`, `bUseBackToWall`; `Jump` (the `Jump` exec this frame); `View` (`FRotator?`, written to the controller rotation this frame only); `FastSpeed` (`bool?`); `MouseX`, `MouseY` (the turn and look axes this frame) |
| `PlayerInputFrame.FromButtons(buttons, jump, view, fastSpeed)` | builds a frame from the scenario names (`"BT_Up"` and so on), the shape of `OpenScct.DiffTest.Model.FrameInput` |
| `PawnSample` | the state after a frame, with the svm.trace/1 frame keys as property names (`Loc`, `Vel`, `Acc`, `Rot`, `CRot`, `Phys`, `PawnState`, `ControllerState`, `Crouch`, `Roll`, `Hp`, `Gs`, `Ch`, `ZMax`, `Jn`, `Fwd`, `Str`, `Npm`, `IMove`, `Fast`, `Cwr`, `Pj`, `Anim0`, `Anim0Frame`) |

### Members

| Member | Effect |
|---|---|
| `PawnRig.Create(team, collision, traversal, options)` | a standalone level over the collision data and GEs, physics attached, the pawn spawned at the origin in its team's start state (spy crouched unarmed, merc standing armed) and possessed |
| `PawnRig.Create(team, map, options)` | the same over a loaded `SvMap`, with the map's actors and the device ports |
| `PawnRig.ForMap(team, svmapDir, options)` | loads `.content/svmap/<Map>` and calls `Create` |
| `World`, `Level`, `Driver`, `Collision`, `Physics`, `Map`, `Pawn`, `Controller` | the parts, for anything the rig does not wrap |
| `Teleport(floor, yaw, clearance = 2)` | scenario `teleport`: the pawn to `floor` with Z raised by its CollisionHeight plus `clearance`, pawn and view yaw set |
| `SetCrouched(b)` | scenario `stance`: presses crouch (a toggle) until `bIsCrouched == b`, at most 120 frames |
| `SetFastSpeed(b)` | scenario `fastSpeed`: writes `bFastSpeed` |
| `SetView(pitch, yaw)` | scenario `view`: writes the controller rotation |
| `SetHealth(n)` | scenario `write` of `Health` |
| `WaitStill(maxFrames)` | scenario `waitStill`: steps until the pawn walks, still, in a resting state, for 10 frames; false on timeout |
| `Place(location, rotation, controllerRotation, crouched)` | a settled start directly: location, rotations, stance and the resting state for that stance (no frames run) |
| `Step(input)`, `Step(input, dt)` | applies the input, then runs one level tick |
| `Sample()` | reads a `PawnSample` |
| `Frame` | frames stepped since creation |

The rig never writes the pawn's Physics: the stance and the start state go through the same actions the game plays.
A fresh level's clock starts at 0, so the rig sets `fTimeWhenHit` long ago (no adrenaline at spawn); the first
stance change still stamps `m_LastSpecialMoveTime`, which delays the zip line grab by 2 s as in the game.

## The pieces

| File | Port |
|---|---|
| `ControlInput.cs` | `NativeComputeControlVariables`: buttons to `aForward`, `aStrafe`, `iMove`, `fNormePadMove`, the mouse to `fTurnValue`, `fLookUpValue` (measured mapping, see the file) |
| `PawnActions.cs` | `SBasePawn.ExecuteAction` for walking, stance, draw, jump, fall, landing and roll |
| `TraversalActions.cs` | `ExecuteAction` for the ledge, vertical pipe, zip line, repositioning and back-to-wall actions (physics mode, root-motion end, clip), and `AnimateBTW` |
| `PawnAnimation.cs` | the pawn's animation channels (25) with the engine's channel natives (PlayAnim, blend params, rate scales, sequence swaps and copies) and the per-tick update (passes, notifies, AnimEnd for one-shots and loops, the root-motion hold); the clip source (built-in table or the converted packages, with notifies and root turns). The pawn's own tick advances them with the current dt before physics, and runs the walk blender (`OpenScct.Game.Animation.PawnMovementAnimation`) and OldPhysics = Physics after it. See docs/ANIMATION.md |
| `PawnRootMotion.cs` | the root-motion mesh for PHYS_RootMotion: lock (start of the move), root deltas from channel 0. EndRootMotion runs at the end of a channel-0 clip in root motion (`SBasePawn.Movement.cs`) |
| `CameraWindows.cs` | the yaw and pitch windows of the camera modes, for the limited-view states |
| `MovementNatives.cs` | Trace, FitsAt, SetCollisionSize, FarMoveActor, setPhysics over the driver |
| `IMovementHooks.cs` | calls into other areas (camera, weapon clips, laser, use and search, drop attack) and `MovementService`, the level service that turns the movement layer on |
| `MovementNet.cs` | the movement's server calls (ServerGetZipP and kin, replicated actions, moves); local: applied in place |
| `MovementStartState.cs` | the start state at possession, and the game-flow ports wrapper the rig installs |
| `MovementStateNames.cs` | the state and label names the movement changes to |
| `MovementK.cs`, `MovementMath.cs` | constants with provenance; truncating rotator math |
| `PawnRig.cs` | the rig |

## States

Walking: `s_PlayerWalking`, `s_PlayerWalkingMario` (spy), `s_PlayerWalkingWeapon` (armed, merc), the four resting
states `s_PlayerWalkingSt0H`, `Cr0H`, `St2H`, `Cr2H`, the stance and hands transitions, `s_PlayerHit`.
Air: `s_PlayerJump`, `PlayerFalling`, `PlayerFallToBackGrab`, `PlayerFallingFromRoll`, `PlayerLanding`,
`PlayerLandingSilent`, `s_PlayerFalling_OutOfWorld`. Roll: `s_PlayerRolling`, `s_PlayerRollingUp`.
Ledge: `GrabLedge`, `OnLedge`, `s_ChangeLedgeWait`, `TopLedge`, `TopLedgeCrouch`, `TopLedgeBarrier`,
`s_Tr_Barrier_Above_Ledge`, `s_TrPipe2Ledge`. Vertical pipe: `s_GrabPipe`, `s_ChangePipeV`, `s_OnPipe`, `s_SpeedPipe`,
`s_TopPipe`, `s_TopPipeCr`, `s_WaitAtTopPipe`, `s_downTopPipe`. Zip line: `s_GrabZip`, `s_ZipLine`, `s_ZipLineSpeedTR`,
`s_ZipLineSpeed`, `s_ZipLineSlowTR`, `s_ZipLineSlow`, `s_ZipLineTrToNormal`, `s_ZipWall`. Back to wall: `s_BTW_Base`,
`s_EnterBTW`, `s_BTW`, `s_BTW_up`, `s_BTW_down`, `s_BTWCorner`, `s_SortieBTW`, the four peek states.

Moveset (spec/movement/ladder.md, pipe-horizontal.md, fence.md, wall-jump-split-vandamme.md,
barrier-and-low-ledges.md, coop.md; actions in `MovesetActions.cs`, natives in OpenScct.Physics `PhysicsDriver.Traversal`,
`.WallJump`, `.Coop`): ladders for both teams (`s_GrabLadder`, `s_OnLadder`, `s_LadderToTopTr`, `s_LadderToTop`,
`s_OnLadderTop`, `s_LadderTop`, `s_WaitAtTopLadder`, `s_downTopLadder`, `s_Back_LadderTop`, `s_LadderGrabFall`,
`s_EndLadderDrawWeapon`, side changes, slide), hand over hand (`s_GrabPipH`, `s_OnPipeH` and its stances), fences
(`GrabFence`, `OnFence`, `OnTopFence`), wall jump (`s_FirstWallJumpPrepareFromJump`, `s_FirstWallJump`,
`s_WallJumpSalto`), split jump and Vandamme (`s_PreSplitJump`, `s_SplitJump`, `VandammeBegin`, `Vandamme`, the
weapon states), barrier and low ledges (`s_BarrierJumpDetect`, `s_BarrierJump`, feet and waist ledges), the co-op
short ladder and hoist (helper and climber states), the merc charge and the drop attack trigger. The movement clips'
script notifies are registered by `MovementNotifies` (the level's AnimNotifyHandlers).

Not ported yet: the back-to-wall fire states and the grab of a merc from the hang.
