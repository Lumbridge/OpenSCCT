# Answers from the fidelity pass (sim against the real-game goldens)

Rules confirmed in the native code while closing the findings of docs/spikes/R4-sim-vs-game.md (F1 to F12), and the
spec errors they exposed. Each section gives the rule in our own words, the evidence (editor and game addresses,
golden scenarios, the editor's live collision queries) and the spec that needs the change. Spec owners fold these
in; the port already follows them.

## Spec corrections

| # | Spec | Says | Should say | Evidence |
|---|---|---|---|---|
| 1 | movement/native-contracts.md, MyDoJump | inferred: JumpZ from any mode | the decoded per-mode table in F2 below | editor 0x111f9cf0, game 0x10adff00 |
| 2 | native/anim.md rule 1.7 | TweenTime 0: frame 0, 1e-4 for a non-looping clip | the other way round: a one-shot starts at exactly 0 (and is nudged to 1e-4 by its first advance, which uses up that pass), a loop starts at 1e-4 | PlayAnim 0x110ff8f0, UpdateAnimation 0x111056c0; goldens crawl-vent, roll |
| 3 | native/collision-primitives.md, UStaticMesh::LineCheck rule 3 and PointCheck | non-zero extent uses "the box variant of the same walk"; PointCheck is a cylinder check of the extent | the box variant sweeps a vertical cylinder on the axes in F1; PointCheck works in the mesh's frame with the local bounds of the box and culls by node boxes (F1); BSP keeps the box | 0x111b5d80, 0x111b4bc0, 0x111b6520, 0x111b6680; editor trace.box |
| 4 | native/physics-walking.md rule 3.1 | wall pre-probe extent Z is H + 18 | H - 18 (the box is lifted 18 and spans from 36 above the feet to the head) | editor 0x112025e0 |
| 5 | native/execute-action.md, pipe hold actions (145, 151, 152, 248) | the hold set, clips and the bIsLeftPart flags | they also set bIsStopped, bCanStop and bBeginSpecial12Anim, fOldFrame 0 and Velocity 0 (see the climb step below) | ExecuteAction cases 145, 151, 152, 248 |
| 6 | the movement port's tick order (src/OpenScct.Game/Movement) | the controller advanced the pawn's animation with the previous frame's dt | the pawn's own tick advances it first, with the current dt (F3, F4, F10 below) | ASBasePawn::Tick 0x11205fb0 |
| 7 | native/physics-traversal.md, hold modes | every mode ends with Velocity = (Location - start) / dt, start not placed | the displacement is measured from after the hold move in physLinear, physOnPipe, physOnPipeH and physOnLadder, and from before it in physLedge and physOnVerticalPlane | 0x11208af0 and the other mode bodies |

native/input.md rule 3 (iMove) and native/natives2-traversal.md rules 1 to 3 (CheckBTW, CheckBTWEyes,
CheckBTWCorner) are right as written: ported from them, the back-to-wall peek matches the golden exactly.

## F1. Static mesh sweeps

**Box sweep (UStaticMesh::LineCheck with an extent).** The mover is a vertical cylinder with radius Extent.X and half
height Extent.Z, in world space, swept against each collision triangle on separating axes:

1. The triangle's bounds on X, Y and Z, on the side of the start.
2. The triangle's normal, both ways.
3. For each edge that is not vertical (horizontal length squared above 0.001): edge x Z, edge x X and Y x edge,
   each flipped to point away from the third corner.
4. A horizontal axis from a corner to the start, when it points outside both edges at that corner (by 0.001).

The cylinder's reach along a unit axis n is H when |n.Z| >= 0.99999, else |n.Z| H + R |n.XY|. The hit is the
latest entry time over the axes; a start that already overlaps is not blocked. A box's corner can therefore pass
a mesh corner that the cylinder clears; BSP still takes the box. Evidence: editor 0x111b5d80 (triangle sweep),
0x111b5ae0, 0x111b4bc0 (reach), 0x111b7590, 0x111b72b0; the editor's live trace.box on the fall-ledge move hits
the sand mesh under the bricks, not the bricks, as the cylinder rule predicts.

**Point check (UStaticMesh::PointCheck, FCylinderPointCheck editor 0x111b6520).** It runs in the mesh's frame: the
point is taken in, and the world box becomes the axis-aligned bounds of the rotated box there; its X and Z half
sizes are the radius and half height. The walk (0x111b6680) drops the point when it lies outside a node's box grown
by those bounds on each axis (node boxes are 5-bit fractions of the parent box, 0x111722d0), then a triangle
overlaps when the point is within the reach of its plane and no edge or corner axis (0x111b4d60) separates them.
The triangle test has no axis along the mesh's X, Y or Z: only the node box covers those. The port uses the
triangle's own bounds for the node box (stricter by up to one node cell).

**Ranking.** ULevel::MultiLineCheck sorts hits by the pulled-back time, not the raw time, so a mesh at the same
height as BSP wins the floor.

## F2. MyDoJump

Runs only where the pawn is controlled locally (a player controller that is not a remote client on the server).
By mode:

| Mode | Result |
|---|---|
| fence, pipe or ladder, or sAction SA_LEDGEFALL (119) in any mode | Velocity = -200 x facing + 50 up, then falling |
| zip line (Linear) | falling, velocity kept |
| falling with nJumpNumber 1 | Velocity = 250 x vWallJumpDir with Z 700, nJumpNumber 2 (the second jump, once) |
| Vandamme | Velocity.Z 0, nJumpNumber 0, falling |
| walking | Velocity.Z = JumpZ, plus the base's Velocity.Z when the base is not world geometry; nJumpNumber 1, falling |
| ledge and every other mode | nothing |

So a drop from a hang never jumps: the hang's crouch or back key goes through SA_LEDGEFALL. Evidence: editor
0x111f9cf0, game 0x10adff00; goldens ledge-grab, ledge-drop, ledge-shimmy, walkoff-ledge-catch.

## F3, F4, F10. Animation in the tick

1. The controller ticks first: the pawn's tick check (0x11182490) defers a pawn until its owner and its controller
   have ticked.
2. ASBasePawn::Tick (0x11205fb0): UpdateAnimation with the current dt, then the authoritative tick (script, state,
   timers, physics), then the post-tick (0x11205ef0): UpdateMovementAnimation when bPhysicsAnimUpdate, then
   OldPhysics = Physics.
3. So a clip started by the controller or by a physics event shows no play on its first frame, and a root-motion
   clip that reaches AnimLast clamps there and ends on the next tick, after that tick's physics (the roll's last
   GroundSpeed applies for one more frame).
4. AnimateWalking (0x111fd360) picks the walk blend's tween: 0.25; 0 when OldPhysics is root motion or falling; 0.1
   when channel 0 plays a forward jump clip; 0.3 when OldBTW is set (OldBTW is then cleared). It also moves
   LastZipP far away. The resting stance actions (SA_WALK_ST and SA_WALK_CR, unarmed and armed) call it; the stance
   transitions (SA_WAIT_ST2CR and SA_WAIT_CR2ST, unarmed and armed, cases 81 to 84) call it, then play their
   one-shot with no tween.

Evidence: the decompiles above; goldens crawl-vent (stand-up starts at frame 0 with no tween), roll, ledge
pull-up (OnLedge on the golden's frame), ledge-drop (LastZipP reset when walking resumes).

## physOnPipe bCanStop: the climb step

physOnPipe never touches bCanStop. The walk blender's climb step (editor 0x11200140, called from
UpdateMovementAnimation for pipe and ladder physics while bBeginSpecial12Anim is set) does:

1. Channel 5 plays ForwardAnim (channel 6 mirrors it).
2. Velocity.Z 0: the channel stops, fOldFrame -1, bIsStopped and bCanStop set.
3. The first moving tick after a stop restarts the clip at rate x dt, plus 0.5 when bIsLeftPart.
4. bCanStop is set when the frame crosses 0.45, passes 0.95 or wraps below 0.5; bIsLeftPart is set on entering
   0.25 to 0.75 and cleared on leaving it.

The pipe hold actions (SA_ON_PIPE 145, SA_ON_PIPE2 151, SA_ON_PIPE2L 152, SA_AFTER_CHANGE_PIPEV 248) set
bIsStopped, bCanStop and bBeginSpecial12Anim, fOldFrame 0 and Velocity 0; 145 uses no tween when OldPhysics is
root motion. The script's SendZipPToServer and SendZipPAndNormalToServer set NewZipP to ZipP on the controlling
side; without that physOnPipe takes the grab for a pipe change and zeroes m_geoNormal. Evidence: the decompiles;
golden pipe-climb-down (bCanStop pulses at channel 5 frames 0.455 and 0.954).

## Hold modes: where the velocity starts

physLinear, physOnPipe, physOnPipeH and physOnLadder take the velocity's start location after the hold move;
physLedge and physOnVerticalPlane before it. physLinear faces ZipP1 - ZipP2 flattened and keeps pitch and roll.
physFalling's floor branch stores the remaining time before the bCanLand test, so a floor hit that does not land
continues the loop with it. Evidence: 0x11208af0 and the other mode bodies, physFalling editor 0x11213090, game
0x10ad3dc0; goldens zip-line-13, pipe-climb-up.

## Open

**F12. Velocity after the narrow-top side push (zip-line-29, frame 330).** The falling spy meets a 4 uu window
sill; the sim and the editor's live queries agree on the contact (the sill mesh, 0.44 uu) and on the side probe's
push of 0.1 R (3.5 uu sideways). After the push frame every golden run's velocity is the average of the velocity
before the update (half-step gravity, or the floor-hit rewrite) and the displacement velocity the decoded
physFalling writes, in all three components (seen in all three runs, on frame 330 or 328). Neither physFalling nor
ASBasePawn::performPhysics averages anything, so the averaging comes from code not yet decoded (a second update
of Velocity after physics in the same tick is the likely place). The sim writes the displacement velocity, so the
sideways and vertical speeds differ for a few frames (location RMS 25.97 against 7.15, end error 0.61).

**BSP windings.** Zero-extent line traces miss the converted lane floor around (-492, -2032): the sim's BSP keeps
front faces only and some converted floor polygons are wound the other way. Box sweeps are unaffected. To check in
the converter or the BSP trace.
